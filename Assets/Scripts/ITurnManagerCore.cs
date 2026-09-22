using UnityEngine;

/// ✅ Interface commune pour LocalTurnManager et TurnManager (réseau)
/// Permet à TurnUI, UIManager et autres de fonctionner indépendamment
/// du mode de jeu (Local ou Network)
public interface ITurnManagerCore
{
    // --- État du jeu ---
    
    /// <summary>Phase actuelle (Aiming, Resolution, CheckResult, Finished, etc.)</summary>
    TurnState CurrentState { get; }
    
    /// <summary>Numéro du tour actuel (1, 2, 3...)</summary>
    int CurrentTurnNumber { get; }
    
    /// <summary>PlayerId du gagnant. -1 si match nul ou en cours.</summary>
    int WinnerPlayerId { get; }

    // --- Temps et durées ---
    
    /// <summary>Temps restant en secondes pour la phase actuelle.</summary>
    float GetRemainingTime();

    // --- Joueurs et pseudo ---
    
    /// <summary>Récupère le pseudo d'un joueur via son PlayerId.</summary>
    string GetPlayerName(int playerId);

    // --- Logique de jeu ---
    
    /// <summary>Démarre un nouveau tour (appelé par la logique de game end).</summary>
    void StartNewTurn();
    
    /// <summary>Signale une fin de partie par but de ballon de foot.</summary>
    void RequestWinBySoccerGoal(int winnerId);
    
    /// <summary>Marque la fin du jeu (victoire, défaite ou match nul).</summary>
    void CheckGameEnd();

    // --- État des billes ---
    
    /// <summary>Vérifie si au moins une bille du jeu est en mouvement.</summary>
    bool IsAnyBallMoving();
    
    /// <summary>Forcer l'arrêt de la visée (quand le timer expire).</summary>
    void ForceStopAiming();
}

/// ✅ Enum partagé pour l'état du jeu
public enum TurnState
{
    RealTime,      // Mode temps réel (not used in turn-based)
    Aiming,        // Phase de visée
    Resolution,    // Exécution des tirs
    CheckResult,   // Vérification du résultat
    Celebrating,   // ⚽ But marqué : célébration en cours, timer gelé, avant la fin de partie
    Finished       // Partie finie
}
