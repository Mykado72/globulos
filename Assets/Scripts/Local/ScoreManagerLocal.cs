using UnityEngine;

/// <summary>
/// ✅ Gestionnaire de Score LOCAL
/// Hérite de ScoreManagerBase pour le mode OFFLINE/Local uniquement
/// Pas de Fusion, pas de réseau
/// Utilise LocalTurnManager pour la gestion des tours en mode local
/// </summary>
public class ScoreManagerLocal : ScoreManagerBase
{
    private void Start()
    {
        Initialize();  // ✅ Appelé automatiquement au démarrage
    }

    public override void Initialize()
    {
        Instance = this;
        Team1Score = 0;
        Team2Score = 0;
        Debug.Log("[ScoreManagerLocal] ✅ ScoreManager initialisé (Mode LOCAL)");
    }

    /// <summary>
    /// Réinitialise le tour après un but en mode LOCAL
    /// </summary>
    protected override void ResetTurnAfterGoal(int scorerPlayerId)
    {
        Debug.Log($"[ScoreManagerLocal] 🔄 ResetTurnAfterGoal appelée pour joueur {scorerPlayerId}");

        // ✅ En mode local, utiliser LocalTurnManager
        if (LocalTurnManager.Instance != null)
        {
            Debug.Log("[ScoreManagerLocal] 🔄 Appel à LocalTurnManager.RequestTurnReset()");
            LocalTurnManager.Instance.RequestTurnReset();
        }
        else
        {
            Debug.LogWarning("[ScoreManagerLocal] ⚠️ LocalTurnManager.Instance est NULL !");
        }
    }

    protected override void EndGameWithWinner(int teamIndex)
    {
        // ✅ En mode local, utiliser LocalTurnManager
        // Convention : Joueur 1 = Jaune (Team1), Joueur 2 = Rouge (Team2)
        int winnerId = (teamIndex == 0) ? 1 : 2;  // PlayerId pour le gagnant

        if (LocalTurnManager.Instance != null)
        {
            Debug.Log($"[ScoreManagerLocal] 🎉 Fin de partie - Équipe {(teamIndex == 0 ? "Jaune" : "Rouge")} (Joueur {winnerId}) gagne !");
            LocalTurnManager.Instance.RequestWinBySoccerGoal(winnerId);
        }
        else
        {
            // Fallback si LocalTurnManager n'existe pas
            Debug.Log($"[ScoreManagerLocal] Fin de partie - Équipe {(teamIndex == 0 ? "Jaune" : "Rouge")} gagne (LocalTurnManager absent)");
        }
    }
}
