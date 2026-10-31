using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>
    /// Server-side state shared between DedicatedServerBootstrap (admission) and
    /// NetworkMatchBridge (loadout, result report). Empty on listen-host and clients.
    /// </summary>
    public static class MatchServerContext
    {
        #region State

        private static readonly Dictionary<ulong, PlayerAdmission> admissionByClient =
            new Dictionary<ulong, PlayerAdmission>();

        /// <summary>True only on a dedicated match server.</summary>
        public static bool IsDedicated { get; private set; }

        /// <summary>MATCH_ID of this container; used as the reported matchId.</summary>
        public static string MatchId { get; private set; }

        public static IMatchResultReporter Reporter { get; private set; } = new NullMatchResultReporter();

        /// <summary>Report in flight; the server waits for it before quitting.</summary>
        public static Task PendingReport { get; set; } = Task.CompletedTask;

        #endregion

        #region Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsDedicated = false;
            MatchId = null;
            Reporter = new NullMatchResultReporter();
            PendingReport = Task.CompletedTask;
            admissionByClient.Clear();
        }

        public static void StartDedicated(string matchId, IMatchResultReporter reporter)
        {
            IsDedicated = true;
            MatchId = matchId;
            Reporter = reporter ?? new NullMatchResultReporter();
            admissionByClient.Clear();
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

        public static int AdmittedCount => admissionByClient.Count;

        public static IEnumerable<string> AdmittedPlayerIds()
        {
            foreach (PlayerAdmission admission in admissionByClient.Values)
            {
                yield return admission.PlayerId;
            }
        }

        #endregion
    }
}
