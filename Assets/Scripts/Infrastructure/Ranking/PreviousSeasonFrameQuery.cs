using System.Threading.Tasks;
using KLTN.Game.Domain.Ranking;

namespace KLTN.Infrastructure.Ranking
{
    public readonly struct ProfileFrameResult
    {
        public readonly int SourceSeasonId;
        public readonly long RefreshAtUtcMs;
        public readonly RankTier Tier;

        public ProfileFrameResult(int sourceSeasonId, long refreshAtUtcMs, RankTier tier)
        {
            SourceSeasonId = sourceSeasonId;
            RefreshAtUtcMs = refreshAtUtcMs;
            Tier = tier;
        }
    }

    /// <summary>Read-only cosmetic reward: final points from the immediately preceding season.</summary>
    public static class PreviousSeasonFrameQuery
    {
        public static async Task<RankingResult<ProfileFrameResult>> ReadAsync(ILeaderboardGateway gateway, string uid)
        {
            if (string.IsNullOrEmpty(uid))
                return RankingResult<ProfileFrameResult>.Fail(RankingErrorCode.NotSignedIn, "Bạn chưa đăng nhập.");

            var season = await gateway.GetCurrentSeasonAsync();
            if (!season.Success)
                return RankingResult<ProfileFrameResult>.Fail(season.Error, season.Message);

            int previousId = (season.Value?.SeasonId ?? 1) - 1;
            long refreshAt = season.Value?.EndUtcMs ?? 0;
            if (previousId < 1)
                return RankingResult<ProfileFrameResult>.Ok(new ProfileFrameResult(0, refreshAt, RankTier.Bronze));

            var standing = await gateway.GetStandingAsync(previousId, uid);
            if (!standing.Success)
                return RankingResult<ProfileFrameResult>.Fail(standing.Error, standing.Message);

            return RankingResult<ProfileFrameResult>.Ok(new ProfileFrameResult(
                previousId, refreshAt, standing.Value?.Tier ?? RankTier.Bronze));
        }
    }
}
