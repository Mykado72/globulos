using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class BallAimController : NetworkBehaviour
{
    [Header("Aim Settings")]
    [SerializeField] private float maxForce = 15f;
    [SerializeField, Range(0.25f, 1.0f)] private float maxDragDistanceFraction = 0.5f;

    [Header("Arrow Visual Settings")]
    [SerializeField] private Sprite arrowShaftSprite;
    [SerializeField] private Sprite arrowHeadSprite;
    [SerializeField] private Color aimColor = new Color(1, 0.5f, 0, 1);
    [SerializeField] private Color activeColor = new Color(1, 0, 0, 1);
    [SerializeField] private int arrowSortingOrder = 20;
    [SerializeField] private string arrowSortingLayerName = "Entities";

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
    [SerializeField] private float flashDuration = 0.08f;

    public static readonly List<BallAimController> AllBalls = new List<BallAimController>();

    // ✅ Accesseurs mis en cache : évitent des GetComponent<NetworkObject>() répétés
    // depuis l'extérieur (TurnManager itère souvent sur toutes les billes).
    public NetworkObject NetObj => _networkObject;
    public PlayerRef Owner => _networkObject.StateAuthority;

    [Networked] public int OwnerPlayerId { get; set; }
    [Networked] public bool IsAiming { get; set; }
    [Networked] public bool IsDead { get; set; }
    [Networked] public bool IsMoving { get; set; }

    [SerializeField] private float stationaryVelocityThreshold = 0.15f;

    [Header("IA (bot)")]
    [Tooltip("Décalage angulaire max (en degrés) ajouté à la visée de l'IA pour simuler l'imprécision.")]
    [SerializeField, Range(0f, 45f)] private float aiAimInaccuracyDegrees = 12f;
    [Tooltip("Fraction min de la force max utilisée par l'IA (l'autre borne étant 1 = force max).")]
    [SerializeField, Range(0.5f, 1f)] private float aiMinForceFraction = 0.7f;

    private bool _isBotControlled = false;
    private bool _botHasQueuedThisTurn = false;
    private GoalZone _aiTargetGoal;

    private Rigidbody2D _rb;
    private Camera _mainCamera;
    private NetworkObject _networkObject;
    private SpriteRenderer _spriteRenderer;
    private Transform _shaftTransform;
    private SpriteRenderer _shaftRenderer;
    private Transform _headTransform;
    private SpriteRenderer _headRenderer;

    private Vector2 _startDragPos;

    // ✅ Stockage local de la force sur le client propriétaire de la bille
    private Vector2 _localQueuedForce = Vector2.zero;
    private Vector3 originalScale;

    private static Sprite _cachedShaftSprite;
    private static Sprite _cachedHeadSprite;

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();
        _networkObject = GetComponent<NetworkObject>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale;
        ConfigureArrowVisual();

        _mainCamera = Camera.main;
        if (_mainCamera == null) _mainCamera = FindObjectOfType<Camera>();

        if (!AllBalls.Contains(this)) AllBalls.Add(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        AllBalls.Remove(this);
    }

    public void SetOwner(int playerId) => OwnerPlayerId = playerId;

    /// <summary>
    /// ✨ NEW : Active/désactive le pilotage par IA de cette bille. Une bille "bot"
    /// ignore la souris et vise automatiquement le but adverse pendant la phase Aiming.
    /// </summary>
    public void SetBotControlled(bool isBot)
    {
        _isBotControlled = isBot;
        _botHasQueuedThisTurn = false;
        _aiTargetGoal = null; // recalculé au prochain tour, une fois OwnerPlayerId défini
    }

    public void ForceStopAiming()
    {
        IsAiming = false;
        HideAimVisual();
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

    private void Update()
    {
        // 🔒 Sécurité : Seul le propriétaire de la bille voit et contrôle sa propre flèche
        if (!HasStateAuthority || IsDead) return;

        // ✨ NEW : une bille pilotée par l'IA ne lit pas la souris, elle décide seule
        if (_isBotControlled)
        {
            UpdateBotAiming();
            return;
        }

        // Si une force est déjà enregistrée en attente, on maintient la flèche affichée localement
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

        // 1. Bloquer la visée si n'importe quelle bille est encore en mouvement
        if (TurnManager.Instance != null && TurnManager.Instance.IsAnyBallMoving())
        {
            return;
        }

        // 2. Bloquer la visée si le jeu n'est pas en phase de visée (Aiming)
        if (TurnManager.Instance != null && TurnManager.Instance.CurrentState != TurnManager.TurnState.Aiming)
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

    // =========================================================================
    // ✨ NEW : Logique IA — niveau "intermédiaire" (vise le but adverse avec imprécision)
    // =========================================================================

    private void UpdateBotAiming()
    {
        if (TurnManager.Instance == null) return;

        // On ne décide qu'une seule fois par phase de visée
        if (TurnManager.Instance.CurrentState != TurnManager.TurnState.Aiming)
        {
            _botHasQueuedThisTurn = false;
            return;
        }

        if (_botHasQueuedThisTurn) return;
        if (TurnManager.Instance.IsAnyBallMoving()) return;

        if (_aiTargetGoal == null) FindTargetGoal();

        if (_aiTargetGoal == null)
        {
            Debug.LogWarning("[BallAimController] 🤖 Aucun but adverse trouvé, l'IA ne tire pas ce tour-ci");
            _botHasQueuedThisTurn = true;
            return;
        }

        Vector2 toGoal = (Vector2)_aiTargetGoal.transform.position - (Vector2)transform.position;
        if (toGoal.sqrMagnitude < 0.0001f)
        {
            _botHasQueuedThisTurn = true;
            return;
        }

        // ✅ Imprécision : décale légèrement l'angle de tir par rapport à la direction du but
        float randomAngleOffset = Random.Range(-aiAimInaccuracyDegrees, aiAimInaccuracyDegrees);
        Vector2 aimDirection = Quaternion.Euler(0f, 0f, randomAngleOffset) * toGoal.normalized;

        // ✅ Force : proche du max, avec une petite variation pour paraître naturel
        float forceMagnitude = maxForce * Random.Range(aiMinForceFraction, 1f);

        _localQueuedForce = aimDirection * forceMagnitude;
        _botHasQueuedThisTurn = true;

        // Debug.Log($"[BallAimController] 🤖 IA (Joueur {OwnerPlayerId}) vise le but adverse — force={_localQueuedForce}");
    }

    private void FindTargetGoal()
    {
        GoalZone.GoalTeam ownTeam = (OwnerPlayerId % 2 == 0) ? GoalZone.GoalTeam.Jaune : GoalZone.GoalTeam.Rouge;

        // Le but à viser est celui qui défend l'équipe adverse
        // (si NOTRE bille y pousse le ballon, c'est NOTRE équipe qui marque).
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
            // ✅ Enregistrement de la force localement
            _localQueuedForce = forceToApply;
            Debug.Log($"[BallAimController] 🎯 Force enregistrée pour la bille {OwnerPlayerId} : {_localQueuedForce}");
        }
        else
        {
            _localQueuedForce = Vector2.zero;
            HideAimVisual();
        }
    }

    // ✅ Appelé par le TurnManager au début de la phase Resolution
    public void ExecuteQueuedShot()
    {
        if (HasStateAuthority && _localQueuedForce.sqrMagnitude > 0.01f)
        {
            // Transmission et application de la force au Rigidbody
            RPC_ApplyImpulse(_localQueuedForce);
            _localQueuedForce = Vector2.zero;
        }

        HideAimVisual();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyImpulse(Vector2 force)
    {
        if (_rb != null)
        {
            _rb.AddForce(force, ForceMode2D.Impulse);
            Debug.Log($"[BallAimController] 💥 Impulsion appliquée : {force}");
        }
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

    private void HideAimVisual()
    {
        if (_shaftRenderer != null) _shaftRenderer.enabled = false;
        if (_headRenderer != null) _headRenderer.enabled = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (_rb != null)
        {
            float thresholdSqr = stationaryVelocityThreshold * stationaryVelocityThreshold;
            IsMoving = !IsDead && _rb.velocity.sqrMagnitude > thresholdSqr;
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayShootSound()
    {
        AudioManager.Instance?.PlayShoot(transform.position);
    }

    // ✅ NOUVEAU : Gestion des collisions pour les effets de rebond
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDead) return;

        // Vérifie si le rebond est assez violent
        if (_rb.velocity.sqrMagnitude > bounceForceThreshold * bounceForceThreshold)
        {
            Debug.Log($"[BallAimController] 💥 Rebond! Velocity: {_rb.velocity.magnitude}");

            // ✅ NOUVEAU : son de rebond. Pas de RPC ici (contrairement au tir) : comme pour
            // l'effet squash juste en dessous, OnCollisionEnter2D se déclenche localement sur
            // chaque client (colliders non-trigger, détectés indépendamment de la State
            // Authority), donc pas besoin de diffusion réseau.
            Vector2 contactPoint = collision.GetContact(0).point;
            AudioManager.Instance?.PlayBounce(contactPoint);

            // Lance tous les effets en parallèle
            StartCoroutine(SquashAnimationCoroutine());
            // StartCoroutine(FlashCoroutine());
        }
    }

    // ✅ Animation d'écrasement (Squash)
    private IEnumerator SquashAnimationCoroutine()
    {
        float elapsedTime = 0f;

        while (elapsedTime < squashDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / squashDuration;

            // Courbe sinusoïdale pour un mouvement naturel
            float squash = Mathf.Lerp(1f, squashAmount, Mathf.Sin(t * Mathf.PI));

            // Écrase Y, étire X pour conserver le volume
            float scaleX = originalScale.x * stretchAmount * (1f - (1f - squash) / 5f);
            float scaleY = originalScale.y * squash;

            transform.localScale = new Vector3(scaleX, scaleY, originalScale.z);

            yield return null;
        }

        // Retour à l'état normal
        transform.localScale = originalScale;
    }

    // ✅ Flash blanc au rebond
    private IEnumerator FlashCoroutine()
    {
        Color originalColor = _spriteRenderer.color;
        _spriteRenderer.color = Color.white;

        yield return new WaitForSeconds(flashDuration);

        _spriteRenderer.color = originalColor;
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

        // ✅ NOUVEAU : déclenché depuis RPC_PlayFallAnimation (RpcTargets.All), donc déjà
        // diffusé à tous les clients sans RPC supplémentaire.
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

        Collider2D collider = GetComponent<Collider2D>();
        if (collider != null)
            collider.enabled = false;
    }

}