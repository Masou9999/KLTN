using System;
using System.Collections;
using System.Globalization;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Scripted player for the multi-match load test (plan 05 D, I6). Started from the server
/// build with <c>-loadTestClient host:port</c>: connects with a fake identity (the server must
/// run with MATCH_REPORTER_STUB=1), keeps its opening hand, passes every few seconds and
/// surrenders after <c>-loadTestMatchSeconds</c>, then quits. Two bots form one match.
/// Never active in a normal run.
/// </summary>
[DisallowMultipleComponent]
public sealed class LoadTestClient : MonoBehaviour
{
    #region Command Line

    public const string ConnectArgument = "-loadTestClient";
    public const string MatchSecondsArgument = "-loadTestMatchSeconds";
    public const string ActionIntervalArgument = "-loadTestActionSeconds";

    #endregion

    #region State

    private NetworkManager networkManager;
    private float matchSeconds = 180f;
    private float actionIntervalSeconds = 2f;

    #endregion

    /// <summary>The <c>-loadTestClient host:port</c> argument, or null in a normal run.</summary>
    public static string ConnectTarget()
    {
        return ReadArgument(ConnectArgument);
    }

    public void Begin(NetworkManager manager, string target)
    {
        networkManager = manager;
        matchSeconds = ReadFloat(MatchSecondsArgument, matchSeconds);
        actionIntervalSeconds = Mathf.Max(0.2f, ReadFloat(ActionIntervalArgument, actionIntervalSeconds));

        string[] parts = target.Split(':');
        string host = parts[0];
        ushort port = parts.Length > 1 && ushort.TryParse(parts[1], out ushort parsed) ? parsed : (ushort)7777;

        var payload = new ConnectionPayload
        {
            playerId = "loadtest-" + Guid.NewGuid().ToString("N").Substring(0, 12),
            accessToken = "loadtest",
            matchId = null,
        };

        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.ConnectionData = payload.ToBytes();
        networkManager.GetComponent<UnityTransport>().SetConnectionData(host, port);
        networkManager.OnClientDisconnectCallback += OnDisconnected;

        Debug.Log($"[LoadTest] {payload.playerId} connecting to {host}:{port}.");

        if (!networkManager.StartClient())
        {
            Debug.LogError("[LoadTest] StartClient failed.");
            Application.Quit(1);
            return;
        }

        StartCoroutine(Play(payload.playerId));
    }

    private IEnumerator Play(string playerId)
    {
        var wait = new WaitForSecondsRealtime(actionIntervalSeconds);
        float startedAt = -1f;
        bool mulliganSent = false;
        bool surrendered = false;

        while (true)
        {
            yield return wait;

            NetworkMatchBridge bridge = FindAnyObjectByType<NetworkMatchBridge>();
            MatchSnapshotDto snapshot = MatchProjectionRegistry.Current.Current;

            if (bridge == null || !bridge.IsSpawned || snapshot == null || string.IsNullOrEmpty(snapshot.matchId))
            {
                continue;
            }

            if (startedAt < 0f)
            {
                startedAt = Time.realtimeSinceStartup;
                Debug.Log($"[LoadTest] {playerId} in match {snapshot.matchId} as seat {snapshot.viewerSeat}.");
            }

            if (snapshot.outcome != (int)MatchOutcome.Running)
            {
                Debug.Log($"[LoadTest] {playerId}: match {snapshot.matchId} over (outcome {snapshot.outcome}).");
                yield return new WaitForSecondsRealtime(2f);
                networkManager.Shutdown();
                Application.Quit(0);
                yield break;
            }

            if (!mulliganSent)
            {
                mulliganSent = bridge.RequestMulligan(Array.Empty<ulong>());
                continue;
            }

            if (!surrendered && Time.realtimeSinceStartup - startedAt >= matchSeconds)
            {
                surrendered = bridge.RequestSurrender();
                continue;
            }

            // Rejected when it is not our turn; either way the server builds and sends snapshots.
            bridge.RequestPass();
        }
    }

    private void OnDisconnected(ulong clientId)
    {
        if (networkManager != null && clientId == networkManager.LocalClientId)
        {
            Debug.Log($"[LoadTest] Disconnected: {networkManager.DisconnectReason}");
            Application.Quit(0);
        }
    }

    #region Helpers

    private static string ReadArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static float ReadFloat(string name, float fallback)
    {
        string text = ReadArgument(name);
        return text != null && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : fallback;
    }

    #endregion
}
