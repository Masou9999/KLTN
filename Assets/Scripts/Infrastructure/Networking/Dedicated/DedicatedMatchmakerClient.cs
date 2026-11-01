using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.Domain.Ranking;
using KLTN.Infrastructure.CloudCode;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class DedicatedMatchmakerClient : MonoBehaviour
{
    #region Constants

    private const int PollDelayMilliseconds = 1500;

    // The matchmaker keeps retrying a failed allocation (e.g. hosting quota reached), so the
    // client gives up on its own instead of searching forever.
    private const float MaxSearchSeconds = 180f;
    private const float MaxServerStartSeconds = 90f;

    // Matchmaker queue rule (Dashboard): Difference on Players.CustomData.rank_tier <= 1.
    private const string RankTierKey = "rank_tier";

    // Must exceed the server's 10 s AdmitPlayer timeout (not part of NGO's config hash).
    private const int ConnectionBufferTimeoutSeconds = 15;

    #endregion

    #region Configuration

    [Header("Matchmaker")]
    [SerializeField]
    private string queueName = "boardgame-queue";

    [Header("UI")]
    [SerializeField]
    private Button matchmakingButton;

    [SerializeField]
    private TMP_Text actionLabel;

    [SerializeField]
    private TMP_Text findingMatchLabel;

    [SerializeField]
    private TMP_Text timerLabel;

    [SerializeField]
    private TMP_Text statusLabel;

    #endregion

    #region Runtime State

    private CancellationTokenSource searchCancellation;
    private string activeTicketId;
    private float elapsedTime;
    private bool isSearching;
    private bool isConnecting;

    #endregion

    #region Unity Lifecycle

    private void OnEnable()
    {
        if (matchmakingButton != null)
        {
            matchmakingButton.onClick.RemoveListener(ToggleMatchmaking);
            matchmakingButton.onClick.AddListener(ToggleMatchmaking);
        }

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        ShowIdleState();
    }

    private void OnDisable()
    {
        if (matchmakingButton != null)
        {
            matchmakingButton.onClick.RemoveListener(ToggleMatchmaking);
        }

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        searchCancellation?.Cancel();
    }

    private void Update()
    {
        if (!isSearching)
        {
            return;
        }

        elapsedTime += Time.deltaTime;
        int minutes = Mathf.FloorToInt(elapsedTime / 60f);
        int seconds = Mathf.FloorToInt(elapsedTime % 60f);

        if (timerLabel != null)
        {
            timerLabel.text = $"{minutes:00}:{seconds:00}";
        }
    }

    #endregion

    #region UI Events

    public async void ToggleMatchmaking()
    {
        if (isSearching)
        {
            await CancelSearchAsync();
            return;
        }

        await StartSearchAsync();
    }

    public async void BackToMainMenu()
    {
        await CancelSearchAsync();
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        SceneManager.LoadScene(SceneNames.MainMenu);
    }

    #endregion

    #region Matchmaking

    private async Task StartSearchAsync()
    {
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            ShowResultState("Bạn chưa đăng nhập UGS.");
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            ShowResultState("Không tìm thấy NetworkManager.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            ShowResultState("NetworkManager đang chạy.");
            return;
        }

        searchCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = searchCancellation.Token;
        isSearching = true;
        isConnecting = false;
        elapsedTime = 0f;
        SetActionLabel("Hủy tìm trận");
        ShowSearchingState();

        try
        {
            string publicIp = await PublicIpResolver.ResolveAsync(cancellationToken);

            RankTier rankTier = await ResolveRankTierAsync();
            var customData = new Dictionary<string, object>
            {
                ["player_ip"] = publicIp,
                [RankTierKey] = (int)rankTier,
            };
            Debug.Log($"[Matchmaker] Ticket rank_tier={(int)rankTier} ({rankTier}).");

            var players = new List<Player>
            {
                new Player(AuthenticationService.Instance.PlayerId, customData),
            };
            ShowSearchingState("ĐANG TẠO TICKET:");

            CreateTicketResponse ticket =
                await MatchmakerService.Instance.CreateTicketAsync(
                    players,
                    new CreateTicketOptions(queueName)
                );
            activeTicketId = ticket.Id;
            Debug.Log($"Created matchmaker ticket: {activeTicketId}");
            ShowSearchingState("ĐANG TÌM ĐỐI THỦ:");
            IpPortAssignment assignment = await WaitForAssignmentAsync(cancellationToken);

            // A matched ticket no longer needs client-side deletion.
            activeTicketId = null;
            ConnectToServer(assignment);
        }
        catch (OperationCanceledException)
        {
            ShowResultState("Đã hủy tìm trận.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            await DeleteActiveTicketAsync();
            ShowResultState($"Tìm trận thất bại: {exception.Message}");
        }
        finally
        {
            isSearching = false;
            searchCancellation?.Dispose();
            searchCancellation = null;

            if (!isConnecting)
            {
                SetActionLabel("Tìm trận");

                if (matchmakingButton != null)
                {
                    matchmakingButton.interactable = true;
                }
            }
        }
    }

    /// <summary>
    /// Ticket rank_tier computed by Cloud Code (GetTicketTier, FR-RANK-12) instead of the
    /// client's own leaderboard row. Throws when the backend is unreachable, which ends the
    /// search with a message (the allocator would refuse the ticket anyway).
    /// </summary>
    private static Task<RankTier> ResolveRankTierAsync()
    {
        return MatchServerModuleClient.GetTicketTierAsync();
    }

    private async Task<IpPortAssignment> WaitForAssignmentAsync(
        CancellationToken cancellationToken
    )
    {
        float searchStarted = Time.realtimeSinceStartup;
        float? serverStartStarted = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float now = Time.realtimeSinceStartup;

            if (serverStartStarted.HasValue && now - serverStartStarted.Value > MaxServerStartSeconds)
            {
                throw new TimeoutException("Không khởi tạo được server trận đấu, hãy thử lại sau.");
            }

            if (now - searchStarted > MaxSearchSeconds)
            {
                throw new TimeoutException("Chưa tìm được trận, hãy thử lại sau.");
            }

            TicketStatusResponse ticketStatus =
                await MatchmakerService.Instance.GetTicketAsync(activeTicketId);

            if (ticketStatus == null || ticketStatus.Type == typeof(NoneAssignment))
            {
                await Task.Delay(PollDelayMilliseconds, cancellationToken);
                continue;
            }

            if (ticketStatus.Type != typeof(IpPortAssignment))
            {
                throw new InvalidOperationException(
                    $"Unexpected assignment type: {ticketStatus.Type}"
                );
            }

            var assignment = (IpPortAssignment)ticketStatus.Value;
            Debug.Log(
                $"Assignment status: {assignment.Status}; message: {assignment.Message}"
            );

            switch (assignment.Status)
            {
                case IpPortAssignment.StatusOptions.Found:
                    ValidateAssignment(assignment);
                    return assignment;

                case IpPortAssignment.StatusOptions.Failed:
                    throw new InvalidOperationException(
                        assignment.Message ?? "Matchmaker assignment failed."
                    );

                case IpPortAssignment.StatusOptions.Timeout:
                    throw new TimeoutException(
                        assignment.Message ?? "Matchmaker assignment timed out."
                    );

                case IpPortAssignment.StatusOptions.InProgress:
                    serverStartStarted ??= Time.realtimeSinceStartup;
                    ShowSearchingState("ĐANG KHỞI TẠO SERVER:");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(assignment.Status),
                        assignment.Status,
                        "Unknown assignment status."
                    );
            }

            await Task.Delay(PollDelayMilliseconds, cancellationToken);
        }
    }

    private static void ValidateAssignment(IpPortAssignment assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment.Ip))
        {
            throw new InvalidOperationException(
                "Assignment does not contain an IP address."
            );
        }

        if (
            !assignment.Port.HasValue
            || assignment.Port.Value <= 0
            || assignment.Port.Value > ushort.MaxValue
        )
        {
            throw new InvalidOperationException("Assignment contains an invalid port.");
        }
    }

    private async Task CancelSearchAsync()
    {
        searchCancellation?.Cancel();
        await DeleteActiveTicketAsync();

        isSearching = false;
        isConnecting = false;
        SetActionLabel("Tìm trận");
        ShowResultState("Đã hủy tìm trận.");

        if (matchmakingButton != null)
        {
            matchmakingButton.interactable = true;
        }
    }

    private async Task DeleteActiveTicketAsync()
    {
        string ticketId = activeTicketId;
        activeTicketId = null;

        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return;
        }

        try
        {
            await MatchmakerService.Instance.DeleteTicketAsync(ticketId);
            Debug.Log($"Deleted matchmaker ticket: {ticketId}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not delete ticket {ticketId}: " + exception.Message);
        }
    }

    #endregion

    #region Network Connection

    private void ConnectToServer(IpPortAssignment assignment)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        UnityTransport transport = networkManager.GetComponent<UnityTransport>();

        if (transport == null)
        {
            throw new InvalidOperationException(
                "NetworkManager does not contain UnityTransport."
            );
        }

        ushort port = checked((ushort)assignment.Port.Value);
        Debug.Log($"Connecting to Edgegap endpoint {assignment.Ip}:{port}");
        transport.SetConnectionData(assignment.Ip, port);
        ConfigureConnectionApproval(networkManager, assignment.MatchId);
        isConnecting = true;
        SetActionLabel("Đang kết nối");
        ShowResultState("Đã tìm thấy trận. Đang kết nối server...");

        if (matchmakingButton != null)
        {
            matchmakingButton.interactable = false;
        }

        if (!networkManager.StartClient())
        {
            isConnecting = false;
            throw new InvalidOperationException(
                "NetworkManager.StartClient returned false."
            );
        }
    }

    /// <summary>
    /// The dedicated server admits only allocated players: send who we are (FR-MM-09).
    /// ConnectionApproval must match the server's flag because NGO hashes it.
    /// </summary>
    private static void ConfigureConnectionApproval(NetworkManager networkManager, string matchId)
    {
        var payload = new ConnectionPayload
        {
            playerId = AuthenticationService.Instance.PlayerId,
            accessToken = AuthenticationService.Instance.AccessToken,
            matchId = matchId,
        };

        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.ConnectionData = payload.ToBytes();
        networkManager.NetworkConfig.ClientConnectionBufferTimeout = Math.Max(
            networkManager.NetworkConfig.ClientConnectionBufferTimeout,
            ConnectionBufferTimeoutSeconds
        );
        networkManager.OnClientStopped -= RestoreListenConfig;
        networkManager.OnClientStopped += RestoreListenConfig;
    }

    /// <summary>Host/Join (listen-host) runs without approval; undo the dedicated settings.</summary>
    private static void RestoreListenConfig(bool wasHost)
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientStopped -= RestoreListenConfig;
        networkManager.NetworkConfig.ConnectionApproval = false;
        networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();
    }

    private void OnClientConnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null || clientId != networkManager.LocalClientId)
        {
            return;
        }

        isConnecting = false;
        SetActionLabel("Đã kết nối");
        ShowResultState("Đã vào server. Đang chờ đối thủ...");

        // Do not load GameScene here. The server synchronizes it for every client.
    }

    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null || clientId != networkManager.LocalClientId)
        {
            return;
        }

        isConnecting = false;
        string reason = networkManager.DisconnectReason;
        SetActionLabel("Tìm trận");

        ShowResultState(
            string.IsNullOrWhiteSpace(reason)
                ? "Đã mất kết nối với server."
                : $"Mất kết nối: {reason}"
        );

        if (matchmakingButton != null)
        {
            matchmakingButton.interactable = true;
        }
    }

    #endregion

    #region UI Rendering

    private void ShowIdleState()
    {
        elapsedTime = 0f;
        SetActionLabel("Tìm trận");
        SetActive(findingMatchLabel, false);
        SetActive(timerLabel, false);
        SetActive(statusLabel, false);

        if (timerLabel != null)
        {
            timerLabel.text = "00:00";
        }

        if (statusLabel != null)
        {
            statusLabel.text = string.Empty;
        }

        if (matchmakingButton != null)
        {
            matchmakingButton.interactable = true;
        }
    }

    private void ShowSearchingState(string message = "ĐANG TÌM TRẬN:")
    {
        SetActive(findingMatchLabel, true);
        SetActive(timerLabel, true);
        SetActive(statusLabel, false);

        if (findingMatchLabel != null)
        {
            findingMatchLabel.text = message;
        }
    }

    private void ShowResultState(string message)
    {
        SetActive(findingMatchLabel, false);
        SetActive(timerLabel, false);
        SetActive(statusLabel, true);

        if (statusLabel != null)
        {
            statusLabel.text = message;
        }
    }

    private void SetActionLabel(string message)
    {
        if (actionLabel != null)
        {
            actionLabel.text = message;
        }
    }

    private static void SetActive(TMP_Text label, bool isActive)
    {
        if (label != null)
        {
            label.gameObject.SetActive(isActive);
        }
    }

    #endregion
}
