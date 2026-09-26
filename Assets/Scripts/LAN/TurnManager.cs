using Fusion;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;
using UnityEngine.SceneManagement;

/// ✅ Mode NETWORK (Fusion) : Implémente ITurnManagerCore
/// Logique synchronisée serveur/client via RPC et propriétés [Networked]
public partial class TurnManager : NetworkBehaviour, ITurnManagerCore
{
    private bool _pendingGoalReset = false;
    [Header("Game Mode Configuration")]
    [SerializeField] private bool defaultTurnBasedMode = true;

    [Header("Turn-Based Settings")]
    [SerializeField] private float aimDuration = 15f;
    [SerializeField] private float resolutionSettleDuration = 0.2f;

    [Tooltip("Durée de l'état Celebrating (voir GoalCelebrationUI) avant de passer en Finished. Doit correspondre à peu près à la durée totale de l'animation de célébration.")]
    [SerializeField] private float celebrationDuration = 2.2f;

    // ✅ État réseau synchronisé (Serveur → Clients)
    [Networked] public NetworkBool IsTurnBased { get; set; }
    [Networked] public TurnState CurrentState { get; set; }
    [Networked] private TickTimer TurnTimer { get; set; }
    [Networked] private TickTimer ResolutionSettleTimer { get; set; }
    [Networked] public int CurrentTurnNumber { get; set; }
    [Networked] public int WinnerPlayerId { get; set; }

    // ✅ FIX : Timers et données pour la célébration (synchronisés sur tous les clients)
    [Networked] private TickTimer CelebrationTimer { get; set; }
    [Networked] private int CelebrationWinnerId { get; set; }

    // ✅ FIX : identique dans l'esprit à _pendingGoalReset côté LocalTurnManager, mais
    // synchronisé via Fusion puisque plusieurs clients exécutent FixedUpdateNetwork.
    // Fige la progression normale des états le temps que la célébration de but ("BUT !")
    // se termine, avant de replacer les billes/le ballon. Volontairement séparé de
    // TurnState.Celebrating pour ne pas interférer avec RequestWinBySoccerGoal (qui, lui,
    // ignore les appels quand CurrentState == Celebrating).
    [Networked] private TickTimer GoalResetTimer { get; set; }
    [Networked] private NetworkBool IsPendingGoalReset { get; set; }

    // ============================================
    // 🆕 Handshake "tous les joueurs prêts avant de spawner"
    // ============================================
    // La scène de jeu est chargée dès que le lobby atteint le nombre de joueurs
    // requis (voir LobbyManager.CheckPlayersAndStartGame). Mais chaque client charge
    // sa PROPRE instance de la scène à sa propre vitesse (réseau, mémoire, etc.).
    // Sans ce garde-fou, GameSpawner spawnait le joueur local dès que SA scène à lui
    // était chargée, sans attendre que l'autre client ait fini la sienne : la partie
    // (et le premier tour) pouvait démarrer alors qu'un des deux joueurs n'était pas
    // encore réellement présent sur le terrain.
    //
    // Principe : chaque GameSpawner appelle NotifyPlayerReadyToSpawn() dès que sa
    // scène est chargée et son LocalPlayer valide. Le State Authority (Master) compte
    // les joueurs prêts ; une fois que tous ont signalé, il diffuse RPC_BeginGameplay
    // à tout le monde, qui déclenche l'événement statique OnAllPlayersReadyToSpawn.
    // C'est UNIQUEMENT à ce moment-là que GameSpawner spawn réellement les billes et
    // le ballon.
    [Networked, Capacity(8)] private NetworkDictionary<PlayerRef, NetworkBool> PlayersReadyToSpawn => default;
    [Networked] private int RequiredPlayersForSpawn { get; set; }
    private bool _gameplayBegun = false;

    /// <summary>
    /// 🆕 Diffusé sur TOUS les clients (y compris le State Authority) une fois que
    /// tous les joueurs ont signalé être prêts à spawner. GameSpawner s'y abonne pour
    /// savoir quand spawner réellement le joueur local / le ballon.
    /// </summary>
    public static event Action OnAllPlayersReadyToSpawn;

    public static TurnManager Instance { get; private set; }

    public override void Spawned()
    {
        Instance = this;

        // ✅ CLIENT/SERVER : Seul le serveur initialise
        if (HasStateAuthority)
        {
            IsTurnBased = defaultTurnBasedMode;
            WinnerPlayerId = -1;

            // 🆕 On ne démarre PAS encore le premier tour ici : on attend que tous les
            // joueurs aient confirmé être prêts à spawner (voir RPC_BeginGameplay).
            RequiredPlayersForSpawn = CountActivePlayers();

            Debug.Log($"[TurnManager] ✅ Serveur initialisé (Mode NETWORK) - en attente de {RequiredPlayersForSpawn} joueur(s) prêt(s) à spawner");
        }
        else
        {
            Debug.Log("[TurnManager] ℹ️ Client : reçoit les mises à jour du serveur");
        }
    }

