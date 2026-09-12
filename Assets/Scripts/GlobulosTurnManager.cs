using Fusion;
using UnityEngine;

public enum GamePhase
{
    WaitingForPlayers,
    Planning,   // Les joueurs préparent leurs coups
    Resolving,  // La physique s'exécute simultanément
    Ended
}

public class GlobulosTurnManager : NetworkBehaviour
{
    // Variable synchronisée à travers le réseau
    [Networked] public GamePhase CurrentPhase { get; set; }
    [Networked] public float PhaseTimer { get; set; }

    [SerializeField] private float planningTime = 10f;
    [SerializeField] private float resolutionTime = 3f;

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentPhase = GamePhase.Planning;
            PhaseTimer = planningTime;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        // Compte à rebours de la phase
        if (PhaseTimer > 0)
        {
            PhaseTimer -= Runner.DeltaTime;
        }
        else
        {
            // Transition automatique de phase à la fin du chrono
            if (CurrentPhase == GamePhase.Planning)
            {
                StartResolutionPhase();
            }
            else if (CurrentPhase == GamePhase.Resolving)
            {
                StartPlanningPhase();
            }
        }
    }

    private void StartResolutionPhase()
    {
        CurrentPhase = GamePhase.Resolving;
        PhaseTimer = resolutionTime;

        // Appeler le déclenchement de la physique pour tous les clients
        RPC_ExecuteSimultaneousMoves();
    }

    private void StartPlanningPhase()
    {
        CurrentPhase = GamePhase.Planning;
        PhaseTimer = planningTime;
    }

    // RPC permettant de déclencher le lancement physique sur tous les clients
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_ExecuteSimultaneousMoves()
    {
        // Code pour débloquer la physique (Physics2D) et appliquer les forces accumulées
    }
}