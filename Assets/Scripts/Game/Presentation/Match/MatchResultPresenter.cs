using UnityEngine;
using TMPro;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Game.Domain.Ranking;
using KLTN.Game.Content;
using KLTN.Infrastructure.Economy;
using KLTN.Infrastructure.Ranking;

/// <summary>
/// Result screen. Rewards and season points are written only by the backend (via the
/// dedicated server); this presenter shows the settlement it receives and then re-reads
/// the wallet and standing (FR-SHOP-09, FR-RANK-10/11, FR-SURR-03).
/// </summary>
public class MatchResultPresenter : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TextMeshProUGUI resultTitleText;

    [Tooltip("Optional. Shows the coins/silver earned, e.g. \"+75 xu · +1 bạc\".")]
    [SerializeField] private TextMeshProUGUI rewardText;

    [Tooltip("Optional. Shows season points, e.g. \"+30 điểm · Bạc → Vàng\". Cloned from rewardText when empty.")]
    [SerializeField] private TextMeshProUGUI rankText;

    private const string VictoryColor = "#FFD700";
    private const string DefeatColor = "#CC0000";
    private const string DrawColor = "#B8C4D0";

    private MatchClientProjection projection;

    // Last finished match seen in a snapshot; settlements for other matches are ignored.
    private string finishedMatchId;
    private MatchOutcome finishedOutcome = MatchOutcome.Running;
    private SeatId viewerSeat;
    private string refreshedForMatchId;

    private void Start()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
    }

    private void OnEnable()
    {
        projection = MatchProjectionRegistry.Current;
        if (projection != null)
        {
            projection.SnapshotChanged += HandleSnapshotChanged;
            if (projection.Current != null)
            {
                HandleSnapshotChanged(projection.Current);
            }
        }

        MatchSettlementRegistry.SettlementChanged += HandleSettlementChanged;
    }

    private void OnDisable()
    {
        if (projection != null)
        {
            projection.SnapshotChanged -= HandleSnapshotChanged;
        }

        MatchSettlementRegistry.SettlementChanged -= HandleSettlementChanged;
    }

    private void HandleSnapshotChanged(MatchSnapshotDto snapshot)
    {
        if (snapshot == null) return;

        MatchOutcome outcome = (MatchOutcome)snapshot.outcome;

        if (outcome == MatchOutcome.Running) return;

        bool firstTime = finishedMatchId != snapshot.matchId || finishedOutcome == MatchOutcome.Running;
        finishedMatchId = snapshot.matchId;
        finishedOutcome = outcome;
        viewerSeat = (SeatId)snapshot.viewerSeat;

        ShowResult(outcome, viewerSeat);

        if (!firstTime)
        {
            return;
        }

        MatchSettlementDto settlement = MatchSettlementRegistry.Current;

        if (settlement != null && settlement.matchId == finishedMatchId)
        {
            ShowSettlement(settlement);
        }
        else
        {
            SetRewardText("Đang chờ kết quả từ máy chủ...");
            SetRankText(string.Empty);
        }
    }

    private void HandleSettlementChanged(MatchSettlementDto settlement)
    {
        if (settlement == null)
        {
            return;
        }

        // The settlement can arrive just before the final snapshot; it is shown from there.
        if (finishedOutcome == MatchOutcome.Running || settlement.matchId != finishedMatchId)
        {
            return;
        }

        ShowSettlement(settlement);
    }

    #region Settlement

    private void ShowSettlement(MatchSettlementDto settlement)
    {
        switch ((MatchSettlementState)settlement.state)
        {
            case MatchSettlementState.PrivateMatch:
                SetRewardText("Trận riêng: không tính thưởng/điểm.");
                SetRankText(string.Empty);
                return;

            case MatchSettlementState.Saving:
                SetRewardText("Đang lưu kết quả...");
                SetRankText(string.Empty);
                return;

            case MatchSettlementState.Failed:
                SetRewardText("Lưu kết quả thất bại" + (string.IsNullOrEmpty(settlement.message) ? "." : ": " + settlement.message));
                SetRankText(string.Empty);
                return;
        }

        SetRewardText(FormatReward(settlement));
        SetRankText(FormatRank(settlement));
        RefreshProfileAndStanding(settlement.matchId);
    }

    private static string FormatReward(MatchSettlementDto settlement)
    {
        if (settlement.result == "surrender_early")
        {
            return "Đầu hàng trước vòng 3: không nhận thưởng";
        }

        if (settlement.coins <= 0 && settlement.silver <= 0)
        {
            return !settlement.ranked && !string.IsNullOrEmpty(settlement.notScoredReason)
                ? settlement.notScoredReason
                : "Không nhận thưởng";
        }

        return settlement.silver > 0
            ? $"+{settlement.coins} xu · +{settlement.silver} bạc"
            : $"+{settlement.coins} xu";
    }

    private static string FormatRank(MatchSettlementDto settlement)
    {
        if (!settlement.ranked)
        {
            return string.IsNullOrEmpty(settlement.notScoredReason)
                ? "Trận không xếp hạng: không tính điểm."
                : settlement.notScoredReason;
        }

        RankCatalog catalog = RankCatalog.Load();
        string after = TierName(catalog, settlement.tierAfter);
        string tiers = after;

        if (settlement.tierBefore != settlement.tierAfter)
        {
            tiers = TierName(catalog, settlement.tierBefore) + " → " + after;
        }

        int applied = settlement.pointsAfter - settlement.pointsBefore;
        return $"{FormatDelta(applied)} điểm · {settlement.pointsAfter} điểm · {tiers}";
    }

    private static string TierName(RankCatalog catalog, int tierValue)
    {
        RankTier tier = tierValue >= 0 && tierValue < RankPolicy.TierCount ? (RankTier)tierValue : RankTier.Bronze;
        return catalog != null ? catalog.NameOf(tier) : RankCatalog.FallbackName(tier);
    }

    private static string FormatDelta(int delta)
    {
        return delta > 0 ? "+" + delta : delta.ToString();
    }

    /// <summary>Reads the wallet and season row the backend just updated (read-only).</summary>
    private async void RefreshProfileAndStanding(string matchId)
    {
        if (string.IsNullOrEmpty(matchId) || matchId == refreshedForMatchId)
        {
            return;
        }

        refreshedForMatchId = matchId;

        try
        {
            PlayerProfileService profiles = PlayerProfileService.Instance;

            if (profiles != null)
            {
                await profiles.EnsureLoadedAsync(forceReload: true);
            }

            LeaderboardService leaderboard = LeaderboardService.Instance;

            if (leaderboard != null)
            {
                await leaderboard.GetMyCurrentTierAsync();
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[MatchResult] Could not refresh wallet/standing: {exception.Message}");
        }
    }

    #endregion

    #region Text

    private void SetRankText(string text)
    {
        if (rankText == null && rewardText != null)
        {
            // GameScene has no slot for it yet: put a copy of the reward line just below it.
            rankText = Instantiate(rewardText, rewardText.transform.parent);
            rankText.name = "RankText";
            var source = rewardText.rectTransform;
            rankText.rectTransform.anchoredPosition = source.anchoredPosition - new Vector2(0f, source.rect.height + 8f);
        }

        if (rankText != null)
        {
            rankText.text = text;
        }
    }

    private void SetRewardText(string text)
    {
        if (rewardText != null)
        {
            rewardText.text = text;
        }
    }

    #endregion

    #region Title

    /// <summary>Shows VICTORY, DEFEAT or DRAW for the viewer (FR-SURR-03).</summary>
    public void ShowResult(MatchOutcome outcome, SeatId viewer)
    {
        MatchResultBanner banner = MatchResultBannerPolicy.For(outcome, viewer);

        if (banner == MatchResultBanner.None)
        {
            return;
        }

        if (resultPanel != null) resultPanel.SetActive(true);

        switch (banner)
        {
            case MatchResultBanner.Victory:
                SetTitle("CHIẾN THẮNG", VictoryColor);
                break;
            case MatchResultBanner.Draw:
                SetTitle("HÒA", DrawColor);
                break;
            default:
                SetTitle("THẤT BẠI", DefeatColor);
                break;
        }
    }

    /// <summary>
    /// Kept for CombatAnimationDirector, which only knows win/lose. Uses the snapshot's
    /// outcome when available so a draw is not shown as a defeat.
    /// </summary>
    public void ShowResult(bool isVictory)
    {
        if (finishedOutcome != MatchOutcome.Running)
        {
            ShowResult(finishedOutcome, viewerSeat);
            return;
        }

        if (resultPanel != null) resultPanel.SetActive(true);

        if (isVictory)
        {
            SetTitle("CHIẾN THẮNG", VictoryColor);
        }
        else
        {
            SetTitle("THẤT BẠI", DefeatColor);
        }
    }

    private void SetTitle(string text, string htmlColor)
    {
        if (resultTitleText == null)
        {
            return;
        }

        resultTitleText.text = text;
        ColorUtility.TryParseHtmlString(htmlColor, out Color color);
        resultTitleText.color = color;
    }

    #endregion

    public void OnReturnButtonClicked()
    {
        Debug.Log("Ngắt kết nối mạng và quay về Main Menu...");
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        SceneManager.LoadScene("MainMenu");
    }
}
