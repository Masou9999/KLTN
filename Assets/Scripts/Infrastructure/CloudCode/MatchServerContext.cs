using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KLTN.Game.ApplicationLayer;
using UnityEngine;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>
    /// Server-side state shared between DedicatedServerBootstrap (admission, allocation) and
    /// NetworkMatchBridge (seats, loadout, result report). Empty on listen-host and clients.
    /// </summary>
    public static class MatchServerContext
    {
        #region State

        private static readonly Dictionary<ulong, PlayerAdmission> admissionByClient =
            new Dictionary<ulong, PlayerAdmission>();

        // MATCH_TOKEN of every match this process hosts; only kept in memory (plan 01 contract).
        private static readonly Dictionary<string, string> tokenByMatch =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly List<Task> reports = new List<Task>();

        /// <summary>True only on a dedicated match server.</summary>
        public static bool IsDedicated { get; private set; }

        /// <summary>Matches hosted by this dedicated server (MATCH_CAPACITY of them at most).</summary>
        public static MatchSessionRegistry Sessions { get; private set; } = new MatchSessionRegistry(1);

        public static IMatchResultReporter Reporter { get; private set; } = new NullMatchResultReporter();

        #endregion

        #region Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsDedicated = false;
            Sessions = new MatchSessionRegistry(1);
            Reporter = new NullMatchResultReporter();
            admissionByClient.Clear();
            tokenByMatch.Clear();
            reports.Clear();
        }

        public static void StartDedicated(IMatchResultReporter reporter, int capacity)
        {
            IsDedicated = true;
            Sessions = new MatchSessionRegistry(capacity);
            Reporter = reporter ?? new NullMatchResultReporter();
            admissionByClient.Clear();
            tokenByMatch.Clear();
            reports.Clear();
        }

        #endregion

        #region Match Tokens

        public static void RegisterMatchToken(string matchId, string matchToken)
        {
            if (!string.IsNullOrEmpty(matchId) && !string.IsNullOrEmpty(matchToken))
            {
                tokenByMatch[matchId] = matchToken;
            }
        }

        /// <summary>MATCH_TOKEN proving this server hosts the match; null when unknown.</summary>
        public static string TokenFor(string matchId)
        {
            return matchId != null && tokenByMatch.TryGetValue(matchId, out string token) ? token : null;
        }

        #endregion

        #region Reports

        /// <summary>Remembers a report in flight; the server waits for all of them before quitting.</summary>
        public static void TrackReport(Task report)
        {
            if (report == null)
            {
                return;
            }

            reports.RemoveAll(task => task.IsCompleted);
            reports.Add(report);
        }

        public static Task AllReports()
        {
            reports.RemoveAll(task => task.IsCompleted);
            return reports.Count == 0 ? Task.CompletedTask : Task.WhenAll(reports.ToArray());
        }

        #endregion

        #region Admissions

        public static void Admit(ulong clientId, PlayerAdmission admission)
        {
            admissionByClient[clientId] = admission ?? throw new ArgumentNullException(nameof(admission));
        }

        public static void Forget(ulong clientId)
        {
            admissionByClient.Remove(clientId);
        }

        public static bool TryGetAdmission(ulong clientId, out PlayerAdmission admission)
        {
            return admissionByClient.TryGetValue(clientId, out admission);
        }

        #endregion
    }
}
