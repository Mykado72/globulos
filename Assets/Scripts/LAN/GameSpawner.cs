using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Prefabs")]
    [SerializeField] private NetworkPrefabRef player1Prefab;
    [SerializeField] private NetworkPrefabRef player2Prefab;
    [SerializeField] private NetworkPrefabRef soccerBallPrefab;

    [Header("Spawn Points")]
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;
    [SerializeField] private Transform soccerBallSpawnPoint;

    private bool _hasSpawnedLocalPlayer = false;
    private bool _hasSpawnedSoccerBall = false;
    private NetworkRunner _runner;

    private void Start()
    {
        _runner = FindFirstObjectByType<NetworkRunner>();
        if (_runner != null)
        {
            _runner.AddCallbacks(this);

            // Si le Runner est déjà actif et en jeu (ex: cas où la scène est déjà chargée)
            if (_runner.IsRunning && _runner.LocalPlayer.IsValid)
            {
                _ = TrySpawnLocalPlayerWithRetry();
            }
        }
    }

    private void OnDisable()
    {
        if (_runner != null)
        {
            _runner.RemoveCallbacks(this);
        }
    }

    // ✅ Déclenché AUTOMATIQUEMENT quand la scène de jeu a fini de charger sur ce client
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log($"[GameSpawner] 🎬 Scène chargée pour LocalPlayer ID : {runner.LocalPlayer.PlayerId}");

        // 1. Tenter de spawner le joueur local
        _ = TrySpawnLocalPlayerWithRetry();

        // 2. Le Master Client spawn le ballon
        if (runner.IsSharedModeMasterClient && !_hasSpawnedSoccerBall)
        {
            _hasSpawnedSoccerBall = true;
            SpawnSoccerBall(runner);
        }
    }

    /// 
    /// Attend que le joueur local soit 100% valide dans la session Fusion avant d'instancier.
    /// 
    private async Task TrySpawnLocalPlayerWithRetry()
    {
        if (_hasSpawnedLocalPlayer) return;

        int attempts = 0;
        // Boucle d'attente pour s'assurer que Fusion a attribué un PlayerId valide au client
        while (_runner != null && _runner.IsRunning && (!_runner.LocalPlayer.IsValid || _runner.LocalPlayer.PlayerId == 0))
        {
            attempts++;
            if (attempts > 50) // Timeout après ~5 secondes
            {
                Debug.LogError("[GameSpawner] ❌ Timeout : LocalPlayer toujours invalide.");
                return;
            }
            await Task.Delay(100);
        }

        if (_hasSpawnedLocalPlayer || _runner == null || !_runner.IsRunning) return;

        _hasSpawnedLocalPlayer = true;
        PlayerRef localPlayer = _runner.LocalPlayer;

        // Déterminer s'il s'agit du Joueur 1 (Master) ou Joueur 2
        bool isPlayer1 = _runner.IsSharedModeMasterClient;

        NetworkPrefabRef prefab = isPlayer1 ? player1Prefab : player2Prefab;
        Transform[] spawnPoints = isPlayer1 ? player1SpawnPoints : player2SpawnPoints;

        Debug.Log($"[GameSpawner] 🚀 Spawning des billes pour {(isPlayer1 ? "JOUEUR 1" : "JOUEUR 2")} (ID: {localPlayer.PlayerId})");

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[GameSpawner] ❌ Erreur : SpawnPoints non assignés pour {(isPlayer1 ? "Joueur 1" : "Joueur 2")} !");
            return;
        }

        // Récupération du pseudo
        string nickname = $"Joueur {localPlayer.PlayerId}";
        byte[] token = _runner.GetPlayerConnectionToken(localPlayer);
        if (token != null && token.Length > 0)
        {
            nickname = System.Text.Encoding.UTF8.GetString(token);
        }

        int index = 0;
        foreach (Transform spawnPoint in spawnPoints)
        {
            index++;
            if (spawnPoint == null) continue;

            try
            {
                // Spawn des billes avec Input Authority attribué au joueur local
                NetworkObject ballObj = await _runner.SpawnAsync(
                    prefab,
                    spawnPoint.position,
                    Quaternion.identity,
                    inputAuthority: localPlayer
                );

                if (ballObj != null)
                {
                    ballObj.name = $"Player_{localPlayer.PlayerId}_Ball_{index}";

                    if (ballObj.TryGetComponent(out BallAimController aimController))
                    {
                        aimController.SetOwner(localPlayer.PlayerId);
                    }

                    if (ballObj.TryGetComponent(out PlayerData playerData))
                    {
                        playerData.RPC_SetPlayerInfo(nickname, localPlayer.PlayerId);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameSpawner] ❌ Erreur lors du spawn d'une bille ({index}) : {ex.Message}");
            }
        }

        Debug.Log($"[GameSpawner] ✅ Succès : Toutes les billes du Joueur {localPlayer.PlayerId} ont été créées !");
    }

    private void SpawnSoccerBall(NetworkRunner runner)
    {
        if (soccerBallPrefab == null) return;

        Vector3 spawnPos = soccerBallSpawnPoint != null ? soccerBallSpawnPoint.position : Vector3.zero;

        try
        {
            runner.Spawn(soccerBallPrefab, spawnPos, Quaternion.identity);
            Debug.Log("[GameSpawner] ⚽ Ballon de football spawné sur le terrain.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameSpawner] ❌ Erreur lors du spawn du ballon : {ex.Message}");
        }
    }

    #region Fusion Callbacks

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // Si c'est le joueur local qui rejoint
        if (player == runner.LocalPlayer)
        {
            _ = TrySpawnLocalPlayerWithRetry();
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

    #endregion
}