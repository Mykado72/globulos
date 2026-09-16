using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// ✅ Spawne les PlayerData au Lobby (dès qu'un joueur rejoint)
/// Les pseudos sont synchronisés en réseau via NetworkBehaviour
public class PlayerDataSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef playerDataPrefab;
    private NetworkRunner _runner;
    [SerializeField] private LobbyManager lobbyManager;

    private void Start()
    {
        _runner = FindObjectOfType<NetworkRunner>();
        if (_runner != null)
        {
            _runner.AddCallbacks(this);
            Debug.Log("[PlayerDataSpawner] ✅ Callbacks enregistrés");
        }
    }

    private void OnDisable()
    {
        if (_runner != null)
        {
            _runner.RemoveCallbacks(this);
        }
    }

    // ✅ Appelé quand un joueur rejoint
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player != runner.LocalPlayer)
        {
            Debug.Log($"[PlayerDataSpawner] ⏭️ Joueur {player.PlayerId} n'est pas local, skip");
            return;
        }

        if (playerDataPrefab == null)
        {
            Debug.LogError("[PlayerDataSpawner] ❌ PlayerDataPrefab non assigné!");
            return;
        }

        Debug.Log($"[PlayerDataSpawner] ⏭️ Spawn Joueur {player.PlayerId}");
        // ✅ Spawn un PlayerData avec StateAuthority du joueur
        // (Le serveur l'autorise, mais le joueur en est le propriétaire logique)
        NetworkObject spawnedPlayerData = runner.Spawn(
            playerDataPrefab,
            Vector3.zero,
            Quaternion.identity,
            player  // ✅ InputAuthority = le joueur
        );

        PlayerData playerData = spawnedPlayerData.GetComponent<PlayerData>();

        string playerNickname = lobbyManager._playerNickname;

        if (playerData != null)
        {
            playerData.SetNickname(playerNickname);
            Debug.Log($"[PlayerDataSpawner] 🌐 Pseudo {playerNickname} assigné à PlayerData du joueur {player.PlayerId}");
            playerData.SetPlayerId(player.PlayerId);
        }

    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[PlayerDataSpawner] 👤 Joueur {player.PlayerId} a quitté");
        
        // ✅ Les PlayerData sont automatiquement despawned quand le joueur quitte
        // (car ils ont InputAuthority du joueur qui vient de partir)
    }

    // ========================================================================
    // Callbacks non utilisés (obligatoires pour INetworkRunnerCallbacks)
    // ========================================================================
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
