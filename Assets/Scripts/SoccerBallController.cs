using Fusion;
using UnityEngine;

/// ✅ Ballon de foot neutre : n'appartient à aucun joueur, poussé par les collisions
/// avec les billes. But adverse touché = victoire immédiate.
/// Nécessite un Rigidbody2D (non-trigger, pour rebondir sur les billes) + un
/// NetworkRigidbody2D sur le prefab (comme sur les billes) pour que sa physique
/// soit répliquée par Fusion.
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class SoccerBallController : NetworkBehaviour
{
    // ✅ Empêche un double déclenchement pendant les ~2s de délai avant le reload de
    // scène (ReloadSceneAfterDelay dans TurnManager_GameEndLogic), au cas où le ballon
    // rebondirait dans le but plusieurs fois avant que la scène ne se recharge.
    private bool _goalScored;

    public override void Spawned()
    {
        _goalScored = false;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // ✅ Seul le client ayant la State Authority sur CE ballon simule vraiment sa
        // physique (les autres n'en ont qu'une copie kinématique) : ce garde-fou évite
        // qu'un client non-autoritaire ne déclenche la victoire par erreur.
        if (!HasStateAuthority || _goalScored) return;
        if (!collision.CompareTag("Goal")) return;

        GoalZone goal = collision.GetComponent<GoalZone>();
        if (goal == null)
        {
            Debug.LogWarning("[SoccerBallController] ⚠️ Le but touché n'a pas de composant GoalZone, impossible de déterminer l'équipe adverse !");
            return;
        }

        PlayerRef scorer = FindScoringPlayer(goal.DefendingTeam);
        if (scorer.IsNone)
        {
            Debug.LogWarning("[SoccerBallController] ⚠️ Impossible de déterminer qui a marqué (aucun joueur adverse trouvé).");
            return;
        }

        _goalScored = true;
        Debug.Log($"[SoccerBallController] ⚽ BUT ! Ballon entré dans le but {goal.DefendingTeam}, marqué par le Joueur {scorer.PlayerId}");

        // ✅ TurnManager.Instance peut avoir une State Authority différente de la nôtre :
        // on passe par une RPC ciblant sa State Authority (voir TurnManager.RPC_RequestWinBySoccerGoal).
        TurnManager.Instance?.RPC_RequestWinBySoccerGoal(scorer);
    }

    // Le joueur qui marque est celui dont l'équipe (déduite de la parité de son PlayerId,
    // même convention que GameSpawner.TrySpawnBalls : pair = Jaune, impair = Rouge) est
    // différente de celle qui défend le but touché. On lit cette info directement depuis
    // le registre statique des billes existantes plutôt que de reconstruire un PlayerRef.
    private PlayerRef FindScoringPlayer(GoalZone.GoalTeam defendingTeam)
    {
        foreach (BallAimController ball in BallAimController.AllBalls)
        {
            PlayerRef owner = ball.Owner;
            GoalZone.GoalTeam ownerTeam = (owner.PlayerId % 2 == 0) ? GoalZone.GoalTeam.Jaune : GoalZone.GoalTeam.Rouge;

            if (ownerTeam != defendingTeam)
            {
                return owner;
            }
        }

        return PlayerRef.None;
    }
}
