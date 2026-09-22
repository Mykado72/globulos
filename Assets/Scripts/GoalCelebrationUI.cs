using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// ✅ Animation "BUT !" avec l'exaltation d'un stade : le texte surgit en s'agrandissant
/// façon élastique (léger dépassement puis stabilisation), les couleurs clignotent comme un
/// panneau lumineux de stade, un tremblement de caméra optionnel simule la ferveur des
/// tribunes, puis tout s'estompe en fondu.
///
/// Singleton comme AudioManager/UIManager : un seul GameObject dans la scène (Local comme
/// Network), avec les références UI assignées dans l'Inspector.
///
/// Appelé directement par SoccerBallController ET LocalSoccerBallController dès qu'un but
/// est détecté (avant même RequestWinBySoccerGoal) — un point d'entrée unique partagé entre
/// les deux modes, comme AudioManager.Instance?.PlaySoccerGoal().
/// </summary>
public class GoalCelebrationUI : MonoBehaviour
{
    public static GoalCelebrationUI Instance { get; private set; }

    [Header("Références UI")]
    [Tooltip("Conteneur racine à activer/désactiver (peut inclure le flash et le texte).")]
    [SerializeField] private GameObject celebrationRoot;
    [SerializeField] private TMP_Text goalText;
    [Tooltip("Optionnel : affiche le nom du buteur sous le texte 'BUT'.")]
    [SerializeField] private TMP_Text scorerText;
    [Tooltip("Optionnel : image plein écran pour un flash blanc façon appareil photo de stade.")]
    [SerializeField] private CanvasGroup flashOverlay;
    [Tooltip("Optionnel : jaillissement de confettis à l'apparition du texte.")]
    [SerializeField] private ParticleSystem confettiParticles;
    [Tooltip("Optionnel : caméra à secouer légèrement pendant la célébration.")]
    [SerializeField] private Camera shakeCamera;

    [Header("Timing")]
    [SerializeField] private float popDuration = 0.4f;
    [SerializeField] private float holdDuration = 1.2f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float flashDuration = 0.25f;

    [Header("Style")]
    [SerializeField] private float overshootScale = 1.3f;
    [SerializeField] private float wobbleMagnitudeDegrees = 15f;
    [SerializeField] private float cameraShakeMagnitude = 0.15f;
    [SerializeField] private float colorFlickerSpeed = 12f;
    [SerializeField]
    private Color[] flickerColors = new[]
    {
        new Color(1f, 0.85f, 0f),   // Or (façon panneau de stade)
        new Color(1f, 1f, 1f),      // Blanc
        new Color(1f, 0.3f, 0.1f),  // Rouge-orangé
    };

