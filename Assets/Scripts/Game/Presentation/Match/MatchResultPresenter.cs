using System.Collections;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using KLTN.Infrastructure.Economy;
using KLTN.Infrastructure.Ranking;
using UnityEngine;

/// <summary>
/// Result screen coordinator. Rewards and season points are written only by the backend;
/// this presenter waits for the settlement behind a loading overlay, then shows the
/// banner (Stage 1) and, for a saved ranked result, the rank progress (Stage 2)
/// (FR-SHOP-09, FR-RANK-10/11, FR-SURR-03).
/// </summary>
/// <remarks>
/// Flow: final snapshot → loading → settlement (or the 15 s client limit) → Stage 1 →
/// Continue → Stage 2 → Exit. Skip on the loading overlay leaves immediately; the
/// server, or the listen host's detached report, still records the result.
/// </remarks>
public class MatchResultPresenter : MonoBehaviour
{
    #region Constants

    private const float SettlementWaitSeconds = 15f;

    #endregion

    #region Serialized Fields

    [Header("Views")]
    [SerializeField] private GameObject dimBackground;
    [SerializeField] private MatchResultLoadingView loadingView;
    [SerializeField] private MatchResultBannerView bannerView;
    [SerializeField] private RankProgressView progressView;

    [Header("Match Result Audio")]
    [SerializeField] private AudioSource resultAudioSource;
    [SerializeField] private AudioClip victoryClip;
    [SerializeField] private AudioClip defeatClip;

    #endregion

    #region Runtime State

    private enum Stage
    {
        Hidden,
        Loading,
        Banner,
        Progress,
        Leaving,
    }

