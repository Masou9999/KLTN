using System;
using System.Collections;
using System.Collections.Generic;
using CMCMProductions;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    /// <summary>
    /// NGO adapter for the match domain. Clients send intent IDs; the server validates
    /// them through <see cref="MatchRulesEngine"/> and publishes viewer-filtered DTOs.
    /// Card GameObjects and transforms are never authoritative network state.
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

        private readonly Dictionary<ulong, SeatId> seatByClient =
            new Dictionary<ulong, SeatId>();
        private readonly Dictionary<string, CardDefinition> definitionsById =
            new Dictionary<string, CardDefinition>();
        private readonly Dictionary<ulong, ulong> lastCommandIdByClient =
            new Dictionary<ulong, ulong>();
        private readonly HashSet<ulong> pendingSnapshotClients = new HashSet<ulong>();
        private ulong revision;
        private MatchState matchState;
        private MatchRulesEngine rulesEngine;
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
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

                BeginSeatTracking();
                AssignConnectedClients();
                TryInitializeMatch();
                BroadcastUpdate();
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
                EndSeatTracking();
            }

            pendingSnapshotClients.Clear();
            ResetLoadouts();
            MatchProjectionRegistry.Current.Reset();
            MatchUpdateInboxRegistry.Current.Reset();
        }

        #endregion
    }
}
