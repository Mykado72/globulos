using Fusion;
using UnityEngine;

/// ✅ VERSION OPTIMISÉE v3
/// - Serveur : State Authority, gère la physique du ballon et détecte les buts
/// - Clients : Reçoivent les mises à jour de position/rotation
/// ✨ FIX: Despawn du ballon après but pour éviter les artefacts
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class SoccerBallController : NetworkBehaviour
{
    [Networked] private bool _goalScored { get; set; }

    public override void Spawned()
    {
        _goalScored = false;
        Debug.Log("[SoccerBallController] ⚽ Ballon spawned (Serveur gère la physique)");
    }

    // ✅ CLIENT/SERVER : Seul le serveur vérifie les collisions avec les buts
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!HasStateAuthority || _goalScored) return;
        if (!collision.CompareTag("Goal")) return;

        GoalZone goal = collision.GetComponent<GoalZone>();
        if (goal == null)
        {
            Debug.LogWarning("[SoccerBallController] ⚠️ Le but n'a pas de composant GoalZone");
            return;
        }

        // Déterminer le marqueur (celui qui n'est pas dans l'équipe adverse)
        int scorerId = FindScoringPlayer(goal.DefendingTeam);
        if (scorerId < 0)
        {
            Debug.LogWarning("[SoccerBallController] ⚠️ Impossible de déterminer le marqueur");
            return;
        }

        _goalScored = true;
        Debug.Log($"[SoccerBallController] ⚽ BUT ! Marqué par PlayerId {scorerId}");

        // ✅ RPC : Diffuse le son à tous les clients
        RPC_PlayGoalSound();

        // ✅ Signale au TurnManager (serveur) la victoire
        TurnManager.Instance?.RPC_RequestWinBySoccerGoal(scorerId);

        // ✨ FIX: Despawn du ballon après but pour éviter artefacts
        // Raison: Le ballon reste visible/physiquement actif après le but
        // Solution: Despawner sur le serveur (StateAuthority)
        if (HasStateAuthority)
        {
            RPC_DespawnBall();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayGoalSound()
    {
        AudioManager.Instance?.PlaySoccerGoal();
    }

    /// <summary>
    /// ✨ FIX: RPC pour despawner le ballon côté serveur
    /// Appelé depuis OnTriggerEnter2D quand un but est marqué
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void RPC_DespawnBall()
    {
        if (HasStateAuthority && Runner != null)
        {
            Debug.Log("[SoccerBallController] 🗑️ Despawn du ballon après but");
            Runner.Despawn(Object);
        }
    }

    private int FindScoringPlayer(GoalZone.GoalTeam defendingTeam)
    {
        foreach (BallAimController ball in BallAimController.AllBalls)
        {
            if (ball == null) continue;

            GoalZone.GoalTeam ownerTeam = (ball.OwnerPlayerId % 2 == 0)
                ? GoalZone.GoalTeam.Jaune
                : GoalZone.GoalTeam.Rouge;

            if (ownerTeam != defendingTeam)
            {
                return ball.OwnerPlayerId;
            }
        }

        return -1;
    }
}
