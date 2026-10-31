#nullable disable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KLTN.CloudCode.Shared
{
    /// <summary>
    /// Mints a Google OAuth access token from a Firebase service account key (JSON stored
    /// in UGS Secret Manager). Admin tokens bypass Firestore Security Rules, which is what
    /// lets Cloud Code modules write documents clients may not (config/season, matches, rewards).
    /// </summary>
    public sealed class GoogleServiceAccount
    {
        private const string TokenUrl = "https://oauth2.googleapis.com/token";
        private const string Scope = "https://www.googleapis.com/auth/datastore";

        public string ProjectId { get; }
        private readonly string clientEmail;
        private readonly string privateKeyPem;

        private string cachedToken;
        private DateTimeOffset cachedUntil;

        private GoogleServiceAccount(string projectId, string clientEmail, string privateKeyPem)
        {
            ProjectId = projectId;
            this.clientEmail = clientEmail;
            this.privateKeyPem = privateKeyPem;
        }

        public static GoogleServiceAccount FromJson(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            return new GoogleServiceAccount(
                root.GetProperty("project_id").GetString(),
                root.GetProperty("client_email").GetString(),
                root.GetProperty("private_key").GetString()
            );
        }

        public async Task<string> GetAccessTokenAsync(HttpClient http)
        {
            if (cachedToken != null && DateTimeOffset.UtcNow < cachedUntil)
            {
                return cachedToken;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["alg"] = "RS256",
                ["typ"] = "JWT",
            }));
            string claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["iss"] = clientEmail,
                ["scope"] = Scope,
                ["aud"] = TokenUrl,
                ["iat"] = now,
                ["exp"] = now + 3600,
            }));

            string unsigned = header + "." + claims;
            byte[] signature;

            using (RSA rsa = RSA.Create())
            {
                rsa.ImportFromPem(privateKeyPem);
                signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            string assertion = unsigned + "." + Base64Url(signature);
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            });

            using HttpResponseMessage response = await http.PostAsync(TokenUrl, body);
            string text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Google token request failed ({(int)response.StatusCode}): {text}");
            }

            using JsonDocument token = JsonDocument.Parse(text);
            cachedToken = token.RootElement.GetProperty("access_token").GetString();
            int expiresIn = token.RootElement.TryGetProperty("expires_in", out JsonElement e) ? e.GetInt32() : 3600;
            cachedUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 120));
            return cachedToken;
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
