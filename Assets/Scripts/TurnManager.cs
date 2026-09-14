using Fusion;
using UnityEngine;

public partial class TurnManager : NetworkBehaviour
{
    public enum TurnState
    {
        RealTime,   // Mode temps réel actif
        Aiming,     // Tour par tour : Phase de visée
        Resolution, // Tour par tour : Application des forces
        CheckResult, // Tour par tour : Attente de l'arrêt des billes
        Finished
    }

    [Header("Game Mode Configuration")]
    [Tooltip("Cocher pour activer le mode tour par tour. Décocher pour le temps réel.")]
    [SerializeField] private bool defaultTurnBasedMode = true;

    [Header("Turn-Based Settings")]
    [SerializeField] private float aimDuration = 15f;

    [Tooltip("Délai minimum après le début de la Résolution avant de commencer à vérifier si les billes sont arrêtées. Nécessaire car l'application des forces (via Render() + RPC) prend un peu de temps à se propager après le passage en Resolution.")]
    [SerializeField] private float resolutionSettleDuration = 0.2f;

    // --- Variables Réseau Synchronisées ---
    [Networked] public NetworkBool IsTurnBased { get; set; }
    [Networked] public TurnState CurrentState { get; set; }
    [Networked] private TickTimer TurnTimer { get; set; }
    [Networked] private TickTimer ResolutionSettleTimer { get; set; }
    [Networked] public int CurrentTurnNumber { get; set; }

    // ✅ AJOUT : Résultat de la partie, répliqué à tous les clients pour piloter l'UI
    [Networked] public int WinnerPlayerId { get; set; }

    public static TurnManager Instance { get; private set; }

    public override void Spawned()
    {
        Instance = this;

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
        if (!HasStateAuthority || !IsTurnBased) return;

        switch (CurrentState)
        {
            case TurnState.Aiming:
                if (TurnTimer.Expired(Runner))
                {
                    // ✅ FIX : Quand le timer expire, force l'arrêt du visage sur TOUS les joueurs
                    RPC_ForceStopAiming();
                    ExecuteTurnResolution();
                }
                break;

            case TurnState.Resolution:
                // ✅ On attend un délai de grâce avant même de commencer à
                // vérifier l'arrêt des billes : le temps que les forces
                // bufferisées soient réellement appliquées (via Render()
                // sur chaque client) et que IsMoving se propage sur le réseau.
                // Sans ça, on peut détecter "tout est arrêté" alors que rien
                // n'a encore commencé à bouger.
                if (!ResolutionSettleTimer.ExpiredOrNotRunning(Runner)) break;

                if (AreAllBallsStopped())
                {
                    CurrentState = TurnState.CheckResult;
                }
                break;

            case TurnState.CheckResult:
                CheckGameEnd();
                // ✅ Si CheckGameEnd() a fait passer l'état à Finished (victoire/égalité),
                // on ne relance pas un nouveau tour par-dessus.
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
    }

    // ✅ NOUVEAU : RPC pour forcer l'arrêt du visage quand le timer arrive à 0
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ForceStopAiming()
    {
        Debug.Log("[TurnManager] ⏰ Timer écoulé - Force l'arrêt du visage");

        // Trouve tous les BallAimController et force OnMouseUp()
        var allBalls = FindObjectsOfType<BallAimController>();
        foreach (var ball in allBalls)
        {
            ball.ForceStopAiming();
        }
    }

    private bool AreAllBallsStopped()
    {
        // ⚠️ On NE PEUT PAS utiliser Rigidbody2D.velocity ici : ce code ne
        // tourne que sur le client ayant la State Authority sur TurnManager
        // (le Master). Pour lui, les billes des AUTRES joueurs sont des
        // proxies réseau cinématiques dont la vélocité locale vaut toujours 0,
        // qu'elles bougent réellement ou non chez leur propriétaire.
        // On se fie donc à BallAimController.IsMoving, répliqué par celui qui
        // a réellement l'autorité sur chaque bille.
        BallAimController[] balls = FindObjectsOfType<BallAimController>();
        foreach (var ball in balls)
        {
            if (ball.IsMoving) return false;
        }
        return true;
    }

    public float GetRemainingTime()
    {
        if (IsTurnBased && TurnTimer.IsRunning)
        {
            return TurnTimer.RemainingTime(Runner) ?? 0f;
        }
        return 0f;
    }
}
