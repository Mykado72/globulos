using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

public static class WebMusic
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void PlayMusic(string url, int loop);
    [DllImport("__Internal")] private static extern void StopMusic();
    [DllImport("__Internal")] private static extern void SetMusicVolume(float volume);

    // Racine StreamingAssets qui a fonctionné (mémorisée pour ne plus sonder ensuite)
    private static string _workingBase;
    // Invalide les sondages en cours si on change de morceau / on arrête la musique
    private static int _requestId;
#endif

    // Ex : WebMusic.Play("Music/game.mp3");
    public static void Play(string fileInStreamingAssets, bool loop = true)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (string.IsNullOrEmpty(fileInStreamingAssets)) return;

        int id = ++_requestId;
        string rel = EncodeRelativePath(fileInStreamingAssets);

        if (_workingBase != null)
        {
            PlayMusic(_workingBase + "/" + rel, loop ? 1 : 0);
            return;
        }

        TryCandidate(BuildCandidateBases(), 0, rel, loop, id);
#endif
    }

    public static void Stop()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        _requestId++;
        StopMusic();
#endif
    }

    public static void SetVolume(float volume01)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SetMusicVolume(volume01);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // "/Music/Mon Fichier.mp3" -> "Music/Mon%20Fichier.mp3" (pas de "//", espaces/accents encodés)
    private static string EncodeRelativePath(string path)
    {
        string[] parts = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
            parts[i] = Uri.EscapeDataString(parts[i]);
        return string.Join("/", parts);
    }

    // Application.streamingAssetsPath est déduit de l'URL de la PAGE : si la page est servie
    // sans slash final (https://site/jeu) ou via une route, il pointe au mauvais endroit.
    // On teste donc plusieurs racines plausibles.
    private static List<string> BuildCandidateBases()
    {
        var list = new List<string>();
        void Add(string b)
        {
            b = b.TrimEnd('/');
            if (!list.Contains(b)) list.Add(b);
        }

        Add(Application.streamingAssetsPath);

        string page = Application.absoluteURL;
        if (!string.IsNullOrEmpty(page))
        {
            int cut = page.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) page = page.Substring(0, cut);

            if (Uri.TryCreate(page, UriKind.Absolute, out Uri u))
            {
                string origin = u.GetLeftPart(UriPartial.Authority);
                string path = u.AbsolutePath;
                int lastSlash = path.LastIndexOf('/');
                string lastSegment = path.Substring(lastSlash + 1);

                // Dossier de la page : /jeu/index.html -> /jeu/ ; /jeu/ -> /jeu/
                string dir = path.Substring(0, lastSlash + 1);
                Add(origin + dir + "StreamingAssets");

                // Page sans slash final et sans extension : https://site/jeu -> /jeu/StreamingAssets
                if (lastSegment.Length > 0 && !lastSegment.Contains("."))
                    Add(origin + path + "/StreamingAssets");

                Add(origin + "/StreamingAssets");
            }
        }
        return list;
    }

    private static void TryCandidate(List<string> bases, int index, string rel, bool loop, int id)
    {
        if (id != _requestId) return; // un autre morceau / Stop() a pris le relais

        if (index >= bases.Count)
        {
            Debug.LogError($"[WebMusic] ❌ '{rel}' introuvable sur toutes les racines testées : " +
                           string.Join(" | ", bases) +
                           ". Vérifie le nom (casse !), le dossier sur le serveur et le type MIME.");
            // Dernier recours : comportement d'origine
            PlayMusic(bases[0] + "/" + rel, loop ? 1 : 0);
            return;
        }

        string url = bases[index] + "/" + rel;
        UnityWebRequest req = UnityWebRequest.Head(url);
        UnityWebRequestAsyncOperation op = req.SendWebRequest();
        op.completed += _ =>
        {
            long code = req.responseCode;
            string contentType = req.GetResponseHeader("Content-Type") ?? "";
            // Un serveur "SPA" peut répondre 200 + index.html pour un fichier absent : on refuse text/*
            bool ok = req.result == UnityWebRequest.Result.Success
                      && !contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
            req.Dispose();

            if (id != _requestId) return;

            if (ok)
            {
                _workingBase = bases[index];
                Debug.Log($"[WebMusic] ✅ {url} (HTTP {code}, {contentType})");
                PlayMusic(url, loop ? 1 : 0);
            }
            else
            {
                Debug.LogWarning($"[WebMusic] ✖ {url} -> HTTP {code} {contentType}");
                TryCandidate(bases, index + 1, rel, loop, id);
            }
        };
    }
#endif
}
