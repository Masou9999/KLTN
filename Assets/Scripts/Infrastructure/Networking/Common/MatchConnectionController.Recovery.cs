using System;
using System.Threading.Tasks;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Infrastructure.CloudCode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public sealed partial class MatchConnectionController
{
    [Serializable]
    private sealed class SavedMatch
    {
        public string owner;
        public string matchId;
        public Route route;
        public string ip;
        public int port;
        public string code;
        public string sessionId;
        public bool awaitingResult;
    }

    private SavedMatch saved;
    private int recoveryView;
    private string expectedResumeId;
    private const string RecoveryKey = "MatchRecovery.v1.";

    private static string SignedInPlayer => UnityServices.State == ServicesInitializationState.Initialized
        && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

    private void TrackRecovery(MatchSnapshotDto snapshot)
    {
        string owner = SignedInPlayer;
        if (route == Route.None || string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(snapshot.matchId))
            return;
        if (expectedResumeId != null && snapshot.matchId != expectedResumeId)
        {
            if (!IsLeaving) GoToMainMenu();
            return;
        }
        expectedResumeId = null;
        if (snapshot.outcome != (int)MatchOutcome.Running)
        {
            if (saved?.matchId == snapshot.matchId && saved.owner == owner && !saved.awaitingResult)
            {
                saved.awaitingResult = true;
                SaveRecord();
                OnSettlementConfirmed(MatchSettlementRegistry.Current);
            }
            return;
        }
        if (saved?.matchId == snapshot.matchId && saved.owner == owner) return;
        saved = new SavedMatch {
            owner = owner, matchId = snapshot.matchId, route = route, ip = dedicatedIp,
            port = dedicatedPort, code = listenJoinCode, sessionId = UgsSessionService.Instance.SessionId
        };
        SaveRecord();
    }

    private void OnSettlementConfirmed(MatchSettlementDto receipt)
    {
        if (receipt == null || saved == null || receipt.matchId != saved.matchId
            || saved.owner != SignedInPlayer || string.IsNullOrEmpty(receipt.result)) return;
        var state = (MatchSettlementState)receipt.state;
        if (state == MatchSettlementState.Saved || state == MatchSettlementState.PrivateMatch) ClearSaved();
    }

    private void SaveRecord()
    {
        PlayerPrefs.SetString(RecoveryKey + saved.owner, JsonUtility.ToJson(saved));
        PlayerPrefs.Save();
    }

    private void ClearSaved()
    {
        PlayerPrefs.DeleteKey(RecoveryKey + saved.owner);
        PlayerPrefs.Save();
        saved = null;
        recoveryView++;
    }

    /// <summary>Restore only the authenticated player's previous match on this device.</summary>
    public static void RestoreAtMenu()
    {
        string player = SignedInPlayer;
        if (string.IsNullOrEmpty(player)) return;
        string json = PlayerPrefs.GetString(RecoveryKey + player);
        if (string.IsNullOrEmpty(json)) return;
        var record = JsonUtility.FromJson<SavedMatch>(json);
        if (record == null || record.owner != player || string.IsNullOrEmpty(record.matchId)) return;
        var controller = Ensure();
        controller.saved = record;
        controller.route = record.route;
        controller.dedicatedIp = record.ip;
        controller.dedicatedPort = (ushort)record.port;
        controller.dedicatedMatchId = record.matchId;
        controller.listenJoinCode = record.code;
        controller.OpenRecovery();
    }

    public static void ShowRegistrationFailure()
    {
        Ensure().ShowFinal("Chưa thể xác nhận đủ hai người chơi. Trận chưa bắt đầu.");
    }

    private void OpenRecovery()
    {
        overlay.HideBanner();
        overlay.ShowDialog("Đang kiểm tra trạng thái trận…");
        _ = WatchRecoveryAsync(++recoveryView, false);
    }

    private bool ViewingRecovery(int view)
    {
        return this != null && recoveryView == view && saved != null && saved.owner == SignedInPlayer;
    }

    private void SurrenderFromMenu()
    {
        overlay.ShowDialog("Đang gửi yêu cầu đầu hàng…");
        _ = WatchRecoveryAsync(++recoveryView, true);
    }

    private async Task WatchRecoveryAsync(int view, bool surrender)
    {
        while (ViewingRecovery(view))
        {
            try
            {
                string status = await ListenMatchClient.RecoverAsync(saved.matchId, surrender);
                if (!ViewingRecovery(view)) return;
                surrender = false;
                if (status == "won" || status == "lost" || status == "finished")
                {
                    ClearSaved();
                    ShowFinal(status == "won" ? "Trận đã kết thúc. Bạn thắng."
                        : status == "lost" ? "Trận đã kết thúc. Bạn thua." : "Trận đã kết thúc.");
                    return;
                }
                if (status == "pending") overlay.ShowDialog("Đã gửi yêu cầu đầu hàng. Đang chờ kết quả…");
                else if (status == "preparing") overlay.ShowDialog("Đang xác nhận trận đấu…");
                else if (status != "active") throw new InvalidOperationException("Unknown match status.");
                else if (saved.awaitingResult)
                    overlay.ShowDialog("Trận đã kết thúc. Đang chờ xác nhận kết quả…");
                else if (route == Route.ListenHost)
                    overlay.ShowDialog("Phòng đã đóng. Đang chờ xác nhận kết quả…");
                else
                    overlay.ShowDialog("Bạn có trận đang diễn ra. Chọn Thoát trận sẽ được tính là đầu hàng.",
                        RejoinText, ResumeSavedMatch, "Thoát trận", SurrenderFromMenu);
            }
            catch (Exception exception)
            {
                if (!ViewingRecovery(view)) return;
                Debug.LogWarning($"[Reconnect] Cannot confirm match status: {exception.Message}");
                overlay.ShowDialog("Chưa xác nhận được kết quả. Kiểm tra kết nối rồi thử lại.",
                    "Kiểm tra lại", OpenRecovery, BackToMenuText, GoToMainMenu);
                return;
            }
            await FrameDelay.Delay(3000);
        }
    }

    private bool allowQuit;
    private bool sendingQuit;

    private bool OnQuitRequested()
    {
        if (allowQuit || route != Route.ListenHost || saved == null || !matchRunning) return true;
        if (!sendingQuit) _ = ReportHostQuitAsync(saved.matchId);
        return false;
    }

    private async Task ReportHostQuitAsync(string matchId)
    {
        sendingQuit = true;
        try
        {
            // A forced shutdown or unavailable backend falls back to the host lease.
            var report = ListenMatchClient.RecoverAsync(matchId, true);
            if (await Task.WhenAny(report, FrameDelay.Delay(2000)) == report) await report;
            else _ = report.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex) { Debug.LogWarning($"[Reconnect] Closing host: {ex.Message}"); }
        allowQuit = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ResumeSavedMatch()
    {
        if (saved == null || saved.owner != SignedInPlayer || reconnecting) return;
        recoveryView++;
        expectedResumeId = saved.matchId;
        if (route == Route.Dedicated) StartCoroutine(ReconnectDedicated());
        else if (route == Route.ListenGuest) RejoinListenSession();
    }
}
