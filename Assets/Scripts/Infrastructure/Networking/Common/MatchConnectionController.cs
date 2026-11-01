using System;
using System.Collections;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Client side of the reconnect grace period (FR-NET-06/07). Remembers how the local player
/// reached the current match, shows the opponent's connection state from the snapshot, and
/// when the local connection drops mid-match: retries the dedicated server for 30 s, offers
/// a "Vào lại" button with the old join code on listen-host, or reports that the host left.
/// Lives on a DontDestroyOnLoad object created by the first Remember call.
/// </summary>
[DisallowMultipleComponent]
public sealed class MatchConnectionController : MonoBehaviour
{
    #region Constants

    private const float ReconnectWindowSeconds = 30f;

    // Approval can wait up to 10 s for AdmitPlayer, plus a Cloud Code cold start.
    private const float AttemptTimeoutSeconds = 15f;
    private const float RetryDelaySeconds = 2f;
    private const float ResyncTimeoutSeconds = 20f;
    private const float ReconnectedNoticeSeconds = 2f;

    private const string BackToMenuText = "Về menu";
    private const string RejoinText = "Vào lại";

    #endregion

    #region Types

    private enum Route
    {
        None,
        Dedicated,
        ListenHost,
        ListenGuest,
    }

    #endregion

    #region Singleton

    private static MatchConnectionController instance;

    #endregion

    #region Runtime State

    private MatchConnectionOverlay overlay;
    private NetworkManager hookedManager;
    private Route route;
    private string dedicatedIp;
    private ushort dedicatedPort;
    private string dedicatedMatchId;
    private string listenJoinCode;

    private bool matchRunning;
    private bool leavingOnPurpose;
    private bool reconnecting;
    private bool attemptActive;
    private bool attemptStopped;
    private float reconnectDeadline;

    private ulong bannerRevision = ulong.MaxValue;
    private float opponentDeadline;
    private bool opponentWasAway;
    private float reconnectedNoticeUntil;

    #endregion

    #region Public API

    /// <summary>Called when the client connects to a dedicated match server.</summary>
    public static void RememberDedicated(string ip, ushort port, string matchId)
    {
        MatchConnectionController controller = Ensure();
        controller.route = Route.Dedicated;
        controller.dedicatedIp = ip;
        controller.dedicatedPort = port;
        controller.dedicatedMatchId = matchId;
        controller.ResetMatchTracking();
    }

    /// <summary>Called when this player hosts a listen-host room.</summary>
    public static void RememberListenHost()
    {
        MatchConnectionController controller = Ensure();
        controller.route = Route.ListenHost;
        controller.listenJoinCode = null;
        controller.ResetMatchTracking();
    }

    /// <summary>Called when this player joins a listen-host room; the code is reused to rejoin.</summary>
    public static void RememberListenGuest(string joinCode)
    {
        MatchConnectionController controller = Ensure();
        controller.route = Route.ListenGuest;
        controller.listenJoinCode = joinCode?.Trim();
        controller.ResetMatchTracking();
    }

    /// <summary>The player leaves on purpose: the next disconnect is not a lost connection.</summary>
    public static void Forget()
    {
        if (instance == null)
        {
            return;
        }

        instance.StopAllCoroutines();
        instance.route = Route.None;
        instance.leavingOnPurpose = true;
        instance.reconnecting = false;
        instance.attemptActive = false;
        instance.ResetMatchTracking();
        instance.overlay.HideAll();
    }

    #endregion

    #region Unity Lifecycle

    private static MatchConnectionController Ensure()
    {
        if (instance != null)
        {
            instance.HookNetworkManager();
            return instance;
        }

        var root = new GameObject(nameof(MatchConnectionController));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<MatchConnectionController>();
        instance.overlay = root.AddComponent<MatchConnectionOverlay>();
        instance.HookNetworkManager();
        return instance;
    }

