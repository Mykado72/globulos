using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Script de test pour une nouvelle scène "Lobby", inspiré du pattern du menu
/// de Fusion Starter (Shared Mode) : JoinSessionLobby -> OnSessionListUpdated
/// -> StartGame -> LoadScene. Compatible WebGL (pas de threads, tout passe par
/// async/await + le SynchronizationContext Unity).
///
/// Corrections déjà appliquées par rapport à ton ancien LobbyManager :
/// - Une seule instanciation du runner de jeu (pas de fuite d'objet).
/// - Le runner précédent est désinscrit (RemoveCallbacks) ET détruit avant
///   d'en créer un nouveau.
/// - Un flag d'état clair (_state) remplace le booléen _isInLobby : impossible
///   d'appliquer par erreur une mise à jour "lobby" pendant qu'on est déjà
///   "en jeu".
/// </summary>
public class TestLobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private enum LobbyState { InLobby, Connecting, InGame }

    [Header("Fusion Setup")]
    [SerializeField] private NetworkRunner runnerPrefab;

    [Header("Game Settings")]
    [SerializeField] private string testRoomName = "TEST_ROOM";
    [SerializeField] private int nbOfPlayers = 2;
    [SerializeField] private string gameSceneName = "GameSceneLAN";

    [Header("UI References")]
    [SerializeField] private TMP_InputField playerNicknameInput;
    [SerializeField] private Button createOrJoinButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI roomStatusText;

    private NetworkRunner _currentRunner;
    private LobbyState _state = LobbyState.InLobby;
    private bool _roomExists = false;
    private bool _isLoadingScene = false;
    private string _playerNickname = "Joueur";

    private async void Start()
    {
        _state = LobbyState.InLobby;

        if (createOrJoinButton != null)
            createOrJoinButton.onClick.AddListener(OnCreateOrJoinPressed);

        _playerNickname = PlayerPrefs.GetString("playerNickname", "Joueur");
        if (playerNicknameInput != null) playerNicknameInput.text = _playerNickname;

        UpdateStatus("Connexion au lobby Fusion...");
        await JoinLobby();
    }

    private void OnDisable()
    {
        if (_currentRunner != null)
            _currentRunner.RemoveCallbacks(this);
    }

    private async Task JoinLobby()
    {
        _currentRunner = Instantiate(runnerPrefab);
        _currentRunner.ProvideInput = true;
        _currentRunner.AddCallbacks(this);

        var result = await _currentRunner.JoinSessionLobby(SessionLobby.Shared);
        UpdateStatus(result.Ok
            ? "Connecté au Lobby. Recherche de sessions..."
            : $"❌ Échec de connexion au Lobby : {result.ShutdownReason}");
    }

    private void OnCreateOrJoinPressed()
    {
        _ = SaveNicknameAndStart();
    }

    private async Task SaveNicknameAndStart()
    {
        if (createOrJoinButton != null) createOrJoinButton.interactable = false;

        // On quitte l'état "lobby" AVANT de toucher au runner : plus aucune
        // mise à jour de la liste de sessions ne pourra modifier l'UI après ce point.
        _state = LobbyState.Connecting;

        if (playerNicknameInput != null && !string.IsNullOrWhiteSpace(playerNicknameInput.text))
            _playerNickname = playerNicknameInput.text.Trim();

        PlayerPrefs.SetString("playerNickname", _playerNickname);
        PlayerPrefs.Save();

        UpdateStatus($"Connexion à la room '{testRoomName}'...");

        // Nettoyage complet de l'ancien runner (lobby) avant d'en créer un nouveau.
        if (_currentRunner != null)
        {
            _currentRunner.RemoveCallbacks(this);
            if (_currentRunner.IsRunning)
                await _currentRunner.Shutdown();
            Destroy(_currentRunner.gameObject);
            _currentRunner = null;
        }

        await StartGameSession();
    }

    private async Task StartGameSession()
    {
        // Seule instanciation du runner de jeu (pas de double Instantiate).
        _currentRunner = Instantiate(runnerPrefab);
        _currentRunner.ProvideInput = true;
        _currentRunner.AddCallbacks(this);

        var sceneManager = _currentRunner.GetComponent<NetworkSceneManagerDefault>()
                            ?? _currentRunner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        var startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = testRoomName,
            PlayerCount = nbOfPlayers,
            ConnectionToken = System.Text.Encoding.UTF8.GetBytes(_playerNickname),
            SceneManager = sceneManager
        };

        var result = await _currentRunner.StartGame(startGameArgs);

        if (result.Ok)
        {
            _state = LobbyState.InGame;
            UpdateStatus($"Connecté à la room '{testRoomName}'. Attente des joueurs...");
            CheckPlayersAndStartGame();
        }
        else
        {
            UpdateStatus($"❌ Échec : {result.ShutdownReason}");
            _state = LobbyState.InLobby;
            if (createOrJoinButton != null) createOrJoinButton.interactable = true;
            await JoinLobby();
        }
    }

    private void CheckPlayersAndStartGame()
    {
        if (_currentRunner == null || !_currentRunner.IsRunning) return;

        int count = 0;
        if (_currentRunner.ActivePlayers != null)
            foreach (var _ in _currentRunner.ActivePlayers) count++;

        UpdateStatus($"Joueurs connectés : {count}/{nbOfPlayers}");

        if (count >= nbOfPlayers && !_isLoadingScene && _currentRunner.IsSharedModeMasterClient)
        {
            _isLoadingScene = true;
            _ = LoadGameSceneAsMaster();
        }
    }

    private async Task LoadGameSceneAsMaster()
    {
        await Task.Delay(1000); // laisse Fusion stabiliser l'autorité Shared Mode

        if (_currentRunner == null || !_currentRunner.IsRunning || !_currentRunner.IsSharedModeMasterClient)
            return;

        int sceneIndex = SceneUtility.GetBuildIndexByScenePath(gameSceneName);
        if (sceneIndex < 0)
            sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{gameSceneName}.unity");

        if (sceneIndex >= 0)
        {
            Debug.Log($"[TestLobbyManager] 🚀 Chargement de la scène '{gameSceneName}' (index {sceneIndex})");
            _currentRunner.LoadScene(SceneRef.FromIndex(sceneIndex));
        }
        else
        {
            Debug.LogError($"[TestLobbyManager] ❌ Scène '{gameSceneName}' introuvable dans les Build Settings !");
            _isLoadingScene = false;
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[TestLobbyManager] {message}");
    }

    #region Fusion Callbacks

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // Garde-fou : on ignore tout événement de liste de sessions dès qu'on
        // n'est plus explicitement en état "lobby".
        if (_state != LobbyState.InLobby) return;

        _roomExists = false;
        foreach (var session in sessionList)
        {
            if (session.Name.Equals(testRoomName, StringComparison.OrdinalIgnoreCase) && session.IsOpen && session.IsVisible)
            {
                _roomExists = true;
                break;
            }
        }

        if (roomStatusText != null)
        {
            roomStatusText.text = _roomExists
                ? $"🟢 Room '{testRoomName}' disponible — rejoindre"
                : $"⚪ Aucune room '{testRoomName}' — en créer une";
        }
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) => CheckPlayersAndStartGame();
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) => CheckPlayersAndStartGame();

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
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    #endregion
}
