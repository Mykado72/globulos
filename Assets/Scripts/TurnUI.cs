using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Fusion;

/// ✅ CLIENT/SERVER & LOCAL
/// TurnUI est complètement agnostique au mode (Local ou Network).
/// Il lit simplement les données via ITurnManagerCore.
public class TurnUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text stateText;

    [Header("Messages d'événements")]
    [SerializeField] private float ballDownMessageDuration = 2f;

    private ITurnManagerCore _turnManager;
    private readonly Dictionary<int, bool> _previousDeadState = new Dictionary<int, bool>();
    private float _eventMessageTimer = 0f;
    private string _eventMessage = "";
    private bool _endGameSoundPlayed = false;

    // ✅ Valeurs d'origine du timer, capturées une fois, pour pouvoir y revenir après le
    // pulse d'urgence (voir TimerPulseEffect).
    private Vector3 _timerBaseScale = Vector3.one;
    private Color _timerBaseColor = Color.white;
    private int _timerFontSize = 50;

    private void Start()
    {
        if (timerText != null)
        {
            _timerBaseScale = timerText.transform.localScale;
            _timerBaseColor = timerText.color;
        }

        // ✅ DÉTECTION AUTOMATIQUE du mode (Local ou Network)
        _turnManager = TurnManagerFactory.GetTurnManager();
        if (_turnManager == null)
        {
            Debug.LogError("[TurnUI] ❌ Aucun ITurnManagerCore trouvé (LocalTurnManager ou TurnManager)!");
        }
    }

    private void Update()
    {
        if (_turnManager == null)
            return;
        // ✅ Attendre que TurnManager soit spawné
        if (!IsTurnManagerReady())
            return;

        DetectBallDeaths();

        // --- Fin de partie ---
        if (_turnManager.CurrentState == TurnState.Finished)
        {
            int winnerId = _turnManager.WinnerPlayerId;
            bool isDraw = winnerId < 0;
            string winnerName = _turnManager.GetPlayerName(winnerId);

            if (!_endGameSoundPlayed)
            {
                _endGameSoundPlayed = true;
                if (isDraw) AudioManager.Instance?.PlayDraw();
                else AudioManager.Instance?.PlayWin();
            }

            if (timerText != null)
            {
                timerText.text = "";
                TimerPulseEffect.Reset(timerText, _timerBaseScale, _timerBaseColor, _timerFontSize);
            }

            if (stateText != null)
            {
                stateText.text = isDraw
                    ? "Match nul !"
                    : $"🎉 Victoire du Joueur {winnerName} !";
            }

            return;
        }

        if (_turnManager.CurrentState == TurnState.Celebrating)
        {
            if (timerText != null)
            {
                timerText.text = "";
                TimerPulseEffect.Reset(timerText, _timerBaseScale, _timerBaseColor, _timerFontSize);
            }

            if (_eventMessageTimer > 0f)
            {
                _eventMessageTimer -= Time.deltaTime;
                if (stateText != null) stateText.text = _eventMessage;
            }
            else if (stateText != null)
            {
                stateText.text = "⚽ GOAAALLLLL !!!!!!";
            }

            return;
        }

        // Chrono
        float remaining = _turnManager.GetRemainingTime();
        if (timerText != null)
        {
            timerText.text = Mathf.CeilToInt(remaining).ToString();
            // ✅ Animation d'urgence : le chiffre grossit et passe au rouge sous 3 secondes
            TimerPulseEffect.Apply(timerText, remaining, _timerBaseScale, _timerBaseColor, _timerFontSize);
        }

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

    private bool IsTurnManagerReady()
    {
        // Mode Local : toujours prêt
        if (_turnManager is LocalTurnManager)
            return true;

        // Mode Network : vérifier IsSpawned
        if (_turnManager is TurnManager networkTM)
        {
            if (networkTM.Object == null || !networkTM.Object.IsValid || !networkTM.Object.IsValid)
                return false;
        }

        return true;
    }
    private void DetectBallDeaths()
    {
        // Mode Local
        foreach (var ball in LocalBallAimController.AllBalls)
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

        // Mode Network (Fusion)
        foreach (var ball in BallAimController.AllBalls)
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
}