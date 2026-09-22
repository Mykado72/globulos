using UnityEngine;

/// <summary>
/// ✅ Utilitaire centralisé pour obtenir le nom d'un joueur
/// Remplace les appels répétitifs à TurnManager/PlayerNamesManager
/// Utilise une hiérarchie de fallbacks garantissant toujours un résultat
/// </summary>
public static class PlayerNameHelper
{
    /// <summary>
    /// Obtient le surnom d'un joueur avec résolution automatique
    /// 
    /// Ordre de priorité :
    /// 1. TurnManager (mode réseau actif)
    /// 2. PlayerNamesManager (données locales synchronisées)
    /// 3. Fallback "Joueur {playerId}"
    /// </summary>
    public static string GetPlayerName(int playerId)
    {
        // 1️⃣ Essayer TurnManager (mode réseau Fusion)
        if (TurnManager.Instance != null)
        {
            string name = TurnManager.Instance.GetPlayerName(playerId);
            if (!string.IsNullOrEmpty(name) && name != $"Joueur {playerId}")
            {
                return name;
            }
        }

        // 2️⃣ Essayer PlayerNamesManager (fallback)
        if (PlayerNamesManager.Instance != null)
        {
            string name = PlayerNamesManager.Instance.GetPlayerName(playerId);
            if (!string.IsNullOrEmpty(name) && name != $"Joueur {playerId}")
            {
                return name;
            }
        }

        // 3️⃣ Fallback final
        return $"Joueur {playerId}";
    }

    /// <summary>
    /// Variante : Obtient le surnom avec cache optionnel pour performances
    /// Utile si appelé très souvent (ex: chaque frame dans l'UI)
    /// </summary>
    private static System.Collections.Generic.Dictionary<int, string> _nameCache 
        = new System.Collections.Generic.Dictionary<int, string>();

    public static string GetPlayerNameCached(int playerId)
    {
        if (!_nameCache.ContainsKey(playerId))
        {
            _nameCache[playerId] = GetPlayerName(playerId);
        }
        return _nameCache[playerId];
    }

    /// <summary>
    /// Vide le cache (à appeler quand un joueur change de surnom ou quitte)
    /// </summary>
    public static void ClearCache()
    {
        _nameCache.Clear();
    }

    /// <summary>
    /// Vide le cache pour un joueur spécifique
    /// </summary>
    public static void ClearCacheForPlayer(int playerId)
    {
        if (_nameCache.ContainsKey(playerId))
        {
            _nameCache.Remove(playerId);
        }
    }
}
