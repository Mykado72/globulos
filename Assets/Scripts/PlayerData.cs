using Fusion;
using UnityEngine;

public class PlayerData : NetworkBehaviour
{
    [Networked] public string Nickname { get; private set; }
    [Networked] public int PlayerId { get; private set; }

    // RPC pour envoyer le pseudo au serveur et le synchroniser sur tous les clients
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetNickname(string nickname)
    {
        Nickname = nickname;
    }

    public void SetPlayerId(int id)
    {
        PlayerId = id;
    }
}