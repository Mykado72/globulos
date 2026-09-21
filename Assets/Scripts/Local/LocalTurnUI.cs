using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// ✅ LOCAL MODE - TurnUI pour le mode local (sans Fusion)
/// Affiche le timer, l'état du jeu et les messages d'événements
/// Utilise LocalBallAimController et LocalTurnManager
public class LocalTurnUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private GameObject panelWIN;
    [SerializeField] private GameObject panelDRAW;

    [Header("Messages d'événements")]
    [SerializeField] private float ballDownMessageDuration = 2f;

    private readonly Dictionary<LocalBallAimController, bool> _previousDeadState = new Dictionary<LocalBallAimController, bool>();
    private float _eventMessageTimer = 0f;
    private string _eventMessage = "";
    private bool _endGameSoundPlayed = false;

    private void Start()
    {
        if (panelWIN != null) panelWIN.SetActive(false);
        if (panelDRAW != null) panelDRAW.SetActive(false);

    }

    private void Update()
    {
        // ✅ Vérifier que LocalTurnManager existe
        if (LocalTurnManager.Instance == null)
        {
            return;
        }

        DetectBallDeaths();

        // --- Fin de partie ---
        if (LocalTurnManager.Instance.CurrentState == TurnState.Finished)
        {
            int winnerId = LocalTurnManager.Instance.WinnerPlayerId;
            bool isDraw = winnerId < 0;
            string winnerName = LocalTurnManager.Instance.GetPlayerName(winnerId);

            if (panelDRAW != null) panelDRAW.SetActive(isDraw);
            if (panelWIN != null) panelWIN.SetActive(!isDraw);

            if (!_endGameSoundPlayed)
            {
                _endGameSoundPlayed = true;
                if (isDraw)
                {
                    AudioManager.Instance?.PlayDraw();
                    Debug.Log("[LocalTurnUI] 🤝 Match nul !");
                }
                else
                {
                    AudioManager.Instance?.PlayWin();
                    Debug.Log($"[LocalTurnUI] 🎉 Victoire du joueur {winnerName} !");
                }
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
        float remaining = LocalTurnManager.Instance.GetRemainingTime();
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

        switch (LocalTurnManager.Instance.CurrentState)
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

    /// <summary>
    /// ✨ Détecte quand une bille tombe dans un but et affiche le message
    /// </summary>
    private void DetectBallDeaths()
    {
        // ✨ FIXED: Utiliser LocalBallAimController au lieu de BallAimController
        foreach (LocalBallAimController ball in LocalBallAimController.AllBalls)
        {
            if (ball == null) continue;

            bool wasDead = _previousDeadState.TryGetValue(ball, out bool prev) && prev;
            bool isDeadNow = ball.IsDead;

            if (isDeadNow && !wasDead)
            {
                int ownerId = ball.OwnerPlayerId;
                string ownerName = LocalTurnManager.Instance != null
                    ? LocalTurnManager.Instance.GetPlayerName(ownerId)
                    : $"Joueur {ownerId}";

                _eventMessage = $"💥 Une bille de {ownerName} est tombée dans un but !";
                _eventMessageTimer = ballDownMessageDuration;

                Debug.Log($"[LocalTurnUI] 💥 {_eventMessage}");
            }

            _previousDeadState[ball] = isDeadNow;
        }
    }
}
