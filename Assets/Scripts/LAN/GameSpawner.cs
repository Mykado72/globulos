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

    // 🆕 Handshake "tous les joueurs prêts" : ne sert plus à retarder le spawn du
    // joueur local (qui est désormais immédiat), uniquement à déclencher le spawn
    // du ballon / le vrai début de partie une fois que TOUT le monde a spawné.
    private bool _hasNotifiedReady = false;
    private bool _allPlayersReadyToSpawn = false;

    private void OnEnable()
    {
        // Abonnement à l'événement diffusé par TurnManager (via RPC) quand TOUS les
        // clients ont signalé être prêts. Ne déclenche plus que le spawn du ballon.
        TurnManager.OnAllPlayersReadyToSpawn += HandleAllPlayersReadyToSpawn;
    }

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
        TurnManager.OnAllPlayersReadyToSpawn -= HandleAllPlayersReadyToSpawn;

        if (_runner != null)
        {
            _runner.RemoveCallbacks(this);
        }
    }

    // ✅ Déclenché AUTOMATIQUEMENT quand la scène de jeu a fini de charger sur ce client
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log($"[GameSpawner] 🎬 Scène chargée pour LocalPlayer ID : {runner.LocalPlayer.PlayerId}");

        // ✅ Le spawn du joueur local est immédiat et ne dépend plus des autres
        // clients. Le ballon, lui, ne spawn QUE via HandleAllPlayersReadyToSpawn,
        // une fois que TurnManager confirme que tout le monde a spawné.
        _ = TrySpawnLocalPlayerWithRetry();
    }

    /// <summary>
    /// Appelé quand TurnManager confirme que TOUS les clients ont spawné leur
    /// joueur local et signalé leur disponibilité. C'est le vrai point de départ
    /// du spawn du ballon côté Master, et donc du vrai début de partie.
    /// </summary>
    private void HandleAllPlayersReadyToSpawn()
    {
        _allPlayersReadyToSpawn = true;

        if (_runner != null && _runner.IsSharedModeMasterClient && !_hasSpawnedSoccerBall)
        {
            _hasSpawnedSoccerBall = true;
            SpawnSoccerBall(_runner);
        }
    }

    /// <summary>
    /// Attend que le joueur local soit 100% valide dans la session Fusion, le
    /// spawn IMMÉDIATEMENT (sans attendre l'autre client), puis notifie
    /// TurnManager en arrière-plan (fire-and-forget) pour faire avancer le
    /// compteur "tous prêts" qui déclenchera le spawn du ballon.
    /// </summary>
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
            await WebGLDelay.Wait(0.1f, this);
        }

        if (_hasSpawnedLocalPlayer || _runner == null || !_runner.IsRunning) return;

        _hasSpawnedLocalPlayer = true;
        PlayerRef localPlayer = _runner.LocalPlayer;

        // Déterminer s'il s'agit du Joueur 1 (Master) ou Joueur 2
        bool isPlayer1 = _runner.IsSharedModeMasterClient;

        NetworkPrefabRef prefab = isPlayer1 ? player1Prefab : player2Prefab;
        Transform[] spawnPoints = isPlayer1 ? player1SpawnPoints : player2SpawnPoints;

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

        Debug.Log("[GameSpawner] ✅ Joueur local spawné. Notification de disponibilité à TurnManager (arrière-plan)...");

        // ✅ Fire-and-forget : cet appel ne bloque plus rien ici. Il ne sert qu'à
        // faire avancer le compteur de TurnManager (PlayersReadyToSpawn) pour que
        // RPC_BeginGameplay se déclenche une fois que les DEUX joueurs ont spawné.
        _ = NotifyReadyWhenTurnManagerAvailable();
    }

    /// <summary>
    /// Attend juste que TurnManager.Instance existe (répliqué depuis le State
    /// Authority), puis signale une seule fois la disponibilité de ce client.
    /// N'attend PLUS que l'autre joueur soit prêt : c'est le rôle exclusif de
    /// TurnManager / HandleAllPlayersReadyToSpawn de déclencher la suite
    /// (spawn du ballon, début de partie) une fois que tout le monde a signalé.
    /// </summary>
    private async Task NotifyReadyWhenTurnManagerAvailable()
    {
        if (_hasNotifiedReady) return;

        int waitAttempts = 0;
        while (TurnManager.Instance == null)
        {
            waitAttempts++;
            if (waitAttempts > 50) // Timeout après ~5 secondes
            {
                Debug.LogError("[GameSpawner] ❌ TurnManager.Instance introuvable après attente.");
                return;
            }
            await WebGLDelay.Wait(0.1f, this);
        }

        if (_hasNotifiedReady || _runner == null) return;

        _hasNotifiedReady = true;
        TurnManager.Instance.NotifyPlayerReadyToSpawn(_runner.LocalPlayer);
    }

    private void SpawnSoccerBall(NetworkRunner runner)
    {
        if (soccerBallPrefab == null) return;

        Vector3 spawnPos = soccerBallSpawnPoint != null ? soccerBallSpawnPoint.position : Vector3.zero;

        try
        {
            runner.Spawn(soccerBallPrefab, spawnPos, Quaternion.identity);
            // Debug.Log("[GameSpawner] ⚽ Ballon de football spawné sur le terrain.");
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
