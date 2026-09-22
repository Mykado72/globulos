using UnityEngine;

/// <summary>
/// ✅ REFACTOR : Logique partagée de la flèche de visée, extraite de BallAimController
/// et LocalBallAimController (code strictement identique dans les deux, dupliqué).
///
/// Classe C# pure (pas un MonoBehaviour) : elle crée ses propres GameObjects enfants
/// pour le shaft/head, donc elle fonctionne aussi bien avec un MonoBehaviour classique
/// (LocalBallAimController) qu'avec un NetworkBehaviour Fusion (BallAimController).
///
/// Utilisation :
///   _arrow = new AimArrowVisual(transform, arrowShaftSprite, arrowHeadSprite, arrowSortingOrder, arrowSortingLayerName);
///   _arrow.Show(force, maxForce, viewHeight, transform, aimColor, activeColor, ...);
///   _arrow.Hide();
/// </summary>
public class AimArrowVisual
{
    private readonly Transform _shaftTransform;
    private readonly SpriteRenderer _shaftRenderer;
    private readonly Transform _headTransform;
    private readonly SpriteRenderer _headRenderer;

    private static Sprite _cachedShaftSprite;
    private static Sprite _cachedHeadSprite;

    public AimArrowVisual(Transform parent, Sprite shaftSprite, Sprite headSprite, int sortingOrder, string sortingLayerName)
    {
        GameObject shaftObj = new GameObject("AimArrowShaft");
        shaftObj.transform.SetParent(parent, false);
        _shaftTransform = shaftObj.transform;
        _shaftRenderer = shaftObj.AddComponent<SpriteRenderer>();
        _shaftRenderer.sprite = shaftSprite != null ? shaftSprite : GetOrCreateShaftSprite();
        _shaftRenderer.sortingOrder = sortingOrder;
        _shaftRenderer.sortingLayerName = sortingLayerName;
        _shaftRenderer.enabled = false;

        GameObject headObj = new GameObject("AimArrowHead");
        headObj.transform.SetParent(parent, false);
        _headTransform = headObj.transform;
        _headRenderer = headObj.AddComponent<SpriteRenderer>();
        _headRenderer.sprite = headSprite != null ? headSprite : GetOrCreateHeadSprite();
        _headRenderer.sortingOrder = sortingOrder + 1;
        _headRenderer.sortingLayerName = sortingLayerName;
        _headRenderer.enabled = false;
    }

    /// <summary>
    /// Met à jour l'affichage de la flèche selon la force actuellement visée.
    /// Regroupe le comportement des deux anciennes méthodes UpdateAimVisual :
    /// useAbsoluteMaxArrowLength existait déjà côté Local mais avait été oublié côté
    /// Network (incohérence corrigée au passage par cette unification).
    /// </summary>
    public void Show(
        Vector2 clampedForce, float maxForce, float viewHeight, Transform parent,
        Color aimColor, Color activeColor, float thicknessFraction, float headSizeFraction,
        bool useAbsoluteMaxArrowLength, float maxArrowLength, float maxArrowLengthFraction)
    {
        float forceRatio = maxForce > 0f ? clampedForce.magnitude / maxForce : 0f;

        Color color = Color.Lerp(aimColor, activeColor, forceRatio);
        float thickness = Mathf.Lerp(thicknessFraction * 0.6f, thicknessFraction * 1.4f, forceRatio) * viewHeight;
        float currentHeadSize = Mathf.Lerp(headSizeFraction * 0.7f, headSizeFraction * 1.3f, forceRatio) * viewHeight;

        bool hasDirection = clampedForce.sqrMagnitude > 0.0001f;
        _shaftRenderer.enabled = hasDirection;
        _headRenderer.enabled = hasDirection;
        if (!hasDirection) return;

        float length = useAbsoluteMaxArrowLength
            ? forceRatio * maxArrowLength
            : forceRatio * maxArrowLengthFraction * viewHeight;

        float shaftLength = Mathf.Max(length - currentHeadSize, 0f);
        float angle = Mathf.Atan2(clampedForce.y, clampedForce.x) * Mathf.Rad2Deg;

        float parentAngle = parent.eulerAngles.z;
        float localAngle = angle - parentAngle;
        Quaternion rot = Quaternion.Euler(0f, 0f, localAngle);

        _shaftTransform.localPosition = Vector3.zero;
        _shaftTransform.localRotation = rot;
        _shaftTransform.localScale = new Vector3(shaftLength, thickness, 1f);
        _shaftRenderer.color = color;

        float localAngleRad = localAngle * Mathf.Deg2Rad;
        Vector2 dirLocal = new Vector2(Mathf.Cos(localAngleRad), Mathf.Sin(localAngleRad));

        _headTransform.localPosition = (Vector3)(dirLocal * shaftLength);
        _headTransform.localRotation = rot;
        _headTransform.localScale = new Vector3(currentHeadSize, currentHeadSize, 1f);
        _headRenderer.color = color;
    }

    public void Hide()
    {
        if (_shaftRenderer != null) _shaftRenderer.enabled = false;
        if (_headRenderer != null) _headRenderer.enabled = false;
    }

    private static Sprite GetOrCreateShaftSprite()
    {
        if (_cachedShaftSprite != null) return _cachedShaftSprite;
        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        _cachedShaftSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
        return _cachedShaftSprite;
    }

    private static Sprite GetOrCreateHeadSprite()
    {
        if (_cachedHeadSprite != null) return _cachedHeadSprite;
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float t = (float)x / (size - 1);
                float halfHeight = (1f - t) * (size / 2f);
                float distFromCenter = Mathf.Abs(y - size / 2f);
                pixels[y * size + x] = distFromCenter <= halfHeight ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _cachedHeadSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 0.5f), size);
        return _cachedHeadSprite;
    }
}
