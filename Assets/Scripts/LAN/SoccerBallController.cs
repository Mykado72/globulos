using System.Collections;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class SoccerBallController : NetworkBehaviour
{
    [Networked] private bool _goalScored { get; set; }

    [Header("Goal Animation Settings")]
    [SerializeField] private float fallDuration = 1f;
    [SerializeField] private float totalRotation = 90f;
    [SerializeField] private float targetScaleFraction = 0.99f;
    [SerializeField] private Color goalGrayColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    // ✅ FIX : singleton, comme GoalCelebrationUI, pour que TurnManager puisse remettre le
    // ballon en jeu (ResetForNewRound) sans FindFirstObjectByType à chaque but.
    public static SoccerBallController Instance { get; private set; }

    // ✅ FIX : état d'origine mémorisé pour pouvoir remettre le ballon en jeu après un but
    // qui ne termine pas la partie (voir ResetForNewRound).
    private Vector3 _spawnPosition;
    private Vector3 _initialScale;
    private Color _initialColor;

    public override void Spawned()
    {
        _goalScored = false;
        Instance = this;

        _spawnPosition = transform.position;
        _initialScale = transform.localScale;
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        _initialColor = sr != null ? sr.color : Color.white;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!HasStateAuthority || _goalScored) return;
        if (!collision.CompareTag("Goal")) return;

        GoalZone goal = collision.GetComponent<GoalZone>();
        if (goal == null) return;

        int scorerId = FindScoringPlayer(goal.DefendingTeam);
        if (scorerId < 0) return;

        _goalScored = true;
        Debug.Log($"[SoccerBallController] ⚽ BUT ! Marqué par PlayerId {scorerId}");

        // 1. Jouer le son
        if (AudioManager.Instance != null)
        {
            RPC_PlayGoalSound();
        }

        // 2. Transmettre l'animation à tous les clients (+ célébration visuelle "BUT !")
        RPC_AnimateGoalBall(scorerId);

        // 3. ✅ FIX : notifier le système de points au lieu de terminer systématiquement la
        // partie. ScoreManagerNetwork.AddGoal() décide lui-même, via CheckWinCondition(), si
        // la partie continue (TurnManager.RequestTurnReset, voir ResetForNewRound) ou se
        // termine (TurnManager.RequestWinBySoccerGoal).
        if (ScoreManagerNetwork.Instance != null)
        {
            ScoreManagerNetwork.Instance.AddGoal(scorerId);
        }
        else if (TurnManager.Instance != null)
        {
            // Fallback : comportement d'origine si aucun ScoreManagerNetwork n'est présent
            Debug.LogWarning("[SoccerBallController] ⚠️ ScoreManagerNetwork.Instance est NULL - fin de partie immédiate");
            TurnManager.Instance.RequestWinBySoccerGoal(scorerId);
        }

        // 4. ✅ FIX : on ne despawn le ballon que si la partie est réellement terminée.
        // Sinon on le laisse en jeu : TurnManager le repositionnera lui-même
        // (RPC_ResetAllForNewRound) une fois la célébration de but terminée.
        bool gameEnding = TurnManager.Instance != null &&
            (TurnManager.Instance.CurrentState == TurnState.Finished ||
             TurnManager.Instance.CurrentState == TurnState.Celebrating);

        if (gameEnding)
        {
            StartCoroutine(DespawnAfterDelayCoroutine(fallDuration));
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayGoalSound()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySoccerGoal();
        }
    }

    /// 
    /// ✨ RPC appelé sur TOUS les clients pour animer le ballon
    /// 
    // ✅ REFACTOR : animation partagée avec LocalSoccerBallController, voir GoalScoreAnimation.cs
    // ✅ Reçoit scorerId pour déclencher la célébration visuelle sur CHAQUE client (l'UI n'est
    // pas réseau : sans ça seul l'hôte, qui a HasStateAuthority, verrait l'animation).
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AnimateGoalBall(int scorerId)
    {
        StartCoroutine(GoalScoreAnimation.Run(
            transform, GetComponent<SpriteRenderer>(), GetComponent<Rigidbody2D>(), GetComponent<Collider2D>(),
            fallDuration, totalRotation, targetScaleFraction, goalGrayColor));

        string scorerName = TurnManager.Instance != null ? TurnManager.Instance.GetPlayerName(scorerId) : $"Joueur {scorerId}";
        if (GoalCelebrationUI.Instance == null)
        {
            Debug.LogWarning("[SoccerBallController] ⚠️ GoalCelebrationUI.Instance est null — as-tu bien un GameObject avec ce script dans la scène ?");
        }
        GoalCelebrationUI.Instance?.PlayGoalCelebration(scorerName);
    }

    /// 
    /// Attend la fin de l'animation avant de Despawn le NetworkObject
    /// 
    private IEnumerator DespawnAfterDelayCoroutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (HasStateAuthority && Runner != null)
        {
            Debug.Log("[SoccerBallController] 🗑️ Despawn du ballon après animation");
            Runner.Despawn(Object);
        }
    }

    /// <summary>
    /// ✅ FIX : remet le ballon dans son état initial (position, rotation, échelle, couleur,
    /// physique, collider, _goalScored) pour la manche suivante, après un but qui ne termine
    /// pas la partie. Appelée localement sur CHAQUE client par
    /// TurnManager.RPC_ResetAllForNewRound(), une fois la célébration de but terminée — même
    /// principe que RPC_AnimateGoalBall.
    /// </summary>
    public void ResetForNewRound()
    {
        StopAllCoroutines();

        _goalScored = false;

        transform.position = _spawnPosition;
        transform.rotation = Quaternion.identity;
        transform.localScale = _initialScale;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = _initialColor;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        Debug.Log("[SoccerBallController] 🔄 Ballon réinitialisé, prêt pour le prochain tour");
    }

    private int FindScoringPlayer(GoalZone.GoalTeam defendingTeam)
    {
        if (Runner == null) return -1;

        GoalZone.GoalTeam scoringTeam = (defendingTeam == GoalZone.GoalTeam.Jaune)
            ? GoalZone.GoalTeam.Rouge
            : GoalZone.GoalTeam.Jaune;

        // ⚠️ Convention (corrigée) : PlayerId IMPAIR = Jaune, PAIR = Rouge.
        // Le créateur de la room est toujours Jaune (constaté en jeu) ; l'ancienne version
        // de cette fonction faisait l'inverse, ce qui inversait les couleurs affichées
        // dans les messages (buteur, vainqueur, etc.).
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            GoalZone.GoalTeam playerTeam = (player.PlayerId % 2 == 0)
                ? GoalZone.GoalTeam.Rouge
                : GoalZone.GoalTeam.Jaune;

            if (playerTeam == scoringTeam)
            {
                return player.PlayerId;
            }
        }

        if (GameModeManager.Instance != null && GameModeManager.Instance.IsVsAI && GameModeManager.Instance.BotPlayerId >= 0)
        {
            GoalZone.GoalTeam botTeam = (GameModeManager.Instance.BotPlayerId % 2 == 0)
                ? GoalZone.GoalTeam.Rouge
                : GoalZone.GoalTeam.Jaune;

            if (botTeam == scoringTeam)
            {
                return GameModeManager.Instance.BotPlayerId;
            }
        }

        return -1;
    }
}