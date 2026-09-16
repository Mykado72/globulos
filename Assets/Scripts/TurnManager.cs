using Fusion;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

/// ✅ CLIENT/SERVER MODE
/// Seul le serveur (State Authority) gère:
/// - La logique de tour par tour
/// - Les changements d'état
/// - La détection de fin de partie
/// 
/// Les clients reçoivent simplement les mises à jour [Networked]
/// et affichent l'état du jeu.
public partial class TurnManager : NetworkBehaviour
{
    public enum TurnState
    {
        RealTime,
        Aiming,
        Resolution,
        CheckResult,
        Finished
    }

    [Header("Game Mode Configuration")]
    [SerializeField] private bool defaultTurnBasedMode = true;

    [Header("Turn-Based Settings")]
    [SerializeField] private float aimDuration = 15f;
    [SerializeField] private float resolutionSettleDuration = 0.2f;

    // ✅ État réseau synchronisé (Serveur → Clients)
    [Networked] public NetworkBool IsTurnBased { get; set; }
    [Networked] public TurnState CurrentState { get; set; }
    [Networked] private TickTimer TurnTimer { get; set; }
    [Networked] private TickTimer ResolutionSettleTimer { get; set; }
    [Networked] public int CurrentTurnNumber { get; set; }
    [Networked] public int WinnerPlayerId { get; set; }

    public static TurnManager Instance { get; private set; }

    public override void Spawned()
    {
        Instance = this;

        // ✅ CLIENT/SERVER : Seul le serveur initialise
        if (HasStateAuthority)
        {
            IsTurnBased = defaultTurnBasedMode;
            WinnerPlayerId = -1;

            if (IsTurnBased)
            {
                StartNewTurn();
            }
            else
            {
                CurrentState = TurnState.RealTime;
                TurnTimer = TickTimer.None;
            }

            Debug.Log("[TurnManager] ✅ Serveur initialisé (State Authority)");
        }
        else
        {
            Debug.Log("[TurnManager] ℹ️ Client : reçoit les mises à jour du serveur");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // ✅ CLIENT/SERVER : Seul le serveur gère la logique
        if (!HasStateAuthority || !IsTurnBased) return;

        switch (CurrentState)
        {
            case TurnState.Aiming:
                if (TurnTimer.Expired(Runner))
                {
                    RPC_ForceStopAiming();
                    ExecuteTurnResolution();
                }
                break;

            case TurnState.Resolution:
                if (!ResolutionSettleTimer.ExpiredOrNotRunning(Runner)) break;

                if (AreAllBallsStopped())
                {
                    CurrentState = TurnState.CheckResult;
                }
                break;

            case TurnState.CheckResult:
                CheckGameEnd();
                if (CurrentState == TurnState.CheckResult)
                {
                    StartNewTurn();
                }
                break;
        }
    }

    public void StartNewTurn()
    {
        CurrentTurnNumber++;
        CurrentState = TurnState.Aiming;
        TurnTimer = TickTimer.CreateFromSeconds(Runner, aimDuration);
    }

    private void ExecuteTurnResolution()
    {
        CurrentState = TurnState.Resolution;
        TurnTimer = TickTimer.None;
        ResolutionSettleTimer = TickTimer.CreateFromSeconds(Runner, resolutionSettleDuration);

        // ✅ Déclenche l'application simultanée des forces préparées sur tous les clients
        RPC_ExecuteAllShots();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ExecuteAllShots()
    {
        Debug.Log("[TurnManager] 💥 Passage en Résolution : Exécution des tirs enregistrés !");
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null)
            {
                ball.ExecuteQueuedShot();
            }
        }
    }

    // ✅ Force l'arrêt de la visée sur tous les clients
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ForceStopAiming()
    {
        Debug.Log("[TurnManager] ⏰ Timer écoulé - Force l'arrêt du visage");
        foreach (var ball in BallAimController.AllBalls)
        {
            ball.ForceStopAiming();
        }
    }

