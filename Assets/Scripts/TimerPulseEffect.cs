using UnityEngine;
using TMPro;

/// <summary>
/// ✅ Anime le chiffre du timer quand il reste peu de temps : grossissement pulsé + rouge,
/// pour donner une sensation d'urgence façon compte à rebours de match.
///
/// Classe statique pure (aucun état interne persistant autre que les constantes) : appelée
/// depuis TurnUI.Update() à chaque frame avec le temps restant. Comme TurnUI est déjà
/// commun aux modes Local et Network, ce comportement s'applique automatiquement aux deux
/// sans duplication.
/// </summary>
public static class TimerPulseEffect
{
    private const float WarningThresholdSeconds = 3f;
    private const float MaxPulseScale = 1.6f;

    private static readonly Color WarningColor = new Color(0.95f, 0.15f, 0.15f);

    /// <summary>
    /// À appeler chaque frame avec le temps restant. Remet automatiquement le texte à son
    /// échelle/couleur d'origine tant qu'on est au-dessus du seuil d'urgence.
    /// </summary>
    /// <param name="text">Le TMP_Text du timer.</param>
    /// <param name="remainingSeconds">Temps restant en secondes.</param>
    /// <param name="baseScale">Échelle normale du texte (capturée une fois, avant toute animation).</param>
    /// <param name="baseColor">Couleur normale du texte (capturée une fois, avant toute animation).</param>
    public static void Apply(TMP_Text text, float remainingSeconds, Vector3 baseScale, Color baseColor)
    {
        if (text == null) return;

        bool isUrgent = remainingSeconds > 0f && remainingSeconds <= WarningThresholdSeconds;

        if (!isUrgent)
        {
            text.transform.localScale = baseScale;
            text.color = baseColor;
            return;
        }

        // Portion écoulée dans la seconde en cours : proche de 1 juste après le "tick"
        // (changement de chiffre affiché), proche de 0 juste avant le tick suivant.
        // Ça crée un battement (pulse) synchronisé avec le changement du chiffre à l'écran.
        float fractional = remainingSeconds - Mathf.Floor(remainingSeconds);
        float pulsePhase = 1f - fractional;
        float pulse = Mathf.Sin(pulsePhase * Mathf.PI * 0.5f);

        float scale = Mathf.Lerp(1f, MaxPulseScale, pulse);
        text.transform.localScale = baseScale * scale;
        text.color = WarningColor;
    }

    /// <summary>Force le retour à l'état normal (à appeler en fin de partie par exemple).</summary>
    public static void Reset(TMP_Text text, Vector3 baseScale, Color baseColor)
    {
        if (text == null) return;
        text.transform.localScale = baseScale;
        text.color = baseColor;
    }
}
