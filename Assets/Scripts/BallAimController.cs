using Fusion;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
// ✅ CORRECTIF bug de position : sans ce composant, Runner.Spawn() ne synchronise
// la position QUE sur le client qui a spawné l'objet (voir doc Fusion :
// "if you pass a position [...] it will only affect the object's transform on the peer that spawns it").
// NetworkTransform réplique la position (et, avec un Rigidbody2D en Interpolate + Forecast Physics
// activé, la physique elle-même) depuis le StateAuthority vers tous les autres clients.
// En Shared Mode (Fusion 2.1+), NetworkTransform est le composant officiellement recommandé pour ça
// (le "Physics Addon" / NetworkRigidbody2D séparé n'est PAS supporté en Shared Mode).
[RequireComponent(typeof(NetworkTransform))]
public class BallAimController : NetworkBehaviour
{
    [Header("Aim Settings")]
    [SerializeField] private float maxForce = 15f;
    // ✅ CORRECTIF proportionnalité : la distance de drag est exprimée en fraction de la hauteur
    // visible par la caméra (comme pour la taille de la flèche), plutôt qu'en unités "monde" brutes.
    // Un drag qui atteint cette fraction (ex: 0.35 = 35% de la hauteur d'écran) applique la force
    // maximale (maxForce). En dessous, la force est proportionnelle à la distance parcourue par
    // la souris. Ça évite que la force sature instantanément quand la caméra a une grande échelle.
    [SerializeField, Range(0.01f, 1f)] private float maxDragDistanceFraction = 0.35f;

    [Header("Arrow Visual Settings")]
    // ✅ Optionnel : assigne tes propres sprites ici si tu en as (tige horizontale + pointe
    // triangulaire, tous deux orientés vers la DROITE avec un pivot sur le bord GAUCHE).
    // Si tu laisses vide, des sprites simples sont générés automatiquement par code
    // (aucune dépendance à un shader/asset externe = plus de bug d'invisibilité).
    [SerializeField] private Sprite arrowShaftSprite;
    [SerializeField] private Sprite arrowHeadSprite;
    [SerializeField] private Color aimColor = new Color(1, 0.5f, 0, 1); // Orange
    [SerializeField] private Color activeColor = new Color(1, 0, 0, 1); // Rouge
    [SerializeField] private int arrowSortingOrder = 20;

    // ✅ CORRECTIF échelle : la longueur/épaisseur de la flèche ne sont PLUS basées sur
    // clampedForce.magnitude (une valeur de FORCE physique, sans rapport avec les unités
    // de distance de ta scène). Elles sont calculées comme une FRACTION de la hauteur visible
    // par la caméra (2 x orthographicSize) : la flèche reste donc toujours bien proportionnée,
    // quelle que soit l'échelle du monde ou le zoom de la caméra.
    [SerializeField, Range(0.01f, 1f)] private float maxArrowLengthFraction = 0.35f;
    [SerializeField, Range(0.001f, 0.2f)] private float headSizeFraction = 0.05f;
    [SerializeField, Range(0.0005f, 0.1f)] private float thicknessFraction = 0.015f;
    // Utilisé uniquement si la caméra n'est PAS orthographique (fallback arbitraire).
    [SerializeField] private float fallbackViewHeight = 10f;

    // ✅ Variables synchronisées via le réseau
    [Networked] public bool IsAiming { get; set; }

    private Rigidbody2D _rb;
    private Camera _mainCamera;
    private NetworkObject _networkObject;

    private Transform _shaftTransform;
    private SpriteRenderer _shaftRenderer;
    private Transform _headTransform;
    private SpriteRenderer _headRenderer;

    private Vector2 _startDragPos;

    // Sprites générés une seule fois et partagés par toutes les instances de boules.
    private static Sprite _cachedShaftSprite;
    private static Sprite _cachedHeadSprite;

