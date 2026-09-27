using UnityEngine;

public class CameraManager : MonoBehaviour
{
    [SerializeField] private float targetAspectRatio = 16f / 9f;  // À adapter à ton jeu
    private Camera _mainCamera;
    private float _lastScreenWidth;
    private float _lastScreenHeight;

    private void Start()
    {
        _mainCamera = Camera.main;
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        UpdateCameraSize();
    }

    private void Update()
    {
        // ✅ Recalculer si l'écran a changé de taille (important en WebGL/mobile)
        if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            UpdateCameraSize();
            Debug.Log($"📱 Resize détecté: {Screen.width}x{Screen.height}");
        }
    }

    private void UpdateCameraSize()
    {
        if (_mainCamera == null || !_mainCamera.orthographic) return;

        float currentAspect = (float)Screen.width / Screen.height;
        float desiredAspect = targetAspectRatio;

        if (currentAspect < desiredAspect)
        {
            // Écran plus étroit → augmente la hauteur visible
            _mainCamera.orthographicSize = 5f;  // À adapter à ta hauteur de jeu
        }
        else
        {
            // Écran plus large → ajuste la hauteur
            _mainCamera.orthographicSize = 5f / (currentAspect / desiredAspect);
        }

        Debug.Log($"📷 Caméra orthographicSize: {_mainCamera.orthographicSize}, aspect: {currentAspect}");
    }
}