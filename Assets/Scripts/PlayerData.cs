using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// ✅ VERSION OPTIMISÉE v3
/// ✨ FIX: Fusionner SetNickname() et RPC_SetNickname en un seul RPC
/// Raison: Évite les risques de désynchronisation entre l'état local et réseau
public class PlayerData : NetworkBehaviour
{
    [Networked]
    public string Nickname { get; private set; }

    [Networked]
    public int PlayerId { get; private set; }

    /// <summary>
    /// ✨ FIX: RPC unique pour définir les infos du joueur
    /// Remplace les deux anciennes méthodes SetNickname() et RPC_SetNickname()
    /// 
    /// Appelé depuis:
    /// - GameSpawner.TrySpawnLocalPlayer() → playerData.RPC_SetPlayerInfo(nickname, playerId)
    /// - PlayerDataSpawner.OnPlayerJoined() → playerData.RPC_SetPlayerInfo(nickname, playerId)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetPlayerInfo(string nickname, int playerId)
    {
        Nickname = nickname;
        PlayerId = playerId;

        Debug.Log($"[PlayerData] ✅ RPC_SetPlayerInfo: {nickname} (ID: {playerId})");

        // ✅ Mise à jour immédiate du PlayerNamesManager local
        if (PlayerNamesManager.Instance != null && !string.IsNullOrEmpty(Nickname))
        {
            PlayerNamesManager.Instance.SetPlayerName(PlayerId, Nickname);
        }
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
