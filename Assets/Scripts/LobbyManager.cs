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

/// ✅ VERSION AMÉLIORÉE
/// - Gère la saisie du pseudo
/// - Stocke le pseudo via PlayerPrefs (persiste entre scènes)
/// - Passe le pseudo à PlayerNamesManager et PlayerData
public class LobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Configuration")]
    [SerializeField] private string defaultRoomName = "COGEP";
    [SerializeField] private NetworkRunner runnerPrefab;

    [Header("UI")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_InputField playerNicknameInput;  
    [SerializeField] private Button playButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI playersNickname;

    private NetworkRunner _currentRunner;
    private string _playerNickname;  // ✅ Stocke le pseudo local

    private void Start()
    {
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonPressed);
        }

        // ✅ Charge le pseudo sauvegardé (si existe)
        _playerNickname = PlayerPrefs.GetString("PlayerNickname", "Joueur");
        if (playerNicknameInput != null)
        {
            playerNicknameInput.text = _playerNickname;
        }
    }

    public async void OnPlayButtonPressed()
    {
        if (playButton != null) playButton.interactable = false;

        // ✅ Récupère et valide le pseudo
        if (!string.IsNullOrEmpty(playerNicknameInput.text))
        {
            _playerNickname = playerNicknameInput.text.Trim();
        }

        if (string.IsNullOrEmpty(_playerNickname))
        {
            _playerNickname = "Joueur";
        }

        // ✅ Sauvegarde le pseudo
        PlayerPrefs.SetString("PlayerNickname", _playerNickname);
        PlayerPrefs.Save();
        Debug.Log($"[LobbyManager] ✅ Pseudo enregistré : {_playerNickname}");

        string roomName = defaultRoomName;
        if (roomNameInput != null && !string.IsNullOrEmpty(roomNameInput.text))
        {
            roomName = roomNameInput.text;
        }

        UpdateStatus($"Connexion en tant que '{_playerNickname}'...");
        await StartGameSession(roomName);
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

        // ✅ Envoie le pseudo au serveur Fusion via le ConnectionToken
        byte[] token = System.Text.Encoding.UTF8.GetBytes(_playerNickname);

        var result = await _currentRunner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            SceneManager = sceneManager,
            ConnectionToken = token // 👈 Transmission réseau du pseudo
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
        Debug.Log($"[LobbyManager] 👤 Joueur {player.PlayerId} : {_playerNickname} a rejoint");

        // ✅ NOUVEAU : Enregistrer le pseudo du joueur local
        if (player == runner.LocalPlayer)
        {
            if (PlayerNamesManager.Instance != null)
            {
                PlayerNamesManager.Instance.SetPlayerName(player.PlayerId, _playerNickname);
                Debug.Log($"[LobbyManager] ✅ Pseudo du joueur local enregistré : {_playerNickname}");
            }
        }

        CheckPlayersAndStartGame();
    }

    private void CheckPlayersAndStartGame()
    {
        if (_currentRunner == null) return;

        int count = _currentRunner.ActivePlayers != null ? System.Linq.Enumerable.Count(_currentRunner.ActivePlayers) : 0;

        if (playersNickname != null)
        {
            string nicknames = "";
            foreach (var player in _currentRunner.ActivePlayers)
            {
                if (!string.IsNullOrEmpty(nicknames)) nicknames += " vs ";

                // Récupère le pseudo depuis le Token si PlayerNamesManager n'a pas encore reçu la donnée réseau
                byte[] token = _currentRunner.GetPlayerConnectionToken(player);
                string name = (token != null && token.Length > 0)
                    ? System.Text.Encoding.UTF8.GetString(token)
                    : $"Joueur {player.PlayerId}";

                nicknames += name;
            }
            playersNickname.text = nicknames;
        }

        UpdateStatus($"Joueurs connectés : {count}/2");

        if (_currentRunner.IsSharedModeMasterClient && count >= 2)
        {
            var sceneRef = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath("GameScene"));
            _currentRunner.LoadScene(sceneRef);
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    // === INetworkRunnerCallbacks ===
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
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