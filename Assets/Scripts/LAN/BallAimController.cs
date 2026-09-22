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
    [SerializeField] private float flashDuration = 0.08f;

    [SerializeField] private float maxTurnDuration = 6.0f; // Durée max d'un tir en secondes
    [SerializeField] private float stationaryVelocityThreshold = 2f;


    public static readonly List<BallAimController> AllBalls = new List<BallAimController>();

    // ✅ Accesseurs mis en cache : évitent des GetComponent<NetworkObject>() répétés
    // depuis l'extérieur (TurnManager itère souvent sur toutes les billes).
    public NetworkObject NetObj => _networkObject;
    public PlayerRef Owner => _networkObject.StateAuthority;

    [Networked] public int OwnerPlayerId { get; set; }
    [Networked] public bool IsAiming { get; set; }
    [Networked] public bool IsDead { get; set; }
    [Networked] public bool IsMoving { get; set; }

    private bool _isBotControlled = false;
    private bool _botHasQueuedThisTurn = false;
    private float _botReactionTimer = 0f;

    // ✅ REFACTOR : unification avec LocalBallAimController. Le bot réseau utilisait avant
    // sa propre logique simplifiée (FindTargetGoal + viser tout droit), qui ne vérifiait pas
    // de quel côté du ballon se trouvait le bot et pouvait donc pousser le ballon dans SON
    // PROPRE but. BotAIStrategy (partagé avec le mode Local) évite ce cas en se replaçant
    // au lieu de tirer quand il est du mauvais côté.
    [Header("IA (bot) - Simplifié")]
    [SerializeField] private BotAIStrategy.AIDifficulty aiDifficulty = BotAIStrategy.AIDifficulty.Medium;
    [SerializeField] private float botReactionDelaySeconds = 0.3f;
    private BotAIStrategy _botAI;
    private GoalZone _enemyGoal;
    private Transform _soccerBallTransform;

    private Rigidbody2D _rb;
    private Camera _mainCamera;
    private NetworkObject _networkObject;
    private SpriteRenderer _spriteRenderer;

    // ✅ REFACTOR : flèche de visée gérée par la classe partagée AimArrowVisual (voir AimArrowVisual.cs)
    private AimArrowVisual _arrow;

    private Vector2 _startDragPos;

    // ✅ Stockage local de la force sur le client propriétaire de la bille
    private Vector2 _localQueuedForce = Vector2.zero;
    private Vector3 originalScale;

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();
        _networkObject = GetComponent<NetworkObject>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale;
        _arrow = new AimArrowVisual(transform, arrowShaftSprite, arrowHeadSprite, arrowSortingOrder, arrowSortingLayerName);
        _botAI = new BotAIStrategy(aiDifficulty, OwnerPlayerId);

        _mainCamera = Camera.main;
        if (_mainCamera == null) _mainCamera = FindObjectOfType<Camera>();

        if (!AllBalls.Contains(this)) AllBalls.Add(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        AllBalls.Remove(this);
    }

    public void SetOwner(int playerId)
    {
        OwnerPlayerId = playerId;
        _botAI?.SetOwnerPlayerId(playerId);
    }

    /// <summary>
    /// ✨ NEW : Active/désactive le pilotage par IA de cette bille. Une bille "bot"
    /// ignore la souris et vise automatiquement le but adverse pendant la phase Aiming.
    /// </summary>
    public void SetBotControlled(bool isBot)
    {
        _isBotControlled = isBot;
        _botHasQueuedThisTurn = false;
        _enemyGoal = null; // recalculé au prochain tour, une fois OwnerPlayerId défini
    }

    public void ForceStopAiming()
    {
        IsAiming = false;
        _arrow.Hide();
    }

    private void Update()
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

        // 1. Bloquer la visée si n'importe quelle bille est encore en mouvement
        if (TurnManager.Instance != null && TurnManager.Instance.IsAnyBallMoving())
        {
            return;
        }

        // 2. Bloquer la visée si le jeu n'est pas en phase de visée (Aiming)
        if (TurnManager.Instance != null && TurnManager.Instance.CurrentState != TurnState.Aiming)
        {
            return;
        }

        Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(mouseWorld, Vector2.zero);

        if (hit.collider != null && hit.collider.gameObject == gameObject)
        {
            IsAiming = true;
            _startDragPos = mouseWorld;
        }
    }

    // =========================================================================
    // ✨ NEW : Logique IA — niveau "intermédiaire" (vise le but adverse avec imprécision)
    // =========================================================================

    // ✅ REFACTOR : délègue désormais à BotAIStrategy (partagée avec LocalBallAimController)
    // au lieu de la logique simplifiée "viser tout droit vers le but adverse", qui pouvait
    // pousser le ballon dans le mauvais but si le bot n'était pas du bon côté du ballon.
    private void UpdateBotAiming()
    {
        if (TurnManager.Instance == null) return;

        if (TurnManager.Instance.CurrentState != TurnState.Aiming)
        {
            _botHasQueuedThisTurn = false;
            _botReactionTimer = 0f;
            return;
        }

        if (_botHasQueuedThisTurn) return;
        if (TurnManager.Instance.IsAnyBallMoving()) return;

        if (_soccerBallTransform == null) FindSoccerBall();
        if (_soccerBallTransform == null)
        {
            _botHasQueuedThisTurn = true;
            return;
        }

        if (_enemyGoal == null) _enemyGoal = _botAI.FindEnemyGoal();
        if (_enemyGoal == null)
        {
            Debug.LogWarning("[BallAimController] 🤖 Aucun but adverse trouvé, l'IA ne tire pas ce tour-ci");
            _botHasQueuedThisTurn = true;
            return;
        }

        // Délai de réaction pour un bot moins instantané
        _botReactionTimer += Time.deltaTime;
        if (_botReactionTimer < botReactionDelaySeconds) return;

        var (direction, forceFraction) = _botAI.CalculateBotShot(
            botPosition: transform.position,
            ballPosition: _soccerBallTransform.position,
            enemyGoalPosition: _enemyGoal.transform.position);

        float force = Mathf.Lerp(maxForce * 0.3f, maxForce, forceFraction);
        _localQueuedForce = direction * force;
        _botHasQueuedThisTurn = true;
    }

    private void FindSoccerBall()
    {
        GameObject ballGO = GameObject.FindWithTag("SoccerBall");
        if (ballGO != null)
        {
            _soccerBallTransform = ballGO.transform;
            return;
        }

        foreach (var t in FindObjectsOfType<Transform>())
        {
            if (t.name.Contains("Ball") || t.name.Contains("Soccer"))
            {
                _soccerBallTransform = t;
                return;
            }
        }

        Debug.LogWarning("[BallAimController] ⚠️ Ballon non trouvé! Tag le ballon avec 'SoccerBall'.");
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
            // ✅ Enregistrement de la force localement
            _localQueuedForce = forceToApply;
        }
        else
        {
            _localQueuedForce = Vector2.zero;
            _arrow.Hide();
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

        _arrow.Hide();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyImpulse(Vector2 force)
    {
        if (_rb != null)
        {
            _rb.AddForce(force, ForceMode2D.Impulse);
            // Debug.Log($"[BallAimController] 💥 Impulsion appliquée : {force}");
        }
    }

    // ✅ REFACTOR : délégation aux classes partagées AimForceUtility / AimArrowVisual.
    // Note : la version réseau ignorait auparavant useAbsoluteMaxArrowLength (toujours en
    // mode "fraction de la vue"), contrairement au mode Local. Ce paramètre existe déjà
    // dans l'Inspector de ce prefab (useAbsoluteMaxArrowLength, maxArrowLength) — le passer
    // à AimArrowVisual.Show unifie le comportement visuel entre les deux modes.
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
            // Debug.Log($"[BallAimController] 💥 Rebond! Velocity: {_rb.velocity.magnitude}");

            // ✅ NOUVEAU : son de rebond. Pas de RPC ici (contrairement au tir) : comme pour
            // l'effet squash juste en dessous, OnCollisionEnter2D se déclenche localement sur
            // chaque client (colliders non-trigger, détectés indépendamment de la State
            // Authority), donc pas besoin de diffusion réseau.
            Vector2 contactPoint = collision.GetContact(0).point;
            AudioManager.Instance?.PlayBounce(contactPoint);

            // Lance tous les effets en parallèle
            StartCoroutine(BallImpactEffects.Squash(transform, originalScale, squashDuration, squashAmount, stretchAmount));
            // StartCoroutine(FlashCoroutine());
        }
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
            _arrow.Hide();

            RPC_PlayFallAnimation();
        }
    }

    // ✅ NOUVEAU : déclenché avec RpcTargets.All, donc IsDead=true et l'animation sont
    // exécutés sur tous les clients sans RPC supplémentaire.
    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_PlayFallAnimation()
    {
        IsDead = true;
        StartCoroutine(BallImpactEffects.Fall(
            transform, _spriteRenderer, _rb, GetComponent<Collider2D>(),
            fallDuration, shrinkStartTime, fallCurve, rotateCurve, totalRotation,
            onFallStarted: () => AudioManager.Instance?.PlayBallDeath(transform.position)));
    }
}