using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;

public class GameSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Network Prefabs")]
    [SerializeField] private NetworkPrefabRef PlayerJaunePrefab;
    [SerializeField] private NetworkPrefabRef PlayerRougePrefab;

    [Header("Spawn Positions")]
    [SerializeField] private Transform[] player1SpawnPoints;
    [SerializeField] private Transform[] player2SpawnPoints;

    private Dictionary<PlayerRef, List<NetworkObject>> _spawnedBalls = new Dictionary<PlayerRef, List<NetworkObject>>();
    private bool _hasSpawned = false;
    private NetworkRunner _runner;

    private void Start()
    {
        Debug.Log("[GameSpawner] Start() exécuté !");

        // ✅ Vérifier que les prefabs sont assignés
        if (PlayerJaunePrefab== null || PlayerRougePrefab== null)
        {
            Debug.LogError("[GameSpawner] ❌ ERREUR CRITIQUE : Les NetworkPrefabRef ne sont pas assignés dans l'inspecteur !");
            Debug.LogError("[GameSpawner] ❌ Assigne PlayerJaunePrefab et PlayerRougePrefab dans l'inspecteur du GameSpawner");
            enabled = false;
            return;
        }

        // Vérifier les spawn points
        if (player1SpawnPoints == null || player1SpawnPoints.Length == 0)
        {
            Debug.LogError("[GameSpawner] ❌ player1SpawnPoints est vide ou null !");
            enabled = false;
            return;
        }

        if (player2SpawnPoints == null || player2SpawnPoints.Length == 0)
        {
            Debug.LogError("[GameSpawner] ❌ player2SpawnPoints est vide ou null !");
            enabled = false;
            return;
        }

        // Recherche du NetworkRunner dans la scène
        _runner = FindObjectOfType<NetworkRunner>();

        if (_runner != null)
        {
            _runner.AddCallbacks(this);
            Debug.Log($"[GameSpawner] ✅ Runner trouvé et enregistré.");
        }
        else
        {
            Debug.LogError("[GameSpawner] ❌ ERREUR : Aucun NetworkRunner trouvé dans la GameScene !");
            enabled = false;
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
        Debug.Log("[GameSpawner] ✅ Callback OnSceneLoadDone reçu !");

        try
        {
            TrySpawnBalls(runner);
            Debug.Log("[GameSpawner] ✅ TrySpawnBalls terminé sans erreur");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameSpawner] ❌ Exception dans TrySpawnBalls: {ex.Message}");
            Debug.LogError($"[GameSpawner] Stack: {ex.StackTrace}");
        }
    }

    private void TrySpawnBalls(NetworkRunner runner)
    {
        if (_hasSpawned)
        {
            Debug.LogWarning("[GameSpawner] ⚠️ Les boules ont déjà été spawnées, on ignore l'appel");
            return;
        }

        _hasSpawned = true;

        int localPlayerId = runner.LocalPlayer.PlayerId;
        Debug.Log($"[GameSpawner] Mon PlayerId réseau : {localPlayerId}");

        // ✅ Le premier joueur connecté (PlayerId pair, généralement 0) = Jaune
        //    Le second (PlayerId impair, généralement 1) = Rouge
        if (localPlayerId % 2 == 0)
        {
            Debug.Log($"[GameSpawner] Je suis Joueur Jaune (PlayerId {localPlayerId})");
            SpawnForPlayer(runner, runner.LocalPlayer, player1SpawnPoints, PlayerJaunePrefab);
        }
        else
        {
            Debug.Log($"[GameSpawner] Je suis Joueur Rouge (PlayerId {localPlayerId})");
            SpawnForPlayer(runner, runner.LocalPlayer, player2SpawnPoints, PlayerRougePrefab);
        }
    }

    private void SpawnForPlayer(NetworkRunner runner, PlayerRef player, Transform[] spawnPoints, NetworkPrefabRef boulePrefab)
    {
        if (boulePrefab == null)
        {
            Debug.LogError("[GameSpawner] ❌ ERREUR : boulePrefab est vide !");
            return;
        }

        if (runner == null)
        {
            Debug.LogError("[GameSpawner] ❌ ERREUR : NetworkRunner est null !");
            return;
        }

        List<NetworkObject> playerBalls = new List<NetworkObject>();

        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint == null)
            {
                Debug.LogError("[GameSpawner] ❌ ERREUR : Un spawn point est null !");
                continue;
            }

            try
            {
                Debug.Log($"[GameSpawner] Tentative de spawn à la position {spawnPoint.position}");
                Debug.Log($"  - Joueur: {player.PlayerId}");

                NetworkObject ball = runner.Spawn(
                    boulePrefab,
                    spawnPoint.position,
                    Quaternion.identity,
                    player
                );

                if (ball == null)
                {
                    Debug.LogError($"[GameSpawner] ❌ ERREUR : runner.Spawn() a retourné null !");
                    continue;
                }

                playerBalls.Add(ball);
                Debug.Log($"[GameSpawner] ✅ Boule spawned pour joueur {player.PlayerId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameSpawner] ❌ Exception lors du spawn : {ex.Message}\n{ex.StackTrace}");
            }
        }

        if (playerBalls.Count == 0)
        {
            Debug.LogError("[GameSpawner] ❌ ERREUR CRITIQUE : Aucune boule n'a pu être spawnée !");
            return;
        }

        _spawnedBalls.Add(player, playerBalls);
        Debug.Log($"[GameSpawner] ✅ {playerBalls.Count} boule(s) spawnée(s) pour joueur {player.PlayerId}");
    }

    // ✅ NOUVEAU : Récupère le nickname depuis PlayerData
    private string GetPlayerNickname(PlayerRef player)
    {
        if (player.IsNone)
            return "Inconnu";

        // Cherche le PlayerData de ce joueur
        PlayerData[] allPlayerDatas = FindObjectsOfType<PlayerData>();

        foreach (PlayerData playerData in allPlayerDatas)
        {
            NetworkObject netObj = playerData.GetComponent<NetworkObject>();
            if (netObj != null && netObj.StateAuthority == player)
            {
                // ✅ Trouve le nickname dans PlayerData
                return playerData.Nickname;  // À adapter selon le nom de ta variable
            }
        }

        return $"Joueur {player.PlayerId}";  // Fallback
    }

    // --- Implémentation des callbacks INetworkRunnerCallbacks ---

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
        UIManager.Instance?.ShowMessage($"❌ {playerName} a quitté la partie!", 5f);
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        string playerName = PlayerNamesManager.Instance?.GetPlayerName(runner.LocalPlayer) ?? "Vous";
        Debug.Log($"[GameSpawner] 🌐 {playerName} déconnecté: {reason}");
        UIManager.Instance?.ShowMessagePermanent($"⚠️ {playerName} déconnecté: {reason}");
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[GameSpawner] 🛑 NetworkRunner arrêté : {shutdownReason}");
        UIManager.Instance?.ShowMessagePermanent($"🛑 Jeu arrêté : {shutdownReason}");
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("[GameSpawner] 🌐 Connecté au serveur");
        UIManager.Instance?.ShowMessage("🌐 Connecté au serveur", 2f);
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.Log($"[GameSpawner] ❌ Connexion échouée: {reason}");
        UIManager.Instance?.ShowMessagePermanent($"❌ Connexion échouée: {reason}");
    }

    public void OnUserSimulationMessage(NetworkRunner runner) { }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("[GameSpawner] 📍 Chargement de la scène...");
    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
