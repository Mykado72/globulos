using Fusion;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

/// ✅ Mode NETWORK (Fusion) : Implémente ITurnManagerCore
/// Logique synchronisée serveur/client via RPC et propriétés [Networked]
public partial class TurnManager : NetworkBehaviour, ITurnManagerCore
{
    [Header("Game Mode Configuration")]
    [SerializeField] private bool defaultTurnBasedMode = true;

    [Header("Turn-Based Settings")]
    [SerializeField] private float aimDuration = 15f;
    [SerializeField] private float resolutionSettleDuration = 0.2f;

    [Tooltip("Durée de l'état Celebrating (voir GoalCelebrationUI) avant de passer en Finished. Doit correspondre à peu près à la durée totale de l'animation de célébration.")]
    [SerializeField] private float celebrationDuration = 2.2f;

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

            Debug.Log("[TurnManager] ✅ Serveur initialisé (Mode NETWORK)");
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

    // ============================================
    // ✅ Implémentation ITurnManagerCore
    // ============================================

    public void StartNewTurn()
    {
        CurrentTurnNumber++;
        CurrentState = TurnState.Aiming;
        TurnTimer = TickTimer.CreateFromSeconds(Runner, aimDuration);

        Debug.Log($"[TurnManager] 🎮 Tour {CurrentTurnNumber} (Mode NETWORK)");
    }

    public float GetRemainingTime()
    {
        if (IsTurnBased && TurnTimer.IsRunning)
        {
            return TurnTimer.RemainingTime(Runner) ?? 0f;
        }
        return 0f;
    }

    public string GetPlayerName(int playerId)
    {
        // ✅ Cherche d'abord dans les PlayerData réseau
        foreach (var player in FindObjectsByType<PlayerData>(FindObjectsSortMode.None))
        {
            if (player.Object != null && player.Object.InputAuthority.PlayerId == playerId)
            {
                return player.Nickname;
            }
        }

        // ✨ Fallback : nom enregistré localement (pour le bot en vs IA réseau)
        if (PlayerNamesManager.Instance != null)
        {
            return PlayerNamesManager.Instance.GetPlayerName(playerId);
        }

        return $"Joueur {playerId}";
    }

    public void RequestWinBySoccerGoal(int winnerId)
    {
        // ✅ RPC : n'importe quel client peut signaler un but, serveur traite
        RPC_RequestWinBySoccerGoal(winnerId);
    }

    public void CheckGameEnd()
    {
        // ✅ Seul le serveur exécute cette vérification
        if (!HasStateAuthority) return;

        List<BallAimController> allBalls = BallAimController.AllBalls;

        Dictionary<int, int> aliveBallsPerPlayer = new Dictionary<int, int>();
        HashSet<int> allPlayerIds = new HashSet<int>();

        foreach (BallAimController ball in allBalls)
        {
            int playerId = ball.OwnerPlayerId; // Correction ici
            allPlayerIds.Add(playerId);

            if (!aliveBallsPerPlayer.ContainsKey(playerId))
                aliveBallsPerPlayer[playerId] = 0;

            if (!ball.IsDead)
                aliveBallsPerPlayer[playerId]++;
        }

        Debug.Log("[TurnManager] État des billes:");
        foreach (var kvp in aliveBallsPerPlayer)
        {
            Debug.Log($"  PlayerId {kvp.Key}: {kvp.Value} billes vivantes");
        }

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

        if (playersWithNoBalls >= 2)
        {
            Debug.Log("[TurnManager] 🤝 ÉGALITÉ!");
            EndGameDraw();
            return;
        }

        if (playersWithNoBalls == 1 && lastAlivePlayer >= 0)
        {
            Debug.Log($"[TurnManager] 🎊 VICTOIRE du Joueur {lastAlivePlayer}!");
            EndGameWin(lastAlivePlayer);
            return;
        }
    }

    public bool IsAnyBallMoving()
    {
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null && ball.IsMoving) return true;
        }
        return false;
    }

    public void ForceStopAiming()
    {
        // ✅ RPC : tous les clients reçoivent l'ordre d'arrêt
        RPC_ForceStopAiming();
    }

    // ============================================
    // ✅ Logique interne + RPC
    // ============================================

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

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ForceStopAiming()
    {
        Debug.Log("[TurnManager] ⏰ Timer écoulé - Force l'arrêt du visage");
        foreach (var ball in BallAimController.AllBalls)
        {
            ball.ForceStopAiming();
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestWinBySoccerGoal(int winnerId)
    {
        if (!HasStateAuthority) return;
        if (CurrentState == TurnState.Finished || CurrentState == TurnState.Celebrating) return;

        // ✅ Geler le timer affiché : TurnTimer.RemainingTime continuerait sinon de décompter
        // en temps réel même hors des cases gérées par FixedUpdateNetwork (contrairement au
        // mode Local où l'absence de case correspondante dans Update() suffit à figer _timer).
        TurnTimer = TickTimer.None;
        CurrentState = TurnState.Celebrating;

        StartCoroutine(CelebrateThenEndGame(winnerId));
    }

    /// <summary>
    /// ✅ Laisse jouer GoalCelebrationUI sur tous les clients avant de terminer réellement
    /// la partie. Seul le serveur (StateAuthority) exécute cette coroutine.
    /// </summary>
    private IEnumerator CelebrateThenEndGame(int winnerId)
    {
        yield return new WaitForSeconds(celebrationDuration);
        EndGameWinBySoccerGoal(winnerId);
    }

    private bool AreAllBallsStopped()
    {
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null && !ball.IsDead && ball.IsMoving) 
                return false;
        }
        return true;
    }

    private void EndGameWin(int winnerId)
    {
        WinnerPlayerId = winnerId;
        IsTurnBased = false;
        CurrentState = TurnState.Finished;

        string winnerName = GetPlayerName(winnerId);
        Debug.Log($"[TurnManager] 🎊 VICTOIRE de {winnerName} !");

        StartCoroutine(ReloadSceneAfterDelay("WIN", winnerName));
    }

    private void EndGameWinBySoccerGoal(int winnerId)
    {
        WinnerPlayerId = winnerId;
        IsTurnBased = false;
        CurrentState = TurnState.Finished;
        string winnerName = GetPlayerName(winnerId);
        Debug.Log($"[TurnManager] ⚽🎊 BUT ! Gagnant : {winnerName}");
        StartCoroutine(ReloadSceneAfterDelay("WIN", winnerName));
    }

    private void EndGameDraw()
    {
        WinnerPlayerId = -1;
        IsTurnBased = false;
        CurrentState = TurnState.Finished;

        StartCoroutine(ReloadSceneAfterDelay("DRAW", ""));
    }

    private IEnumerator ReloadSceneAfterDelay(string result, string winner)
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
}
