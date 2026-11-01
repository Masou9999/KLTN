using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using KLTN.CloudCode.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

namespace KLTN.CloudCode.MatchServer
{
    public sealed class ModuleConfig : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            config.AddGameApiClient();
            config.Dependencies.AddSingleton(http);
            config.Dependencies.AddSingleton<IPlayerTokenVerifier>(new UgsPlayerTokenVerifier(http));
            config.Dependencies.AddSingleton<IPlayerIdentityLookup>(new UgsPlayerAuthLookup(http));
        }
    }

    /// <summary>
    /// Server-trusted match flow (SDS 3.4.6 / 10.3):
    ///   GetTicketTier  player  → tier from season points, for the matchmaker ticket (FR-RANK-12)
    ///   AdmitPlayer    server  → checks MATCH_TOKEN + the player's UGS token, returns the loadout (FR-MM-09)
    ///   ReportResult   server  → rewards and season points once per match (FR-SHOP-08/09, FR-RANK-05..07/11)
    ///   GetAssignedMatches server → heartbeat + matches placed on a multi-match server (FR-MM-10)
    /// Server calls use a UGS service account (no PlayerId); a player calling them is refused here
    /// and should also be denied by an Access Control policy (README).
    /// </summary>
    public sealed class MatchServerFunctions
    {
        private const string SecretKey = "FIREBASE_SERVICE_ACCOUNT";

        private static GoogleServiceAccount account;

        private readonly IGameApiClient gameApiClient;
        private readonly HttpClient http;
        private readonly IPlayerTokenVerifier verifier;
        private readonly IPlayerIdentityLookup lookup;
        private readonly ILogger<MatchServerFunctions> logger;

        public MatchServerFunctions(
            IGameApiClient gameApiClient,
            HttpClient http,
            IPlayerTokenVerifier verifier,
            IPlayerIdentityLookup lookup,
            ILogger<MatchServerFunctions> logger
        )
        {
            this.gameApiClient = gameApiClient;
            this.http = http;
            this.verifier = verifier;
            this.lookup = lookup;
            this.logger = logger;
        }

        [CloudCodeFunction("GetTicketTier")]
        public async Task<TicketTierResult> GetTicketTier(IExecutionContext context)
        {
            if (string.IsNullOrEmpty(context.PlayerId))
            {
                throw new InvalidOperationException("GetTicketTier must be called by a signed-in player.");
            }

            FirestoreAdmin firestore = await FirestoreAsync(context);
            return await TicketTier.GetAsync(
                firestore,
                lookup,
                context.PlayerId,
                context.AccessToken,
                context.ProjectId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }

        [CloudCodeFunction("AdmitPlayer")]
        public async Task<AdmitResult> AdmitPlayer(
            IExecutionContext context,
            string matchId,
            string matchToken,
            string playerId,
            string playerAccessToken
        )
        {
            RequireServer(context, "AdmitPlayer");
            FirestoreAdmin firestore = await FirestoreAsync(context);
            AdmitResult result = await MatchService.AdmitAsync(
                firestore,
                verifier,
                lookup,
                context.ProjectId,
                matchId,
                matchToken,
                playerId,
                playerAccessToken,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            if (!result.Admitted)
            {
                logger.LogWarning("Match {MatchId}: player {PlayerId} refused ({Reason}).", matchId, playerId, result.Reason);
            }

            return result;
        }

        [CloudCodeFunction("ReportResult")]
        public async Task<ReportResultResponse> ReportResult(
            IExecutionContext context,
            string matchId,
            string matchToken,
            string hostPlayerId,
            string guestPlayerId,
            string outcome,
            string surrenderedPlayerId,
            int roundNumber,
            long matchStartedUtcMs
        )
        {
            RequireServer(context, "ReportResult");
            FirestoreAdmin firestore = await FirestoreAsync(context);

            try
            {
                ReportResultResponse response = await MatchService.ReportAsync(
                    firestore,
                    new MatchService.Report
                    {
                        MatchId = matchId,
                        MatchToken = matchToken,
                        HostPlayerId = hostPlayerId,
                        GuestPlayerId = guestPlayerId,
                        Outcome = outcome,
                        SurrenderedPlayerId = surrenderedPlayerId,
                        RoundNumber = roundNumber,
                        MatchStartedUtcMs = matchStartedUtcMs,
                    },
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );

                if (!response.AlreadyReported)
                {
                    logger.LogInformation("Match {MatchId} settled: {Outcome} after round {Round}.", matchId, outcome, roundNumber);
                }

                return response;
            }
            catch (MatchRejectedException exception)
            {
                logger.LogWarning("Match {MatchId}: report refused ({Reason}).", matchId, exception.Reason);
                throw;
            }
        }

        /// <summary>
        /// Heartbeat of a multi-match server (FR-MM-10): records capacity and running matches,
        /// returns matches the allocator placed on it with a fresh MATCH_TOKEN each. The server
        /// proves itself with SERVER_TOKEN (only its SHA-256 is in servers/{serverId}).
        /// </summary>
        [CloudCodeFunction("GetAssignedMatches")]
        public async Task<AssignedMatchesResult> GetAssignedMatches(
            IExecutionContext context,
            string serverId,
            string serverToken,
            int capacity,
            List<string> knownMatchIds,
            List<string> activeMatchIds,
            bool draining
        )
        {
            RequireServer(context, "GetAssignedMatches");
            FirestoreAdmin firestore = await FirestoreAsync(context);

            try
            {
                AssignedMatchesResult result = await ServerService.GetAssignedAsync(
                    firestore,
                    serverId,
                    serverToken,
                    capacity,
                    knownMatchIds,
                    activeMatchIds,
                    draining,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );

                foreach (AssignedMatch match in result.Matches)
                {
                    logger.LogInformation("Server {ServerId} picked up match {MatchId}.", serverId, match.MatchId);
                }

                return result;
            }
            catch (MatchRejectedException exception)
            {
                logger.LogWarning("Server {ServerId}: heartbeat refused ({Reason}).", serverId, exception.Reason);
                throw;
            }
        }

        #region Helpers

        private static void RequireServer(IExecutionContext context, string function)
        {
            // Service-account calls carry no player. Also deny player access in UGS Access Control.
            if (!string.IsNullOrEmpty(context.PlayerId))
            {
                throw new InvalidOperationException(function + " can only be called by the match server.");
            }
        }

        private async Task<FirestoreAdmin> FirestoreAsync(IExecutionContext context)
        {
            if (account == null)
            {
                Secret secret = await gameApiClient.SecretManager.GetSecret(context, SecretKey);
                account = GoogleServiceAccount.FromJson(secret.Value);
            }

            return new FirestoreAdmin(http, account);
        }

        #endregion
    }
}
