using System.Collections;
using UnityEngine;

/// <summary>
/// ✅ REFACTOR : Coroutine extraite de SoccerBallController et LocalSoccerBallController
/// (GoalAnimationCoroutine était dupliquée, avec de légères variantes de timing/valeurs
/// d'Inspector mais une logique identique).
/// </summary>
public static class GoalScoreAnimation
{
    public static IEnumerator Run(
        Transform t, SpriteRenderer spriteRenderer, Rigidbody2D rb, Collider2D col,
        float fallDuration, float totalRotation, float targetScaleFraction, Color goalGrayColor)
    {
        if (rb != null)
        {
            rb.velocity /= 5f;
            rb.angularVelocity /= 5f;
        }

        if (col != null) col.enabled = false;

        float elapsedTime = 0f;
        Vector3 initialScale = t.localScale;
        Quaternion initialRotation = t.rotation;
        Color initialColor = spriteRenderer != null ? spriteRenderer.color : Color.white;

        while (elapsedTime < fallDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / fallDuration;

            float currentAngle = (totalRotation / fallDuration) * elapsedTime;
            t.rotation = initialRotation * Quaternion.Euler(0f, 0f, currentAngle);

            float currentScaleFraction = Mathf.Lerp(1f, targetScaleFraction, progress);
            
            t.localScale = initialScale * currentScaleFraction;

            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.Lerp(initialColor, goalGrayColor, progress);
            }

            yield return new WaitForSeconds(0.05f);
        }

        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            // ✅ FIX : isKinematic = true a été retiré. Il n'était pas nécessaire (le collider
            // est déjà désactivé ci-dessus et l'animation pilote transform directement, sans
            // physique), et il obligeait chaque appelant à penser à le remettre à false plus
            // tard. C'est précisément ce qui causait le bug "ballon bloqué en Kinematic" en
            // local (ResetForNewRound() interrompait la coroutine via StopAllCoroutines()
            // avant qu'elle n'atteigne ResetBall(), qui seul remettait isKinematic = false).
        }
    }
}
