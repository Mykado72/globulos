using UnityEngine;

public class LocalGameSpawner : MonoBehaviour
{
    // ✅ FIX : singleton pour que LocalTurnManager puisse déclencher le reset des positions
    public static LocalGameSpawner Instance { get; private set; }

    [Header("Prefabs (GameObject classique)")]
    [SerializeField] private GameObject player1Prefab;
    [SerializeField] private GameObject player2Prefab;
    [SerializeField] private GameObject soccerBallPrefab;

    [Header("Points de Spawn")]
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;
    [SerializeField] private Transform soccerBallSpawnPoint;

    // ✅ FIX : mémorise le point de spawn d'origine de chaque bille pour pouvoir l'y replacer
    private readonly System.Collections.Generic.Dictionary<LocalBallAimController, Vector3> _ballSpawnPositions
        = new System.Collections.Generic.Dictionary<LocalBallAimController, Vector3>();
    private LocalSoccerBallController _soccerBall;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        SpawnLocalGame();
    }

    private void SpawnLocalGame()
    {
        string playerNickname = PlayerPrefs.GetString("playerNickname", "Joueur");

        // 1. Spawn des billes du Joueur Humain (Joueur 1)
        int i = 0;
        foreach (Transform spawnPoint in player1SpawnPoints)
        {
            if (spawnPoint == null) continue;
            i++;

            GameObject ballObj = Instantiate(player1Prefab, spawnPoint.position, Quaternion.identity);
            ballObj.name = $"Player 1_Ball{i}";

            if (ballObj.TryGetComponent(out LocalBallAimController ballController))
            {
                ballController.SetOwner(1);
                _ballSpawnPositions[ballController] = spawnPoint.position; // ✅ FIX
            }
        }
        PlayerNamesManager.Instance?.SetPlayerName(1, playerNickname);

        // 2. Spawn des billes de l'IA (Joueur 2)
        int botPlayerId = 2;
        int j = 0;
        foreach (Transform spawnPoint in player2SpawnPoints)
        {
            if (spawnPoint == null) continue;
            j++;

            GameObject botObj = Instantiate(player2Prefab, spawnPoint.position, Quaternion.identity);
            botObj.name = $"Bot_Ball{j}";

            if (botObj.TryGetComponent(out LocalBallAimController ballController))
            {
                ballController.SetOwner(botPlayerId);
                ballController.SetBotControlled(true);
                _ballSpawnPositions[ballController] = spawnPoint.position; // ✅ FIX
            }
        }

        if (GameModeManager.Instance != null)
        {
            GameModeManager.Instance.BotPlayerId = botPlayerId;
        }
        PlayerNamesManager.Instance?.SetPlayerName(botPlayerId, "🤖 IA");

        // 3. Spawn du ballon de football
        if (soccerBallPrefab != null)
        {
            Vector3 ballPos = soccerBallSpawnPoint != null ? soccerBallSpawnPoint.position : Vector3.zero;
            GameObject soccerBallObj = Instantiate(soccerBallPrefab, ballPos, Quaternion.identity);
            _soccerBall = soccerBallObj.GetComponent<LocalSoccerBallController>(); // ✅ FIX
        }
    }

    // ======================== ✅ FIX : RESET APRÈS UN BUT ========================
    /// <summary>
    /// Replace toutes les billes et le ballon de foot à leur position de spawn d'origine.
    /// Appelée par LocalTurnManager.RequestTurnReset() après un but marqué (ScoreManagerLocal),
    /// pour que la partie reparte visuellement comme un nouveau round, sans recharger la scène.
    /// C'est ce qui manquait en mode Local : le tour se réinitialisait (timer/état) mais
    /// personne ne repositionnait les billes ni le ballon.
    /// </summary>
    public void ResetAllToSpawnPoints()
    {
        foreach (var kvp in _ballSpawnPositions)
        {
            LocalBallAimController ball = kvp.Key;
            if (ball != null)
            {
                ball.ResetForNewRound(kvp.Value);
            }
        }

        if (_soccerBall != null)
        {
            Vector3 ballPos = soccerBallSpawnPoint != null ? soccerBallSpawnPoint.position : Vector3.zero;
            _soccerBall.ResetForNewRound(ballPos);
        }
    }
}