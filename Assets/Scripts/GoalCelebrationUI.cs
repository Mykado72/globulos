using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Debug")]
    [Tooltip("Affiche dans la console un diagnostic de visibilité du texte pendant les animations.")]
    [SerializeField] private bool debugVisibility = true;

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

    [Header("Défaite (l'adversaire a marqué)")]
    [Tooltip("Texte affiché au joueur qui vient d'encaisser un but.")]
    [SerializeField] private string loserMessage = "LOOSER...";
    [Tooltip("Couleur du texte pendant l'animation triste.")]
    [SerializeField] private Color loserColor = new Color(0.55f, 0.65f, 0.85f);
    [Tooltip("Échelle de départ : le texte apparaît un peu gros et se dégonfle doucement jusqu'à 1.")]
    [SerializeField] private float loserStartScale = 1.3f;
    [Tooltip("De combien le texte coule vers le bas (unités du Canvas). Mettre 0 pour désactiver.")]
    [SerializeField] private float loserSinkDistance = 30f;
    [Tooltip("Balancement triste en degrés (lent et léger).")]
    [SerializeField] private float loserSwayDegrees = 3f;
    [Tooltip("Multiplicateur de durée : l'animation triste est plus lente que la célébration.")]
    [SerializeField] private float loserSlowFactor = 1.6f;

    private Vector3 _cameraOriginalLocalPos;
    // Décalage vertical actuellement appliqué au texte par l'animation triste (travail en RELATIF,
    // pour ne dépendre d'aucune position mémorisée avant le premier layout du Canvas).
    private Vector3 _appliedSinkOffset;
    private Coroutine _activeCelebration;

    // Cas où le CanvasGroup "flash" est aussi le PARENT du texte : son alpha (0 hors flash) masquerait
    // alors le texte. Dans ce cas on garde ce groupe à alpha 1 et on fait clignoter uniquement les
    // Image (hors texte) qu'il contient.
    private bool _flashWrapsText;
    private readonly List<Graphic> _flashGraphics = new List<Graphic>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Debug.Log("[GoalCelebrationUI] ✅ Singleton initialisé");
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

        _flashWrapsText = flashOverlay != null && goalText != null && goalText.transform.IsChildOf(flashOverlay.transform);
        if (_flashWrapsText)
        {
            foreach (Graphic g in flashOverlay.GetComponentsInChildren<Graphic>(true))
            {
                if (g is TMP_Text) continue; // jamais le texte
                _flashGraphics.Add(g);
                g.canvasRenderer.SetAlpha(0f);
            }
            flashOverlay.alpha = 1f;
            Debug.Log($"[GoalCelebrationUI] flashOverlay '{flashOverlay.name}' contient goalText : le groupe reste à alpha 1, " +
                      $"le flash ne fait clignoter que {_flashGraphics.Count} image(s) : " + string.Join(", ", _flashGraphics.ConvertAll(x => x.name)));
        }
        else if (flashOverlay != null)
        {
            flashOverlay.alpha = 0f;
        }

        if (celebrationRoot != null) celebrationRoot.SetActive(false);
        if (shakeCamera != null) _cameraOriginalLocalPos = shakeCamera.transform.localPosition;
    }

    /// <summary>
    /// Déclenche l'animation de but. scorerName est optionnel (peut être laissé vide).
    /// </summary>
    public void PlayGoalCelebration(string scorerName = "")
    {
        Debug.Log($"[GoalCelebrationUI] 🎬 PlayGoalCelebration appelé (scorer: {scorerName})");

        AudioManager.Instance?.PlaySoccerGoal(); // "GOOOAL" : réservé au buteur
        AudioManager.Instance?.PlayGoalCrowd(popDuration + holdDuration + fadeOutDuration);

        if (_activeCelebration != null) StopCoroutine(_activeCelebration);
        _activeCelebration = StartCoroutine(CelebrationCoroutine("BUUUTTTT !!!", scorerName));
    }

    /// <summary>
    /// ✨ Point d'entrée unique pour un but : célébration pour celui qui marque,
    /// animation de défaite ("LOOSER...") pour celui qui encaisse.
    /// </summary>
    public void PlayGoalOutcome(string scorerName, bool localPlayerScored)
    {
        if (localPlayerScored) PlayGoalCelebration(scorerName);
        else PlayLoserCelebration(scorerName);
    }

    /// <summary>Animation triste : pas de confettis, pas de flash, pas de secousse ; le texte se dégonfle, se balance et coule.</summary>
    public void PlayLoserCelebration(string scorerName = "")
    {
        Debug.Log($"[GoalCelebrationUI] PlayLoserCelebration appelé (buteur adverse: {scorerName}) - message='{loserMessage}'");

        AudioManager.Instance?.PlayLoser();
        AudioManager.Instance?.PlayGoalCrowd((popDuration + holdDuration) * loserSlowFactor + fadeOutDuration);

        if (_activeCelebration != null) StopCoroutine(_activeCelebration);
        _activeCelebration = StartCoroutine(CelebrationCoroutine(loserMessage, scorerName, true));
    }

    private IEnumerator CelebrationCoroutine(string Message, string scorerName, bool sad = false)
    {
        if (celebrationRoot != null) celebrationRoot.SetActive(true);
        EnsureTextGroupsVisible();
        if (_flashWrapsText && flashOverlay != null) flashOverlay.alpha = 1f;
        ClearSinkOffset();
        if (goalText != null)
        {
            goalText.text = Message;
            goalText.transform.localScale = sad ? Vector3.one * loserStartScale : Vector3.zero;
            goalText.transform.localRotation = Quaternion.identity;
            // Couleur de base du texte quasi noire : la célébration la remplace par ses couleurs vives,
            // l'animation triste utilise sa propre couleur visible.
            Color c = sad ? loserColor : goalText.color; c.a = 1f; goalText.color = c;
        }
        if (scorerText != null)
        {
            if (string.IsNullOrEmpty(scorerName)) scorerText.text = "";
            else scorerText.text = sad ? $"{scorerName} a marqué" : $"{scorerName} !";
            Color c = scorerText.color; c.a = 1f; scorerText.color = c;
        }

        if (sad)
        {
            yield return SadDeflate();
            yield return SadHold();
        }
        else
        {
            // `?.` ne passe pas par l'opérateur == surchargé d'Unity : on garde des `if` explicites.
            if (confettiParticles != null) confettiParticles.Play();
            if (flashOverlay != null) StartCoroutine(FlashCoroutine());

            yield return PopIn();
            yield return HoldWithCrowdEnergy();
        }
        yield return FadeOut();

        if (goalText != null)
        {
            ClearSinkOffset();
            goalText.transform.localRotation = Quaternion.identity;
        }
        if (celebrationRoot != null) celebrationRoot.SetActive(false);
        _activeCelebration = null;
    }

    /// <summary>Diagnostic : pourquoi un texte "actif, à la bonne échelle et à la bonne couleur" peut rester invisible.</summary>
    private void LogTextVisibility(string tag)
    {
        if (!debugVisibility || goalText == null) return;

        Canvas canvas = goalText.canvas;
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        Vector3[] corners = new Vector3[4];
        goalText.rectTransform.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        string canvasInfo = canvas != null ? canvas.name + "/" + canvas.renderMode + "/order=" + canvas.sortingOrder : "NULL";
        string groups = "";
        foreach (CanvasGroup g in goalText.GetComponentsInParent<CanvasGroup>(true))
            groups += $"{g.name}(alpha={g.alpha}, enabled={g.enabled}) ";

        Debug.Log($"[Vis:{tag}] inheritedAlpha={goalText.canvasRenderer.GetInheritedAlpha()}, cull={goalText.canvasRenderer.cull}, " +
                  $"chars={goalText.textInfo.characterCount}, bounds={goalText.bounds.size}, enabled={goalText.enabled}, " +
                  $"canvas={canvasInfo}, " +
                  $"screenRect=({min.x:F0},{min.y:F0})->({max.x:F0},{max.y:F0}) sur {Screen.width}x{Screen.height}, " +
                  $"localPos={goalText.transform.localPosition}, scale={goalText.transform.localScale}, color={goalText.color}, " +
                  $"canvasGroups=[{groups}]");
    }

    /// <summary>
    /// Un CanvasGroup parent resté à alpha 0 masque le texte même si sa couleur/échelle sont correctes.
    /// On remet à 1 tous les CanvasGroup ancêtres, sauf le flash (dont l'alpha est piloté volontairement).
    /// </summary>
    private void EnsureTextGroupsVisible()
    {
        if (goalText == null) return;
        foreach (CanvasGroup g in goalText.GetComponentsInParent<CanvasGroup>(true))
        {
            if (g == flashOverlay) continue;
            if (g.alpha < 1f)
            {
                Debug.LogWarning($"[GoalCelebrationUI] CanvasGroup '{g.name}' avait alpha={g.alpha} : remis à 1 pour que le texte soit visible.");
                g.alpha = 1f;
            }
        }
    }

    /// <summary>Retire le décalage de l'animation triste (remet le texte à sa place).</summary>
    private void ClearSinkOffset()
    {
        if (goalText != null && _appliedSinkOffset != Vector3.zero)
            goalText.transform.localPosition -= _appliedSinkOffset;
        _appliedSinkOffset = Vector3.zero;
    }

    /// <summary>Triste 1/2 : le texte apparaît un peu gros et se dégonfle doucement.</summary>
    private IEnumerator SadDeflate()
    {
        if (goalText == null) yield break;

        float duration = Mathf.Max(0.01f, popDuration * loserSlowFactor);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t); // ease-out
            goalText.transform.localScale = Vector3.one * Mathf.Lerp(loserStartScale, 1f, eased);
            yield return null;
        }
        goalText.transform.localScale = Vector3.one;
    }

    /// <summary>Triste 2/2 : le texte se ternit, se balance lentement et coule.</summary>
    private IEnumerator SadHold()
    {
        float duration = Mathf.Max(0.01f, holdDuration * loserSlowFactor);
        float elapsed = 0f;
        bool logged = false;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            if (!logged && elapsed > 0.3f) { logged = true; LogTextVisibility("TRISTE"); }

            if (goalText != null)
            {
                Color c = loserColor;
                c.a = 1f;
                goalText.color = c;

                float sway = Mathf.Sin(elapsed * 2.5f) * loserSwayDegrees;
                goalText.transform.localRotation = Quaternion.Euler(0f, 0f, sway);

                Vector3 newOffset = Vector3.down * (loserSinkDistance * progress);
                goalText.transform.localPosition += newOffset - _appliedSinkOffset;
                _appliedSinkOffset = newOffset;
            }
            yield return null;
        }
    }

    public void PlayKILLERCelebration(string scorerName = "")
    {
        Debug.Log($"[GoalCelebrationUI] 🎬 PlayKILLERCelebration appelé (scorer: {scorerName})");

        if (_activeCelebration != null) StopCoroutine(_activeCelebration);
        _activeCelebration = StartCoroutine(CelebrationCoroutine("KILLER !!!", scorerName));
    }

    public void PlayDRAWCelebration()
    {
        Debug.Log($"[GoalCelebrationUI] 🎬 DRAW appelé");

        if (_activeCelebration != null) StopCoroutine(_activeCelebration);
        _activeCelebration = StartCoroutine(CelebrationCoroutine("MATCH NULL !!!", ""));
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
        bool logged = false;
        while (elapsed < holdDuration)
        {
            elapsed += Time.deltaTime;
            float remainingRatio = 1f - (elapsed / holdDuration);
            if (!logged && elapsed > 0.3f) { logged = true; LogTextVisibility("CELEBRATION"); }

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

        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            SetFlashAlpha(Mathf.Lerp(1f, 0f, elapsed / flashDuration));
            yield return null;
        }
        SetFlashAlpha(0f);
    }

    /// <summary>Alpha du flash : celui du groupe, ou seulement celui de ses images si le groupe contient le texte.</summary>
    private void SetFlashAlpha(float a)
    {
        if (_flashWrapsText)
        {
            foreach (Graphic g in _flashGraphics)
                if (g != null) g.canvasRenderer.SetAlpha(a);
        }
        else
        {
            flashOverlay.alpha = a;
        }
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
