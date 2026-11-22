using KLTN.Infrastructure.Economy;
using Unity.Services.CloudCode;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
    Console.WriteLine("ok " + message);
}
var sdk = CloudCodeService.Instance;
sdk.Next = () => Task.FromException<object>(new IOException("lost response"));
try { await UsernameChangeClient.ChangeAsync("uidA", "alex"); }
catch (IOException) { }
string initialId = sdk.LastId;
Check(Guid.TryParseExact(initialId, "N", out _), "request ID persisted before network request");
sdk.Next = () => Task.FromResult<object>(
    new UsernameChangeResponse { Success = true, Username = "alex", UsernameVersion = 1 });
await UsernameChangeClient.ChangeAsync("uidA", "alex");
Check(sdk.LastId == initialId, "retry after lost response reuses request ID");
await UsernameChangeClient.ChangeAsync("uidA", "alex");
Check(sdk.LastId == initialId, "success replay retains original request ID");
await UsernameChangeClient.ChangeAsync("uidB", "alex");
Check(sdk.LastId != initialId, "request storage is isolated by UID");
await UsernameChangeClient.ChangeAsync("uidA", "other");
string changedId = sdk.LastId;
Check(changedId != initialId, "changed payload gets a different request ID");
sdk.Next = () => Task.FromResult<object>(new UsernameChangeResponse { Error = "name_taken" });
await UsernameChangeClient.ChangeAsync("uidA", "other");
Check(sdk.LastId == changedId, "pending matching payload still uses original attempt");
await UsernameChangeClient.ChangeAsync("uidA", "other");
Check(sdk.LastId != changedId, "definitive rejection permits a fresh attempt");
sdk.Next = () => Task.FromResult<object>(null);
try { await UsernameChangeClient.ChangeAsync("uidC", "unknown"); }
catch (InvalidOperationException) { }
string nullId = sdk.LastId;
sdk.Next = () => Task.FromResult<object>(new UsernameChangeResponse { Success = true });
await UsernameChangeClient.ChangeAsync("uidC", "unknown");
Check(sdk.LastId == nullId, "invalid/empty response retains the request for reconciliation");
Console.WriteLine($"{passed} passed, 0 failed");

namespace UnityEngine
{
    // In-memory replacement: tests never touch real PlayerPrefs or a real account.
    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> Values = new();
        public static string GetString(string key, string fallback) =>
            Values.TryGetValue(key, out string value) ? value : fallback;
        public static void SetString(string key, string value) => Values[key] = value;
        public static void DeleteKey(string key) => Values.Remove(key);
        public static void Save() { }
    }
}

namespace KLTN.Infrastructure.CloudCode
{
    public static class CloudCodeMatchResultReporter
    {
        public const string ModuleName = "MatchServerModule";
    }
}

namespace Unity.Services.CloudCode
{
    public sealed class CloudCodeService
    {
        public static CloudCodeService Instance { get; } = new();
        public Func<Task<object>> Next;
        public string LastId;
        public async Task<T> CallModuleEndpointAsync<T>(
            string module, string endpoint, Dictionary<string, object> payload)
        {
            if (module != "MatchServerModule" || endpoint != "ChangeUsername")
                throw new Exception("Incorrect endpoint.");
            if (payload.Count != 2 || !payload.ContainsKey("username"))
                throw new Exception("Unexpected identity or credential in payload.");
            LastId = (string)payload["requestId"];
            return (T)await Next();
        }
    }
}
