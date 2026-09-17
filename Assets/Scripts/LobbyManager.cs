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
    [SerializeField] private int nbOfPlayers = 3;

    [Header("UI")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_InputField playerNicknameInput;  
    [SerializeField] private Button playButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI playersNickname;
    [SerializeField] private TextMeshProUGUI playersListText;

    private NetworkRunner _currentRunner;
    public string playerNickname;  // ✅ Stocke le pseudo local

    private void Start()
    {
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonPressed);
        }

        // ✅ Charge le pseudo sauvegardé (si existe)
        playerNickname = PlayerPrefs.GetString("PlayerNickname", "Joueur");
        if (playerNicknameInput != null)
        {
            playerNicknameInput.text = playerNickname;
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
        PlayerPrefs.SetString("PlayerNickname", playerNickname);
        PlayerPrefs.Save();
        
        string roomName = defaultRoomName;
        if (roomNameInput != null && !string.IsNullOrEmpty(roomNameInput.text))
        {
            roomName = roomNameInput.text;
        }

        UpdateStatus($"Connexion en tant que '{playerNickname}'...");
        await StartGameSession(roomName);
    }

    public void RefreshPlayersList()
    {
        if (_currentRunner == null) return;

        string playersList = "🎮 Joueurs connectés:\n";
        string nickname = "";

        foreach (var playerData in GetAllPlayerData())
        {
            nickname = playerData.GetNickname();
            playersList += $"✅ {nickname}\n";

        }

        if (playersListText != null)
        {
            playersListText.text = playersList;
        }
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

        CheckPlayersAndStartGame();
        RefreshPlayersList();
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

        UpdateStatus($"Joueurs connectés : {count}/{nbOfPlayers}");
        if (_currentRunner.IsSharedModeMasterClient && count >= nbOfPlayers)
        {
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
            if ((playerData.Object != null) && (playerData.PlayerId == playerId))
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