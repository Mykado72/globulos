using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// ✅ VERSION OPTIMISÉE v3
/// ✨ FIX: Fusionner SetNickname() et RPC_SetNickname en un seul RPC
/// Raison: Évite les risques de désynchronisation entre l'état local et réseau
public class PlayerData : NetworkBehaviour
{
    [Networked, Capacity(32)]
    public string Nickname { get; private set; }

    [Networked]
    public int PlayerId { get; private set; }

    /// <summary>Registre statique de tous les PlayerData vivants (évite FindObjectsByType à chaque affichage).</summary>
    public static readonly List<PlayerData> All = new List<PlayerData>();

    private string _lastPushedNickname;

    public override void Spawned()
    {
        if (!All.Contains(this)) All.Add(this);
        PushNameToManager();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        All.Remove(this);
    }

    /// <summary>
    /// ✨ FIX PRINCIPAL : Nickname est [Networked] donc répliqué chez tout le monde, mais le
    /// PlayerNamesManager n'était rempli que côté StateAuthority (dans le RPC).
    /// Ici CHAQUE client recopie le pseudo répliqué dans son PlayerNamesManager local.
    /// </summary>
    public override void Render()
    {
        if (Nickname != _lastPushedNickname) PushNameToManager();
    }

    private void PushNameToManager()
    {
        _lastPushedNickname = Nickname;
        if (Object == null || PlayerNamesManager.Instance == null) return;

        // Clé = InputAuthority (fiable dès le spawn), PlayerId networké n'est parfois pas encore arrivé
        int id = Object.InputAuthority.IsNone ? PlayerId : Object.InputAuthority.PlayerId;
        if (!PlayerNameHelper.IsPlaceholder(id, Nickname))
            PlayerNamesManager.Instance.SetPlayerName(id, Nickname);
    }

    /// <summary>
    /// ✨ FIX: RPC unique pour définir les infos du joueur
    /// Remplace les deux anciennes méthodes SetNickname() et RPC_SetNickname()
    /// 
    /// Appelé depuis:
    /// - BallAimController.Spawned() (propriétaire réel, Input Authority) → playerData.RPC_SetPlayerInfo(nickname, playerId)
    /// - PlayerDataSpawner.OnPlayerJoined() → playerData.RPC_SetPlayerInfo(nickname, playerId)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetPlayerInfo(string nickname, int playerId)
    {
        Nickname = nickname;
        PlayerId = playerId;


        // ✅ Mise à jour immédiate du PlayerNamesManager local (les autres clients passent par Render())
        PushNameToManager();
    }

    /// <summary>
    /// Récupère le pseudo du joueur
    /// </summary>
    public string GetNickname()
    {
        return Nickname;
    }

    /// <summary>
    /// Récupère l'ID du joueur
    /// </summary>
    public int GetPlayerId()
    {
        return PlayerId;
    }
}
