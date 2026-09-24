using Fusion;
using Fusion.Sockets;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Version simplifiée de GameSpawner, à mettre dans la scène de jeu chargée
/// après le TestLobbyManager. Sert uniquement à valider que le flux
/// lobby -> StartGame -> LoadScene -> spawn fonctionne de bout en bout.
/// </summary>
public class TestPlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    private NetworkRunner _runner;
    private bool _hasSpawned = false;

    private void Start()
    {
        _runner = FindFirstObjectByType<NetworkRunner>();
        if (_runner != null)
            _runner.AddCallbacks(this);
    }

    private void OnDisable()
    {
        if (_runner != null)
            _runner.RemoveCallbacks(this);
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        _ = TrySpawn();
    }

    private async Task TrySpawn()
    {
        if (_hasSpawned || _runner == null || !_runner.IsRunning) return;

        // Attente courte que le PlayerId local soit valide (utile en Shared Mode).
        int attempts = 0;
        while (!_runner.LocalPlayer.IsValid && attempts < 50)
        {
            attempts++;
            await Task.Delay(100);
        }

        if (_hasSpawned || !_runner.IsRunning) return;
        _hasSpawned = true;

        Transform point = (spawnPoints != null && spawnPoints.Length > 0)
            ? spawnPoints[_runner.LocalPlayer.PlayerId % spawnPoints.Length]
            : transform;

        var obj = await _runner.SpawnAsync(playerPrefab, point.position, Quaternion.identity, _runner.LocalPlayer);
        if (obj != null)
            Debug.Log($"[TestPlayerSpawner] ✅ Joueur {_runner.LocalPlayer.PlayerId} spawné.");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player == runner.LocalPlayer)
            _ = TrySpawn();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, Fusion.Sockets.NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, Fusion.Sockets.NetAddress remoteAddress, Fusion.Sockets.NetConnectFailedReason reason) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
}
