using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KLTN.Infrastructure.CloudCode;
using Unity.Services.CloudCode;
using UnityEngine;

namespace KLTN.Infrastructure.Economy
{
    /// <summary>The durable receipt returned by the authoritative rename endpoint.</summary>
    [Serializable]
    public sealed class UsernameChangeResponse
    {
        public bool Success;
        public string Error;
        public string Username;
        public int UsernameVersion;
        public int UsernameChangeCount;
    }

    /// <summary>Retains a request ID across timeouts and restarts, separately for each UID.</summary>
    public static class UsernameChangeClient
    {
        public static async Task<UsernameChangeResponse> ChangeAsync(string uid, string name)
        {
            string key = "UsernameRename.v1." + uid;
            string pending = PlayerPrefs.GetString(key, "");
            string requestId;
            if (pending.Length > 33 && pending.Substring(33) == name &&
                Guid.TryParseExact(pending.Substring(0, 32), "N", out _))
            {
                requestId = pending.Substring(0, 32);
            }
            else
            {
                requestId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(key, requestId + ":" + name);
                PlayerPrefs.Save();
            }

            var call = CloudCodeService.Instance.CallModuleEndpointAsync<UsernameChangeResponse>(
                CloudCodeMatchResultReporter.ModuleName, "ChangeUsername",
                new Dictionary<string, object>
                {
                    ["requestId"] = requestId,
                    ["username"] = name,
                });
            if (await Task.WhenAny(call, FrameDelay.Delay(20000)) != call)
            {
                _ = call.ContinueWith(task => { _ = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("Rename result unknown; retry with the same request.");
            }
            var result = await call;
            if (result != null && !result.Success)
            {
                // A definitive rejection is complete. A later attempt gets a fresh ID.
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
            // Keep successful receipts locally too: an identical retry must not allocate a new request.
            return result ?? throw new InvalidOperationException("Empty rename response.");
        }
    }
}
