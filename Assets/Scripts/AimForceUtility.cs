using UnityEngine;

/// <summary>
/// ✅ REFACTOR : Fonctions pures extraites de BallAimController et LocalBallAimController
/// (ComputeClampedForce et GetViewHeight étaient identiques dans les deux fichiers).
/// </summary>
public static class AimForceUtility
{
    public static Vector2 ComputeClampedForce(
        Vector2 startDragPos, Vector2 currentMousePos,
        float maxDragDistanceFraction, float viewHeight, float maxForce)
    {
        Vector2 dragVector = currentMousePos - startDragPos;
        float maxDragDistance = maxDragDistanceFraction * viewHeight;

        float dragRatio = maxDragDistance > 0f ? Mathf.Clamp01(dragVector.magnitude / maxDragDistance) : 0f;
        return dragVector.normalized * dragRatio * maxForce;
    }

    public static float GetViewHeight(Camera camera, float fallbackViewHeight)
    {
        return (camera != null && camera.orthographic) ? camera.orthographicSize * 2f : fallbackViewHeight;
    }
}
