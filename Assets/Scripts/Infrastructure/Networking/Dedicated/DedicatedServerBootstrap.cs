using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(UnityTransport))]
public sealed class DedicatedServerBootstrap : MonoBehaviour
{
    #region Configuration

    [Header("Server")]
    [SerializeField]
    private ushort listenPort = 7777;

    [SerializeField]
    private int requiredPlayerCount = 2;

    [SerializeField]
    private float emptyServerTimeoutSeconds = 120f;

    [Header("Admission")]
    [SerializeField]
    private float admissionTimeoutSeconds = 10f;

    [Tooltip("Must exceed the admission timeout; NGO drops pending connections after it.")]
    [SerializeField]
    private int connectionBufferTimeoutSeconds = 15;

    [Tooltip("Longest wait for an in-flight ReportResult before the empty server quits.")]
    [SerializeField]
    private float reportShutdownGraceSeconds = 120f;

    [Header("Editor Testing")]
    [SerializeField]
    private bool allowEditorDedicatedServer;

    [Tooltip("Editor only: use StubMatchResultReporter instead of Cloud Code.")]
    [SerializeField]
    private bool useStubReporterInEditor = true;

    #endregion

    #region Dependencies

    private NetworkManager networkManager;
    private UnityTransport transport;

    #endregion

    #region Runtime State

    // Admitted clients whose connection completed.
    private readonly HashSet<ulong> connectedClients = new();

