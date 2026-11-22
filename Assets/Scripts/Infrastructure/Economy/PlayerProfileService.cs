using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KLTN.Game.Content;
using KLTN.Game.Domain.Economy;
using KLTN.Game.Domain.Ranking;
using KLTN.Infrastructure.Ranking;
using KLTN.Infrastructure.Firestore;
using UnityEngine;

namespace KLTN.Infrastructure.Economy
{
    /// <summary>
    /// Client-side cache of the signed-in player's wallet and owned cards.
    /// Loads after login, clears on logout, and exposes purchase commands to the UI.
    /// Match rewards are applied by the backend; the result screen only reloads the profile.
    /// Created automatically; survives scene changes.
    /// </summary>
    public sealed class PlayerProfileService : MonoBehaviour
    {
        public static PlayerProfileService Instance { get; private set; }

        public event Action<PlayerProfile> ProfileChanged;

        public PlayerProfile Current { get; private set; }
        public bool IsLoaded => Current != null;
        public ShopCatalog Catalog { get; private set; }

        private IEconomyGateway gateway;
        private Task<EconomyResult> loadTask;
        private int sessionVersion;
        private bool renameInFlight;
        private string CurrentUserId => AuthManager.Instance != null ? AuthManager.Instance.CurrentUserId : null;

        #region Bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_SERVER
            if (true)
#else
            if (Application.isBatchMode)
#endif
            {
                // Dedicated server has no signed-in player.
                return;
            }

            var host = new GameObject(nameof(PlayerProfileService));
            DontDestroyOnLoad(host);
            host.AddComponent<PlayerProfileService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Catalog = ShopCatalog.Load();
            gateway = new FirestoreEconomyGateway(
                new FirestoreRestClient(),
                () => AuthManager.Instance != null ? AuthManager.Instance.CurrentUserId : null,
                () => AuthManager.Instance != null ? AuthManager.Instance.GetValidIdTokenAsync() : Task.FromResult<string>(null)
            );

            KLTN.Game.Networking.MatchLoadoutRegistry.LocalOwnedCardIds = OwnedCardIdsOrStarter;

            AuthManager.OnLoginSuccess += HandleLogin;
            AuthManager.OnLogoutSuccess += HandleLogout;
        }

        private void OnDestroy()
        {
            AuthManager.OnLoginSuccess -= HandleLogin;
            AuthManager.OnLogoutSuccess -= HandleLogout;

            if (Instance == this)
            {
                Instance = null;
                KLTN.Game.Networking.MatchLoadoutRegistry.LocalOwnedCardIds = null;
            }
        }

        private async void HandleLogin()
        {
            ++sessionVersion;
            Current = null;
            loadTask = null;
            ProfileChanged?.Invoke(null);
            EconomyResult result = await EnsureLoadedAsync();

            if (!result.Success)
            {
                Debug.LogError($"[Profile] Could not load profile: {result.Error} {result.Message}");
            }
        }

        private void HandleLogout()
        {
            ++sessionVersion;
            Current = null;
            loadTask = null;
            ProfileChanged?.Invoke(null);
        }

        #endregion

        #region Commands

        /// <summary>Checks the canonical reservation without claiming it; rename rechecks atomically.</summary>
        public async Task<string> CheckUsernameAsync(string requestedName)
        {
            string error = UsernamePolicy.ValidateChoice(requestedName);
            if (error != null) return error;
            if (Current == null || Current.UserId != CurrentUserId) return "not_signed_in";
            int session = sessionVersion;
            try
            {
                string token = await AuthManager.Instance.GetValidIdTokenAsync();
                if (string.IsNullOrEmpty(token)) return "not_signed_in";
                string key = UsernamePolicy.Normalize(requestedName);
                var response = await new FirestoreRestClient().GetDocumentAsync(
                    "usernames/" + Uri.EscapeDataString(key), token);
                if (session != sessionVersion) return "session_changed";
                if (response.IsNotFound) return null;
                if (response.Success) return "name_taken";
                return "availability_unavailable";
            }
            catch (Exception)
            {
                return "availability_unavailable";
            }
        }

