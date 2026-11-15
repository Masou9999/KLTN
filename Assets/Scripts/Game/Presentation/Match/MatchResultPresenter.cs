using UnityEngine;
using TMPro;
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
    [Header("Match Result Audio")]
    [SerializeField] private AudioSource resultAudioSource;
    [SerializeField] private AudioClip victoryClip;
    [SerializeField] private AudioClip defeatClip;

    private bool hasPlayedResultAudio = false;
    private float rankedProgressTarget = 0f;
    private float rankedProgressStart = 0f;
    private bool isRankedResultUIActive = false;
    [Header("New Match Result UI")]
    [SerializeField] private TextMeshProUGUI coinRewardText;
    [SerializeField] private TextMeshProUGUI silverRewardText;
    [SerializeField] private TextMeshProUGUI rankRewardText;
    [SerializeField] private TextMeshProUGUI totalRankText;

    [SerializeField] private UnityEngine.UI.Image rankBeforeIcon;
    [SerializeField] private UnityEngine.UI.Image rankAfterIcon;

    [Header("Ranked Match Result UI")]
    [SerializeField] private GameObject rankedResultDimBackground;
    [SerializeField] private GameObject rankedResultStage1;
    [SerializeField] private GameObject rankedResultStage2;
    [SerializeField] private Sprite rankedVictorySprite;
    [SerializeField] private Sprite rankedDefeatSprite;
    [SerializeField] private TextMeshProUGUI rankedCoinRewardText;
    [SerializeField] private TextMeshProUGUI rankedSilverRewardText;
    [SerializeField] private TextMeshProUGUI rankedPointRewardText;
    [SerializeField] private UnityEngine.UI.Image rankedCurrentRankIcon;
    [SerializeField] private UnityEngine.UI.Image rankedNextRankIcon;
    [SerializeField] private UnityEngine.UI.Image rankProgressFill;
    [SerializeField] private TMPro.TextMeshProUGUI rankProgressText;
    private int rankedPointsBefore;
    private int rankedPointsAfter;
    private int rankedPointsMax;
    private int rankedPointsMaxBefore;
    private bool rankedTierChanged;
    private RankTier rankedTierDisplayed;
    private RankTier rankedTierBefore;
    private RankTier rankedTierAfter;

    [SerializeField] private UnityEngine.UI.Button rankedContinueButton;
    [SerializeField] private UnityEngine.UI.Button rankedReturnButton;

    [Header("Match Result Backgrounds")]
    [SerializeField] private UnityEngine.UI.Image matchResultBackground;

    [SerializeField] private Sprite victoryRankChanged;
    [SerializeField] private Sprite victoryRankUnchanged;
    [SerializeField] private Sprite defeatRankChanged;
    [SerializeField] private Sprite defeatRankUnchanged;

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
        
        isRankedResultUIActive = false;

        if (resultPanel != null)
            resultPanel.SetActive(false);

        if (rankedResultDimBackground != null)
            rankedResultDimBackground.SetActive(false);

        if (rankedResultStage1 != null)
            rankedResultStage1.SetActive(false);

        if (rankedResultStage2 != null)
            rankedResultStage2.SetActive(false);
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
    if (snapshot == null)
        return;

    MatchOutcome outcome = (MatchOutcome)snapshot.outcome;

    if (outcome == MatchOutcome.Running)
        return;

    bool firstTime =
        finishedMatchId != snapshot.matchId ||
        finishedOutcome == MatchOutcome.Running;

    finishedMatchId = snapshot.matchId;
    finishedOutcome = outcome;
    viewerSeat = (SeatId)snapshot.viewerSeat;

    // Đặt lại trạng thái giao diện khi có trận mới.
    if (firstTime)
        isRankedResultUIActive = false;

    

if (!firstTime)
{
    return;
}


    // Reset âm thanh cho kết quả trận đấu mới.
    hasPlayedResultAudio = false;

    if (resultAudioSource != null)
    {
        resultAudioSource.Stop();
    }

    // Xóa số liệu cũ trước khi hiển thị kết quả trận mới.
    ClearNewResultUI();

    
