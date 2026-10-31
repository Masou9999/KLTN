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
        private const int TicketTierTimeoutMilliseconds = 5000;

        [Serializable]
        private sealed class TicketTierResponse
        {
            public int tier;
            public int seasonId;
        }

        /// <summary>
        /// Tier for the matchmaker ticket, computed by the backend (FR-RANK-12). The allocator
        /// checks it again, so a slow or failed lookup simply searches as Bronze.
        /// </summary>
        public static async Task<RankTier> GetTicketTierAsync()
        {
            try
            {
                Task<TicketTierResponse> call = CloudCodeService.Instance.CallModuleEndpointAsync<TicketTierResponse>(
                    CloudCodeMatchResultReporter.ModuleName,
                    "GetTicketTier"
                );
                Task finished = await Task.WhenAny(call, Task.Delay(TicketTierTimeoutMilliseconds));

                if (finished != call)
                {
                    Debug.LogWarning("[Matchmaker] GetTicketTier timed out, searching as Bronze.");
                    return RankTier.Bronze;
                }

                int tier = call.Result?.tier ?? 0;
                return tier >= 0 && tier < RankPolicy.TierCount ? (RankTier)tier : RankTier.Bronze;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Matchmaker] GetTicketTier failed, searching as Bronze: {exception.Message}");
                return RankTier.Bronze;
            }
        }
    }
}
