using System.Collections;
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

    [Header("Reset Settings")]
    [Tooltip("Délai après la fin de l'animation de but avant que le ballon ne redevienne jouable.")]
    [SerializeField] private float respawnDelay = 0.5f;

    private SpriteRenderer _spriteRenderer;
    private Rigidbody2D _rb;

    // ✅ FIX : état initial du ballon, mémorisé pour pouvoir le "remplacer" après un but
    // (position/rotation/scale/couleur au moment du spawn par LocalGameSpawner).
    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private Vector3 _initialScale;
    private Color _initialColor;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();

        _initialPosition = transform.position;
        _initialRotation = transform.rotation;
        _initialScale = transform.localScale;
        _initialColor = _spriteRenderer != null ? _spriteRenderer.color : Color.white;
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

        // ✅ FIX : on enchaîne l'animation de but puis la remise en jeu du ballon,
        // au lieu de le laisser figé/gris indéfiniment sur le terrain.
        StartCoroutine(PlayGoalAnimationAndReset());
    }

    private IEnumerator PlayGoalAnimationAndReset()
    {
        // ✅ REFACTOR : animation partagée avec SoccerBallController (réseau), voir GoalScoreAnimation.cs
        yield return StartCoroutine(GoalScoreAnimation.Run(
            transform, _spriteRenderer, _rb, GetComponent<Collider2D>(),
            fallDuration, totalRotation, targetScaleFraction, goalGrayColor));

        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(respawnDelay);
        }

        ResetBall();
    }

    /// <summary>
    /// Réinitialise la position, la rotation et l'état du ballon pour un nouveau round.
    /// </summary>
    /// <param name="spawnPosition">Nouvelle position de spawn.</param>
    public void ResetForNewRound(Vector3 spawnPosition)
    {
        // Arrête toute animation/coroutine en cours
        StopAllCoroutines();

        // Réinitialise la position, la rotation et l'échelle
        transform.position = spawnPosition;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        // Réinitialise la vélocité physique
        if (_rb != null)
        {
            _rb.velocity = Vector2.zero;
            _rb.angularVelocity = 0f;
        }

        // Réinitialise la couleur si nécessaire
        if (_spriteRenderer != null)
        {
            _spriteRenderer.color = _initialColor;
        }

        // Réinitialise les états internes
        _goalScored = false;
    }

    /// <summary>
    /// ✅ FIX : remet le ballon dans son état initial (position, rotation, échelle,
    /// couleur, physique, collider) pour qu'il redevienne jouable après un but.
    /// Sans ça, le ballon restait figé/gris/rétréci sur le terrain pour le reste
    /// de la partie en mode local.
    /// </summary>
    public void ResetBall()
    {
        _goalScored = false;

        transform.position = _initialPosition;
        transform.rotation = _initialRotation;
        transform.localScale = _initialScale;

        if (_spriteRenderer != null)
        {
            _spriteRenderer.color = _initialColor;
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.velocity = Vector2.zero;
            _rb.angularVelocity = 0f;
        }

        Debug.Log("[LocalSoccerBallController] 🔄 Ballon réinitialisé, prêt pour le prochain tour");
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