if (!MatchConnectionController.IsDedicatedMatch)
{
    ShowResult(outcome, viewerSeat);
}


    MatchSettlementDto settlement = MatchSettlementRegistry.Current;

    if (settlement != null &&
        settlement.matchId == finishedMatchId)
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

        
if (isRankedResultUIActive &&
    (MatchSettlementState)settlement.state == MatchSettlementState.Saved &&
    settlement.ranked)
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
            ShowNewResultMessage("Trận riêng: không tính thưởng/điểm.");
            return;

        case MatchSettlementState.Saving:
            SetRewardText("Đang lưu kết quả...");
            SetRankText(string.Empty);
            ShowNewResultMessage("Đang lưu kết quả...");
            return;

        
case MatchSettlementState.Failed:
    string errorMessage = "Lưu kết quả thất bại" +
        (string.IsNullOrEmpty(settlement.message)
            ? "."
            : ": " + settlement.message);

    isRankedResultUIActive = false;

    ShowResult(finishedOutcome, viewerSeat);

    SetRewardText(errorMessage);
    SetRankText(string.Empty);
    ShowNewResultMessage(errorMessage);
    return;

    }

    SetRewardText(FormatReward(settlement));
    SetRankText(FormatRank(settlement));
    UpdateNewResultUI(settlement);
    UpdateRankIcons(settlement);

    

if (!settlement.ranked)
{
    isRankedResultUIActive = false;
    ShowResult(finishedOutcome, viewerSeat);
}
else
{
    UpdateRankedRewardUI(settlement);

    UpdateRankedStage2Icons(settlement);
    UpdateRankedProgress(settlement);

    MatchResultBanner rankedBanner =
        MatchResultBannerPolicy.For(finishedOutcome, viewerSeat);
    
    
if (rankedBanner == MatchResultBanner.Draw)
{
    isRankedResultUIActive = false;
    ShowResult(finishedOutcome, viewerSeat);
}


    if (rankedBanner == MatchResultBanner.Victory ||
        rankedBanner == MatchResultBanner.Defeat)
    {
        isRankedResultUIActive = true;

        ShowRankedStage1(
            rankedBanner == MatchResultBanner.Victory
        );
    }
}


    MatchResultBanner banner =
    MatchResultBannerPolicy.For(finishedOutcome, viewerSeat);

bool rankChanged = settlement.tierBefore != settlement.tierAfter;

if (banner == MatchResultBanner.Victory)
{
    UpdateResultBackground(true, rankChanged);
    PlayMatchResultAudio(true);
}
else if (banner == MatchResultBanner.Defeat)
{
    UpdateResultBackground(false, rankChanged);
    PlayMatchResultAudio(false);
}

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
    private void PlayMatchResultAudio(bool isVictory)
{
    if (hasPlayedResultAudio)
        return;

    if (resultAudioSource == null)
        return;

    AudioClip selectedClip = isVictory
        ? victoryClip
        : defeatClip;

    if (selectedClip == null)
        return;

    resultAudioSource.Stop();
    resultAudioSource.clip = selectedClip;
    resultAudioSource.Play();

    hasPlayedResultAudio = true;
}
    private void UpdateRankIcons(MatchSettlementDto settlement)
{
    if (settlement == null)
        return;

    RankCatalog catalog = RankCatalog.Load();

    if (catalog == null)
        return;

    if (rankBeforeIcon != null)
    {
        Sprite beforeSprite =
            catalog.IconOf((RankTier)settlement.tierBefore);

        rankBeforeIcon.sprite = beforeSprite;
        rankBeforeIcon.enabled = beforeSprite != null;
    }

    if (rankAfterIcon != null)
    {
        Sprite afterSprite =
            catalog.IconOf((RankTier)settlement.tierAfter);

        rankAfterIcon.sprite = afterSprite;
        rankAfterIcon.enabled = afterSprite != null;
    }
}
    private void UpdateNewResultUI(MatchSettlementDto settlement)
{
    if (settlement == null)
        return;
    if (settlement.result == "surrender_early")
{
    ClearNewResultUI();
    return;
}

    if (coinRewardText != null)
        coinRewardText.text = $"+{settlement.coins} xu";

    if (silverRewardText != null)
        silverRewardText.text = $"+{settlement.silver} bạc";

    int rankDelta = settlement.pointsAfter - settlement.pointsBefore;

    if (rankRewardText != null)
        rankRewardText.text = settlement.ranked
            ? $"{(rankDelta >= 0 ? "+" : "")}{rankDelta} điểm"
            : "";

    if (totalRankText != null)
        totalRankText.text = settlement.ranked
            ? $"{settlement.pointsAfter} điểm"
            : "";
}
private void ClearNewResultUI()
{
    if (coinRewardText != null)
        coinRewardText.text = "";

    if (silverRewardText != null)
        silverRewardText.text = "";

    if (rankRewardText != null)
        rankRewardText.text = "";

    if (totalRankText != null)
        totalRankText.text = "";
    
    if (rankBeforeIcon != null)
        rankBeforeIcon.enabled = false;

    if (rankAfterIcon != null)
        rankAfterIcon.enabled = false;
}
private void ShowNewResultMessage(string message)
{
    ClearNewResultUI();

    if (coinRewardText != null)
    {
        coinRewardText.text = message;
    }
}

    #endregion

    #region Title

    /// <summary>Shows VICTORY, DEFEAT or DRAW for the viewer (FR-SURR-03).</summary>
    public void ShowResult(MatchOutcome outcome, SeatId viewer)
    {
        if (isRankedResultUIActive)
            return;

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
                UpdateResultBackground(true, false);
                PlayMatchResultAudio(true);
                break;
            case MatchResultBanner.Draw:
                SetTitle("HÒA", DrawColor);
                break;
            default:
                SetTitle("THẤT BẠI", DefeatColor);
                UpdateResultBackground(false, false);
                PlayMatchResultAudio(false);
                break;
        }
        RefreshResultBackgroundFromSettlement();
    }

    /// <summary>
    /// Kept for CombatAnimationDirector, which only knows win/lose. Uses the snapshot's
    /// outcome when available so a draw is not shown as a defeat.
    /// </summary>
    
