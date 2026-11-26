using System;
using System.Globalization;
using System.Threading.Tasks;
using KLTN.Game.Content;
using KLTN.Game.Domain.Ranking;
using KLTN.Infrastructure.Economy;
using KLTN.Infrastructure.Firestore;
using KLTN.Infrastructure.Ranking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI
{
    public sealed class MainMenuProfileView : MonoBehaviour
    {
        [SerializeField] private TMP_Text username;
        [SerializeField] private TMP_Text coins;
        [SerializeField] private TMP_Text silver;
        [SerializeField] private Image frame;
        [SerializeField] private ProfileFrameCatalog catalog;

        private PlayerProfileService profiles;
        private int generation;
        private bool loading;
        private float nextRefresh;
        private long seasonEnd;
        private bool reloadWallet;
        private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("vi-VN");
        private string UserId => AuthManager.Instance != null ? AuthManager.Instance.CurrentUserId : null;

        private void Awake()
        {
            PlayerInformationView.Attach(username);
        }

        private void OnEnable()
        {
            profiles = PlayerProfileService.Instance;
            if (profiles != null) profiles.ProfileChanged += RenderProfile;
            AuthManager.OnLoginSuccess += ResetSession;
            AuthManager.OnLogoutSuccess += ResetSession;
            ResetSession();
        }

        private void OnDisable()
        {
            ++generation;
            if (profiles != null) profiles.ProfileChanged -= RenderProfile;
            AuthManager.OnLoginSuccess -= ResetSession;
            AuthManager.OnLogoutSuccess -= ResetSession;
        }

        private void ResetSession()
        {
            ++generation;
            loading = false;
            seasonEnd = 0;
            reloadWallet = true;
            catalog.Apply(frame, RankTier.Bronze);
            var ranking = LeaderboardService.Instance;
            if (ranking != null && ranking.TryGetCachedProfileFrame(out var cached))
                catalog.Apply(frame, cached.Tier);
            RenderProfile(profiles?.Current);
            nextRefresh = 0;
        }

        private void Update()
        {
            if (loading || string.IsNullOrEmpty(UserId)) return;
            if (Time.unscaledTime >= nextRefresh || (seasonEnd > 0 && ServerClock.UtcNowMs >= seasonEnd))
                _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            int request = generation;
            string uid = UserId;
            loading = true;
            seasonEnd = 0;
            try
            {
                // Start both independently: an unavailable wallet must not hide a valid frame.
                var profileTask = profiles != null ? profiles.EnsureLoadedAsync(forceReload: reloadWallet) : null;
                var ranking = LeaderboardService.Instance;
                var frameTask = ranking != null ? ranking.RefreshProfileFrameAsync() : null;
                if (profileTask != null)
                {
                    var result = await profileTask;
                    if (!IsCurrent(request, uid)) return;
                    if (result.Success)
                    {
                        reloadWallet = false;
                        RenderProfile(result.Profile);
                    }
                    else if (profiles.Current == null) username.text = "Chưa tải được hồ sơ";
                }
                if (frameTask != null)
                {
                    var result = await frameTask;
                    if (!IsCurrent(request, uid)) return;
                    if (result.Success)
                    {
                        catalog.Apply(frame, result.Value.Tier);
                        seasonEnd = result.Value.RefreshAtUtcMs;
                    }
                }
            }
            catch (Exception exception)
            {
                if (IsCurrent(request, uid)) Debug.LogWarning($"[MainMenuProfile] {exception.Message}");
            }
            finally
            {
                if (IsCurrent(request, uid))
                {
                    loading = false;
                    nextRefresh = Time.unscaledTime + 60;
                }
            }
        }

        private bool IsCurrent(int request, string uid) =>
            this != null && isActiveAndEnabled && request == generation && uid == UserId;

        private void RenderProfile(PlayerProfile profile)
        {
            if (profile == null || profile.UserId != UserId)
            {
                username.text = string.IsNullOrEmpty(UserId) ? "Chưa đăng nhập" : "Đang tải…";
                coins.text = "— Xu";
                silver.text = "— Bạc";
                return;
            }
            username.text = string.IsNullOrEmpty(profile.Username) ? "Người chơi" : profile.Username;
            coins.text = profile.Wallet.Coins.ToString("N0", NumberCulture) + " Xu";
            silver.text = profile.Wallet.Silver.ToString("N0", NumberCulture) + " Bạc";
        }
    }
}
