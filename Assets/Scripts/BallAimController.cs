using Fusion;
using Fusion.Addons.Physics;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]

public class BallAimController : NetworkBehaviour
{
    [Header("Aim Settings")]
    [SerializeField] private float maxForce = 15f;
    [SerializeField, Range(0.5f, 2f)] private float maxDragDistanceFraction = 0.35f;

    [Header("Arrow Visual Settings")]
    [SerializeField] private Sprite arrowShaftSprite;
    [SerializeField] private Sprite arrowHeadSprite;
    [SerializeField] private Color aimColor = new Color(1, 0.5f, 0, 1);
    [SerializeField] private Color activeColor = new Color(1, 0, 0, 1);
    [SerializeField] private int arrowSortingOrder = 20;

    [SerializeField, Range(0.01f, 1f)] private float maxArrowLengthFraction = 1.0f;
    [SerializeField, Range(0.001f, 0.2f)] private float headSizeFraction = 0.05f;
    [SerializeField, Range(0.0005f, 0.1f)] private float thicknessFraction = 0.015f;
    [SerializeField] private float fallbackViewHeight = 10f;

    [Header("Fall Animation Settings")]
    [SerializeField] private float fallDuration = 1.5f;
    [SerializeField] private float shrinkStartTime = 0.5f;
    [SerializeField] private AnimationCurve fallCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve rotateCurve = AnimationCurve.Linear(0, 0, 1, 1);
    [SerializeField] private float totalRotation = 720f;

    // ✅ État synchronisé via le réseau
    [Networked] public bool IsAiming { get; set; }
    [Networked] public bool IsDead { get; set; }
    [Networked] public bool IsMoving { get; set; }

    [SerializeField] private float stationaryVelocityThreshold = 0.15f;
    [SerializeField] private int forceMultiplier = 1;

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

    private static Sprite _cachedShaftSprite;
    private static Sprite _cachedHeadSprite;

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();
        _networkObject = GetComponent<NetworkObject>();
        _spriteRenderer = GetComponent<SpriteRenderer>();

        Debug.Log($"[BallAimController] Spawned() - HasStateAuthority: {HasStateAuthority}, StateAuthority: {_networkObject.StateAuthority.PlayerId}, InputAuthority: {_networkObject.InputAuthority.PlayerId}, LocalPlayer: {Runner.LocalPlayer.PlayerId}");

        ConfigureArrowVisual();

        _mainCamera = Camera.main;
        if (_mainCamera == null) _mainCamera = FindObjectOfType<Camera>();

        if (_mainCamera == null)
        {
            Debug.LogError("[BallAimController] ERREUR : Aucune caméra trouvée !");
            enabled = false;
            return;
        }

