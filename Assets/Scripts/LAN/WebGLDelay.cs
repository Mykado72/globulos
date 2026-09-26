using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class WebGLDelay
{
    /// <summary>
    /// Remplacement de Task.Delay compatible WebGL (Task.Delay repose sur
    /// System.Threading.Timer, non supporté sous WebGL — voir ticket Unity).
    /// </summary>
    public static Task Wait(float seconds, MonoBehaviour host)
    {
        var tcs = new TaskCompletionSource<bool>();
        host.StartCoroutine(Routine(seconds, tcs));
        return tcs.Task;
    }

    private static IEnumerator Routine(float seconds, TaskCompletionSource<bool> tcs)
    {
        yield return new WaitForSeconds(seconds);
        tcs.SetResult(true);
    }
}
