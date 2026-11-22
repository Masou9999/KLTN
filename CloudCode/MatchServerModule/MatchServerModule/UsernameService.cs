using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.CloudCode.Shared;
using KLTN.Game.Domain.Ranking;

namespace KLTN.CloudCode.MatchServer
{
    public sealed class UsernameChangeResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Username { get; set; }
        public int UsernameVersion { get; set; }
        public int UsernameChangeCount { get; set; }
    }

    /// <summary>One atomic rename, including its durable idempotency receipt.</summary>
    public static class UsernameService
    {
        public static Task<UsernameChangeResult> ChangeAsync(
            FirestoreAdmin firestore, string uid, string requestId, string requestedName,
            Func<long> clock = null)
        {
            if (string.IsNullOrEmpty(uid) || uid.Contains('/'))
                throw new InvalidOperationException("Authenticated Firebase identity required.");
            if (!Guid.TryParseExact(requestId, "N", out _))
                return Task.FromResult(Fail("invalid_request"));
            string name = UsernamePolicy.DisplayName(requestedName);
            string key = UsernamePolicy.Normalize(name);
            if (name.Length > 128) return Task.FromResult(Fail("invalid_name"));
            clock ??= () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return firestore.RunTransactionAsync(async tx =>
            {
                string requestPath = $"users/{uid}/usernameChangeRequests/{requestId}";
                (JsonObject receipt, _) = await tx.GetAsync(requestPath);
                JsonObject stored = FirestoreAdmin.Fields(receipt);
                if (receipt != null)
                {
                    if (stored.ContainsKey("requestedDisplayName")
                        ? FirestoreAdmin.GetString(stored, "requestedDisplayName") != name
                        : FirestoreAdmin.GetString(stored, "normalizedUsername") != key)
                        return Fail("request_id_conflict");
                    return new UsernameChangeResult
                    {
                        Success = FirestoreAdmin.GetBool(stored, "success", true),
                        Error = FirestoreAdmin.GetString(stored, "error"),
                        Username = FirestoreAdmin.GetString(stored, "username"),
                        UsernameVersion = (int)FirestoreAdmin.GetLong(stored, "usernameVersion"),
                        UsernameChangeCount = (int)FirestoreAdmin.GetLong(stored, "usernameChangeCount"),
                    };
                }

                async Task<UsernameChangeResult> Finish(UsernameChangeResult result, JsonArray writes = null)
                {
                    writes ??= new JsonArray();
                    writes.Add(tx.Write(requestPath, new JsonObject
                    {
                        ["normalizedUsername"] = FirestoreAdmin.String(key),
                        ["requestedDisplayName"] = FirestoreAdmin.String(name),
                        ["success"] = FirestoreAdmin.Boolean(result.Success),
                        ["error"] = FirestoreAdmin.String(result.Error),
                        ["username"] = FirestoreAdmin.String(result.Username),
                        ["usernameVersion"] = FirestoreAdmin.Integer(result.UsernameVersion),
                        ["usernameChangeCount"] = FirestoreAdmin.Integer(result.UsernameChangeCount),
                        ["completedAt"] = FirestoreAdmin.Timestamp(clock()),
                    }, exists: false));
                    await tx.CommitAsync(writes);
                    return result;
                }

                (JsonObject user, _) = await tx.GetAsync($"users/{uid}");
                JsonObject profile = FirestoreAdmin.Fields(user);
                string oldName = FirestoreAdmin.GetString(profile, "username");
                if (!UsernamePolicy.IsValid(oldName)) return await Finish(Fail("profile_not_ready"));
                if (FirestoreAdmin.GetLong(profile, "usernameChangeCount") != 0 ||
                    FirestoreAdmin.GetLong(profile, "usernameVersion") != 0)
                    return await Finish(Fail("already_changed"));
                string validationError = UsernamePolicy.ValidateChoice(name);
                if (name != oldName && validationError != null)
                    return await Finish(Fail(validationError));
                string oldKey = UsernamePolicy.Normalize(oldName);
                (JsonObject reservation, _) = await tx.GetAsync($"usernames/{key}");
                if (key != oldKey && reservation != null) return await Finish(Fail("name_taken"));
                (JsonObject oldReservation, _) = key == oldKey
                    ? (reservation, (string)null)
                    : await tx.GetAsync($"usernames/{oldKey}");
                if (FirestoreAdmin.GetString(FirestoreAdmin.Fields(oldReservation), "uid") != uid)
                    return await Finish(Fail("profile_inconsistent"));

                (JsonObject configDocument, _) = await tx.GetAsync("config/season");
                SeasonConfig? config = SeasonDocuments.ParseConfig(configDocument);
                if (!config.HasValue) return await Finish(Fail("season_unavailable"));
                // Effective time is assigned by the backend on this transaction attempt.
                long now = clock();
                if (!config.Value.TryGetSeasonAt(now, out SeasonWindow season))
                    return await Finish(Fail("season_unavailable"));
                string entryPath = SeasonDocuments.EntryPath(season.SeasonId, uid);
                (JsonObject entry, _) = await tx.GetAsync(entryPath);
                bool changed = name != oldName;
                int version = changed ? 1 : 0;
                var writes = new JsonArray();
                if (changed)
                {
                    if (key != oldKey)
                    {
                        writes.Add(tx.Write($"usernames/{key}", new JsonObject
                        {
                            ["uid"] = FirestoreAdmin.String(uid),
                            ["status"] = FirestoreAdmin.String("active"),
                            ["createdAt"] = FirestoreAdmin.Timestamp(now),
                        }, exists: false));
                        writes.Add(tx.Delete($"usernames/{oldKey}"));
                    }
                    writes.Add(tx.Patch($"users/{uid}", new JsonObject
                    {
                        ["username"] = FirestoreAdmin.String(name),
                        ["usernameKey"] = FirestoreAdmin.String(key),
                        ["initialUsername"] = FirestoreAdmin.String(oldName),
                        ["usernameVersion"] = FirestoreAdmin.Integer(version),
                        ["usernameChangeCount"] = FirestoreAdmin.Integer(1),
                        ["usernameChangedAt"] = FirestoreAdmin.Timestamp(now),
                        ["updatedAt"] = FirestoreAdmin.Timestamp(now),
                    }));
                    JsonObject fields = new JsonObject
                    {
                        ["username"] = FirestoreAdmin.String(name),
                        ["usernameVersion"] = FirestoreAdmin.Integer(version),
                    };
                    if (entry == null)
                    {
                        foreach (string field in new[] { "points", "wins", "losses", "draws", "matches", "winRate" })
                            fields[field] = FirestoreAdmin.Integer(0);
                        fields["updatedAt"] = FirestoreAdmin.Timestamp(now);
                        writes.Add(tx.Write(entryPath, fields, exists: false));
                    }
                    else
                    {
                        // updatedAt is also the ranked-result cooldown clock. Never change it here.
                        writes.Add(tx.Patch(entryPath, fields));
                    }
                }
                return await Finish(new UsernameChangeResult
                {
                    Success = true, Username = name,
                    UsernameVersion = version, UsernameChangeCount = version,
                }, writes);
            });
        }

        private static UsernameChangeResult Fail(string reason) =>
            new UsernameChangeResult { Error = reason };
    }
}