        Debug.Log($"[BallAimController] ✅ Setup complet");
    }

    public void ForceStopAiming()
    {
        if (!IsAiming) return;

        Debug.Log("[BallAimController] Arrêt forcé du visage");

        IsAiming = false;

        // ✅ FIX : on ne cache plus la flèche ici. Elle doit rester visible tant que le tir
        // n'a pas réellement été appliqué (ApplyForce s'en charge). S'il n'y a aucun tir en
        // attente (le joueur n'a pas assez tiré), on la cache directement.
        if (_bufferedForce != Vector2.zero && HasStateAuthority)
        {
            ApplyForce(_bufferedForce);
            _bufferedForce = Vector2.zero;
        }
        else
        {
            HideAimVisual();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (_rb != null)
        {
            float thresholdSqr = stationaryVelocityThreshold * stationaryVelocityThreshold;
            IsMoving = !IsDead && _rb.velocity.sqrMagnitude > thresholdSqr;
        }
    }

    public override void Render()
    {
        if (TurnManager.Instance != null && TurnManager.Instance.IsTurnBased)
        {
            if (TurnManager.Instance.CurrentState == TurnManager.TurnState.Resolution)
            {
                // ✅ FIX : on vérifie HasStateAuthority (et non plus HasInputAuthority) puisque
                // c'est la State Authority qui a le droit d'appliquer la force sur son propre
                // Rigidbody2D en Shared Mode.
                if (_bufferedForce != Vector2.zero && HasStateAuthority)
                {
                    ApplyForce(_bufferedForce);
                    _bufferedForce = Vector2.zero;
                }
            }
        }
    }

    // ✅ NOUVEAU : point unique pour cacher la flèche, appelé uniquement quand le tir
    // est réellement parti (dans ApplyForce), et non plus au relâchement de la souris.
    private void HideAimVisual()
    {
        if (_shaftRenderer != null) _shaftRenderer.enabled = false;
        if (_headRenderer != null) _headRenderer.enabled = false;
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

        Debug.Log("[BallAimController] Flèche configurée ✅");
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
        if (_mainCamera == null || !HasStateAuthority || IsDead) return;

        if (TurnManager.Instance != null && TurnManager.Instance.IsTurnBased)
        {
            if (TurnManager.Instance.CurrentState != TurnManager.TurnState.Aiming) return;
        }

        IsAiming = true;
        _startDragPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);

        // ✅ Force l'affichage des flèches
        _shaftRenderer.enabled = true;
        _headRenderer.enabled = true;
    }

    private void OnMouseDrag()
    {
        if (!IsAiming || !HasStateAuthority || IsDead) return;
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
        if (!IsAiming || !HasStateAuthority || IsDead) return;

        IsAiming = false;

        if (_mainCamera == null)
        {
            HideAimVisual();
            return;
        }

        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 forceToApply = ComputeClampedForce(currentMousePos);

        // ✅ FIX : on fige l'affichage de la flèche sur la force finalement choisie au
        // relâchement, au lieu de la cacher. Elle restera visible tant que le tir n'est
        // pas réellement exécuté (voir ApplyForce), ce qui donne au joueur une confirmation
        // visuelle de son tir pendant le temps d'attente en mode tour par tour.
        UpdateAimVisual(forceToApply);

        bool isTurnBased = TurnManager.Instance != null && TurnManager.Instance.IsTurnBased;

        if (forceToApply == Vector2.zero)
        {
            // Rien à tirer (glissé trop court) : pas de tir en attente, on cache direct.
            HideAimVisual();
        }
        else if (isTurnBased)
        {
            _bufferedForce = forceToApply;
        }
        else
        {
            Debug.Log($"[BallAimController] ✅ Shoot! Force: {forceToApply.magnitude:F2}");
            ApplyForce(forceToApply);
        }
    }

    public void ExecuteBufferedShoot()
    {
        if (_bufferedForce != Vector2.zero)
        {
            ApplyForce(_bufferedForce);
            _bufferedForce = Vector2.zero;
        }
    }

    private void ApplyForce(Vector2 force)
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[BallAimController] ⚠️ Tentative d'application de force sans State Authority, ignorée.");
            return;
        }

        if (_rb != null)
        {
            _rb.AddForce(force, ForceMode2D.Impulse);
            Debug.Log($"[BallAimController] Force appliquée : {force}");
        }

        // ✅ Le tir est parti pour de bon : c'est SEULEMENT maintenant qu'on cache la flèche.
        HideAimVisual();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Goal") && !IsDead)
        {
            Debug.Log($"[BallAimController] 🎯 Bille entrée dans un but!");

            IsAiming = false;
            HideAimVisual();

            RPC_PlayFallAnimation();
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_PlayFallAnimation()
    {
        StartCoroutine(FallAnimationCoroutine());
    }

    private IEnumerator FallAnimationCoroutine()
    {
        IsDead = true;
        _rb.isKinematic = true;
        _rb.velocity = Vector2.zero;

        float elapsedTime = 0f;
        Vector3 startScale = transform.localScale;

        while (elapsedTime < fallDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / fallDuration;

            float currentRotation = rotateCurve.Evaluate(t) * totalRotation;
            transform.Rotate(Vector3.forward, currentRotation - (rotateCurve.Evaluate(t - Time.deltaTime / fallDuration) * totalRotation));

            float shrinkRatio = Mathf.Max(0f, t - shrinkStartTime) / (1f - shrinkStartTime);
            float scale = Mathf.Lerp(1f, 0f, shrinkRatio);
            transform.localScale = startScale * scale;

            float alpha = fallCurve.Evaluate(t);
            Color color = _spriteRenderer.color;
            color.a = 1f - alpha;
            _spriteRenderer.color = color;

            yield return null;
        }

        // ❌ SUPPRIME CETTE LIGNE :
        // gameObject.SetActive(false);

        // ✅ À la place, désactive juste le collider pour éviter les collisions
        Collider2D collider = GetComponent<Collider2D>();
        if (collider != null)
            collider.enabled = false;
    }

    public bool IsAlive => !IsDead;
}