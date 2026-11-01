using System;
using System.Collections;
using System.Collections.Generic;
using CMCMProductions;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    /// <summary>
    /// NGO transport adapter for the match application layer. Clients send intent IDs; the
    /// server routes each one from its connection to the right <see cref="MatchSession"/>
    /// (several matches can share one dedicated process, FR-MM-10), validates it through
    /// <see cref="MatchRulesEngine"/> and sends viewer-filtered DTOs only to that match's
    /// players. Card GameObjects and transforms are never authoritative network state.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Constants

        private const int OpeningHandSize = 4;
        private const int SnapshotRequestAttempts = 10;
        private const float SnapshotRequestIntervalSeconds = 0.5f;

        #endregion

        #region Server State

        // Card content is the same for every match, so definitions and mappers are shared.
        private readonly Dictionary<string, CardDefinition> definitionsById =
            new Dictionary<string, CardDefinition>();
        private readonly HashSet<ulong> pendingSnapshotClients = new HashSet<ulong>();

        // Per-match runtime state that only the NGO adapter needs (coroutines, settlement DTOs).
        private readonly Dictionary<string, MatchRuntime> runtimeByMatch =
            new Dictionary<string, MatchRuntime>(StringComparer.Ordinal);

        private MatchSessionRegistry sessions;
        private MatchCommandService commandService;
        private MatchSnapshotBuilder snapshotBuilder;
        private RoundResolutionDtoMapper resolutionDtoMapper;

        #endregion

        #region Client State

        private ulong nextLocalCommandId = 1;
        private Coroutine initialSnapshotCoroutine;
        private bool hasReceivedSnapshot;

        #endregion

        #region Network Lifecycle

        public override void OnNetworkSpawn()
        {
            hasReceivedSnapshot = false;
            MatchSettlementRegistry.Reset();

            if (IsServer)
            {
                // A dedicated server shares the registry with DedicatedServerBootstrap, which
                // creates its matches; listen-host has exactly one local match.
                sessions = MatchServerContext.IsDedicated ? MatchServerContext.Sessions : CreateListenSessions();
                commandService = new MatchCommandService(sessions);

                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

                AssignConnectedClients();

                foreach (MatchSession session in sessions.Sessions)
                {
                    TryInitializeMatch(session);
                    BroadcastUpdate(session);
                }
            }

            if (IsClient)
            {
                // Host (server + client) and remote clients both report their owned cards.
                SubmitLocalLoadout();
            }

            if (IsClient && !IsServer)
            {
                initialSnapshotCoroutine = StartCoroutine(RequestInitialSnapshot());
            }

            Debug.Log(
                $"NetworkMatchBridge spawned. "
                    + $"ClientId={NetworkManager.LocalClientId}, "
                    + $"IsServer={IsServer}, IsClient={IsClient}, "
                    + $"NetworkObjectId={NetworkObjectId}."
            );
        }

        public override void OnNetworkDespawn()
        {
            if (initialSnapshotCoroutine != null)
            {
                StopCoroutine(initialSnapshotCoroutine);
                initialSnapshotCoroutine = null;
            }

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            foreach (MatchRuntime runtime in runtimeByMatch.Values)
            {
                StopRuntimeCoroutines(runtime);
            }

            runtimeByMatch.Clear();
            pendingSnapshotClients.Clear();
            MatchProjectionRegistry.Current.Reset();
            MatchUpdateInboxRegistry.Current.Reset();
        }

        #endregion

        #region Match Runtime

        /// <summary>NGO-side state of one match; the domain state lives in <see cref="MatchSession"/>.</summary>
        private sealed class MatchRuntime
        {
            public readonly Dictionary<SeatId, MatchSettlementDto> SettlementBySeat =
                new Dictionary<SeatId, MatchSettlementDto>();

            public Coroutine GraceCoroutine;
            public Coroutine LoadoutTimeoutCoroutine;
            public bool LoadoutTimeoutElapsed;
        }

        private MatchRuntime RuntimeOf(MatchSession session)
        {
            if (!runtimeByMatch.TryGetValue(session.MatchId, out MatchRuntime runtime))
            {
                runtime = new MatchRuntime();
                runtimeByMatch.Add(session.MatchId, runtime);
            }

            return runtime;
        }

        private void StopRuntimeCoroutines(MatchRuntime runtime)
        {
            if (runtime.GraceCoroutine != null)
            {
                StopCoroutine(runtime.GraceCoroutine);
                runtime.GraceCoroutine = null;
            }

            if (runtime.LoadoutTimeoutCoroutine != null)
            {
                StopCoroutine(runtime.LoadoutTimeoutCoroutine);
                runtime.LoadoutTimeoutCoroutine = null;
            }
        }

        /// <summary>Drops the adapter state of matches the server no longer hosts.</summary>
        private void ForgetRemovedMatches()
        {
            foreach (string matchId in new List<string>(runtimeByMatch.Keys))
            {
                if (!sessions.TryGet(matchId, out _))
                {
                    StopRuntimeCoroutines(runtimeByMatch[matchId]);
                    runtimeByMatch.Remove(matchId);
                }
            }
        }

        private static MatchSessionRegistry CreateListenSessions()
        {
            var registry = new MatchSessionRegistry(1);
            registry.TryAdd(new MatchSession(Guid.NewGuid().ToString("N"), createdAtMs: NowMs()));
            return registry;
        }

        #endregion
    }
}
