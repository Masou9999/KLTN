using System.Collections.Generic;
using System.Threading.Tasks;
using KLTN.Game.Domain.Ranking;

namespace KLTN.Infrastructure.Ranking
{
    public enum RankingErrorCode
    {
        None = 0,
        NotSignedIn,
        Network,
        PermissionDenied,
        Rejected,
        Unknown,
    }

    public sealed class RankingResult<T>
    {
        public bool Success { get; private set; }
        public RankingErrorCode Error { get; private set; }
        public string Message { get; private set; }
        public T Value { get; private set; }

        public static RankingResult<T> Ok(T value)
        {
            return new RankingResult<T> { Success = true, Value = value };
        }

        public static RankingResult<T> Fail(RankingErrorCode error, string message)
        {
            return new RankingResult<T> { Success = false, Error = error, Message = message };
        }
    }

    /// <summary>Position in the leaderboard order (points desc, winRate desc, username asc).</summary>
    public sealed class LeaderboardCursor
    {
        public int Points { get; }
        public int WinRate { get; }
        public string Username { get; }

        /// <summary>Full Firestore document name of the last row (final tie-breaker).</summary>
        public string DocumentName { get; }

        public LeaderboardCursor(int points, int winRate, string username, string documentName)
        {
            Points = points;
            WinRate = winRate;
            Username = username;
            DocumentName = documentName;
        }
    }

    public sealed class LeaderboardPage
    {
        public IReadOnlyList<SeasonStanding> Rows { get; }
        public bool HasMore { get; }
        public LeaderboardCursor Next { get; }

        public LeaderboardPage(IReadOnlyList<SeasonStanding> rows, bool hasMore, LeaderboardCursor next)
        {
            Rows = rows;
            HasMore = hasMore;
            Next = next;
        }
    }

    /// <summary>
    /// Persistence boundary for seasons and leaderboard rows.
    /// FirestoreLeaderboardGateway reads seasons and rows and creates the player's empty row.
    /// Ranked results are written only by Cloud Code ReportResult (FR-RANK-11).
    /// </summary>
    public interface ILeaderboardGateway
    {
        /// <summary>The running season from config/season (written by Cloud Code); null if none yet.</summary>
        Task<RankingResult<SeasonWindow?>> GetCurrentSeasonAsync();

        /// <summary>Every season document, newest first.</summary>
        Task<RankingResult<IReadOnlyList<SeasonWindow>>> ListSeasonsAsync();

        Task<RankingResult<LeaderboardPage>> GetPageAsync(int seasonId, LeaderboardCursor after, int pageSize);

        /// <summary>The user's row; Value is null when they have not played this season.</summary>
        Task<RankingResult<SeasonStanding>> GetStandingAsync(int seasonId, string userId);

        /// <summary>
        /// Own row in the running season, creating the empty one (0 points, Bronze, 0 %) when missing,
        /// so the player is listed before their first match.
        /// </summary>
        Task<RankingResult<SeasonStanding>> EnsureStandingAsync(int seasonId, string username);

        /// <summary>1-based position of <paramref name="standing"/> using COUNT aggregations.</summary>
        Task<RankingResult<int>> GetPositionAsync(int seasonId, SeasonStanding standing);
    }
}
