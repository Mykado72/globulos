using UnityEngine;

/// ✅ Script simple pour marquer une zone comme "But"
/// Attache ce script à un objet avec un Collider2D marqué comme "Is Trigger"
/// et tag "Goal" pour que les billes le reconnaissent.
public class GoalZone : MonoBehaviour
{
    private void Start()
    {
        // ✅ Vérifier que le tag est bien défini
        if (!gameObject.CompareTag("Goal"))
        {
            Debug.LogWarning($"[GoalZone] {gameObject.name} n'a pas le tag 'Goal' assigné!");
        }

        // ✅ Vérifier que le Collider2D est un trigger
        Collider2D col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[GoalZone] {gameObject.name} Collider2D doit avoir 'Is Trigger' activé!");
        }
    }

    // Les billes déclenchent OnTriggerEnter2D dans leur propre script (BallAimController)
    // donc on n'a rien de spécial à faire ici, c'est juste un marker/zone
}
