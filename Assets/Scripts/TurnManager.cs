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

    // --- Variables Réseau Synchronisées ---
    [Networked] public NetworkBool IsTurnBased { get; set; }
    [Networked] public TurnState CurrentState { get; set; }
    [Networked] private TickTimer TurnTimer { get; set; }
    [Networked] public int CurrentTurnNumber { get; set; }

    [SerializeField] private float MagnitudeConsideredStationary = 0.15f;
    public static TurnManager Instance { get; private set; }

    public override void Spawned()
    {
        Instance = this;

        if (HasStateAuthority)
        {
            IsTurnBased = defaultTurnBasedMode;

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
                    ExecuteTurnResolution();
                }
                break;

            case TurnState.Resolution:
                if (AreAllBallsStopped())
                {
                    CurrentState = TurnState.CheckResult;
                }
                break;

            case TurnState.CheckResult:
                CheckGameEnd();
                StartNewTurn();
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
        // Plus besoin du foreach ici ! 
        // Chaque client gère le tir de ses billes de son côté via Render().
        /*
        // Déclenche tous les tirs mis en mémoire tampon
        BallAimController[] balls = FindObjectsOfType<BallAimController>();
        foreach (var ball in balls)
        {
            ball.ExecuteBufferedShoot();
        }
        */
    }

    private bool AreAllBallsStopped()
    {
        Rigidbody2D[] bodies = FindObjectsOfType<Rigidbody2D>();
        foreach (var rb in bodies)
        {
            if (rb.velocity.sqrMagnitude > MagnitudeConsideredStationary) return false;
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