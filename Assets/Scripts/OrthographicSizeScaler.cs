using UnityEngine;

[RequireComponent(typeof(Camera))]
public class OrthographicSizeScaler : MonoBehaviour
{
    [Header("Configuration du Terrain")]
    [Tooltip("Largeur cible de votre zone de jeu en unités Unity.")]
    [SerializeField] private float targetWidth = 16f;

    [Tooltip("Hauteur cible de votre zone de jeu en unités Unity.")]
    [SerializeField] private float targetHeight = 9f;

    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        UpdateCameraSize();
    }

    // S'exécute aussi dans l'éditeur lors du redimensionnement de la fenêtre Game
    private void OnValidate()
    {
        _cam = GetComponent<Camera>();
        UpdateCameraSize();
    }

    private void Update()
    {
#if UNITY_EDITOR
        UpdateCameraSize();
#endif
    }

    public void UpdateCameraSize()
    {
        if (_cam == null || !_cam.orthographic) return;

        float targetAspect = targetWidth / targetHeight;
        float currentAspect = (float)Screen.width / (float)Screen.height;

        // Si l'écran est plus étroit que le ratio cible (ex: mobile vertical ou écran carré)
        if (currentAspect < targetAspect)
        {
            float unitsPerPixel = targetWidth / Screen.width;
            float desiredHalfHeight = 0.5f * unitsPerPixel * Screen.height;
            _cam.orthographicSize = desiredHalfHeight;
        }
        else
        {
            // Si l'écran est plus large, on se base sur la hauteur cible
            _cam.orthographicSize = targetHeight / 2f;
        }
    }
}