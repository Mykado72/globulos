using System.Runtime.InteropServices;
using UnityEngine;

public static class WebMusic
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void PlayMusic(string url, int loop);
    [DllImport("__Internal")] private static extern void StopMusic();
    [DllImport("__Internal")] private static extern void SetMusicVolume(float volume);
#endif

    // Ex : WebMusic.Play("Music/game.mp3");
    public static void Play(string fileInStreamingAssets, bool loop = true)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string url = Application.streamingAssetsPath + "/" + fileInStreamingAssets;
        PlayMusic(url, loop ? 1 : 0);
#endif
    }

    public static void Stop()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        StopMusic();
#endif
    }

    public static void SetVolume(float volume01)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SetMusicVolume(volume01);
#endif
    }
}