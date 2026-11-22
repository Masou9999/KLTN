#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace KLTN.CloudCode.Shared
{
    /// <summary>Tiny Firestore REST client authenticated as the service account.</summary>
    public sealed class FirestoreAdmin
    {
        private readonly HttpClient http;
        private readonly GoogleServiceAccount account;
        private readonly string root;

        private readonly string host;
        private string transaction;
        private bool committed;

        private FirestoreAdmin(FirestoreAdmin source, string transaction)
        {
            http = source.http;
            account = source.account;
            host = source.host;
            root = source.root;
            this.transaction = transaction;
        }

        /// <summary>
        /// Runs a serializable transaction. Reads, including missing documents, participate
        /// in conflict detection; the callback must have no side effects outside Firestore.
        /// </summary>
        public async Task<T> RunTransactionAsync<T>(Func<FirestoreAdmin, Task<T>> action)
        {
            if (transaction != null) throw new InvalidOperationException("Nested transaction.");
            for (int attempt = 0; ; attempt++)
            {
                JsonObject begin = await TransactionRequestAsync("beginTransaction", new JsonObject());
                var scoped = new FirestoreAdmin(this, begin["transaction"].GetValue<string>());
                try
                {
                    T result = await action(scoped);
                    if (!scoped.committed) await scoped.CommitAsync(new JsonArray());
                    return result;
                }
                catch (FirestoreConflictException) when (attempt < 4)
                {
                    // Roll back before retrying. Jitter prevents two devices from repeatedly
                    // upgrading the same shared read lock at exactly the same time.
                }
                finally
                {
                    if (!scoped.committed)
                    {
                        try
                        {
                            await TransactionRequestAsync("rollback",
                                new JsonObject { ["transaction"] = scoped.transaction });
                        }
                        catch (Exception) { /* Preserve the original error; server expires locks. */ }
                    }
                }
                await Task.Delay(80 * (1 << attempt) +
                    System.Security.Cryptography.RandomNumberGenerator.GetInt32(30, 250));
            }
        }

        private async Task<JsonObject> TransactionRequestAsync(string operation, JsonObject body)
        {
            using HttpRequestMessage request = await RequestAsync(HttpMethod.Post, $"{host}/v1/{root}:{operation}");
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Firestore {operation} failed ({(int)response.StatusCode}).");
            return JsonNode.Parse(text).AsObject();
        }

        public FirestoreAdmin(HttpClient http, GoogleServiceAccount account)
            : this(http, account, account.ProjectId, "https://firestore.googleapis.com")
        {
        }

        /// <summary>Emulator/test constructor: <paramref name="account"/> null sends "Bearer owner".</summary>
        public FirestoreAdmin(HttpClient http, GoogleServiceAccount account, string projectId, string host)
        {
            this.http = http;
            this.account = account;
            this.host = host.TrimEnd('/');
            root = $"projects/{projectId}/databases/(default)/documents";
        }

        public async Task<(JsonObject document, string updateTime)> GetAsync(string path)
        {
            if (transaction != null) return await GetInTransactionAsync(path);
            using HttpRequestMessage request = await RequestAsync(HttpMethod.Get, $"{host}/v1/{root}/{path}");
            using HttpResponseMessage response = await http.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (null, null);
            }

            string text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    throw new FirestoreConflictException("Transaction read aborted.");
                throw new InvalidOperationException($"Firestore GET {path} failed ({(int)response.StatusCode}): {text}");
            }

            JsonObject document = JsonNode.Parse(text).AsObject();
            return (document, document["updateTime"]?.GetValue<string>());
        }

        private async Task<(JsonObject document, string updateTime)> GetInTransactionAsync(string path)
        {
            // batchGet carries the transaction bytes in JSON, supported by production and emulator.
            using HttpRequestMessage request = await RequestAsync(HttpMethod.Post, $"{host}/v1/{root}:batchGet");
            var body = new JsonObject
            {
                ["documents"] = new JsonArray($"{root}/{path}"),
                ["transaction"] = transaction,
            };
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict ||
                    text.Contains("ABORTED", StringComparison.Ordinal))
                    throw new FirestoreConflictException("Transaction read aborted.");
                throw new InvalidOperationException($"Firestore transaction read failed ({(int)response.StatusCode}): {text}");
            }
            JsonArray results = JsonNode.Parse(text).AsArray();
            foreach (JsonNode result in results)
            {
                if (result["found"] is JsonObject document)
                    return (document, document["updateTime"]?.GetValue<string>());
                if (result["missing"] != null) return (null, null);
            }
            throw new InvalidOperationException("Firestore batchGet returned no document result.");
        }

        /// <summary>Every document of a collection (follows page tokens).</summary>
        public async Task<List<JsonObject>> ListAsync(string collectionPath)
        {
            var documents = new List<JsonObject>();
            string pageToken = null;

            do
            {
                string url = $"{host}/v1/{root}/{collectionPath}?pageSize=300";

                if (pageToken != null)
                {
                    url += "&pageToken=" + Uri.EscapeDataString(pageToken);
                }

                using HttpRequestMessage request = await RequestAsync(HttpMethod.Get, url);
                using HttpResponseMessage response = await http.SendAsync(request);
                string text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Firestore list {collectionPath} failed ({(int)response.StatusCode}): {text}");
                }

                JsonObject page = JsonNode.Parse(text).AsObject();

                if (page["documents"] is JsonArray rows)
                {
                    foreach (JsonNode row in rows)
                    {
                        documents.Add(row.AsObject());
                    }
                }

                pageToken = page["nextPageToken"]?.GetValue<string>();
            }
            while (!string.IsNullOrEmpty(pageToken));

            return documents;
        }

        public static string DocumentId(JsonObject document)
        {
            string name = document["name"]?.GetValue<string>() ?? string.Empty;
            return name.Substring(name.LastIndexOf('/') + 1);
        }

        /// <summary>Sets only the given fields of an existing document (optionally at a known version).</summary>
        public JsonObject Patch(string path, JsonObject fields, string updateTime = null)
        {
            var mask = new JsonArray();

            foreach (KeyValuePair<string, JsonNode> field in fields)
            {
                mask.Add(field.Key);
            }

            JsonObject write = updateTime != null
                ? Write(path, fields, updateTime: updateTime)
                : Write(path, fields, exists: true);
            write["updateMask"] = new JsonObject { ["fieldPaths"] = mask };
            return write;
        }

        /// <summary>Deletes an existing document as part of a commit or transaction.</summary>
        public JsonObject Delete(string path) => new JsonObject
        {
            ["delete"] = $"{root}/{path}",
            ["currentDocument"] = new JsonObject { ["exists"] = true },
        };

        /// <summary>Non-atomic writes (500 per request); returns how many succeeded.</summary>
        public async Task<int> BatchWriteAsync(JsonArray writes)
        {
            int succeeded = 0;
            var all = new List<JsonNode>(writes);

            for (int offset = 0; offset < all.Count; offset += 500)
            {
                var chunk = new JsonArray();

                for (int index = offset; index < Math.Min(all.Count, offset + 500); index++)
                {
                    chunk.Add(JsonNode.Parse(all[index].ToJsonString()));
                }

                var body = new JsonObject { ["writes"] = chunk };
                using HttpRequestMessage request = await RequestAsync(HttpMethod.Post, $"{host}/v1/{root}:batchWrite");
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await http.SendAsync(request);
                string text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Firestore batchWrite failed ({(int)response.StatusCode}): {text}");
                }

                if (JsonNode.Parse(text)?["status"] is JsonArray statuses)
                {
                    foreach (JsonNode status in statuses)
                    {
                        // google.rpc.Status: code is absent (0 = OK) on success.
                        if ((status?["code"]?.GetValue<int>() ?? 0) == 0)
                        {
                            succeeded++;
                        }
                    }
                }
            }

            return succeeded;
        }

        public JsonObject Write(string path, JsonObject fields, bool? exists = null, string updateTime = null)
        {
            var write = new JsonObject
            {
                ["update"] = new JsonObject { ["name"] = $"{root}/{path}", ["fields"] = fields },
            };

            if (updateTime != null)
            {
                write["currentDocument"] = new JsonObject { ["updateTime"] = updateTime };
            }
            else if (exists.HasValue)
            {
                write["currentDocument"] = new JsonObject { ["exists"] = exists.Value };
            }

            return write;
        }

        public async Task CommitAsync(JsonArray writes)
        {
            var body = new JsonObject { ["writes"] = writes };
            if (transaction != null) body["transaction"] = transaction;
            using HttpRequestMessage request = await RequestAsync(HttpMethod.Post, $"{host}/v1/{root}:commit");
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string text = await response.Content.ReadAsStringAsync();
                string message = $"Firestore commit failed ({(int)response.StatusCode}): {text}";

                // Precondition failed (updateTime / exists) or a contended transaction: re-read and retry.
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict
                    || response.StatusCode == System.Net.HttpStatusCode.NotFound
                    || text.Contains("FAILED_PRECONDITION", StringComparison.Ordinal))
                {
                    throw new FirestoreConflictException(message);
                }

                throw new InvalidOperationException(message);
            }
            committed = true;
        }

        /// <summary>Adds server-side increments to a write, so counters never lose a concurrent update.</summary>
        public static JsonObject WithIncrements(JsonObject write, IReadOnlyDictionary<string, long> increments)
        {
            var transforms = new JsonArray();

            foreach (KeyValuePair<string, long> increment in increments)
            {
                transforms.Add(new JsonObject
                {
                    ["fieldPath"] = increment.Key,
                    ["increment"] = Integer(increment.Value),
                });
            }

            write["updateTransforms"] = transforms;
            return write;
        }

        public static JsonObject Integer(long value)
        {
            return new JsonObject { ["integerValue"] = value.ToString(CultureInfo.InvariantCulture) };
        }

        public static JsonObject String(string value)
        {
            return new JsonObject { ["stringValue"] = value ?? string.Empty };
        }

        public static JsonObject Boolean(bool value)
        {
            return new JsonObject { ["booleanValue"] = value };
        }

        public static JsonObject StringArray(IEnumerable<string> values)
        {
            var array = new JsonArray();

            foreach (string value in values)
            {
                array.Add(String(value));
            }

            return new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = array } };
        }

        public static JsonObject Fields(JsonObject document)
        {
            return document?["fields"]?.AsObject();
        }

        public static string GetString(JsonObject fields, string name)
        {
            return fields?[name]?["stringValue"]?.GetValue<string>();
        }

        public static long GetLong(JsonObject fields, string name, long fallback = 0)
        {
            string text = fields?[name]?["integerValue"]?.GetValue<string>();
            return text == null ? fallback : long.Parse(text, CultureInfo.InvariantCulture);
        }

        public static bool GetBool(JsonObject fields, string name, bool fallback = false)
        {
            JsonNode value = fields?[name]?["booleanValue"];
            return value == null ? fallback : value.GetValue<bool>();
        }

        public static bool Has(JsonObject fields, string name)
        {
            return fields != null && fields.ContainsKey(name);
        }

        public static List<string> GetStrings(JsonObject fields, string name)
        {
            var result = new List<string>();

            if (fields?[name]?["arrayValue"]?["values"] is JsonArray values)
            {
                foreach (JsonNode value in values)
                {
                    string text = value?["stringValue"]?.GetValue<string>();

                    if (text != null)
                    {
                        result.Add(text);
                    }
                }
            }

            return result;
        }

        public static JsonObject Timestamp(long unixMs)
        {
            return new JsonObject
            {
                ["timestampValue"] = DateTimeOffset.FromUnixTimeMilliseconds(unixMs)
                    .UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            };
        }

        public static long ParseTimestamp(JsonNode value)
        {
            string text = value?["timestampValue"]?.GetValue<string>();
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUnixTimeMilliseconds();
        }

        private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            string token = account != null ? await account.GetAccessTokenAsync(http) : "owner";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }
    }

    /// <summary>A commit lost a race (precondition failed, document exists/missing): re-read and retry.</summary>
    public sealed class FirestoreConflictException : InvalidOperationException
    {
        public FirestoreConflictException(string message)
            : base(message)
        {
        }
    }
}
