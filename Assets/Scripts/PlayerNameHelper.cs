using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ✅ Point d'entrée UNIQUE pour afficher le nom d'un joueur (HUD, célébrations, messages...).
///
/// Ordre de priorité :
///   1. PlayerData réseau (le pseudo [Networked], présent chez TOUS les clients)
///   2. PlayerNamesManager (pseudos locaux : joueur local, bot en mode Local...)
///   3. Fallback "Joueur {playerId}"
///
/// Un pseudo "placeholder" (vide, "Joueur 2", "Joueur_2") n'est jamais considéré comme un vrai pseudo.
/// </summary>
public static class PlayerNameHelper
{
    /// <summary>
    /// ✨ Pseudo du joueur LOCAL, gardé en mémoire (pas dans PlayerPrefs).
    /// Renseigné par LobbyManager juste avant StartGame. PlayerPrefs est partagé entre deux
    /// instances lancées sur la même machine/navigateur (2 onglets WebGL, 2 builds...) : la
    /// dernière qui sauvegarde écrase l'autre, et les deux joueurs récupéraient le même pseudo.
    /// </summary>
    public static string LocalNickname { get; set; }

    public static bool IsPlaceholder(int playerId, string name)
    {
        return string.IsNullOrWhiteSpace(name)
            || name == $"Joueur {playerId}"
            || name == $"Joueur_{playerId}";
    }

    /// <summary>
    /// true si un vrai pseudo est connu. Permet à l'appelant de choisir son propre fallback
    /// (ex : "Équipe Jaune" dans le HUD).
    /// </summary>
    public static bool TryGetRealName(int playerId, out string name)
    {
        // 1️⃣ PlayerData répliqués par Fusion
        var all = PlayerData.All;
        for (int i = 0; i < all.Count; i++)
        {
            var pd = all[i];
            if (pd == null || pd.Object == null) continue;
            if (pd.Object.InputAuthority.PlayerId != playerId) continue;

            string n = pd.Nickname;
            if (!IsPlaceholder(playerId, n))
            {
                name = n;
                return true;
            }
        }

        // 1️⃣bis Pseudo répliqué avec les billes (ne dépend pas de PlayerData)
        var balls = BallAimController.AllBalls;
        for (int i = 0; i < balls.Count; i++)
        {
            var b = balls[i];
            if (b == null || b.OwnerPlayerId != playerId) continue;

            string n = b.OwnerNickname;
            if (!IsPlaceholder(playerId, n))
            {
                name = n;
                return true;
            }
        }

        // 2️⃣ Pseudos locaux
        if (PlayerNamesManager.Instance != null &&
            PlayerNamesManager.Instance.TryGetPlayerName(playerId, out string local) &&
            !IsPlaceholder(playerId, local))
        {
            name = local;
            return true;
        }

        name = null;
        return false;
    }

    public static string GetPlayerName(int playerId)
    {
        return TryGetRealName(playerId, out string name) ? name : $"Joueur {playerId}";
    }

    // Cache optionnel : on ne met JAMAIS en cache un fallback (sinon le vrai pseudo n'apparaîtrait jamais).
    private static readonly Dictionary<int, string> _nameCache = new Dictionary<int, string>();

    public static string GetPlayerNameCached(int playerId)
    {
        if (_nameCache.TryGetValue(playerId, out string cached)) return cached;

        if (TryGetRealName(playerId, out string name))
        {
            _nameCache[playerId] = name;
            return name;
        }
        return $"Joueur {playerId}";
    }

    public static void ClearCache() => _nameCache.Clear();

    public static void ClearCacheForPlayer(int playerId) => _nameCache.Remove(playerId);
}
