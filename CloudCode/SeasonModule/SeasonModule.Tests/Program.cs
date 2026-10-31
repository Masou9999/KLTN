using System; using System.Net.Http; using System.Threading.Tasks; using KLTN.CloudCode.Season; using KLTN.CloudCode.Shared; using KLTN.Game.Domain.Ranking;
class P { static async Task<int> Main() {
  var http = new HttpClient();
  var fs = new FirestoreAdmin(http, null, "demo-kltn", "http://127.0.0.1:8080");
  await http.DeleteAsync("http://127.0.0.1:8080/emulator/v1/projects/demo-kltn/databases/(default)/documents");
  long H = 3600_000, T0 = 1_790_000_000_000;
  int fails = 0;
  void Check(bool ok, string name) { Console.WriteLine((ok ? "ok   " : "FAIL ") + name); if (!ok) fails++; }
  var legacy = new HttpRequestMessage(HttpMethod.Patch, "http://127.0.0.1:8080/v1/projects/demo-kltn/databases/(default)/documents/config/season") { Content = new StringContent("{\"fields\":{\"seasonId\":{\"integerValue\":\"1\"}}}", System.Text.Encoding.UTF8, "application/json") };
  legacy.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "owner");
  Check((await http.SendAsync(legacy)).IsSuccessStatusCode, "seeded config/season written by the first module version");
  var s1 = await SeasonRollover.RunAsync(fs, 24, 30, T0);
  Check(s1.Status == "created" && s1.SeasonId == 1 && s1.StartAt.EndsWith("T17:00:00Z"), $"first tick replaces legacy config with calendar, season 1 at VN midnight ({s1.StartAt} -> {s1.EndAt}, lock {s1.LockAt})");
  var r = await SeasonRollover.RunAsync(fs, 24, 30, T0 + H);
  Check(r.Status == "running" && r.SeasonId == 1, "tick with same settings changes nothing");
  long end1 = DateTimeOffset.Parse(s1.EndAt).ToUnixTimeMilliseconds();
  var late = await SeasonRollover.RunAsync(fs, 24, 30, end1 + 59 * 60_000);
  Check(late.Status == "running" && late.SeasonId == 2 && late.StartAt == s1.EndAt, $"late tick finds season 2 already running since {late.StartAt}");
  var (doc1, _) = await fs.GetAsync("seasons/1");
  Check(doc1?["fields"]?["closedAt"] != null, "season 1 history document closed");
  var a = SeasonRollover.RunAsync(fs, 1, 10, end1 + 2 * H); var b = SeasonRollover.RunAsync(fs, 1, 10, end1 + 2 * H);
  int upd = 0, other = 0; foreach (var t in new[] { a, b }) { try { if ((await t).Status == "updated") upd++; else other++; } catch (Exception e) { other++; Console.WriteLine("     rejected: " + e.Message.Substring(0, Math.Min(90, e.Message.Length))); } }
  Check(upd >= 1, $"settings change written once under concurrency (updated={upd}, other={other})");
  var s3 = await SeasonRollover.RunAsync(fs, 1, 10, end1 + 24 * H + 30 * 60_000);
  Check(s3.SeasonId == 3 && s3.EndAt == DateTimeOffset.FromUnixTimeMilliseconds(end1 + 25 * H).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), $"new 1h duration starts after season 2 ends: season {s3.SeasonId} {s3.StartAt} -> {s3.EndAt} lock {s3.LockAt}");
  var (cfg, _) = await fs.GetAsync("config/season");
  Check(cfg?["fields"]?["pendingSeasonId"]?["integerValue"]?.ToString() == "0" && cfg?["fields"]?["anchorSeasonId"]?["integerValue"]?.ToString() == "3", "pending calendar promoted (anchor = season 3)");
  // Roster: every username gets an empty row; old rows get a winRate; existing rows are kept.
  async Task Put(string path, string json) { var m = new HttpRequestMessage(HttpMethod.Patch, "http://127.0.0.1:8080/v1/projects/demo-kltn/databases/(default)/documents/" + path) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }; m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "owner"); (await http.SendAsync(m)).EnsureSuccessStatusCode(); }
  string Str(string v) => "{\"stringValue\":\"" + v + "\"}";
  await Put("usernames/user000001", "{\"fields\":{\"uid\":" + Str("u1") + "}}");
  await Put("usernames/user000002", "{\"fields\":{\"uid\":" + Str("u2") + "}}");
  await Put("users/u1", "{\"fields\":{\"username\":" + Str("user000001") + "}}");
  await Put("users/u2", "{\"fields\":{\"username\":" + Str("user000002") + "}}");
  await Put("users/u3", "{\"fields\":{\"coins\":{\"integerValue\":\"5\"}}}");
  await Put("users/u4", "{\"fields\":{\"coins\":{\"integerValue\":\"0\"}}}");
  await Put("seasons/3/entries/u2", "{\"fields\":{\"username\":" + Str("user000002") + ",\"points\":{\"integerValue\":\"30\"},\"wins\":{\"integerValue\":\"1\"},\"matches\":{\"integerValue\":\"3\"}}}");
  var names = new System.Collections.Generic.Queue<string>(new[] { "user000001", "user000777", "user000888" });
  int added = await SeasonRoster.FillAsync(fs, 3, end1 + 24 * H + 40 * 60_000, () => names.Dequeue());
  string F(System.Text.Json.Nodes.JsonObject d, string f) => d?["fields"]?[f]?["stringValue"]?.ToString() ?? d?["fields"]?[f]?["integerValue"]?.ToString();
  var (u1, _) = await fs.GetAsync("seasons/3/entries/u1");
  var (u2, _) = await fs.GetAsync("seasons/3/entries/u2");
  var (u3, _) = await fs.GetAsync("seasons/3/entries/u3");
  var (u4, _) = await fs.GetAsync("seasons/3/entries/u4");
  var (p3, _) = await fs.GetAsync("users/u3");
  var (n3, _) = await fs.GetAsync("usernames/user000777");
  Check(added == 6 && F(u1, "points") == "0" && F(u1, "winRate") == "0" && F(u1, "username") == "user000001", $"roster adds empty rows (0 points, 0 %) for every account (writes={added})");
  Check(F(u2, "points") == "30" && F(u2, "winRate") == "3333", "roster keeps existing row and fills its winRate (1/3 = 3333)");
  Check(F(u3, "username") == "user000777" && F(p3, "username") == "user000777" && F(p3, "coins") == "5" && F(n3, "uid") == "u3", "account without username gets one (taken name retried), profile fields kept");
  Check(F(u4, "username") == "user000888", "second account without username listed too");
  Check(await SeasonRoster.FillAsync(fs, 3, end1 + 25 * H) == 0, "roster run again writes nothing");
  return fails;
} }
