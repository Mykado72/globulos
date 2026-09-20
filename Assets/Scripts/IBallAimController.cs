using UnityEngine;

/// ✅ Interface commune pour LocalBallAimController et BallAimController (réseau)
/// Permet aux autres scripts (TurnUI, GameSpawner, etc.) de traiter les balles
/// indépendamment du mode (Local ou Network)
public interface IBallAimController
{
    // --- Identification ---
    
    /// <summary>Identifiant unique de la balle dans le jeu.</summary>
    int GetInstanceID();
    
    /// <summary>PlayerId du propriétaire de cette balle.</summary>
    int OwnerPlayerId { get; }

    // --- État de la balle ---
    
    /// <summary>La balle est-elle morte (tombée dans un but) ?</summary>
    bool IsDead { get; }
    
    /// <summary>La balle est-elle actuellement en mouvement ?</summary>
    bool IsMoving { get; }

    // --- Visée et tir ---
    
    /// <summary>Force du tir actuellement preparée (lecture depuis l'UI).</summary>
    Vector2 CurrentQueuedForce { get; }
    
    /// <summary>Direction du tir actuellement preparée (lecture depuis l'UI).</summary>
    Vector2 CurrentAimDirection { get; }

    // --- Actions ---
    
    /// <summary>Exécute le tir qui a été preparé en phase d'aiming.</summary>
    void ExecuteQueuedShot();
    
    /// <summary>Force l'arrêt de la visée (quand le timer expire).</summary>
    void ForceStopAiming();
    
    /// <summary>Place la balle en position initiale (réinitialisation).</summary>
    void ResetBall();
}