    // Players whose AdmitPlayer call is still running.
    private readonly HashSet<string> verifyingPlayerIds = new(StringComparer.Ordinal);
    private CancellationTokenSource lifetimeCancellation;
    private bool gameSceneLoadStarted;
    private bool shutdownPending;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
    }

    private void Start()
    {
        if (!IsDedicatedRuntime())
        {
            enabled = false;
            return;
        }

        StartDedicatedServer();
    }

    private void OnDestroy()
    {
        lifetimeCancellation?.Cancel();
        lifetimeCancellation?.Dispose();

        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;

            if (networkManager.ConnectionApprovalCallback == ApproveConnection)
            {
                networkManager.ConnectionApprovalCallback = null;
            }
        }
    }

    #endregion

    #region Server Startup

    private bool IsDedicatedRuntime()
    {
#if UNITY_SERVER
        return true;
#else
        return allowEditorDedicatedServer
            || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
#endif
    }

    private void StartDedicatedServer()
    {
        Application.runInBackground = true;
        string envMatchId = Environment.GetEnvironmentVariable("MATCH_ID");
        string matchId = string.IsNullOrWhiteSpace(envMatchId) ? "local-match" : envMatchId.Trim();
        Debug.Log($"Starting dedicated server. Match ID: {matchId}");

        IMatchResultReporter reporter = MatchResultReporterFactory.Create(
            Application.isEditor && useStubReporterInEditor
        );
        MatchServerContext.StartDedicated(matchId, reporter);

        // Clients enable ConnectionApproval too: it is part of NGO's config hash.
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.ClientConnectionBufferTimeout = Math.Max(
            connectionBufferTimeoutSeconds,
            Mathf.CeilToInt(admissionTimeoutSeconds) + 1
        );
        networkManager.ConnectionApprovalCallback = ApproveConnection;
        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        transport.SetConnectionData("0.0.0.0", listenPort, "0.0.0.0");

        if (!networkManager.StartServer())
        {
            Debug.LogError("Failed to start dedicated server.");
            Application.Quit(1);
            return;
        }

        Debug.Log($"Dedicated server listening on 0.0.0.0:{listenPort}");
        lifetimeCancellation = new CancellationTokenSource();
        _ = QuitIfUnusedAsync(lifetimeCancellation.Token);
    }

    #endregion

    #region Connection Approval

    /// <summary>
    /// Accepts only the players allocated to this match (FR-MM-09): the payload is checked
    /// locally, then AdmitPlayer verifies the UGS token, match token and owned cards.
    /// </summary>
    private void ApproveConnection(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response
    )
    {
        ulong clientId = request.ClientNetworkId;

        if (!ConnectionPayload.TryParse(request.Payload, out ConnectionPayload payload))
        {
            Reject(clientId, response, AdmissionRejection.MissingPayload);
            return;
        }

        if (gameSceneLoadStarted)
        {
            // FR-MM-08: after the match started only a player whose seat is held may return.
            AdmissionRejection rejoinRejection = MatchAdmissionPolicy.PreCheckRejoin(
                payload.playerId,
                payload.accessToken,
                MatchServerContext.CanRejoin(payload.playerId),
                verifyingPlayerIds
            );

            if (rejoinRejection != AdmissionRejection.None)
            {
                Reject(clientId, response, rejoinRejection);
                return;
            }

            response.Pending = true;
            verifyingPlayerIds.Add(payload.playerId);
            _ = AdmitAsync(clientId, payload, response);
            return;
        }

        var knownPlayerIds = new List<string>(verifyingPlayerIds);
        knownPlayerIds.AddRange(MatchServerContext.AdmittedPlayerIds());

        AdmissionRejection rejection = MatchAdmissionPolicy.PreCheck(
            payload.playerId,
            payload.accessToken,
            payload.matchId,
            // The allocator creates its own MATCH_ID, different from the matchmaker ID the
            // client knows, so membership is checked by AdmitPlayer (players + MATCH_TOKEN).
            null,
            knownPlayerIds,
            requiredPlayerCount,
            gameSceneLoadStarted
        );

        if (rejection != AdmissionRejection.None)
        {
            Reject(clientId, response, rejection);
            return;
        }

        response.Pending = true;
        verifyingPlayerIds.Add(payload.playerId);
        _ = AdmitAsync(clientId, payload, response);
    }

    private async Task AdmitAsync(
        ulong clientId,
        ConnectionPayload payload,
        NetworkManager.ConnectionApprovalResponse response
    )
    {
        PlayerAdmission admission;

        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(admissionTimeoutSeconds)
            );
            admission = await MatchServerContext.Reporter.AdmitPlayerAsync(
                MatchServerContext.MatchId,
                payload.playerId,
                payload.accessToken,
                timeout.Token
            );
        }
        catch (OperationCanceledException)
        {
            admission = PlayerAdmission.Reject(payload.playerId, "Hết thời gian xác minh người chơi.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            admission = PlayerAdmission.Reject(payload.playerId, "Không xác minh được người chơi.");
        }
        finally
        {
            verifyingPlayerIds.Remove(payload.playerId);
        }

        if (this == null || networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        // The seat may have expired while AdmitPlayer was running.
        if (admission.Admitted && gameSceneLoadStarted && !MatchServerContext.CanRejoin(payload.playerId))
        {
            admission = PlayerAdmission.Reject(
                payload.playerId,
                MatchAdmissionPolicy.Describe(AdmissionRejection.CannotRejoin)
            );
        }

        if (admission.Admitted && !networkManager.PendingClients.ContainsKey(clientId))
        {
            // The client gave up while being verified.
            response.Approved = false;
            response.Pending = false;
            return;
        }

        if (admission.Admitted)
        {
            MatchServerContext.Admit(clientId, admission);
            Debug.Log($"Admitted client {clientId} as player {admission.PlayerId}.");
        }
        else
        {
            Debug.LogWarning(
                $"Rejected client {clientId} (player {payload.playerId}): {admission.Reason}"
            );
        }

        response.Approved = admission.Admitted;
        response.Reason = admission.Admitted ? string.Empty : admission.Reason;
        response.CreatePlayerObject = networkManager.NetworkConfig.PlayerPrefab != null;
        response.Pending = false;
    }

    private static void Reject(
        ulong clientId,
        NetworkManager.ConnectionApprovalResponse response,
        AdmissionRejection rejection
    )
    {
        Debug.LogWarning($"Rejected client {clientId}: {rejection}.");
        response.Approved = false;
        response.Reason = MatchAdmissionPolicy.Describe(rejection);
        response.Pending = false;
    }

    #endregion

    #region Client Lifecycle

    private void OnClientConnected(ulong clientId)
    {
        if (!MatchServerContext.TryGetAdmission(clientId, out _))
        {
            // Approval guarantees this; kept as a guard against configuration mistakes.
            networkManager.DisconnectClient(clientId, "Chưa được xác minh.");
            return;
        }

        if (gameSceneLoadStarted)
        {
            // Approval only lets a player with a held seat in; NGO synchronizes GameScene and
            // NetworkMatchBridge gives the seat back.
            connectedClients.Add(clientId);
            Debug.Log($"Client {clientId} rejoined. Players: {connectedClients.Count}/{requiredPlayerCount}");
            return;
        }

        connectedClients.Add(clientId);
        Debug.Log(
            $"Client connected: {clientId}. Players: {connectedClients.Count}/{requiredPlayerCount}"
        );

        // Only admitted players count, so the match starts with exactly the allocated pair.
        if (connectedClients.Count == requiredPlayerCount)
        {
            LoadGameScene();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        connectedClients.Remove(clientId);

        if (!gameSceneLoadStarted)
        {
            // Frees the seat; after the match started the admission is kept for the report.
            MatchServerContext.Forget(clientId);
        }

        Debug.Log(
            $"Client disconnected: {clientId}. Remaining players: {connectedClients.Count}"
        );

        if (gameSceneLoadStarted && connectedClients.Count == 0)
        {
            _ = QuitWhenSeatsReleasedAsync();
        }
    }

    #endregion

    #region Scene Flow

    private void LoadGameScene()
    {
        if (gameSceneLoadStarted)
        {
            return;
        }

        gameSceneLoadStarted = true;
        lifetimeCancellation?.Cancel();
        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(
            SceneNames.GameScene,
            LoadSceneMode.Single
        );

        if (status != SceneEventProgressStatus.Started)
        {
            gameSceneLoadStarted = false;
            Debug.LogError($"Could not start GameScene load: {status}");
        }
    }

    #endregion

    #region Server Lifetime

    /// <summary>
    /// Everyone left: keep running while a seat is still held (a player may come back,
    /// FR-MM-08), then wait for the report and quit. Cancelled when someone returns.
    /// </summary>
    private async Task QuitWhenSeatsReleasedAsync()
    {
        if (shutdownPending)
        {
            return;
        }

        shutdownPending = true;

        while (MatchServerContext.IsHoldingSeats())
        {
            Debug.Log("All clients left. Holding seats until the grace period ends.");
            await Task.Delay(1000);

            if (this == null)
            {
                return;
            }

            if (connectedClients.Count > 0)
            {
                shutdownPending = false;
                return;
            }
        }

        if (connectedClients.Count > 0)
        {
            shutdownPending = false;
            return;
        }

        await QuitAfterReportAsync();
    }

    private async Task QuitAfterReportAsync()
    {
        Task report = MatchServerContext.PendingReport ?? Task.CompletedTask;

        if (!report.IsCompleted)
        {
            Debug.Log("All clients left. Waiting for the match report before stopping.");
            int graceMilliseconds = Mathf.CeilToInt(reportShutdownGraceSeconds * 1000f);
            await Task.WhenAny(report, Task.Delay(graceMilliseconds));
        }

        Debug.Log("All clients left. Stopping server.");
        Application.Quit();
    }

    private async Task QuitIfUnusedAsync(CancellationToken cancellationToken)
    {
        try
        {
            int delayMilliseconds = Mathf.CeilToInt(emptyServerTimeoutSeconds * 1000f);
            await Task.Delay(delayMilliseconds, cancellationToken);

            if (connectedClients.Count == 0)
            {
                Debug.Log("Server remained unused. Quitting.");
                Application.Quit();
            }
        }
        catch (OperationCanceledException)
        {
            // A complete match was formed.
        }
    }

    #endregion
}
