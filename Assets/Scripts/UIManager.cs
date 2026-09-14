using System.Collections;
using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    
    [SerializeField] private TextMeshProUGUI stateText;
    [SerializeField] private float defaultMessageDuration = 5f;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("[UIManager] Une instance existe déjà, destruction de celle-ci");
            Destroy(gameObject);
        }
    }
    
    public void ShowMessage(string message, float duration = -1)
    {
        if (duration < 0) duration = defaultMessageDuration;
        
        Debug.Log($"[UIManager] {message}");
        
        if (stateText != null)
        {
            stateText.text = message;
            StopAllCoroutines();
            StartCoroutine(ClearMessageAfterDelay(duration));
        }
    }
    
    public void ShowMessagePermanent(string message)
    {
        Debug.Log($"[UIManager] {message} (permanent)");
        
        if (stateText != null)
        {
            stateText.text = message;
            StopAllCoroutines();
        }
    }
    
    public void ClearMessage()
    {
        if (stateText != null)
        {
            stateText.text = "";
        }
    }
    
    private IEnumerator ClearMessageAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (stateText != null)
        {
            stateText.text = "";
        }
    }
}
