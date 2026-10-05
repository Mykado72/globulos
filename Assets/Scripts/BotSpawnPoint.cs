using UnityEngine;

/// <summary>
/// ✅ Réglages de stratégie d'UN bot, à poser sur son point de spawn.
///
/// Même principe que les "spawn points" : dans la scène du niveau, ajoute ce composant sur chaque
/// Transform référencé dans LocalGameSpawner > Player 2 Spawn Points. Chaque bot a ainsi sa
/// propre difficulté, son propre rôle, son propre délai de réaction, etc.
/// Un point de spawn SANS ce composant garde les valeurs du prefab (comportement d'avant).
/// </summary>
public class BotSpawnPoint : MonoBehaviour
{
    [Header("Stratégie de ce bot")]
    public BotAIStrategy.AIDifficulty difficulty = BotAIStrategy.AIDifficulty.Medium;
    public BotAIStrategy.BotRole role = BotAIStrategy.BotRole.Mixte;

    [Tooltip("Délai (s) avant que le bot ne tire : plus il est long, moins le bot est réactif.")]
    [Min(0f)] public float reactionDelaySeconds = 0.3f;

    [Header("Rôle Mixte")]
    [Tooltip("Le bot passe en défense si le ballon est à moins de cette distance de son but (ajustée selon la difficulté).")]
    [Min(50f)] public float defensiveZoneRadius = 550f;

    [Header("Rôle Défensif (gardien)")]
    [Tooltip("Distance à laquelle le bot se tient devant son but.")]
    public float defenseDepth = 180f;

    [Tooltip("Amplitude latérale max autour du but (largeur de la cage couverte).")]
    public float maxLateralRange = 60f;

    [Tooltip("En dessous de cette distance de sa position cible, le bot ne bouge pas.")]
    [Min(0f)] public float snapDistance = 120f;
}