    private int CountActivePlayers()
    {
        int count = 0;
        if (Runner != null && Runner.ActivePlayers != null)
        {
            foreach (var p in Runner.ActivePlayers)
            {
                count++;
            }
        }
        return count;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ============================================
    // 🆕 Handshake de démarrage - appelé par GameSpawner
    // ============================================

    /// <summary>
    /// 🆕 À appeler par GameSpawner dès que le client local a fini de charger sa scène
    /// et que son LocalPlayer est valide. N'effectue AUCUN spawn ici : ça ne fait que
    /// signaler la disponibilité au State Authority.
    /// </summary>
    public void NotifyPlayerReadyToSpawn(PlayerRef player)
    {
        RPC_NotifyPlayerReadyToSpawn(player);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_NotifyPlayerReadyToSpawn(PlayerRef player)
    {
        if (!HasStateAuthority || _gameplayBegun) return;

        PlayersReadyToSpawn.Set(player, true);

        Debug.Log($"[TurnManager] 🟢 Joueur {player.PlayerId} prêt à spawner ({PlayersReadyToSpawn.Count}/{RequiredPlayersForSpawn})");

        if (RequiredPlayersForSpawn > 0 && PlayersReadyToSpawn.Count >= RequiredPlayersForSpawn)
        {
            _gameplayBegun = true;
            RPC_BeginGameplay();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BeginGameplay()
    {
        Debug.Log("[TurnManager] 🚀 Tous les joueurs sont prêts - démarrage effectif de la partie");

        // Prévient GameSpawner (sur CE client) qu'il peut maintenant spawner réellement.
        OnAllPlayersReadyToSpawn?.Invoke();

        // Le premier tour ne démarre que maintenant, et uniquement côté State Authority.
        if (HasStateAuthority)
        {
            if (IsTurnBased)
            {
                StartNewTurn();
            }
            else
            {
                CurrentState = TurnState.RealTime;
                TurnTimer = TickTimer.None;
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        // ✅ CLIENT/SERVER : Seul le serveur gère la logique
        if (!HasStateAuthority || !IsTurnBased) return;

        // 🆕 Tant que tous les joueurs ne sont pas prêts, la logique de tour ne doit
        // pas tourner (CurrentState vaut encore sa valeur par défaut / précédente).
        if (!_gameplayBegun) return;

        // ✅ FIX : un but vient d'être marqué mais la partie continue (voir
        // ScoreManagerNetwork.ResetTurnAfterGoal -> RequestTurnReset). On attend que la
        // célébration ("BUT !") soit terminée avant de replacer billes + ballon, au lieu de
        // le faire immédiatement (ce qui les faisait sauter à leur position de spawn pendant
        // que le texte "BUT !" était encore affiché).
        if (IsPendingGoalReset)
        {
            if (GoalResetTimer.Expired(Runner))
            {
                IsPendingGoalReset = false;

                // Si ce but a en fait déclenché la victoire entre-temps (RequestWinBySoccerGoal
                // a pu être appelé juste après, voir ScoreManagerNetwork.CheckWinCondition), on
                // ne repositionne rien : la partie se termine / la scène va être rechargée.
                if (CurrentState != TurnState.Finished && CurrentState != TurnState.Celebrating)
                {
                    RPC_ResetAllForNewRound();
                    StartNewTurn();
                    Debug.Log("[TurnManager] ✅ Tour réinitialisé - Prêt pour le prochain joueur");
                }
            }
            return;
        }

        switch (CurrentState)
        {
            case TurnState.Aiming:
                if (_pendingGoalReset) break; // ✅ FIX : figé pendant la célébration de but
                if (TurnTimer.Expired(Runner))
                {
                    RPC_ForceStopAiming();
                    ExecuteTurnResolution();
                }                
                break;

            case TurnState.Resolution:
                if (_pendingGoalReset) break; // ✅ FIX : figé pendant la célébration de but
                if (!ResolutionSettleTimer.ExpiredOrNotRunning(Runner)) break;

                if (AreAllBallsStopped())
                {
                    CurrentState = TurnState.CheckResult;
                }
                break;

            case TurnState.CheckResult:
                if (_pendingGoalReset) break; // ✅ FIX : figé pendant la célébration de but
                if (CurrentState == TurnState.CheckResult)
                {
                    CheckGameEnd();
                    StartNewTurn();
                }
                break;

            case TurnState.Celebrating:
                // ✅ FIX : Gestion de l'état Celebrating avec timer synchronisé
                if (CelebrationTimer.Expired(Runner))
                {
                    Debug.Log($"[TurnManager] 🎉 Fin de célébration → Terminer le jeu (Gagnant: {CelebrationWinnerId})");
                }
                break;

            default:
                CheckGameEnd();
                Debug.LogWarning($"[TurnManager] ⚠️ État non géré : {CurrentState}");
                break;
        }
    }

    // ============================================
    // ✅ Implémentation ITurnManagerCore
    // ============================================

    public void StartNewTurn()
    {
        CurrentTurnNumber++;
        CurrentState = TurnState.Aiming;
        TurnTimer = TickTimer.CreateFromSeconds(Runner, aimDuration);

        // Debug.Log($"[TurnManager] 🎮 Tour {CurrentTurnNumber} (Mode NETWORK)");
    }

    public float GetRemainingTime()
    {
        if (IsTurnBased && TurnTimer.IsRunning)
        {
            return TurnTimer.RemainingTime(Runner) ?? 0f;
        }
        return 0f;
    }

    public string GetPlayerName(int playerId)
    {
        // ✅ Cherche d'abord dans les PlayerData réseau
        foreach (var player in FindObjectsByType<PlayerData>(FindObjectsSortMode.None))
        {
            if (player.Object != null && player.Object.InputAuthority.PlayerId == playerId)
            {
                return player.Nickname;
            }
        }

        // ✨ Fallback : nom enregistré localement (pour le bot en vs IA réseau)
        if (PlayerNamesManager.Instance != null)
        {
            return PlayerNamesManager.Instance.GetPlayerName(playerId);
        }

        return $"Joueur {playerId}";
    }

    public void RequestWinBySoccerGoal(int winnerId)
    {
        // ✅ RPC : n'importe quel client peut signaler un but, serveur traite
        RPC_RequestWinBySoccerGoal(winnerId);
    }

    public void CheckGameEnd()
    {
        // ✅ Seul le serveur exécute cette vérification
        if (!HasStateAuthority) return;

        List<BallAimController> allBalls = BallAimController.AllBalls;

        Dictionary<int, int> aliveBallsPerPlayer = new Dictionary<int, int>();
        HashSet<int> allPlayerIds = new HashSet<int>();

        foreach (BallAimController ball in allBalls)
        {
            int playerId = ball.OwnerPlayerId; // Correction ici
            allPlayerIds.Add(playerId);

            if (!aliveBallsPerPlayer.ContainsKey(playerId))
                aliveBallsPerPlayer[playerId] = 0;

            if (!ball.IsDead)
                aliveBallsPerPlayer[playerId]++;
        }

        Debug.Log("[TurnManager] État des billes:");
        foreach (var kvp in aliveBallsPerPlayer)
        {
            Debug.Log($"  PlayerId {kvp.Key}: {kvp.Value} billes vivantes");
        }

        int playersWithNoBalls = 0;
        int lastAlivePlayer = -1;

        foreach (int playerId in allPlayerIds)
        {
            if (!aliveBallsPerPlayer.ContainsKey(playerId) || aliveBallsPerPlayer[playerId] == 0)
            {
                playersWithNoBalls++;
            }
            else
            {
                lastAlivePlayer = playerId;
            }
        }

        if (playersWithNoBalls >= 2)
        {
            Debug.Log("[TurnManager] 🤝 ÉGALITÉ - Les deux joueurs n'ont plus de balles!");

            // ✅ RPC pour que TOUS les clients voient l'animation
            RPC_PlayDRAWCelebration();

            StartCoroutine(ResetAfterCelebration());
            return;
        }

        if (playersWithNoBalls == 1 && lastAlivePlayer >= 0)
        {
            string winnerName = GetPlayerName(lastAlivePlayer);
            Debug.Log($"[TurnManager] 🎊 Joueur {lastAlivePlayer} à tué l'adversaire !");

            // ✅ RPC pour que TOUS les clients voient l'animation
            RPC_PlayKILLERCelebration(winnerName);

            StartCoroutine(ResetAfterCelebration());
            return;
        }
    }

    public bool IsAnyBallMoving()
    {
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null && ball.IsMoving) return true;
        }
        return false;
    }

    public void ForceStopAiming()
    {
        // ✅ RPC : tous les clients reçoivent l'ordre d'arrêt
        RPC_ForceStopAiming();
    }

    // ============================================
    // ✅ Logique interne + RPC
    // ============================================

    private void ExecuteTurnResolution()
    {
        CurrentState = TurnState.Resolution;
        TurnTimer = TickTimer.None;
        ResolutionSettleTimer = TickTimer.CreateFromSeconds(Runner, resolutionSettleDuration);

        // ✅ Déclenche l'application simultanée des forces préparées sur tous les clients
        RPC_ExecuteAllShots();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ExecuteAllShots()
    {
        // Debug.Log("[TurnManager] 💥 Passage en Résolution : Exécution des tirs enregistrés !");
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null)
            {
                ball.ExecuteQueuedShot();
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ForceStopAiming()
    {
        // Debug.Log("[TurnManager] ⏰ Timer écoulé - Force l'arrêt du visage");
        foreach (var ball in BallAimController.AllBalls)
        {
            ball.ForceStopAiming();
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestWinBySoccerGoal(int winnerId)
    {
        if (!HasStateAuthority) return;
        if (CurrentState == TurnState.Finished || CurrentState == TurnState.Celebrating)
        {
            Debug.LogWarning($"[TurnManager] ⚠️ Tentative de BUT quand état = {CurrentState}. Ignorée.");
            return;
        }

        // ✅ Geler le timer affiché : TurnTimer.RemainingTime continuerait sinon de décompter
        // en temps réel même hors des cases gérées par FixedUpdateNetwork (contrairement au
        // mode Local où l'absence de case correspondante dans Update() suffit à figer _timer).
        TurnTimer = TickTimer.None;
        CurrentState = TurnState.Celebrating;

        // ✅ FIX : Utiliser un timer synchronisé au lieu d'une coroutine
        // Cela garantit que TOUS les clients (serveur ET clients) attendent exactement le même délai
        CelebrationTimer = TickTimer.CreateFromSeconds(Runner, celebrationDuration);
        CelebrationWinnerId = winnerId;
        
        Debug.Log($"[TurnManager] 🎉 CELEBRATION START - PlayerId: {winnerId}, " +
                  $"Duration: {celebrationDuration}s");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayDRAWCelebration()
    {
        GoalCelebrationUI.Instance?.PlayDRAWCelebration();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayKILLERCelebration(string winnerName)
    {
        GoalCelebrationUI.Instance?.PlayKILLERCelebration(winnerName);
    }


    private bool AreAllBallsStopped()
    {
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null && !ball.IsDead && ball.IsMoving)
                return false;
        }
        return true;
    }
    /// <summary>
    /// ✅ FIX : Réinitialise le tour SANS terminer la partie, utilisé après un but si la
    /// partie continue (ScoreManagerNetwork.ResetTurnAfterGoal). L'ancienne version mettait
    /// CurrentState = TurnState.TakingTurn, un état non géré par FixedUpdateNetwork (tombait
    /// dans le "default" avec un warning) et ne repositionnait ni les billes ni le ballon.
    /// Le repositionnement réel n'a lieu qu'à la fin de la célébration de but, voir
    /// IsPendingGoalReset dans FixedUpdateNetwork().
    /// </summary>
    public void RequestTurnReset()
    {
        RPC_RequestTurnReset();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestTurnReset()
    {
        if (!HasStateAuthority) return;
        if (CurrentState == TurnState.Finished || CurrentState == TurnState.Celebrating) return;
        if (IsPendingGoalReset) return; // reset déjà programmé

        Debug.Log($"[TurnManager] 🔄 But marqué, partie continue - reset programmé dans {celebrationDuration}s (fin de célébration)");

        IsPendingGoalReset = true;
        GoalResetTimer = TickTimer.CreateFromSeconds(Runner, celebrationDuration);
    }

    /// <summary>
    /// ✅ FIX : diffusé à tous les clients une fois la célébration de but terminée. Chaque
    /// bille et le ballon se remettent eux-mêmes à leur position de spawn d'origine (voir
    /// BallAimController.ResetForNewRound / SoccerBallController.ResetForNewRound), sur le
    /// même principe que RPC_AnimateGoalBall pour l'animation de but.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ResetAllForNewRound()
    {
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null) ball.ResetForNewRound();
        }

        if (SoccerBallController.Instance != null)
        {
            SoccerBallController.Instance.ResetForNewRound();
        }
    }

    public void BroadcastScoreSync(int team1Score, int team2Score)
    {
        RPC_SyncScore(team1Score, team2Score);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SyncScore(int team1Score, int team2Score)
    {
        // Le pair autoritaire a appelé BroadcastScoreSync juste après avoir déjà mis à jour
        // et notifié son propre ScoreManagerNetwork local (voir ScoreManagerNetwork.AddGoal) —
        // on ne réapplique donc que sur les AUTRES clients, pour éviter un double
        // NotifyScoreChanged() sur celui qui a marqué le but.
        if (HasStateAuthority) return;

        var scoreManager = ScoreManagerNetwork.Instance as ScoreManagerNetwork;
        if (scoreManager != null)
        {
            scoreManager.ApplySyncedScore(team1Score, team2Score);
        }
    }

    private IEnumerator ResetAfterCelebration()
    {
        yield return new WaitForSeconds(celebrationDuration);
        RPC_ResetAllForNewRound();
    }

}