    public override void Spawned()
    {
        Debug.Log($"[BallAimController] Spawned() - HasInputAuthority: {HasInputAuthority}, InputAuthority: {GetComponent<NetworkObject>().InputAuthority}");

        _rb = GetComponent<Rigidbody2D>();
        _networkObject = GetComponent<NetworkObject>();

        ConfigureArrowVisual();

        _mainCamera = Camera.main;
        if (_mainCamera == null) _mainCamera = FindObjectOfType<Camera>();

        if (_mainCamera == null)
        {
            Debug.LogError("[BallAimController] ERREUR : Aucune caméra trouvée !");
            enabled = false;
            return;
        }

        Debug.Log($"[BallAimController] ✅ Setup complet - HasInputAuthority: {HasInputAuthority}, InputAuthority PlayerRef: {_networkObject.InputAuthority}");
    }

    private void ConfigureArrowVisual()
    {
        // --- Tige de la flèche (s'étire selon la force) ---
        GameObject shaftObj = new GameObject("AimArrowShaft");
        shaftObj.transform.SetParent(transform, false);
        _shaftTransform = shaftObj.transform;
        _shaftRenderer = shaftObj.AddComponent<SpriteRenderer>();
        _shaftRenderer.sprite = arrowShaftSprite != null ? arrowShaftSprite : GetOrCreateShaftSprite();
        _shaftRenderer.sortingOrder = arrowSortingOrder;
        _shaftRenderer.enabled = false;

        // --- Pointe de la flèche (taille fixe, collée au bout de la tige) ---
        GameObject headObj = new GameObject("AimArrowHead");
        headObj.transform.SetParent(transform, false);
        _headTransform = headObj.transform;
        _headRenderer = headObj.AddComponent<SpriteRenderer>();
        _headRenderer.sprite = arrowHeadSprite != null ? arrowHeadSprite : GetOrCreateHeadSprite();
        _headRenderer.sortingOrder = arrowSortingOrder + 1;
        _headRenderer.enabled = false;

        Debug.Log("[BallAimController] Flèche (sprites étirables) configurée ✅");
    }

    // ✅ Génère un sprite 1x1 blanc, pivot sur le bord gauche, largeur = 1 unité du monde à scale=1.
    // Cela permet d'étirer la tige simplement en changeant transform.localScale.x = longueur voulue.
    private static Sprite GetOrCreateShaftSprite()
    {
        if (_cachedShaftSprite != null) return _cachedShaftSprite;

        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        _cachedShaftSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
        return _cachedShaftSprite;
    }

