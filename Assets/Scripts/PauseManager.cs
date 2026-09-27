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

        // ✅ FIX : en mode réseau, il faut aussi propager la pause aux autres joueurs
        // (synchroniser IsGamePaused + afficher "X a mis en pause" chez l'adversaire).
        // Time.timeScale seul n'est que local à cet appareil et ne fait rien de tout ça.
        if (_turnManager is TurnManager networkTurnManager && networkTurnManager.Runner != null)
        {
            networkTurnManager.PauseGameNetwork(networkTurnManager.Runner.LocalPlayer.PlayerId);
        }
    }

    /// <summary>
    /// Appelée par le HTML en mode paysage
    /// Fonctionne en Local ET en Réseau (LAN)
    /// </summary>
    public void ResumeGame()
    {
        Time.timeScale = 1f;
        Debug.Log("▶️ Jeu repris (mode paysage)");

        // ✅ FIX : idem, propage la reprise aux autres joueurs en mode réseau.
        if (_turnManager is TurnManager networkTurnManager && networkTurnManager.Runner != null)
        {
            networkTurnManager.ResumeGameNetwork();
        }
    }
}