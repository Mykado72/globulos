using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class LocalSoccerBallController : MonoBehaviour
{
    private bool _goalScored = false;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_goalScored || !collision.CompareTag("Goal")) return;

        GoalZone goal = collision.GetComponent<GoalZone>();
        if (goal == null) return;

        int scorerId = FindScoringPlayer(goal.DefendingTeam);
        if (scorerId < 0) return;

        _goalScored = true;

        Debug.Log($"[LocalSoccerBallController] ⚽ But marqué! Scoreur: Joueur {scorerId}");

        AudioManager.Instance?.PlaySoccerGoal();
        LocalTurnManager.Instance?.RequestWinBySoccerGoal(scorerId);

        // ✨ CLEANUP: Nettoyer la bille si elle a un composant LocalBallAimController
        // (au cas où elle serait aussi marquée comme "à jouer")
        LocalBallAimController ballController = GetComponent<LocalBallAimController>();
        if (ballController != null)
        {
            Debug.Log("[LocalSoccerBallController] 🧹 Nettoyage de AllBalls");
            LocalBallAimController.AllBalls.Remove(ballController);
        }

        Debug.Log("[LocalSoccerBallController] 💥 Destruction du ballon");
        Destroy(gameObject);
    }

    private int FindScoringPlayer(GoalZone.GoalTeam defendingTeam)
    {
        GoalZone.GoalTeam scoringTeam = (defendingTeam == GoalZone.GoalTeam.Jaune)
            ? GoalZone.GoalTeam.Rouge
            : GoalZone.GoalTeam.Jaune;

        // Joueur 1 (Humain) : ID 1
        // Joueur 2 (IA) : ID 2
        // GoalTeam.Jaune = Joueur pair (ex: 2)
        // GoalTeam.Rouge = Joueur impair (ex: 1)

        // Donc si scoringTeam == Jaune → Joueur pair (2)
        //       si scoringTeam == Rouge → Joueur impair (1)

        return (scoringTeam == GoalZone.GoalTeam.Jaune) ? 2 : 1;
    }
}