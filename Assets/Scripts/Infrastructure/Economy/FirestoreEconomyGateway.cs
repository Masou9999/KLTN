using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KLTN.Game.Domain.Economy;
using KLTN.Game.Domain.Ranking;
using KLTN.Infrastructure.Firestore;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KLTN.Infrastructure.Economy
{
    /// <summary>
    /// Firestore layout:
    ///   users/{uid}                         coins, silver, schemaVersion, createdAt, updatedAt,
    ///                                       lastPurchase, lastRewardMatchId, username
    ///   usernames/{username}                uid, createdAt   (uniqueness key, create-only)
    ///   users/{uid}/ownedCards/{cardId}     acquiredAt, source
    ///   users/{uid}/matchRewards/{matchId}  result, coins, silver, createdAt  (written by Cloud Code ReportResult)
    /// Every write is one :commit batch so Security Rules can validate it with getAfter().
    /// </summary>
    public sealed class FirestoreEconomyGateway : IEconomyGateway
    {
        private const int SchemaVersion = 1;
        private const int MaxUsernameAttempts = 5;

        private readonly FirestoreRestClient client;
        private readonly Func<string> userIdProvider;
        private readonly Func<Task<string>> tokenProvider;

        public FirestoreEconomyGateway(
            FirestoreRestClient client,
            Func<string> userIdProvider,
            Func<Task<string>> tokenProvider
        )
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.userIdProvider = userIdProvider ?? throw new ArgumentNullException(nameof(userIdProvider));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        #region IEconomyGateway

        public async Task<EconomyResult> LoadOrCreateProfileAsync(IReadOnlyList<string> starterCardIds)
        {
            (string uid, string token, EconomyResult error) = await SessionAsync();

            if (error != null)
            {
                return error;
            }

            FirestoreResponse user = await client.GetDocumentAsync(UserPath(uid), token);

            if (user.IsNotFound)
            {
                EconomyResult created = await CreateProfileAsync(uid, token, starterCardIds);

                // ALREADY_EXISTS / FAILED_PRECONDITION: another device created it first. Just reload.
                if (!created.Success && created.Error != EconomyErrorCode.Rejected)
                {
                    return created;
                }
            }
            else if (!user.Success)
            {
                return Map(user);
            }

            return await ReadProfileAsync(uid, token);
        }

        public async Task<EconomyResult> PurchaseAsync(ShopEntry entry)
        {
            if (entry == null || entry.IsStarter)
            {
                return EconomyResult.Fail(EconomyErrorCode.Rejected, "Thẻ này không bán.");
            }

            (string uid, string token, EconomyResult error) = await SessionAsync();

            if (error != null)
            {
                return error;
            }

            string source = entry.Currency == Currency.Coins ? "coins" : "silver";
            var increments = new Dictionary<string, long> { [source] = -entry.Price };

            var writes = new List<JObject>
            {
                client.UpdateWrite(
                    UserPath(uid),
                    new Dictionary<string, object> { ["lastPurchase"] = entry.CardId },
                    increments,
                    "updatedAt"
                ),
                client.CreateWrite(
                    $"{UserPath(uid)}/ownedCards/{entry.CardId}",
                    new Dictionary<string, object> { ["source"] = source },
                    "acquiredAt"
                ),
            };

            FirestoreResponse response = await client.CommitAsync(writes, token);

            if (!response.Success)
            {
                EconomyResult failure = Map(response);
                Debug.LogWarning($"[Economy] Purchase {entry.CardId} failed: {failure.Error} {failure.Message}");
                return failure;
            }

            return await ReadProfileAsync(uid, token);
        }

        #endregion

        #region Helpers

        private async Task<EconomyResult> CreateProfileAsync(string uid, string token, IReadOnlyList<string> starterCardIds)
        {
            FirestoreResponse response = null;

            for (int attempt = 0; attempt < MaxUsernameAttempts; attempt++)
            {
                string username = NewUsername();
                var writes = new List<JObject>
                {
                    client.CreateWrite(
                        UserPath(uid),
                        new Dictionary<string, object>
                        {
                            ["coins"] = 0,
                            ["silver"] = 0,
                            ["schemaVersion"] = SchemaVersion,
                            ["username"] = username,
                        },
                        "createdAt",
                        "updatedAt"
                    ),
                    UsernameWrite(username, uid),
                };

                foreach (string cardId in starterCardIds)
                {
                    writes.Add(
                        client.CreateWrite(
                            $"{UserPath(uid)}/ownedCards/{cardId}",
                            new Dictionary<string, object> { ["source"] = "starter" },
                            "acquiredAt"
                        )
                    );
                }

                response = await client.CommitAsync(writes, token);

                if (response.Success)
                {
                    Debug.Log($"[Economy] Created profile for {uid} ({username}) with {starterCardIds.Count} starter cards.");
                    return EconomyResult.Ok(null);
                }

                if (!IsConflict(response))
                {
                    return Map(response);
                }

                // Conflict: either another device created the profile first, or the name is taken.
                FirestoreResponse user = await client.GetDocumentAsync(UserPath(uid), token);

                if (user.Success)
                {
                    return EconomyResult.Fail(EconomyErrorCode.Rejected, "Hồ sơ đã được tạo ở thiết bị khác.");
                }

                Debug.Log($"[Economy] Username {username} unavailable, retrying.");
            }

            return Map(response);
        }

        /// <summary>
        /// Gives an account created before usernames existed its unique name (lazy backfill on login).
        /// Returns null when every attempt failed; the profile still loads without one.
        /// </summary>
        private async Task<string> ClaimUsernameAsync(string uid, string token)
        {
            for (int attempt = 0; attempt < MaxUsernameAttempts; attempt++)
            {
                string username = NewUsername();
                var writes = new List<JObject>
                {
                    client.UpdateWrite(
                        UserPath(uid),
                        new Dictionary<string, object> { ["username"] = username },
                        null,
                        "updatedAt"
                    ),
                    UsernameWrite(username, uid),
                };

                FirestoreResponse response = await client.CommitAsync(writes, token);

                if (response.Success)
                {
                    Debug.Log($"[Economy] Assigned username {username} to {uid}.");
                    return username;
                }

                if (!IsConflict(response))
                {
                    Debug.LogWarning($"[Economy] Username claim failed: {response.ErrorStatus} {response.ErrorMessage}");
                    return null;
                }

                // Another device may have claimed one in the meantime.
                FirestoreResponse user = await client.GetDocumentAsync(UserPath(uid), token);
                string existing = FirestoreValueMapper.GetString(user.Body, "username");

                if (!string.IsNullOrEmpty(existing))
                {
                    return existing;
                }
            }

            return null;
        }

        private JObject UsernameWrite(string username, string uid)
        {
            return client.CreateWrite(
                $"usernames/{username}",
                new Dictionary<string, object> { ["uid"] = uid },
                "createdAt"
            );
        }

        private static string NewUsername()
        {
            int number = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, UsernamePolicy.MaxNumber + 1);
            return UsernamePolicy.Format(number);
        }

        /// <summary>A create precondition failed (document exists) or Rules saw the existing doc.</summary>
        private static bool IsConflict(FirestoreResponse response)
        {
            return response.ErrorStatus == "ALREADY_EXISTS"
                || response.ErrorStatus == "FAILED_PRECONDITION"
                || response.ErrorStatus == "PERMISSION_DENIED";
        }

        private async Task<EconomyResult> ReadProfileAsync(string uid, string token)
        {
            FirestoreResponse user = await client.GetDocumentAsync(UserPath(uid), token);

            if (!user.Success)
            {
                return Map(user);
            }

            (FirestoreResponse listResponse, List<JObject> cards) =
                await client.ListDocumentsAsync($"{UserPath(uid)}/ownedCards", token);

            if (!listResponse.Success)
            {
                return Map(listResponse);
            }

            var owned = new List<string>(cards.Count);

            foreach (JObject card in cards)
            {
                string id = FirestoreValueMapper.DocumentId(card);

                if (!string.IsNullOrEmpty(id))
                {
                    owned.Add(id);
                }
            }

            var wallet = new Wallet(
                (int)FirestoreValueMapper.GetInteger(user.Body, "coins"),
                (int)FirestoreValueMapper.GetInteger(user.Body, "silver")
            );

            string username = FirestoreValueMapper.GetString(user.Body, "username");

            if (string.IsNullOrEmpty(username))
            {
                username = await ClaimUsernameAsync(uid, token);
            }

            return EconomyResult.Ok(new PlayerProfile(uid, username, wallet, owned));
        }

        private async Task<(string uid, string token, EconomyResult error)> SessionAsync()
        {
            string uid = userIdProvider();
            string token = await tokenProvider();

            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(token))
            {
                return (null, null, EconomyResult.Fail(EconomyErrorCode.NotSignedIn, "Bạn chưa đăng nhập."));
            }

            return (uid, token, null);
        }

        private static string UserPath(string uid)
        {
            return $"users/{uid}";
        }

        private static EconomyResult Map(FirestoreResponse response)
        {
            switch (response.ErrorStatus)
            {
                case "PERMISSION_DENIED":
                    return EconomyResult.Fail(EconomyErrorCode.PermissionDenied, "Giao dịch bị từ chối.");
                case "UNAUTHENTICATED":
                    return EconomyResult.Fail(EconomyErrorCode.NotSignedIn, "Phiên đăng nhập đã hết hạn.");
                case "ALREADY_EXISTS":
                case "FAILED_PRECONDITION":
                    return EconomyResult.Fail(EconomyErrorCode.Rejected, response.ErrorMessage);
                case "NETWORK_ERROR":
                case "UNAVAILABLE":
                case "DEADLINE_EXCEEDED":
                    return EconomyResult.Fail(EconomyErrorCode.Network, "Không thể kết nối máy chủ dữ liệu.");
                default:
                    return EconomyResult.Fail(EconomyErrorCode.Unknown, response.ErrorMessage ?? "Lỗi không xác định.");
            }
        }

        #endregion
    }
}
