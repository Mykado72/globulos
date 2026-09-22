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

    [Header("Célébration")]
    [Tooltip("Délai avant de notifier la fin de partie, pour laisser le temps à l'animation GoalCelebrationUI de jouer entièrement (sinon le panneau de victoire et le rechargement de scène l'interrompent).")]
    [SerializeField] private float winNotificationDelay = 2.2f;

    public override void Spawned()
    {
        _goalScored = false;
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

        // 3. Notifier le TurnManager, après un délai pour laisser jouer la célébration.
        // ✅ FIX : appelé auparavant immédiatement, ce qui basculait l'état en Finished
        // (panneau de victoire + rechargement de scène) pendant que GoalCelebrationUI
        // était encore en train de jouer sur les clients.
        StartCoroutine(NotifyWinAfterDelay(scorerId));

        // 4. Lancer la suppression différée (seulement sur le serveur)
        StartCoroutine(DespawnAfterDelayCoroutine(fallDuration));
    }

    private IEnumerator NotifyWinAfterDelay(int scorerId)
    {
        yield return new WaitForSeconds(winNotificationDelay);
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.RequestWinBySoccerGoal(scorerId);
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

    private int FindScoringPlayer(GoalZone.GoalTeam defendingTeam)
    {
        if (Runner == null) return -1;

        GoalZone.GoalTeam scoringTeam = (defendingTeam == GoalZone.GoalTeam.Jaune)
            ? GoalZone.GoalTeam.Rouge
            : GoalZone.GoalTeam.Jaune;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            GoalZone.GoalTeam playerTeam = (player.PlayerId % 2 == 0)
                ? GoalZone.GoalTeam.Jaune
                : GoalZone.GoalTeam.Rouge;

            if (playerTeam == scoringTeam)
            {
                return player.PlayerId;
            }
        }

        if (GameModeManager.Instance != null && GameModeManager.Instance.IsVsAI && GameModeManager.Instance.BotPlayerId >= 0)
        {
            GoalZone.GoalTeam botTeam = (GameModeManager.Instance.BotPlayerId % 2 == 0)
                ? GoalZone.GoalTeam.Jaune
                : GoalZone.GoalTeam.Rouge;

            if (botTeam == scoringTeam)
            {
                return GameModeManager.Instance.BotPlayerId;
            }
        }

        return -1;
    }
}