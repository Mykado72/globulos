using Fusion;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(LineRenderer))]
public class BallAimController : NetworkBehaviour
{
    [Header("Aim Settings")]
    [SerializeField] private float maxForce = 15f;
    [SerializeField] private float forceMultiplier = 3f;

    private Rigidbody2D _rb;
    private LineRenderer _lineRenderer;
    private Camera _mainCamera;

    private Vector2 _startDragPos;
    private bool _isAiming = false;

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody2D>();
        _lineRenderer = GetComponent<LineRenderer>();
        _mainCamera = Camera.main;

        // On masque la ligne de visée au départ
        _lineRenderer.positionCount = 2;
        _lineRenderer.enabled = false;
    }

    private void OnMouseDown()
    {
        // Seul le joueur contrôlant cette bille (StateAuthority/InputAuthority) peut viser
        if (!HasInputAuthority) return;

        _isAiming = true;
        _startDragPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        _lineRenderer.enabled = true;
    }

    private void OnMouseDrag()
    {
        if (!_isAiming || !HasInputAuthority) return;

        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragVector = _startDragPos - currentMousePos; // Direction opposée au glissement (style fronde)

        // Limiter la force maximale
        Vector2 clampedForce = Vector2.ClampMagnitude(dragVector * forceMultiplier, maxForce);

        // Mise à jour de la ligne de visée dans le monde
        _lineRenderer.SetPosition(0, transform.position);
        _lineRenderer.SetPosition(1, (Vector2)transform.position + clampedForce);
    }

    private void OnMouseUp()
    {
        // VÉRIFICATION : Seul le joueur possédant l'Input Authority peut interagir avec cette bille
        if (!_isAiming || !HasInputAuthority) return;

        _isAiming = true;
        _startDragPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        _lineRenderer.enabled = true;
        _isAiming = false;
        _lineRenderer.enabled = false;

        Vector2 currentMousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dragVector = _startDragPos - currentMousePos;
        Vector2 forceToApply = Vector2.ClampMagnitude(dragVector * forceMultiplier, maxForce);

        // Application de l'impulsion physique
        Shoot(forceToApply);
    }

    private void Shoot(Vector2 force)
    {
        _rb.AddForce(force, ForceMode2D.Impulse);
    }
}