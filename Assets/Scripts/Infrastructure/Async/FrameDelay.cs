using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// WebGL-safe replacements for <see cref="Task.Delay(int)"/> and <see cref="Task.Yield"/>.
/// The browser player has no threads, so timer-based waits can stall forever; there the
/// wait is counted in player-loop frames against real time. Desktop and server builds keep
/// the original Task-based behaviour.
/// </summary>
/// <remarks>
/// The WebGL path uses <see cref="Awaitable"/>, so it must be awaited from the main thread
/// (every caller is Unity-side client code).
/// </remarks>
public static class FrameDelay
{
    #region Public API

    /// <summary>Waits the given real time; throws <see cref="OperationCanceledException"/> on cancel.</summary>
    public static Task Delay(int milliseconds, CancellationToken cancellationToken = default)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return WaitRealtimeAsync(milliseconds / 1000f, cancellationToken);
#else
        return Task.Delay(milliseconds, cancellationToken);
#endif
    }

    /// <inheritdoc cref="Delay(int, CancellationToken)"/>
    public static Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return Delay((int)Math.Max(0, delay.TotalMilliseconds), cancellationToken);
    }

    /// <summary>Resumes on a later frame so NGO/UGS state can advance between polls.</summary>
    public static async Task NextFrame()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        await Awaitable.NextFrameAsync();
#else
        await Task.Yield();
#endif
    }

    #endregion

    #region Helpers

#if UNITY_WEBGL && !UNITY_EDITOR
    private static async Task WaitRealtimeAsync(float seconds, CancellationToken cancellationToken)
    {
        // Real time, not Time.time: a paused timeScale must not stretch network timeouts.
        float end = Time.realtimeSinceStartup + seconds;

        while (Time.realtimeSinceStartup < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Awaitable.NextFrameAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
#endif

    #endregion
}
