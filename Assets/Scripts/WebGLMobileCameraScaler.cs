using UnityEngine;

[RequireComponent(typeof(Camera))]
public class WebGLMobileCameraScaler : MonoBehaviour
{
    [SerializeField] private float targetAspect = 16f / 9f; // ou 9f/16f pour portrait
    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        AdjustCamera();
    }

    private void OnRectTransformDimensionsChange()
    {
        AdjustCamera();
    }

    private void AdjustCamera()
    {
        if (_cam == null) return;

        float windowAspect = (float)Screen.width / (float)Screen.height;
        float scaleHeight = windowAspect / targetAspect;

        if (scaleHeight < 1.0f)
        {
            Rect rect = _cam.rect;
            rect.width = 1.0f;
            rect.height = scaleHeight;
            rect.x = 0;
            rect.y = (1.0f - scaleHeight) / 2.0f;
            _cam.rect = rect;
        }
        else
        {
            float scaleWidth = 1.0f / scaleHeight;
            Rect rect = _cam.rect;
            rect.width = scaleWidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) / 2.0f;
            rect.y = 0;
            _cam.rect = rect;
        }
    }
}