    private bool AreAllBallsStopped()
    {
        // ✅ CLIENT/SERVER : Le serveur simule, donc IsMoving vaut la vérité
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball.IsMoving) return false;
        }
        return true;
    }

    // ✅ RPC : Le ballon de foot signale un but (appelée par un client, traitée par le serveur)
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestWinBySoccerGoal(int winnerId)
    {
        if (!HasStateAuthority) return;
        if (CurrentState == TurnState.Finished) return;

        EndGameWinBySoccerGoal(winnerId);
    }

    public void CheckGameEnd()
    {
        List<BallAimController> allBalls = BallAimController.AllBalls;

        // Décompte des billes vivantes par joueur
        Dictionary<int, int> aliveBallsPerPlayer = new Dictionary<int, int>();
        HashSet<int> allPlayerIds = new HashSet<int>();

        foreach (BallAimController ball in allBalls)
        {
            int playerId = ball.OwnerPlayerId;
            allPlayerIds.Add(playerId);

            if (!aliveBallsPerPlayer.ContainsKey(playerId))
            {
                aliveBallsPerPlayer[playerId] = 0;
            }

            if (!ball.IsDead)
            {
                aliveBallsPerPlayer[playerId]++;
            }
        }

        Debug.Log("[TurnManager] État des billes:");
        foreach (var kvp in aliveBallsPerPlayer)
        {
            Debug.Log($"  PlayerId {kvp.Key}: {kvp.Value} billes vivantes");
        }

        // Vérifier l'état de fin
        int playersWithNoBalls = 0;
        int lastAlivePlayer = -1;

        foreach (int playerId in allPlayerIds)
        {
            if (!aliveBallsPerPlayer.ContainsKey(playerId) || aliveBallsPerPlayer[playerId] == 0)
            {
                playersWithNoBalls++;
            }
            else
            {
                lastAlivePlayer = playerId;
            }
        }

        // Les deux joueurs n'ont plus de billes = égalité
        if (playersWithNoBalls >= 2)
        {
            Debug.Log("[TurnManager] 🤝 ÉGALITÉ!");
            EndGameDraw();
            return;
        }

        // Un seul joueur sans billes = l'autre a gagné
        if (playersWithNoBalls == 1 && lastAlivePlayer >= 0)
        {
            Debug.Log($"[TurnManager] 🎊 VICTOIRE du Joueur {lastAlivePlayer}!");
            EndGameWin(lastAlivePlayer);
            return;
        }
    }

    private void EndGameWin(int winnerId)
    {
        WinnerPlayerId = winnerId;
        IsTurnBased = false;
        CurrentState = TurnState.Finished;

        string winnerName = GetPlayerName(winnerId);
        Debug.Log($"[TurnManager] 🎊 VICTOIRE de {winnerName} !");

        StartCoroutine(ReloadSceneAfterDelay("WIN", winnerName, "looser"));
    }

    // Vérifie si au moins une bille du jeu est en mouvement
    public bool IsAnyBallMoving()
    {
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null && ball.IsMoving) return true;
        }
        return false;
    }
    private void EndGameWinBySoccerGoal(int winnerId)
    {
        WinnerPlayerId = winnerId;
        IsTurnBased = false;
        CurrentState = TurnState.Finished;
        string winnerName = GetPlayerName(winnerId);
        Debug.Log($"[TurnManager] ⚽🎊 BUT ! Gagnant : {winnerName}");
        StartCoroutine(ReloadSceneAfterDelay("WIN", winnerName, "looser"));
    }

    private void EndGameDraw()
    {
        WinnerPlayerId = -1;
        IsTurnBased = false;
        CurrentState = TurnState.Finished;

        StartCoroutine(ReloadSceneAfterDelay("DRAW", "",""));
    }

    private IEnumerator ReloadSceneAfterDelay(string result, string winner, string loser)
    {
        yield return new WaitForSeconds(2f);

        if (result == "WIN")
        {
            Debug.Log($"[TurnManager] 🔄 Reload scene... Gagnant: {winner}");
        }
        else if (result == "DRAW")
        {
            Debug.Log($"[TurnManager] 🔄 Reload scene... Match nul!");
        }

        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        Runner.LoadScene(SceneRef.FromIndex(currentSceneIndex));
    }

    public float GetRemainingTime()
    {
        if (IsTurnBased && TurnTimer.IsRunning)
        {
            return TurnTimer.RemainingTime(Runner) ?? 0f;
        }
        return 0f;
    }

    // ✅ Récupère le pseudo associé à un PlayerId via PlayerData
    public string GetPlayerName(int playerId)
    {
        foreach (var player in FindObjectsOfType<PlayerData>())
        {
            if (player.Object != null && player.Object.InputAuthority.PlayerId == playerId)
            {
                return player.Nickname;
            }
        }
        return $"Joueur {playerId}";
    }
}
