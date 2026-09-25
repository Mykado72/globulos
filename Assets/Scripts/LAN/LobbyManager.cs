using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Fusion Setup")]
    [SerializeField] private NetworkRunner runnerPrefab;

    [Header("UI References - Controls")]
    [SerializeField] private TMP_InputField playerNicknameInput;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private Button playButton;
    [SerializeField] private Button playVsAIButton;
    [SerializeField] private Button joinRoomButton;

    [Header("UI References - Status")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI roomStatusText;

    [Header("Game Settings")]
    [SerializeField] private string defaultRoomName = "COGEP";
    [SerializeField] private int nbOfPlayers = 2;

    private NetworkRunner _currentRunner;
    private bool _isInLobby = true;
    private bool _isVsAIMode = false;
    private string _playerNickname;
    private bool _cogepRoomExists = false;

    public string playerNickname => _playerNickname;
    private bool _isLoadingScene = false;
    private Task<StartGameResult> _lobbyJoinTask; // stocker la task
    private bool _sceneLoadConfirmed = false;
    private int _sceneLoadRetryCount = 0;
    private const int MaxSceneLoadRetries = 3;
    private const float SceneLoadTimeoutSeconds = 8f;
    private void CheckPlayersAndStartGame()
    {
        if (_currentRunner == null || !_currentRunner.IsRunning) return;

        // Compter les joueurs actifs
        int count = 0;
        if (_currentRunner.ActivePlayers != null)
        {
            foreach (var p in _currentRunner.ActivePlayers)
            {
                count++;
            }
        }

        int requiredPlayers = _isVsAIMode ? 1 : nbOfPlayers;
        UpdateStatus($"Joueurs connectés : {count}/{requiredPlayers}");

        // Si le nombre de joueurs est atteint et qu'aucun chargement n'est déjà en cours
        if (count >= requiredPlayers && !_isLoadingScene)
        {
            // On vérifie qui est le MasterClient
            if (_currentRunner.IsSharedModeMasterClient)
            {
                _isLoadingScene = true;
                UpdateStatus("Tous les joueurs sont présents ! Lancement par le MasterClient...");
                _ = StartGameSceneForMaster();
            }
            else
            {
                UpdateStatus("Tous les joueurs sont connectés. En attente du MasterClient...");
            }
        }
    }

    private async Task StartGameSceneForMaster()
    {
        // Attente de sécurité pour laisser Fusion stabiliser l'autorité Shared Mode
        await Task.Delay(1000);

        if (_currentRunner == null || !_currentRunner.IsRunning) return;

        if (_currentRunner.IsSharedModeMasterClient)
        {
            _sceneLoadConfirmed = false;
            _sceneLoadRetryCount = 0;
            await AttemptLoadSceneWithRetry();
        }
    }

    private async Task AttemptLoadSceneWithRetry()
    {
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath("GameSceneLAN");
        if (sceneIndex < 0)
        {
            sceneIndex = SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/GameSceneLAN.unity");
        }

        if (sceneIndex < 0)
        {
            Debug.LogError("❌ Scène 'GameSceneLAN' non trouvée dans les Build Settings !");
            UpdateStatus("❌ Erreur de configuration : scène introuvable.");
            _isLoadingScene = false;
            return;
        }

        // 🔴 On ne lance LoadScene qu'UNE fois, ici, avant la boucle.
        _currentRunner.LoadScene(SceneRef.FromIndex(sceneIndex));
        Debug.Log($"[LobbyManager] 🚀 Chargement scène index {sceneIndex}...");
        UpdateStatus("Chargement de la partie...");

        float elapsed = 0f;
        const float pollInterval = 0.25f;
        const float generousTimeout = 30f; // 🔴 Bien plus généreux, adapté à une mémoire contrainte

        while (elapsed < generousTimeout && !_sceneLoadConfirmed)
        {
            await Task.Delay(TimeSpan.FromSeconds(pollInterval));
            elapsed += pollInterval;

            if (_currentRunner == null || !_currentRunner.IsRunning) return;
        }

        if (_sceneLoadConfirmed)
        {
            Debug.Log("[LobbyManager] ✅ Scène chargée avec succès.");
            return;
        }
        // 🔴 Après un VRAI échec (rien n'a jamais confirmé), on informe sans relancer
        // LoadScene par-dessus un chargement peut-être encore actif.
        Debug.LogError("[LobbyManager] ❌ Le chargement de la scène n'a pas été confirmé après " + generousTimeout + "s.");
        UpdateStatus("❌ Le chargement prend trop de temps. Rechargez la page si besoin.");
        _isLoadingScene = false;
    }
    private async void Start()
    {
        _isInLobby = true;

        if (playButton != null) playButton.onClick.AddListener(OnPlayButtonPressed);
        if (playVsAIButton != null) playVsAIButton.onClick.AddListener(OnPlayVsAIButtonPressed);

        if (joinRoomButton != null)
        {
            joinRoomButton.onClick.AddListener(OnJoinCogepRoomPressed);
            joinRoomButton.interactable = false;
        }

        _playerNickname = PlayerPrefs.GetString("playerNickname", "Joueur");
        if (playerNicknameInput != null) playerNicknameInput.text = _playerNickname;
        if (roomNameInput != null) roomNameInput.text = defaultRoomName;

        UpdateStatus("Connexion au lobby Fusion...");
        await JoinLobbySessionList();
    }

    private void OnDisable()
    {
        if (_currentRunner != null)
        {
            _currentRunner.RemoveCallbacks(this);
        }
    }

    private async Task JoinLobbySessionList()
    {
        if (_currentRunner == null)
        {
            _currentRunner = FindFirstObjectByType<NetworkRunner>() ?? Instantiate(runnerPrefab);
        }

        _currentRunner.ProvideInput = true;
        _currentRunner.AddCallbacks(this);

        _lobbyJoinTask = _currentRunner.JoinSessionLobby(SessionLobby.Shared);
        var result = await _lobbyJoinTask;

        if (result.Ok)
        {       
            UpdateStatus("Connecté au Lobby. Recherche de sessions...");
        }
        else
        {
            UpdateStatus($"❌ Échec de connexion au Lobby : {result.ShutdownReason}");
        }
    }

    private void OnPlayButtonPressed()
    {
        _isVsAIMode = false;
        SaveNicknameAndStart();
    }

    private void OnPlayVsAIButtonPressed()
    {
        _isVsAIMode = true;
        SaveNicknameAndStart();
    }

    public void OnJoinCogepRoomPressed()
    {
        if (!_cogepRoomExists) return;

        _isVsAIMode = false;
        SaveNicknameAndStart();
    }

    private async void SaveNicknameAndStart()
    {
        SetAllButtonsInteractable(false);

        // 🔴 Attendre que le join du lobby soit bien terminé avant de toucher au runner
        if (_lobbyJoinTask != null && !_lobbyJoinTask.IsCompleted)
            await _lobbyJoinTask;
        _isInLobby = false;

        if (playerNicknameInput != null && !string.IsNullOrWhiteSpace(playerNicknameInput.text))
        {
            _playerNickname = playerNicknameInput.text.Trim();
        }
        else
        {
            _playerNickname = "Joueur";
        }

        PlayerPrefs.SetString("playerNickname", _playerNickname);
        PlayerPrefs.Save();

        string roomName = GetTargetRoomName();

        UpdateStatus($"Connexion à la room '{roomName}'...");

        // 🔴 CRUCIAL : Quitter le Lobby proprement avant de lancer la session de jeu
        if (_currentRunner != null && _currentRunner.IsRunning)
        {
            // 🔴 FIX : se désinscrire AVANT le Shutdown pour être sûr de ne plus recevoir
            // aucun callback (OnSessionListUpdated, etc.) de cette instance.
            _currentRunner.RemoveCallbacks(this);
            await _currentRunner.Shutdown();

            // 🔴 FIX : détruire explicitement l'ancien runner pour ne pas le laisser
            // traîner dans la scène (fuite d'objet).
            if (_currentRunner != null)
            {
                Destroy(_currentRunner.gameObject);
            }
            _currentRunner = null;
        }

        // 🔴 FIX : StartGameSession se charge maintenant de LA SEULE instanciation du
        // runner de jeu. On n'instancie plus un runner ici pour éviter la double
        // instanciation (l'ancien code créait un runner ici PUIS un second dans
        // StartGameSession, abandonnant le premier sans jamais le détruire ni le
        // désinscrire : c'était une fuite ET une source d'événements fantômes).
        await StartGameSession(roomName);
    }

    private async Task StartGameSession(string roomName)
    {
        // 1. Instanciation du nouveau Runner vierge (seule instanciation du runner de jeu)
        _currentRunner = Instantiate(runnerPrefab);
        _currentRunner.ProvideInput = true;
        _currentRunner.AddCallbacks(this);

        // 2. Récupération explicite du NetworkSceneManagerDefault présent sur le prefab
        var sceneManager = _currentRunner.GetComponent<NetworkSceneManagerDefault>();
        if (sceneManager == null)
        {
            sceneManager = _currentRunner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        var customToken = System.Text.Encoding.UTF8.GetBytes(_playerNickname);

        // 3. Passer le sceneManager dans les arguments d'initialisation Fusion
        var startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            PlayerCount = _isVsAIMode ? 1 : nbOfPlayers,
            ConnectionToken = customToken,
            SceneManager = sceneManager // 👈 Ligne clé pour éviter le freeze de déconnexion
        };

        var result = await _currentRunner.StartGame(startGameArgs);

        if (result.Ok)
        {
            UpdateStatus($"Connecté à la room '{roomName}'. Attente des joueurs...");
            CheckPlayersAndStartGame();
        }
        else
        {
            UpdateStatus($"❌ Échec : {result.ShutdownReason}");

            // 🔴 FIX : si on échoue et qu'on revient au lobby, il faut redonner la main
            // à la logique de lobby (boutons + OnSessionListUpdated) explicitement.
            _isInLobby = true;
            SetAllButtonsInteractable(true);

            await JoinLobbySessionList();
        }
    }



    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[LobbyManager] {message}");
    }

    private void SetAllButtonsInteractable(bool state)
    {
        if (playButton != null) playButton.interactable = state;
        if (playVsAIButton != null) playVsAIButton.interactable = state;
        if (joinRoomButton != null) joinRoomButton.interactable = state;
    }

    private void UpdateButtonsState()
    {
        if (_cogepRoomExists)
        {
            if (playButton != null) playButton.interactable = false;
            if (joinRoomButton != null) joinRoomButton.interactable = true;

            if (roomStatusText != null)
                roomStatusText.text = $"🟢 Room '{GetTargetRoomName()}' disponible !";
        }
        else
        {
            if (playButton != null) playButton.interactable = true;
            if (joinRoomButton != null) joinRoomButton.interactable = false;

            if (roomStatusText != null)
                roomStatusText.text = $"⚪ Aucune partie '{GetTargetRoomName()}' en cours";
        }
    }

    private string GetTargetRoomName()
    {
        return roomNameInput != null && !string.IsNullOrWhiteSpace(roomNameInput.text)
            ? roomNameInput.text.Trim()
            : defaultRoomName;
    }

    #region Fusion Callbacks

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // 🔴 FIX : garde-fou. Si on n'est plus (ou pas encore revenu) dans le lobby,
        // on ignore complètement cet évènement, même s'il provient d'un runner
        // résiduel ou d'une souscription tardive. C'est ce qui empêchait l'UI de
        // "revenir" en état lobby pendant que la partie était déjà en cours.
        if (!_isInLobby) return;

        _cogepRoomExists = false;

        string targetRoom = GetTargetRoomName();

        foreach (var session in sessionList)
        {
            if (session.Name.Equals(targetRoom, StringComparison.OrdinalIgnoreCase) && session.IsOpen && session.IsVisible)
            {
                _cogepRoomExists = true;
                break;
            }
        }

        UpdateButtonsState();
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        CheckPlayersAndStartGame();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        CheckPlayersAndStartGame();
    }

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
    public void OnSceneLoadDone(NetworkRunner runner) { _sceneLoadConfirmed = true; }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    #endregion
}
