using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;

public class GameSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef player1Prefab;
    [SerializeField] private NetworkPrefabRef player2Prefab;
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;
    [SerializeField] private NetworkPrefabRef soccerBallPrefab;
    [SerializeField] private Transform soccerBallSpawnPoint;

    private bool _isSpawning = false; // Flag anti-relecture immédiat
    private bool _hasSpawnedLocalPlayer = false;
    private bool _hasSpawnedBall = false;
    private NetworkRunner _runner;

    private void Start()
    {
        _runner = FindObjectOfType<NetworkRunner>();
        if (_runner != null)
        {
            _runner.AddCallbacks(this);
            Debug.Log("[GameSpawner] Runner trouvé et enregistré");
        }
        else
        {
            Debug.LogError("[GameSpawner] ❌ Aucun NetworkRunner trouvé !");
        }
    }

    // ✅ NOUVEAU : Vérifie chaque frame si le Runner est prêt
    private void Update()
    {
        if (!_hasSpawnedLocalPlayer && _runner != null && _runner.IsRunning)
        {
            Debug.Log("[GameSpawner] 🎮 Runner prêt, tentative de spawn...");
            TrySpawnLocalPlayer(_runner);
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
        Debug.Log("[GameSpawner] 📍 OnSceneLoadDone (appelé par Master Client)");

        // Master Client spawne le ballon (une seule fois)
        if (runner.IsSharedModeMasterClient && !_hasSpawnedBall)
        {
            _hasSpawnedBall = true;
            SpawnSoccerBall(runner);
            Debug.Log("[GameSpawner] ⚽ Master Client a spawné le ballon");
        }
    }

    private async void TrySpawnLocalPlayer(NetworkRunner runner)
    {
        // Verrouillage immédiat pour éviter les déclenchements multiples via Update
        if (_hasSpawnedLocalPlayer || _isSpawning) return;
        _isSpawning = true;

        PlayerRef localPlayer = runner.LocalPlayer;
        if (!localPlayer.IsValid)
        {
            _isSpawning = false;
            return;
        }

        // Le Master Client (premier connecté) prend le prefab 1, le second prend le prefab 2
        bool isPlayer1 = runner.IsSharedModeMasterClient;
        NetworkPrefabRef prefab = isPlayer1 ? player1Prefab : player2Prefab;
        Transform[] spawnPoints = isPlayer1 ? player1SpawnPoints : player2SpawnPoints;

        Debug.Log($"[GameSpawner] 👤 Début du spawn pour le Joueur {(isPlayer1 ? 1 : 2)}");

        if (prefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[GameSpawner] ❌ Prefab ou SpawnPoints manquants !");
            _isSpawning = false;
            return;
        }

        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint == null) continue;

            try
            {
                NetworkObject spawnedBall = await runner.SpawnAsync(
                    prefab,
                    spawnPoint.position,
                    Quaternion.identity,
                    inputAuthority: localPlayer
                );

                if (spawnedBall != null)
                {
                    // ✅ Spécification explicite du composant BallAimController
                    if (spawnedBall.TryGetComponent(out BallAimController ballController))
                    {
                        ballController.SetOwner(localPlayer.PlayerId);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameSpawner] ❌ Exception Spawn: {ex.Message}");
            }
        }

        _hasSpawnedLocalPlayer = true;
        _isSpawning = false;
        Debug.Log("[GameSpawner] ✅ Spawn terminé !");
    }

    private void SpawnSoccerBall(NetworkRunner runner)
    {
        if (soccerBallPrefab == null)
        {
            Debug.LogWarning("[GameSpawner] ⚠️ Ballon prefab non assigné");
            return;
        }

        Vector3 pos = soccerBallSpawnPoint != null ? soccerBallSpawnPoint.position : Vector3.zero;

        try
        {
            NetworkObject ball = runner.Spawn(soccerBallPrefab, pos, Quaternion.identity);

            if (ball != null)
            {
                Debug.Log("[GameSpawner] ⚽ Ballon spawné avec succès");
            }
            else
            {
                Debug.LogError("[GameSpawner] ❌ Spawn du ballon a retourné null !");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameSpawner] ❌ Exception lors du spawn du ballon : {ex.Message}");
        }
    }

    // =========================================================================
    // Callbacks INetworkRunnerCallbacks
    // =========================================================================

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[GameSpawner] 👤 Joueur {player.PlayerId} a rejoint");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[GameSpawner] 👤 Joueur {player.PlayerId} a quitté");
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}