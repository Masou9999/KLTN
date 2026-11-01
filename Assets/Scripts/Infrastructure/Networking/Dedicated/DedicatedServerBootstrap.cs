using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Dedicated match server process. Hosts up to MATCH_CAPACITY matches (FR-MM-10, NFR-22):
/// the first one comes from MATCH_ID/MATCH_TOKEN, later ones from Cloud Code
/// (GetAssignedMatches, polled together with the capacity heartbeat). Every match is a
/// <see cref="MatchSession"/> in <see cref="MatchServerContext.Sessions"/>; GameScene is loaded
/// once and shared, each client only receives its own match's snapshots.
/// MATCH_CAPACITY=1 (the default) keeps the original one-match-per-process behaviour.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(UnityTransport))]
public sealed class DedicatedServerBootstrap : MonoBehaviour
{
    #region Environment Variables

    public const string MatchIdVariable = "MATCH_ID";
    public const string MatchmakerMatchIdVariable = "MATCHMAKER_MATCH_ID";
    public const string MatchPlayersVariable = "MATCH_PLAYERS";
    public const string CapacityVariable = "MATCH_CAPACITY";
    public const string ServerIdVariable = "SERVER_ID";
    public const string ServerTokenVariable = "SERVER_TOKEN";

    private const int MaxCapacity = 16;
    private const string LocalMatchIdPrefix = "local-match";

    #endregion

    #region Configuration

    [Header("Server")]
    [SerializeField]
    private ushort listenPort = 7777;

    [Tooltip("Quit after this long without any match (NFR-21).")]
    [SerializeField]
    private float emptyServerTimeoutSeconds = 120f;

    [Tooltip("Used when MATCH_CAPACITY is not set (Editor and local runs).")]
    [SerializeField]
    private int defaultMatchCapacity = 1;

    [Header("Multi-match")]
    [Tooltip("Heartbeat and new-match poll interval (GetAssignedMatches).")]
    [SerializeField]
    private float assignmentPollSeconds = 2f;

    [Tooltip("A match whose two players have not both connected by then is cancelled.")]
    [SerializeField]
    private float unsealedMatchTimeoutSeconds = 120f;

    [Tooltip("Players still on the result screen this long after the end are disconnected.")]
    [SerializeField]
    private float finishedMatchLingerSeconds = 120f;

    [Header("Admission")]
    [SerializeField]
    private float admissionTimeoutSeconds = 10f;

    [Tooltip("Must exceed the admission timeout; NGO drops pending connections after it.")]
    [SerializeField]
    private int connectionBufferTimeoutSeconds = 15;

    [Tooltip("Longest wait for in-flight ReportResult calls before the empty server quits.")]
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

    // When each match was first seen finished, to free slots held by idle result screens.
    private readonly Dictionary<string, float> finishedSince = new(StringComparer.Ordinal);

    private CancellationTokenSource lifetimeCancellation;
    private MatchSessionRegistry sessions;
    private IMatchAssignmentSource assignments;
    private string serverId;
    private string serverToken;
    private bool gameSceneLoadStarted;
    private bool shutdownPending;
    private int localMatchCount;
    private float idleSince = -1f;
    private TaskCompletionSource<bool> pollNow;
    private Task<bool> pollInFlight;

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

        // Load test (plan 05 D): the same build runs as a scripted player instead of a server.
        string loadTestTarget = LoadTestClient.ConnectTarget();