public void ShowResult(bool isVictory)
{
    if (isRankedResultUIActive)
        return;

    if (finishedOutcome != MatchOutcome.Running)
    {
        ShowResult(finishedOutcome, viewerSeat);
        return;
    }

    if (resultPanel != null)
        resultPanel.SetActive(true);

    if (isVictory)
    {
        SetTitle("CHIẾN THẮNG", VictoryColor);
        UpdateResultBackground(true, false);
    }
    else
    {
        SetTitle("THẤT BẠI", DefeatColor);
        UpdateResultBackground(false, false);
    }
}

    private void UpdateResultBackground(bool isVictory, bool rankChanged)
{
    if (isRankedResultUIActive)
        return;

    if (matchResultBackground == null)
        return;

    matchResultBackground.gameObject.SetActive(true);

    Sprite selectedSprite;

    if (isVictory)
    {
        selectedSprite = rankChanged
            ? victoryRankChanged
            : victoryRankUnchanged;
    }
    else
    {
        selectedSprite = rankChanged
            ? defeatRankChanged
            : defeatRankUnchanged;
    }

    if (selectedSprite != null)
    {
        matchResultBackground.sprite = selectedSprite;
    }
}
private void RefreshResultBackgroundFromSettlement()
{
    MatchSettlementDto settlement = MatchSettlementRegistry.Current;

    if (settlement == null || settlement.matchId != finishedMatchId)
        return;

    MatchSettlementState state = (MatchSettlementState)settlement.state;

    if (state == MatchSettlementState.PrivateMatch ||
        state == MatchSettlementState.Saving ||
        state == MatchSettlementState.Failed)
        return;

    MatchResultBanner banner =
        MatchResultBannerPolicy.For(finishedOutcome, viewerSeat);

    bool rankChanged =
        settlement.ranked &&
        settlement.tierBefore != settlement.tierAfter;

    if (banner == MatchResultBanner.Victory)
        UpdateResultBackground(true, rankChanged);
    else if (banner == MatchResultBanner.Defeat)
        UpdateResultBackground(false, rankChanged);
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

    
private void UpdateRankedRewardUI(MatchSettlementDto settlement)
{
    if (settlement == null)
        return;

    if (settlement.result == "surrender_early")
    {
        if (rankedCoinRewardText != null)
            rankedCoinRewardText.text = "";

        if (rankedSilverRewardText != null)
            rankedSilverRewardText.text = "";

        if (rankedPointRewardText != null)
            rankedPointRewardText.text = "";

        return;
    }

    if (rankedCoinRewardText != null)
        rankedCoinRewardText.text = $"+{settlement.coins} xu";

    if (rankedSilverRewardText != null)
        rankedSilverRewardText.text = $"+{settlement.silver} bạc";

    int rankDelta = settlement.pointsAfter - settlement.pointsBefore;

    if (rankedPointRewardText != null)
        rankedPointRewardText.text = settlement.ranked
            ? $"{(rankDelta >= 0 ? "+" : "")}{rankDelta} điểm"
            : "";
}


private void UpdateRankedStage2Icons(MatchSettlementDto settlement)
{
    if (settlement == null || !settlement.ranked)
        return;

    RankCatalog catalog = RankCatalog.Load();

    if (catalog == null)
        return;

    int currentTier = Mathf.Clamp(
        settlement.tierAfter, 0, RankPolicy.TierCount - 1);

    int nextTier = Mathf.Min(
        currentTier + 1, RankPolicy.TierCount - 1);

    if (rankedCurrentRankIcon != null)
    {
        Sprite currentSprite =
            catalog.IconOf((RankTier)currentTier);

        rankedCurrentRankIcon.sprite = currentSprite;
        rankedCurrentRankIcon.enabled = currentSprite != null;
    }

    if (rankedNextRankIcon != null)
    {
        Sprite nextSprite =
            catalog.IconOf((RankTier)nextTier);

        rankedNextRankIcon.sprite = nextSprite;
        rankedNextRankIcon.enabled = nextSprite != null;
    }
}

private void UpdateRankedAnimatedIcons(RankTier tier)
{
    RankCatalog catalog = RankCatalog.Load();

    if (catalog == null)
        return;

    int currentTier = Mathf.Clamp(
        (int)tier, 0, RankPolicy.TierCount - 1
    );

    int nextTier = Mathf.Min(
        currentTier + 1, RankPolicy.TierCount - 1
    );

    if (rankedCurrentRankIcon != null)
    {
        Sprite sprite = catalog.IconOf((RankTier)currentTier);

        rankedCurrentRankIcon.sprite = sprite;
        rankedCurrentRankIcon.enabled = sprite != null;
    }

    if (rankedNextRankIcon != null)
    {
        Sprite sprite = catalog.IconOf((RankTier)nextTier);

        rankedNextRankIcon.sprite = sprite;
        rankedNextRankIcon.enabled = sprite != null;
    }
}


private void UpdateRankedProgress(MatchSettlementDto settlement)
{
    if (settlement == null || !settlement.ranked)
        return;

if (rankProgressFill == null)
    return;

rankedPointsBefore = Mathf.Max(0, settlement.pointsBefore);
rankedPointsAfter = Mathf.Max(0, settlement.pointsAfter);

int points = rankedPointsAfter;


rankedTierBefore = RankPolicy.TierFor(rankedPointsBefore);
rankedTierAfter = RankPolicy.TierFor(rankedPointsAfter);

rankedTierChanged = rankedTierBefore != rankedTierAfter;
rankedTierDisplayed = rankedTierBefore;

rankedPointsMaxBefore =
    rankedTierBefore == RankTier.Challenger
        ? RankPolicy.MinPoints(RankTier.Challenger)
        : RankPolicy.MinPoints(
            (RankTier)((int)rankedTierBefore + 1)
        );




    RankTier currentTier = RankPolicy.TierFor(points);

    
if (currentTier == RankTier.Challenger)
{
    rankedPointsMax = RankPolicy.MinPoints(RankTier.Challenger);

    
rankedProgressStart = Mathf.InverseLerp(
    RankPolicy.MinPoints(rankedTierBefore),
    rankedPointsMaxBefore,
    rankedPointsBefore
);


    rankedProgressTarget = 1f;
    return;
}


    int minPoints = RankPolicy.MinPoints(currentTier);

    RankTier nextTier = (RankTier)((int)currentTier + 1);

    int maxPoints = RankPolicy.MinPoints(nextTier);
    rankedPointsMax = maxPoints;
    

rankedProgressStart = Mathf.InverseLerp(
    RankPolicy.MinPoints(rankedTierBefore),
    rankedPointsMaxBefore,
    rankedPointsBefore
);


    float progress = Mathf.InverseLerp(
        minPoints,
        maxPoints,
        points
    );

    rankedProgressTarget = progress;
}


    private void ShowRankedStage1(bool isVictory)
{
    if (resultPanel != null)
        resultPanel.SetActive(false);

    if (matchResultBackground != null)
        matchResultBackground.gameObject.SetActive(false);

    if (rankedResultDimBackground != null)
        rankedResultDimBackground.SetActive(true);

    if (rankedResultStage2 != null)
        rankedResultStage2.SetActive(false);

    if (rankedResultStage1 != null)
    {
        UnityEngine.UI.Image image =
            rankedResultStage1.GetComponent<UnityEngine.UI.Image>();

        if (image != null)
        {
            Sprite selectedSprite = isVictory
                ? rankedVictorySprite
                : rankedDefeatSprite;

            if (selectedSprite != null)
                image.sprite = selectedSprite;
        }

        rankedResultStage1.SetActive(true);
    }
}

private float GetRankProgress(int points, RankTier tier)
{
    if (tier == RankTier.Challenger)
        return 1f;

    int minPoints = RankPolicy.MinPoints(tier);
    int maxPoints = RankPolicy.MinPoints(
        (RankTier)((int)tier + 1)
    );

    return Mathf.InverseLerp(minPoints, maxPoints, points);
}

private System.Collections.IEnumerator AnimateRankedProgress()
{
    if (rankProgressFill == null)
        yield break;

    float duration = 1.5f;
    float elapsed = 0f;

    
if (rankedReturnButton != null)
{
    rankedReturnButton.gameObject.SetActive(false);
}


    
rankProgressFill.fillAmount = GetRankProgress(
    rankedPointsBefore,
    rankedTierBefore
);
    
    rankedTierDisplayed = rankedTierBefore;

    UpdateRankedAnimatedIcons(rankedTierBefore);

    
    


if (rankProgressText != null)
{
    rankProgressText.text = rankedTierBefore == RankTier.Challenger
        ? $"{rankedPointsBefore}"
        : $"{rankedPointsBefore}/{rankedPointsMaxBefore}";
}




    while (elapsed < duration)
    {
        elapsed += Time.deltaTime;

        float t = Mathf.Clamp01(elapsed / duration);
        t = Mathf.SmoothStep(0f, 1f, t);
        

int displayedPoints = Mathf.RoundToInt(
    Mathf.Lerp(rankedPointsBefore, rankedPointsAfter, t)
);


RankTier newTier = RankPolicy.TierFor(displayedPoints);

if (newTier != rankedTierDisplayed)
{
    rankedTierDisplayed = newTier;
    UpdateRankedAnimatedIcons(rankedTierDisplayed);
}




rankProgressFill.fillAmount = GetRankProgress(
    displayedPoints,
    RankPolicy.TierFor(displayedPoints)
);

if (rankProgressText != null)
{


    
RankTier displayedTier = RankPolicy.TierFor(displayedPoints);

int displayedMax = displayedTier == RankTier.Challenger
    ? RankPolicy.MinPoints(RankTier.Challenger)
    : RankPolicy.MinPoints(
        (RankTier)((int)displayedTier + 1)
    );


rankProgressText.text = displayedTier == RankTier.Challenger
    ? $"{displayedPoints}"
    : $"{displayedPoints}/{displayedMax}";


}


        yield return null;
    }

    rankProgressFill.fillAmount = GetRankProgress(
    rankedPointsAfter,
    rankedTierAfter
);

rankedTierDisplayed = rankedTierAfter;

UpdateRankedAnimatedIcons(rankedTierDisplayed);

    

if (rankProgressText != null)
{
    rankProgressText.text = rankedTierAfter == RankTier.Challenger
        ? $"{rankedPointsAfter}"
        : $"{rankedPointsAfter}/{rankedPointsMax}";
}


if (rankedReturnButton != null)
{
    rankedReturnButton.gameObject.SetActive(true);
}


}


    

public void OnRankedContinueClicked()
{
    if (!isRankedResultUIActive ||
        rankedResultStage1 == null ||
        !rankedResultStage1.activeSelf)
    {
        return;
    }
    
if (rankedResultStage2 == null)
{
    Debug.LogWarning("RankedResultStage2 chưa được gán trong Inspector!");
    return;
}


    rankedResultStage1.SetActive(false);
    
if (rankedReturnButton != null)
{
    rankedReturnButton.gameObject.SetActive(false);
}


rankedResultStage2.SetActive(true);


    
if (rankProgressFill != null)
{
    StopCoroutine(nameof(AnimateRankedProgress));
    StartCoroutine(AnimateRankedProgress());
}
else
{
    Debug.LogWarning("RankProgressFill chưa được gán trong Inspector!");

    if (rankedReturnButton != null)
    {
        rankedReturnButton.gameObject.SetActive(true);
    }
}

}



    public async void OnReturnButtonClicked()
    {
        await MatchConnectionController.ReturnToMainMenuAsync();
    }
}
