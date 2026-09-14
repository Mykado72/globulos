using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using System.Collections.Generic;
using System;

public class LobbyUIManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private TextMeshProUGUI playersListText;

    private NetworkRunner _runner;

    private void Start()
    {
        _runner = FindObjectOfType<NetworkRunner>();
        if (_runner != null)
        {
            _runner.AddCallbacks(this);
        }
    }

    // Quand un joueur rejoint (ancienne version, à supprimer si non utilisée)
    public void OnPlayerJoined(PlayerRef player)
    {
        Debug.Log($"[LobbyUIManager] Joueur {player.PlayerId} a rejoint");
        RefreshPlayersList();
    }

    // INetworkRunnerCallbacks - OnPlayerJoined
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[LobbyUIManager] Joueur {player.PlayerId} a rejoint (INetworkRunnerCallbacks)");
        RefreshPlayersList();
    }

    // INetworkRunnerCallbacks - OnObjectEnterAOI
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // Implémentation vide requise
    }

    // INetworkRunnerCallbacks - OnObjectExitAOI
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // Implémentation vide requise
    }

    // INetworkRunnerCallbacks - OnDisconnectedFromServer
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        // Implémentation vide requise
    }

    // INetworkRunnerCallbacks - OnReliableDataReceived
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
    {
        // Implémentation vide requise
    }

    // INetworkRunnerCallbacks - OnReliableDataProgress
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        // Implémentation vide requise
    }

    // INetworkRunnerCallbacks - OnSessionListUpdated
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // Implémentation vide requise
    }

    // INetworkRunnerCallbacks - OnCustomAuthenticationResponse
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {
        // Implémentation vide requise
    }

    // Met à jour la liste affichée
    private void RefreshPlayersList()
    {
        PlayerData[] allPlayerDatas = FindObjectsOfType<PlayerData>();

        string playersList = "🎮 Joueurs connectés:\n";

        foreach (PlayerData playerData in allPlayerDatas)
        {
            string nickname = playerData.Nickname ?? $"Joueur {playerData.GetComponent<NetworkObject>().StateAuthority.PlayerId}";
            playersList += $"✅ {nickname}\n";
        }

        if (playersListText != null)
        {
            playersListText.text = playersList;
        }
    }

    private void OnDisable()
    {
        if (_runner != null)
        {
            _runner.RemoveCallbacks(this);
        }
    }

    // Implémentations vides requises par INetworkRunnerCallbacks
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}