        /// <summary>Changes the public name once; late replies cannot mutate another session.</summary>
        public async Task<UsernameChangeResponse> ChangeUsernameAsync(string requestedName)
        {
            string name = UsernamePolicy.DisplayName(requestedName);
            if (!UsernamePolicy.IsValid(name))
                return new UsernameChangeResponse { Error = "invalid_name" };
            if (Current == null || Current.UserId != CurrentUserId)
                return new UsernameChangeResponse { Error = "not_signed_in" };
            if (renameInFlight) return new UsernameChangeResponse { Error = "busy" };
            int session = sessionVersion;
            string uid = CurrentUserId;
            renameInFlight = true;
            try
            {
                var result = await UsernameChangeClient.ChangeAsync(uid, name);
                if (session != sessionVersion || uid != CurrentUserId)
                    return new UsernameChangeResponse { Error = "session_changed" };
                if (result.Success && Current != null &&
                    result.UsernameVersion >= Current.UsernameVersion)
                {
                    Apply(new PlayerProfile(uid, result.Username, Current.Wallet,
                        Current.OwnedCardIds, result.UsernameVersion, result.UsernameChangeCount));
                    LeaderboardService.Instance?.InvalidateUsername();
                }
                if (result.Error == "already_changed")
                    await EnsureLoadedAsync(forceReload: true);
                return result;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Profile] Rename response unavailable: " +
                    exception.GetType().Name);
                return new UsernameChangeResponse { Error = "network_unknown" };
            }
            finally
            {
                renameInFlight = false;
            }
        }

        /// <summary>Loads (or creates with starter cards) the profile once; later calls reuse it.</summary>
        public Task<EconomyResult> EnsureLoadedAsync(bool forceReload = false)
        {
            if (!forceReload && Current != null)
            {
                return Task.FromResult(EconomyResult.Ok(Current));
            }

            if (loadTask == null || loadTask.IsCompleted)
            {
                loadTask = LoadInternalAsync();
            }

            return loadTask;
        }

        public async Task<EconomyResult> PurchaseAsync(string cardId)
        {
            if (Catalog == null || !Catalog.TryGet(cardId, out ShopEntry entry))
            {
                return EconomyResult.Fail(EconomyErrorCode.Rejected, "Thẻ không có trong cửa hàng.");
            }

            if (Current == null)
            {
                return EconomyResult.Fail(EconomyErrorCode.NotSignedIn, "Hồ sơ người chơi chưa sẵn sàng.");
            }

            PurchaseRejection rejection = PurchaseRules.Check(entry, Current.Owns(cardId), Current.Wallet);

            if (rejection != PurchaseRejection.None)
            {
                return EconomyResult.Fail(EconomyErrorCode.Rejected, RejectionMessage(rejection));
            }

            int session = sessionVersion;
            string uid = CurrentUserId;
            EconomyResult result = await gateway.PurchaseAsync(entry);
            if (session != sessionVersion || uid != CurrentUserId)
                return EconomyResult.Fail(EconomyErrorCode.NotSignedIn, "Phiên đăng nhập đã thay đổi.");

            if (result.Success)
            {
                Apply(result.Profile);
            }
            else if (result.Error == EconomyErrorCode.PermissionDenied)
            {
                // Local cache was stale (e.g. purchase from another device). Resync.
                await EnsureLoadedAsync(forceReload: true);
            }

            return result;
        }

        /// <summary>Owned card IDs for the match loadout; falls back to the starter set.</summary>
        public List<string> OwnedCardIdsOrStarter()
        {
            if (Current != null && Current.OwnedCardIds.Count > 0)
            {
                var ids = new List<string>(Current.OwnedCardIds);
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }

            return Catalog != null ? Catalog.StarterCardIds() : new List<string>();
        }

        #endregion

        #region Internals

        private async Task<EconomyResult> LoadInternalAsync()
        {
            int session = sessionVersion;
            string uid = CurrentUserId;
            IReadOnlyList<string> starters = Catalog != null ? Catalog.StarterCardIds() : new List<string>();
            EconomyResult result = await gateway.LoadOrCreateProfileAsync(starters);
            if (session != sessionVersion || uid != CurrentUserId)
                return EconomyResult.Fail(EconomyErrorCode.NotSignedIn, "Phiên đăng nhập đã thay đổi.");

            if (result.Success)
            {
                Apply(result.Profile);
                Debug.Log(
                    $"[Profile] Loaded {result.Profile.UserId}: {result.Profile.Wallet.Coins} xu, "
                        + $"{result.Profile.Wallet.Silver} bạc, {result.Profile.OwnedCardIds.Count} thẻ."
                );
            }

            return result;
        }

        private void Apply(PlayerProfile profile)
        {
            if (profile == null || profile.UserId != CurrentUserId)
            {
                return;
            }

            // A wallet/profile read started before rename may finish after its receipt.
            if (Current != null && Current.UserId == profile.UserId &&
                profile.UsernameVersion < Current.UsernameVersion)
            {
                profile = new PlayerProfile(profile.UserId, Current.Username,
                    profile.Wallet, profile.OwnedCardIds,
                    Current.UsernameVersion, Current.UsernameChangeCount);
            }
            Current = profile;
            ProfileChanged?.Invoke(profile);
        }

        private static string RejectionMessage(PurchaseRejection rejection)
        {
            switch (rejection)
            {
                case PurchaseRejection.AlreadyOwned:
                    return "Bạn đã sở hữu thẻ này.";
                case PurchaseRejection.InsufficientFunds:
                    return "Không đủ tiền.";
                default:
                    return "Thẻ này không bán.";
            }
        }

        #endregion
    }
}
