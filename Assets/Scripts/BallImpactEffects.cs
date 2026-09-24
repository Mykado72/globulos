using System.Collections;
using UnityEngine;

/// <summary>
/// ✅ REFACTOR : Coroutines extraites de BallAimController et LocalBallAimController
/// (SquashAnimationCoroutine et FallAnimationCoroutine étaient copiées-collées à l'identique).
///
/// Classe statique : chaque contrôleur reste responsable de démarrer la coroutine via son
/// propre StartCoroutine (MonoBehaviour et NetworkBehaviour l'exposent tous les deux),
/// par ex. : StartCoroutine(BallImpactEffects.Squash(transform, _originalScale, squashDuration, squashAmount, stretchAmount));
/// </summary>
public static class BallImpactEffects
{
    public static IEnumerator Squash(Transform t, Vector3 originalScale, float duration, float squashAmount, float stretchAmount)
    {
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / duration;

            float squash = Mathf.Lerp(1f, squashAmount, Mathf.Sin(progress * Mathf.PI));

            float scaleX = originalScale.x * stretchAmount * (1f - (1f - squash) / 5f);
            float scaleY = originalScale.y * squash;

            t.localScale = new Vector3(scaleX, scaleY, originalScale.z);

            yield return null;
        }

        t.localScale = originalScale;
    }

    /// <summary>
    /// Animation de "mort" d'une bille tombée dans un but : rotation + rétrécissement + fondu.
    /// onFallStarted permet à l'appelant de jouer un son ou d'autres effets au bon moment
    /// (ex. AudioManager.PlayBallDeath) sans dupliquer la coroutine pour ça.
    /// </summary>
    public static IEnumerator Fall(
        Transform t, SpriteRenderer spriteRenderer, Rigidbody2D rb, Collider2D col,
        float fallDuration, float shrinkStartTime,
        AnimationCurve fallCurve, AnimationCurve rotateCurve, float totalRotation,
        System.Action onFallStarted = null)
    {
        rb.isKinematic = true;
        rb.linearVelocity = Vector2.zero;

        onFallStarted?.Invoke();

        float elapsedTime = 0f;
        Vector3 startScale = t.localScale;

        while (elapsedTime < fallDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / fallDuration;

            float currentRotation = rotateCurve.Evaluate(progress) * totalRotation;
            t.Rotate(Vector3.forward, currentRotation - (rotateCurve.Evaluate(progress - Time.deltaTime / fallDuration) * totalRotation));

            float shrinkRatio = Mathf.Max(0f, progress - shrinkStartTime) / (1f - shrinkStartTime);
            float scale = Mathf.Lerp(1f, 0f, shrinkRatio);
            t.localScale = startScale * scale;

            float alpha = fallCurve.Evaluate(progress);
            Color color = spriteRenderer.color;
            color.a = 1f - alpha;
            spriteRenderer.color = color;

            yield return null;
        }

        if (col != null) col.enabled = false;
    }
}
