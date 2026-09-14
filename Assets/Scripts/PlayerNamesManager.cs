using Fusion;
using System.Collections.Generic;
using UnityEngine;

public class PlayerNamesManager : MonoBehaviour
{
    public static PlayerNamesManager Instance { get; private set; }

    // Stocke les pseudos : PlayerRef.PlayerId → Pseudo
    private Dictionary<int, string> _playerNames = new Dictionary<int, string>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Enregistre le pseudo d'un joueur (appelle depuis le Lobby)
    /// </summary>
    public void SetPlayerName(int playerId, string name)
    {
        _playerNames[playerId] = name;
        Debug.Log($"[PlayerNamesManager] Joueur {playerId} = {name}");
    }

    /// <summary>
    /// Récupère le pseudo d'un joueur
    /// </summary>
    public string GetPlayerName(int playerId)
    {
        if (_playerNames.ContainsKey(playerId))
            return _playerNames[playerId];

        return $"Joueur {playerId}";  // Fallback
    }

    /// <summary>
    /// Récupère le pseudo via un PlayerRef
    /// </summary>
    public string GetPlayerName(PlayerRef playerRef)
    {
        if (playerRef.IsNone)
            return "Inconnu";

        return GetPlayerName(playerRef.PlayerId);
    }

    /// <summary>
    /// Efface les pseudos (pour nouvelle partie)
    /// </summary>
    public void Clear()
    {
        _playerNames.Clear();
    }
}