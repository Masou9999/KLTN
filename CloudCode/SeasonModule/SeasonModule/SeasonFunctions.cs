using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.CloudCode.Shared;
using KLTN.Game.Domain.Ranking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

namespace KLTN.CloudCode.Season
{
    public sealed class ModuleConfig : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            config.AddGameApiClient();
            config.Dependencies.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        }
    }

    public sealed class SeasonState
    {
        public string Status { get; set; }
        public int SeasonId { get; set; }
        public string StartAt { get; set; }
        public string EndAt { get; set; }
        public string LockAt { get; set; }

        /// <summary>Empty rows created (or rows given a winRate) for the running season this tick.</summary>
        public int RosterRows { get; set; }
    }

    /// <summary>
    /// Season tick. Called by a UGS Scheduler event (hourly is the minimum UGS allows).
    /// Idempotent; a late or repeated tick is harmless because seasons follow the clock.
    ///
    ///   config/season       anchorSeasonId, anchorStartMs, durationMs, lockMs,
    ///                       pendingSeasonId/StartMs/DurationMs/LockMs (0 = none), updatedAt
    ///   seasons/{seasonId}  seasonId, startAt, endAt, lockAt, closedAt? (history list)
    ///   seasons/{id}/entries/{uid}  empty row (0 points) for every users/{uid}, so the
    ///                       leaderboard lists every account (old accounts get a username first).
    /// </summary>
    public sealed class SeasonFunctions
    {
        private const string SecretKey = "FIREBASE_SERVICE_ACCOUNT";
        private const double DefaultDurationHours = 24;
        private const int DefaultLockMinutes = 30;

        private static GoogleServiceAccount account;

        private readonly ILogger<SeasonFunctions> logger;

        public SeasonFunctions(ILogger<SeasonFunctions> logger)
        {
            this.logger = logger;
        }

        [CloudCodeFunction("AdvanceSeason")]
        public async Task<SeasonState> AdvanceSeason(
            IExecutionContext context,
            IGameApiClient gameApiClient,
            HttpClient http,
            double durationHours,
            int lockMinutes
        )
        {
            // Players must not drive the season clock (they could pick a tiny duration).
            // Scheduler-triggered calls carry no player. Also deny player access in UGS Access Control.
            if (!string.IsNullOrEmpty(context.PlayerId))
            {
                throw new InvalidOperationException("AdvanceSeason can only be called by the scheduler.");
            }

            if (durationHours <= 0)
            {
                durationHours = DefaultDurationHours;
            }

            if (lockMinutes < 0)
            {
                lockMinutes = DefaultLockMinutes;
            }

            GoogleServiceAccount serviceAccount = await GetAccountAsync(context, gameApiClient);
            var firestore = new FirestoreAdmin(http, serviceAccount);
            SeasonState state = await SeasonRollover.RunAsync(
                firestore,
                durationHours,
                lockMinutes,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            try
            {
                state.RosterRows = await SeasonRoster.FillAsync(
                    firestore,
                    state.SeasonId,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );
            }
            catch (Exception exception)
            {
                // The calendar is already saved; the roster is retried next tick.
                logger.LogError(exception, "Season {SeasonId} roster fill failed.", state.SeasonId);
            }

            if (state.RosterRows > 0)
            {
                logger.LogInformation("Season {SeasonId}: {Rows} leaderboard rows or usernames written.", state.SeasonId, state.RosterRows);
            }

            if (state.Status != "running")
            {
                logger.LogInformation(
                    "Season calendar {Status}; running season {SeasonId}: {Start} -> {End} (lock {Lock}).",
                    state.Status,
                    state.SeasonId,
                    state.StartAt,
                    state.EndAt,
                    state.LockAt
                );
            }

            return state;
        }

        #region Helpers

        private static async Task<GoogleServiceAccount> GetAccountAsync(IExecutionContext context, IGameApiClient api)
        {
            if (account != null)
            {
                return account;
            }

            Secret secret = await api.SecretManager.GetSecret(context, SecretKey);
            account = GoogleServiceAccount.FromJson(secret.Value);
            return account;
        }

        #endregion
    }

    /// <summary>
    /// Tick logic, independent of the Cloud Code runtime (testable against the emulator).
    /// The season itself is derived from config/season and the clock (see SeasonCalendar),
    /// so a late tick never delays a reset; the tick only applies setting changes and keeps
    /// seasons/{id} documents for the history list.
    /// </summary>
    public static class SeasonRollover
    {
        public static async Task<SeasonState> RunAsync(FirestoreAdmin firestore, double durationHours, int lockMinutes, long now)
        {
            long durationMs = (long)Math.Round(durationHours * 3_600_000d);
            long lockMs = lockMinutes * 60_000L;

            (JsonObject document, string updateTime) = await firestore.GetAsync("config/season");
            SeasonConfig? current = document == null ? null : SeasonDocuments.ParseConfig(document);
            SeasonConfig next = SeasonSchedule.Plan(current, now, durationMs, lockMs);
            var writes = new JsonArray();
            string status = "running";

            if (!current.HasValue)
            {
                // Missing, or written by the first (tick-based) version of this module: replace it.
                writes.Add(document == null
                    ? firestore.Write("config/season", ConfigFields(next, now), exists: false)
                    : firestore.Write("config/season", ConfigFields(next, now), updateTime: updateTime));
                status = "created";
            }
            else if (!current.Value.Equals(next))
            {
                // updateTime precondition: two overlapping ticks cannot both rewrite the calendar.
                writes.Add(firestore.Write("config/season", ConfigFields(next, now), updateTime: updateTime));
                status = "updated";
            }

            if (!next.TryGetSeasonAt(now, out SeasonWindow running))
            {
                throw new InvalidOperationException("config/season has no season running now.");
            }

            // History documents: deterministic upserts, so repeating them is harmless.
            writes.Add(firestore.Write($"seasons/{running.SeasonId}", SeasonFields(running, null)));

            SeasonConfig history = current ?? next;

            if (history.TryGetWindow(running.SeasonId - 1, out SeasonWindow previous)
                || next.TryGetWindow(running.SeasonId - 1, out previous))
            {
                writes.Add(firestore.Write($"seasons/{previous.SeasonId}", SeasonFields(previous, previous.EndUtcMs)));
            }

            await firestore.CommitAsync(writes);
            return State(status, running);
        }

        #region Helpers

        private static JsonObject ConfigFields(SeasonConfig config, long now)
        {
            SeasonCalendar active = config.Active;
            var fields = new JsonObject
            {
                ["anchorSeasonId"] = FirestoreAdmin.Integer(active.AnchorSeasonId),
                ["anchorStartMs"] = FirestoreAdmin.Integer(active.AnchorStartUtcMs),
                ["anchorStartAt"] = FirestoreAdmin.Timestamp(active.AnchorStartUtcMs),
                ["durationMs"] = FirestoreAdmin.Integer(active.DurationMs),
                ["lockMs"] = FirestoreAdmin.Integer(active.LockMs),
                ["pendingSeasonId"] = FirestoreAdmin.Integer(config.Pending?.AnchorSeasonId ?? 0),
                ["pendingStartMs"] = FirestoreAdmin.Integer(config.Pending?.AnchorStartUtcMs ?? 0),
                ["pendingDurationMs"] = FirestoreAdmin.Integer(config.Pending?.DurationMs ?? 0),
                ["pendingLockMs"] = FirestoreAdmin.Integer(config.Pending?.LockMs ?? 0),
                ["updatedAt"] = FirestoreAdmin.Timestamp(now),
            };
            return fields;
        }

        private static JsonObject SeasonFields(SeasonWindow window, long? closedAt)
        {
            var fields = new JsonObject
            {
                ["seasonId"] = FirestoreAdmin.Integer(window.SeasonId),
                ["startAt"] = FirestoreAdmin.Timestamp(window.StartUtcMs),
                ["endAt"] = FirestoreAdmin.Timestamp(window.EndUtcMs),
                ["lockAt"] = FirestoreAdmin.Timestamp(window.LockUtcMs),
            };

            if (closedAt.HasValue)
            {
                fields["closedAt"] = FirestoreAdmin.Timestamp(closedAt.Value);
            }

            return fields;
        }

        private static SeasonState State(string status, SeasonWindow window)
        {
            return new SeasonState
            {
                Status = status,
                SeasonId = window.SeasonId,
                StartAt = Iso(window.StartUtcMs),
                EndAt = Iso(window.EndUtcMs),
                LockAt = Iso(window.LockUtcMs),
            };
        }

        private static string Iso(long unixMs)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        #endregion
    }

    /// <summary>
    /// Lists every account in the running season: each users/{uid} gets a row with 0 points (Bronze)
    /// and 0 % win rate. Accounts created before usernames existed (and not logged in since) are given
    /// their unique username here, the same way the game does on login. Rows missing "winRate"
    /// get it computed. Rows use a non-atomic batchWrite with an exists = false precondition,
    /// so a row the player created meanwhile only fails that one write.
    /// </summary>
    public static class SeasonRoster
    {
        private const int MaxUsernameAttempts = 5;

        public static async Task<int> FillAsync(
            FirestoreAdmin firestore,
            int seasonId,
            long now,
            Func<string> newUsername = null
        )
        {
            newUsername ??= RandomUsername;
            string entries = $"seasons/{seasonId.ToString(CultureInfo.InvariantCulture)}/entries";
            var present = new HashSet<string>(StringComparer.Ordinal);
            var writes = new JsonArray();
            int assigned = 0;

            foreach (JsonObject entry in await firestore.ListAsync(entries))
            {
                string uid = FirestoreAdmin.DocumentId(entry);
                present.Add(uid);
                JsonObject fields = entry["fields"]?.AsObject();

                if (fields != null && fields["winRate"] == null)
                {
                    // Rows without winRate are left out of the sorted leaderboard query: fill it in.
                    int wins = (int)Long(fields, "wins");
                    int matches = (int)Long(fields, "matches");
                    writes.Add(firestore.Patch(
                        $"{entries}/{uid}",
                        new JsonObject { ["winRate"] = FirestoreAdmin.Integer(SeasonStanding.WinRateOf(wins, matches)) }
                    ));
                }
            }

            foreach (JsonObject user in await firestore.ListAsync("users"))
            {
                string uid = FirestoreAdmin.DocumentId(user);

                if (present.Contains(uid))
                {
                    continue;
                }

                string username = user["fields"]?["username"]?["stringValue"]?.GetValue<string>();

                if (!UsernamePolicy.IsValid(username))
                {
                    username = await AssignUsernameAsync(firestore, uid, user["updateTime"]?.GetValue<string>(), now, newUsername);

                    if (username == null)
                    {
                        continue;
                    }

                    assigned++;
                }

                present.Add(uid);
                writes.Add(firestore.Write($"{entries}/{uid}", EmptyRow(username, now), exists: false));
            }

            int written = writes.Count == 0 ? 0 : await firestore.BatchWriteAsync(writes);
            return written + assigned;
        }

        /// <summary>
        /// usernames/{name} (create-only, so names stay unique) + users/{uid}.username in one commit.
        /// The users updateTime precondition stops it overwriting a name the player claimed meanwhile.
        /// </summary>
        private static async Task<string> AssignUsernameAsync(
            FirestoreAdmin firestore,
            string uid,
            string updateTime,
            long now,
            Func<string> newUsername
        )
        {
            for (int attempt = 0; attempt < MaxUsernameAttempts; attempt++)
            {
                string username = newUsername();
                var writes = new JsonArray
                {
                    firestore.Write(
                        $"usernames/{username}",
                        new JsonObject
                        {
                            ["uid"] = new JsonObject { ["stringValue"] = uid },
                            ["createdAt"] = FirestoreAdmin.Timestamp(now),
                        },
                        exists: false
                    ),
                    firestore.Patch(
                        $"users/{uid}",
                        new JsonObject
                        {
                            ["username"] = new JsonObject { ["stringValue"] = username },
                            ["updatedAt"] = FirestoreAdmin.Timestamp(now),
                        },
                        updateTime
                    ),
                };

                try
                {
                    await firestore.CommitAsync(writes);
                    return username;
                }
                catch (InvalidOperationException)
                {
                    // Name taken, or the player changed their profile: re-read and try again.
                    (JsonObject user, string latest) = await firestore.GetAsync($"users/{uid}");
                    string current = user?["fields"]?["username"]?["stringValue"]?.GetValue<string>();

                    if (user == null || UsernamePolicy.IsValid(current))
                    {
                        return user == null ? null : current;
                    }

                    updateTime = latest;
                }
            }

            return null;
        }

        private static string RandomUsername()
        {
            int number = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, UsernamePolicy.MaxNumber + 1);
            return UsernamePolicy.Format(number);
        }

        private static JsonObject EmptyRow(string username, long now)
        {
            return new JsonObject
            {
                ["username"] = new JsonObject { ["stringValue"] = username },
                ["points"] = FirestoreAdmin.Integer(0),
                ["wins"] = FirestoreAdmin.Integer(0),
                ["losses"] = FirestoreAdmin.Integer(0),
                ["draws"] = FirestoreAdmin.Integer(0),
                ["matches"] = FirestoreAdmin.Integer(0),
                ["winRate"] = FirestoreAdmin.Integer(0),
                ["updatedAt"] = FirestoreAdmin.Timestamp(now),
            };
        }

        private static long Long(JsonObject fields, string name)
        {
            string text = fields?[name]?["integerValue"]?.GetValue<string>();
            return text == null ? 0 : long.Parse(text, CultureInfo.InvariantCulture);
        }
    }
}
