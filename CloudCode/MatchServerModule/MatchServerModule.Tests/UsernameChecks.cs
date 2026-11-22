using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.CloudCode.MatchServer;
using KLTN.CloudCode.Shared;
using KLTN.Game.Domain.Ranking;

internal static class UsernameChecks
{
    public static async Task<int> Run(FirestoreAdmin fs, long now)
    {
        int failures = 0;
        void Check(bool ok, string name)
        {
            Console.WriteLine((ok ? "ok   " : "FAIL ") + "username: " + name);
            if (!ok) failures++;
        }
        async Task Put(string path, JsonObject fields) =>
            await fs.CommitAsync(new JsonArray { fs.Write(path, fields) });
        async Task Seed(string uid, string name)
        {
            await Put("users/" + uid, new JsonObject
            {
                ["username"] = FirestoreAdmin.String(name),
                ["coins"] = FirestoreAdmin.Integer(17),
                ["silver"] = FirestoreAdmin.Integer(4),
            });
            await Put("usernames/" + name, new JsonObject { ["uid"] = FirestoreAdmin.String(uid) });
        }
        Task<UsernameChangeResult> Rename(string uid, string id, string name, long at) =>
            UsernameService.ChangeAsync(fs, uid, id, name, () => at);
        string Id() => Guid.NewGuid().ToString("N");
        await Put("config/season", new JsonObject
        {
            ["anchorSeasonId"] = FirestoreAdmin.Integer(1),
            ["anchorStartMs"] = FirestoreAdmin.Integer(now - 60000),
            ["durationMs"] = FirestoreAdmin.Integer(120000),
            ["lockMs"] = FirestoreAdmin.Integer(1000),
            ["pendingSeasonId"] = FirestoreAdmin.Integer(2),
            ["pendingStartMs"] = FirestoreAdmin.Integer(now + 60000),
            ["pendingDurationMs"] = FirestoreAdmin.Integer(604800000),
            ["pendingLockMs"] = FirestoreAdmin.Integer(1800000),
        });
        Check(UsernamePolicy.Normalize("  AlEx 01 ") == "alex 01" &&
            UsernamePolicy.CanChoose("AlEx 01") && !UsernamePolicy.CanChoose("user123456") &&
            !UsernamePolicy.CanChoose("admin") && !UsernamePolicy.CanChoose("1alex") &&
            !UsernamePolicy.CanChoose("álex") && !UsernamePolicy.CanChoose("a/bc"),
            "canonical format and reserved names");
        await Seed("rename-a", "user900001");
        await Put("seasons/1/entries/rename-a", new JsonObject
        {
            ["username"] = FirestoreAdmin.String("user900001"),
            ["points"] = FirestoreAdmin.Integer(100),
            ["wins"] = FirestoreAdmin.Integer(3),
            ["updatedAt"] = FirestoreAdmin.Timestamp(now - 10000),
        });
        await Put("seasons/0/entries/rename-a", new JsonObject
        {
            ["username"] = FirestoreAdmin.String("user900001"),
        });
        string request = Id();
        var first = await Rename("rename-a", request, " AlEx 01 ", now);
        var retry = await Rename("rename-a", request, "AlEx 01", now + 1);
        Check(first.Success && retry.Success && retry.UsernameVersion == 1,
            "lost response replay returns durable result before allowance check");
        Check((await Rename("rename-a", request, "different", now)).Error == "request_id_conflict",
            "same request ID cannot bind another name");
        Check((await Rename("rename-a", Id(), "different", now)).Error == "already_changed",
            "one rename enforced");
        var (user, _) = await fs.GetAsync("users/rename-a");
        var profile = FirestoreAdmin.Fields(user);
        var (entry, _) = await fs.GetAsync("seasons/1/entries/rename-a");
        var row = FirestoreAdmin.Fields(entry);
        Check(FirestoreAdmin.GetLong(profile, "coins") == 17 &&
            FirestoreAdmin.GetLong(profile, "silver") == 4 &&
            FirestoreAdmin.GetString(profile, "initialUsername") == "user900001",
            "legacy profile migration preserves wallet and initial name");
        Check(FirestoreAdmin.GetString(row, "username") == "AlEx 01" &&
            FirestoreAdmin.GetLong(row, "points") == 100 &&
            FirestoreAdmin.ParseTimestamp(row["updatedAt"]) == now - 10000,
            "current standings preserve points and cooldown time");
        var (old, _) = await fs.GetAsync("seasons/0/entries/rename-a");
        Check(FirestoreAdmin.GetString(FirestoreAdmin.Fields(old), "username") == "user900001",
            "closed season untouched");
        var (reserved, _) = await fs.GetAsync("usernames/user900001");
        Check(reserved == null, "old reservation removed atomically");
        var (receipt, _) = await fs.GetAsync("users/rename-a/usernameChangeRequests/" + request);
        Check(FirestoreAdmin.GetString(FirestoreAdmin.Fields(receipt), "username") == "AlEx 01",
            "receipt persisted with profile");

        Check((await Rename("rename-a", request, "ALEX 01", now)).Error == "request_id_conflict",
            "receipt binds display casing as well as reservation key");
        var (canonical, _) = await fs.GetAsync("usernames/alex 01");
        Check(FirestoreAdmin.GetString(FirestoreAdmin.Fields(canonical), "uid") == "rename-a" &&
            FirestoreAdmin.GetString(profile, "usernameKey") == "alex 01",
            "display casing retained while reservation uses lowercase spaces");
        await Seed("rename-invalid", "user900020");
        foreach (var sample in new[] {
            ("abc", "invalid_length"), ("Abcd1234567890123", "invalid_length"),
            ("ADMIN", "reserved_name"), ("Support", "reserved_name"),
            ("1Alex", "invalid_name"), ("Alex_01", "special_characters") })
            Check((await Rename("rename-invalid", Id(), sample.Item1, now)).Error == sample.Item2,
                "backend validation: " + sample.Item1);
        await Seed("rename-legacy", "legacy_name_12345678");
        Check((await Rename("rename-legacy", Id(), "New Legacy", now)).Success,
            "legacy 20-character underscore profile remains renameable");
        await Seed("release-owner", "old name");
        await Seed("release-claimant", "user900030");
        string releaseRequest = Id();
        Check((await Rename("release-owner", releaseRequest, "New Name", now)).Success &&
            (await Rename("release-claimant", Id(), "OLD  NAME", now)).Success,
            "another account can claim released canonical name");
        Check((await Rename("release-owner", releaseRequest, "New Name", now + 1)).Success,
            "rename replay succeeds after old name belongs to another account");
        var (reclaimed, _) = await fs.GetAsync("usernames/old name");
        Check(FirestoreAdmin.GetString(FirestoreAdmin.Fields(reclaimed), "uid") == "release-claimant",
            "replay never deletes a reservation reclaimed by another account");
        await Seed("release-case", "same key");
        Check((await Rename("release-case", Id(), "Same Key", now)).Success,
            "case-only rename succeeds without releasing its own key");
        var (sameKey, _) = await fs.GetAsync("usernames/same key");
        Check(FirestoreAdmin.GetString(FirestoreAdmin.Fields(sameKey), "uid") == "release-case",
            "case-only rename retains reservation");
        await Seed("rename-b", "user900002");
        await Seed("rename-c", "user900003");
        string rejectedRequest = Id();
        Check((await Rename("rename-b", rejectedRequest, "AlEx 01", now)).Error == "name_taken" &&
            (await Rename("rename-b", rejectedRequest, "another name", now)).Error == "request_id_conflict",
            "even a rejected request ID stays bound to its original payload");
        var race = await Task.WhenAll(
            Rename("rename-b", Id(), "same name", now),
            Rename("rename-c", Id(), "SAME  NAME", now));
        Check(race.Count(r => r.Success) == 1 && race.Count(r => r.Error == "name_taken") == 1,
            "two accounts racing for one name");
        await Seed("rename-d", "user900004");
        var devices = await Task.WhenAll(
            Rename("rename-d", Id(), "device one", now),
            Rename("rename-d", Id(), "device two", now));
        Check(devices.Count(r => r.Success) == 1 &&
            devices.Count(r => r.Error == "already_changed") == 1,
            "two devices consume only one allowance");
        await Seed("rename-e", "user900005");
        string duplicate = Id();
        var duplicates = await Task.WhenAll(
            Rename("rename-e", duplicate, "duplicate", now),
            Rename("rename-e", duplicate, "duplicate", now));
        Check(duplicates.All(r => r.Success && r.UsernameVersion == 1),
            "concurrent identical requests return the same result");
        await Seed("rename-f", "user900006");
        Check((await Rename("rename-f", Id(), "user900006", now)).Success &&
            (await Rename("rename-f", Id(), "real name", now)).Success,
            "same name no-op does not consume allowance");
        await Seed("rename-boundary", "user900007");
        Check((await Rename("rename-boundary", Id(), "next season", now + 60000)).Success,
            "rename at pending season boundary");
        var (boundary, _) = await fs.GetAsync("users/rename-boundary");
        Check(SeasonDocuments.UsernameAtEnd(FirestoreAdmin.Fields(boundary), now + 60000) == "user900007" &&
            SeasonDocuments.UsernameVersionAtEnd(FirestoreAdmin.Fields(boundary), now + 60000) == 0 &&
            SeasonDocuments.UsernameAtEnd(FirestoreAdmin.Fields(boundary), now + 60001) == "next season",
            "historical name uses exclusive end boundary");
        var (newSeason, _) = await fs.GetAsync("seasons/2/entries/rename-boundary");
        Check(FirestoreAdmin.GetString(FirestoreAdmin.Fields(newSeason), "username") == "next season",
            "pending calendar selects new season");

        // Real settlement races rename. A serializable transaction must preserve both effects.
        await Seed("rename-settle", "user900008");
        await Seed("rename-opponent", "user900009");
        await PlayerDirectory.RecordAsync(fs, "rename-player", "rename-settle", now);
        await PlayerDirectory.RecordAsync(fs, "rename-other", "rename-opponent", now);
        string token = MatchRecords.NewToken();
        await MatchRecords.CreateAsync(fs, "rename-match", MatchRecords.HashToken(token),
            new[] { "rename-player", "rename-other" }, new[] { RankTier.Bronze, RankTier.Bronze },
            true, "rename-test", now - 10000);
        var settlement = MatchService.ReportAsync(fs, new MatchService.Report
        {
            MatchId = "rename-match", MatchToken = token,
            HostPlayerId = "rename-player", GuestPlayerId = "rename-other",
            Outcome = "HostWon", RoundNumber = 5, MatchStartedUtcMs = now - 5000,
        }, now);
        var rename = Rename("rename-settle", Id(), "Settled Name", now);
        await Task.WhenAll(settlement, rename);
        var (settled, _) = await fs.GetAsync("seasons/1/entries/rename-settle");
        Check(rename.Result.Success &&
            FirestoreAdmin.GetString(FirestoreAdmin.Fields(settled), "username") == "Settled Name" &&
            FirestoreAdmin.GetLong(FirestoreAdmin.Fields(settled), "usernameVersion") == 1 &&
            FirestoreAdmin.GetLong(FirestoreAdmin.Fields(settled), "points") == 30,
            "settlement and rename preserve newest name and awarded points");
        return failures;
    }
}
