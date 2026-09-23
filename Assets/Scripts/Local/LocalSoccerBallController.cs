using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class LocalSoccerBallController : MonoBehaviour
{
    private bool _goalScored = false;

    [Header("Goal Animation Settings")]
    [SerializeField] private float fallDuration = 0.75f;
    [SerializeField] private float totalRotation = 180f;
    [SerializeField] private float targetScaleFraction = 0.99f; // Taille finale (85%)
    [SerializeField] private Color goalGrayColor = new Color(0.4f, 0.4f, 0.4f, 1f); // Gris foncé

    private SpriteRenderer _spriteRenderer;
    private Rigidbody2D _rb;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
    }

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

        // ✅ Célébration visuelle "BUT !", partagée avec SoccerBallController (réseau)
        if (GoalCelebrationUI.Instance == null)
        {
            Debug.LogWarning("[LocalSoccerBallController] ⚠️ GoalCelebrationUI.Instance est null — as-tu bien un GameObject avec ce script dans la scène ?");
        }
        GoalCelebrationUI.Instance?.PlayGoalCelebration(TurnManagerFactory.GetPlayerName(scorerId));

        // ✅ FIX : Ajouter le point au lieu de terminer immédiatement
        if (ScoreManagerLocal.Instance != null)
        {
            ScoreManagerLocal.Instance.AddGoal(scorerId);
            // ScoreManagerLocal.CheckWinCondition() vérifiera si quelqu'un a gagné
            // Si oui, il appellera LocalTurnManager.Instance?.RequestWinBySoccerGoal()
            // Si non, la partie continue
        }
        else
        {
            // ✨ Fallback si ScoreManager n'existe pas (ancien comportement)
            Debug.LogWarning("[LocalSoccerBallController] ⚠️ ScoreManagerLocal.Instance est null - fin de partie immédiate");
            LocalTurnManager.Instance?.RequestWinBySoccerGoal(scorerId);
        }

        // ✨ CLEANUP: Retirer de la liste des billes jouables si nécessaire
        LocalBallAimController ballController = GetComponent<LocalBallAimController>();
        if (ballController != null)
        {
            Debug.Log("[LocalSoccerBallController] 🧹 Nettoyage de AllBalls");
            LocalBallAimController.AllBalls.Remove(ballController);
        }

        // ✅ REFACTOR : animation partagée avec SoccerBallController (réseau), voir GoalScoreAnimation.cs
        StartCoroutine(GoalScoreAnimation.Run(
            transform, _spriteRenderer, _rb, GetComponent<Collider2D>(),
            fallDuration, totalRotation, targetScaleFraction, goalGrayColor));
    }

    private int FindScoringPlayer(GoalZone.GoalTeam defendingTeam)
    {
        GoalZone.GoalTeam scoringTeam = (defendingTeam == GoalZone.GoalTeam.Jaune)
            ? GoalZone.GoalTeam.Rouge
            : GoalZone.GoalTeam.Jaune;

        // Convention (voir LocalGameSpawner) : Joueur 1 = Jaune (humain), Joueur 2 = Rouge (bot).
        return (scoringTeam == GoalZone.GoalTeam.Jaune) ? 1 : 2;
    }
}
