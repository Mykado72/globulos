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
        // Debug.Log("[SoccerBallController] ⚽ Ballon spawned (Serveur gère la physique)");
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
        if (AudioManager.Instance != null)
        {
            RPC_PlayGoalSound();
        }

        // ✅ Signale au TurnManager (serveur) la victoire
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.RequestWinBySoccerGoal(scorerId);
        }

        // ✨ FIX: Despawn du ballon après but pour éviter artefacts
        if (HasStateAuthority)
        {
            RPC_DespawnBall();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayGoalSound()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySoccerGoal();
        }
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
        if (Runner == null) return -1;

        // ✅ L'équipe qui marque est l'opposée de celle qui défend ce but
        GoalZone.GoalTeam scoringTeam = (defendingTeam == GoalZone.GoalTeam.Jaune)
            ? GoalZone.GoalTeam.Rouge
            : GoalZone.GoalTeam.Jaune;

        // 1. Chercher parmi les vrais joueurs connectés
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            GoalZone.GoalTeam playerTeam = (player.PlayerId % 2 == 0)
                ? GoalZone.GoalTeam.Jaune
                : GoalZone.GoalTeam.Rouge;

            if (playerTeam == scoringTeam)
            {
                return player.PlayerId;
            }
        }

        // 2. ✨ NEW : le bot (mode vs IA) n'est pas un vrai PlayerRef réseau,
        // donc il n'apparaît jamais dans Runner.ActivePlayers.
        if (GameModeManager.Instance != null && GameModeManager.Instance.IsVsAI && GameModeManager.Instance.BotPlayerId >= 0)
        {
            GoalZone.GoalTeam botTeam = (GameModeManager.Instance.BotPlayerId % 2 == 0)
                ? GoalZone.GoalTeam.Jaune
                : GoalZone.GoalTeam.Rouge;

            if (botTeam == scoringTeam)
            {
                return GameModeManager.Instance.BotPlayerId;
            }
        }

        Debug.LogWarning($"[SoccerBallController] ⚠️ Aucun joueur/bot trouvé pour l'équipe {scoringTeam} (adverse de {defendingTeam})");
        return -1;
    }
}