        if (loadTestTarget != null)
        {
            Application.runInBackground = true;
            enabled = false;
            gameObject.AddComponent<LoadTestClient>().Begin(networkManager, loadTestTarget);
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

    /// <summary>Whether matches arrive from Cloud Code; otherwise players fill local matches in arrival order.</summary>
    private bool IsAssigned => assignments != null;

    private void StartDedicatedServer()
    {
        Application.runInBackground = true;
        int capacity = ReadCapacity();
        IMatchResultReporter reporter = MatchResultReporterFactory.Create(
            Application.isEditor && useStubReporterInEditor
        );
        MatchServerContext.StartDedicated(reporter, capacity);
        sessions = MatchServerContext.Sessions;

        serverId = ReadVariable(ServerIdVariable);
        serverToken = ReadVariable(ServerTokenVariable);

        // Only a server with spare slots and its own identity polls for more matches.
        if (capacity > 1 && serverId != null && serverToken != null && reporter is IMatchAssignmentSource source)
        {
            assignments = source;
        }

        string envMatchId = ReadVariable(MatchIdVariable);

        if (envMatchId != null)
        {
            AddMatch(
                envMatchId,
                ReadVariable(MatchmakerMatchIdVariable),
                SplitPlayers(ReadVariable(MatchPlayersVariable)),
                ReadVariable(MatchResultReporterFactory.MatchTokenVariable)
            );
        }

        Debug.Log(
            $"Starting dedicated server. Capacity: {capacity}. First match: {envMatchId ?? "(local)"}. "
                + $"Assignments: {(IsAssigned ? "Cloud Code, server " + serverId : "local")}."
        );

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
        _ = RunLifetimeAsync(lifetimeCancellation.Token);

        if (IsAssigned)
        {
            _ = RunAssignmentPollAsync(lifetimeCancellation.Token);
        }
    }

    private int ReadCapacity()
    {
        string text = ReadVariable(CapacityVariable);
        int capacity = defaultMatchCapacity;

        if (text != null && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out capacity))
        {
            Debug.LogWarning($"{CapacityVariable}='{text}' is not a number; using 1.");
            capacity = 1;
        }

        return Mathf.Clamp(capacity, 1, MaxCapacity);
    }

    private MatchSession AddMatch(string matchId, string matchmakerMatchId, IEnumerable<string> players, string matchToken)
    {
        var session = new MatchSession(matchId, matchmakerMatchId, players, NowMs());

        if (!sessions.TryAdd(session))
        {
            Debug.LogWarning($"Match {matchId} not added: server full ({sessions.Count}/{sessions.Capacity}) or already hosted.");
            return null;
        }

        MatchServerContext.RegisterMatchToken(matchId, matchToken);
        idleSince = -1f;
        Debug.Log($"Hosting match {matchId}. Matches: {sessions.Count}/{sessions.Capacity}.");
        return session;
    }

    #endregion

    #region Connection Approval

    /// <summary>
    /// Accepts only players allocated to a match of this server (FR-MM-09) and routes them to
    /// it: the payload is checked locally, then AdmitPlayer verifies the UGS token, the
    /// match's MATCH_TOKEN and the owned cards. After a match's pair connected, only a player
    /// whose seat is held may come back (FR-MM-08).
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

        MatchSession held = sessions.FindHeldSeat(payload.playerId, NowMs());

        if (held != null)
        {
            AdmissionRejection rejoinRejection = MatchAdmissionPolicy.PreCheckRejoin(
                payload.playerId,
                payload.accessToken,
                true,
                verifyingPlayerIds
            );

            if (rejoinRejection != AdmissionRejection.None)
            {
                Reject(clientId, response, rejoinRejection);
                return;
            }

            response.Pending = true;
            verifyingPlayerIds.Add(payload.playerId);
            _ = AdmitAsync(clientId, payload, held, true, response);
            return;
        }

        if (string.IsNullOrWhiteSpace(payload.playerId) || string.IsNullOrWhiteSpace(payload.accessToken))
        {
            Reject(clientId, response, AdmissionRejection.MissingPayload);
            return;
        }

        if (verifyingPlayerIds.Contains(payload.playerId) || sessions.IsPlayerActive(payload.playerId))
        {
            Reject(clientId, response, AdmissionRejection.DuplicatePlayer);
            return;
        }

