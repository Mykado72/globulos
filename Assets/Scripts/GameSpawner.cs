using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

public class GameSpawner : SimulationBehaviour, IPlayerJoined
{
    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef ballPrefab;

    [Header("Spawn Positions")]
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;

    // Correction : Déclaration correcte du dictionnaire pour stocker les balles spawnées par joueur.
    // Utilise les bons types génériques pour Dictionary et List.
    private Dictionary<PlayerRef, List<NetworkObject>> _spawnedBalls = new Dictionary<PlayerRef, List<NetworkObject>>();

    public void PlayerJoined(PlayerRef player)
    {
        if (Runner.IsSharedModeMasterClient)
        {
            Transform[] spawnPoints = (Runner.ActivePlayers.Count() == 1) ? player1SpawnPoints : player2SpawnPoints;

            List<NetworkObject> playerBalls = new List<NetworkObject>();

            foreach (Transform spawnPoint in spawnPoints)
            {
                NetworkObject ball = Runner.Spawn(ballPrefab, spawnPoint.position, Quaternion.identity, player);
                playerBalls.Add(ball);
            }

            _spawnedBalls.Add(player, playerBalls);
        }
    }
}