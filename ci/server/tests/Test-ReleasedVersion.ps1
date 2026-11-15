#Requires -Version 7.0
# Compile and exercise the production allocator resolver against fake HTTP responses.
# This needs no .NET SDK, Unity login, secrets, or network.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = '#nullable enable' + [Environment]::NewLine +
    (Get-Content (Join-Path $root 'CloudCode/EdgegapAllocator/Project/Client/ReleasedVersion.cs') -Raw)
$harness = @'

public static class ReleasedVersionChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        private readonly JObject app;
        private readonly JObject versions;
        private readonly JObject detail;
        public Handler(bool active, int count, bool ready, bool versionActive, bool more)
        {
            app = new JObject { ["is_active"] = active };
            var entries = new JArray();
            for (int i = 0; i < count; i++) entries.Add(new JObject { ["name"] = "sha-123456789abc" });
            versions = new JObject { ["versions"] = entries, ["total_count"] = count,
                ["pagination"] = new JObject { ["has_next"] = more } };
            detail = new JObject { ["is_active"] = versionActive,
                ["envs"] = new JArray(new JObject { ["key"] = "KLTN_RELEASE_READY", ["value"] = ready ? "true" : "false" }) };
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            JObject body = path.EndsWith("/versions") ? versions : path.Contains("/version/") ? detail : app;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new StringContent(body.ToString()) });
        }
    }

    public static int Run()
    {
        var cases = new (bool active, int count, bool ready, bool versionActive, bool more, bool succeeds)[] {
            (true, 1, true, true, false, true),
            (false, 1, true, true, false, false),
            (true, 0, true, true, false, false),
            (true, 2, true, true, false, false),
            (true, 1, false, true, false, false),
            (true, 1, true, false, false, false),
            (true, 1, true, true, true, false),
        };
        foreach (var test in cases)
        {
            using var client = new HttpClient(new Handler(test.active, test.count, test.ready, test.versionActive, test.more));
            bool succeeded = false;
            try {
                string version = ReleasedVersion.ResolveAsync(client, "https://example.invalid", "kltn").GetAwaiter().GetResult();
                if (version != "sha-123456789abc") throw new Exception("Wrong version selected");
                succeeded = true;
            } catch (InvalidOperationException) { }
            if (succeeded != test.succeeds) throw new Exception("Incorrect release gate decision");
        }
        return cases.Length;
    }
}
'@
$references = @(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName) +
    [Newtonsoft.Json.Linq.JObject].Assembly.Location
Add-Type -TypeDefinition ($source + $harness) -ReferencedAssemblies $references -CompilerOptions '/nowarn:1701'
$count = [EdgegapAllocatorModule.Client.ReleasedVersionChecks]::Run()
Write-Output "Allocator resolver: $count checks passed."
