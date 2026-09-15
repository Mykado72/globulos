using Fusion;
using UnityEngine;

public class GameSpawner : NetworkBehaviour, IPlayerJoined
{
    [SerializeField] private NetworkObject player1Prefab;
    [SerializeField] private NetworkObject player2Prefab;
    [SerializeField] private Transform[] player1SpawnPoint;
    [SerializeField] private Transform[] player2SpawnPoint;
    [SerializeField] private NetworkObject soccerBallPrefab;
    [SerializeField] private Transform soccerBallSpawnPoint;

    private bool _hasSpawnedLocalPlayer = false;

    // ✅ Appelé automatiquement par Fusion quand un joueur rejoint la partie
    public void PlayerJoined(PlayerRef player)
    {
        // if (player == Runner.LocalPlayer)
        {
            TrySpawnLocalPlayer();
        }
    }


    public void TrySpawnLocalPlayer()
    {
        if (_hasSpawnedLocalPlayer) return;

        PlayerRef localPlayer = Runner.LocalPlayer;
        if (!localPlayer.IsValid) return;

        // Détermination du numéro de joueur (0 = J1, 1 = J2)
        // Note: On utilise l'index dans Runner.ActivePlayers pour déterminer l'ordre d'arrivée
        int playerIndex = 0;
        // Si vous jouez à 2 joueurs : le premier arrivé (Master) est J1 (0), le second est J2 (1)
        NetworkObject prefabToSpawn;
        Transform[] spawnPoints;
        if (!Runner.IsSharedModeMasterClient)
        {
            playerIndex = 2;
            prefabToSpawn = player2Prefab;
            spawnPoints = player2SpawnPoint;
        }
        else
        {
            playerIndex = 0;
            prefabToSpawn = player1Prefab;
            spawnPoints = player1SpawnPoint;
        }

        Transform spawnPoint = null;
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                spawnPoint = spawnPoints[i];
                if (prefabToSpawn == null)
                {
                    Debug.LogError($"[GameSpawner] ❌ Le prefab pour le Joueur {playerIndex + 1} n'est pas assigné dans l'Inspecteur !");
                    return;
                }

                Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
                // Spawn de la bille du joueur local
                NetworkObject spawnedObj = Runner.Spawn(
                    prefabToSpawn,
                    spawnPos,
                    Quaternion.identity,
                    inputAuthority: localPlayer
                );
                if (spawnedObj != null)
                {
                    _hasSpawnedLocalPlayer = true;

                    // Correction CS0411 : Spécification explicite du type générique pour TryGetComponent
                    if (spawnedObj.TryGetComponent<BallAimController>(out var ball))
                    {
                        ball.SetOwner(localPlayer.PlayerId);
                    }

                    Debug.Log($"[GameSpawner] ✅ Bille du Joueur {playerIndex + 1} (PlayerId: {localPlayer.PlayerId}) spawnée au bon emplacement !");
                }
            }
        }
        else
        {
            spawnPoint = null;
        }
    }

    
    public override void Spawned()
    {
        Debug.Log("[GameSpawner] GameSpawner activé sur le réseau !");

        // 1. Spawner le ballon de foot (une seule fois par la State Authority / Premier arrivé)
        if (HasStateAuthority && soccerBallPrefab != null)
        {
            Vector3 pos = soccerBallSpawnPoint != null ? soccerBallSpawnPoint.position : Vector3.zero;
            Runner.Spawn(soccerBallPrefab, pos, Quaternion.identity);
            Debug.Log("[GameSpawner] Ballon de foot spawné par le Master/StateAuthority.");
        }

        // 2. Le joueur est déjà connecté quand la scène charge : on force le spawn de sa bille
        TrySpawnLocalPlayer();
    }    

}