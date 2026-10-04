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

    [SerializeField] private float stationaryVelocityThreshold = 2f;


    public static readonly List<BallAimController> AllBalls = new List<BallAimController>();

    // ✅ Accesseurs mis en cache : évitent des GetComponent<NetworkObject>() répétés
    // depuis l'extérieur (TurnManager itère souvent sur toutes les billes).
    public NetworkObject NetObj => _networkObject;
    public PlayerRef Owner => _networkObject.StateAuthority;

    [Networked] public int OwnerPlayerId { get; set; }

    // ✨ Pseudo du propriétaire, répliqué avec la bille (lu par PlayerNameHelper / Render)
    [Networked, Capacity(32)] public string OwnerNickname { get; set; }
    private string _lastPushedOwnerNickname;
    [Networked] public bool IsDead { get; set; }
    [Networked] public bool IsMoving { get; set; }

    // ✅ FIX (centralisation physique) : IsAiming n'est lu/écrit que par le propriétaire de
    // la bille (Input Authority) et n'est jamais consulté depuis un autre script/instance :
    // ça n'a donc jamais eu besoin d'être répliqué. Le garder [Networked] serait même
    // dangereux maintenant que la State Authority passe au Master : une écriture faite par
    // un pair qui n'a pas la State Authority se ferait écraser par la resynchronisation
    // réseau (qui, elle, ne change jamais cette valeur côté Master).
    private bool IsAiming;

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
    [SerializeField] private BotAIStrategy.BotRole aiRole = BotAIStrategy.BotRole.Defensive;
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

    // ✅ FIX (tirs simultanés) : force connue du Master (State Authority). Elle est envoyée par
    // l'Input Authority dès qu'elle relâche la souris (RPC_SubmitShot), pendant la phase Aiming,
    // pour que le Master applique TOUTES les impulsions dans la même frame à la fin du timer.
    private Vector2 _masterQueuedForce = Vector2.zero;
    private Vector3 originalScale;

    // ✅ FIX : position/couleur d'origine mémorisées pour pouvoir remettre la bille en jeu
    // après un but qui ne termine pas la partie (voir ResetForNewRound).
    private Vector3 _spawnPosition;
    private Color _initialColor;

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();
        _networkObject = GetComponent<NetworkObject>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale;
        _spawnPosition = transform.position;
        _initialColor = _spriteRenderer != null ? _spriteRenderer.color : Color.white;
        _arrow = new AimArrowVisual(transform, arrowShaftSprite, arrowHeadSprite, arrowSortingOrder, arrowSortingLayerName);
        // Convention LAN : celui qui crée la room est toujours Jaune (voir SoccerBallController.
        // FindScoringPlayer, qui attribue Jaune au PlayerId pair) — distincte de la convention Local.
        _botAI = new BotAIStrategy(aiDifficulty, OwnerPlayerId, BotAIStrategy.BotRole.Offensive, BotAIStrategy.PlayerIdConvention.EvenIsJaune);
        _botAI.SetRole(aiRole);

        _mainCamera = Camera.main;
        if (_mainCamera == null) _mainCamera = FindAnyObjectByType<Camera>();

        if (!AllBalls.Contains(this)) AllBalls.Add(this);

        // ✅ FIX (centralisation physique) : en Shared Mode, l'Input Authority n'est
        // correctement attribuée que si CHAQUE joueur spawn sa propre bille (voir
        // GameSpawner) — un pair ne peut pas spawner "pour" un autre joueur en lui donnant
        // son Input Authority de façon fiable. La bille appartient donc encore, à l'instant
        // du spawn, à son propriétaire (Input ET State Authority).
        // C'est pourquoi le Master reprend ici la State Authority de TOUTE bille qui ne lui
        // appartient pas déjà, via RequestStateAuthority() : lui seul doit ensuite simuler
        // la physique, pour éviter que deux pairs résolvent différemment la même collision.
        // ⚠️ Nécessite "Allow State Authority Override" coché dans les Shared Mode Settings
        // du NetworkObject, sur les DEUX prefabs de bille (réglage à faire dans l'éditeur
        // Unity, sur le prefab — impossible à changer après le spawn).
        if (Runner != null && Runner.IsSharedModeMasterClient && !HasStateAuthority)
        {
            Object.RequestStateAuthority();
        }

        // ✅ FIX (centralisation physique) : RPC_SetPlayerInfo exige l'Input Authority pour
        // être appelée (voir PlayerData). C'est donc chaque propriétaire réel qui envoie
        // lui-même son pseudo, dès qu'il reçoit sa bille — reste valide même une fois la
        // State Authority reprise par le Master juste au-dessus.
        // ✨ FIX v2 : le pseudo voyage maintenant AVEC LA BILLE (OwnerNickname, [Networked]) :
        // plus besoin que le prefab de bille porte un PlayerData ni que PlayerDataSpawner ait tourné.
        if (HasInputAuthority)
        {
            string nickname = ResolveLocalNickname();
            int ownerId = Object.InputAuthority.PlayerId;

            // Le joueur local connaît immédiatement son propre pseudo
            PlayerNamesManager.Instance?.SetPlayerName(ownerId, nickname);

            // Publier le pseudo sur la bille (on est encore State Authority à cet instant)
            if (HasStateAuthority) OwnerNickname = nickname;

            // Compatibilité : si la bille porte aussi un PlayerData, on l'alimente également
            if (TryGetComponent(out PlayerData playerData))
            {
                playerData.RPC_SetPlayerInfo(nickname, ownerId);
            }
        }
    }

    /// <summary>
    /// Pseudo du joueur local : PlayerNamesManager (si VRAI pseudo) → PlayerPrefs (écrit par le Lobby
    /// avant StartGame) → token de connexion → "Joueur_X".
    /// </summary>
    private string ResolveLocalNickname()
    {
        int id = Object.InputAuthority.PlayerId;

        // 1️⃣ Pseudo en mémoire de CETTE instance (posé par LobbyManager)
        if (!string.IsNullOrWhiteSpace(PlayerNameHelper.LocalNickname))
            return PlayerNameHelper.LocalNickname;

        // 2️⃣ Pseudo déjà connu localement pour ce joueur
        if (PlayerNamesManager.Instance != null &&
            PlayerNamesManager.Instance.TryGetPlayerName(id, out string known) &&
            !PlayerNameHelper.IsPlaceholder(id, known))
            return known;

        // 3️⃣ Token de connexion (le Lobby y met le pseudo)
        if (Runner != null)
        {
            byte[] token = Runner.GetPlayerConnectionToken(Runner.LocalPlayer);
            if (token != null && token.Length > 0)
            {
                string fromToken = System.Text.Encoding.UTF8.GetString(token);
                if (!string.IsNullOrWhiteSpace(fromToken)) return fromToken;
            }
        }

        // ℹ️ PlayerPrefs n'est volontairement PAS utilisé ici : il ne sert qu'à pré-remplir le
        // champ du Lobby (et peut être partagé entre deux instances lancées sur la même machine).
        return $"Joueur_{id}";
    }

    /// <summary>Chaque client recopie le pseudo répliqué de la bille dans son PlayerNamesManager local.</summary>
    public override void Render()
    {
        if (_lastPushedOwnerNickname == OwnerNickname) return;
        _lastPushedOwnerNickname = OwnerNickname;

        if (string.IsNullOrWhiteSpace(OwnerNickname) || PlayerNamesManager.Instance == null) return;
        int id = OwnerPlayerId != 0 ? OwnerPlayerId : (Object != null ? Object.InputAuthority.PlayerId : 0);
        if (id != 0) PlayerNamesManager.Instance.SetPlayerName(id, OwnerNickname);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        AllBalls.Remove(this);
    }

    public void SetOwner(int playerId)
    {
        OwnerPlayerId = playerId;
        if (HasStateAuthority && HasInputAuthority && string.IsNullOrEmpty(OwnerNickname))
            OwnerNickname = ResolveLocalNickname();
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

    private void FixedUpdate()
    {
        // ✅ FIX : ce bloc modifiait directement le Rigidbody2D (bodyType, isKinematic,
        // simulated) SANS vérifier HasStateAuthority, donc il s'exécait aussi sur les
        // billes des AUTRES joueurs (proxies). Or avec Physics Forecast désactivé, Fusion
        // met automatiquement ces Rigidbody en kinematic sur les proxies pour piloter leur
        // position uniquement via le réseau. Les repasser en Dynamic + simulated ici
        // relançait une simulation physique locale en parallèle de la position reçue par
        // le réseau, causant les mêmes saccades que sur SoccerBallController.
        if (HasStateAuthority)
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
                _rb.bodyType = RigidbodyType2D.Dynamic;
                _rb.isKinematic = false;
                _rb.simulated = true;
                _rb.angularVelocity = 0f;
            }
        }

        // 🔒 Sécurité : Seul le propriétaire de la bille voit et contrôle sa propre flèche
        if (!HasStateAuthority || IsDead) return;

        // ✨ NEW : une bille pilotée par l'IA ne lit pas la souris, elle décide seule
        if (_isBotControlled)
        {
            UpdateBotAiming();
            return;
        }
    }
    private void Update()
    {
        // ✅ FIX (centralisation physique) : la lecture de la souris doit rester chez le
        // propriétaire réel de la bille (Input Authority), même si la State Authority
        // (qui pilote désormais la physique) appartient au Master. Une bille pilotée par
        // l'IA n'a pas d'Input Authority humaine : elle ne passe jamais ici (voir
        // UpdateBotAiming, appelé depuis FixedUpdate côté State Authority).
        if (!HasInputAuthority || IsDead) return;

        // ✅ DEBUG : affiche le raycast en continu
        if (_mainCamera != null)
        {
            Vector3 mouseScreenPos = Input.mousePosition;
            mouseScreenPos.z = -_mainCamera.transform.position.z;
            Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(mouseScreenPos);

            // La croix rouge s'affiche tant que tu ne bouges pas la souris
            Debug.DrawLine(
                mouseWorld + Vector3.left * 200.2f,
                mouseWorld + Vector3.right * 200.2f,
                Color.red
            );
            Debug.DrawLine(
                mouseWorld + Vector3.down * 200.2f,
                mouseWorld + Vector3.up * 200.2f,
                Color.red
            );
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
            enemyGoal: _enemyGoal);

        float force = Mathf.Lerp(maxForce * 0.3f, maxForce, forceFraction);
        _localQueuedForce = direction * force;
        _masterQueuedForce = _localQueuedForce; // le bot tourne sur le Master
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

        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
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
            // ✅ Enregistrement de la force localement (affichage de la flèche)
            _localQueuedForce = forceToApply;
            // ✅ FIX : transmis immédiatement au Master (appliqué plus tard, simultanément)
            RPC_SubmitShot(forceToApply);
        }
        else
        {
            _localQueuedForce = Vector2.zero;
            RPC_SubmitShot(Vector2.zero); // tir annulé
            _arrow.Hide();
        }
    }

    // ✅ FIX (tirs simultanés) : l'Input Authority envoie sa force au Master dès qu'elle la valide
    // (relâchement de la souris), et non plus au moment de la résolution. Ainsi, quand le timer
    // expire, le Master dispose déjà des forces de TOUS les joueurs et peut les appliquer dans la
    // même frame, sans dépendre de la latence de chaque client.
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SubmitShot(Vector2 force)
    {
        _masterQueuedForce = force; // Vector2.zero = tir annulé
    }

    /// <summary>
    /// Appelée UNIQUEMENT par le Master (TurnManager.ExecuteTurnResolution), pour toutes les
    /// billes dans la même boucle, donc dans la même frame : toutes les impulsions partent
    /// avant le même pas de physique.
    /// </summary>
    public void ApplyQueuedShotOnAuthority()
    {
        if (!HasStateAuthority || IsDead) return;

        if (_masterQueuedForce.sqrMagnitude > 0.01f)
        {
            RPC_ApplyImpulse(_masterQueuedForce);
            _masterQueuedForce = Vector2.zero;
        }
    }

    // ✅ Appelé sur CHAQUE client par TurnManager.RPC_ExecuteAllShots : nettoyage visuel seulement
    // (l'impulsion, elle, est appliquée par le Master via ApplyQueuedShotOnAuthority).
    public void ExecuteQueuedShot()
    {
        _localQueuedForce = Vector2.zero;
        _arrow.Hide();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyImpulse(Vector2 force)
    {
        if (_rb != null)
        {
            _rb.AddForce(force, ForceMode2D.Impulse);

            // ✅ FIX : le son de tir est joué ici, au même instant que l'impulsion, sur TOUS les
            // clients (RpcTargets.All). Avant, RPC_PlayShootSound n'était appelé nulle part : le
            // tir restait muet en LAN. Comme les rebonds ne peuvent arriver qu'après l'impulsion,
            // le tir est garanti d'être le premier son.
            AudioManager.Instance?.PlayShoot(transform.position);
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

    // ✅ NOUVEAU : Gestion des collisions pour les effets de rebond
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDead) return;

        // Vérifie si le rebond est assez violent
        if (_rb.velocity.sqrMagnitude > bounceForceThreshold * bounceForceThreshold)
        {

            // ✅ NOUVEAU : son de rebond. Pas de RPC ici (contrairement au tir) : comme pour
            // l'effet squash juste en dessous, OnCollisionEnter2D se déclenche localement sur
            // chaque client (colliders non-trigger, détectés indépendamment de la State
            // Authority), donc pas besoin de diffusion réseau.
            Vector2 contactPoint = collision.GetContact(0).point;
            AudioManager.Instance?.PlayBounce(contactPoint);

            // Lance tous les effets en parallèle
            StartCoroutine(BallImpactEffects.Squash(transform, originalScale, squashDuration, squashAmount, stretchAmount));
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

    /// <summary>
    /// ✅ FIX : remet cette bille à son état initial (position, rotation, échelle, couleur,
    /// physique, collider, IsDead) pour la manche suivante, après un but qui ne termine pas
    /// la partie. Appelée localement sur CHAQUE client par
    /// TurnManager.RPC_ResetAllForNewRound(), une fois la célébration de but terminée — même
    /// principe que RPC_PlayFallAnimation / RPC_AnimateGoalBall (chaque client rejoue le même
    /// résultat déterministe localement plutôt que de dépendre d'une réplication physique).
    /// </summary>
    public void ResetForNewRound()
    {
        StopAllCoroutines();

        _localQueuedForce = Vector2.zero;
        _masterQueuedForce = Vector2.zero;
        _botHasQueuedThisTurn = false;
        IsAiming = false;
        _arrow?.Hide();

        transform.position = _spawnPosition;
        transform.rotation = Quaternion.identity;
        transform.localScale = originalScale;

        if (_spriteRenderer != null)
        {
            _spriteRenderer.color = _initialColor;
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        // ✅ FIX : même principe que dans Update() — ne repasser le Rigidbody en
        // Dynamic/simulated que sur l'autorité. Sur un proxy, Fusion doit garder la
        // main sur le Rigidbody (kinematic) pour piloter sa position via le réseau ;
        // on se contente d'annuler toute vélocité résiduelle locale par sécurité.
        if (_rb != null)
        {
            _rb.velocity = Vector2.zero;
            _rb.angularVelocity = 0f;

            if (HasStateAuthority)
            {
                _rb.bodyType = RigidbodyType2D.Dynamic;
                _rb.isKinematic = false;
                _rb.simulated = true;
            }
        }

        IsDead = false;
        IsMoving = false;
    }
}