    // ✅ Génère un triangle pointant vers la droite, pivot sur le bord gauche (base du triangle),
    // largeur = 1 unité du monde à scale=1.
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
                float t = (float)x / (size - 1);              // 0 (base) -> 1 (pointe)
                float halfHeight = (1f - t) * (size / 2f);     // se rétrécit vers la pointe
                float distFromCenter = Mathf.Abs(y - size / 2f);
                pixels[y * size + x] = distFromCenter <= halfHeight ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        _cachedHeadSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0f, 0.5f), size);
        return _cachedHeadSprite;
    }

    private void OnMouseDown()
    {
        if (_mainCamera == null)
        {
            Debug.LogError("[BallAimController] OnMouseDown : Camera est null !");
            return;
        }

        // ✅ CRITIQUE : Vérifier que JE contrôle cette boule !
        if (!HasInputAuthority)
        {
            Debug.Log($"[BallAimController] OnMouseDown : Je n'ai pas l'autorité sur cette boule (InputAuthority: {_networkObject.InputAuthority})");
            return;
        }

        IsAiming = true;
        _startDragPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        UpdateAimVisual(Vector2.zero);

        Debug.Log($"[BallAimController] ✅ Aiming started! (Autorité: {_networkObject.InputAuthority}) - " +
            $"Vue caméra (hauteur monde): {GetViewHeight():F2}");
    }

    private void OnMouseDrag()
    {
        if (!IsAiming || !HasInputAuthority) return;
        if (_mainCamera == null) return;

        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 clampedForce = ComputeClampedForce(currentMousePos);

        UpdateAimVisual(clampedForce);
    }

    // ✅ Convertit le vecteur de drag (en unités monde, potentiellement à une échelle
    // énorme selon la caméra) en une force 0→maxForce basée sur un ratio RELATIF
    // à la hauteur de vue de la caméra, plutôt que sur la distance brute.
    private Vector2 ComputeClampedForce(Vector2 currentMousePos)
    {
        Vector2 dragVector = _startDragPos - currentMousePos;
        float viewHeight = GetViewHeight();
        float maxDragDistance = maxDragDistanceFraction * viewHeight;

        float dragRatio = maxDragDistance > 0f ? Mathf.Clamp01(dragVector.magnitude / maxDragDistance) : 0f;
        return dragVector.normalized * dragRatio * maxForce;
    }

    // ✅ Hauteur du monde visible par la caméra (2 x orthographicSize).
    // Sert de référence pour que la flèche garde une taille cohérente à l'écran
    // quelle que soit l'échelle du monde ou le zoom de la caméra.
    private float GetViewHeight()
    {
        if (_mainCamera != null && _mainCamera.orthographic)
        {
            return _mainCamera.orthographicSize * 2f;
        }
        return fallbackViewHeight;
    }

    // ✅ Positionne/étire la tige et la pointe. La DIRECTION vient du vecteur de drag ;
    // la TAILLE (longueur, épaisseur, couleur) est proportionnelle à forceRatio (0→1),
    // exprimée en fraction de la hauteur visible par la caméra (donc toujours visible,
    // peu importe l'échelle "physique" de clampedForce).
    private void UpdateAimVisual(Vector2 clampedForce)
    {
        float forceRatio = maxForce > 0f ? clampedForce.magnitude / maxForce : 0f;
        float viewHeight = GetViewHeight();

        Color color = Color.Lerp(aimColor, activeColor, forceRatio);
        float thickness = Mathf.Lerp(thicknessFraction * 0.6f, thicknessFraction * 1.4f, forceRatio) * viewHeight;
        float currentHeadSize = Mathf.Lerp(headSizeFraction * 0.7f, headSizeFraction * 1.3f, forceRatio) * viewHeight;

        bool hasDirection = clampedForce.sqrMagnitude > 0.0001f;
        _shaftRenderer.enabled = hasDirection;
        _headRenderer.enabled = hasDirection;

        if (!hasDirection) return;

        // La longueur affichée est purement basée sur forceRatio (0→1), PAS sur
        // clampedForce.magnitude directement : elle est donc indépendante des unités physiques.
        float length = forceRatio * maxArrowLengthFraction * viewHeight;
        float shaftLength = Mathf.Max(length - currentHeadSize, 0f);
        float angle = Mathf.Atan2(clampedForce.y, clampedForce.x) * Mathf.Rad2Deg;
        Vector2 dir = clampedForce.normalized;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);

        // La tige part du centre de la boule (pivot = bord gauche du sprite)
        _shaftTransform.localPosition = Vector3.zero;
        _shaftTransform.localRotation = rot;
        _shaftTransform.localScale = new Vector3(shaftLength, thickness, 1f);
        _shaftRenderer.color = color;

        // La pointe est collée exactement au bout de la tige, taille fixe (non étirée)
        _headTransform.localPosition = (Vector3)(dir * shaftLength);
        _headTransform.localRotation = rot;
        _headTransform.localScale = new Vector3(currentHeadSize, currentHeadSize, 1f);
        _headRenderer.color = color;
    }

    private void OnMouseUp()
    {
        if (!IsAiming || !HasInputAuthority) return;

        IsAiming = false;
        _shaftRenderer.enabled = false;
        _headRenderer.enabled = false;

        if (_mainCamera == null) return;

        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 forceToApply = ComputeClampedForce(currentMousePos);

        Debug.Log($"[BallAimController] ✅ Shoot! Force: {forceToApply.magnitude:F2}");

        // ✅ Utiliser une RPC pour appliquer la force sur tous les clients
        RPC_ApplyForce(forceToApply);
    }

    // ✅ RPC pour synchroniser la force appliquée sur tous les clients
    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_ApplyForce(Vector2 force)
    {
        if (_rb != null)
        {
            _rb.AddForce(force, ForceMode2D.Impulse);
            Debug.Log($"[BallAimController] Force appliquée via RPC: {force}");
        }
    }
}