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

    // ✅ NOUVEAU : rôle du bot. Offensive = comportement existant (vise le but adverse).
    // Defensive = reste de son côté du terrain, devant son propre but, aligné sur le
    // ballon façon gardien de but.
    public enum BotRole { Offensive, Defensive, Mixte }

    /// <summary>
    /// Convention reliant la parité du PlayerId à la couleur d'équipe.
    /// - Local (LocalGameSpawner) : Joueur 1 (humain) = Jaune, Joueur 2 (bot) = Rouge
    /// - LAN (Fusion, voir SoccerBallController.FindScoringPlayer) : celui qui crée la
    ///   room est toujours Jaune, et son PlayerId Fusion s'avère impair en pratique.
    /// Les deux modes utilisent donc actuellement la même règle : PlayerId IMPAIR = Jaune
    /// (OddIsJaune). L'enum reste distinct pour documenter explicitement cette convention
    /// par appelant, au cas où l'un des deux modes changerait sa logique d'attribution.
    /// </summary>
    public enum PlayerIdConvention { OddIsJaune, EvenIsJaune }

    private AIDifficulty _difficulty;
    private BotRole _role;
    private PlayerIdConvention _convention;
    public int _ownerPlayerId;

    public BotRole Role => _role;

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

    public BotAIStrategy(AIDifficulty difficulty = AIDifficulty.Medium, int ownerPlayerId = -1, BotRole role = BotRole.Offensive, PlayerIdConvention convention = PlayerIdConvention.OddIsJaune)
    {
        _ownerPlayerId = ownerPlayerId;
        _role = role;
        _convention = convention;
        SetDifficulty(difficulty, ownerPlayerId);
    }

    public void SetDifficulty(AIDifficulty difficulty, int ownerPlayerId)
    {
        _difficulty = difficulty;
        _ownerPlayerId = ownerPlayerId;
    }

    public void SetRole(BotRole role)
    {
        _role = role;
    }

    public void SetPlayerIdConvention(PlayerIdConvention convention)
    {
        _convention = convention;
    }

    public void SetOwnerPlayerId(int playerId)
    {
        _ownerPlayerId = playerId;
    }

    public (Vector2 direction, float forceFraction) CalculateBotDecision(
       Vector2 botPosition,
       Vector2 ballPosition,
       GoalZone enemyGoal,
       GoalZone ownGoal)
    {
        BotRole roleToUse = _role;

        // 🎯 MIXTE : détecter dynamiquement le comportement
        if (_role == BotRole.Mixte)
        {
            roleToUse = DetermineBotRole(ballPosition, ownGoal, enemyGoal);
            // Debug.Log($"🔄 Bot MIXTE décide: {roleToUse}");
        }

        if (roleToUse == BotRole.Offensive)
        {
            return CalculateBotShot(botPosition, ballPosition, enemyGoal);
        }
        else // Defensive
        {
            if (ownGoal == null) ownGoal = FindOwnGoal();
            return CalculateDefensiveShot(botPosition, ballPosition, ownGoal, enemyGoal);
        }
    }

    /// <summary>
    /// Détermine si le bot doit jouer offensif ou défensif selon la position du ballon
    /// </summary>
    private BotRole DetermineBotRole(Vector2 ballPosition, GoalZone ownGoal, GoalZone enemyGoal)
    {
        if (ownGoal == null || enemyGoal == null)
        {
            return BotRole.Offensive; // Par défaut, attaque
        }

        // Récupérer les positions réelles des buts
        Vector2 ownGoalPos = GetGoalFrontPosition(ownGoal);
        Vector2 enemyGoalPos = GetGoalFrontPosition(enemyGoal);

        // Calculer le milieu du terrain
        float midfield = (ownGoalPos.x + enemyGoalPos.x) / 2f;

        // Si le ballon est du côté du bot → défendre
        // Si le ballon est du côté adverse → attaquer
        bool ballIsOnOwnSide = (ownGoalPos.x < enemyGoalPos.x)
            ? ballPosition.x < midfield      // But du bot à gauche
            : ballPosition.x > midfield;     // But du bot à droite

        if (ballIsOnOwnSide)
        {
            // Debug.Log($"🛡️ Ballon du côté du bot ({ballPosition.x}) → DÉFENSIF");
            return BotRole.Defensive;
        }
        else
        {
            // Debug.Log($"⚔️ Ballon du côté adverse ({ballPosition.x}) → OFFENSIF");
            return BotRole.Offensive;
        }
    }


    /// Calcule le tir du bot :
    /// - Vise le ballon s'il est du bon côté (derrière le ballon par rapport à son camp).
    /// - Sinon, tire vers l'arrière avec un décalage vertical pour se replacer sans marquer CSC.
    /// 

    public (Vector2 direction, float forceFraction) CalculateBotShot(
        Vector2 botPosition,
        Vector2 ballPosition,
        GoalZone enemyGoal)
    {
        var config = DifficultySettings[(int)_difficulty];
        Vector2 enemyGoalPosition = GetGoalFrontPosition(enemyGoal);

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
            forceFraction = Random.Range(config.minForceFraction, config.maxForceFraction / 2f);  // pour se déplacer sans trop de force
        }

        // 3. Imprécision et force
        Vector2 aimWithInaccuracy = AddAimInaccuracy(targetDirection, config.aimInaccuracyDegrees);
        return (aimWithInaccuracy, forceFraction);
    }

    /// <summary>
    /// ✅ NOUVEAU : Comportement DÉFENSIF façon gardien de but.
    /// Le bot ne cherche jamais à attaquer le ballon : il reste toujours de son côté du
    /// terrain, se replace légèrement devant son propre but, et se cale latéralement sur
    /// la position Y du ballon (dans les limites de sa "cage") pour couvrir les tirs
    /// adverses — comme un gardien qui suit le ballon des yeux sans sortir de sa surface.
    /// </summary>
    /// <param name="defenseDepth">Distance à laquelle le bot se tient devant son but (vers le centre du terrain).</param>
    /// <param name="maxLateralRange">Amplitude latérale max autour du but (largeur de la "cage" couverte).</param>
    /// <param name="snapDistance">En dessous de cette distance de la position cible, le bot ne bouge pas (évite un jitter permanent).</param>
    public (Vector2 direction, float forceFraction) CalculateDefensiveShot(
        Vector2 botPosition,
        Vector2 ballPosition,
        GoalZone ownGoal,
        GoalZone enemyGoal,
        float defenseDepth = 180f,
        float maxLateralRange = 60.0f,
        float snapDistance = 120.0f)
    {
        var config = DifficultySettings[(int)_difficulty];

        // Récupérer la vraie position du but (extrémité du collider)
        Vector2 ownGoalPosition = GetGoalFrontPosition(ownGoal);
        Vector2 enemyGoalPosition = GetGoalFrontPosition(enemyGoal);

        // 1. "Vers le terrain" = direction de son propre but vers le but adverse
        // (même convention que CalculateBotShot, robuste même si le terrain n'est pas
        // parfaitement symétrique autour de x=0).
        float depthDirection = Mathf.Sign(enemyGoalPosition.x - ownGoalPosition.x);
        if (depthDirection == 0f) depthDirection = 1f;

        // 2. Position cible : légèrement devant son but, alignée en Y sur le ballon,
        // bornée à l'amplitude de la "cage" pour ne jamais trop s'excentrer.
        float targetX = ownGoalPosition.x + depthDirection * defenseDepth;
        float targetY = Mathf.Clamp(ballPosition.y, ownGoalPosition.y - maxLateralRange, ownGoalPosition.y + maxLateralRange);
        Vector2 targetPosition = new Vector2(targetX, targetY);

        Vector2 toTarget = targetPosition - botPosition;

        // 3. Déjà bien placé : on ne tire pas (évite un replacement permanent bille sur bille)
        if (toTarget.magnitude < snapDistance)
        {
            // Debug.Log("🧤 Gardien déjà bien positionné -> pas de replacement");
            return (Vector2.zero, 0f);
        }

        Vector2 targetDirection = toTarget.normalized;

        // Un gardien ajuste sa position par petites touches, jamais à pleine puissance
        float forceFraction = Random.Range(config.minForceFraction * 0.005f, config.maxForceFraction * 0.015f);

        // Debug.Log($"🧤 Gardien -> replacement devant son but (cible={targetPosition})");

        Vector2 aimWithInaccuracy = AddAimInaccuracy(targetDirection, config.aimInaccuracyDegrees);
        return (aimWithInaccuracy, forceFraction);
    }


    /// <summary>
    /// Détermine la couleur d'équipe du bot à partir de son PlayerId, selon la convention
    /// du mode courant (voir <see cref="PlayerIdConvention"/>).
    /// </summary>
    private GoalZone.GoalTeam GetBotTeam()
    {
        bool idIsEven = _ownerPlayerId % 2 == 0;
        bool isJaune = (_convention == PlayerIdConvention.EvenIsJaune) ? idIsEven : !idIsEven;
        return isJaune ? GoalZone.GoalTeam.Jaune : GoalZone.GoalTeam.Rouge;
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

        GoalZone.GoalTeam botTeam = GetBotTeam();
        GoalZone.GoalTeam enemyTeam = botTeam == GoalZone.GoalTeam.Jaune ? GoalZone.GoalTeam.Rouge : GoalZone.GoalTeam.Jaune;

        // Chercher tous les GoalZone
        GoalZone[] allGoals = Object.FindObjectsByType<GoalZone>(FindObjectsSortMode.None);

        foreach (GoalZone goal in allGoals)
        {
            if (goal.DefendingTeam == enemyTeam)
            {
                // Debug.Log($"✅ But adverse trouvé pour l'équipe {botTeam}: {goal.name}");
                return goal;
            }
        }

        Debug.LogWarning($"⚠️ Aucun but adverse trouvé pour l'équipe {botTeam}!");
        return null;
    }

    /// <summary>
    /// ✅ NOUVEAU : Trouve le GoalZone que ce bot doit DÉFENDER (utilisé par le rôle Defensive).
    /// Symétrique de FindEnemyGoal.
    /// </summary>
    public GoalZone FindOwnGoal()
    {
        if (_ownerPlayerId < 0)
        {
            Debug.LogWarning("⚠️ OwnerPlayerId non défini pour le bot!");
            return null;
        }

        GoalZone.GoalTeam botTeam = GetBotTeam();

        GoalZone[] allGoals = Object.FindObjectsByType<GoalZone>(FindObjectsSortMode.None);

        foreach (GoalZone goal in allGoals)
        {
            if (goal.DefendingTeam == botTeam)
            {
                //Debug.Log($"🧤 But à défendre trouvé pour l'équipe {botTeam}: {goal.name}");
                return goal;
            }
        }

        Debug.LogWarning($"⚠️ Aucun but à défendre trouvé pour l'équipe {botTeam}!");
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

    /// <summary>
    /// Récupère la vraie position du but en utilisant les bounds du BoxCollider2D
    /// Retourne l'extrémité du collider (côté terrain)
    /// </summary>
    public Vector2 GetGoalFrontPosition(GoalZone goalZone)
    {
        Collider2D collider = goalZone.GetComponent<Collider2D>();
        if (collider == null)
        {
            Debug.LogWarning($"⚠️ GoalZone {goalZone.name} n'a pas de Collider2D!");
            return goalZone.transform.position;
        }

        Bounds bounds = collider.bounds;

        // Si le but est à gauche (x < 0), on prend l'extrémité droite (vers le terrain)
        // Si le but est à droite (x > 0), on prend l'extrémité gauche (vers le terrain)
        float goalX = goalZone.transform.position.x;
        float frontX = (goalX < 0)
            ? bounds.max.x  // But à gauche → extrémité droite
            : bounds.min.x; // But à droite → extrémité gauche

        // Debug.Log($"🥅 Goal {goalZone.name}: bounds=[{bounds.min.x}, {bounds.max.x}], front={frontX}");

        return new Vector2(frontX, goalZone.transform.position.y);
    }
}