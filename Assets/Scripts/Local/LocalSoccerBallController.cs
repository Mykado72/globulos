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

        AudioManager.Instance?.PlaySoccerGoal();
        LocalTurnManager.Instance?.RequestWinBySoccerGoal(scorerId);

        Destroy(gameObject);
    }

    private int FindScoringPlayer(GoalZone.GoalTeam defendingTeam)
    {
        GoalZone.GoalTeam scoringTeam = (defendingTeam == GoalZone.GoalTeam.Jaune)
            ? GoalZone.GoalTeam.Rouge
            : GoalZone.GoalTeam.Jaune;

        // Joueur 1 (Humain) : ID 1
        // Joueur 2 (IA) : ID 2
        GoalZone.GoalTeam player1Team = GoalZone.GoalTeam.Jaune; // ID 1 % 2 != 0 -> Rouge ou Jaune selon vos règles

        // Match l'équipe
        return (scoringTeam == GoalZone.GoalTeam.Jaune) ? 1 : 2;
    }
}