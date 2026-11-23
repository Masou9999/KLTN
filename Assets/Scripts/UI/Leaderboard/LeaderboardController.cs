using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using KLTN.Game.Content;
using KLTN.Game.Domain.Ranking;
using KLTN.Infrastructure.Firestore;
using KLTN.Infrastructure.Ranking;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KLTN.UI.Leaderboard
{
    /// <summary>
    /// Seasonal leaderboard screen: season list on the left, title "MÙA N" + countdown,
    /// paged table, and the viewer's own row pinned to the bottom of the table while the
    /// real row is below the visible area (or not loaded yet). Scrolling down to it makes
    /// the pinned copy disappear so the row sits in its place.
    /// </summary>
    public sealed class LeaderboardController : MonoBehaviour
    {
        #region Inspector

        [Header("Header")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI countdownText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button backButton;

        [Header("Season List")]
        [SerializeField] private RectTransform seasonListContent;
        [SerializeField] private Button seasonButtonTemplate;

        [Header("Table")]
        [SerializeField] private ScrollRect tableScroll;
        [SerializeField] private RectTransform tableContent;
        [SerializeField] private LeaderboardRowView rowTemplate;
        [SerializeField] private LeaderboardRowView pinnedSelfRow;

        [Tooltip("Must match the row height used by the scene builder; pinning maths relies on fixed-height rows.")]
        [SerializeField] private float rowHeight = 76f;
        [SerializeField] private float rowSpacing = 6f;

        [Tooltip("Load the next page when the scroll position is this close to the bottom (0..1).")]
        [Range(0f, 0.5f)]
        [SerializeField] private float loadMoreThreshold = 0.08f;

        #endregion

        #region Runtime State

        private static readonly Color SelectedSeason = new Color(0.78f, 0.62f, 0.25f, 1f);
        private static readonly Color IdleSeason = new Color(0.22f, 0.19f, 0.21f, 1f);

        private readonly List<LeaderboardRowView> rows = new List<LeaderboardRowView>();
        private readonly List<(SeasonWindow window, Button button)> seasonButtons =
            new List<(SeasonWindow window, Button button)>();

        private LeaderboardService service;
        private RankCatalog catalog;
        private RankingSettings settings;

        private SeasonWindow? selectedSeason;
        private int? currentSeasonId;
        private LeaderboardCursor nextCursor;
        private bool hasMore;
        private bool loadingPage;
        private int loadVersion;

        private SeasonStanding myStanding;
        private int myPosition;
        private float nextCountdownRefresh;
        private bool reloadingAfterReset;

        #endregion

        #region Unity Lifecycle

        private async void Start()
        {
            service = LeaderboardService.Instance;
            if (service != null) service.UsernameChanged += HandleUsernameChanged;
            catalog = RankCatalog.Load();
            settings = RankingSettings.Load();

            if (backButton != null)
            {
                backButton.onClick.AddListener(() => SceneManager.LoadScene(SceneNames.MainMenu));
            }

            rowTemplate.gameObject.SetActive(false);
            seasonButtonTemplate.gameObject.SetActive(false);
            pinnedSelfRow.gameObject.SetActive(false);
            tableScroll.onValueChanged.AddListener(_ => HandleScroll());
            SetTitle(null);

            if (service == null)
            {
                SetStatus("Không có dịch vụ bảng xếp hạng (chưa đăng nhập?).");
                return;
            }

            await LoadSeasonsAsync();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCountdownRefresh)
            {
                return;
            }

            nextCountdownRefresh = Time.unscaledTime + 0.5f;
            RefreshCountdown();
        }

        #endregion

        private void OnDestroy()
        {
            if (service != null) service.UsernameChanged -= HandleUsernameChanged;
        }

        private async void HandleUsernameChanged()
        {
            ++loadVersion;
            nextCursor = null;
            loadingPage = false;
            try
            {
                await LoadSeasonsAsync();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus("Không tải được bảng xếp hạng: " + exception.Message);
            }
        }

        #region Seasons

        private async Task LoadSeasonsAsync()
        {
            SetStatus("Đang tải mùa giải...");

            RankingResult<SeasonWindow?> current = await service.RefreshSeasonAsync();
            RankingResult<IReadOnlyList<SeasonWindow>> list = await service.Gateway.ListSeasonsAsync();

            if (this == null)
            {
                return;
            }

            if (!list.Success)
            {
                SetStatus("Không tải được danh sách mùa: " + list.Message);
                return;
            }

            currentSeasonId = current.Success && current.Value.HasValue ? current.Value.Value.SeasonId : (int?)null;
            var seasons = new List<SeasonWindow>(list.Value);

            // config/season may be newer than the listing (eventual consistency); make sure it is shown.
            if (current.Success && current.Value.HasValue && !seasons.Exists(s => s.SeasonId == current.Value.Value.SeasonId))
            {
                seasons.Insert(0, current.Value.Value);
            }

            if (seasons.Count == 0)
            {
                SetStatus("Chưa có mùa giải nào. Kiểm tra lịch Cloud Code AdvanceSeason.");
                return;
            }

            BuildSeasonButtons(seasons);
            SetStatus(string.Empty);
            await SelectSeasonAsync(seasons[0]);
        }

        private void BuildSeasonButtons(IReadOnlyList<SeasonWindow> seasons)
        {
            foreach ((SeasonWindow window, Button button) entry in seasonButtons)
            {
                Destroy(entry.button.gameObject);
            }

            seasonButtons.Clear();

            foreach (SeasonWindow season in seasons)
            {
                Button button = Instantiate(seasonButtonTemplate, seasonListContent);
                button.gameObject.SetActive(true);
                button.name = "Season" + season.SeasonId;

                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);

                if (label != null)
                {
                    label.text = season.SeasonId == currentSeasonId
                        ? $"Mùa {season.SeasonId}  •  hiện tại"
                        : $"Mùa {season.SeasonId}";
                }

                SeasonWindow captured = season;
                button.onClick.AddListener(() => _ = SelectSeasonAsync(captured));
                seasonButtons.Add((season, button));
            }
        }

        private async Task SelectSeasonAsync(SeasonWindow season)
        {
            int version = ++loadVersion;
            selectedSeason = season;
            SetTitle(season);
            HighlightSeason(season.SeasonId);
            ClearRows();
            myStanding = null;
            myPosition = 0;
            nextCursor = null;
            hasMore = false;
            SetStatus("Đang tải bảng xếp hạng...");

            string uid = service.CurrentUserId;
            // Running season: make sure the player has a row (0 points) so they are always listed.
            RankingResult<SeasonStanding> mine = season.SeasonId == currentSeasonId
                ? await service.EnsureMyStandingAsync(season.SeasonId)
                : await service.Gateway.GetStandingAsync(season.SeasonId, uid);

            if (this == null || version != loadVersion)
            {
                return;
            }

            if (mine.Success && mine.Value != null)
            {
                myStanding = mine.Value;
                RankingResult<int> position = await service.Gateway.GetPositionAsync(season.SeasonId, myStanding);

                if (this == null || version != loadVersion)
                {
                    return;
                }

                myPosition = position.Success ? position.Value : 0;
            }

            await LoadNextPageAsync(version);
        }

        private void HighlightSeason(int seasonId)
        {
            foreach ((SeasonWindow window, Button button) entry in seasonButtons)
            {
                if (entry.button.targetGraphic != null)
                {
                    entry.button.targetGraphic.color = entry.window.SeasonId == seasonId ? SelectedSeason : IdleSeason;
                }
            }
        }

        #endregion

        #region Table

        private async Task LoadNextPageAsync(int version)
        {
            if (!selectedSeason.HasValue || loadingPage)
            {
                return;
            }

            loadingPage = true;
            int seasonId = selectedSeason.Value.SeasonId;
            RankingResult<LeaderboardPage> page =
                await service.Gateway.GetPageAsync(seasonId, nextCursor, settings.PageSize);
            loadingPage = false;

            if (this == null || version != loadVersion)
            {
                return;
            }

            if (!page.Success)
            {
                SetStatus("Không tải được bảng xếp hạng: " + page.Message);
                return;
            }

            string uid = service.CurrentUserId;

            foreach (SeasonStanding standing in page.Value.Rows)
            {
                int position = rows.Count + 1;
                bool isSelf = !string.IsNullOrEmpty(uid) && standing.UserId == uid;

                if (isSelf)
                {
                    // The loaded order is exact; the COUNT-based position may be off on ties.
                    myPosition = position;
                    myStanding = standing;
                }

                LeaderboardRowView row = Instantiate(rowTemplate, tableContent);
                row.gameObject.SetActive(true);
                row.Bind(position, standing, isSelf, catalog);
                rows.Add(row);
            }

            nextCursor = page.Value.Next;
            hasMore = page.Value.HasMore;
            SetStatus(rows.Count == 0 ? "Mùa này chưa có ai thi đấu xếp hạng." : string.Empty);

            if (myStanding != null)
            {
                pinnedSelfRow.Bind(myPosition, myStanding, isSelf: true, catalog);
            }

            ResizeContent();
            HandleScroll();
        }

        private void ClearRows()
        {
            foreach (LeaderboardRowView row in rows)
            {
                Destroy(row.gameObject);
            }

            rows.Clear();
            pinnedSelfRow.gameObject.SetActive(false);
            tableContent.anchoredPosition = Vector2.zero;
            ResizeContent();
        }

        /// <summary>
        /// Rows are laid out by the VerticalLayoutGroup; the content gets one extra row of bottom
        /// space so the last row can scroll above the pinned copy.
        /// </summary>
        private void ResizeContent()
        {
            float height = rows.Count * (rowHeight + rowSpacing) + rowHeight;
            tableContent.sizeDelta = new Vector2(tableContent.sizeDelta.x, height);
        }

        private void HandleScroll()
        {
            UpdatePinnedRow();

            if (hasMore && !loadingPage && tableScroll.verticalNormalizedPosition <= loadMoreThreshold)
            {
                _ = LoadNextPageAsync(loadVersion);
            }
        }

        /// <summary>
        /// Fixed-height rows make this O(1): row i spans [i*stride, i*stride + rowHeight] from the
        /// content top, and the viewport shows [scrollY, scrollY + viewportHeight].
        /// </summary>
        private void UpdatePinnedRow()
        {
            if (myStanding == null || myPosition <= 0)
            {
                pinnedSelfRow.gameObject.SetActive(false);
                return;
            }

            int index = myPosition - 1;
            float stride = rowHeight + rowSpacing;
            float rowBottom = index * stride + rowHeight;
            float viewportHeight = ((RectTransform)tableScroll.viewport.transform).rect.height;
            // Keep the pinned row's own height free at the bottom of the viewport.
            float visibleBottom = tableContent.anchoredPosition.y + viewportHeight - rowHeight;
            bool loaded = index < rows.Count;
            bool rowIsBelowView = !loaded || rowBottom > visibleBottom;

            pinnedSelfRow.gameObject.SetActive(rowIsBelowView);
        }

        #endregion

        #region Header

        private void SetTitle(SeasonWindow? season)
        {
            if (titleText != null)
            {
                titleText.text = season.HasValue
                    ? $"BẢNG XẾP HẠNG — MÙA {season.Value.SeasonId}"
                    : "BẢNG XẾP HẠNG";
            }

            RefreshCountdown();
        }

        private void RefreshCountdown()
        {
            if (countdownText == null)
            {
                return;
            }

            if (!selectedSeason.HasValue)
            {
                countdownText.text = string.Empty;
                return;
            }

            SeasonWindow season = selectedSeason.Value;
            long now = ServerClock.UtcNowMs;

            // Seasons follow the clock: when the running one ends, the next is already open.
            if (season.SeasonId == currentSeasonId && season.IsEnded(now) && !reloadingAfterReset)
            {
                _ = ReloadAfterResetAsync();
            }

            if (season.SeasonId != currentSeasonId || season.IsEnded(now))
            {
                DateTime ended = DateTimeOffset.FromUnixTimeMilliseconds(season.EndUtcMs).LocalDateTime;
                countdownText.text = "Đã kết thúc " + ended.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
                return;
            }

            string remaining = FormatDuration(season.MillisecondsUntilEnd(now));
            countdownText.text = season.IsLocked(now)
                ? $"Đã khóa điểm · reset sau {remaining}"
                : $"Kết thúc sau {remaining}";
        }

        private async Task ReloadAfterResetAsync()
        {
            reloadingAfterReset = true;

            try
            {
                await LoadSeasonsAsync();
            }
            finally
            {
                reloadingAfterReset = false;
            }
        }

        /// <summary>Formats a remaining duration as dd:hh:mm:ss (days:hours:minutes:seconds).</summary>
        private static string FormatDuration(long milliseconds)
        {
            TimeSpan span = TimeSpan.FromMilliseconds(Math.Max(0L, milliseconds));
            return $"{span.Days:00}:{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        #endregion

#if UNITY_EDITOR
        public void EditorWire(
            TextMeshProUGUI title,
            TextMeshProUGUI countdown,
            TextMeshProUGUI status,
            Button back,
            RectTransform seasonContent,
            Button seasonTemplate,
            ScrollRect scroll,
            RectTransform content,
            LeaderboardRowView template,
            LeaderboardRowView pinned,
            float height,
            float spacing
        )
        {
            titleText = title;
            countdownText = countdown;
            statusText = status;
            backButton = back;
            seasonListContent = seasonContent;
            seasonButtonTemplate = seasonTemplate;
            tableScroll = scroll;
            tableContent = content;
            rowTemplate = template;
            pinnedSelfRow = pinned;
            rowHeight = height;
            rowSpacing = spacing;
        }
#endif
    }
}
