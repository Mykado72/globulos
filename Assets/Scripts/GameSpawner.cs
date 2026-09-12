using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef ballPrefab;

    [Header("Spawn Positions")]
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;

    private Dictionary<PlayerRef, List<NetworkObject>> _spawnedBalls = new Dictionary<PlayerRef, List<NetworkObject>>();
    private bool _hasSpawned = false;
    private bool _isRegistered = false;
    private NetworkRunner _runner;

    // private NetworkRunner Runner => FindObjectOfType<NetworkRunner>();


    private void Start()
    {
        Debug.Log("[GameSpawner] Start() exécuté !");

        // Recherche du NetworkRunner persistant dans la scène
        _runner = FindObjectOfType<NetworkRunner>();

        if (_runner != null)
        {
            _runner.AddCallbacks(this);
            Debug.Log($"[GameSpawner] Runner trouvé. IsMaster: {_runner.IsSharedModeMasterClient}");
        }
        else
        {
            Debug.LogError("[GameSpawner] ERREUR : Aucun NetworkRunner trouvé dans la GameScene !");
        }
    }

    private void Update()
    {
        // On s'enregistre dès que le Runner devient disponible dans la scène
        if (!_isRegistered && _runner != null)
        {
            _isRegistered = true;
            _runner.AddCallbacks(this);
            Debug.Log($"[GameSpawner] Enregistré auprès du NetworkRunner. IsMaster: {_runner.IsSharedModeMasterClient}");

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
        TrySpawnBalls();
    }

    private void TrySpawnBalls()
    {
        if (_hasSpawned) return;

        Debug.Log($"[GameSpawner] TrySpawnBalls - Runner: {(_runner != null)}, IsMaster: {_runner?.IsSharedModeMasterClient}, Players: {_runner?.ActivePlayers.Count()}");

        _hasSpawned = true;
        var players = _runner.ActivePlayers.ToList();

        if (players.Count > 0)
        {
            SpawnForPlayer(players[0], player1SpawnPoints);
        }

        if (players.Count > 1)
        {
            SpawnForPlayer(players[1], player2SpawnPoints);
        }
    }

    private void SpawnForPlayer(PlayerRef player, Transform[] spawnPoints)
    {
        List<NetworkObject> playerBalls = new List<NetworkObject>();
        Debug.Log($"On essai de spawn {player.PlayerId}");
        foreach (Transform spawnPoint in spawnPoints)
        {
            NetworkObject ball = _runner.Spawn(ballPrefab, spawnPoint.position, Quaternion.identity, player);
            playerBalls.Add(ball);
            Debug.Log($"[GameSpawner] Spawned {playerBalls.Count} balls for player {player.PlayerId}");
        }
        
        _spawnedBalls.Add(player, playerBalls);
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