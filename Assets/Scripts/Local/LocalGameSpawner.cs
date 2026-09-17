using UnityEngine;

public class LocalGameSpawner : MonoBehaviour
{
    [Header("Prefabs (GameObject classique)")]
    [SerializeField] private GameObject player1Prefab;
    [SerializeField] private GameObject player2Prefab;
    [SerializeField] private GameObject soccerBallPrefab;

    [Header("Points de Spawn")]
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;
    [SerializeField] private Transform soccerBallSpawnPoint;

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

            if (ballObj.TryGetComponent(out BallAimController ballController))
            {
                ballController.SetOwner(1);
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

            if (botObj.TryGetComponent(out BallAimController ballController))
            {
                ballController.SetOwner(botPlayerId);
                ballController.SetBotControlled(true);
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
            Instantiate(soccerBallPrefab, ballPos, Quaternion.identity);
        }
    }
}