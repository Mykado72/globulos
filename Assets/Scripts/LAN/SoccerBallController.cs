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

        // 2. Transmettre l'animation à tous les clients
        RPC_AnimateGoalBall();

        // 3. Notifier le TurnManager
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.RequestWinBySoccerGoal(scorerId);
        }

        // 4. Lancer la suppression différée (seulement sur le serveur)
        StartCoroutine(DespawnAfterDelayCoroutine(fallDuration));
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
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AnimateGoalBall()
    {
        StartCoroutine(GoalAnimationCoroutine());
    }

    private IEnumerator GoalAnimationCoroutine()
    {
        // Désactiver la physique pour figer le ballon dans les cages
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.velocity /= 5f;
            rb.angularVelocity /= 5f;
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();

        float elapsedTime = 0f;
        Vector3 initialScale = transform.localScale;
        Quaternion initialRotation = transform.rotation;
        Color initialColor = sr != null ? sr.color : Color.white;

        while (elapsedTime < fallDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / fallDuration;

            // 1. Calcul de l'échelle (maintenant exact selon targetScaleFraction)
            float currentScale = Mathf.Lerp(1f, targetScaleFraction, t);
            transform.localScale = initialScale * currentScale;

            // 2. Calcul propre de la rotation (évite la déformation matricielle)
            float currentAngle = (totalRotation / fallDuration) * elapsedTime;
            transform.rotation = initialRotation * Quaternion.Euler(0f, 0f, currentAngle);

            // Transition vers le gris
            if (sr != null)
            {
                sr.color = Color.Lerp(initialColor, goalGrayColor, t);
            }
            yield return new WaitForSeconds(0.05f);
        }
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.isKinematic = true;
        }

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