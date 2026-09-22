using UnityEngine;

/// ✅ Factory Pattern : Aide à obtenir le bon ITurnManagerCore
/// sans connaître le mode (Local ou Network)
public static class TurnManagerFactory
{
    /// <summary>
    /// Retourne l'instance ITurnManagerCore disponible (Local ou Network).
    /// Cherche d'abord LocalTurnManager, puis TurnManager réseau.
    /// </summary>
    public static ITurnManagerCore GetTurnManager()
    {
        // Mode Local
        LocalTurnManager localTM = Object.FindFirstObjectByType<LocalTurnManager>();
        if (localTM != null)
            return localTM;

        // Mode Network (Fusion)
        TurnManager networkTM = Object.FindFirstObjectByType<TurnManager>();
        if (networkTM != null)
            return networkTM;

        Debug.LogError("[TurnManagerFactory] ❌ Aucun TurnManager trouvé (ni Local ni Network)!");
        return null;
    }

    /// <summary>
    /// Retourne le temps restant, indépendamment du mode.
    /// </summary>
    public static float GetRemainingTime()
    {
        var tm = GetTurnManager();
        return tm?.GetRemainingTime() ?? 0f;
    }

    /// <summary>
    /// Retourne l'état actuel du jeu, indépendamment du mode.
    /// </summary>
    public static TurnState GetCurrentState()
    {
        var tm = GetTurnManager();
        return tm?.CurrentState ?? TurnState.Finished;
    }

    /// <summary>
    /// Retourne le pseudo d'un joueur, indépendamment du mode.
    /// </summary>
    public static string GetPlayerName(int playerId)
    {
        var tm = GetTurnManager();
        return tm?.GetPlayerName(playerId) ?? $"Joueur {playerId}";
    }
}
