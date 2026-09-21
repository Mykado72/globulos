using UnityEngine;

/// <summary>
/// Système d'IA du bot SMART pour Globulos
/// 
/// Le bot vise vers le BUT ADVERSE au lieu de juste viser le ballon.
/// Cela évite les buts contre son camp!
/// </summary>
public class BotAIStrategy
{
    public enum AIDifficulty { Easy, Medium, Hard }

    private AIDifficulty _difficulty;
    public int _ownerPlayerId;

    // Configuration par difficulté
    private readonly struct DifficultyConfig
    {
        public readonly float aimInaccuracyDegrees;
        public readonly float minForceFraction;
        public readonly float maxForceFraction;

        public DifficultyConfig(float inaccuracy, float minForce, float maxForce)
        {
            aimInaccuracyDegrees = inaccuracy;
            minForceFraction = minForce;
            maxForceFraction = maxForce;
        }
    }

    private static readonly DifficultyConfig[] DifficultySettings = new[]
    {
        new DifficultyConfig(inaccuracy: 25f, minForce: 0.5f, maxForce: 0.8f),   // Easy
        new DifficultyConfig(inaccuracy: 12f, minForce: 0.7f, maxForce: 0.95f),  // Medium
        new DifficultyConfig(inaccuracy: 5f, minForce: 0.85f, maxForce: 1.0f)    // Hard
    };

    public BotAIStrategy(AIDifficulty difficulty = AIDifficulty.Medium, int ownerPlayerId = -1)
    {
        _ownerPlayerId = ownerPlayerId;
        SetDifficulty(difficulty);
    }

    public void SetDifficulty(AIDifficulty difficulty)
    {
        _difficulty = difficulty;
    }

    public void SetOwnerPlayerId(int playerId)
    {
        _ownerPlayerId = playerId;
    }

    /// <summary>
    /// Calcule le tir du bot pour frapper le ballon VERS LE BUT ADVERSE
    /// </summary>
    public (Vector2 direction, float forceFraction) CalculateBotShot(
        Vector2 botPosition,
        Vector2 ballPosition,
        Vector2 enemyGoalPosition)
    {
        var config = DifficultySettings[(int)_difficulty];

        // 🎯 Direction: du ballon vers le but adverse (pas juste vers le ballon!)
        Vector2 directionToEnemyGoal = (enemyGoalPosition - ballPosition).normalized;

        // Ajouter imprécision
        Vector2 aimWithInaccuracy = AddAimInaccuracy(directionToEnemyGoal, config.aimInaccuracyDegrees);

        // Force aléatoire entre min et max
        float forceFraction = Random.Range(config.minForceFraction, config.maxForceFraction);

        return (aimWithInaccuracy, forceFraction);
    }

    /// <summary>
    /// Trouve le GoalZone qui est adverse pour ce bot
    /// </summary>
    public GoalZone FindEnemyGoal()
    {
        if (_ownerPlayerId < 0)
        {
            Debug.LogWarning("⚠️ OwnerPlayerId non défini pour le bot!");
            return null;
        }

        // Déterminer l'équipe du bot
        // PlayerId pair = Jaune, impair = Rouge
        GoalZone.GoalTeam botTeam = _ownerPlayerId % 2 == 0 ? GoalZone.GoalTeam.Jaune : GoalZone.GoalTeam.Rouge;
        GoalZone.GoalTeam enemyTeam = botTeam == GoalZone.GoalTeam.Jaune ? GoalZone.GoalTeam.Rouge : GoalZone.GoalTeam.Jaune;

        // Chercher tous les GoalZone
        GoalZone[] allGoals = Object.FindObjectsOfType<GoalZone>();
        
        foreach (GoalZone goal in allGoals)
        {
            if (goal.DefendingTeam == enemyTeam)
            {
                Debug.Log($"✅ But adverse trouvé pour l'équipe {botTeam}: {goal.name}");
                return goal;
            }
        }

        Debug.LogWarning($"⚠️ Aucun but adverse trouvé pour l'équipe {botTeam}!");
        return null;
    }

    /// <summary>
    /// Ajoute une imprécision angulaire pour rendre l'IA plus réaliste
    /// </summary>
    private Vector2 AddAimInaccuracy(Vector2 direction, float inaccuracyDegrees)
    {
        if (inaccuracyDegrees <= 0f) return direction;

        // Convertir en angle
        float currentAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // Ajouter décalage aléatoire
        float randomOffset = Random.Range(-inaccuracyDegrees, inaccuracyDegrees);
        float newAngle = currentAngle + randomOffset;

        // Convertir back en vecteur
        float radians = newAngle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }
}
