using Fusion;
using TMPro;
using UnityEngine;

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

    private string _shownTeam1Name;
    private string _shownTeam2Name;
    private float _nameRefreshTimer;

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
            // ✨ Rafraîchir le HUD quand un pseudo arrive (il peut arriver APRÈS le premier affichage)
            if (PlayerNamesManager.Instance != null)
                PlayerNamesManager.Instance.OnNameChanged += OnPlayerNameChanged;
            // Debug.Log("[ScoreHUD] ✅ Abonné aux changements de score");
        }
        else
        {
            Debug.LogWarning("[ScoreHUD] ⚠️ ScoreManagerBase.Instance non trouvé! Vérifier que ScoreManager existe en scene.");
        }

        UpdateStatus("Jeu commencé");
    }

    // ✨ Filet de sécurité : les pseudos peuvent arriver après l'affichage (répliqués par Fusion),
    // on revérifie 2 fois par seconde et on redessine seulement si un nom a changé.
    private void Update()
    {
        _nameRefreshTimer -= Time.unscaledDeltaTime;
        if (_nameRefreshTimer > 0f) return;
        _nameRefreshTimer = 0.5f;

        if (ScoreManagerBase.Instance == null) return;
        if (GetTeamName(0) == _shownTeam1Name && GetTeamName(1) == _shownTeam2Name) return;

        var (t1, t2) = ScoreManagerBase.Instance.GetScores();
        UpdateScoreDisplay(t1, t2);
    }

    private void OnPlayerNameChanged(int playerId, string name)
    {
        if (ScoreManagerBase.Instance == null) return;
        var (t1, t2) = ScoreManagerBase.Instance.GetScores();
        UpdateScoreDisplay(t1, t2);
    }

    private void OnDestroy()
    {
        if (PlayerNamesManager.Instance != null)
            PlayerNamesManager.Instance.OnNameChanged -= OnPlayerNameChanged;

        // Se désabonner pour éviter les memory leaks
        if (ScoreManagerBase.Instance != null)
        {
            ScoreManagerBase.Instance.OnScoreChanged -= UpdateScoreDisplay;
        }
    }

    /// <summary>
    /// Callback appelé quand le score change
    /// ✨ FIX: Afficher les pseudonymes des joueurs au lieu de "Jaune" et "Rouge"
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

        // ✨ FIX: Récupérer les pseudonymes des deux équipes
        string team1Name = GetTeamName(0);  // PlayerId impair = Team1
        string team2Name = GetTeamName(1);  // PlayerId pair = Team2
        _shownTeam1Name = team1Name;
        _shownTeam2Name = team2Name;

        // Formater avec couleurs et pseudonymes
        string team1Text = $"<color=#{ColorUtility.ToHtmlStringRGB(team1Color)}><b>{team1Name} {team1Score}</b></color>";
        string team2Text = $"<color=#{ColorUtility.ToHtmlStringRGB(team2Color)}><b>{team2Name} {team2Score}</b></color>";

        scoreText.text = $"{team1Text} <size=80%>-</size> {team2Text}";

        // Debug.Log($"[ScoreHUD] 📊 Score mis à jour : {team1Name} {team1Score} - {team2Score} {team2Name}");

        // Mise à jour du statut
        if (ScoreManagerBase.Instance != null)
        {
            int winCondition = ScoreManagerBase.Instance.GetWinConditionPoints();

            if (team1Score >= winCondition)
            {
                UpdateStatus($"Victoire {team1Name} ! ({team1Score}/{winCondition})");
            }
            else if (team2Score >= winCondition)
            {
                UpdateStatus($"Victoire {team2Name} ! ({team2Score}/{winCondition})");
            }
            else
            {
                // Afficher la progression
                int remainingTeam1 = winCondition - team1Score;
                int remainingTeam2 = winCondition - team2Score;
                UpdateStatus($"{team1Name} à {remainingTeam1} point(s) | {team2Name} à {remainingTeam2} point(s)");
            }
        }
    }

    /// <summary>
    /// ✨ FIX: Récupère le pseudonyme du joueur représentant l'équipe
    /// teamIndex 0 = Team1 (PlayerId impair), 1 = Team2 (PlayerId pair)
    /// </summary>
    private string GetTeamName(int teamIndex)
    {
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null)
        {
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                bool isTeam1 = (player.PlayerId % 2 != 0);
                bool isTargetTeam = (teamIndex == 0) ? isTeam1 : !isTeam1;

                if (isTargetTeam)
                {
                    if (PlayerNameHelper.TryGetRealName(player.PlayerId, out string pseudo))
                        return pseudo;
                }
            }
        }

        // ✅ FIX mode SOLO : il n'y a pas de NetworkRunner actif, donc la boucle ci-dessus ne trouvait
        // jamais personne et le HUD retombait sur "Équipe Jaune" / "Équipe Rouge". En Local, la
        // convention (voir LocalGameSpawner) est : Joueur 1 = humain = Team1, Joueur 2 = IA = Team2,
        // et leurs pseudos sont dans PlayerNamesManager (lu par PlayerNameHelper).
        if (runner == null || !runner.IsRunning)
        {
            int localPlayerId = (teamIndex == 0) ? 1 : 2;
            if (PlayerNameHelper.TryGetRealName(localPlayerId, out string localPseudo))
                return localPseudo;
        }

        // Fallback si aucun joueur trouvé
        return teamIndex == 0 ? "Équipe Jaune" : "Équipe Rouge";
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