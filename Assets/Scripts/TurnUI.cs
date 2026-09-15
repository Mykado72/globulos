using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

/// ✅ CLIENT/SERVER MODE
/// TurnUI est peu impacté : il lit juste les propriétés [Networked] du TurnManager
/// et du BallAimController, qui sont maintenant centralisées sur le serveur
/// et répliquées aux clients.
public class TurnUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private GameObject panelWIN;
    [SerializeField] private GameObject panelDRAW;

    [Header("Messages d'événements")]
    [SerializeField] private float ballDownMessageDuration = 2f;

    private readonly Dictionary<NetworkId, bool> _previousDeadState = new Dictionary<NetworkId, bool>();
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
        if (TurnManager.Instance == null || !TurnManager.Instance.Object.IsValid) 
            return;

        DetectBallDeaths();

        // --- Fin de partie ---
        if (TurnManager.Instance.CurrentState == TurnManager.TurnState.Finished)
        {
            int winnerId = TurnManager.Instance.WinnerPlayerId;
            bool isDraw = winnerId < 0;

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
                    : $"🎉 Victoire du Joueur {winnerId} !";
            }

            return;
        }

        // Partie en cours
        if (panelWIN != null) panelWIN.SetActive(false);
        if (panelDRAW != null) panelDRAW.SetActive(false);

        // Chrono
        float remaining = TurnManager.Instance.GetRemainingTime();
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

        switch (TurnManager.Instance.CurrentState)
        {
            case TurnManager.TurnState.Aiming:
                stateText.text = "Phase de préparation des tirs";
                break;
            case TurnManager.TurnState.Resolution:
                stateText.text = "Déplacements en cours...";
                break;
            case TurnManager.TurnState.CheckResult:
                stateText.text = "Fin du tour";
                break;
        }
    }

    private void DetectBallDeaths()
    {
        foreach (BallAimController ball in BallAimController.AllBalls)
        {
            if (ball == null) continue;

            NetworkObject netObj = ball.NetObj;
            if (netObj == null || !netObj.IsValid) continue;

            NetworkId id = netObj.Id;
            bool wasDead = _previousDeadState.TryGetValue(id, out bool prev) && prev;
            bool isDeadNow = ball.IsDead;

            if (isDeadNow && !wasDead)
            {
                int ownerId = ball.OwnerPlayerId;
                _eventMessage = $"💥 Une bille du Joueur {ownerId} est tombée dans un but !";
                _eventMessageTimer = ballDownMessageDuration;
            }

            _previousDeadState[id] = isDeadNow;
        }
    }
}
