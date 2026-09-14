using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

public class TurnUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private GameObject panelWIN;
    [SerializeField] private GameObject panelDRAW;

    [Header("Messages d'événements (bille dans un but)")]
    [Tooltip("Durée d'affichage (en secondes) du message temporaire dans stateText quand une bille tombe dans un but.")]
    [SerializeField] private float ballDownMessageDuration = 2f;

    // Suivi local (par client) de l'état IsDead de chaque bille pour détecter les transitions
    // false -> true, sans avoir besoin d'ajouter la moindre RPC : IsDead est déjà une
    // propriété [Networked] répliquée à tout le monde par BallAimController.
    private readonly Dictionary<NetworkId, bool> _previousDeadState = new Dictionary<NetworkId, bool>();

    private float _eventMessageTimer = 0f;
    private string _eventMessage = "";

    private void Start()
    {
        if (panelWIN != null) panelWIN.SetActive(false);
        if (panelDRAW != null) panelDRAW.SetActive(false);
    }

    private void Update()
    {
        // Object.IsValid garantit que Spawned() a bien été appelé
        // et que les propriétés [Networked] sont accessibles.
        if (TurnManager.Instance == null || !TurnManager.Instance.Object.IsValid) return;

        DetectBallDeaths();

        // --- Fin de partie : Victoire / Égalité ---
        if (TurnManager.Instance.CurrentState == TurnManager.TurnState.Finished)
        {
            int winnerId = TurnManager.Instance.WinnerPlayerId;
            bool isDraw = winnerId < 0;

            if (panelDRAW != null) panelDRAW.SetActive(isDraw);
            if (panelWIN != null) panelWIN.SetActive(!isDraw);

            if (timerText != null) timerText.text = "";

            if (stateText != null)
            {
                stateText.text = isDraw
                    ? "Match nul !"
                    : $"🎉 Victoire du Joueur {winnerId} !";
            }

            return;
        }

        // Partie en cours : on s'assure que les panels de fin sont masqués
        if (panelWIN != null) panelWIN.SetActive(false);
        if (panelDRAW != null) panelDRAW.SetActive(false);

        // Affichage du chrono
        float remaining = TurnManager.Instance.GetRemainingTime();
        if (timerText != null) timerText.text = Mathf.CeilToInt(remaining).ToString();

        // --- Message temporaire (bille tombée dans un but) : prioritaire sur la phase ---
        if (_eventMessageTimer > 0f)
        {
            _eventMessageTimer -= Time.deltaTime;
            if (stateText != null) stateText.text = _eventMessage;
            return;
        }

        // Affichage normal de la phase en cours
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

    // Détecte les transitions IsDead (false -> true) sur toutes les billes connues et
    // déclenche un message temporaire dans stateText. Fonctionne indépendamment sur
    // chaque client, sans RPC supplémentaire, car IsDead est déjà répliqué par Fusion.
    private void DetectBallDeaths()
    {
        // ✅ OPTIMISATION : registre statique (BallAimController.AllBalls), toujours à
        // jour en temps réel — plus besoin de rafraîchir périodiquement une copie locale.
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
                // StateAuthority = propriétaire réel de la bille en Shared Mode
                int ownerId = netObj.StateAuthority.PlayerId;
                _eventMessage = $"💥 Une bille du Joueur {ownerId} est tombée dans un but !";
                _eventMessageTimer = ballDownMessageDuration;
            }

            _previousDeadState[id] = isDeadNow;
        }
    }
}