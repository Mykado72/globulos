using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// ✨ Sur WebGL + navigateur mobile UNIQUEMENT : remplace l'édition normale d'un TMP_InputField
/// par une boîte de dialogue window.prompt() native, qui ouvre fiablement le clavier virtuel du
/// téléphone (voir WebGLMobileNicknamePrompt.jslib pour le pourquoi).
///
/// N'a aucun effet ailleurs (Éditeur, desktop WebGL, build natif Android/iOS) : le TMP_InputField
/// garde son comportement standard, qui fonctionne déjà très bien dans ces cas-là.
///
/// Usage : s'attache automatiquement (voir LobbyManager.Awake) aux champs playerNicknameInput et
/// roomNameInput. Peut aussi être ajouté manuellement dans l'Inspector sur n'importe quel
/// GameObject portant un TMP_InputField.
/// </summary>
[RequireComponent(typeof(TMP_InputField))]
public class WebGLMobileNicknamePrompt : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private string promptMessage = "Entrez votre pseudo :";

    private TMP_InputField _field;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern string WebGLPrompt_Show(string message, string defaultValue);
#endif

    private void Awake()
    {
        _field = GetComponent<TMP_InputField>();
    }

    /// <summary>Permet de personnaliser le message affiché, notamment quand le composant est ajouté par code (voir LobbyManager).</summary>
    public void Configure(string message)
    {
        if (!string.IsNullOrEmpty(message)) promptMessage = message;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // Desktop WebGL : un vrai clavier est branché, le TMP_InputField natif suffit.
        if (!Application.isMobilePlatform) return;

        // Empêche le TMP_InputField d'entrer AUSSI en mode édition (double interaction : le
        // prompt() natif suffit, on ne veut pas que le caret Unity capte des touches en plus).
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        string result = WebGLPrompt_Show(promptMessage, _field.text ?? "");
        if (!string.IsNullOrEmpty(result) && result != _field.text)
        {
            _field.text = result;
            // Certaines versions de TMP_InputField n'invoquent pas onValueChanged quand on passe
            // par la propriété .text : on le déclenche explicitement pour rester cohérent avec
            // tout code qui écoute ces événements (ex: validation en direct).
            _field.onValueChanged?.Invoke(result);
        }
        // Toujours notifier la fin d'édition, même si le texte n'a pas changé (Annuler = on
        // garde l'ancienne valeur, mais un code qui écoute onEndEdit doit quand même le savoir).
        _field.onEndEdit?.Invoke(_field.text);
#endif
    }
}
