using TMPro;
using UnityEngine;

public class TurnUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text stateText;

    private void Update()
    {
        // Object.IsValid garantit que Spawned() a bien été appelé
        // et que les propriétés [Networked] sont accessibles.
        if (TurnManager.Instance == null || !TurnManager.Instance.Object.IsValid) return;

        // Affichage du chrono
        float remaining = TurnManager.Instance.GetRemainingTime();
        timerText.text = Mathf.CeilToInt(remaining).ToString();

        // Affichage de la phase
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
            case TurnManager.TurnState.Finished:
                stateText.text = "Partie terminée";
                break;
        }
    }
}