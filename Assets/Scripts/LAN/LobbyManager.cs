using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// ✅ VERSION v4 - AMÉLIORATIONS LOBBY
/// - Gère la saisie du pseudo
/// - Stocke le pseudo via PlayerPrefs (persiste entre scènes)
/// - Passe le pseudo à PlayerNamesManager et PlayerData
/// ✨ NEW: Refresh périodique de la liste des joueurs (toutes les 1 sec)
/// ✨ NEW: Retour au Lobby en cas de départ joueur ou erreur réseau
public class LobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Configuration")]
    [SerializeField] private string defaultRoomName = "COGEP";
    [SerializeField] private NetworkRunner runnerPrefab;
    [SerializeField] private int nbOfPlayers = 3;

    [Header("Lobby Refresh")]
    [SerializeField] private float playerListRefreshInterval = 1f;  // ✨ Refresh toutes les 1 sec

    [Header("UI")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_InputField playerNicknameInput;
    [SerializeField] private Button playButton;
    [SerializeField] private Button playVsAIButton; // ✨ NEW : bouton "Jouer vs IA"
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI playersNickname;
    [SerializeField] private TextMeshProUGUI playersListText;

    public string playerNickname { get; private set; }

    private NetworkRunner _currentRunner;
    private float _lastRefreshTime = 0f;  // ✨ Timer pour refresh périodique
    private bool _isInLobby = true;  // ✨ Flag pour savoir si on est au Lobby
    private bool _isVsAIMode = false;  // ✨ NEW : vrai si la partie a été lancée via "Jouer vs IA"

    private void Start()
    {
        _isInLobby = true;
        _lastRefreshTime = 0f;

        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonPressed);
        }

        // ✨ NEW : bouton pour jouer contre l'IA
        if (playVsAIButton != null)
        {
            playVsAIButton.onClick.AddListener(OnPlayVsAIButtonPressed);
        }

        // ✅ Charge le pseudo sauvegardé (si existe)
        playerNickname = PlayerPrefs.GetString("playerNickname", "Joueur");
        if (playerNicknameInput != null)
        {
            playerNicknameInput.text = playerNickname;
        }
    }

    private void Update()
    {
        // ✨ NEW: Refresh la liste des joueurs toutes les X secondes
        if (_isInLobby && _currentRunner != null && _currentRunner.IsRunning)
        {
            _lastRefreshTime += Time.deltaTime;

            if ((_lastRefreshTime >= playerListRefreshInterval) && (_isVsAIMode != true))
            {
                RefreshPlayersList();
                _lastRefreshTime = 0f;
            }
        }
    }

    public async void OnPlayButtonPressed()
    {
        if (playButton != null) playButton.interactable = false;

        // ✅ Récupère et valide le pseudo
        if (!string.IsNullOrEmpty(playerNicknameInput.text))
        {
            playerNickname = playerNicknameInput.text.Trim();
        }

        if (string.IsNullOrEmpty(playerNickname))
        {
            playerNickname = "Joueur";
        }

        // ✅ Sauvegarde le pseudo
        PlayerPrefs.SetString("playerNickname", playerNickname);
        PlayerPrefs.Save();

        string roomName = defaultRoomName;
        
        if (roomNameInput != null && !string.IsNullOrEmpty(roomNameInput.text))
        {
            roomName = roomNameInput.text;
        }
        
        UpdateStatus($"Connexion en tant que '{playerNickname}'...");
        await StartGameSession(roomName);
    }

    /// <summary>
    /// ✨ NEW: Lance une partie solo contre l'IA. Ne nécessite aucun second joueur :
    /// démarre la session immédiatement (voir CheckPlayersAndStartGame) et utilise
    /// une room dédiée générée aléatoirement pour éviter qu'un vrai joueur ne
    /// rejoigne par hasard une partie censée être vs IA.
    /// </summary>
    public void OnPlayVsAIButtonPressed()
    {
        if (playButton != null) playButton.interactable = false;
        if (playVsAIButton != null) playVsAIButton.interactable = false;

        _isVsAIMode = true;

        GameModeManager modeManager = GetOrCreateGameModeManager();
        modeManager.ResetForNewSession();
        modeManager.IsVsAI = true;

        // Pseudo
        if (!string.IsNullOrEmpty(playerNicknameInput.text))
            playerNickname = playerNicknameInput.text.Trim();
        if (string.IsNullOrEmpty(playerNickname))
            playerNickname = "Joueur";

        PlayerPrefs.SetString("playerNickname", playerNickname);
        PlayerPrefs.Save();

        // Enregistrer le nom du joueur localement
        PlayerNamesManager.Instance?.SetPlayerName(1, playerNickname);

        UpdateStatus("Lancement de la partie locale...");

        // 🚀 CHARGEMENT EN LOCAL SANS FUSION
        SceneManager.LoadScene("GameSceneLocal");
    }

    /// <summary>
    /// ✨ NEW: Crée le GameModeManager s'il n'existe pas encore dans la scène.
    /// </summary>
    private GameModeManager GetOrCreateGameModeManager()
    {
        if (GameModeManager.Instance == null)
        {
            GameObject go = new GameObject("GameModeManager");
            go.AddComponent<GameModeManager>();
        }
        return GameModeManager.Instance;
    }

    /// <summary>
    /// ✨ NEW: Refresh la liste des joueurs actuellement connectés
    /// Appelée toutes les secondes via Update()
    /// </summary>
    public void RefreshPlayersList()
    {
        if (_currentRunner == null) return;

        string playersList = "🎮 Joueurs connectés:\n";
        int playerCount = 0;

        foreach (var player in GetAllPlayerData())
        {
            if (player == null) continue;
            string nickname = player.GetNickname();
            playersList += $"✅ {nickname}\n";
            playerCount++;
        }

        // Ajouter le compteur
        playersList += $"\n{playerCount}/{_currentRunner.ActivePlayers.Count()} joueurs";

        if (playersListText != null)
        {
            playersListText.text = playersList;
        }

        Debug.Log($"[LobbyManager] 🔄 Liste des joueurs rafraîchie ({playerCount} joueurs)");
    }

    private async Task StartGameSession(string roomName)
    {
        if (_currentRunner == null)
        {
            _currentRunner = UnityEngine.Object.FindFirstObjectByType<NetworkRunner>();

            if (_currentRunner == null)
            {
                if (runnerPrefab != null)
                {
                    _currentRunner = Instantiate(runnerPrefab);
                }
                else
                {
                    GameObject runnerObject = new GameObject("NetworkRunner");
                    _currentRunner = runnerObject.AddComponent<NetworkRunner>();
                }
            }
        }

        _currentRunner.ProvideInput = true;
        _currentRunner.AddCallbacks(this);

        INetworkSceneManager sceneManager = null;
        if (!_currentRunner.TryGetComponent<INetworkSceneManager>(out sceneManager) || sceneManager == null)
        {
            var concreteSceneManager = _currentRunner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            sceneManager = concreteSceneManager;
        }

        // ✅ Récupère et valide le pseudo
        if (!string.IsNullOrEmpty(playerNicknameInput.text))
        {
            playerNickname = playerNicknameInput.text.Trim();
        }

        if (string.IsNullOrEmpty(playerNickname))
        {
            playerNickname = "Joueur";
        }

        // ✅ Envoie le pseudo au serveur Fusion via le ConnectionToken
        byte[] token = System.Text.Encoding.UTF8.GetBytes(playerNickname);

        var result = await _currentRunner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            SceneManager = sceneManager,
            ConnectionToken = token
        });

        if (result.Ok)
        {
            Debug.Log($"[LobbyManager] ✅ Connecté à '{roomName}'. En attente du second joueur...");
            CheckPlayersAndStartGame();
        }
        else
        {
            Debug.LogError($"[LobbyManager] ❌ Échec : {result.ShutdownReason}");
            UpdateStatus($"Échec : {result.ShutdownReason}");
            if (playButton != null) playButton.interactable = true;
        }

    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        CheckPlayersAndStartGame();
    }

    /// <summary>
    /// ✨ NEW: Appelé quand un joueur quitte
    /// Retour au Lobby si le Master Client s'en va
    /// </summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[LobbyManager] 👤 Joueur {player.PlayerId} a quitté");

        // ✨ NEW: Si c'est le Master Client qui part, revenir au Lobby
        if (player.IsValid && runner.IsSharedModeMasterClient)
        {
            Debug.LogWarning("[LobbyManager] ⚠️ Master Client a quitté ! Retour au Lobby...");
            ReturnToLobby("Master Client a quitté la partie");
        }
        else if (_isInLobby)
        {
            // On est au Lobby, juste mettre à jour la liste
            RefreshPlayersList();
        }
    }

    private void CheckPlayersAndStartGame()
    {
        if (_currentRunner == null) return;

        int count = _currentRunner.ActivePlayers != null ? System.Linq.Enumerable.Count(_currentRunner.ActivePlayers) : 0;

        if (playersNickname != null)
        {
            string nicknames = "";
            playersNickname.text = nicknames;
        }

        // ✨ NEW : en mode vs IA, un seul joueur (le local) suffit pour démarrer
        int requiredPlayers = _isVsAIMode ? 1 : nbOfPlayers;

        UpdateStatus($"Joueurs connectés : {count}/{requiredPlayers}");
        if (_currentRunner.IsSharedModeMasterClient && count >= requiredPlayers)
        {
            _isInLobby = false;  // ✨ Marquer qu'on quitte le Lobby
            var sceneRef = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath("GameScene"));
            _currentRunner.LoadScene(sceneRef);
        }
    }

    private PlayerData[] GetAllPlayerData()
    {
        PlayerData[] allPlayerData = FindObjectsByType<PlayerData>(FindObjectsSortMode.None);
        return allPlayerData;
    }

    public PlayerData GetPlayerDataById(int playerId)
    {
        foreach (var playerData in FindObjectsOfType<PlayerData>())
        {
            if ((playerData.Object != null) && (playerData.Object.InputAuthority.PlayerId == playerId))
            {
                Debug.Log($"[LobbyManager] 🔍 Trouvé PlayerData pour ID {playerId}: {playerData.GetNickname()}");
                return playerData;
            }
        }
        Debug.Log($"[LobbyManager] 🔍 pas de PlayerData Trouvé pour ID {playerId}");
        return null;
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    /// <summary>
    /// ✨ NEW: Retourne au Lobby en cas d'erreur réseau ou départ joueur
    /// </summary>
    private void ReturnToLobby(string reason)
    {
        _isInLobby = true;
        _isVsAIMode = false; // ✨ NEW

        Debug.Log($"[LobbyManager] 🔙 Retour au Lobby - Raison: {reason}");

        // Arrêter le Runner si actif
        if (_currentRunner != null)
        {
            _currentRunner.Shutdown();
            Destroy(_currentRunner.gameObject);
            _currentRunner = null;
        }

        // Afficher message d'erreur
        UpdateStatus($"Erreur: {reason}");

        // Réactiver les boutons Play / Play vs IA
        if (playButton != null)
        {
            playButton.interactable = true;
        }
        if (playVsAIButton != null)
        {
            playVsAIButton.interactable = true;
        }

        // Réinitialiser la liste des joueurs
        if (playersListText != null)
        {
            playersListText.text = "🎮 Joueurs connectés:\n(Aucun)";
        }

        // Nettoyer les données réseau
        PlayerNamesManager.Instance?.Clear();
    }

    // === INetworkRunnerCallbacks ===

    public void OnInput(NetworkRunner runner, NetworkInput input) { }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    /// <summary>
    /// ✨ NEW: Appelé quand le Runner s'arrête (erreur réseau, déconnexion, etc.)
    /// </summary>
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.LogWarning($"[LobbyManager] ⚠️ Runner arrêté - Raison: {shutdownReason}");

        if (_isInLobby)
        {
            ReturnToLobby($"Problème réseau: {shutdownReason}");
        }
        else
        {
            // On était en jeu, revenir au Lobby
            Debug.Log("[LobbyManager] 🔄 Déconnexion en jeu - Retour au Lobby");
            LoadLobbyScene();
        }
    }

    /// <summary>
    /// ✨ NEW: Appelé quand la connexion au serveur est perdue
    /// </summary>
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogError($"[LobbyManager] ❌ Déconnexion du serveur: {reason}");
        ReturnToLobby($"Déconnexion serveur: {reason}");
    }

    /// <summary>
    /// ✨ NEW: Appelé quand la connexion au serveur échoue
    /// </summary>
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"[LobbyManager] ❌ Connexion échouée: {reason}");
        ReturnToLobby($"Impossible de se connecter: {reason}");
    }

    /// <summary>
    /// ✨ NEW: Charge la scène du Lobby
    /// Utilisée en cas de déconnexion ou erreur en jeu
    /// </summary>
    private void LoadLobbyScene()
    {
        Debug.Log("[LobbyManager] 📍 Chargement de la scène Lobby...");

        // Nettoyer les managers singletons
        if (PlayerNamesManager.Instance != null)
        {
            Destroy(PlayerNamesManager.Instance.gameObject);
        }

        if (AudioManager.Instance != null)
        {
            Destroy(AudioManager.Instance.gameObject);
        }

        // Charger la scène du Lobby
        SceneManager.LoadScene("LobbyScene");
    }

    // Callbacks non utilisés
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
