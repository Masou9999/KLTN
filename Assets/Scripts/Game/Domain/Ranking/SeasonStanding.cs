using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain.Ranking
{
    /// <summary>One player's row in a season leaderboard.</summary>
    public sealed class SeasonStanding
    {
        /// <summary>Win rate is stored in basis points: 10000 = 100 %.</summary>
        public const int WinRateScale = 10000;

        /// <summary>
        /// Leaderboard order: points desc, then win rate desc, then username asc.
        /// Firestore sorts the same way on (points, winRate, username).
        /// </summary>
        public static readonly IComparer<SeasonStanding> LeaderboardOrder = new LeaderboardComparer();

        public string UserId { get; }
        public string Username { get; }
        public int Points { get; }
        public int Wins { get; }
        public int Losses { get; }
        public int Draws { get; }
        public int Matches { get; }

        public RankTier Tier => RankPolicy.TierFor(Points);

        /// <summary>Wins / matches (draws count as played); 0 before the first match.</summary>
        public double WinRate => (double)WinRateBasisPoints / WinRateScale;

        /// <summary>Stored "winRate" field: wins * 10000 / matches rounded down (same formula as the Rules).</summary>
        public int WinRateBasisPoints => WinRateOf(Wins, Matches);

        public SeasonStanding(
            string userId,
            string username,
            int points,
            int wins,
            int losses,
            int draws,
            int matches
        )
        {
            UserId = userId;
            Username = username ?? string.Empty;
            Points = Math.Max(0, points);
            Wins = Math.Max(0, wins);
            Losses = Math.Max(0, losses);
            Draws = Math.Max(0, draws);
            Matches = Math.Max(0, matches);
        }

        public static int WinRateOf(int wins, int matches)
        {
            return matches > 0 ? (int)((long)Math.Max(0, wins) * WinRateScale / matches) : 0;
        }

        private sealed class LeaderboardComparer : IComparer<SeasonStanding>
        {
            public int Compare(SeasonStanding left, SeasonStanding right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }

                if (left == null)
                {
                    return 1;
                }

                if (right == null)
                {
                    return -1;
                }

                int byPoints = right.Points.CompareTo(left.Points);

                if (byPoints != 0)
                {
                    return byPoints;
                }

                int byWinRate = right.WinRateBasisPoints.CompareTo(left.WinRateBasisPoints);
                return byWinRate != 0 ? byWinRate : string.CompareOrdinal(left.Username, right.Username);
            }
        }
    }

    /// <summary>Canonical public names and legacy "user" + 6 digit generation.</summary>
    public static class UsernamePolicy
    {
        #region Format

        public const string Prefix = "user";
        public const int DigitCount = 6;
        public const int MaxNumber = 999999;
        public const int MinLength = 4;
        public const int MaxLength = 16;

        #endregion

        #region Public API

        public static string Format(int number)
        {
            if (number < 0 || number > MaxNumber)
                throw new ArgumentOutOfRangeException(nameof(number));
            return Prefix + number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Preserves display casing and collapses ASCII spaces.</summary>
        public static string DisplayName(string username) =>
            string.Join(" ", (username ?? string.Empty).Split(
                new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Stable reservation key; existing lowercase keys remain unchanged.</summary>
        public static string Normalize(string username) => DisplayName(username).ToLowerInvariant();

        /// <summary>Reads new names and legacy lowercase names (including underscores).</summary>
        public static bool IsValid(string username)
        {
            if (username == null || username.Length < MinLength || username.Length > 20)
                return false;
            if (ValidateChoice(username) == null) return true;
            if (username[0] < 'a' || username[0] > 'z') return false;
            foreach (char c in username)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                    return false;
            return true;
        }

        /// <summary>Returns a stable error code for a newly chosen display name.</summary>
        public static string ValidateChoice(string username)
        {
            string name = DisplayName(username);
            if (name.Length < MinLength || name.Length > MaxLength) return "invalid_length";
            foreach (char c in name)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != ' ')
                    return "special_characters";
            if (name[0] >= '0' && name[0] <= '9') return "invalid_name";
            string key = Normalize(name);
            if (key == "admin" || key == "support" || key == "system" || key == "moderator")
                return "reserved_name";
            if (key.StartsWith(Prefix, StringComparison.Ordinal) && key.Length > Prefix.Length)
            {
                bool generated = true;
                for (int i = Prefix.Length; i < key.Length; i++)
                    if (key[i] < '0' || key[i] > '9') generated = false;
                if (generated) return "reserved_name";
            }
            return null;
        }

        public static bool CanChoose(string username) => ValidateChoice(username) == null;

        #endregion
    }

    /// <summary>
    /// Matchmaking prerequisite: players may only meet opponents within
    /// <see cref="MaxTierDifference"/> tiers (enforced by the matchmaker queue rule on
    /// Players.CustomData.rank_tier). Host/Client sessions bypass the matchmaker.
    /// </summary>
    public static class RankMatchmakingPolicy
    {
        public const int MaxTierDifference = 1;

        public static bool CanMeet(RankTier left, RankTier right)
        {
            return Math.Abs((int)left - (int)right) <= MaxTierDifference;
        }
    }
}
