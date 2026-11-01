using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KLTN.CloudCode.MatchServer
{
    // Wire shapes shared with the Unity match server and client (plan 01/02 contract in
    // 00_TongQuan_KeHoach.md). Property names are pinned so the serializer settings do not matter.

    public sealed class TicketTierResult
    {
        [JsonProperty("tier")] public int Tier { get; set; }
        [JsonProperty("seasonId")] public int SeasonId { get; set; }
    }

    public sealed class AdmitResult
    {
        [JsonProperty("admitted")] public bool Admitted { get; set; }

        /// <summary>"ok" or why the player was refused (unknown_match, bad_token, not_in_match, ...).</summary>
        [JsonProperty("reason")] public string Reason { get; set; }

        [JsonProperty("uid")] public string Uid { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("ownedCardIds")] public string[] OwnedCardIds { get; set; } = Array.Empty<string>();
        [JsonProperty("tier")] public int Tier { get; set; }

        public static AdmitResult Deny(string reason)
        {
            return new AdmitResult { Admitted = false, Reason = reason };
        }
    }

    public sealed class ReportResultResponse
    {
        [JsonProperty("alreadyReported")] public bool AlreadyReported { get; set; }
        [JsonProperty("players")] public List<PlayerSettlement> Players { get; set; } = new List<PlayerSettlement>();
    }

    public sealed class PlayerSettlement
    {
        [JsonProperty("playerId")] public string PlayerId { get; set; }

        /// <summary>"win", "loss", "draw", "surrender_early" (own surrender before round 3) or "surrender".</summary>
        [JsonProperty("result")] public string Result { get; set; }

        [JsonProperty("coins")] public int Coins { get; set; }
        [JsonProperty("silver")] public int Silver { get; set; }

        /// <summary>True when season points changed with this match.</summary>
        [JsonProperty("ranked")] public bool Ranked { get; set; }

        /// <summary>pointsAfter - pointsBefore (a loss at 10 points gives -10: points never go below 0).</summary>
        [JsonProperty("rankDelta")] public int RankDelta { get; set; }

        [JsonProperty("pointsBefore")] public int PointsBefore { get; set; }
        [JsonProperty("pointsAfter")] public int PointsAfter { get; set; }
        [JsonProperty("tierBefore")] public int TierBefore { get; set; }
        [JsonProperty("tierAfter")] public int TierAfter { get; set; }

        /// <summary>
        /// Null when scored, otherwise: unranked, no_active_season, started_before_season,
        /// started_after_lock, finished_after_season_end, cooldown, already_scored.
        /// </summary>
        [JsonProperty("notScoredReason")] public string NotScoredReason { get; set; }
    }

    /// <summary>GetAssignedMatches: matches newly placed on a running multi-match server.</summary>
    public sealed class AssignedMatchesResult
    {
        [JsonProperty("matches")] public List<AssignedMatch> Matches { get; set; } = new List<AssignedMatch>();
    }

    public sealed class AssignedMatch
    {
        [JsonProperty("matchId")] public string MatchId { get; set; }
        [JsonProperty("matchmakerMatchId")] public string MatchmakerMatchId { get; set; }

        /// <summary>Fresh MATCH_TOKEN; Firestore keeps only its SHA-256.</summary>
        [JsonProperty("matchToken")] public string MatchToken { get; set; }

        [JsonProperty("players")] public string[] Players { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// The report was refused for good (bad token, unknown match, player not in the match...).
    /// Retrying the same call cannot succeed; the message starts with "rejected:".
    /// </summary>
    public sealed class MatchRejectedException : Exception
    {
        public string Reason { get; }

        public MatchRejectedException(string reason)
            : base("rejected:" + reason)
        {
            Reason = reason;
        }
    }
}
