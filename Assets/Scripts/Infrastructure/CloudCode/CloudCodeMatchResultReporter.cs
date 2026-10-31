using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>
    /// Calls MatchServerModule (AdmitPlayer, ReportResult) over the Cloud Code REST API with
    /// a stateless service-account token. Credentials come from the container environment
    /// (UGS_PROJECT_ID, UGS_ENVIRONMENT_ID, UGS_SERVICE_ACCOUNT_KEY_ID/SECRET) and MATCH_TOKEN.
    /// </summary>
    public sealed class CloudCodeMatchResultReporter : IMatchResultReporter
    {
        #region Constants

        public const string ModuleName = "MatchServerModule";

        private const string TokenExchangeUrl = "https://services.api.unity.com/auth/v1/token-exchange";
        private const string CloudCodeBaseUrl = "https://cloud-code.services.api.unity.com/v1/projects";

        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50);
        private static readonly int[] ReportRetryDelaysSeconds = { 2, 4, 8, 16 };

        #endregion

        #region Configuration

        private readonly string projectId;
        private readonly string environmentId;
        private readonly string serviceAccountKeyId;
        private readonly string serviceAccountSecret;
        private readonly string matchToken;
        private readonly HttpClient http;

        #endregion

        #region Token Cache

        private string accessToken;
        private DateTime accessTokenExpiresUtc;
        private readonly SemaphoreSlim tokenLock = new SemaphoreSlim(1, 1);

        #endregion

        public CloudCodeMatchResultReporter(
            string projectId,
            string environmentId,
            string serviceAccountKeyId,
            string serviceAccountSecret,
            string matchToken
        )
        {
            this.projectId = projectId;
            this.environmentId = environmentId;
            this.serviceAccountKeyId = serviceAccountKeyId;
            this.serviceAccountSecret = serviceAccountSecret;
            this.matchToken = matchToken;
            http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        public bool IsRanked => true;

        #region IMatchResultReporter

        public async Task<PlayerAdmission> AdmitPlayerAsync(
            string matchId,
            string playerId,
            string playerAccessToken,
            CancellationToken cancellationToken
        )
        {
            var parameters = new JObject
            {
                ["matchId"] = matchId,
                ["matchToken"] = matchToken,
                ["playerId"] = playerId,
                ["playerAccessToken"] = playerAccessToken,
            };

            CallResult call = await CallAsync("AdmitPlayer", parameters, cancellationToken);

            if (!call.Success)
            {
                return PlayerAdmission.Reject(playerId, "Không xác minh được người chơi: " + call.Message);
            }

            JToken output = call.Output;
            bool admitted = output.Value<bool?>("admitted") ?? false;

            if (!admitted)
            {
                return PlayerAdmission.Reject(playerId, output.Value<string>("reason") ?? "Bị từ chối.");
            }

            var owned = new List<string>();

            if (output["ownedCardIds"] is JArray ids)
            {
                foreach (JToken id in ids)
                {
                    string value = id.Type == JTokenType.String ? id.Value<string>() : null;

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        owned.Add(value);
                    }
                }
            }

            return new PlayerAdmission(
                true,
                string.Empty,
                playerId,
                output.Value<string>("uid"),
                output.Value<string>("username"),
                owned,
                output.Value<int?>("tier") ?? 0
            );
        }

        /// <summary>Retries with backoff (2/4/8/16 s); the same matchId keeps it idempotent.</summary>
        public async Task<MatchSettlementResult> ReportResultAsync(
            MatchResultReport report,
            CancellationToken cancellationToken
        )
        {
            var parameters = new JObject
            {
                ["matchId"] = report.MatchId,
                ["matchToken"] = matchToken,
                ["hostPlayerId"] = report.HostPlayerId,
                ["guestPlayerId"] = report.GuestPlayerId,
                ["outcome"] = MatchResultBannerPolicy.WireName(report.Outcome),
                ["surrenderedPlayerId"] = string.IsNullOrEmpty(report.SurrenderedPlayerId)
                    ? JValue.CreateNull()
                    : new JValue(report.SurrenderedPlayerId),
                ["roundNumber"] = report.RoundNumber,
                ["matchStartedUtcMs"] = report.MatchStartedUtcMs,
            };

            CallResult call = default;

            for (int attempt = 0; attempt <= ReportRetryDelaysSeconds.Length; attempt++)
            {
                if (attempt > 0)
                {
                    int delay = ReportRetryDelaysSeconds[attempt - 1];
                    Debug.LogWarning(
                        $"[CloudCode] ReportResult {report.MatchId} failed ({call.Message}); retry {attempt} in {delay} s."
                    );
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                }

                call = await CallAsync("ReportResult", parameters, cancellationToken);

                if (call.Success || !call.Retryable)
                {
                    break;
                }
            }

            if (!call.Success)
            {
                return MatchSettlementResult.Fail(call.Message);
            }

            return ParseSettlement(call.Output);
        }

        #endregion

        #region Parsing

        public static MatchSettlementResult ParseSettlement(JToken output)
        {
            var players = new List<PlayerSettlement>();

            if (output?["players"] is JArray rows)
            {
                foreach (JToken row in rows)
                {
                    players.Add(
                        new PlayerSettlement
                        {
                            PlayerId = row.Value<string>("playerId"),
                            Result = row.Value<string>("result"),
                            Coins = row.Value<int?>("coins") ?? 0,
                            Silver = row.Value<int?>("silver") ?? 0,
                            Ranked = row.Value<bool?>("ranked") ?? false,
                            RankDelta = row.Value<int?>("rankDelta") ?? 0,
                            PointsBefore = row.Value<int?>("pointsBefore") ?? 0,
                            PointsAfter = row.Value<int?>("pointsAfter") ?? 0,
                            TierBefore = row.Value<int?>("tierBefore") ?? 0,
                            TierAfter = row.Value<int?>("tierAfter") ?? 0,
                            NotScoredReason = row.Value<string>("notScoredReason"),
                        }
                    );
                }
            }

            return MatchSettlementResult.Ok(players, output?.Value<bool?>("alreadyReported") ?? false);
        }

        #endregion

        #region HTTP

        private struct CallResult
        {
            public bool Success;
            public bool Retryable;
            public string Message;
            public JToken Output;
        }

        private async Task<CallResult> CallAsync(
            string function,
            JObject parameters,
            CancellationToken cancellationToken
        )
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CallTimeout);

            try
            {
                string token = await GetAccessTokenAsync(timeout.Token);
                string url = $"{CloudCodeBaseUrl}/{projectId}/modules/{ModuleName}/{function}";
                var body = new JObject { ["params"] = parameters };

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
                string text = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    accessToken = null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    int status = (int)response.StatusCode;
                    return new CallResult
                    {
                        Success = false,
                        Retryable = status >= 500 || status == 401 || status == 408 || status == 429,
                        Message = $"HTTP {status} {Truncate(text)}",
                    };
                }

                JObject json = JObject.Parse(text);
                return new CallResult { Success = true, Output = json["output"] ?? new JObject() };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new CallResult { Success = false, Retryable = true, Message = "hết thời gian chờ" };
            }
            catch (HttpRequestException exception)
            {
                return new CallResult { Success = false, Retryable = true, Message = exception.Message };
            }
            catch (Newtonsoft.Json.JsonException exception)
            {
                return new CallResult { Success = false, Retryable = false, Message = exception.Message };
            }
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            await tokenLock.WaitAsync(cancellationToken);

            try
            {
                if (!string.IsNullOrEmpty(accessToken) && DateTime.UtcNow < accessTokenExpiresUtc)
                {
                    return accessToken;
                }

                string url = $"{TokenExchangeUrl}?projectId={Uri.EscapeDataString(projectId)}"
                    + $"&environmentId={Uri.EscapeDataString(environmentId)}";
                string basic = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{serviceAccountKeyId}:{serviceAccountSecret}")
                );

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
                string text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"token-exchange HTTP {(int)response.StatusCode} {Truncate(text)}");
                }

                accessToken = JObject.Parse(text).Value<string>("accessToken");

                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new HttpRequestException("token-exchange returned no accessToken.");
                }

                accessTokenExpiresUtc = DateTime.UtcNow + TokenLifetime;
                return accessToken;
            }
            finally
            {
                tokenLock.Release();
            }
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= 200 ? text : text.Substring(0, 200);
        }

        #endregion
    }
}
