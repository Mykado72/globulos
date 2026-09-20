using UnityEngine;

/// ✅ Implémentation MOCK de ITurnManagerCore pour tests unitaires
/// Permet de tester TurnUI, AudioManager, etc. SANS dépendre de Fusion ou GameState
/// 
/// Utilisation en tests :
///   var mockTM = new MockTurnManager();
///   mockTM.SetCurrentState(TurnState.Aiming);
///   mockTM.SetRemainingTime(10f);
///   // Tester votre logique...
public class MockTurnManager : ITurnManagerCore
{
    // État interne
    private TurnState _currentState = TurnState.Aiming;
    private int _currentTurnNumber = 1;
    private int _winnerPlayerId = -1;
    private float _remainingTime = 15f;
    private bool _isAnyBallMoving = false;

    // Suivi des appels (pour vérifier que les bonnes méthodes sont appelées)
    public int StartNewTurnCallCount { get; private set; } = 0;
    public int CheckGameEndCallCount { get; private set; } = 0;
    public int ForceStopAimingCallCount { get; private set; } = 0;
    public int RequestWinBySoccerGoalCallCount { get; private set; } = 0;

    // ============================================
    // Implémentation ITurnManagerCore
    // ============================================

    public TurnState CurrentState
    {
        get => _currentState;
    }

    public int CurrentTurnNumber
    {
        get => _currentTurnNumber;
    }

    public int WinnerPlayerId
    {
        get => _winnerPlayerId;
    }

    public float GetRemainingTime()
    {
        return _remainingTime;
    }

    public string GetPlayerName(int playerId)
    {
        return $"MockPlayer_{playerId}";
    }

    public void StartNewTurn()
    {
        StartNewTurnCallCount++;
        _currentTurnNumber++;
        _currentState = TurnState.Aiming;
        Debug.Log($"[MockTurnManager] StartNewTurn called (count: {StartNewTurnCallCount})");
    }

    public void CheckGameEnd()
    {
        CheckGameEndCallCount++;
        Debug.Log($"[MockTurnManager] CheckGameEnd called (count: {CheckGameEndCallCount})");
    }

    public bool IsAnyBallMoving()
    {
        return _isAnyBallMoving;
    }

    public void ForceStopAiming()
    {
        ForceStopAimingCallCount++;
        Debug.Log($"[MockTurnManager] ForceStopAiming called (count: {ForceStopAimingCallCount})");
    }

    public void RequestWinBySoccerGoal(int winnerId)
    {
        RequestWinBySoccerGoalCallCount++;
        _winnerPlayerId = winnerId;
        _currentState = TurnState.Finished;
        Debug.Log($"[MockTurnManager] RequestWinBySoccerGoal({winnerId}) called (count: {RequestWinBySoccerGoalCallCount})");
    }

    // ============================================
    // Helpers pour configurer l'état du mock
    // ============================================

    public void SetCurrentState(TurnState state)
    {
        _currentState = state;
    }

    public void SetCurrentTurnNumber(int turnNumber)
    {
        _currentTurnNumber = turnNumber;
    }

    public void SetWinnerPlayerId(int playerId)
    {
        _winnerPlayerId = playerId;
    }

    public void SetRemainingTime(float time)
    {
        _remainingTime = time;
    }

    public void SetIsAnyBallMoving(bool isMoving)
    {
        _isAnyBallMoving = isMoving;
    }

    public void ResetCallCounts()
    {
        StartNewTurnCallCount = 0;
        CheckGameEndCallCount = 0;
        ForceStopAimingCallCount = 0;
        RequestWinBySoccerGoalCallCount = 0;
    }
}

/// ============================================
/// 📝 Exemples de tests unitaires
/// ============================================

/*
// Exemple 1 : Tester que TurnUI affiche le bon temps
[Test]
public void TurnUI_DisplaysRemainingTime()
{
    // Arrange
    var mock = new MockTurnManager();
    mock.SetRemainingTime(10.5f);
    
    var turnUI = new TurnUI();
    turnUI.SetTurnManager(mock); // à ajouter à TurnUI
    
    // Act
    turnUI.UpdateDisplay();
    
    // Assert
    Assert.AreEqual("10", turnUI.timerText.text); // Ceil(10.5) = 10
}

// Exemple 2 : Tester la détection de fin de partie
[Test]
public void TurnUI_ShowsWinPanel_WhenGameFinished()
{
    // Arrange
    var mock = new MockTurnManager();
    mock.SetCurrentState(TurnState.Finished);
    mock.SetWinnerPlayerId(0);
    
    var turnUI = new TurnUI();
    turnUI.SetTurnManager(mock);
    
    // Act
    turnUI.UpdateDisplay();
    
    // Assert
    Assert.IsTrue(turnUI.panelWIN.activeSelf);
    Assert.AreEqual("🎉 Victoire du Joueur MockPlayer_0 !", turnUI.stateText.text);
}

// Exemple 3 : Tester match nul
[Test]
public void TurnUI_ShowsDrawPanel_WhenGameDraw()
{
    // Arrange
    var mock = new MockTurnManager();
    mock.SetCurrentState(TurnState.Finished);
    mock.SetWinnerPlayerId(-1); // -1 = nul
    
    var turnUI = new TurnUI();
    turnUI.SetTurnManager(mock);
    
    // Act
    turnUI.UpdateDisplay();
    
    // Assert
    Assert.IsTrue(turnUI.panelDRAW.activeSelf);
    Assert.AreEqual("Match nul !", turnUI.stateText.text);
}

// Exemple 4 : Tester que les appels méthode sont bien exécutés
[Test]
public void MockTurnManager_TracksCalls()
{
    // Arrange
    var mock = new MockTurnManager();
    
    // Act
    mock.StartNewTurn();
    mock.StartNewTurn();
    mock.ForceStopAiming();
    
    // Assert
    Assert.AreEqual(2, mock.StartNewTurnCallCount);
    Assert.AreEqual(1, mock.ForceStopAimingCallCount);
}
*/
