using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ✅ Gestionnaire de Score HYBRIDE
/// - Fonctionne en MODE RÉSEAU (Fusion) via version networké
/// - Fonctionne en MODE LOCAL (Offline) via version simple
/// </summary>
public class ScoreManagerBase : MonoBehaviour
{
    [Header("Score Settings")]
    [SerializeField] protected int pointsPerGoal = 1;
    [SerializeField] protected int winConditionPoints = 5;

    // Scores simples (pas de réseau)
    protected int Team1Score = 0;
    protected int Team2Score = 0;

    public static ScoreManagerBase Instance { get; protected set; }

    // Événement pour notifier l'UI du changement de score
    public delegate void ScoreChangedDelegate(int team1Score, int team2Score);
    public event ScoreChangedDelegate OnScoreChanged;

    public virtual void Initialize()
    {
        Instance = this;
        Team1Score = 0;
        Team2Score = 0;
        Debug.Log("[ScoreManager] ✅ ScoreManager initialisé (Mode LOCAL)");
    }

    public virtual void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Ajoute des points à une équipe
    /// Détermine l'équipe en fonction du PlayerId du buteur
    /// </summary>
    public virtual void AddGoal(int scorerPlayerId)
    {
        // ✅ Convention : PlayerId impair = Jaune (Team1), pair = Rouge (Team2)
        bool isTeam1 = (scorerPlayerId % 2 != 0);

        if (isTeam1)
        {
            Team1Score += pointsPerGoal;
            Debug.Log($"[ScoreManager] 🎯 BUT ! Équipe Jaune : {Team1Score} - {Team2Score}");
        }
        else
        {
            Team2Score += pointsPerGoal;
            Debug.Log($"[ScoreManager] 🎯 BUT ! Équipe Rouge : {Team1Score} - {Team2Score}");
        }

        // Notifier l'UI
        NotifyScoreChanged();

        // Vérifier condition de victoire
        CheckWinCondition();
    }

    /// <summary>
    /// Retourne le score actuel
    /// </summary>
    public virtual (int team1, int team2) GetScores()
    {
        return (Team1Score, Team2Score);
    }

    /// <summary>
    /// Retourne le score formaté pour l'UI
    /// </summary>
    public virtual string GetScoreDisplay()
    {
        return $"Jaune {Team1Score} - {Team2Score} Rouge";
    }

    /// <summary>
    /// Réinitialise les scores
    /// </summary>
    public virtual void ResetScores()
    {
        Team1Score = 0;
        Team2Score = 0;
        NotifyScoreChanged();
        Debug.Log("[ScoreManager] 🔄 Scores réinitialisés");
    }

    protected virtual void NotifyScoreChanged()
    {
        OnScoreChanged?.Invoke(Team1Score, Team2Score);
    }

    protected virtual void CheckWinCondition()
    {
        if (Team1Score >= winConditionPoints)
        {
            Debug.Log($"[ScoreManager] 🎉 VICTOIRE ! Équipe Jaune (Score: {Team1Score})");
            EndGameWithWinner(0);
        }
        else if (Team2Score >= winConditionPoints)
        {
            Debug.Log($"[ScoreManager] 🎉 VICTOIRE ! Équipe Rouge (Score: {Team2Score})");
            EndGameWithWinner(1);
        }
    }

    protected virtual void EndGameWithWinner(int teamIndex)
    {
        Debug.Log($"[ScoreManager] Fin de partie - Équipe {(teamIndex == 0 ? "Jaune" : "Rouge")} gagne");
        // À override dans les sous-classes
    }

    // ========== Interface ==========
    public int GetWinConditionPoints() => winConditionPoints;
    public int GetPointsPerGoal() => pointsPerGoal;
    public void SetWinConditionPoints(int points) => winConditionPoints = points;
}
