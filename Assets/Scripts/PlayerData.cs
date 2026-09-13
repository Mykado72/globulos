using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerData : NetworkBehaviour
{
    [Networked]
    public string Nickname { get; private set; }

    public void SetNickname(string nickname)
    {
        Nickname = nickname;
    }
}
