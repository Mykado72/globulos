using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ✅ Campagne solo : une suite de niveaux, chaque niveau étant une SCÈNE.
///
/// Dans chaque scène de niveau (copie de GameSceneLocal), tout se règle dans l'Inspector :
///   - disposition du terrain, points de spawn, buts ;
///   - BotSpawnPoint sur chaque point de spawn des bots (stratégie individuelle) ;
///   - ScoreManagerLocal > Win Condition Points (buts nécessaires pour gagner) ;
///   - LocalTurnManager > Aim Duration (temps de visée).
///
/// À placer UNE fois dans la scène du Lobby (comme GameModeManager) : il survit aux changements
/// de scène (DontDestroyOnLoad). Les scènes de niveau doivent être dans Build Settings.
/// </summary>
public class CampaignManager : MonoBehaviour
{
    [System.Serializable]
    public class Level
    {
        [Tooltip("Nom exact de la scène (Build Settings).")]
        public string sceneName;
        [Tooltip("Nom affiché (menu de sélection, intro de niveau...).")]
        public string displayName;
    }

    public static CampaignManager Instance { get; private set; }

    [SerializeField] private Level[] levels;

    [Tooltip("Scène chargée quand tous les niveaux sont terminés.")]
    [SerializeField] private string menuSceneName = "Lobby";

    private const string UnlockedKey = "campaignUnlockedLevels";

    /// <summary>true tant qu'une campagne est en cours (LocalTurnManager s'y fie à la fin de partie).</summary>
    public bool IsCampaign { get; private set; }
    public int CurrentLevelIndex { get; private set; }
    public int LevelCount => levels != null ? levels.Length : 0;
    public Level CurrentLevel => (levels != null && CurrentLevelIndex >= 0 && CurrentLevelIndex < levels.Length) ? levels[CurrentLevelIndex] : null;

    /// <summary>Nombre de niveaux jouables (le 1er est toujours débloqué).</summary>
    public int UnlockedLevelCount => Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 1), 1, Mathf.Max(1, LevelCount));

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>Reprend au dernier niveau débloqué (bouton "Campagne").</summary>
    public void ContinueCampaign() => StartLevel(UnlockedLevelCount - 1);

    /// <summary>Repart du niveau 1.</summary>
    public void StartCampaign() => StartLevel(0);

    /// <summary>Lance un niveau précis (écran de sélection de niveau). Ignoré s'il n'est pas débloqué.</summary>
    public void StartLevel(int index)
    {
        if (LevelCount == 0)
        {
            Debug.LogWarning("[CampaignManager] ⚠️ Aucun niveau défini dans l'Inspector !");
            return;
        }

        index = Mathf.Clamp(index, 0, LevelCount - 1);
        if (index >= UnlockedLevelCount)
        {
            Debug.LogWarning($"[CampaignManager] Niveau {index + 1} pas encore débloqué.");
            return;
        }

        IsCampaign = true;
        CurrentLevelIndex = index;

        // Le mode campagne est un mode "solo vs IA" : même drapeau que depuis le Lobby.
        if (GameModeManager.Instance != null) GameModeManager.Instance.IsVsAI = true;

        SceneManager.LoadScene(levels[index].sceneName);
    }

    /// <summary>
    /// Appelé par LocalTurnManager à la fin d'un niveau (après le délai d'affichage du résultat).
    /// Victoire : débloque et lance le niveau suivant (ou termine la campagne). Sinon : on rejoue le niveau.
    /// </summary>
    public void OnLevelFinished(bool playerWon)
    {
        if (!playerWon)
        {
            SceneManager.LoadScene(levels[CurrentLevelIndex].sceneName);
            return;
        }

        int next = CurrentLevelIndex + 1;
        if (next >= LevelCount)
        {
            Debug.Log("[CampaignManager] 🏆 Campagne terminée !");
            EndCampaign();
            return;
        }

        if (next + 1 > UnlockedLevelCount)
        {
            PlayerPrefs.SetInt(UnlockedKey, next + 1);
            PlayerPrefs.Save();
        }

        CurrentLevelIndex = next;
        SceneManager.LoadScene(levels[next].sceneName);
    }

    /// <summary>Quitte la campagne et retourne au menu (à brancher sur un bouton "Quitter" si besoin).</summary>
    public void EndCampaign()
    {
        IsCampaign = false;
        SceneManager.LoadScene(menuSceneName);
    }

    public void ResetProgress()
    {
        PlayerPrefs.DeleteKey(UnlockedKey);
        PlayerPrefs.Save();
    }
}