    private void OnEnable()
    {
        UgsSessionService.Instance.HostSessionEnded += OnHostSessionEnded;
    }

    private void OnDisable()
    {
        UgsSessionService.Instance.HostSessionEnded -= OnHostSessionEnded;

        if (hookedManager != null)
        {
            hookedManager.OnClientStopped -= OnClientStopped;
            hookedManager = null;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        if (route == Route.None || reconnecting)
        {
            return;
        }

        MatchSnapshotDto snapshot = MatchProjectionRegistry.Current.Current;
        bool inGame = SceneManager.GetActiveScene().name == SceneNames.GameScene;

        // The projection is cleared when the bridge despawns, so keep the last known state.
        if (inGame && snapshot != null && !string.IsNullOrEmpty(snapshot.matchId))
        {
            matchRunning = snapshot.outcome == (int)MatchOutcome.Running;
        }

        if (inGame && snapshot != null && !overlay.IsDialogVisible)
        {
            RenderOpponentConnection(snapshot);
        }
    }

    #endregion

    #region Opponent Connection Banner

    /// <summary>FR-NET-06: "Đối thủ mất kết nối – chờ X s" while their seat is held.</summary>
    private void RenderOpponentConnection(MatchSnapshotDto snapshot)
    {
        bool running = snapshot.outcome == (int)MatchOutcome.Running;
        bool away = running && snapshot.opponentConnection == (int)SeatConnectionState.Disconnected;
        bool gone = running && snapshot.opponentConnection == (int)SeatConnectionState.Abandoned;

        if (away)
        {
            if (snapshot.revision != bannerRevision)
            {
                bannerRevision = snapshot.revision;
                opponentDeadline = Time.realtimeSinceStartup + snapshot.opponentReconnectRemainingMs / 1000f;
            }

            int seconds = Mathf.CeilToInt(Mathf.Max(0f, opponentDeadline - Time.realtimeSinceStartup));
            overlay.ShowBanner($"Đối thủ mất kết nối – chờ {seconds} s");
            opponentWasAway = true;
            return;
        }

        if (gone)
        {
            overlay.ShowBanner("Đối thủ không quay lại. Đang chờ máy chủ kết thúc trận...");
            opponentWasAway = true;
            return;
        }

        if (opponentWasAway)
        {
            opponentWasAway = false;
            reconnectedNoticeUntil = running ? Time.realtimeSinceStartup + ReconnectedNoticeSeconds : 0f;

            if (running)
            {
                overlay.ShowBanner("Đối thủ đã kết nối lại. Trận đấu tiếp tục.");
                return;
            }
        }

        if (Time.realtimeSinceStartup >= reconnectedNoticeUntil)
        {
            overlay.HideBanner();
        }
    }

    #endregion

    #region Local Disconnect

    private void HookNetworkManager()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null || networkManager == hookedManager)
        {
            leavingOnPurpose = false;
            return;
        }

        if (hookedManager != null)
        {
            hookedManager.OnClientStopped -= OnClientStopped;
        }

