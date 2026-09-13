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

    // ✅ FIX : ce flag est maintenant LOCAL à chaque client (chacun spawne pour lui-même),
    // et non plus un flag "un seul spawn global fait par le master".
    private bool _hasSpawnedLocally = false;

    private NetworkRunner _runner;

    private void Start()
    {
        Debug.Log("[GameSpawner] Start() exécuté !");

        // Vérifier que les prefabs sont assignés
        if (PlayerJaunePrefab == null || PlayerRougePrefab == null)
        {
            Debug.LogError("[GameSpawner] ❌ ERREUR CRITIQUE : Les NetworkPrefabRef ne sont pas assignés dans l'inspecteur !");
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
            Debug.Log($"[GameSpawner] ✅ Runner trouvé et enregistré. IsMaster: {_runner.IsSharedModeMasterClient}");
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

    // ✅ FIX : OnSceneLoadDone se déclenche indépendamment sur CHAQUE client, dès que SA PROPRE
    // scène a fini de charger. On n'a donc plus besoin d'attendre que "le master spawne pour tout
    // le monde" : chaque client spawne directement ses propres billes ici.
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("[GameSpawner] ✅ Callback OnSceneLoadDone reçu ! Je spawne mes propres billes.");
        SpawnMyBalls(runner);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[GameSpawner] 👤 Joueur rejoint : {player.PlayerId}");

        // Si un joueur rejoint APRÈS que notre scène a déjà chargé (cas rare), on s'assure
        // quand même d'avoir spawné nos propres billes.
        if (player == runner.LocalPlayer)
        {
            SpawnMyBalls(runner);
        }
    }

    // ✅ FIX PRINCIPAL :
    // Avant, seul le Master spawnait les billes des DEUX joueurs (runner.Spawn appelé par
    // le Master pour player0 ET player1). Or en Shared Mode :
    //   - "Runner.Spawn() ne peut être appelé que par le client qui a l'intention de devenir
    //      la State Authority de l'objet spawné" (doc Fusion 2 Shared).
    //   - "L'Input Authority n'est pas applicable en Shared Mode" (doc Fusion 2 - NetworkObject).
    // Résultat : le Master devenait State Authority sur TOUTES les billes (jaunes ET rouges),
    // et le champ InputAuthority qu'on essayait d'assigner à la volée n'était pas fiable
    // pour déterminer qui contrôle quoi. D'où le bug : la 1ère instance contrôlait les
    // mauvaises billes, et la 2ème instance n'avait jamais State Authority sur rien.
    //
    // Le fix : CHAQUE client appelle lui-même runner.Spawn() pour SES PROPRES billes.
    // Il en devient alors automatiquement State Authority, ce qui est le concept fiable
    // à utiliser en Shared Mode (voir BallAimController : HasStateAuthority).
    private void SpawnMyBalls(NetworkRunner runner)
    {
        if (_hasSpawnedLocally)
        {
            Debug.LogWarning("[GameSpawner] ⚠️ J'ai déjà spawné mes billes, on ignore l'appel");
            return;
        }

        _hasSpawnedLocally = true;

        // Critère fiable et documenté par Photon pour distinguer les deux joueurs :
        // le Master Client (celui qui a créé la session) joue Jaune, l'autre joue Rouge.
        bool isMaster = runner.IsSharedModeMasterClient;

        Transform[] spawnPoints = isMaster ? player1SpawnPoints : player2SpawnPoints;
        NetworkPrefabRef prefab = isMaster ? PlayerJaunePrefab : PlayerRougePrefab;

        Debug.Log($"[GameSpawner] Je suis {(isMaster ? "Master (Jaune)" : "Client (Rouge)")} - LocalPlayer: {runner.LocalPlayer.PlayerId}");

        SpawnForPlayer(runner, runner.LocalPlayer, spawnPoints, prefab);
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
                Debug.Log($"  - Prefab: {(boulePrefab == PlayerJaunePrefab ? "Jaune" : "Rouge")}");

                // ✅ On spawne pour SOI (player == runner.LocalPlayer) : on devient
                // automatiquement State Authority sur cet objet, ce qui est exactement
                // ce dont BallAimController a besoin (HasStateAuthority).
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
                Debug.Log($"[GameSpawner] ✅ Boule spawned pour joueur {player.PlayerId} - StateAuthority: {ball.StateAuthority.PlayerId} - InputAuthority: {ball.InputAuthority.PlayerId}");
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

        _spawnedBalls[player] = playerBalls;
        Debug.Log($"[GameSpawner] ✅ {playerBalls.Count} boule(s) spawnée(s) pour joueur {player.PlayerId}");
    }

    // --- Implémentation des callbacks INetworkRunnerCallbacks ---
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[GameSpawner] 👤 Joueur parti : {player.PlayerId}");
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[GameSpawner] 🛑 NetworkRunner arrêté : {shutdownReason}");
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("[GameSpawner] 🌐 Connecté au serveur");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"[GameSpawner] 🌐 Déconnecté du serveur : {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
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