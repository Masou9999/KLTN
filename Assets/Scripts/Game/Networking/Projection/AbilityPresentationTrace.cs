using System;
using System.Diagnostics;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    /// <summary>
    /// Records opt-in development timings from viewer-filtered updates. No player
    /// names, card identities, targets or payloads are written to the trace.
    /// </summary>
    public static class AbilityPresentationTrace
    {
        #region Configuration

        public static bool Enabled { get; set; } = Array.IndexOf(
            Environment.GetCommandLineArgs(), "-abilityPresentationTrace") >= 0;

        #endregion

        #region Timing Probes

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Record(MatchUpdateDto update, string phase,
            AbilityResolutionEventDto effect = null, int eventIndex = -1,
            int queueDepth = -1)
        {
            RecordSnapshot(update?.snapshot, phase, effect, eventIndex, queueDepth);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void RecordSnapshot(MatchSnapshotDto snapshot, string phase,
            AbilityResolutionEventDto effect = null, int eventIndex = -1,
            int queueDepth = -1)
        {
            if (!Enabled || snapshot == null) return;
            NetworkManager manager = NetworkManager.Singleton;
            double network = manager != null && manager.IsListening
                ? manager.ServerTime.Time : double.NaN;
            double monotonic = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            UnityEngine.Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[AbilityTiming] revision={0} viewer={1} event={2} "
                + "ability={3} actor={4} phase={5} frame={6} network={7:F6} "
                + "local={8:F6} queue={9} seq={10}",
                snapshot.revision, snapshot.viewerSeat, eventIndex,
                effect?.abilityId ?? "-", effect?.sourceOwner ?? -1, phase,
                Time.frameCount, network, monotonic, queueDepth,
                effect?.sequence ?? -1));
        }

        #endregion
    }
}
