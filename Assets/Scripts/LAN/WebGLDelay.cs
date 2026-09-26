using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Remplacement de Task.Delay compatible WebGL (Task.Delay repose sur
/// System.Threading.Timer, non fonctionnel sous IL2CPP/WebGL — voir historique du
/// bug de démarrage de scène).
///
/// ✅ FIX : la version précédente démarrait la coroutine sur le MonoBehaviour
/// appelant (ex: GameSpawner). Si ce GameObject était désactivé entre-temps (par
/// n'importe quelle logique de jeu/UI), StartCoroutine levait une exception
/// ("Coroutine couldn't be started because the game object is inactive"), ce qui
/// interrompait silencieusement le await et empêchait la suite du code de
/// s'exécuter (ex: NotifyPlayerReadyToSpawn jamais appelée → le ballon ne spawn
/// jamais). Cette version fait tourner la coroutine sur un GameObject dédié,
/// créé une seule fois et marqué DontDestroyOnLoad, qui ne dépend d'aucun autre
/// objet du jeu et reste donc toujours actif.
/// </summary>
public static class WebGLDelay
{
    private class Runner : MonoBehaviour { }

    private static Runner _runner;

    private static Runner EnsureRunner()
    {
        if (_runner != null) return _runner;

        var go = new GameObject("[WebGLDelay Runner]");
        Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<Runner>();
        return _runner;
    }

    /// <summary>
    /// Le paramètre 'host' est conservé pour ne pas casser les appels existants
    /// (await WebGLDelay.Wait(0.1f, this)), mais n'est plus utilisé : la coroutine
    /// tourne toujours sur le Runner persistant, jamais sur 'host'.
    /// </summary>
    public static Task Wait(float seconds, MonoBehaviour host = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        EnsureRunner().StartCoroutine(Routine(seconds, tcs));
        return tcs.Task;
    }

    private static IEnumerator Routine(float seconds, TaskCompletionSource<bool> tcs)
    {
        yield return new WaitForSeconds(seconds);
        tcs.SetResult(true);
    }
}
