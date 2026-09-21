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
        new DifficultyConfig(inaccuracy: 10f, minForce: 0.7f, maxForce: 0.95f),  // Medium
        new DifficultyConfig(inaccuracy: 0f, minForce: 0.85f, maxForce: 1.0f)    // Hard
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

    /// 
    /// Calcule le tir du bot :
    /// - Vise le ballon s'il est du bon côté (derrière le ballon par rapport à son camp).
    /// - Sinon, tire vers l'arrière avec un décalage vertical pour se replacer sans marquer CSC.
    /// 
    public (Vector2 direction, float forceFraction) CalculateBotShot(
        Vector2 botPosition,
        Vector2 ballPosition,
        Vector2 enemyGoalPosition)
    {
        var config = DifficultySettings[(int)_difficulty];

        // 1. Sens de l'attaque : vecteur qui va du terrain vers le but adverse
        // Si enemyGoalPosition.x > 0, l'attaque va vers la droite (+1), donc la défense est à gauche (-1)
        float attackXDirection = Mathf.Sign(enemyGoalPosition.x - botPosition.x);
        float defenseXDirection = -attackXDirection;

        // 2. Vérification du bon côté
        // Bon côté = le bot est en amont du ballon par rapport au sens de l'attaque
        bool isGoodSide = (attackXDirection > 0)
            ? (botPosition.x < ballPosition.x)   // Attaque vers la droite : bot doit être à gauche du ballon
            : (botPosition.x > ballPosition.x);  // Attaque vers la gauche : bot doit être à droite du ballon

        Vector2 targetDirection;
        float forceFraction;

        if (isGoodSide)
        {
            // 🎯 BON CÔTÉ : Attaque directe du ballon
            targetDirection = (ballPosition - botPosition).normalized;
            forceFraction = Random.Range(config.minForceFraction, config.maxForceFraction);
            Debug.Log("🤖 Bot du BON CÔTÉ -> Attaque le ballon");
        }
        else
        {
            // 🛡️ MAUVAIS CÔTÉ : Replacement vers son propre camp (à l'opposé du but adverse)
            // Dégagement latéral avec offset vertical (+1.5 ou -1.5) pour éviter de rentrer dans ses propres cages
            float offsetY = (botPosition.y >= 0) ? 1.5f : -1.5f;

            Vector2 replacementTarget = new Vector2(
                botPosition.x + (defenseXDirection * 3f),
                botPosition.y + offsetY
            );

            targetDirection = (replacementTarget - botPosition).normalized;
            Debug.Log("⚠️ Bot du MAUVAIS CÔTÉ -> Contournement vers son camp");
            forceFraction = Random.Range(config.minForceFraction, config.maxForceFraction/2f);  // pour se déplacer sans trop de force
        }

        // 3. Imprécision et force
        Vector2 aimWithInaccuracy = AddAimInaccuracy(targetDirection, config.aimInaccuracyDegrees);
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