        hookedManager = networkManager;
        hookedManager.OnClientStopped += OnClientStopped;
        leavingOnPurpose = false;
    }

    private void OnClientStopped(bool wasHost)
    {
        if (attemptActive)
        {
            attemptStopped = true;
            return;
        }

        if (leavingOnPurpose)
        {
            leavingOnPurpose = false;
            return;
        }

        if (route == Route.None || reconnecting || !matchRunning)
        {
            return;
        }

        matchRunning = false;
        overlay.HideBanner();
        string reason = hookedManager != null ? hookedManager.DisconnectReason : null;
        Debug.LogWarning($"[Reconnect] Lost connection mid-match ({route}). Reason: {reason}");

        switch (route)
        {
            case Route.Dedicated:
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    // The server closed the connection on purpose, e.g. the seat expired.
                    ShowFinal($"Mất kết nối: {reason}");
                    return;
                }

                StartCoroutine(ReconnectDedicated());
                return;

            case Route.ListenGuest:
                reconnectDeadline = Time.realtimeSinceStartup + ReconnectWindowSeconds;
                ShowListenRejoinDialog(null);
                return;

            default:
                ShowFinal("Mất kết nối mạng. Phòng đã đóng, trận đấu kết thúc.");
                return;
        }
    }

    private void OnHostSessionEnded()
    {
        if (route != Route.ListenGuest)
        {
            return;
        }

        bool inGame = SceneManager.GetActiveScene().name == SceneNames.GameScene;

        if (inGame || overlay.IsDialogVisible)
        {
            StopAllCoroutines();
            reconnecting = false;
            ShowFinal("Chủ phòng đã rời, trận đấu kết thúc.");
        }
    }

    #endregion

    #region Dedicated Reconnect

    /// <summary>
    /// Retries the remembered server with a fresh payload until the 30 s window closes.
    /// GameScene is left first: NGO then loads a clean copy while synchronizing the
    /// returning client instead of reusing in-scene objects that were despawned locally.
    /// </summary>
    private IEnumerator ReconnectDedicated()
    {
        reconnecting = true;
        reconnectDeadline = Time.realtimeSinceStartup + ReconnectWindowSeconds;
        string lastReason = null;
        overlay.ShowDialog(ReconnectMessage(null), BackToMenuText, GoToMainMenu);

        NetworkManager networkManager = NetworkManager.Singleton;

        while (networkManager != null && networkManager.ShutdownInProgress)
        {
            yield return null;
        }

        SceneManager.LoadScene(SceneNames.FindMatch);
        yield return null;

        while (networkManager != null && Time.realtimeSinceStartup < reconnectDeadline)
        {
            attemptActive = true;
            attemptStopped = false;
            bool started = StartDedicatedClient(networkManager);
            float attemptEnd = Mathf.Min(reconnectDeadline, Time.realtimeSinceStartup + AttemptTimeoutSeconds);

            while (
                started
                && !attemptStopped
                && !networkManager.IsConnectedClient
                && Time.realtimeSinceStartup < attemptEnd
            )
            {
                overlay.SetDialogMessage(ReconnectMessage(lastReason));
                yield return null;
            }

            if (started && networkManager.IsConnectedClient)
            {
                attemptActive = false;
                reconnecting = false;
                Debug.Log("[Reconnect] Connected again; waiting for the match snapshot.");
                overlay.ShowDialog("Đã kết nối lại. Đang đồng bộ trận đấu...");
                StartCoroutine(HideDialogWhenResynced());
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(networkManager.DisconnectReason))
            {
                lastReason = networkManager.DisconnectReason;
            }

            if (networkManager.IsListening || networkManager.ShutdownInProgress)
            {
                networkManager.Shutdown();

                while (networkManager.ShutdownInProgress)
                {
                    yield return null;
                }
            }

            attemptActive = false;
            float retryAt = Time.realtimeSinceStartup + RetryDelaySeconds;

            while (Time.realtimeSinceStartup < retryAt && Time.realtimeSinceStartup < reconnectDeadline)
            {
                overlay.SetDialogMessage(ReconnectMessage(lastReason));
                yield return null;
            }
        }

        reconnecting = false;
        ShowFinal(
            "Không thể vào lại trận trong 30 giây. Bạn bị xử thua do mất kết nối."
                + (string.IsNullOrWhiteSpace(lastReason) ? string.Empty : $"\n({lastReason})")
        );
    }

    private bool StartDedicatedClient(NetworkManager networkManager)
    {
        try
        {
            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            transport.SetConnectionData(dedicatedIp, dedicatedPort);
            DedicatedMatchmakerClient.ConfigureConnectionApproval(networkManager, dedicatedMatchId);
            return networkManager.StartClient();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Reconnect] Could not start the client: {exception.Message}");
            return false;
        }
    }

    private string ReconnectMessage(string lastReason)
    {
        int seconds = Mathf.CeilToInt(Mathf.Max(0f, reconnectDeadline - Time.realtimeSinceStartup));
        string message = $"Mất kết nối – đang thử vào lại... ({seconds} s)";
        return string.IsNullOrWhiteSpace(lastReason) ? message : $"{message}\n{lastReason}";
    }

    #endregion

    #region Listen-Host Rejoin

    private void ShowListenRejoinDialog(string error)
    {
        string message = "Mất kết nối với chủ phòng. Chỗ của bạn được giữ trong 30 giây.";

        if (!string.IsNullOrWhiteSpace(error))
        {
            message += $"\n{error}";
        }

        overlay.ShowDialog(message, RejoinText, RejoinListenSession, BackToMenuText, GoToMainMenu);
    }

    /// <summary>Joins the same private room again with the old join code (FR-NET-07).</summary>
    private async void RejoinListenSession()
    {
        if (reconnecting || string.IsNullOrWhiteSpace(listenJoinCode))
        {
            return;
        }

        reconnecting = true;
        overlay.ShowDialog("Đang vào lại phòng...");

        try
        {
            NetworkManager networkManager = NetworkManager.Singleton;

            while (networkManager != null && networkManager.ShutdownInProgress)
            {
                await System.Threading.Tasks.Task.Yield();
            }

            // Leave GameScene so the host's scene synchronization loads a clean copy.
            SceneManager.LoadScene(SceneNames.HostClientMenu);
            await UgsSessionService.Instance.JoinByCodeAsync(listenJoinCode);

            if (this == null)
            {
                return;
            }

            reconnecting = false;
            overlay.ShowDialog("Đã vào lại phòng. Đang đồng bộ trận đấu...");
            StartCoroutine(HideDialogWhenResynced());
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Reconnect] Rejoin failed: {exception.Message}");

            if (this == null)
            {
                return;
            }

            reconnecting = false;

            if (Time.realtimeSinceStartup < reconnectDeadline)
            {
                ShowListenRejoinDialog("Không vào lại được. Có thể chủ phòng đã rời trận.");
                return;
            }

            ShowFinal("Không thể vào lại phòng. Chủ phòng đã rời hoặc chỗ của bạn đã hết hạn.");
        }
    }

    #endregion

    #region Shared Flow

    private IEnumerator HideDialogWhenResynced()
    {
        float giveUpAt = Time.realtimeSinceStartup + ResyncTimeoutSeconds;

        while (Time.realtimeSinceStartup < giveUpAt)
        {
            MatchSnapshotDto snapshot = MatchProjectionRegistry.Current.Current;

            if (SceneManager.GetActiveScene().name == SceneNames.GameScene && snapshot != null)
            {
                overlay.HideDialog();
                yield break;
            }

            yield return null;
        }

        ShowFinal("Đã kết nối lại nhưng không nhận được trạng thái trận đấu.");
    }

    private void ShowFinal(string message)
    {
        overlay.HideBanner();
        overlay.ShowDialog(message, BackToMenuText, GoToMainMenu);
    }

    private void GoToMainMenu()
    {
        Route leftRoute = route;
        Forget();

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }
        else
        {
            leavingOnPurpose = false;
        }

        if (leftRoute == Route.ListenGuest || leftRoute == Route.ListenHost)
        {
            _ = LeaveSessionQuietlyAsync();
        }

        SceneManager.LoadScene(SceneNames.MainMenu);
    }

    private static async System.Threading.Tasks.Task LeaveSessionQuietlyAsync()
    {
        try
        {
            await UgsSessionService.Instance.LeaveAsync();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(exception.Message);
        }
    }

    private void ResetMatchTracking()
    {
        matchRunning = false;
        bannerRevision = ulong.MaxValue;
        opponentWasAway = false;
        reconnectedNoticeUntil = 0f;
    }

    #endregion
}