    private MatchClientProjection projection;
    private string finishedMatchId;
    private MatchOutcome finishedOutcome = MatchOutcome.Running;
    private SeatId viewerSeat;
    private string refreshedForMatchId;
    private Stage stage = Stage.Hidden;
    private MatchSettlementDto shownSettlement;
    private Coroutine waitRoutine;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        HideAll();
        if (loadingView != null) loadingView.SkipRequested += ReturnToMainMenu;
        if (bannerView != null) bannerView.ContinueRequested += OnContinue;
        if (progressView != null) progressView.ExitRequested += ReturnToMainMenu;
    }

    private void OnDestroy()
    {
        if (loadingView != null) loadingView.SkipRequested -= ReturnToMainMenu;
        if (bannerView != null) bannerView.ContinueRequested -= OnContinue;
        if (progressView != null) progressView.ExitRequested -= ReturnToMainMenu;
    }

    private void OnEnable()
    {
        projection = MatchProjectionRegistry.Current;
        if (projection != null)
        {
            projection.SnapshotChanged += HandleSnapshotChanged;
            if (projection.Current != null) HandleSnapshotChanged(projection.Current);
        }

        MatchSettlementRegistry.SettlementChanged += HandleSettlementChanged;
    }

    private void OnDisable()
    {
        if (projection != null) projection.SnapshotChanged -= HandleSnapshotChanged;
        MatchSettlementRegistry.SettlementChanged -= HandleSettlementChanged;
    }

    #endregion

    #region Public API

    /// <summary>Kept for persistent button listeners authored before the new screen.</summary>
    public void OnReturnButtonClicked()
    {
        ReturnToMainMenu();
    }

    #endregion

    #region Snapshot And Settlement

    private void HandleSnapshotChanged(MatchSnapshotDto snapshot)
    {
        if (snapshot == null) return;
        MatchOutcome outcome = (MatchOutcome)snapshot.outcome;
        if (outcome == MatchOutcome.Running) return;

        bool firstTime = finishedMatchId != snapshot.matchId
            || finishedOutcome == MatchOutcome.Running;
        finishedMatchId = snapshot.matchId;
        finishedOutcome = outcome;
        viewerSeat = (SeatId)snapshot.viewerSeat;
        if (!firstTime) return;

        // The final snapshot is projected after combat and chained abilities.
        shownSettlement = null;
        stage = Stage.Loading;
        HideAll();
        if (dimBackground != null) dimBackground.SetActive(true);
        if (loadingView != null) loadingView.Show();
        if (waitRoutine != null) StopCoroutine(waitRoutine);
        waitRoutine = StartCoroutine(WaitForSettlement());

        MatchSettlementDto settlement = MatchSettlementRegistry.Current;
        if (settlement != null && settlement.matchId == finishedMatchId)
            HandleSettlementChanged(settlement);
    }

    private void HandleSettlementChanged(MatchSettlementDto settlement)
    {
        // The settlement can arrive before the final snapshot; it is replayed from there.
        if (settlement == null || finishedOutcome == MatchOutcome.Running
            || settlement.matchId != finishedMatchId)
            return;
        if ((MatchSettlementState)settlement.state == MatchSettlementState.Saving) return;

        // A backend-confirmed forfeit may race the last local command or host lease.
        switch (settlement.result)
        {
            case "draw":
                finishedOutcome = MatchOutcome.Draw;
                break;
            case "win":
                finishedOutcome = viewerSeat == SeatId.Host ? MatchOutcome.HostWon : MatchOutcome.GuestWon;
                break;
            case "loss":
            case "surrender":
            case "surrender_early":
                finishedOutcome = viewerSeat == SeatId.Host ? MatchOutcome.GuestWon : MatchOutcome.HostWon;
                break;
        }
        shownSettlement = settlement;
        if ((MatchSettlementState)settlement.state == MatchSettlementState.Saved)
            RefreshProfileAndStanding(settlement.matchId);

        if (stage == Stage.Loading)
        {
            ShowBanner(MatchResultRewards.From(settlement));
        }
        else if (stage == Stage.Banner && bannerView != null)
        {
            bannerView.UpdateRewards(MatchResultRewards.From(settlement));
        }
    }

    private IEnumerator WaitForSettlement()
    {
        yield return ResultTween.Wait(SettlementWaitSeconds);
        waitRoutine = null;
        // Past the client limit only the skip action remains. A late settlement still
        // advances to the banner from HandleSettlementChanged.
        if (stage == Stage.Loading && loadingView != null) loadingView.ShowDelayed();
    }

    #endregion

    #region Stages

    private void ShowBanner(MatchResultRewards rewards)
    {
        if (waitRoutine != null) StopCoroutine(waitRoutine);
        waitRoutine = null;
        stage = Stage.Banner;
        if (loadingView != null) loadingView.Hide();

        MatchResultBanner banner = MatchResultBannerPolicy.For(finishedOutcome, viewerSeat);
        if (bannerView != null) bannerView.Show(banner, rewards);
        PlayResultAudio(banner);
    }

    private void OnContinue()
    {
        if (stage != Stage.Banner) return;
        if (!HasRankedProgress(shownSettlement) || progressView == null)
        {
            ReturnToMainMenu();
            return;
        }

        stage = Stage.Progress;
        StartCoroutine(ShowProgress(shownSettlement));
    }

    private IEnumerator ShowProgress(MatchSettlementDto settlement)
    {
        if (bannerView != null) yield return bannerView.Hide(0.3f);
        progressView.Play(settlement.pointsBefore, settlement.pointsAfter);
    }

    private static bool HasRankedProgress(MatchSettlementDto settlement)
    {
        return settlement != null
            && (MatchSettlementState)settlement.state == MatchSettlementState.Saved
            && settlement.ranked;
    }

    private void HideAll()
    {
        if (dimBackground != null) dimBackground.SetActive(false);
        if (loadingView != null) loadingView.Hide();
        if (bannerView != null) bannerView.gameObject.SetActive(false);
        if (progressView != null) progressView.Hide();
    }

    private async void ReturnToMainMenu()
    {
        if (stage == Stage.Leaving) return;
        stage = Stage.Leaving;
        await MatchConnectionController.ReturnToMainMenuAsync();
    }

    #endregion

    #region Helpers

    private void PlayResultAudio(MatchResultBanner banner)
    {
        if (resultAudioSource == null) return;
        AudioClip clip = banner == MatchResultBanner.Victory ? victoryClip
            : banner == MatchResultBanner.Defeat ? defeatClip
            : null;
        if (clip == null) return;
        resultAudioSource.Stop();
        resultAudioSource.clip = clip;
        resultAudioSource.Play();
    }

    /// <summary>Reads the wallet and season row the backend just updated (read-only).</summary>
    private async void RefreshProfileAndStanding(string matchId)
    {
        if (string.IsNullOrEmpty(matchId) || matchId == refreshedForMatchId) return;
        refreshedForMatchId = matchId;

        try
        {
            PlayerProfileService profiles = PlayerProfileService.Instance;
            if (profiles != null) await profiles.EnsureLoadedAsync(forceReload: true);
            LeaderboardService leaderboard = LeaderboardService.Instance;
            if (leaderboard != null) await leaderboard.GetMyCurrentTierAsync();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[MatchResult] Could not refresh wallet/standing: {exception.Message}");
        }
    }

    #endregion
}
