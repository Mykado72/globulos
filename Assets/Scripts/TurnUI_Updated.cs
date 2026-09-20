using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// ✅ CLIENT/SERVER & LOCAL
/// TurnUI est complètement agnostique au mode (Local ou Network).
/// Il lit simplement les données via ITurnManagerCore.
public class TurnUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private GameObject panelWIN;
    [SerializeField] private GameObject panelDRAW;

    [Header("Messages d'événements")]
    [SerializeField] private float ballDownMessageDuration = 2f;

    private ITurnManagerCore _turnManager;
    private readonly Dictionary<int, bool> _previousDeadState = new Dictionary<int, bool>();
    private float _eventMessageTimer = 0f;
    private string _eventMessage = "";
    private bool _endGameSoundPlayed = false;

    private void Start()
    {
        if (panelWIN != null) panelWIN.SetActive(false);
        if (panelDRAW != null) panelDRAW.SetActive(false);

        // ✅ DÉTECTION AUTOMATIQUE du mode (Local ou Network)
        _turnManager = FindTurnManager();
        if (_turnManager == null)
        {
            Debug.LogError("[TurnUI] ❌ Aucun ITurnManagerCore trouvé (LocalTurnManager ou TurnManager)!");
        }
    }

    private void Update()
    {
        if (_turnManager == null)
            return;

        DetectBallDeaths();

        // --- Fin de partie ---
        if (_turnManager.CurrentState == TurnState.Finished)
        {
            int winnerId = _turnManager.WinnerPlayerId;
            bool isDraw = winnerId < 0;
            string winnerName = _turnManager.GetPlayerName(winnerId);

            if (panelDRAW != null) panelDRAW.SetActive(isDraw);
            if (panelWIN != null) panelWIN.SetActive(!isDraw);

            if (!_endGameSoundPlayed)
            {
                _endGameSoundPlayed = true;
                if (isDraw) AudioManager.Instance?.PlayDraw();
                else AudioManager.Instance?.PlayWin();
            }

            if (timerText != null) timerText.text = "";

            if (stateText != null)
            {
                stateText.text = isDraw
                    ? "Match nul !"
                    : $"🎉 Victoire du Joueur {winnerName} !";
            }

            return;
        }

        // Partie en cours
        if (panelWIN != null) panelWIN.SetActive(false);
        if (panelDRAW != null) panelDRAW.SetActive(false);

        // Chrono
        float remaining = _turnManager.GetRemainingTime();
        if (timerText != null) timerText.text = Mathf.CeilToInt(remaining).ToString();

        // Message temporaire : prioritaire
        if (_eventMessageTimer > 0f)
        {
            _eventMessageTimer -= Time.deltaTime;
            if (stateText != null) stateText.text = _eventMessage;
            return;
        }

        // Affichage normal
        if (stateText == null) return;

        switch (_turnManager.CurrentState)
        {
            case TurnState.Aiming:
                stateText.text = "Phase de préparation des tirs";
                break;
            case TurnState.Resolution:
                stateText.text = "Déplacements en cours...";
                break;
            case TurnState.CheckResult:
                stateText.text = "Fin du tour";
                break;
        }
    }

    private void DetectBallDeaths()
    {
        foreach (var ball in GetAllBalls())
        {
            if (ball == null) continue;

            int ballId = ball.GetInstanceID();
            bool wasDead = _previousDeadState.TryGetValue(ballId, out bool prev) && prev;
            bool isDeadNow = ball.IsDead;

            if (isDeadNow && !wasDead)
            {
                int ownerId = ball.OwnerPlayerId;
                string ownerName = _turnManager.GetPlayerName(ownerId);
                _eventMessage = $"💥 Une bille de {ownerName} est tombée dans un but !";
                _eventMessageTimer = ballDownMessageDuration;
            }

            _previousDeadState[ballId] = isDeadNow;
        }
    }

    /// ✅ Détecte automatiquement les balles du mode courant
    private List<IBallAimController> GetAllBalls()
    {
        var balls = new List<IBallAimController>();

        // Mode Local
        balls.AddRange(LocalBallAimController.AllBalls);

        // Mode Network (si disponible)
        balls.AddRange(BallAimController.AllBalls);

        return balls;
    }

    /// ✅ Détecte automatiquement le TurnManager du mode courant
    private ITurnManagerCore FindTurnManager()
    {
        // Cherche d'abord LocalTurnManager
        LocalTurnManager localTM = FindObjectOfType<LocalTurnManager>();
        if (localTM != null)
        {
            Debug.Log("[TurnUI] ✅ Mode LOCAL détecté");
            return localTM;
        }

        // Sinon cherche TurnManager (réseau)
        TurnManager networkTM = FindObjectOfType<TurnManager>();
        if (networkTM != null)
        {
            Debug.Log("[TurnUI] ✅ Mode NETWORK détecté");
            return networkTM;
        }

        return null;
    }
}

/// ✅ Interface minimale pour abstraction des balles
/// (Optionnel, mais utile pour GetAllBalls())
public interface IBallAimController
{
    int OwnerPlayerId { get; }
    bool IsDead { get; }
}
