using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// ✅ Mode LOCAL : Implémente ITurnManagerCore
/// Gestion directe sans Fusion, Update classique
public class LocalTurnManager : MonoBehaviour, ITurnManagerCore
{
    [Header("Paramètres")]
    [SerializeField] private float aimDuration = 15f;
    [SerializeField] private float resolutionSettleDuration = 0.2f;

    [Tooltip("Durée de l'état Celebrating (voir GoalCelebrationUI) avant de passer en Finished. Doit correspondre à peu près à la durée totale de l'animation de célébration.")]
    [SerializeField] private float celebrationDuration = 2.2f;

    public TurnState CurrentState { get; private set; }
    public int CurrentTurnNumber { get; private set; }
    public int WinnerPlayerId { get; private set; } = -1;

    public static LocalTurnManager Instance { get; private set; }

    private float _timer;
    private float _settleTimer;

    // ✅ FIX : fige la progression normale de Update() (Resolution -> CheckResult -> Aiming)
    // pendant que la célébration de but ("BUT !") est affichée, sans toucher à CurrentState
    // (pour ne pas interférer avec RequestWinBySoccerGoal, qui ignore les appels quand
    // CurrentState == Celebrating).
    private bool _pendingGoalReset = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("[LocalTurnManager] Une instance existe déjà !");
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Debug.Log($"[LocalTurnManager] ✅ Démarrage du TurnManager (Mode LOCAL)");
        StartNewTurn();
    }

    private void Update()
    {
        switch (CurrentState)
        {
            case TurnState.Aiming:
                if (_pendingGoalReset) break; // ✅ FIX : figé pendant la célébration de but
                _timer -= Time.deltaTime;

                if (_timer <= 0f)
                {
                    ForceStopAiming();
                    ExecuteTurnResolution();
                }
                break;

            case TurnState.Resolution:
                if (_pendingGoalReset) break; // ✅ FIX : figé pendant la célébration de but
                _settleTimer -= Time.deltaTime;
                if (_settleTimer <= 0f && AreAllBallsStopped())
                {
                    CurrentState = TurnState.CheckResult;
                }
                break;

            case TurnState.CheckResult:
                if (_pendingGoalReset) break; // ✅ FIX : figé pendant la célébration de but
                CheckGameEnd();
                if (CurrentState == TurnState.CheckResult)
                {
                    StartNewTurn();
                }
                break;
            case TurnState.Celebrating:
                // ✅ FIX : Gestion de l'état Celebrating avec timer synchronisé
                /*if (CelebrationTimer.Expired(Runner))
                {
                    Debug.Log($"[TurnManager] 🎉 Fin de célébration → Terminer le jeu (Gagnant: {CelebrationWinnerId})");
                }*/
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
        _timer = aimDuration;

    }

    public float GetRemainingTime() => Mathf.Max(0f, _timer);

    public string GetPlayerName(int playerId)
    {
        if (PlayerNamesManager.Instance != null)
            return PlayerNamesManager.Instance.GetPlayerName(playerId);

        return $"Joueur {playerId}";
    }

    public void RequestTurnReset()
    {
        Debug.Log("[LocalTurnManager] 🔄 Reset du tour demandé après un but (attente fin de célébration)");

        // ✅ FIX : on ne replace plus les billes/le ballon immédiatement. On attend la fin
        // de l'animation de célébration ("BUT !") avant de le faire, sinon tout saute à sa
        // position de spawn pendant que le texte "BUT !" est encore affiché.
        StartCoroutine(ResetTurnAfterCelebration());
    }

    /// <summary>
    /// ✅ FIX : attend la durée de la célébration de but avant de replacer les billes/le
    /// ballon et de relancer un nouveau tour. Pendant l'attente, _pendingGoalReset fige
    /// Update() pour éviter qu'un nouveau tour ne démarre "en double" via le flux normal
    /// (Resolution -> CheckResult -> StartNewTurn) pendant que ce reset est en cours.
    /// </summary>
    private IEnumerator ResetTurnAfterCelebration()
    {
        _pendingGoalReset = true;

        yield return new WaitForSeconds(celebrationDuration);

        _pendingGoalReset = false;

        // Si la partie s'est terminée entre-temps (ce but a déclenché la victoire, voir
        // ScoreManagerLocal.EndGameWithWinner -> RequestWinBySoccerGoal), on ne repositionne
        // rien : la scène va être rechargée par ReloadSceneAfterDelay().
        if (CurrentState == TurnState.Finished || CurrentState == TurnState.Celebrating)
        {
            yield break;
        }

        LocalGameSpawner.Instance?.ResetAllToSpawnPoints();

        // ✅ Réinitialiser pour le prochain tour
        StartNewTurn();

        Debug.Log("[LocalTurnManager] ✅ Tour réinitialisé - Prêt pour le prochain joueur");
    }

    public void RequestWinBySoccerGoal(int winnerId)
    {
        Debug.Log($"[LocalTurnManager] ⚽ But marqué par le joueur {winnerId}!");

        if (CurrentState == TurnState.Finished || CurrentState == TurnState.Celebrating) return;

        StartCoroutine(CelebrateThenEndGame(winnerId));
    }

    /// <summary>
    /// ✅ Bascule en Celebrating (le timer se fige automatiquement, aucun case du switch
    /// de Update() ne correspondant à cet état), laisse jouer GoalCelebrationUI, puis
    /// termine réellement la partie.
    /// </summary>
    private IEnumerator CelebrateThenEndGame(int winnerId)
    {
        CurrentState = TurnState.Celebrating;
        yield return new WaitForSeconds(celebrationDuration);
        EndGame(winnerId);
    }

    public void CheckGameEnd()
    {
        Dictionary<int, int> aliveBallsPerPlayer = new Dictionary<int, int>();
        HashSet<int> allPlayerIds = new HashSet<int>();

        foreach (LocalBallAimController ball in LocalBallAimController.AllBalls)
        {
            if (ball == null) continue;
            int playerId = ball.OwnerPlayerId;
            allPlayerIds.Add(playerId);

            if (!aliveBallsPerPlayer.ContainsKey(playerId))
                aliveBallsPerPlayer[playerId] = 0;
            if (!ball.IsDead)
                aliveBallsPerPlayer[playerId]++;
        }

        // Debug
        foreach (var kvp in aliveBallsPerPlayer)
        {
        }

        int playersWithNoBalls = 0;
        int lastAlivePlayer = -1;

        foreach (int playerId in allPlayerIds)
        {
            if (!aliveBallsPerPlayer.ContainsKey(playerId) || aliveBallsPerPlayer[playerId] == 0)
                playersWithNoBalls++;
            else
                lastAlivePlayer = playerId;
        }

        if (playersWithNoBalls >= 2)
        {
            Debug.Log("[LocalTurnManager] 🤝 ÉGALITÉ - Les deux joueurs n'ont plus de balles!");
            GoalCelebrationUI.Instance?.PlayDRAWCelebration();
            EndGame(-1); // Match nul
        }
        else if (playersWithNoBalls == 1 && lastAlivePlayer >= 0)
        {
            string winnerName = GetPlayerName(lastAlivePlayer);
            Debug.Log($"[LocalTurnManager] 🎉 VICTOIRE du joueur {lastAlivePlayer} ({winnerName})!");
            GoalCelebrationUI.Instance?.PlayKILLERCelebration(winnerName);
            EndGame(lastAlivePlayer);
        }
    }

    public bool IsAnyBallMoving()
    {
        foreach (var ball in LocalBallAimController.AllBalls)
        {
            if (ball != null && ball.IsMoving) return true;
        }
        return false;
    }

    // ============================================
    // ✅ Logique interne (privée)
    // ============================================

    private void ExecuteTurnResolution()
    {
        CurrentState = TurnState.Resolution;
        _settleTimer = resolutionSettleDuration;


        foreach (var ball in LocalBallAimController.AllBalls)
        {
            if (ball != null)
            {
                ball.ExecuteQueuedShot();
            }
        }
    }

    public void ForceStopAiming()
    {

        foreach (var ball in LocalBallAimController.AllBalls)
        {
            if (ball != null)
            {
                ball.ForceStopAiming();
            }
        }
    }

    private bool AreAllBallsStopped()
    {
        // Ignorer les balles mortes (qui peuvent rester en mouvement temporairement)
        foreach (var ball in LocalBallAimController.AllBalls)
        {
            if (ball != null && !ball.IsDead && ball.IsMoving)
            {
                return false;
            }
        }
        return true;
    }

    private void EndGame(int winnerId)
    {
        WinnerPlayerId = winnerId;
        CurrentState = TurnState.Finished;

        string result = (winnerId < 0) ? "ÉGALITÉ" : $"VICTOIRE du joueur {winnerId}";
        Debug.Log($"[LocalTurnManager] 🏁 Fin du jeu: {result}");

        StartCoroutine(ReloadSceneAfterDelay());
    }

    private IEnumerator ReloadSceneAfterDelay()
    {
        yield return new WaitForSeconds(2f);
        Debug.Log("[LocalTurnManager] 🔄 Rechargement de la scène...");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
