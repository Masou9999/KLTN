#nullable disable
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.Game.Domain.Ranking;

namespace KLTN.CloudCode.Shared
{
    /// <summary>
    /// Reads config/season (written by SeasonModule) and season rows. The running season is a
    /// function of the calendar and the clock, the same formula the client and the Rules use.
    /// </summary>
    public static class SeasonDocuments
    {
        public const string ConfigPath = "config/season";

        public static SeasonConfig? ParseConfig(JsonObject document)
        {
            JsonObject fields = FirestoreAdmin.Fields(document);

            if (FirestoreAdmin.GetLong(fields, "anchorSeasonId") < 1 || FirestoreAdmin.GetLong(fields, "durationMs") <= 0)
            {
                return null;
            }

            var active = new SeasonCalendar(
                (int)FirestoreAdmin.GetLong(fields, "anchorSeasonId"),
                FirestoreAdmin.GetLong(fields, "anchorStartMs"),
                FirestoreAdmin.GetLong(fields, "durationMs"),
                FirestoreAdmin.GetLong(fields, "lockMs")
            );

            int pendingId = (int)FirestoreAdmin.GetLong(fields, "pendingSeasonId");

            if (pendingId <= 0)
            {
                return new SeasonConfig(active);
            }

            var pending = new SeasonCalendar(
                pendingId,
                FirestoreAdmin.GetLong(fields, "pendingStartMs"),
                FirestoreAdmin.GetLong(fields, "pendingDurationMs"),
                FirestoreAdmin.GetLong(fields, "pendingLockMs")
            );
            return new SeasonConfig(active, pending);
        }

        /// <summary>The season running at <paramref name="nowUtcMs"/>, or null before the first tick.</summary>
        public static async Task<SeasonWindow?> RunningAsync(FirestoreAdmin firestore, long nowUtcMs)
        {
            (JsonObject document, _) = await firestore.GetAsync(ConfigPath);
            SeasonConfig? config = document == null ? null : ParseConfig(document);

            if (config.HasValue && config.Value.TryGetSeasonAt(nowUtcMs, out SeasonWindow window))
            {
                return window;
            }

            return null;
        }

        public static string EntryPath(int seasonId, string uid)
        {
            return $"seasons/{seasonId.ToString(CultureInfo.InvariantCulture)}/entries/{uid}";
        }

        public static string ResultPath(int seasonId, string uid, string matchId)
        {
            return $"{EntryPath(seasonId, uid)}/results/{matchId}";
        }

        /// <summary>Season points of a player (0 when the row is missing or there is no season).</summary>
        public static async Task<int> PointsAsync(FirestoreAdmin firestore, SeasonWindow? season, string uid)
        {
            if (!season.HasValue)
            {
                return 0;
            }

            (JsonObject entry, _) = await firestore.GetAsync(EntryPath(season.Value.SeasonId, uid));
            return (int)FirestoreAdmin.GetLong(FirestoreAdmin.Fields(entry), "points");
        }

        /// <summary>Tier from season points (Appendix B, <see cref="RankPolicy"/>).</summary>
        public static async Task<(RankTier tier, int seasonId)> TierAsync(FirestoreAdmin firestore, string uid, long nowUtcMs)
        {
            SeasonWindow? season = await RunningAsync(firestore, nowUtcMs);
            int points = await PointsAsync(firestore, season, uid);
            return (RankPolicy.TierFor(points), season?.SeasonId ?? 0);
        }
    }
}
