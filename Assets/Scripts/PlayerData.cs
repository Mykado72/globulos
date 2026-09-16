using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerData : NetworkBehaviour
{
    [Networked]
    public string Nickname { get; private set; }
    [Networked]
    public int PlayerId { get; private set; }

    public void SetNickname(string nickname)
    {
        Nickname = nickname;
    }

    public void SetPlayerId(int id)
    {
        PlayerId = id;
    }

    // RPC pour permettre à la machine locale de définir le pseudo synchronisé
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetNickname(string nickname, int playerId)
    {
        Nickname = nickname;
        PlayerId = playerId;
    }

    private void OnNicknameChanged()
    {
        // Quand le pseudo est répliqué par le réseau, on met à jour le PlayerNamesManager local
        if (PlayerNamesManager.Instance != null && !string.IsNullOrEmpty(Nickname))
        {
            PlayerNamesManager.Instance.SetPlayerName(PlayerId, Nickname);
            Debug.Log($"[PlayerData] 🌐 Pseudo réseau mis à jour : {Nickname} pour l'ID {PlayerId}");
        }
    }
}
