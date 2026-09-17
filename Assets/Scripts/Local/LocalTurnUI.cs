using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

/// ✅ CLIENT/SERVER MODE
/// TurnUI est peu impacté : il lit juste les propriétés [Networked] du TurnManager
/// et du BallAimController, qui sont maintenant centralisées sur le serveur
/// et répliquées aux clients.
public class LocalTurnUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private GameObject panelWIN;
    [SerializeField] private GameObject panelDRAW;

    [Header("Messages d'événements")]
    [SerializeField] private float ballDownMessageDuration = 2f;

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
        // ✅ CLIENT/SERVER : Tous les clients reçoivent les données du serveur
        if (LocalTurnManager.Instance == null) 
            return;

        DetectBallDeaths();

        // --- Fin de partie ---
        if (LocalTurnManager.Instance.CurrentState == LocalTurnManager.TurnState.Finished)
        {
            int winnerId = LocalTurnManager.Instance.WinnerPlayerId;
            bool isDraw = winnerId < 0;
            string winnerName = LocalTurnManager.Instance.GetPlayerName(winnerId);

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
            case LocalTurnManager.TurnState.Aiming:
                stateText.text = "Phase de préparation des tirs";
                break;
            case LocalTurnManager.TurnState.Resolution:
                stateText.text = "Déplacements en cours...";
                break;
            case LocalTurnManager.TurnState.CheckResult:
                stateText.text = "Fin du tour";
                break;
        }
    }

    private void DetectBallDeaths()
    {
        foreach (LocalBallAimController ball in LocalBallAimController.AllBalls)
        {
            if (ball == null) continue;

            bool isDeadNow = ball.IsDead;

            if (isDeadNow)
            {
                int ownerId = ball.OwnerPlayerId;
                string ownerName = LocalTurnManager.Instance != null ? LocalTurnManager.Instance.GetPlayerName(ownerId) : $"Joueur {ownerId}";
                _eventMessage = $"💥 Une bille de {ownerName} est tombée dans un but !"; // ✅ Pseudo au lieu de ID
                _eventMessageTimer = ballDownMessageDuration;
            }

        }
    }
}
