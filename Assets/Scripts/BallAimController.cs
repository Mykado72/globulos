using Fusion;
using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public class BallAimController : NetworkBehaviour
{
    [Header("Aim Settings")]
    [SerializeField] private float maxForce = 15f;
    [SerializeField, Range(0.01f, 1f)] private float maxDragDistanceFraction = 0.35f;

    [Header("Arrow Visual Settings")]
    [SerializeField] private Sprite arrowShaftSprite;
    [SerializeField] private Sprite arrowHeadSprite;
    [SerializeField] private Color aimColor = new Color(1, 0.5f, 0, 1); // Orange
    [SerializeField] private Color activeColor = new Color(1, 0, 0, 1); // Rouge
    [SerializeField] private int arrowSortingOrder = 20;

    [SerializeField, Range(0.01f, 1f)] private float maxArrowLengthFraction = 0.35f;
    [SerializeField, Range(0.001f, 0.2f)] private float headSizeFraction = 0.05f;
    [SerializeField, Range(0.0005f, 0.1f)] private float thicknessFraction = 0.015f;
    [SerializeField] private float fallbackViewHeight = 10f;

    [Header("Fall Animation Settings")]
    [SerializeField] private float fallDuration = 1.5f;
    [SerializeField] private float shrinkStartTime = 0.5f; // Commence à rétrécir après 0.5s
    [SerializeField] private AnimationCurve fallCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve rotateCurve = AnimationCurve.Linear(0, 0, 1, 1);
    [SerializeField] private float totalRotation = 720f; // 2 rotations complètes

    // ✅ État de la bille synchronisé via le réseau
    [Networked] public bool IsAiming { get; set; }
    [Networked] public bool IsDead { get; set; } // ✅ NOUVEAU : état mort/vivant

    private Rigidbody2D _rb;
    private Camera _mainCamera;
    private NetworkObject _networkObject;
    private SpriteRenderer _spriteRenderer;

    private Transform _shaftTransform;
    private SpriteRenderer _shaftRenderer;
    private Transform _headTransform;
    private SpriteRenderer _headRenderer;

    private Vector2 _startDragPos;
    private Vector2 _bufferedForce;

    [SerializeField]
    private int forceMultiplier;

    // Sprites générés une seule fois et partagés par toutes les instances de boules
    private static Sprite _cachedShaftSprite;
    private static Sprite _cachedHeadSprite;

    public override void Spawned()
    {
        Debug.Log($"[BallAimController] Spawned() - HasInputAuthority: {HasInputAuthority}, InputAuthority: {GetComponent<NetworkObject>().InputAuthority}");

        _rb = GetComponent<Rigidbody2D>();
        _networkObject = GetComponent<NetworkObject>();
        _spriteRenderer = GetComponent<SpriteRenderer>();

        ConfigureArrowVisual();

        _mainCamera = Camera.main;
        if (_mainCamera == null) _mainCamera = FindObjectOfType<Camera>();

        if (_mainCamera == null)
        {
            Debug.LogError("[BallAimController] ERREUR : Aucune caméra trouvée !");
            enabled = false;
            return;
        }

        IsDead = false; // ✅ NOUVEAU : on commence vivant
        Debug.Log($"[BallAimController] ✅ Setup complet - HasInputAuthority: {HasInputAuthority}, InputAuthority PlayerRef: {_networkObject.InputAuthority}");
    }

    public override void Render()
    {
        if (TurnManager.Instance != null && TurnManager.Instance.IsTurnBased)
        {
            if (TurnManager.Instance.CurrentState == TurnManager.TurnState.Resolution)
            {
                if (_bufferedForce != Vector2.zero && HasInputAuthority)
                {
                    RPC_ApplyForce(_bufferedForce);
                    _bufferedForce = Vector2.zero;
                }
            }
        }
    }

    private void ConfigureArrowVisual()
    {
        GameObject shaftObj = new GameObject("AimArrowShaft");
        shaftObj.transform.SetParent(transform, false);
        _shaftTransform = shaftObj.transform;
        _shaftRenderer = shaftObj.AddComponent<SpriteRenderer>();
        _shaftRenderer.sprite = arrowShaftSprite != null ? arrowShaftSprite : GetOrCreateShaftSprite();
        _shaftRenderer.sortingOrder = arrowSortingOrder;
        _shaftRenderer.enabled = false;

        GameObject headObj = new GameObject("AimArrowHead");
        headObj.transform.SetParent(transform, false);
        _headTransform = headObj.transform;
        _headRenderer = headObj.AddComponent<SpriteRenderer>();
        _headRenderer.sprite = arrowHeadSprite != null ? arrowHeadSprite : GetOrCreateHeadSprite();
        _headRenderer.sortingOrder = arrowSortingOrder + 1;
        _headRenderer.enabled = false;

        Debug.Log("[BallAimController] Flèche (sprites étirables) configurée ✅");
    }

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

        // ✅ Ne pas pouvoir viser si la bille est morte
        if (!HasInputAuthority || IsDead) return;

        if (TurnManager.Instance != null && TurnManager.Instance.IsTurnBased)
        {
            if (TurnManager.Instance.CurrentState != TurnManager.TurnState.Aiming) return;
        }

        IsAiming = true;
        _startDragPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        UpdateAimVisual(Vector2.zero);

        Debug.Log($"[BallAimController] ✅ Aiming started! (Autorité: {_networkObject.InputAuthority}) - Vue caméra (hauteur monde): {GetViewHeight():F2}");
    }

    private void OnMouseDrag()
    {
        if (!IsAiming || !HasInputAuthority || IsDead) return;
        if (_mainCamera == null) return;

        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragVector = currentMousePos - _startDragPos;
        Vector2 clampedForce = Vector2.ClampMagnitude(dragVector * forceMultiplier, maxForce);

        UpdateAimVisual(clampedForce);
    }

    private Vector2 ComputeClampedForce(Vector2 currentMousePos)
    {
        Vector2 dragVector = currentMousePos - _startDragPos;
        float viewHeight = GetViewHeight();
        float maxDragDistance = maxDragDistanceFraction * viewHeight;

        float dragRatio = maxDragDistance > 0f ? Mathf.Clamp01(dragVector.magnitude / maxDragDistance) : 0f;
        return dragVector.normalized * dragRatio * maxForce;
    }

    private float GetViewHeight()
    {
        if (_mainCamera != null && _mainCamera.orthographic)
        {
            return _mainCamera.orthographicSize * 2f;
        }
        return fallbackViewHeight;
    }

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

        float length = forceRatio * maxArrowLengthFraction * viewHeight;
        float shaftLength = Mathf.Max(length - currentHeadSize, 0f);
        float angle = Mathf.Atan2(clampedForce.y, clampedForce.x) * Mathf.Rad2Deg;

        float parentAngle = transform.eulerAngles.z;
        float localAngle = angle - parentAngle;

        Vector2 dir = clampedForce.normalized;
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

    private void OnMouseUp()
    {
        if (!IsAiming || !HasInputAuthority || IsDead) return;

        IsAiming = false;
        _shaftRenderer.enabled = false;
        _headRenderer.enabled = false;

        if (_mainCamera == null) return;

        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 forceToApply = ComputeClampedForce(currentMousePos);

        bool isTurnBased = TurnManager.Instance != null && TurnManager.Instance.IsTurnBased;

        if (isTurnBased)
        {
            _bufferedForce = forceToApply;
        }
        else
        {
            Debug.Log($"[BallAimController] ✅ Shoot! Force: {forceToApply.magnitude:F2}");
            RPC_ApplyForce(forceToApply);
        }
    }

    public void ExecuteBufferedShoot()
    {
        if (_bufferedForce != Vector2.zero)
        {
            RPC_ApplyForce(_bufferedForce);
            _bufferedForce = Vector2.zero;
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_ApplyForce(Vector2 force)
    {
        if (_rb != null)
        {
            _rb.AddForce(force, ForceMode2D.Impulse);
            Debug.Log($"[BallAimController] Force appliquée via RPC: {force}");
        }
    }

    // ✅ NOUVEAU : Détection de collision avec un but
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Vérifier si on entre en collision avec un "Goal" (but)
        if (collision.CompareTag("Goal") && !IsDead)
        {
            Debug.Log($"[BallAimController] 🎯 Bille entrée dans un but! PlayerRef: {_networkObject.InputAuthority}");

            // Désactiver les inputs immédiatement
            IsAiming = false;
            _shaftRenderer.enabled = false;
            _headRenderer.enabled = false;

            // Appeler la RPC pour synchroniser l'animation de chute sur tous les clients
            RPC_PlayFallAnimation();
        }
    }

    // ✅ NOUVEAU : RPC pour synchroniser l'animation de chute
    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_PlayFallAnimation()
    {
        StartCoroutine(FallAnimationCoroutine());
    }

    // ✅ NOUVEAU : Coroutine pour l'animation de chute (rotation + rétrécissement + disparition)
    private IEnumerator FallAnimationCoroutine()
    {
        IsDead = true;
        _rb.isKinematic = true; // Arrêter la physique
        _rb.velocity = Vector2.zero;

        float elapsedTime = 0f;
        Vector3 startScale = transform.localScale;

        while (elapsedTime < fallDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / fallDuration;

            // ✅ Rotation progressive : 0° -> totalRotation (720°)
            float currentRotation = rotateCurve.Evaluate(t) * totalRotation;
            transform.Rotate(Vector3.forward, currentRotation - (rotateCurve.Evaluate(t - Time.deltaTime / fallDuration) * totalRotation));

            // ✅ Rétrécissement à partir de shrinkStartTime
            float shrinkRatio = Mathf.Max(0f, t - shrinkStartTime) / (1f - shrinkStartTime);
            float scale = Mathf.Lerp(1f, 0f, shrinkRatio);
            transform.localScale = startScale * scale;

            // ✅ Changement d'alpha (transparence) : fade out
            float alpha = fallCurve.Evaluate(t);
            Color color = _spriteRenderer.color;
            color.a = 1f - alpha;
            _spriteRenderer.color = color;

            yield return null;
        }

        // ✅ Faire disparaître la bille
        gameObject.SetActive(false);
        /*
        // ✅ Vérifier si le joueur n'a plus de billes vivantes
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.CheckGameEnd();
        }
        */
    }

    // ✅ Getter pour vérifier si la bille est vivante
    public bool IsAlive => !IsDead;
}