    private Vector3 _cameraOriginalLocalPos;
    private Coroutine _activeCelebration;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("[GoalCelebrationUI] ✅ Singleton initialisé");
        }
        else
        {
            Debug.LogWarning("[GoalCelebrationUI] Une instance existe déjà, destruction de celle-ci");
            Destroy(gameObject);
            return;
        }

        if (goalText == null)
        {
            Debug.LogWarning("[GoalCelebrationUI] ⚠️ 'goalText' n'est pas assigné dans l'Inspector — l'animation ne sera pas visible !");
        }

        if (celebrationRoot == gameObject)
        {
            Debug.LogError("[GoalCelebrationUI] ❌ 'celebrationRoot' pointe vers CE GameObject (celui qui porte le script) ! " +
                "En le désactivant, plus aucune coroutine ne peut tourner dessus. " +
                "celebrationRoot doit être un OBJET ENFANT séparé (un panneau), jamais l'objet du script lui-même.");
        }

        if (celebrationRoot != null) celebrationRoot.SetActive(false);
        if (flashOverlay != null) flashOverlay.alpha = 0f;
        if (shakeCamera != null) _cameraOriginalLocalPos = shakeCamera.transform.localPosition;
    }

    /// <summary>
    /// Déclenche l'animation de but. scorerName est optionnel (peut être laissé vide).
    /// </summary>
    public void PlayGoalCelebration(string scorerName = "")
    {
        Debug.Log($"[GoalCelebrationUI] 🎬 PlayGoalCelebration appelé (scorer: {scorerName})");

        if (_activeCelebration != null) StopCoroutine(_activeCelebration);
        _activeCelebration = StartCoroutine(CelebrationCoroutine(scorerName));
    }

    private IEnumerator CelebrationCoroutine(string scorerName)
    {
        if (celebrationRoot != null) celebrationRoot.SetActive(true);
        if (goalText != null)
        {
            goalText.text = "BUUUUT !";
            goalText.transform.localScale = Vector3.zero;
            goalText.transform.localRotation = Quaternion.identity;
            Color c = goalText.color; c.a = 1f; goalText.color = c;
        }
        if (scorerText != null)
        {
            scorerText.text = string.IsNullOrEmpty(scorerName) ? "" : $"⚽ {scorerName} !";
            Color c = scorerText.color; c.a = 1f; scorerText.color = c;
        }

        // ✅ FIX : `confettiParticles?.Play()` provoquait une UnassignedReferenceException
        // quand le champ était laissé à "None" dans l'Inspector. L'opérateur ?. ne passe
        // pas par l'opérateur == surchargé d'Unity (qui traite "None" comme null) ; il fait
        // un vrai test de référence C#, qui ne considère pas ce champ comme null. Un `if`
        // explicite, lui, utilise bien la surcharge Unity.
        if (confettiParticles != null) confettiParticles.Play();
        if (flashOverlay != null) StartCoroutine(FlashCoroutine());

        yield return PopIn();
        yield return HoldWithCrowdEnergy();
        yield return FadeOut();

        if (celebrationRoot != null) celebrationRoot.SetActive(false);
        _activeCelebration = null;
    }

    /// <summary>1. Le texte surgit avec un léger dépassement élastique (effet "pop").</summary>
    private IEnumerator PopIn()
    {
        if (goalText == null) yield break;

        float elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / popDuration;
            float eased = EaseOutBack(t);
            goalText.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0f, overshootScale, eased);
            yield return null;
        }
        goalText.transform.localScale = Vector3.one;
    }

    /// <summary>2. Maintien avec clignotement de couleurs + wobble + tremblement caméra (ferveur du stade).</summary>
    private IEnumerator HoldWithCrowdEnergy()
    {
        float elapsed = 0f;
        while (elapsed < holdDuration)
        {
            elapsed += Time.deltaTime;
            float remainingRatio = 1f - (elapsed / holdDuration);

            if (goalText != null)
            {
                int colorIndex = Mathf.FloorToInt(elapsed * colorFlickerSpeed) % flickerColors.Length;
                Color flicker = flickerColors[colorIndex];
                flicker.a = goalText.color.a;
                goalText.color = flicker;

                float wobble = Mathf.Sin(elapsed * 20f) * wobbleMagnitudeDegrees * remainingRatio;
                goalText.transform.localRotation = Quaternion.Euler(0f, 0f, wobble);
            }

            if (shakeCamera != null)
            {
                Vector2 offset = Random.insideUnitCircle * cameraShakeMagnitude * remainingRatio;
                shakeCamera.transform.localPosition = _cameraOriginalLocalPos + (Vector3)offset;
            }

            yield return null;
        }

        if (goalText != null) goalText.transform.localRotation = Quaternion.identity;
        if (shakeCamera != null) shakeCamera.transform.localPosition = _cameraOriginalLocalPos;
    }

    /// <summary>3. Fondu de sortie du texte et du nom du buteur.</summary>
    private IEnumerator FadeOut()
    {
        float elapsed = 0f;
        Color goalStart = goalText != null ? goalText.color : Color.white;
        Color scorerStart = scorerText != null ? scorerText.color : Color.white;

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);

            if (goalText != null)
            {
                Color c = goalStart; c.a = alpha; goalText.color = c;
            }
            if (scorerText != null)
            {
                Color c = scorerStart; c.a = alpha; scorerText.color = c;
            }

            yield return null;
        }
    }

    private IEnumerator FlashCoroutine()
    {
        if (flashOverlay == null) yield break;

        flashOverlay.alpha = 1f;
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            flashOverlay.alpha = Mathf.Lerp(1f, 0f, elapsed / flashDuration);
            yield return null;
        }
        flashOverlay.alpha = 0f;
    }

    /// <summary>Easing "back out" : léger dépassement puis retour, donne l'effet élastique.</summary>
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * (x * x * x) + c1 * (x * x);
    }
}
