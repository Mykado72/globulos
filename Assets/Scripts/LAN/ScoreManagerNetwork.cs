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
    // ✅ Scores synchronisés en réseau
    [Networked] private int Team1ScoreNet { get; set; }
    [Networked] private int Team2ScoreNet { get; set; }

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

        // Notifier l'UI
        NotifyScoreChanged();

        // Vérifier condition de victoire
        CheckWinCondition();
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
}
