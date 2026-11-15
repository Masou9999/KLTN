using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EdgegapAllocatorModule.Client;

/// <summary>Fail closed while the single-version CI release is in progress.</summary>
public static class ReleasedVersion
{
    public const string ReadyVariable = "KLTN_RELEASE_READY";

    public static async Task<string> ResolveAsync(HttpClient client, string apiUrl, string appName)
    {
        string appUrl = $"{apiUrl}/v1/app/{Uri.EscapeDataString(appName)}";
        JObject app = await ReadAsync(client, appUrl);
        if (app.Value<bool?>("is_active") != true)
            throw new InvalidOperationException("Server maintenance in progress; retry matchmaking later.");

        JObject list = await ReadAsync(client, appUrl + "/versions?limit=2&page=1");
        if (list["versions"] is not JArray versions || versions.Count != 1
            || list.Value<int?>("total_count") != 1
            || list["pagination"]?.Value<bool?>("has_next") == true)
            throw new InvalidOperationException("Expected exactly one released server version.");

        string? name = versions[0].Value<string>("name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Server version has no name.");
        JObject version = await ReadAsync(client, appUrl + "/version/" + Uri.EscapeDataString(name));
        if (version.Value<bool?>("is_active") != true
            || version["envs"] is not JArray envs
            || !envs.Any(env => env.Value<string>("key") == ReadyVariable
                && env.Value<string>("value") == "true"))
            throw new InvalidOperationException("Server release verification in progress; retry matchmaking later.");
        return name;
    }

    private static async Task<JObject> ReadAsync(HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(url);
        // Do not put API response bodies (possibly credentials) in allocation errors.
        response.EnsureSuccessStatusCode();
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }
}
