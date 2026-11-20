using System.Threading.Tasks;
using KLTN.CloudCode.Shared;
using Newtonsoft.Json;

namespace KLTN.CloudCode.MatchServer
{
    /// <summary>Explicit allowlist of public profile fields shared with an opponent.</summary>
    public sealed class PublicMatchProfile
    {
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("points")] public int Points { get; set; } = -1;
        [JsonProperty("seasonId")] public int SeasonId { get; set; }

        public static async Task<PublicMatchProfile> ReadAsync(
            FirestoreAdmin firestore, string uid, long nowUtcMs)
        {
            if (string.IsNullOrEmpty(uid))
                throw new MatchRejectedException("no_firebase_link");
            var (user, _) = await firestore.GetAsync("users/" + uid);
            if (user == null) throw new MatchRejectedException("no_profile");
            var season = await SeasonDocuments.RunningAsync(firestore, nowUtcMs);
            return new PublicMatchProfile
            {
                Username = FirestoreAdmin.GetString(
                    FirestoreAdmin.Fields(user), "username") ?? string.Empty,
                Points = season.HasValue
                    ? await SeasonDocuments.PointsAsync(firestore, season, uid) : -1,
                SeasonId = season?.SeasonId ?? 0,
            };
        }
    }

    /// <summary>Roster order is authoritative: host first, guest second.</summary>
    public sealed class ListenProfilesResult
    {
        [JsonProperty("host")] public PublicMatchProfile Host { get; set; }
        [JsonProperty("guest")] public PublicMatchProfile Guest { get; set; }
    }
}
