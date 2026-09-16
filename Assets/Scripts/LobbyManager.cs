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
    [Header("Configuration")]
    [SerializeField] private string defaultRoomName = "Room1";
    [SerializeField] private NetworkRunner runnerPrefab;

    [Header("UI")]
    [SerializeField] private InputField roomNameInput;
    [SerializeField] private Button playButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI playersNickname;

    private NetworkRunner _currentRunner;

    private void Start()
    {
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonPressed);
        }
    }

    public async void OnPlayButtonPressed()
    {
        if (playButton != null) playButton.interactable = false;

        string roomName = defaultRoomName;
        if (roomNameInput != null && !string.IsNullOrEmpty(roomNameInput.text))
        {
            roomName = roomNameInput.text;
        }

        UpdateStatus("Connexion à la room...");
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

        // Enregistre ce script pour écouter les callbacks de connexion (OnPlayerJoined, etc.)
        _currentRunner.AddCallbacks(this);

        INetworkSceneManager sceneManager = null;
        // Correction : TryGetComponent ne crée pas d'allocation si le composant n'existe pas
        if (!_currentRunner.TryGetComponent<INetworkSceneManager>(out sceneManager) || sceneManager == null)
        {
            // Utiliser le type concret correct fourni par Fusion
            var concreteSceneManager = _currentRunner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            sceneManager = concreteSceneManager;
        }

        // En Shared Mode, on rejoint la session sans charger de scène immédiatement
        var result = await _currentRunner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            SceneManager = sceneManager
        });

        if (result.Ok)
        {
            Debug.Log($"[LobbyManager] Connecté à '{roomName}'. En attente du second joueur...");
            CheckPlayersAndStartGame();
        }
        else
        {
            Debug.LogError($"[LobbyManager] Échec : {result.ShutdownReason}");
            UpdateStatus($"Échec : {result.ShutdownReason}");
            if (playButton != null) playButton.interactable = true;
        }
    }

    // Callback Fusion : appelé chaque fois qu'un joueur rejoint la room
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[LobbyManager] Un joueur a rejoint : {player}");
        CheckPlayersAndStartGame();
    }

    private void CheckPlayersAndStartGame()
    {
        if (_currentRunner == null) return;

        int count = _currentRunner.ActivePlayers != null ? System.Linq.Enumerable.Count(_currentRunner.ActivePlayers) : 0;
        UpdateStatus($"Joueurs connectés : {count}/2");

        // Seul le MasterClient (hôte) déclenche la transition de scène quand il y a au moins 2 joueurs
        if (_currentRunner.IsSharedModeMasterClient && count >= 2)
        {
            Debug.Log("[LobbyManager] 2 joueurs détectés ! Chargement de GameScene...");
            UpdateStatus("Lancement de la partie !");

            var sceneRef = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath("GameScene"));
            _currentRunner.LoadScene(sceneRef);
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    // Interfaçage obligatoire d'INetworkRunnerCallbacks
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
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