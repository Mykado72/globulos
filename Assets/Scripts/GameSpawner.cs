using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// ✅ VERSION v4 - GESTION ERREURS RÉSEAU
/// - Intègre PlayerData avec le pseudo
/// - Synchronise le pseudo en réseau
/// - Enregistre le pseudo dans PlayerNamesManager
/// - Fix: Race condition - check `!_isSpawning` dans Update
/// ✨ NEW: Gestion des erreurs réseau et retour au Lobby
public class GameSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef player1Prefab;
    [SerializeField] private NetworkPrefabRef player2Prefab;
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;
    [SerializeField] private NetworkPrefabRef soccerBallPrefab;
    [SerializeField] private Transform soccerBallSpawnPoint;

    private bool _isSpawning = false;
    private bool _hasSpawnedLocalPlayer = false;
    private bool _hasSpawnedBall = false;
    private NetworkRunner _runner;

    private void Start()
    {
        _runner = FindObjectOfType<NetworkRunner>();
        if (_runner != null)
        {
            _runner.AddCallbacks(this);
            Debug.Log("[GameSpawner] ✅ Runner trouvé et enregistré");
        }
        else
        {
            Debug.LogError("[GameSpawner] ❌ Aucun NetworkRunner trouvé !");
        }
    }

    private void Update()
    {
        // ✨ FIX: Check `!_isSpawning` pour éviter la race condition
        if (!_hasSpawnedLocalPlayer && !_isSpawning && _runner != null && _runner.IsRunning)
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

        // ✅ Enregistre les pseudos de TOUS les joueurs
        if (PlayerNamesManager.Instance != null)
        {
            foreach (var player in runner.ActivePlayers)
            {
                byte[] token = runner.GetPlayerConnectionToken(player);
                if (token != null && token.Length > 0)
                {
                    string nickname = System.Text.Encoding.UTF8.GetString(token);
                    PlayerNamesManager.Instance.SetPlayerName(player.PlayerId, nickname);
                }
            }
        }

        if (runner.IsSharedModeMasterClient && !_hasSpawnedBall)
        {
            _hasSpawnedBall = true;
            SpawnSoccerBall(runner);
            Debug.Log("[GameSpawner] ⚽ Master Client a spawné le ballon");
        }
    }

    private async void TrySpawnLocalPlayer(NetworkRunner runner)
    {
        if (_hasSpawnedLocalPlayer || _isSpawning) return;
        _isSpawning = true;

        PlayerRef localPlayer = runner.LocalPlayer;
        if (!localPlayer.IsValid)
        {
            _isSpawning = false;
            return;
        }

        // ✅ Récupère le pseudo depuis le token Fusion du joueur local
        string playerNickname = $"Joueur {localPlayer.PlayerId}";
        byte[] token = runner.GetPlayerConnectionToken(localPlayer);

        if (token != null && token.Length > 0)
        {
            playerNickname = System.Text.Encoding.UTF8.GetString(token);
        }

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
                    // Ne définir le pseudo que si le joueur local possède l'autorité sur ce NetworkObject
                    if (spawnedBall.HasInputAuthority)
                    {
                        if (spawnedBall.TryGetComponent(out PlayerData playerData))
                        {
                            playerData.RPC_SetPlayerInfo(playerNickname, localPlayer.PlayerId);
                            Debug.Log($"[GameSpawner] ✅ Pseudo local envoyé au réseau : {playerNickname} (ID: {localPlayer.PlayerId})");
                        }

                        if (PlayerNamesManager.Instance != null)
                        {
                            PlayerNamesManager.Instance.SetPlayerName(localPlayer.PlayerId, playerNickname);
                        }
                    }

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

    /// <summary>
    /// ✨ NEW: Quand un joueur quitte la partie en jeu
    /// </summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.LogWarning($"[GameSpawner] ⚠️ Joueur {player.PlayerId} a quitté en jeu!");

        // ✨ NEW: Si un joueur quitte le jeu, retourner au Lobby
        if (SceneManager.GetActiveScene().name == "GameScene")
        {
            ReturnToLobbyOnDisconnect($"Joueur {player.PlayerId} a quitté");
        }
    }

    /// <summary>
    /// ✨ NEW: Arrêt du Runner (erreur réseau, déconnexion, etc.)
    /// </summary>
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.LogError($"[GameSpawner] ❌ Runner arrêté - Raison: {shutdownReason}");

        // ✨ NEW: Retourner au Lobby si une erreur survient en jeu
        if (SceneManager.GetActiveScene().name == "GameScene")
        {
            ReturnToLobbyOnDisconnect($"Erreur réseau: {shutdownReason}");
        }
    }

    /// <summary>
    /// ✨ NEW: Déconnexion du serveur
    /// </summary>
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogError($"[GameSpawner] ❌ Déconnexion serveur: {reason}");

        if (SceneManager.GetActiveScene().name == "GameScene")
        {
            ReturnToLobbyOnDisconnect($"Déconnexion du serveur: {reason}");
        }
    }

    /// <summary>
    /// ✨ NEW: Connexion au serveur échouée
    /// </summary>
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"[GameSpawner] ❌ Connexion échouée: {reason}");

        if (SceneManager.GetActiveScene().name == "GameScene")
        {
            ReturnToLobbyOnDisconnect($"Impossible de se reconnecter: {reason}");
        }
    }

    /// <summary>
    /// ✨ NEW: Retourne au Lobby avec message d'erreur
    /// </summary>
    private void ReturnToLobbyOnDisconnect(string reason)
    {
        Debug.Log($"[GameSpawner] 🔙 Retour au Lobby - Raison: {reason}");

        // Nettoyer les données
        if (TurnManager.Instance != null)
        {
            Destroy(TurnManager.Instance.gameObject);
        }

        if (PlayerNamesManager.Instance != null)
        {
            Destroy(PlayerNamesManager.Instance.gameObject);
        }

        if (AudioManager.Instance != null)
        {
            Destroy(AudioManager.Instance.gameObject);
        }

        // Revenir au Lobby
        SceneManager.LoadScene("LobbyScene");
    }

    // Callbacks non utilisés (obligatoires pour INetworkRunnerCallbacks)
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
