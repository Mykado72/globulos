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
            Debug.Log($"[GoalScoreAnimation] progress: {progress:F2}, currentScaleFraction: {currentScaleFraction:F2} cible : {targetScaleFraction:F2}");
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
            rb.isKinematic = true;
        }
    }
}
