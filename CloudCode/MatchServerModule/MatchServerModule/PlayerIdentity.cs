using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KLTN.CloudCode.MatchServer
{
    /// <summary>Checks that a UGS player access token is genuine and belongs to the given player.</summary>
    public interface IPlayerTokenVerifier
    {
        /// <summary>Null when the token is valid, otherwise a short reason.</summary>
        Task<string> VerifyAsync(string accessToken, string playerId, string projectId, long nowUtcMs);
    }

    /// <summary>Finds the Firebase uid linked to a UGS player (OpenID Connect provider "oidc-firebase").</summary>
    public interface IPlayerIdentityLookup
    {
        /// <summary>Null when the player has no Firebase identity.</summary>
        Task<string> GetFirebaseUidAsync(string playerId, string accessToken, string projectId);
    }

    /// <summary>
    /// Verifies UGS player JWTs offline: RS256 signature against the player-auth JWKS, issuer,
    /// subject (PlayerId), project and expiry. Keys are cached and refreshed when an unknown kid shows up.
    /// </summary>
    public sealed class UgsPlayerTokenVerifier : IPlayerTokenVerifier
    {
        public const string DefaultIssuer = "https://player-auth.services.api.unity.com";
        private const string JwksPath = "/.well-known/jwks.json";
        private static readonly TimeSpan KeyCacheLifetime = TimeSpan.FromHours(1);
        private const long ClockSkewMs = 30_000;

        private readonly Func<Task<string>> jwksSource;
        private readonly string issuer;
        private readonly SemaphoreSlim refreshLock = new SemaphoreSlim(1, 1);

        private Dictionary<string, RSAParameters> keys = new Dictionary<string, RSAParameters>(StringComparer.Ordinal);
        private DateTimeOffset keysFetchedAt = DateTimeOffset.MinValue;

        public UgsPlayerTokenVerifier(HttpClient http)
            : this(() => http.GetStringAsync(DefaultIssuer + JwksPath), DefaultIssuer)
        {
        }

        /// <summary>Test constructor: custom key set and issuer.</summary>
        public UgsPlayerTokenVerifier(Func<Task<string>> jwksSource, string issuer)
        {
            this.jwksSource = jwksSource;
            this.issuer = issuer;
        }

        public async Task<string> VerifyAsync(string accessToken, string playerId, string projectId, long nowUtcMs)
        {
            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(playerId))
            {
                return "missing_token";
            }

            string[] parts = accessToken.Split('.');

            if (parts.Length != 3)
            {
                return "malformed_token";
            }

            JsonElement header;
            JsonElement claims;
            byte[] signature;

            try
            {
                header = JsonDocument.Parse(Base64UrlDecode(parts[0])).RootElement;
                claims = JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement;
                signature = Base64UrlDecode(parts[2]);
            }
            catch (Exception)
            {
                return "malformed_token";
            }

            if (Text(header, "alg") != "RS256")
            {
                return "unsupported_alg";
            }

            string kid = Text(header, "kid") ?? string.Empty;
            RSAParameters? key = await FindKeyAsync(kid);

            if (!key.HasValue)
            {
                return "unknown_key";
            }

            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(key.Value);
                byte[] signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);

                if (!rsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return "bad_signature";
                }
            }

            if (Text(claims, "iss") != issuer)
            {
                return "bad_issuer";
            }

            if (Text(claims, "sub") != playerId)
            {
                return "wrong_player";
            }

            if (!Number(claims, "exp", out long exp) || exp * 1000 + ClockSkewMs <= nowUtcMs)
            {
                return "expired";
            }

            if (Number(claims, "nbf", out long nbf) && nbf * 1000 - ClockSkewMs > nowUtcMs)
            {
                return "not_yet_valid";
            }

            return BelongsToProject(claims, projectId) ? null : "wrong_project";
        }

        #region Helpers

        /// <summary>UGS puts the project in "project_id" and/or in the audience ("upid:{id}", "idd:{id}").</summary>
        private static bool BelongsToProject(JsonElement claims, string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                return false;
            }

            if (Text(claims, "project_id") == projectId)
            {
                return true;
            }

            if (!claims.TryGetProperty("aud", out JsonElement aud))
            {
                return false;
            }

            var audiences = new List<string>();

            if (aud.ValueKind == JsonValueKind.String)
            {
                audiences.Add(aud.GetString());
            }
            else if (aud.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement value in aud.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        audiences.Add(value.GetString());
                    }
                }
            }

            return audiences.Contains("upid:" + projectId) || audiences.Contains("idd:" + projectId);
        }

        private async Task<RSAParameters?> FindKeyAsync(string kid)
        {
            bool fresh = DateTimeOffset.UtcNow - keysFetchedAt < KeyCacheLifetime;

            if (fresh && keys.TryGetValue(kid, out RSAParameters cached))
            {
                return cached;
            }

            await refreshLock.WaitAsync();

            try
            {
                // Refetch at most once a minute for unknown kids (rotation), always when stale.
                if (!fresh || DateTimeOffset.UtcNow - keysFetchedAt > TimeSpan.FromMinutes(1))
                {
                    keys = ParseJwks(await jwksSource());
                    keysFetchedAt = DateTimeOffset.UtcNow;
                }
            }
            finally
            {
                refreshLock.Release();
            }

            return keys.TryGetValue(kid, out RSAParameters key) ? key : (RSAParameters?)null;
        }

        private static Dictionary<string, RSAParameters> ParseJwks(string json)
        {
            var result = new Dictionary<string, RSAParameters>(StringComparer.Ordinal);
            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("keys", out JsonElement list))
            {
                return result;
            }

            foreach (JsonElement key in list.EnumerateArray())
            {
                if (Text(key, "kty") != "RSA" || Text(key, "n") == null || Text(key, "e") == null)
                {
                    continue;
                }

                result[Text(key, "kid") ?? string.Empty] = new RSAParameters
                {
                    Modulus = Base64UrlDecode(Text(key, "n")),
                    Exponent = Base64UrlDecode(Text(key, "e")),
                };
            }

            return result;
        }

        private static string Text(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }

        private static bool Number(JsonElement element, string name, out long value)
        {
            value = 0;
            return element.TryGetProperty(name, out JsonElement node)
                && node.ValueKind == JsonValueKind.Number
                && node.TryGetInt64(out value);
        }

        public static byte[] Base64UrlDecode(string text)
        {
            string padded = text.Replace('-', '+').Replace('_', '/');

            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }

        #endregion
    }

    /// <summary>
    /// Reads the player's linked identities from UGS Player Authentication with the player's own
    /// token (GET /v1/users/{playerId}, the call behind AuthenticationService.GetPlayerInfoAsync).
    /// The Firebase uid is the externalId of provider "oidc-firebase" (AuthManager signs in with it).
    /// </summary>
    public sealed class UgsPlayerAuthLookup : IPlayerIdentityLookup
    {
        public const string FirebaseProviderId = "oidc-firebase";

        private readonly HttpClient http;
        private readonly string baseUrl;

        public UgsPlayerAuthLookup(HttpClient http, string baseUrl = UgsPlayerTokenVerifier.DefaultIssuer)
        {
            this.http = http;
            this.baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<string> GetFirebaseUidAsync(string playerId, string accessToken, string projectId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/users/{Uri.EscapeDataString(playerId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("ProjectId", projectId);

            using HttpResponseMessage response = await http.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Player auth lookup failed ({(int)response.StatusCode}): {text}");
            }

            return FirebaseUidFrom(text);
        }

        /// <summary>Picks the oidc-firebase externalId out of a player-auth user document.</summary>
        public static string FirebaseUidFrom(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("externalIds", out JsonElement ids) || ids.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement id in ids.EnumerateArray())
            {
                string provider = id.TryGetProperty("providerId", out JsonElement p) ? p.GetString() : null;
                string external = id.TryGetProperty("externalId", out JsonElement e) ? e.GetString() : null;

                if (string.Equals(provider, FirebaseProviderId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(external))
                {
                    return external;
                }
            }

            return null;
        }
    }
}
