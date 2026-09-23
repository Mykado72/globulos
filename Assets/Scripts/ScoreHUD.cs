using UnityEngine;
using TMPro;

/// <summary>
/// ✅ HUD Score Avancé
/// - Affiche le score en temps réel
/// - Affiche les noms des équipes
/// - Affiche les couleurs de chaque équipe
/// - Animation quand le score augmente
/// Fonctionne pour les DEUX modes : Réseau ET Local
/// </summary>
public class ScoreHUD : MonoBehaviour
{
    [SerializeField] private ScoreManagerBase ScoreManagerBase;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Team Colors")]
    [SerializeField] private Color team1Color = Color.yellow;
    [SerializeField] private Color team2Color = new Color(1f, 0f, 0f);

    [Header("Animation")]
    [SerializeField] private bool enableAnimation = true;
    [SerializeField] private float animationDuration = 0.3f;

    private int lastTeam1Score = -1;
    private int lastTeam2Score = -1;

    private void Start()
    {
        // Chercher les composants s'ils ne sont pas assignés
        if (scoreText == null)
        {
            scoreText = GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        }

        // S'abonner aux changements de score
        // ✅ Utilise ScoreManagerBase qui fonctionne pour les DEUX modes
        if (ScoreManagerBase.Instance != null)
        {
            ScoreManagerBase.Instance.OnScoreChanged += UpdateScoreDisplay;
            // Affichage initial
            UpdateScoreDisplay(0, 0);
            Debug.Log("[ScoreHUD] ✅ Abonné aux changements de score");
        }
        else
        {
            Debug.LogWarning("[ScoreHUD] ⚠️ ScoreManagerBase.Instance non trouvé! Vérifier que ScoreManager existe en scene.");
        }

        UpdateStatus("Jeu commencé");
    }

    private void OnDestroy()
    {
        // Se désabonner pour éviter les memory leaks
        if (ScoreManagerBase.Instance != null)
        {
            ScoreManagerBase.Instance.OnScoreChanged -= UpdateScoreDisplay;
        }
    }

    /// <summary>
    /// Callback appelé quand le score change
    /// </summary>
    private void UpdateScoreDisplay(int team1Score, int team2Score)
    {
        if (scoreText == null) return;

        // Vérifier s'il y a eu un changement
        bool scoreChanged = (team1Score != lastTeam1Score || team2Score != lastTeam2Score);
        if (scoreChanged && enableAnimation)
        {
            StopCoroutine(nameof(ScorePulseAnimation));
            StartCoroutine(ScorePulseAnimation());
        }

        lastTeam1Score = team1Score;
        lastTeam2Score = team2Score;

        // Formater avec couleurs
        string team1Text = $"<color=#{ColorUtility.ToHtmlStringRGB(team1Color)}><b>Jaune {team1Score}</b></color>";
        string team2Text = $"<color=#{ColorUtility.ToHtmlStringRGB(team2Color)}><b>Rouge {team2Score}</b></color>";

        scoreText.text = $"{team1Text} <size=80%>-</size> {team2Text}";

        Debug.Log($"[ScoreHUD] 📊 Score mis à jour : Jaune {team1Score} - {team2Score} Rouge");

        // Mise à jour du statut
        if (ScoreManagerBase.Instance != null)
        {
            int winCondition = ScoreManagerBase.Instance.GetWinConditionPoints();

            if (team1Score >= winCondition)
            {
                UpdateStatus($"🎉 Victoire Équipe Jaune ! ({team1Score}/{winCondition})");
            }
            else if (team2Score >= winCondition)
            {
                UpdateStatus($"🎉 Victoire Équipe Rouge ! ({team2Score}/{winCondition})");
            }
            else
            {
                // Afficher la progression
                int remainingTeam1 = winCondition - team1Score;
                int remainingTeam2 = winCondition - team2Score;
                UpdateStatus($"Jaune à {remainingTeam1} point(s) | Rouge à {remainingTeam2} point(s)");
            }
        }
    }

    /// <summary>
    /// Animation de "pulse" quand le score augmente
    /// </summary>
    private System.Collections.IEnumerator ScorePulseAnimation()
    {
        Vector3 originalScale = scoreText.transform.localScale;
        float elapsed = 0f;

        // Grandir
        while (elapsed < animationDuration / 2)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / (animationDuration / 2);
            scoreText.transform.localScale = Vector3.Lerp(originalScale, originalScale * 1.2f, progress);
            yield return null;
        }

        // Revenir normal
        elapsed = 0f;
        while (elapsed < animationDuration / 2)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / (animationDuration / 2);
            scoreText.transform.localScale = Vector3.Lerp(originalScale * 1.2f, originalScale, progress);
            yield return null;
        }

        scoreText.transform.localScale = originalScale;
    }

    /// <summary>
    /// Mettre à jour le texte de statut
    /// </summary>
    private void UpdateStatus(string status)
    {
        if (statusText != null)
        {
            statusText.text = status;
        }
    }

    /// <summary>
    /// Force la mise à jour de l'affichage (utile pour les tests)
    /// </summary>
    public void ForceUpdate()
    {
        if (ScoreManagerBase.Instance != null)
        {
            var (team1, team2) = ScoreManagerBase.Instance.GetScores();
            UpdateScoreDisplay(team1, team2);
        }
    }
}
