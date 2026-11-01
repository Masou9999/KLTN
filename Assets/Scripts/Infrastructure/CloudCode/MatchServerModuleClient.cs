using System;
using System.Threading.Tasks;
using KLTN.Game.Domain.Ranking;
using Unity.Services.CloudCode;
using UnityEngine;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>Player-side calls to MatchServerModule through the Cloud Code SDK.</summary>
    public static class MatchServerModuleClient
    {
        // A cold Cloud Code module can take ~10 s on its first call.
        private const int TicketTierTimeoutMilliseconds = 15000;

        [Serializable]
        private sealed class TicketTierResponse
        {
            public int tier;
            public int seasonId;
        }

        /// <summary>
        /// Tier for the matchmaker ticket, computed by the backend (FR-RANK-12). The call also
        /// links the UGS player to their Firebase account, and the allocator refuses players
        /// who skipped it, so a failure stops the search instead of queueing a doomed ticket.
        /// </summary>
        /// <exception cref="InvalidOperationException">The backend could not be reached in time.</exception>
        public static async Task<RankTier> GetTicketTierAsync()
        {
            Task<TicketTierResponse> call = CloudCodeService.Instance.CallModuleEndpointAsync<TicketTierResponse>(
                CloudCodeMatchResultReporter.ModuleName,
                "GetTicketTier"
            );
            Task finished = await Task.WhenAny(call, Task.Delay(TicketTierTimeoutMilliseconds));

            if (finished != call)
            {
                ObserveLateFailure(call);
                Debug.LogWarning("[Matchmaker] GetTicketTier timed out.");
                throw new InvalidOperationException("Máy chủ xếp hạng phản hồi quá chậm, hãy thử lại.");
            }

            TicketTierResponse response;

            try
            {
                response = await call;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Matchmaker] GetTicketTier failed: {exception.Message}");
                throw new InvalidOperationException("Không lấy được hạng từ máy chủ, hãy thử lại.", exception);
            }

            int tier = response?.tier ?? 0;
            return tier >= 0 && tier < RankPolicy.TierCount ? (RankTier)tier : RankTier.Bronze;
        }

        private static void ObserveLateFailure(Task call)
        {
            call.ContinueWith(
                task => Debug.LogWarning($"[Matchmaker] Late GetTicketTier failure: {task.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted
            );
        }
    }
}
