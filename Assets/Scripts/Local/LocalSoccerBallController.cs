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
        LocalTurnManager.Instance?.RequestWinBySoccerGoal(scorerId);

        // ✨ CLEANUP: Retirer de la liste des billes jouables si nécessaire
        LocalBallAimController ballController = GetComponent<LocalBallAimController>();
        if (ballController != null)
        {
            Debug.Log("[LocalSoccerBallController] 🧹 Nettoyage de AllBalls");
            LocalBallAimController.AllBalls.Remove(ballController);
        }

        // Lancer l'animation au lieu de détruire instantanément
        StartCoroutine(GoalAnimationCoroutine());
    }

    private IEnumerator GoalAnimationCoroutine()
    {
        // Stopper la physique
        if (_rb != null)
        {
            _rb.velocity /= 5f; 
            _rb.angularVelocity /= 5f;

        }

        // Désactiver les collisions pour éviter d'autres déclenchements
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        float elapsedTime = 0f;
        Vector3 initialScale = transform.localScale;
        Quaternion initialRotation = transform.rotation;
        Color initialColor = _spriteRenderer != null ? _spriteRenderer.color : Color.white;

        while (elapsedTime < fallDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / fallDuration;

            // 1. Calcul de l'échelle (maintenant exact selon targetScaleFraction)
            float currentScale = Mathf.Lerp(1f, targetScaleFraction, t);
            transform.localScale = initialScale * currentScale;

            // 2. Calcul propre de la rotation (évite la déformation matricielle)
            float currentAngle = (totalRotation / fallDuration) * elapsedTime;
            transform.rotation = initialRotation * Quaternion.Euler(0f, 0f, currentAngle);

            // 3. Transition vers la couleur grise (sans baisser l'alpha)
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = Color.Lerp(initialColor, goalGrayColor, t);
            }

            yield return new WaitForSeconds(0.05f);
            // Stopper la physique
            if (_rb != null)
            {
                _rb.velocity = Vector2.zero;
                _rb.angularVelocity = 0f;
                _rb.isKinematic = true;
            }

        }
    }

    private int FindScoringPlayer(GoalZone.GoalTeam defendingTeam)
    {
        GoalZone.GoalTeam scoringTeam = (defendingTeam == GoalZone.GoalTeam.Jaune)
            ? GoalZone.GoalTeam.Rouge
            : GoalZone.GoalTeam.Jaune;

        return (scoringTeam == GoalZone.GoalTeam.Jaune) ? 2 : 1;
    }
}