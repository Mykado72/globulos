using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// ✅ Gestionnaire de Score RÉSEAU
/// Hérite de ScoreManagerBase mais utilise Fusion pour synchroniser
/// À utiliser EN MODE RÉSEAU UNIQUEMENT (LAN/Online)
/// </summary>
public class ScoreManagerNetwork : ScoreManagerBase, INetworkRunnerCallbacks
{
    // ✅ FIX : [Networked] n'a aucun effet ici — ScoreManagerNetwork hérite de ScoreManagerBase
    // (un simple MonoBehaviour), pas de NetworkBehaviour, donc Fusion ne synchronisait jamais
    // réellement ces deux champs. Ce sont maintenant de simples champs locaux tenus à jour
    // manuellement sur chaque client via TurnManager.BroadcastScoreSync/RPC_SyncScore (voir
    // AddGoal / ApplySyncedScore ci-dessous), TurnManager étant lui un vrai NetworkBehaviour.
    private int Team1ScoreNet;
    private int Team2ScoreNet;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        Initialize();  // ✅ Appelé automatiquement au démarrage
    }

    public override void Initialize()
    {
        Instance = this;
        Team1ScoreNet = 0;
        Team2ScoreNet = 0;
        Team1Score = 0;
        Team2Score = 0;
        Debug.Log("[ScoreManagerNetwork] ✅ ScoreManager initialisé (Mode RÉSEAU)");
    }

    public override void AddGoal(int scorerPlayerId)
    {
        // ✅ Convention : PlayerId impair = Jaune (Team1), pair = Rouge (Team2)
        bool isTeam1 = (scorerPlayerId % 2 != 0);

        if (isTeam1)
        {
            Team1ScoreNet += pointsPerGoal;
            Team1Score = Team1ScoreNet;
            Debug.Log($"[ScoreManagerNetwork] 🎯 BUT ! Équipe Jaune : {Team1ScoreNet} - {Team2ScoreNet}");
        }
        else
        {
            Team2ScoreNet += pointsPerGoal;
            Team2Score = Team2ScoreNet;
            Debug.Log($"[ScoreManagerNetwork] 🎯 BUT ! Équipe Rouge : {Team1ScoreNet} - {Team2ScoreNet}");
        }

        // Notifier l'UI (sur CE client, celui qui a l'autorité sur le ballon)
        NotifyScoreChanged();

        // ✅ FIX : sans ça, seul ce client voyait son score changer. On diffuse le score à
        // jour à tous les autres clients via TurnManager (voir ApplySyncedScore plus bas).
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.BroadcastScoreSync(Team1ScoreNet, Team2ScoreNet);
        }

        // ✅ FIX : cet appel manquait. AddGoal() est entièrement redéfinie ici (elle n'appelle
        // pas base.AddGoal()), donc ResetTurnAfterGoal() n'était jamais déclenché en mode
        // Réseau — TurnManager.RequestTurnReset() n'était donc jamais appelé après un but qui
        // ne terminait pas la partie. Comme dans ScoreManagerBase.AddGoal(), on l'appelle
        // AVANT de vérifier la victoire.
        ResetTurnAfterGoal(scorerPlayerId);

        // Vérifier condition de victoire
        CheckWinCondition();
    }

    /// <summary>
    /// ✅ FIX : en mode Réseau, délègue à TurnManager.RequestTurnReset(), qui programme le
    /// repositionnement des billes/du ballon pour APRÈS la fin de la célébration de but (voir
    /// TurnManager.IsPendingGoalReset), au lieu de le faire immédiatement.
    /// </summary>
    protected override void ResetTurnAfterGoal(int scorerPlayerId)
    {
        Debug.Log($"[ScoreManagerNetwork] 🔄 ResetTurnAfterGoal appelée pour joueur {scorerPlayerId}");

        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.RequestTurnReset();
        }
        else
        {
            Debug.LogWarning("[ScoreManagerNetwork] ⚠️ TurnManager.Instance est NULL !");
        }
    }

    public override (int team1, int team2) GetScores()
    {
        return (Team1ScoreNet, Team2ScoreNet);
    }

    public override string GetScoreDisplay()
    {
        return $"Jaune {Team1ScoreNet} - {Team2ScoreNet} Rouge";
    }

    public override void ResetScores()
    {
        Team1ScoreNet = 0;
        Team2ScoreNet = 0;
        Team1Score = 0;
        Team2Score = 0;
        NotifyScoreChanged();
        Debug.Log("[ScoreManagerNetwork] 🔄 Scores réinitialisés");
    }

    protected override void EndGameWithWinner(int teamIndex)
    {
        if (TurnManager.Instance != null)
        {
            // Trouver un joueur de l'équipe gagnante
            var runner = FindFirstObjectByType<NetworkRunner>();
            if (runner != null)
            {
                foreach (PlayerRef player in runner.ActivePlayers)
                {
                    bool playerTeam1 = (player.PlayerId % 2 != 0);
                    bool isWinner = (teamIndex == 0) ? playerTeam1 : !playerTeam1;

                    if (isWinner)
                    {
                        TurnManager.Instance.RequestWinBySoccerGoal(player.PlayerId);
                        return;
                    }
                }
            }
        }
    }

    // ========== INetworkRunnerCallbacks (pour compatibilité) ==========
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    
    // Ajout de la méthode manquante pour corriger CS1061
    public void ApplySyncedScore(int team1Score, int team2Score)
    {
        Team1ScoreNet = team1Score;
        Team2ScoreNet = team2Score;
        NotifyScoreChanged();
    }
}