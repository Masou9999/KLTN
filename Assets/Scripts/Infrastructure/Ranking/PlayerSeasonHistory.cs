using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.Domain.Ranking;

namespace KLTN.Infrastructure.Ranking
{
    public sealed class PlayerSeasonRow
    {
        public int SeasonId { get; }
        public SeasonStanding Standing { get; }

        public PlayerSeasonRow(int seasonId, SeasonStanding standing)
        {
            SeasonId = seasonId;
            Standing = standing;
        }
    }

    /// <summary>Reads only completed seasons, with bounded requests and no standing creation.</summary>
    public static class PlayerSeasonHistory
    {
        public const int PageSize = 5;

        public static List<SeasonWindow> Completed(
            IEnumerable<SeasonWindow> seasons, long nowUtcMs) =>
            seasons.Where(s => s.IsEnded(nowUtcMs))
                .OrderByDescending(s => s.SeasonId).ToList();

        public static async Task<RankingResult<IReadOnlyList<PlayerSeasonRow>>> ReadPageAsync(
            ILeaderboardGateway gateway, IReadOnlyList<SeasonWindow> seasons,
            int offset, string uid, CancellationToken cancellation)
        {
            if (string.IsNullOrEmpty(uid))
                return RankingResult<IReadOnlyList<PlayerSeasonRow>>.Fail(
                    RankingErrorCode.NotSignedIn, "Bạn chưa đăng nhập.");
            if (offset < 0 || offset > seasons.Count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            cancellation.ThrowIfCancellationRequested();
            int count = Math.Min(PageSize, seasons.Count - offset);
            var tasks = new Task<RankingResult<SeasonStanding>>[count];
            for (int i = 0; i < count; i++)
                tasks[i] = gateway.GetStandingAsync(seasons[offset + i].SeasonId, uid);
            var results = await Task.WhenAll(tasks);
            cancellation.ThrowIfCancellationRequested();
            var rows = new List<PlayerSeasonRow>();
            for (int i = 0; i < results.Length; i++)
            {
                if (!results[i].Success)
                    return RankingResult<IReadOnlyList<PlayerSeasonRow>>.Fail(
                        results[i].Error, results[i].Message);
                if (results[i].Value != null)
                    rows.Add(new PlayerSeasonRow(seasons[offset + i].SeasonId, results[i].Value));
            }
            return RankingResult<IReadOnlyList<PlayerSeasonRow>>.Ok(rows);
        }
    }
}
