using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("UI Components")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text statusText;


    [SerializeField] private string gameSceneName = "GameScene";

    #if UNITY_EDITOR

        [SerializeField] private SceneAsset sceneAsset;

        private void OnValidate()
        {
            if (sceneAsset != null)
            {
                gameSceneName = sceneAsset.name;
            }
        }
    #endif

    [Header("Matchmaking Settings")]

    private const int MAX_PLAYERS = 2; // 1v1 pour Globulos

    private NetworkRunner _runner;

    private void Start()
    {
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonPressed);
        }
        UpdateStatus("Entrez votre pseudo pour jouer.");
    }

    private async void OnPlayButtonPressed()
    {
        string nickname = nicknameInput != null ? nicknameInput.text.Trim() : "";

        if (string.IsNullOrEmpty(nickname))
        {
            UpdateStatus("<color=red>Veuillez entrer un pseudo !</color>");
            return;
        }

        playButton.interactable = false;
        nicknameInput.interactable = false;
        UpdateStatus("Initialisation du réseau...");

        if (_runner == null)
        {
            _runner = gameObject.AddComponent<NetworkRunner>();
        }

        _runner.AddCallbacks(this);
        _runner.ProvideInput = true;

        UpdateStatus("Recherche d'un adversaire avec du charisme...");
        // Récupère l'index de la scène actuelle (LobbyScene)
        int currentSceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

        var startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = "", // Session aléatoire
            PlayerCount = MAX_PLAYERS,
            Scene = SceneRef.FromIndex(currentSceneIndex), // <-- Définit la scène initiale pour le Runner
            // SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await _runner.StartGame(startGameArgs);

        if (!result.Ok)
        {
            UpdateStatus($"<color=red>Échec : {result.ShutdownReason}</color>");
            ResetUI();
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void ResetUI()
    {
        if (playButton != null) playButton.interactable = true;
        if (nicknameInput != null) nicknameInput.interactable = true;
    }

    // =========================================================================
    // CALLBACKS GÉRÉS
    // =========================================================================

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        int currentPlayers = runner.ActivePlayers.Count();
        UpdateStatus($"Joueurs dans le salon : {currentPlayers}/{MAX_PLAYERS}");

        if (currentPlayers == MAX_PLAYERS)
        {
            UpdateStatus("Partie trouvée ! Chargement du terrain...");

            if (runner.IsSharedModeMasterClient)
            {
                // Récupère l'index de la scène dans le Build Settings
                int sceneIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(gameSceneName);

                if (sceneIndex >= 0)
                {
                    runner.LoadScene(SceneRef.FromIndex(sceneIndex));
                }
                else
                {
                    Debug.LogError($"[LobbyManager] La scène '{gameSceneName}' n'a pas été trouvée dans les Build Settings !");
                }
            }
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        UpdateStatus("L'adversaire s'est déconnecté.");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        UpdateStatus($"Déconnecté ({shutdownReason}).");
        ResetUI();
    }

    // =========================================================================
    // CALLBACKS OBLIGATOIRES FUSION 2.1.2 (SIGNATURES EXACTES)
    // =========================================================================

    public void OnObjectReady(NetworkRunner runner, NetworkObject obj) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}