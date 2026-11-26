using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CMCMProductions;
using KLTN.Audio;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Game.Domain.Ranking;
using KLTN.Game.Presentation;
using KLTN.Infrastructure.Economy;
using KLTN.Infrastructure.Firestore;
using KLTN.Infrastructure.Ranking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>Coordinates private profile, completed-season history and owned-card inspection.</summary>
    public sealed class PlayerInformationView : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private Image avatar;
        [SerializeField] private CardPresentationCatalog presentation;
        [SerializeField] private CardDetailModal detailPrefab;
        [SerializeField] private ModalTheme theme = new ModalTheme();
        [Tooltip("Personal, ranking and owned-card tab icons, in that order.")]
        [SerializeField] private Sprite[] tabIcons = new Sprite[3];
        [Tooltip("Gold ingot shared with the match result screen.")]
        [SerializeField] private Sprite coinIcon;
        [Tooltip("Silver ingot shared with the match result screen.")]
        [SerializeField] private Sprite silverIcon;
        [SerializeField] private TMP_FontAsset bodyFont;
        [SerializeField] private Image profileFrame;
        [SerializeField] private ProfileFrameCatalog frameCatalog;

        #endregion

        #region Runtime State

        private PlayerInformationPanel panel;
        private UsernameChangeView rename;
        private CardDetailModal details;
        private PlayerProfileService profiles;
        private Button avatarButton;
        private GameObject previousSelection;
        private CancellationTokenSource cancellation;
        private string openedUid;
        private int generation;
        private int tab;
        private List<SeasonWindow> seasons;
        private int nextSeason;
        private int historyRows;
        private bool historyBusy;
        private bool historyFailed;
        private bool collectionDirty = true;
        private Sprite lastFrame;
        private RankCatalog rankCatalog;

        // Owned-card grid: artwork pops above the card frame, so the first row and the
        // end of the list keep a margin inside the scroll viewport.
        private const float CardWidth = 228;
        private const float CardHeight = 319;
        private const float CardStepX = 262;
        private const float CardStepY = 365;
        private const float CardTopPadding = 56;
        private bool childWasOpen;
        private readonly List<Selectable> blockedMenu = new List<Selectable>();
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("vi-VN");
        private string UserId => AuthManager.Instance?.CurrentUserId;
        private bool IsOpen => panel != null && panel.Root.activeSelf;

        #endregion

        #region Construction and Lifecycle

        public static void Attach(TMP_Text username)
        {
            var view = username.transform.parent.GetComponent<PlayerInformationView>();
            if (view == null)
            {
                Debug.LogError("[PlayerInformation] MainMenu is missing its authored view bindings.");
                return;
            }
            view.Initialize(username);
        }

        private void Initialize(TMP_Text username)
        {
            if (panel != null) return;
            Transform canvas = username.GetComponentInParent<Canvas>().rootCanvas.transform;
            TMP_FontAsset font = bodyFont != null ? bodyFont : username.font;
            panel = new PlayerInformationPanel(canvas, font, theme, tabIcons, coinIcon,
                silverIcon, SelectTab, Close);
            panel.HistoryScroll.onValueChanged.AddListener(_ => LoadMoreHistoryIfNeeded());
            UsernameChangeView.Attach(username);
            rename = username.transform.parent.GetComponent<UsernameChangeView>();
            rename.MoveTriggerTo(panel.Pages[0], PlayerInformationPanel.RenameTrigger);
            rename.ApplyTheme(theme, font);
            details = Instantiate(detailPrefab, canvas, false);
            details.name = "PlayerCardDetailModal";
            details.gameObject.SetActive(false);
            // The whole black portrait disc is clickable, not just the inset icon.
            var disc = avatar.transform.parent != null
                ? avatar.transform.parent.GetComponent<Image>() : null;
            Image hitArea = disc != null ? disc : avatar;
            hitArea.raycastTarget = true;
            avatar.raycastTarget = false;
            avatarButton = hitArea.GetComponent<Button>();
            if (avatarButton == null) avatarButton = hitArea.gameObject.AddComponent<Button>();
            avatarButton.targetGraphic = hitArea;
            // Locked (not unavailable) while another menu modal is open.
            ButtonDisabledStyle.LockOnly(avatarButton);
            var sound = hitArea.GetComponent<UIButtonSound>();
            if (sound == null) sound = hitArea.gameObject.AddComponent<UIButtonSound>();
            avatarButton.onClick.AddListener(sound.PlayClick);
            avatarButton.onClick.AddListener(Open);
        }

        private void OnEnable()
        {
            AuthManager.OnLogoutSuccess += CloseSession;
            AuthManager.OnLoginSuccess += CloseSession;
        }

        private void OnDisable()
        {
            AuthManager.OnLogoutSuccess -= CloseSession;
            AuthManager.OnLoginSuccess -= CloseSession;
            if (profiles != null) profiles.ProfileChanged -= ProfileChanged;
            profiles = null;
            CloseSession();
        }

        private void OnDestroy()
        {
            if (panel != null) Destroy(panel.Root);
            if (details != null) Destroy(details.gameObject);
        }

        private void Update()
        {
            if (profileFrame != null && profileFrame.sprite != lastFrame)
            {
                lastFrame = profileFrame.sprite;
                frameCatalog.AlignAvatar(profileFrame, (RectTransform)avatar.transform.parent);
            }
            if (!IsOpen) return;
            if (openedUid != UserId) { CloseSession(); return; }
            bool childOpen = rename.IsOpen || details.IsOpen;
            panel.Group.interactable = !childOpen;
            if (childWasOpen && !childOpen)
                EventSystem.current?.SetSelectedGameObject(panel.Tabs[tab].gameObject);
            childWasOpen = childOpen;
            // Details may already have consumed Escape earlier in this same frame.
            if (details.IsOpen || CardDetailModal.LastClosedFrame == Time.frameCount) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (rename.IsOpen) rename.CloseTop();
                else Close();
            }
        }

        #endregion

        #region Modal Commands

        public void Open()
        {
            if (IsOpen || string.IsNullOrEmpty(UserId)) return;
            previousSelection = EventSystem.current?.currentSelectedGameObject;
            if (profiles != PlayerProfileService.Instance)
            {
                if (profiles != null) profiles.ProfileChanged -= ProfileChanged;
                profiles = PlayerProfileService.Instance;
                if (profiles != null) profiles.ProfileChanged += ProfileChanged;
            }
            openedUid = UserId;
            panel.Root.transform.SetAsLastSibling();
            panel.Root.SetActive(true);
            panel.Group.interactable = true;
            childWasOpen = false;
            foreach (var selectable in panel.Root.GetComponentInParent<Canvas>().rootCanvas
                .GetComponentsInChildren<Selectable>())
            {
                if (!selectable.transform.IsChildOf(panel.Root.transform) && selectable.interactable)
                {
                    blockedMenu.Add(selectable);
                    selectable.interactable = false;
                }
            }
            SelectTab(0);
            Refresh();
            EventSystem.current?.SetSelectedGameObject(panel.Close.gameObject);
        }

        public void Close()
        {
            if (!IsOpen || rename.IsOpen || details.IsOpen) return;
            CloseSession();
            EventSystem.current?.SetSelectedGameObject(previousSelection);
        }

        private void CloseSession()
        {
            ++generation;
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            rename?.Dismiss();
            if (details != null && details.IsOpen) details.Hide();
            if (panel != null) panel.Root.SetActive(false);
            foreach (var selectable in blockedMenu)
                if (selectable != null) selectable.interactable = true;
            blockedMenu.Clear();
            openedUid = null;
            historyBusy = false;
        }

        private void SelectTab(int index)
        {
            tab = index;
            panel.Select(index);
            if (!IsOpen) return;
            // Reopening the tab retries a failed page; there is no separate retry button.
            if (index == 1 && !historyBusy && (seasons == null || historyFailed))
                _ = LoadHistoryAsync();
            if (index == 2 && collectionDirty) RenderCollection();
        }

        private void Refresh()
        {
            if (!IsOpen) return;
            ++generation;
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            seasons = null;
            nextSeason = historyRows = 0;
            historyBusy = historyFailed = false;
            Clear(panel.HistoryContent);
            panel.HistoryStatus.text = "";
            collectionDirty = true;
            RenderPersonal(profiles?.Current);
            _ = LoadProfileAsync(generation);
            _ = LoadEmailAsync(generation);
            _ = LoadRankAsync(generation);
            if (tab == 1) _ = LoadHistoryAsync();
            if (tab == 2) RenderCollection();
        }

        private bool Current(int request) => this != null && isActiveAndEnabled && IsOpen
            && request == generation && openedUid == UserId;

        #endregion

        #region Personal Data

        private async Task LoadProfileAsync(int request)
        {
            try
            {
                if (profiles == null) throw new InvalidOperationException();
                var result = await profiles.EnsureLoadedAsync(forceReload: true);
                if (!Current(request)) return;
                if (result.Success) ProfileChanged(result.Profile);
                else ShowProfileError();
            }
            catch (Exception) { if (Current(request)) ShowProfileError(); }
        }

        private void ShowProfileError()
        {
            if (profiles?.Current?.UserId == openedUid) return;
            panel.Values[0].text = panel.Values[3].text = panel.Values[4].text = "Chưa tải được";
            panel.CardsStatus.text = "Chưa tải được bộ sưu tập. Hãy đóng và mở lại để thử lại.";
        }

        private async Task LoadEmailAsync(int request)
        {
            panel.Values[1].text = "Đang tải…";
            try
            {
                string email = await AuthManager.Instance.GetCurrentEmailAsync();
                if (Current(request)) panel.Values[1].text = string.IsNullOrEmpty(email) ? "Chưa có email" : email;
            }
            catch (Exception) { if (Current(request)) panel.Values[1].text = "Chưa tải được"; }
        }

        private async Task LoadRankAsync(int request)
        {
            panel.Values[2].text = "Đang tải…";
            try
            {
                var gateway = LeaderboardService.Instance.Gateway;
                var season = await gateway.GetCurrentSeasonAsync();
                if (!Current(request)) return;
                if (!season.Success) throw new InvalidOperationException();
                if (!season.Value.HasValue) { panel.Values[2].text = "Chưa có mùa hiện tại"; return; }
                var standing = await gateway.GetStandingAsync(season.Value.Value.SeasonId, openedUid);
                if (!Current(request)) return;
                panel.Values[2].text = standing.Success
                    ? RankText(standing.Value?.Points ?? 0) : "Chưa tải được";
            }
            catch (Exception) { if (Current(request)) panel.Values[2].text = "Chưa tải được"; }
        }

        private void ProfileChanged(PlayerProfile profile)
        {
            if (!IsOpen) return;
            if (profile == null || profile.UserId != openedUid) { CloseSession(); return; }
            RenderPersonal(profile);
            collectionDirty = true;
            if (tab == 2) RenderCollection();
        }

        private void RenderPersonal(PlayerProfile profile)
        {
            bool valid = profile != null && profile.UserId == openedUid;
            panel.Values[0].text = valid ? profile.Username : "Đang tải…";
            panel.Values[3].text = valid ? profile.Wallet.Coins.ToString("N0", Culture) : "Đang tải…";
            panel.Values[4].text = valid ? profile.Wallet.Silver.ToString("N0", Culture) : "Đang tải…";
            // The rename policy allows exactly one change (PlayerProfile.CanChangeUsername).
            panel.RenameHint.text = valid
                ? $"Đã thay đổi tên: {(profile.CanChangeUsername ? 0 : 1)} / 1 lần" : "";
        }

        /// <summary>Formats the current standing as "&lt;tier&gt; - &lt;points&gt;".</summary>
        /// <remarks>A player without a standing this season is shown at 0 points.</remarks>
        private string RankText(int points)
        {
            if (rankCatalog == null) rankCatalog = RankCatalog.Load();
            RankTier tier = RankPolicy.TierFor(points);
            string name = rankCatalog != null ? rankCatalog.NameOf(tier) : RankCatalog.FallbackName(tier);
            return $"{name} - {points.ToString("N0", Culture)}";
        }

        #endregion

        #region Season History

        private async Task LoadHistoryAsync()
        {
            if (!IsOpen || historyBusy || cancellation == null) return;
            int request = generation;
            string uid = openedUid;
            CancellationToken token = cancellation.Token;
            historyBusy = true;
            historyFailed = false;
            panel.HistoryStatus.text = "Đang tải lịch sử mùa…";
            try
            {
                var gateway = LeaderboardService.Instance.Gateway;
                if (seasons == null)
                {
                    var result = await gateway.ListSeasonsAsync();
                    if (!Current(request)) return;
                    if (!result.Success) throw new InvalidOperationException();
                    seasons = PlayerSeasonHistory.Completed(result.Value, ServerClock.UtcNowMs);
                }
                var page = await PlayerSeasonHistory.ReadPageAsync(gateway, seasons, nextSeason, uid, token);
                if (!Current(request)) return;
                if (!page.Success) throw new InvalidOperationException();
                foreach (var row in page.Value) AddHistoryRow(row);
                nextSeason = Math.Min(seasons.Count, nextSeason + PlayerSeasonHistory.PageSize);
                panel.HistoryStatus.text = historyRows == 0
                    ? (nextSeason < seasons.Count ? "Chưa có dữ liệu trong các mùa vừa tải." : "Chưa có lịch sử xếp hạng.")
                    : "Kết quả các mùa đã kết thúc";
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!Current(request)) return;
                historyFailed = true;
                panel.HistoryStatus.text = "Không tải được lịch sử. Chọn lại tab để thử lại.";
            }
            finally
            {
                if (Current(request))
                {
                    historyBusy = false;
                    LoadMoreHistoryIfNeeded();
                }
            }
        }

        /// <summary>
        /// Requests the next season page when the list is scrolled near its end, or when
        /// the loaded rows do not yet fill the viewport (nothing could be scrolled).
        /// </summary>
        /// <remarks>Stops after a failure so an offline gateway is not retried in a loop.</remarks>
        private void LoadMoreHistoryIfNeeded()
        {
            if (!IsOpen || tab != 1 || historyBusy || historyFailed || seasons == null
                || nextSeason >= seasons.Count) return;
            var scroll = panel.HistoryScroll;
            bool fits = panel.HistoryContent.rect.height <= scroll.viewport.rect.height;
            if (fits || scroll.verticalNormalizedPosition <= .05f) _ = LoadHistoryAsync();
        }

        private void AddHistoryRow(PlayerSeasonRow row)
        {
            float y = -35 - historyRows++ * 70;
            float width = PlayerInformationPanel.HistoryWidth;
            var root = PlayerInformationPanel.Image("Season" + row.SeasonId, panel.HistoryContent,
                0, y, width - 10, 64, new Color(1, 1, 1, historyRows % 2 == 0 ? .05f : .02f));
            TopAnchor(root.rectTransform);
            if (rankCatalog == null) rankCatalog = RankCatalog.Load();
            string tier = rankCatalog != null ? rankCatalog.NameOf(row.Standing.Tier)
                : RankCatalog.FallbackName(row.Standing.Tier);
            string[] values = { "Mùa " + row.SeasonId, row.Standing.Points.ToString("N0", Culture),
                tier, row.Standing.WinRate.ToString("P1", Culture) };
            float[] columns = PlayerInformationPanel.HistoryColumns;
            for (int i = 0; i < values.Length; i++)
                panel.Text("Cell" + i, root.transform, values[i], columns[i], 0, 240, 55, 25, true);
            panel.HistoryContent.sizeDelta = new Vector2(width, historyRows * 70);
        }

        #endregion

        #region Owned Cards

        private void RenderCollection()
        {
            Clear(panel.CardsContent);
            var profile = profiles?.Current;
            if (profile == null || profile.UserId != openedUid)
            {
                panel.CardsStatus.text = "Đang tải bộ sưu tập…";
                return;
            }
            collectionDirty = false;
            var cards = profile.OwnedCardIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            panel.CardsStatus.text = cards.Length == 0 ? "Bạn chưa sở hữu lá bài nào."
                : $"Đã sở hữu {cards.Length} lá bài";
            for (int i = 0; i < cards.Length; i++)
            {
                Card card = Resources.Load<Card>("Cards/" + cards[i]);
                var artwork = presentation.Find(cards[i]);
                string name = card != null ? card.cardName : cards[i];
                var data = new CardDetailData(name, card != null ? card.energy : 0,
                    card != null ? card.damage : 0, card != null ? card.health : 0,
                    artwork, card != null ? card.rulesText : "Nội dung thẻ chưa khả dụng.",
                    card != null ? card.keywords : UnitKeyword.None);
                float x = (i % 4 - 1.5f) * CardStepX;
                float y = -(CardTopPadding + CardHeight * .5f) - i / 4 * CardStepY;
                // Transparent hit area for right-click; the face is copied from the detail
                // modal so tiles show the same cost/damage/health badges.
                var tile = PlayerInformationPanel.Image(cards[i], panel.CardsContent,
                    x, y, CardWidth, CardHeight, new Color(0, 0, 0, 0));
                tile.raycastTarget = true;
                TopAnchor(tile.rectTransform);
                var face = details.CreateCardFace(data, tile.rectTransform, CardWidth);
                if (face == null)
                    panel.Text("Name", tile.transform, name, 0, 0, CardWidth, 48, 22, true);
                tile.gameObject.AddComponent<OwnedCardItem>().Bind(data, details);
            }
            int rows = (cards.Length + 3) / 4;
            panel.CardsContent.sizeDelta = new Vector2(PlayerInformationPanel.CardsWidth,
                rows == 0 ? 0 : CardTopPadding * 2 + rows * CardStepY - (CardStepY - CardHeight));
        }

        /// <summary>
        /// Anchors a scroll item to the content's top edge; y offsets are measured from
        /// there, so items no longer drift by half the content height.
        /// </summary>
        private static void TopAnchor(RectTransform item)
        {
            item.anchorMin = item.anchorMax = new Vector2(.5f, 1);
        }

        private static void Clear(RectTransform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            content.sizeDelta = new Vector2(content.sizeDelta.x, 0);
            content.anchoredPosition = Vector2.zero;
        }

        #endregion
    }
}
