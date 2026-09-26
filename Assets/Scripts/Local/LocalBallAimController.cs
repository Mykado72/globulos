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

    [SerializeField] private float stationaryVelocityThreshold = 2f;


    [Header("IA (bot) - Simplifié")]
    [SerializeField] private BotAIStrategy.AIDifficulty aiDifficulty = BotAIStrategy.AIDifficulty.Medium;
    [SerializeField] private BotAIStrategy.BotRole aiRole = BotAIStrategy.BotRole.Defensive;
    [SerializeField] private float botReactionDelaySeconds = 0.3f; // Délai avant tir (plus naturel)

    private BotAIStrategy _botAI;
    private GoalZone _enemyGoal; // Cache du but adverse
    private Transform _soccerBallTransform; // Cache du ballon
    private float _botReactionTimer = 0f;
    public static readonly List<LocalBallAimController> AllBalls = new List<LocalBallAimController>();

    public int OwnerPlayerId { get; private set; }
    public bool IsAiming { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsMoving { get; private set; }

    private bool _isBotControlled = false;
    private bool _botHasQueuedThisTurn = false;

    private Rigidbody2D _rb;
    private Camera _mainCamera;
    private SpriteRenderer _spriteRenderer;

    // ✅ REFACTOR : flèche de visée gérée par la classe partagée AimArrowVisual
    // (voir AimArrowVisual.cs), remplace ConfigureArrowVisual/GetOrCreate*Sprite/UpdateAimVisual
    // qui étaient dupliqués à l'identique dans BallAimController (réseau).
    private AimArrowVisual _arrow;

    private Vector2 _startDragPos;
    private Vector2 _localQueuedForce = Vector2.zero;
    private Vector3 _originalScale;
    private Color _originalColor; // ✅ FIX : capturée pour pouvoir restaurer après l'animation de mort

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _originalScale = transform.localScale;
        if (_spriteRenderer != null) _originalColor = _spriteRenderer.color;
        _arrow = new AimArrowVisual(transform, arrowShaftSprite, arrowHeadSprite, arrowSortingOrder, arrowSortingLayerName);
        // 🤖 Initialiser l'IA du bot
        _botAI = new BotAIStrategy(aiDifficulty);
        _botAI.SetRole(aiRole);
    }

    // ======================== ✅ FIX : RESET APRÈS UN BUT ========================
    /// <summary>
    /// Replace la bille à sa position de spawn et annule tout effet de "mort"
    /// (IsDead, animation de chute, collider désactivé, etc.). Appelée par
    /// LocalGameSpawner.ResetAllToSpawnPoints() quand un but au foot est marqué
    /// et que la partie continue (avant, RIEN ne repositionnait les billes en
    /// mode Local, contrairement au mode réseau qui rechargeait toute la scène).
    /// </summary>
    public void ResetForNewRound(Vector3 position)
    {
        StopAllCoroutines(); // stoppe une éventuelle animation de chute (BallImpactEffects.Fall) en cours

        IsDead = false;
        IsAiming = false;
        IsMoving = false;
        _localQueuedForce = Vector2.zero;
        _botHasQueuedThisTurn = false;
        _botReactionTimer = 0f;

        transform.position = position;
        transform.rotation = Quaternion.identity;
        transform.localScale = _originalScale;

        if (_rb != null)
        {
            _rb.velocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.angularVelocity = 0f;            
            _rb.simulated = true;
        }

        if (_spriteRenderer != null)
        {
            _spriteRenderer.color = _originalColor;
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        _arrow.Hide();

        if (!AllBalls.Contains(this)) AllBalls.Add(this);
    }

    // ======================== SETTER POUR DIFFICULTÉ ========================
    public void SetBotDifficulty(BotAIStrategy.AIDifficulty difficulty)
    {
        aiDifficulty = difficulty;
        if (_botAI != null)
        {
            _botAI.SetDifficulty(difficulty, OwnerPlayerId);
        }
    }

    public void SetBotRole(BotAIStrategy.BotRole role)
    {
        aiRole = role;
        if (_botAI != null)
        {
            _botAI.SetRole(aiRole);
        }
    }


    // ======================== UPDATE BOT AIMING - VERSION SIMPLIFIÉE ========================
    private void UpdateBotAiming()
    {
        if (LocalTurnManager.Instance == null) return;

        // Vérifier qu'on est en phase de visée
        if (LocalTurnManager.Instance.CurrentState != TurnState.Aiming)
        {
            _botHasQueuedThisTurn = false;
            _botReactionTimer = 0f;
            return;
        }

        // Le bot a déjà tiré ce tour
        if (_botHasQueuedThisTurn) return;

        // Trouver le ballon s'il n'est pas en cache
        if (_soccerBallTransform == null)
        {
            FindSoccerBall();
        }

        if (_soccerBallTransform == null)
        {
            _botHasQueuedThisTurn = true;
            return;
        }

        if (_enemyGoal == null)
        {
            _enemyGoal = _botAI.FindEnemyGoal();
            // Debug.Log("🤖 Bot " + OwnerPlayerId + " IA PlayerId=" + _botAI._ownerPlayerId);
            if (_enemyGoal != null)
                Debug.Log("✅ But adverse trouvé: " + _enemyGoal.name);
            else
                Debug.Log("❌ But adverse NULL!");
        }

        // Ajouter un délai de "réaction" pour rendre le bot moins instantané
        _botReactionTimer += Time.deltaTime;
        if (_botReactionTimer < botReactionDelaySeconds)
        {
            return;
        }

        // Decision de l'IA : direction et force
        var (direction, forceFraction) = _botAI.CalculateBotDecision(
            botPosition: transform.position,
            ballPosition: _soccerBallTransform.position,
            enemyGoal: _enemyGoal,
            ownGoal: _botAI.FindOwnGoal()
        );

        // Appliquer la force
        float force = Mathf.Lerp(maxForce * 0.3f, maxForce, forceFraction);
        _localQueuedForce = direction * force;
        _botHasQueuedThisTurn = true;
    }

    // ======================== TROUVER LE BALLON ========================
    private void FindSoccerBall()
    {
        // Chercher par tag (recommandé)
        GameObject ballGO = GameObject.FindWithTag("SoccerBall");
        if (ballGO != null)
        {
            _soccerBallTransform = ballGO.transform;
            // Debug.Log($"✅ Ballon trouvé: {ballGO.name}");
            return;
        }

        // Fallback: chercher par nom
        var allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (var t in allTransforms)
        {
            if (t.name.Contains("Ball") || t.name.Contains("Soccer"))
            {
                _soccerBallTransform = t;
                return;
            }
        }

        Debug.LogWarning("⚠️ Ballon non trouvé! Tag le ballon avec 'Ball' ou mets 'Ball' dans son nom.");
    }

    // ======================== RESET (à appeler avant chaque tour du bot) ========================
    public void ResetBotState()
    {
        _botHasQueuedThisTurn = false;
        _botReactionTimer = 0f;
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        if (_mainCamera == null) _mainCamera = FindAnyObjectByType<Camera>();
    }

    private void OnEnable()
    {
        if (!AllBalls.Contains(this)) AllBalls.Add(this);
    }

    private void OnDisable()
    {
        AllBalls.Remove(this);
    }

    public void SetOwner(int playerId)
    {
        OwnerPlayerId = playerId;

        if (_botAI != null)
        {
            _botAI.SetOwnerPlayerId(playerId);
        }
    }

    public void SetBotControlled(bool isBot)
    {
        _isBotControlled = isBot;
        _botHasQueuedThisTurn = false;
    }

    public void ForceStopAiming()
    {
        IsAiming = false;
        _arrow.Hide();
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
            UpdateAimVisualDisplay(_localQueuedForce);
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

        // ... checks existants ...

        // ✅ Ne passe pas par ScreenToWorldPoint, utilise le raycast directement
        Vector2 mousePos = Input.mousePosition;
        RaycastHit2D hit = Physics2D.Raycast(_mainCamera.ScreenToWorldPoint(mousePos), Vector3.back, 100f);

        if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
        {
            IsAiming = true;
            _startDragPos = (Vector2)_mainCamera.ScreenToWorldPoint(mousePos);
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

    private void ContinueAiming()
    {
        if (_mainCamera == null) return;
        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 forceToApply = ComputeClampedForce(currentMousePos);
        UpdateAimVisualDisplay(forceToApply);
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
            _arrow.Hide();
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

        _arrow.Hide();
    }

    // ✅ REFACTOR : les méthodes ci-dessous délèguent maintenant à AimForceUtility et
    // AimArrowVisual (classes partagées avec BallAimController, voir ces fichiers).

    private Vector2 ComputeClampedForce(Vector2 currentMousePos)
    {
        return AimForceUtility.ComputeClampedForce(_startDragPos, currentMousePos, maxDragDistanceFraction, GetViewHeight(), maxForce);
    }

    private float GetViewHeight()
    {
        return AimForceUtility.GetViewHeight(_mainCamera, fallbackViewHeight);
    }

    private void UpdateAimVisualDisplay(Vector2 clampedForce)
    {
        _arrow.Show(clampedForce, maxForce, GetViewHeight(), transform, aimColor, activeColor,
            thicknessFraction, headSizeFraction, useAbsoluteMaxArrowLength, maxArrowLength, maxArrowLengthFraction);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDead) return;

        if (_rb.velocity.sqrMagnitude > bounceForceThreshold * bounceForceThreshold)
        {
            Vector2 contactPoint = collision.GetContact(0).point;
            AudioManager.Instance?.PlayBounce(contactPoint);
            StartCoroutine(BallImpactEffects.Squash(transform, _originalScale, squashDuration, squashAmount, stretchAmount));
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Goal") && !IsDead)
        {
            IsAiming = false;
            _arrow.Hide();
            IsDead = true;
            StartCoroutine(BallImpactEffects.Fall(
                transform, _spriteRenderer, _rb, GetComponent<Collider2D>(),
                fallDuration, shrinkStartTime, fallCurve, rotateCurve, totalRotation,
                onFallStarted: () => AudioManager.Instance?.PlayBallDeath(transform.position)));
        }
    }
}