        response.Pending = true;
        _ = RouteAndAdmitAsync(clientId, payload, response);
    }

    /// <summary>Finds the player's match, fetching new assignments once if it is not known yet.</summary>
    private async Task RouteAndAdmitAsync(
        ulong clientId,
        ConnectionPayload payload,
        NetworkManager.ConnectionApprovalResponse response
    )
    {
        MatchSession session = FindOrCreateMatch(payload);

        if (session == null && IsAssigned)
        {
            // The client may connect before the next heartbeat brought its match.
            await PollAssignmentsNowAsync();

            if (this == null || networkManager == null || !networkManager.IsListening)
            {
                return;
            }

            session = FindOrCreateMatch(payload);
        }

        if (session == null)
        {
            Reject(clientId, response, RoutingRejection(payload.playerId));
            return;
        }

        AdmissionRejection rejection = MatchAdmissionPolicy.PreCheck(
            payload.playerId,
            payload.accessToken,
            payload.matchId,
            // The allocator creates its own MATCH_ID, different from the matchmaker ID the
            // client knows; routing used both, membership is checked by AdmitPlayer.
            null,
            new List<string>(session.Members),
            MatchSession.PlayersPerMatch,
            session.IsSealed
        );

        // The same player may have connected again while this request waited for a poll.
        if (rejection == AdmissionRejection.None && verifyingPlayerIds.Contains(payload.playerId))
        {
            rejection = AdmissionRejection.DuplicatePlayer;
        }

        if (rejection == AdmissionRejection.None && !session.TryAddMember(payload.playerId))
        {
            rejection = AdmissionRejection.MatchFull;
        }

        if (rejection != AdmissionRejection.None)
        {
            Reject(clientId, response, rejection);
            return;
        }

        verifyingPlayerIds.Add(payload.playerId);
        await AdmitAsync(clientId, payload, session, false, response);
    }

    private MatchSession FindOrCreateMatch(ConnectionPayload payload)
    {
        MatchSession session = sessions.FindForNewPlayer(payload.playerId, payload.matchId);

        if (session != null || IsAssigned || sessions.IsFull)
        {
            return session;
        }

        // Local/unassigned server: open a new match for the next pair of players.
        localMatchCount++;
        string matchId = localMatchCount == 1 && !sessions.IsKnown(LocalMatchIdPrefix)
            ? LocalMatchIdPrefix
            : $"{LocalMatchIdPrefix}-{localMatchCount}";
        return AddMatch(matchId, null, null, null);
    }

    private AdmissionRejection RoutingRejection(string playerId)
    {
        foreach (MatchSession session in sessions.Sessions)
        {
            if (session.Seats.TryFindSeat(playerId, out _))
            {
                return AdmissionRejection.CannotRejoin;
            }
        }

        return IsAssigned || sessions.Count == 0 ? AdmissionRejection.WrongMatch : AdmissionRejection.MatchFull;
    }

    private async Task AdmitAsync(
        ulong clientId,
        ConnectionPayload payload,
        MatchSession session,
        bool rejoin,
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
                session.MatchId,
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

        // The seat may have expired, or the match been cancelled, while AdmitPlayer was running.
        if (admission.Admitted && (session.IsClosed || (rejoin && !session.Seats.CanReconnect(payload.playerId, NowMs()))))
        {
            admission = PlayerAdmission.Reject(
                payload.playerId,
                MatchAdmissionPolicy.Describe(AdmissionRejection.CannotRejoin)
            );
        }

        if (admission.Admitted && !networkManager.PendingClients.ContainsKey(clientId))
        {
            // The client gave up while being verified.
            admission = PlayerAdmission.Reject(payload.playerId, "Đã hủy kết nối.");
        }

        if (admission.Admitted)
        {
            MatchServerContext.Admit(clientId, admission);
            sessions.Bind(clientId, session);
            Debug.Log($"Admitted client {clientId} as player {admission.PlayerId} to match {session.MatchId}.");
        }
        else
        {
            if (!rejoin)
            {
                session.RemoveMember(payload.playerId);
            }

            Debug.LogWarning(
                $"Rejected client {clientId} (player {payload.playerId}, match {session.MatchId}): {admission.Reason}"
            );
        }

        response.Approved = admission.Admitted;
        response.Reason = admission.Admitted ? string.Empty : admission.Reason;
        response.CreatePlayerObject = admission.Admitted && networkManager.NetworkConfig.PlayerPrefab != null;
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
        if (!MatchServerContext.TryGetAdmission(clientId, out _)
            || !sessions.TryGetByConnection(clientId, out MatchSession session))
        {
            // Approval guarantees this; kept as a guard against configuration mistakes.
            networkManager.DisconnectClient(clientId, "Chưa được xác minh.");
            return;
        }

        connectedClients.Add(clientId);
        idleSince = -1f;

        if (session.IsSealed)
        {
            // Approval only lets a player with a held seat in; NetworkMatchBridge gives the
            // seat back (GameScene is synchronized by NGO).
            Debug.Log($"Client {clientId} rejoined match {session.MatchId}.");
            return;
        }

        int players = ConnectedCount(session);
        Debug.Log(
            $"Client connected: {clientId}. Match {session.MatchId}: {players}/{MatchSession.PlayersPerMatch}"
        );

        // Only admitted players count, so a match starts with exactly the allocated pair.
        if (players == MatchSession.PlayersPerMatch)
        {
            session.Seal();
            RequestPoll();

            // GameScene is shared by every match: loaded for the first pair, later pairs are
            // synchronized into it by NGO and only see their own match's snapshots.
            LoadGameScene();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        connectedClients.Remove(clientId);

        if (sessions.TryGetByConnection(clientId, out MatchSession session) && !session.IsSealed)
        {
            // Frees the place before the pair is complete; afterwards the admission is kept
            // for the report and the seat is held by NetworkMatchBridge.
            if (MatchServerContext.TryGetAdmission(clientId, out PlayerAdmission admission))
            {
                session.RemoveMember(admission.PlayerId);
            }

            MatchServerContext.Forget(clientId);
        }

        Debug.Log(
            $"Client disconnected: {clientId}. Remaining players: {connectedClients.Count}"
        );

        CleanUpMatches();
    }

    private int ConnectedCount(MatchSession session)
    {
        int count = 0;

        foreach (ulong clientId in connectedClients)
        {
            if (sessions.TryGetByConnection(clientId, out MatchSession routed) && routed == session)
            {
                count++;
            }
        }

        return count;
    }

    private List<ulong> ConnectedClientsOf(MatchSession session)
    {
        var result = new List<ulong>();

        foreach (ulong clientId in connectedClients)
        {
            if (sessions.TryGetByConnection(clientId, out MatchSession routed) && routed == session)
            {
                result.Add(clientId);
            }
        }

        return result;
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

    #region Match Lifetime

    /// <summary>
    /// Frees the slot of every match that is over: finished (or abandoned) with nobody left
    /// and no seat held, cancelled before its pair connected, or idle on the result screen.
    /// A match whose player is away keeps its slot until the grace period decides it.
    /// </summary>
    private void CleanUpMatches()
    {
        bool changed = false;
        float now = Time.realtimeSinceStartup;

        foreach (MatchSession session in sessions.Sessions)
        {
            List<ulong> clients = ConnectedClientsOf(session);

            if (session.IsFinished && !finishedSince.ContainsKey(session.MatchId))
            {
                finishedSince[session.MatchId] = now;
            }

            if (!session.IsSealed && !session.IsClosed
                && NowMs() - session.CreatedAtMs > (long)(unsealedMatchTimeoutSeconds * 1000f))
            {
                Debug.LogWarning($"Match {session.MatchId}: the pair never connected; cancelling.");
                session.Close();
                Disconnect(clients, "Đối thủ không vào trận. Trận đấu bị hủy.");
            }
            else if (session.IsFinished && clients.Count > 0
                && now - finishedSince[session.MatchId] > finishedMatchLingerSeconds)
            {
                Disconnect(clients, "Trận đấu đã kết thúc.");
            }

            bool over = session.IsClosed
                || (session.IsFinished && session.IsSettlementStarted)
                || (session.IsSealed && !session.IsStarted && !session.IsHoldingSeats);

            if (over && clients.Count == 0 && !session.IsHoldingSeats)
            {
                session.Close();
                sessions.Remove(session.MatchId);
                finishedSince.Remove(session.MatchId);
                changed = true;
                Debug.Log($"Match {session.MatchId} closed. Matches: {sessions.Count}/{sessions.Capacity}.");
            }
        }

        if (changed)
        {
            RequestPoll();
        }
    }

    private void Disconnect(List<ulong> clients, string reason)
    {
        foreach (ulong clientId in clients)
        {
            networkManager.DisconnectClient(clientId, reason);
        }
    }

    /// <summary>
    /// Once a second: frees finished matches and quits when the server has no match.
    /// Capacity 1 quits as soon as its match is over (original behaviour); a multi-match
    /// server waits <see cref="emptyServerTimeoutSeconds"/> for new matches first (NFR-21).
    /// </summary>
    private async Task RunLifetimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);

                if (this == null || shutdownPending)
                {
                    return;
                }

                CleanUpMatches();

                if (!sessions.IsEmpty || connectedClients.Count > 0 || verifyingPlayerIds.Count > 0)
                {
                    idleSince = -1f;
                    continue;
                }

                bool hostedAMatch = sessions.KnownMatchIds.Count > 0;

                if (sessions.Capacity == 1 && hostedAMatch)
                {
                    await QuitAsync("The match is over.");
                    return;
                }

                if (idleSince < 0f)
                {
                    idleSince = Time.realtimeSinceStartup;
                }

                if (Time.realtimeSinceStartup - idleSince >= emptyServerTimeoutSeconds)
                {
                    // Tell the allocator first; a match placed meanwhile cancels the shutdown.
                    if (IsAssigned && await PollAssignmentsAsync(true) && !sessions.IsEmpty)
                    {
                        idleSince = -1f;
                        continue;
                    }

                    await QuitAsync("Server remained unused.");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Server destroyed.
        }
    }

    private async Task QuitAsync(string reason)
    {
        if (shutdownPending)
        {
            return;
        }

        shutdownPending = true;
        Task reports = MatchServerContext.AllReports();

        if (!reports.IsCompleted)
        {
            Debug.Log("Waiting for match reports before stopping.");
            int graceMilliseconds = Mathf.CeilToInt(reportShutdownGraceSeconds * 1000f);
            await Task.WhenAny(reports, Task.Delay(graceMilliseconds));
        }

        Debug.Log($"{reason} Stopping server.");
        Application.Quit();
    }

    #endregion

    #region Match Assignments

    /// <summary>
    /// Heartbeat every <see cref="assignmentPollSeconds"/> (or at once after a match started or
    /// ended): reports capacity and running matches, and receives matches placed here.
    /// </summary>
    private async Task RunAssignmentPollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !shutdownPending)
        {
            await PollAssignmentsAsync(false);

            if (this == null)
            {
                return;
            }

            pollNow = new TaskCompletionSource<bool>();
            int delay = Mathf.CeilToInt(Mathf.Max(0.5f, assignmentPollSeconds) * 1000f);

            try
            {
                await Task.WhenAny(Task.Delay(delay, cancellationToken), pollNow.Task);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void RequestPoll()
    {
        pollNow?.TrySetResult(true);
    }

    private Task PollAssignmentsNowAsync()
    {
        return PollAssignmentsAsync(false);
    }

    /// <summary>One GetAssignedMatches call (shared when one is already running). True on success.</summary>
    private Task<bool> PollAssignmentsAsync(bool draining)
    {
        if (!IsAssigned)
        {
            return Task.FromResult(false);
        }

        if (pollInFlight != null && !pollInFlight.IsCompleted && !draining)
        {
            return pollInFlight;
        }

        pollInFlight = PollOnceAsync(draining);
        return pollInFlight;
    }

    private async Task<bool> PollOnceAsync(bool draining)
    {
        var heartbeat = new ServerHeartbeat
        {
            ServerId = serverId,
            ServerToken = serverToken,
            Capacity = sessions.Capacity,
            KnownMatchIds = new List<string>(sessions.KnownMatchIds),
            ActiveMatchIds = sessions.ActiveMatchIds(),
            Draining = draining,
        };

        AssignmentPoll poll;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(admissionTimeoutSeconds));
            poll = await assignments.GetAssignedMatchesAsync(heartbeat, timeout.Token);
        }
        catch (Exception exception)
        {
            poll = AssignmentPoll.Fail(exception.Message);
        }

        if (this == null)
        {
            return false;
        }

        if (!poll.Success)
        {
            Debug.LogWarning($"[Assignments] GetAssignedMatches failed: {poll.Message}");
            return false;
        }

        foreach (MatchAssignment assignment in poll.Matches)
        {
            if (sessions.TryGet(assignment.MatchId, out _) || sessions.IsKnown(assignment.MatchId))
            {
                continue;
            }

            AddMatch(assignment.MatchId, assignment.MatchmakerMatchId, assignment.PlayerIds, assignment.MatchToken);
        }

        return true;
    }

    #endregion

    #region Helpers

    private static string ReadVariable(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IEnumerable<string> SplitPlayers(string csv)
    {
        return string.IsNullOrEmpty(csv)
            ? Array.Empty<string>()
            : csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static long NowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    #endregion
}
