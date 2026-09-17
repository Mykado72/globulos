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

    private bool _spawnAttempted = false;

    private void Start()
    {
        _runner = FindObjectOfType<NetworkRunner>();
        if (_runner != null)
        {
            _runner.AddCallbacks(this);
            // Debug.Log("[GameSpawner] ✅ Runner trouvé et enregistré");
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

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        _ = TrySpawnLocalPlayer(runner);
        // Debug.Log("[GameSpawner] 📍 OnSceneLoadDone (appelé par Master Client)");

        // ✅ Enregistre les pseudos de TOUS les joueurs
        /*
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
        */
        if (runner.IsSharedModeMasterClient && !_hasSpawnedBall)
        {
            _hasSpawnedBall = true;
            SpawnSoccerBall(runner);
            //Debug.Log("[GameSpawner] ⚽ Master Client a spawné le ballon");
        }
    }

    private async System.Threading.Tasks.Task TrySpawnLocalPlayer(NetworkRunner runner)
    {
        if (_hasSpawnedLocalPlayer || _isSpawning) return;        
            _isSpawning = true;
            _hasSpawnedLocalPlayer = true;     

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

        // ✅ Une bille est spawnée pour CHAQUE spawn point défini (design : plusieurs
        // billes par joueur), et non un seul point choisi au hasard.
        int i = 0;
        foreach (Transform spawnPoint in spawnPoints)
        {             
            i++;
            Debug.Log($"[GameSpawner] 🔍 Index: {i} | Point: {spawnPoint.name} | Frame: {Time.frameCount} | Time: {Time.time}");
            // Debug.Log($"[GameSpawner] 🔹 Tentative de spawn de la bille {i} pour le Joueur {(isPlayer1 ? 1 : 2)}");
            if (spawnPoint == null) continue;

            try
            {
                NetworkObject spawnedBall = await runner.SpawnAsync(
                    prefab,
                    spawnPoint.position,
                    Quaternion.identity,
                    inputAuthority: localPlayer
                );
                spawnedBall.name = $"Player {localPlayer.PlayerId}_Ball{i}";
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

        // ✨ NEW : en mode vs IA, le Master Client (seul joueur présent) spawn aussi le bot
        if (GameModeManager.Instance != null && GameModeManager.Instance.IsVsAI && isPlayer1)
        {
            await SpawnBotPlayer(runner, localPlayer.PlayerId);
        }

        _isSpawning = false;
    }

    /// <summary>
    /// ✨ NEW : Spawn une bille "Joueur 2" par spawn point (même logique que pour le joueur
    /// humain), chacune contrôlée par l'IA au lieu de la souris.
    /// PlayerId = humanPlayerId + 1, ce qui garantit une parité opposée
    /// (donc une équipe adverse) quel que soit le PlayerId réel attribué par Fusion.
    /// </summary>
    private async System.Threading.Tasks.Task SpawnBotPlayer(NetworkRunner runner, int humanPlayerId)
    {
        if (player2Prefab == null || player2SpawnPoints == null || player2SpawnPoints.Length == 0)
        {
            Debug.LogWarning("[GameSpawner] ⚠️ Impossible de spawn le bot : prefab/spawn points Joueur 2 manquants");
            return;
        }

        int botPlayerId = humanPlayerId + 1;
        int spawnedCount = 0;

        foreach (Transform spawnPoint in player2SpawnPoints)
        {
            if (spawnPoint == null) continue;

            try
            {
                // Pas d'inputAuthority : en mode Shared, le client qui spawn (le seul présent
                // ici) devient automatiquement State Authority sur l'objet.
                NetworkObject spawnedBall = await runner.SpawnAsync(player2Prefab, spawnPoint.position, Quaternion.identity);

                if (spawnedBall != null && spawnedBall.TryGetComponent(out BallAimController ballController))
                {
                    ballController.SetOwner(botPlayerId);
                    ballController.SetBotControlled(true);
                    spawnedCount++;
                }
                else
                {
                    Debug.LogError("[GameSpawner] ❌ Échec du spawn du bot : composant BallAimController manquant");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameSpawner] ❌ Exception lors du spawn du bot: {ex.Message}");
            }
        }

        if (spawnedCount > 0)
        {
            if (GameModeManager.Instance != null)
            {
                GameModeManager.Instance.BotPlayerId = botPlayerId;
            }

            PlayerNamesManager.Instance?.SetPlayerName(botPlayerId, "🤖 IA");
        }

        // Debug.Log($"[GameSpawner] 🤖 {spawnedCount} bille(s) IA spawnée(s) (PlayerId {botPlayerId})");
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
                // Debug.Log("[GameSpawner] ⚽ Ballon spawné avec succès");
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