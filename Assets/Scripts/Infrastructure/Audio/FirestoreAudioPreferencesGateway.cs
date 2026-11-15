using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KLTN.Audio;
using KLTN.Infrastructure.Firestore;
using Newtonsoft.Json.Linq;

namespace KLTN.Infrastructure.Audio
{
    /// <summary>Reads and writes the owner's audio document without touching profile data.</summary>
    public sealed class FirestoreAudioPreferencesGateway
    {
        private readonly FirestoreRestClient client = new FirestoreRestClient();

        public async Task<AudioPreferences> LoadAsync(string uid)
        {
            string token = await TokenAsync(uid);
            var response = await client.GetDocumentAsync(Path(uid), token);
            if (response.IsNotFound) return null;
            if (!response.Success) throw new InvalidOperationException(response.ErrorStatus);
            JToken fields = response.Body["fields"];
            var value = new AudioPreferences
            {
                music = int.Parse((string)fields["music"]["integerValue"]),
                ui = int.Parse((string)fields["ui"]["integerValue"]),
                combat = int.Parse((string)fields["combat"]["integerValue"]),
                musicMuted = (bool)fields["musicMuted"]["booleanValue"],
                uiMuted = (bool)fields["uiMuted"]["booleanValue"],
            };
            if (!value.IsValid()) throw new InvalidOperationException("Invalid audio settings");
            return value;
        }

        public async Task SaveAsync(string uid, AudioPreferences value)
        {
            string token = await TokenAsync(uid);
            var fields = new Dictionary<string, object>
            {
                ["music"] = value.music, ["ui"] = value.ui,
                ["combat"] = value.combat, ["musicMuted"] = value.musicMuted,
                ["uiMuted"] = value.uiMuted,
            };
            JObject write = client.CreateWrite(Path(uid), fields);
            // Absolute replacement is safe for both first save and later updates.
            write.Remove("currentDocument");
            var response = await client.CommitAsync(new[] { write }, token);
            if (!response.Success) throw new InvalidOperationException(response.ErrorStatus);
        }

        private static string Path(string uid) => "users/" + uid + "/settings/audio";

        private static async Task<string> TokenAsync(string uid)
        {
            var auth = AuthManager.Instance;
            if (auth == null || auth.CurrentUserId != uid)
                throw new InvalidOperationException("Account changed");
            string token = await auth.GetValidIdTokenAsync();
            if (auth.CurrentUserId != uid || string.IsNullOrEmpty(token))
                throw new InvalidOperationException("Account changed");
            return token;
        }
    }
}
