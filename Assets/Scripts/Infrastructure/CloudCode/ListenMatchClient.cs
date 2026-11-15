using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.Domain;
using Newtonsoft.Json.Linq;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>Player-authenticated registration and settlement for listen matches.</summary>
    public static class ListenMatchClient
    {
        #region Player Calls

        public static string PlayerId => AuthenticationService.Instance.PlayerId;

        public static Task<string> RegisterAsync(
            string matchId, string hostPlayerId, CancellationToken cancellationToken)
        {
            return CallAsync<string>("RegisterListenMatch", new Dictionary<string, object>
            {
                ["matchId"] = matchId,
                ["hostPlayerId"] = hostPlayerId,
            }, cancellationToken);
        }

        public static async Task<MatchSettlementResult> ReportAsync(
            string matchId, MatchOutcome outcome, SeatId? surrenderedSeat, int roundNumber,
            CancellationToken cancellationToken)
        {
            JObject response = await CallAsync<JObject>("ReportListenResult",
                new Dictionary<string, object>
                {
                    ["matchId"] = matchId,
                    ["outcome"] = outcome.ToString(),
                    ["hostSurrendered"] = surrenderedSeat == SeatId.Host,
                    ["guestSurrendered"] = surrenderedSeat == SeatId.Guest,
                    ["roundNumber"] = roundNumber,
                }, cancellationToken);
            if (!(response?["players"] is JArray players) || players.Count != 2)
                return MatchSettlementResult.Fail("Máy chủ trả kết quả không hợp lệ.");
            return CloudCodeMatchResultReporter.ParseSettlement(response);
        }

        #endregion

        #region Retry And Timeout

        /// <summary>Retries use the same match ID, so a lost response cannot pay twice.</summary>
        private static async Task<T> CallAsync<T>(
            string endpoint, Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task<T> call = CloudCodeService.Instance.CallModuleEndpointAsync<T>(
                    CloudCodeMatchResultReporter.ModuleName, endpoint, parameters);
                try
                {
                    if (await Task.WhenAny(call, Task.Delay(15000, cancellationToken)) != call)
                    {
                        ObserveFailure(call);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Máy chủ lưu kết quả phản hồi quá chậm.");
                    }
                    return await call;
                }
                catch when (attempt < 2 && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000 << attempt, cancellationToken);
                }
            }
        }

        private static void ObserveFailure(Task call)
        {
            call.ContinueWith(task => { _ = task.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        #endregion
    }
}
