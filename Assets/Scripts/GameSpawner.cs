using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef PlayerJaunePrefab;
    [SerializeField] private NetworkPrefabRef PlayerRougePrefab;

    [Header("Spawn Positions")]
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;

    private Dictionary<PlayerRef, List<NetworkObject>> _spawnedBalls = new Dictionary<PlayerRef, List<NetworkObject>>();
    private bool _hasSpawned = false;
    private NetworkRunner _runner;

    private void Start()
    {
        Debug.Log("[GameSpawner] Start() exécuté !");

        // Recherche du NetworkRunner dans la scène
        _runner = FindObjectOfType<NetworkRunner>();

        if (_runner != null)
        {
            _runner.AddCallbacks(this);
            Debug.Log($"[GameSpawner] Runner trouvé et enregistré. IsMaster: {_runner.IsSharedModeMasterClient}");
        }
        else
        {
            Debug.LogError("[GameSpawner] ERREUR : Aucun NetworkRunner trouvé dans la GameScene !");
        }
    }

    private void OnDisable()
    {
        if (_runner != null)
        {
            _runner.RemoveCallbacks(this);
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("[GameSpawner] Callback OnSceneLoadDone reçu !");
        TrySpawnBalls(runner);
    }

    private void TrySpawnBalls(NetworkRunner runner)
    {
        if (_hasSpawned) return;

        // ✅ CORRECTIF : en Shared Mode, StateAuthority == InputAuthority == celui qui appelle Spawn().
        // Il est donc IMPOSSIBLE de donner l'autorité au joueur 2 en spawnant depuis le master.
        // -> Chaque client doit spawn SES PROPRES boules, localement, une fois la scène chargée.
        _hasSpawned = true;

        bool isMaster = runner.IsSharedModeMasterClient;
        Transform[] spawnPoints = isMaster ? player1SpawnPoints : player2SpawnPoints;
        NetworkPrefabRef boulePrefab = isMaster ? PlayerJaunePrefab : PlayerRougePrefab;

        Debug.Log($"[GameSpawner] Je spawn mes propres boules - LocalPlayer: {runner.LocalPlayer.PlayerId}, IsMaster: {isMaster}");

        SpawnForPlayer(runner, runner.LocalPlayer, spawnPoints, boulePrefab);
    }

    private void SpawnForPlayer(NetworkRunner runner, PlayerRef player, Transform[] spawnPoints, NetworkPrefabRef boulePrefab)
    {
        List<NetworkObject> playerBalls = new List<NetworkObject>();

        foreach (Transform spawnPoint in spawnPoints)
        {
            // ✅ Spawn avec InputAuthority = le joueur propriétaire
            NetworkObject ball = runner.Spawn(boulePrefab, spawnPoint.position, Quaternion.identity, player);
            playerBalls.Add(ball);

            Debug.Log($"[GameSpawner] Boule spawned pour joueur {player.PlayerId} - InputAuthority: {player.PlayerId}");
        }

        _spawnedBalls.Add(player, playerBalls);
        Debug.Log($"[GameSpawner] Total boules pour joueur {player.PlayerId}: {playerBalls.Count}");
    }

    // --- Implémentation des callbacks INetworkRunnerCallbacks (Fusion 2.1.2) ---
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}