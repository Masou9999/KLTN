using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.CloudCode.MatchServer;
using KLTN.CloudCode.Shared;
using KLTN.Game.Domain.Ranking;

// Integration test against the Firestore emulator (see CloudCode/MatchServerModule/README.md).
// UGS is faked: player tokens are signed with a local RSA key served as the JWKS, and the
// oidc-firebase lookup is a dictionary. Times are explicit so cooldown/season cases are exact.
class P
{
    const string Project = "proj-kltn";
    const string Issuer = "https://player-auth.test";
    static readonly HttpClient http = new HttpClient();
    static FirestoreAdmin fs;
    static int fails;
    static RSA key = RSA.Create(2048);

    sealed class FakeLookup : IPlayerIdentityLookup
    {
        public readonly Dictionary<string, string> Links = new Dictionary<string, string>();
        public int Calls;

        public Task<string> GetFirebaseUidAsync(string playerId, string accessToken, string projectId)
        {
            Calls++;
            return Task.FromResult(Links.TryGetValue(playerId, out string uid) ? uid : null);
        }
    }

    static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + name);
        if (!ok) fails++;
    }

    static async Task<int> Main()
    {
        fs = new FirestoreAdmin(http, null, "demo-kltn", "http://127.0.0.1:8080");
        await http.DeleteAsync("http://127.0.0.1:8080/emulator/v1/projects/demo-kltn/databases/(default)/documents");

        long H = 3_600_000, M = 60_000;
        long T = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Season 1 = [T - 1h, T + 2h), points locked from T + 1.5h.
        await Put("config/season", new JsonObject
        {
            ["anchorSeasonId"] = FirestoreAdmin.Integer(1),
            ["anchorStartMs"] = FirestoreAdmin.Integer(T - H),
            ["durationMs"] = FirestoreAdmin.Integer(3 * H),
            ["lockMs"] = FirestoreAdmin.Integer(30 * M),
            ["pendingSeasonId"] = FirestoreAdmin.Integer(0),
        });
        await User("u1", "user000001", 0, 0, "MaDa", "MaDoi", "MaMatMam");
        await User("u2", "user000002", 100, 0, "MaDa");
        await User("u3", "user000003", 0, 0, "MaDa");
        await Put("seasons/1/entries/u2", Row("user000002", 160, 0, 0));  // Gold
        await Put("seasons/1/entries/u3", Row("user000003", 10, 0, 0));   // Bronze

        var lookup = new FakeLookup();
        lookup.Links["p1"] = "u1";
        lookup.Links["p2"] = "u2";
        lookup.Links["p3"] = "u3";
        string jwks = Jwks();
        var verifier = new UgsPlayerTokenVerifier(() => Task.FromResult(jwks), Issuer);

        // ---- Ticket tier (FR-RANK-12) ----
        TicketTierResult t1 = await TicketTier.GetAsync(fs, lookup, "p1", "tok", Project, T);
        TicketTierResult t2 = await TicketTier.GetAsync(fs, lookup, "p2", "tok", Project, T);
        Check(t1.Tier == 0 && t1.SeasonId == 1 && t2.Tier == (int)RankTier.Gold, $"GetTicketTier reads season points (p1 {t1.Tier}, p2 {t2.Tier}, season {t1.SeasonId})");
        Check(await PlayerDirectory.GetUidAsync(fs, "p1") == "u1", "GetTicketTier records ugsPlayers/p1 = u1");
        int calls = lookup.Calls;
        await TicketTier.GetAsync(fs, lookup, "p1", "tok", Project, T);
        Check(lookup.Calls == calls, "second GetTicketTier uses the stored link");
        Check(await Throws(() => TicketTier.GetAsync(fs, lookup, "nobody", "tok", Project, T), "no_firebase_link"), "player without Firebase link refused");
        Check(MatchRecords.TiersCanMeet(new[] { RankTier.Silver, RankTier.Gold }) && !MatchRecords.TiersCanMeet(new[] { RankTier.Bronze, RankTier.Gold }), "allocator tier rule: 1 tier apart ok, 2 apart refused");
        await TicketTier.GetAsync(fs, lookup, "p3", "tok", Project, T);
        (RankTier tierP3, _) = await SeasonDocuments.TierAsync(fs, await PlayerDirectory.GetUidAsync(fs, "p3"), T);
        Check(!MatchRecords.TiersCanMeet(new[] { tierP3, RankTier.Gold }), "allocator re-check from Firestore: Bronze p3 vs Gold p2 refused");

        // ---- Admission (FR-MM-09) ----
        string token = MatchRecords.NewToken();
        await MatchRecords.CreateAsync(fs, "m1", MatchRecords.HashToken(token), new[] { "p1", "p2" }, new[] { RankTier.Bronze, RankTier.Bronze }, true, "mm-1", T - 5 * M);
        Check(MatchRecords.HashToken(token).Length == 64 && token.Length >= 43, "token is >= 32 bytes base64url, Firestore keeps SHA-256 hex");
        Task<AdmitResult> Admit(string match, string tok, string player, string jwt) => MatchService.AdmitAsync(fs, verifier, lookup, Project, match, tok, player, jwt, T);
        string good = Jwt("p1", T / 1000 + 3600);
        Check((await Admit("nope", token, "p1", good)).Reason == "unknown_match", "unknown match refused");
        Check((await Admit("m1", "wrong", "p1", good)).Reason == "bad_token", "wrong match token refused");
        Check((await Admit("m1", token, "p3", Jwt("p3", T / 1000 + 3600))).Reason == "not_in_match", "player not in the match refused");
        Check((await Admit("m1", token, "p1", Jwt("p2", T / 1000 + 3600))).Reason == "bad_player_token:wrong_player", "token of another player refused");
        Check((await Admit("m1", token, "p1", Jwt("p1", T / 1000 - 600))).Reason == "bad_player_token:expired", "expired player token refused");
        Check((await Admit("m1", token, "p1", Jwt("p1", T / 1000 + 3600, project: "other"))).Reason == "bad_player_token:wrong_project", "token of another project refused");
        Check((await Admit("m1", token, "p1", Forged("p1", T / 1000 + 3600))).Reason == "bad_player_token:bad_signature", "forged signature refused");
        AdmitResult a1 = await Admit("m1", token, "p1", good);
        Check(a1.Admitted && a1.Uid == "u1" && a1.Username == "user000001" && string.Join(",", a1.OwnedCardIds) == "MaDa,MaDoi,MaMatMam" && a1.Tier == 0,
            $"valid player admitted with loadout ({string.Join(",", a1.OwnedCardIds)})");

        // ---- Settlement (FR-SHOP-08/09, FR-RANK-05..07) ----
        MatchService.Report Rep(string match, string tok, string outcome, string surrendered, int round, long started) => new MatchService.Report
        {
            MatchId = match, MatchToken = tok, HostPlayerId = "p1", GuestPlayerId = "p2",
            Outcome = outcome, SurrenderedPlayerId = surrendered, RoundNumber = round, MatchStartedUtcMs = started,
        };
        long now = T;
        Check(await Throws(() => MatchService.ReportAsync(fs, Rep("m1", "wrong", "HostWon", null, 5, T - 4 * M), now), "bad_token"), "report with wrong token refused");
        var outsider = Rep("m1", token, "HostWon", null, 5, T - 4 * M);
        outsider.GuestPlayerId = "p3";
        Check(await Throws(() => MatchService.ReportAsync(fs, outsider, now), "not_in_match"), "report naming a player outside the match refused");
        Check(await Throws(() => MatchService.ReportAsync(fs, Rep("m1", token, "Running", null, 5, T - 4 * M), now), "bad_outcome"), "running/unknown outcome refused");

        ReportResultResponse r1 = await MatchService.ReportAsync(fs, Rep("m1", token, "HostWon", null, 5, T - 4 * M), now);
        PlayerSettlement h = r1.Players[0], g = r1.Players[1];
        Check(!r1.AlreadyReported && h.Result == "win" && h.Coins == 75 && h.Silver == 1 && g.Result == "loss" && g.Coins == 25 && g.Silver == 0, "win: 75 coins + 1 silver, loss: 25 coins");
        Check(h.Ranked && h.RankDelta == 30 && h.PointsAfter == 30 && g.Ranked && g.PointsBefore == 160 && g.PointsAfter == 140 && g.TierBefore == 2 && g.TierAfter == 1, "points: +30 / -20, tier Gold -> Silver");
        Check(await Coins("u1") == (75, 1) && await Coins("u2") == (125, 0), "wallets incremented once");
        var (row1, _) = await fs.GetAsync("seasons/1/entries/u1");
        var f1 = FirestoreAdmin.Fields(row1);
        Check(FirestoreAdmin.GetLong(f1, "wins") == 1 && FirestoreAdmin.GetLong(f1, "matches") == 1 && FirestoreAdmin.GetLong(f1, "winRate") == 10000
            && FirestoreAdmin.GetString(f1, "lastMatchId") == "m1" && FirestoreAdmin.GetString(f1, "username") == "user000001", "season row created with counters, winRate and username");
        var (res1, _) = await fs.GetAsync("seasons/1/entries/u2/results/m1");
        var (rew1, _) = await fs.GetAsync("users/u1/matchRewards/m1");
        Check(FirestoreAdmin.GetString(FirestoreAdmin.Fields(res1), "result") == "loss" && FirestoreAdmin.GetLong(FirestoreAdmin.Fields(res1), "delta") == -20
            && FirestoreAdmin.GetLong(FirestoreAdmin.Fields(rew1), "coins") == 75, "results/{matchId} and matchRewards/{matchId} written");
        var (m1doc, _) = await fs.GetAsync("matches/m1");
        Check(FirestoreAdmin.Has(FirestoreAdmin.Fields(m1doc), "reportedAt") && FirestoreAdmin.GetString(FirestoreAdmin.Fields(m1doc), "settlement").Contains("\"rankDelta\":30"), "matches/m1 has reportedAt + settlement");

        ReportResultResponse again = await MatchService.ReportAsync(fs, Rep("m1", token, "GuestWon", null, 9, T - 4 * M), now + M);
        Check(again.AlreadyReported && again.Players[0].Coins == 75 && again.Players[0].Result == "win", "repeated report returns the stored settlement (alreadyReported)");
        Check(await Coins("u1") == (75, 1) && await Coins("u2") == (125, 0), "repeated report pays nothing twice");
        Check((await Admit("m1", token, "p1", good)).Reason == "match_finished", "finished match admits nobody");

        // Concurrent duplicate reports: exactly one settles.
        string tc = await NewMatch("mc", T - 3 * M);
        var parallel = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => MatchService.ReportAsync(fs, Rep("mc", tc, "Draw", null, 6, T - 2 * M), now + 2 * M)));
        Check(parallel.Count(x => !x.AlreadyReported) == 1, $"3 concurrent reports settle once ({parallel.Count(x => x.AlreadyReported)} saw alreadyReported)");
        Check(await Coins("u1") == (100, 1) && await Coins("u2") == (150, 0), "draw: 25 coins each, paid once");
        Check(parallel[0].Players.All(p => p.Ranked && p.RankDelta == 0 && p.Result == "draw"), "draw scores 0 points (counted as played)");

        // Cooldown: the next result within 60 s of the previous scored one is not scored (still rewarded).
        string tq = await NewMatch("mq", T - 3 * M);
        ReportResultResponse quick = await MatchService.ReportAsync(fs, Rep("mq", tq, "HostWon", null, 4, T - 2 * M), now + 2 * M + 30_000);
        Check(quick.Players.All(p => !p.Ranked && p.NotScoredReason == "cooldown") && quick.Players[0].Coins == 75, "result within 60 s: rewarded, not scored (cooldown)");

        // Early surrender (before round 3): the surrendering player gets nothing, the winner the full reward.
        string te = await NewMatch("me", T - M);
        ReportResultResponse early = await MatchService.ReportAsync(fs, Rep("me", te, "GuestWon", "p1", 2, T), now + 5 * M);
        Check(early.Players[0].Result == "surrender_early" && early.Players[0].Coins == 0 && early.Players[0].RankDelta == -20
            && early.Players[1].Result == "win" && early.Players[1].Coins == 75, "early surrender: 0 coins, -20 points; winner +75");

        // Floor at 0: p1 has 30 - 20 = 10 points; another loss leaves 0 (delta reported as -10).
        string tf = await NewMatch("mf", T + 6 * M);
        ReportResultResponse floor = await MatchService.ReportAsync(fs, Rep("mf", tf, "GuestWon", null, 5, T + 7 * M), now + 8 * M);
        Check(floor.Players[0].PointsBefore == 10 && floor.Players[0].PointsAfter == 0 && floor.Players[0].RankDelta == -10, "points never go below 0");

        // Late surrender (round >= 3): counts as a loss reward, "surrender" result.
        string tl = await NewMatch("ml", T + 9 * M);
        ReportResultResponse late = await MatchService.ReportAsync(fs, Rep("ml", tl, "HostWon", "p2", 3, T + 10 * M), now + 15 * M);
        Check(late.Players[1].Result == "surrender" && late.Players[1].Coins == 25 && late.Players[1].RankDelta == -20, "late surrender: 25 coins, -20 points");

        // Season window (FR-RANK-05): started before the season / after the lock / no season.
        string tb = await NewMatch("mb", T - H - 10 * M);
        ReportResultResponse before = await MatchService.ReportAsync(fs, Rep("mb", tb, "HostWon", null, 5, T - H - 5 * M), now + 20 * M);
        Check(before.Players.All(p => !p.Ranked && p.NotScoredReason == "started_before_season") && before.Players[0].Coins == 75, "match started before the season: rewarded, not scored");
        string ta = await NewMatch("ma", T + 95 * M);
        ReportResultResponse afterLock = await MatchService.ReportAsync(fs, Rep("ma", ta, "HostWon", null, 5, T + 96 * M), T + 100 * M);
        Check(afterLock.Players.All(p => p.NotScoredReason == "started_after_lock"), "match started after the points lock: not scored");
        string tw = await NewMatch("mw", T + 25 * M);
        ReportResultResponse wild = await MatchService.ReportAsync(fs, Rep("mw", tw, "HostWon", null, 5, T - 5 * H), now + 30 * M);
        Check(wild.Players[0].Ranked && wild.Players[0].RankDelta == 30, "impossible matchStartedUtcMs falls back to the allocation time");
        string tx = await NewMatch("mx", T + 30 * M);
        ReportResultResponse ended = await MatchService.ReportAsync(fs, Rep("mx", tx, "HostWon", null, 5, T + 31 * M), T + 2 * H + M);
        Check(ended.Players.All(p => !p.Ranked && p.NotScoredReason == "started_before_season"), "reported after its season ended: not scored in the next season");
        var drop = new HttpRequestMessage(HttpMethod.Delete, "http://127.0.0.1:8080/v1/projects/demo-kltn/databases/(default)/documents/config/season");
        drop.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "owner");
        (await http.SendAsync(drop)).EnsureSuccessStatusCode();
        string tn = await NewMatch("mn", T + 40 * M);
        ReportResultResponse noSeason = await MatchService.ReportAsync(fs, Rep("mn", tn, "HostWon", null, 5, T + 41 * M), now + 45 * M);
        Check(noSeason.Players.All(p => p.NotScoredReason == "no_active_season") && noSeason.Players[0].Coins == 75, "no season calendar yet: rewarded, not scored");

        // Unranked allocation.
        string tu = MatchRecords.NewToken();
        await MatchRecords.CreateAsync(fs, "mu", MatchRecords.HashToken(tu), new[] { "p1", "p2" }, new[] { RankTier.Bronze, RankTier.Bronze }, false, "mm-u", T + 35 * M);
        ReportResultResponse unranked = await MatchService.ReportAsync(fs, Rep("mu", tu, "HostWon", null, 5, T + 36 * M), now + 40 * M);
        Check(unranked.Players.All(p => !p.Ranked && p.NotScoredReason == "unranked") && unranked.Players[0].Coins == 75, "unranked match: rewarded, not scored");

        Console.WriteLine(fails == 0 ? "\nall passed" : $"\n{fails} failed");
        return fails;
    }

    #region Helpers

    static async Task<string> NewMatch(string id, long createdAt)
    {
        string tok = MatchRecords.NewToken();
        await MatchRecords.CreateAsync(fs, id, MatchRecords.HashToken(tok), new[] { "p1", "p2" }, new[] { RankTier.Bronze, RankTier.Bronze }, true, "mm-" + id, createdAt);
        return tok;
    }

    static Task Put(string path, JsonObject fields) => fs.CommitAsync(new JsonArray { fs.Write(path, fields) });

    static async Task User(string uid, string username, int coins, int silver, params string[] cards)
    {
        await Put("users/" + uid, new JsonObject
        {
            ["username"] = FirestoreAdmin.String(username),
            ["coins"] = FirestoreAdmin.Integer(coins),
            ["silver"] = FirestoreAdmin.Integer(silver),
        });

        foreach (string card in cards)
        {
            await Put($"users/{uid}/ownedCards/{card}", new JsonObject { ["source"] = FirestoreAdmin.String("starter") });
        }
    }

    static JsonObject Row(string username, int points, int wins, int matches) => new JsonObject
    {
        ["username"] = FirestoreAdmin.String(username),
        ["points"] = FirestoreAdmin.Integer(points),
        ["wins"] = FirestoreAdmin.Integer(wins),
        ["losses"] = FirestoreAdmin.Integer(matches - wins),
        ["draws"] = FirestoreAdmin.Integer(0),
        ["matches"] = FirestoreAdmin.Integer(matches),
        ["winRate"] = FirestoreAdmin.Integer(SeasonStanding.WinRateOf(wins, matches)),
        ["updatedAt"] = FirestoreAdmin.Timestamp(0),
    };

    static async Task<(long coins, long silver)> Coins(string uid)
    {
        var (user, _) = await fs.GetAsync("users/" + uid);
        var f = FirestoreAdmin.Fields(user);
        return (FirestoreAdmin.GetLong(f, "coins"), FirestoreAdmin.GetLong(f, "silver"));
    }

    static async Task<bool> Throws(Func<Task> call, string reason)
    {
        try
        {
            await call();
            return false;
        }
        catch (MatchRejectedException exception)
        {
            return exception.Reason == reason;
        }
    }

    static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string Jwks()
    {
        RSAParameters p = key.ExportParameters(false);
        return JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = "k1", alg = "RS256", n = B64(p.Modulus), e = B64(p.Exponent) } } });
    }

    static string Jwt(string sub, long exp, string project = Project, RSA signer = null)
    {
        string header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "k1", typ = "JWT" }));
        string claims = B64(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = Issuer, sub, project_id = project, aud = new[] { "upid:" + project, "envName:production" },
            exp, nbf = exp - 7200, iat = exp - 7200,
        }));
        byte[] signature = (signer ?? key).SignData(Encoding.ASCII.GetBytes(header + "." + claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + claims + "." + B64(signature);
    }

    static string Forged(string sub, long exp)
    {
        using RSA other = RSA.Create(2048);
        return Jwt(sub, exp, Project, other);
    }

    #endregion
}
