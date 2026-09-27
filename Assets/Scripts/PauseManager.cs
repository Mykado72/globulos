using UnityEngine;
public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }

    private ITurnManagerCore _turnManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // ✅ Détecte automatiquement quel type de TurnManager est actif
        _turnManager = TurnManagerFactory.GetTurnManager();

        if (_turnManager == null)
        {
            Debug.LogWarning("⚠️ Aucun TurnManager trouvé!");
        }
    }

    /// <summary>
    /// Appelée par le HTML en mode portrait
    /// Fonctionne en Local ET en Réseau (LAN)
    /// </summary>
    public void PauseGame()
    {
        Time.timeScale = 0f;
        Debug.Log("⏸️ Jeu en pause (mode portrait)");
    }

    /// <summary>
    /// Appelée par le HTML en mode paysage
    /// Fonctionne en Local ET en Réseau (LAN)
    /// </summary>
    public void ResumeGame()
    {
        Time.timeScale = 1f;
        Debug.Log("▶️ Jeu repris (mode paysage)");
    }
}