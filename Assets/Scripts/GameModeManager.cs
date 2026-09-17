using UnityEngine;
using UnityEngine.SceneManagement;

/// ✅ Transporte le mode de jeu choisi au Lobby (vs IA ou multijoueur normal)
/// jusqu'à la GameScene, où GameSpawner et SoccerBallController en ont besoin.
/// Même pattern singleton + DontDestroyOnLoad que PlayerNamesManager / AudioManager.
public class GameModeManager : MonoBehaviour
{
    public static GameModeManager Instance { get; private set; }

    public bool IsVsAI { get; set; } = false;

    // ✅ PlayerId attribué au bot une fois spawné. Comme le bot n'est pas un vrai
    // PlayerRef réseau, SoccerBallController et TurnManager en ont besoin pour
    // savoir "qui" a marqué / afficher son nom.
    public int BotPlayerId { get; set; } = -1;

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

    public void ResetForNewSession()
    {
        IsVsAI = false;
        BotPlayerId = -1;
    }

    public void StartSceneGameLocal()
    {
        SceneManager.LoadScene("GameSceneLocal");
    }
}
