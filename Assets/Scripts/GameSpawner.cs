using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;
using UnityEngine;

/// ✅ SHARED MODE - WEBGL OPTIMIZED
/// Chaque joueur spawne ses propres boules.
/// Master Client spawne le ballon de foot.
/// Pas de complexité Client/Server inutile.
public class GameSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef player1Prefab;
    [SerializeField] private NetworkPrefabRef player2Prefab;
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;
    [SerializeField] private NetworkPrefabRef soccerBallPrefab;
    [SerializeField] private Transform soccerBallSpawnPoint;

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

    private void OnDisable()
    {
        if (_runner != null)
        {
            _runner.RemoveCallbacks(this);
        }
    }

    // ✅ CALLBACK FUSION : Quand la scène est chargée
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("[GameSpawner] 📍 OnSceneLoadDone");

        // ✅ Master Client spawne le ballon (une seule fois)
        if (runner.IsSharedModeMasterClient && !_hasSpawnedBall)
        {
            _hasSpawnedBall = true;
            SpawnSoccerBall(runner);
            Debug.Log("[GameSpawner] ⚽ Master Client a spawné le ballon de foot");
        }

        // Chaque joueur spawne ses propres boules
        TrySpawnLocalPlayer(runner);
    }

    // ✅ CALLBACK FUSION : Quand un joueur rejoint
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        string playerName = PlayerNamesManager.Instance?.GetPlayerName(player) ?? $"Joueur {player.PlayerId}";
        Debug.Log($"[GameSpawner] 👤 {playerName} a rejoint");
        UIManager.Instance?.ShowMessage($"✅ {playerName} a rejoint la partie", 3f);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        string playerName = PlayerNamesManager.Instance?.GetPlayerName(player) ?? $"Joueur {player.PlayerId}";
        Debug.Log($"[GameSpawner] 👤 {playerName} a quitté");
        UIManager.Instance?.ShowMessage($"❌ {playerName} a quitté la partie !", 5f);
    }

    private void TrySpawnLocalPlayer(NetworkRunner runner)
    {
        if (_hasSpawnedLocalPlayer) return;

        PlayerRef localPlayer = runner.LocalPlayer;
        if (!localPlayer.IsValid)
        {
            Debug.LogWarning("[GameSpawner] ⚠️ LocalPlayer pas valide");
            return;
        }

        // ✅ Détermine qui on est :
        // - Master Client (premier arrivé) = PlayerId 0
        // - Autre = PlayerId 1
        bool isMaster = runner.IsSharedModeMasterClient;
        int playerIndex = isMaster ? 0 : 1;
        NetworkPrefabRef prefab = isMaster ? player1Prefab : player2Prefab;
        Transform[] spawnPoints = isMaster ? player1SpawnPoints : player2SpawnPoints;

        Debug.Log($"[GameSpawner] Spawn pour joueur {playerIndex} (Master: {isMaster})");

        if (prefab == null)
        {
            Debug.LogError($"[GameSpawner] ❌ Prefab pour joueur {playerIndex} est null !");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[GameSpawner] ❌ Spawn points pour joueur {playerIndex} est vide !");
            return;
        }

        // Spawn chaque boule
        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint == null) continue;

            Vector3 pos = spawnPoint.position;

            try
            {
                // ✅ Chaque client spawne ses boules avec InputAuthority sur lui-même
                NetworkObject spawnedBall = runner.Spawn(
                    prefab,
                    pos,
                    Quaternion.identity,
                    inputAuthority: localPlayer  // ← Ce joueur contrôle cette boule
                );

                if (spawnedBall != null)
                {
                    // ✅ Enregistrer le propriétaire dans le composant
                    if (spawnedBall.TryGetComponent<BallAimController>(out var ball))
                    {
                        ball.SetOwner(localPlayer.PlayerId);
                    }

                    Debug.Log($"[GameSpawner] ✅ Boule spawnée pour joueur {playerIndex} à {pos}");
                }
                else
                {
                    Debug.LogError("[GameSpawner] ❌ Spawn a retourné null !");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameSpawner] ❌ Exception lors du spawn : {ex.Message}");
            }
        }

        _hasSpawnedLocalPlayer = true;
        Debug.Log($"[GameSpawner] ✅ Boules du joueur {playerIndex} spawnées avec succès");
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
            // ✅ Master Client spawne le ballon (InputAuthority = none, State Authority = Master)
            NetworkObject ball = runner.Spawn(soccerBallPrefab, pos, Quaternion.identity);

            if (ball != null)
            {
                Debug.Log("[GameSpawner] ⚽ Ballon de foot spawné avec succès");
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
    // IMPLÉMENTATION INetworkRunnerCallbacks (obligatoire)
    // =========================================================================

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"[GameSpawner] 🌐 Déconnecté: {reason}");
        UIManager.Instance?.ShowMessagePermanent($"⚠️ Déconnecté: {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.Log($"[GameSpawner] ❌ Connexion échouée: {reason}");
        UIManager.Instance?.ShowMessagePermanent($"❌ Connexion échouée: {reason}");
    }

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