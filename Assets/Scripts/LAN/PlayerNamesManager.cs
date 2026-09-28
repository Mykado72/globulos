using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerNamesManager : MonoBehaviour
{
    public static PlayerNamesManager Instance { get; private set; }

    /// <summary>Déclenché quand un pseudo est ajouté ou modifié (permet aux UI de se rafraîchir).</summary>
    public event Action<int, string> OnNameChanged;

    // Stocke les pseudos : PlayerRef.PlayerId → Pseudo
    private Dictionary<int, string> _playerNames = new Dictionary<int, string>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Enregistre le pseudo d'un joueur. Ignore les valeurs vides et ne notifie que si ça change.
    /// </summary>
    public void SetPlayerName(int playerId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_playerNames.TryGetValue(playerId, out string existing) && existing == name)
            return;

        _playerNames[playerId] = name;
        PlayerNameHelper.ClearCacheForPlayer(playerId);
        OnNameChanged?.Invoke(playerId, name);
    }

    /// <summary>
    /// ✨ Renvoie true seulement si un VRAI pseudo est connu (pas le fallback "Joueur X").
    /// </summary>
    public bool TryGetPlayerName(int playerId, out string name)
    {
        return _playerNames.TryGetValue(playerId, out name) && !string.IsNullOrEmpty(name);
    }

    /// <summary>
    /// Récupère le pseudo d'un joueur (fallback "Joueur X" si inconnu).
    /// ⚠️ Ne renvoie JAMAIS null : pour tester "est-ce que le pseudo est connu ?", utiliser TryGetPlayerName.
    /// </summary>
    public string GetPlayerName(int playerId)
    {
        if (TryGetPlayerName(playerId, out string name))
            return name;

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
        PlayerNameHelper.ClearCache();
    }
}
