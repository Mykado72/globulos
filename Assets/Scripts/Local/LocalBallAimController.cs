using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class LocalBallAimController : MonoBehaviour
{
    [Header("Aim Settings")]
    [SerializeField] private float maxForce = 15f;
    [SerializeField, Range(0.25f, 10.0f)] private float maxDragDistanceFraction = 0.5f;

    [Header("Arrow Visual Settings")]
    [SerializeField] private Sprite arrowShaftSprite;
    [SerializeField] private Sprite arrowHeadSprite;
    [SerializeField] private Color aimColor = new Color(1, 0.5f, 0, 1);
    [SerializeField] private Color activeColor = new Color(1, 0, 0, 1);
    [SerializeField] private int arrowSortingOrder = 20;
    [SerializeField] private string arrowSortingLayerName = "Entities";

    [Tooltip("Active le réglage de la longueur max de la flèche en unités Unity fixes.")]
    [SerializeField] private bool useAbsoluteMaxArrowLength = true;
    [Tooltip("Longueur maximale de la flèche en unités Unity.")]
    [SerializeField] private float maxArrowLength = 3.0f;

    [SerializeField, Range(0.1f, 5.0f)] private float maxArrowLengthFraction = 1.5f;
    [SerializeField, Range(0.001f, 0.2f)] private float headSizeFraction = 0.05f;
    [SerializeField, Range(0.0005f, 0.1f)] private float thicknessFraction = 0.015f;
    [SerializeField] private float fallbackViewHeight = 10f;

    [Header("Fall Animation Settings")]
    [SerializeField] private float fallDuration = 1.5f;
    [SerializeField] private float shrinkStartTime = 0.5f;
    [SerializeField] private AnimationCurve fallCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve rotateCurve = AnimationCurve.Linear(0, 0, 1, 1);
    [SerializeField] private float totalRotation = 720f;

    [Header("Bounce Effects Settings")]
    [SerializeField] private float bounceForceThreshold = 1f;
    [SerializeField] private float squashDuration = 0.15f;
    [SerializeField] private float squashAmount = 0.7f;
    [SerializeField] private float stretchAmount = 1.2f;

    [SerializeField] private float maxTurnDuration = 6.0f; // Durée max d'un tir en secondes
    [SerializeField] private float stationaryVelocityThreshold = 2f;

    [Header("IA (bot)")]
    [Tooltip("Décalage angulaire max (en degrés) ajouté à la visée de l'IA pour simuler l'imprécision.")]
    [SerializeField, Range(0f, 45f)] private float aiAimInaccuracyDegrees = 12f;
    [Tooltip("Fraction min de la force max utilisée par l'IA.")]
    [SerializeField, Range(0.5f, 1f)] private float aiMinForceFraction = 0.7f;

    public static readonly List<LocalBallAimController> AllBalls = new List<LocalBallAimController>();

    public int OwnerPlayerId { get; private set; }
    public bool IsAiming { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsMoving { get; private set; }

    private bool _isBotControlled = false;
    private bool _botHasQueuedThisTurn = false;
    private GoalZone _aiTargetGoal;

    private Rigidbody2D _rb;
    private Camera _mainCamera;
    private SpriteRenderer _spriteRenderer;
    private Transform _shaftTransform;
    private SpriteRenderer _shaftRenderer;
    private Transform _headTransform;
    private SpriteRenderer _headRenderer;

    private Vector2 _startDragPos;
    private Vector2 _localQueuedForce = Vector2.zero;
    private Vector3 _originalScale;

    private static Sprite _cachedShaftSprite;
    private static Sprite _cachedHeadSprite;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _originalScale = transform.localScale;
        ConfigureArrowVisual();
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        if (_mainCamera == null) _mainCamera = FindObjectOfType<Camera>();
    }

    private void OnEnable()
    {
        if (!AllBalls.Contains(this)) AllBalls.Add(this);
    }

    private void OnDisable()
    {
        AllBalls.Remove(this);
    }

    public void SetOwner(int playerId) => OwnerPlayerId = playerId;

    public void SetBotControlled(bool isBot)
    {
        _isBotControlled = isBot;
        _botHasQueuedThisTurn = false;
        _aiTargetGoal = null;
    }

    public void ForceStopAiming()
    {
        IsAiming = false;
        HideAimVisual();
    }

    private void Update()
    {
        if (IsDead) return;

        // Mise à jour de l'état du mouvement
        if (_rb != null)
        {
            float thresholdSqr = stationaryVelocityThreshold * stationaryVelocityThreshold;
            if (_rb.velocity.sqrMagnitude > thresholdSqr)
            {
                IsMoving = true;
            }
            else
            {
                IsMoving = false;
                _rb.velocity = Vector2.zero;
                _rb.angularVelocity = 0f;
            }
        }

        // Logique IA
        if (_isBotControlled)
        {
            UpdateBotAiming();
            return;
        }

        // Seul le joueur 1 (humain) peut cliquer
        if (OwnerPlayerId != 1) return;

        if (_localQueuedForce.sqrMagnitude > 0.01f && !IsAiming)
        {
            UpdateAimVisual(_localQueuedForce);
        }

        if (Input.GetMouseButtonDown(0))
        {
            StartAimingCheck();
        }

        if (IsAiming)
        {
            if (Input.GetMouseButton(0))
            {
                ContinueAiming();
            }
            else if (Input.GetMouseButtonUp(0))
            {
                FinishAiming();
            }
        }
    }

    private void StartAimingCheck()
    {
        if (_mainCamera == null) return;

        // 1. Vérifier si une bille bouge encore
        if (LocalTurnManager.Instance != null && IsAnyBallMoving())
        {
            return;
        }

        // 2. Vérifier si on est bien en phase de visée
        if (LocalTurnManager.Instance != null && LocalTurnManager.Instance.CurrentState != LocalTurnManager.TurnState.Aiming)
        {
            return;
        }

        Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mouseWorld, Vector2.zero);

        if (hit.collider != null && hit.collider.gameObject == gameObject)
        {
            IsAiming = true;
            _startDragPos = mouseWorld;
            _shaftRenderer.enabled = true;
            _headRenderer.enabled = true;
        }
    }

    // Ajouter cette méthode utilitaire dans LocalBallAimController.cs
    private bool IsAnyBallMoving()
    {
        foreach (var ball in AllBalls)
        {
            if (ball != null && !ball.IsDead && ball.IsMoving) return true;
        }
        return false;
    }

    private void UpdateBotAiming()
    {
        if (LocalTurnManager.Instance == null) return;

        if (LocalTurnManager.Instance.CurrentState != LocalTurnManager.TurnState.Aiming)
        {
            _botHasQueuedThisTurn = false;
            return;
        }

        if (_botHasQueuedThisTurn) return;

        if (_aiTargetGoal == null) FindTargetGoal();

        if (_aiTargetGoal == null)
        {
            _botHasQueuedThisTurn = true;
            return;
        }

        Vector2 toGoal = (Vector2)_aiTargetGoal.transform.position - (Vector2)transform.position;
        if (toGoal.sqrMagnitude < 0.0001f)
        {
            _botHasQueuedThisTurn = true;
            return;
        }

        float randomAngleOffset = Random.Range(-aiAimInaccuracyDegrees, aiAimInaccuracyDegrees);
        Vector2 aimDirection = Quaternion.Euler(0f, 0f, randomAngleOffset) * toGoal.normalized;

        float forceMagnitude = maxForce * Random.Range(aiMinForceFraction, 1f);

        _localQueuedForce = aimDirection * forceMagnitude;
        _botHasQueuedThisTurn = true;
    }

    private void FindTargetGoal()
    {
        GoalZone.GoalTeam ownTeam = (OwnerPlayerId % 2 == 0) ? GoalZone.GoalTeam.Jaune : GoalZone.GoalTeam.Rouge;

        foreach (GoalZone goal in FindObjectsOfType<GoalZone>())
        {
            if (goal.DefendingTeam != ownTeam)
            {
                _aiTargetGoal = goal;
                return;
            }
        }
    }

    private void ContinueAiming()
    {
        if (_mainCamera == null) return;
        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 forceToApply = ComputeClampedForce(currentMousePos);
        UpdateAimVisual(forceToApply);
    }

    private void FinishAiming()
    {
        IsAiming = false;
        if (_mainCamera == null) return;

        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 forceToApply = ComputeClampedForce(currentMousePos);

        if (forceToApply.sqrMagnitude > 0.1f)
        {
            _localQueuedForce = forceToApply;
        }
        else
        {
            _localQueuedForce = Vector2.zero;
            HideAimVisual();
        }
    }

    public void ExecuteQueuedShot()
    {
        if (_localQueuedForce.sqrMagnitude > 0.01f)
        {
            if (_rb != null)
            {
                _rb.AddForce(_localQueuedForce, ForceMode2D.Impulse);
                AudioManager.Instance?.PlayShoot(transform.position);
            }
            _localQueuedForce = Vector2.zero;
        }

        HideAimVisual();
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
        return (_mainCamera != null && _mainCamera.orthographic) ? _mainCamera.orthographicSize * 2f : fallbackViewHeight;
    }

    private void ConfigureArrowVisual()
    {
        GameObject shaftObj = new GameObject("AimArrowShaft");
        shaftObj.transform.SetParent(transform, false);
        _shaftTransform = shaftObj.transform;
        _shaftRenderer = shaftObj.AddComponent<SpriteRenderer>();
        _shaftRenderer.sprite = arrowShaftSprite != null ? arrowShaftSprite : GetOrCreateShaftSprite();
        _shaftRenderer.sortingOrder = arrowSortingOrder;
        _shaftRenderer.sortingLayerName = arrowSortingLayerName;
        _shaftRenderer.enabled = false;

        GameObject headObj = new GameObject("AimArrowHead");
        headObj.transform.SetParent(transform, false);
        _headTransform = headObj.transform;
        _headRenderer = headObj.AddComponent<SpriteRenderer>();
        _headRenderer.sprite = arrowHeadSprite != null ? arrowHeadSprite : GetOrCreateHeadSprite();
        _headRenderer.sortingOrder = arrowSortingOrder + 1;
        _headRenderer.sortingLayerName = arrowSortingLayerName;
        _headRenderer.enabled = false;
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

        // Calcul de la longueur (Unité fixe vs Fraction de la vue)
        float length = useAbsoluteMaxArrowLength
            ? forceRatio * maxArrowLength
            : forceRatio * maxArrowLengthFraction * viewHeight;

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

    private void HideAimVisual()
    {
        if (_shaftRenderer != null) _shaftRenderer.enabled = false;
        if (_headRenderer != null) _headRenderer.enabled = false;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDead) return;

        if (_rb.velocity.sqrMagnitude > bounceForceThreshold * bounceForceThreshold)
        {
            Vector2 contactPoint = collision.GetContact(0).point;
            AudioManager.Instance?.PlayBounce(contactPoint);
            StartCoroutine(SquashAnimationCoroutine());
        }
    }

    private IEnumerator SquashAnimationCoroutine()
    {
        float elapsedTime = 0f;

        while (elapsedTime < squashDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / squashDuration;

            float squash = Mathf.Lerp(1f, squashAmount, Mathf.Sin(t * Mathf.PI));

            float scaleX = _originalScale.x * stretchAmount * (1f - (1f - squash) / 5f);
            float scaleY = _originalScale.y * squash;

            transform.localScale = new Vector3(scaleX, scaleY, _originalScale.z);

            yield return null;
        }

        transform.localScale = _originalScale;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Goal") && !IsDead)
        {
            IsAiming = false;
            HideAimVisual();
            StartCoroutine(FallAnimationCoroutine());
        }
    }

    private IEnumerator FallAnimationCoroutine()
    {
        IsDead = true;
        _rb.isKinematic = true;
        _rb.velocity = Vector2.zero;

        AudioManager.Instance?.PlayBallDeath(transform.position);

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

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
    }
}