using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LocalTurnManager : MonoBehaviour
{
    public enum TurnState
    {
        Aiming,
        Resolution,
        CheckResult,
        Finished
    }

    [Header("Paramètres")]
    [SerializeField] private float aimDuration = 15f;
    [SerializeField] private float resolutionSettleDuration = 0.2f;

    public TurnState CurrentState { get; private set; }
    public int CurrentTurnNumber { get; private set; }
    public int WinnerPlayerId { get; private set; } = -1;

    public static LocalTurnManager Instance { get; private set; }

    private float _timer;
    private float _settleTimer;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        StartNewTurn();
    }

    private void Update()
    {
        switch (CurrentState)
        {
            case TurnState.Aiming:
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    ForceStopAiming();
                    ExecuteTurnResolution();
                }
                break;

            case TurnState.Resolution:
                _settleTimer -= Time.deltaTime;
                if (_settleTimer <= 0f && AreAllBallsStopped())
                {
                    CurrentState = TurnState.CheckResult;
                }
                break;

            case TurnState.CheckResult:
                CheckGameEnd();
                if (CurrentState == TurnState.CheckResult)
                {
                    StartNewTurn();
                }
                break;
        }
    }

    public void StartNewTurn()
    {
        CurrentTurnNumber++;
        CurrentState = TurnState.Aiming;
        Debug.Log($"Turn {CurrentTurnNumber} started. Players can aim their balls.");
        _timer = aimDuration;
    }

    private void ExecuteTurnResolution()
    {
        CurrentState = TurnState.Resolution;
        _settleTimer = resolutionSettleDuration;

        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null) ball.ExecuteQueuedShot();
        }
    }

    private void ForceStopAiming()
    {
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null) ball.ForceStopAiming();
        }
    }

    private bool AreAllBallsStopped()
    {
        foreach (var ball in BallAimController.AllBalls)
        {
            if (ball != null && ball.IsMoving) return false;
        }
        return true;
    }

    public void RequestWinBySoccerGoal(int winnerId)
    {
        if (CurrentState == TurnState.Finished) return;
        EndGame(winnerId);
    }

    public void CheckGameEnd()
    {
        Dictionary<int, int> aliveBallsPerPlayer = new Dictionary<int, int>();
        HashSet<int> allPlayerIds = new HashSet<int>();

        foreach (BallAimController ball in BallAimController.AllBalls)
        {
            if (ball == null) continue;
            int playerId = ball.OwnerPlayerId;
            allPlayerIds.Add(playerId);

            if (!aliveBallsPerPlayer.ContainsKey(playerId)) aliveBallsPerPlayer[playerId] = 0;
            if (!ball.IsDead) aliveBallsPerPlayer[playerId]++;
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
            EndGame(-1); // Match nul
        }
        else if (playersWithNoBalls == 1 && lastAlivePlayer >= 0)
        {
            EndGame(lastAlivePlayer);
        }
    }

    private void EndGame(int winnerId)
    {
        WinnerPlayerId = winnerId;
        CurrentState = TurnState.Finished;
        StartCoroutine(ReloadSceneAfterDelay());
    }

    private IEnumerator ReloadSceneAfterDelay()
    {
        yield return new WaitForSeconds(2f);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public float GetRemainingTime() => Mathf.Max(0f, _timer);

    public string GetPlayerName(int playerId)
    {
        if (PlayerNamesManager.Instance != null)
            return PlayerNamesManager.Instance.GetPlayerName(playerId);

        return $"Joueur {playerId}";
    }
}