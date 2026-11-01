using System;
using System.Collections;
using System.Collections.Generic;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Connection Handling and Seat Assignment

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            if (!TryAssignClient(clientId, out MatchSession session))
            {
                return;
            }

            TryInitializeMatch(session);
            BroadcastUpdate(session);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            pendingSnapshotClients.Remove(clientId);

            // Found by seat, not by route: the bootstrap may already have forgotten the route.
            if (!TryFindSeatedSession(clientId, out MatchSession session)
                || !session.RemoveConnection(clientId, out SeatId leavingSeat))
            {
                ForgetRemovedMatches();
                return;
            }

            Debug.Log($"Client {clientId} left {leavingSeat} of match {session.MatchId}.");

            // The match is kept: the seat is held for the grace period (FR-NET-06). A finished
            // match only needs the remaining player to see the result.
            if (!session.IsFinished && !session.IsClosed)
            {
                HoldSeat(session, leavingSeat);
            }

            BroadcastUpdate(session);
            ForgetRemovedMatches();
        }

        private void AssignConnectedClients()
        {
            if (NetworkManager.IsHost)
            {
                TryAssignClient(NetworkManager.LocalClientId, out _);
            }

            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                TryAssignClient(clientId, out _);
            }
        }

        /// <summary>
        /// Seats the connection in its own match: the held seat of a returning player, else the
        /// first free seat. Returns the match when the connection belongs to one.
        /// </summary>
        private bool TryAssignClient(ulong clientId, out MatchSession session)
        {
            if (!TryResolveSession(clientId, out session))
            {
                Debug.LogWarning($"Client {clientId} has no match on this server.");
                return false;
            }

            if (session.TryGetSeat(clientId, out _) || session.IsClosed)
            {
                return true;
            }

            if (TryReclaimSeat(session, clientId))
            {
                return true;
            }

            if (session.TryTakeFreeSeat(clientId, out SeatId seat))
            {
                BindSeat(session, clientId, seat);
                Debug.Log($"Assigned client {clientId} to {seat} of match {session.MatchId}.");
                return true;
            }

            Debug.LogWarning(
                $"Client {clientId} could not be assigned. Both seats of match {session.MatchId} are occupied."
            );
            return true;
        }

        /// <summary>
        /// Dedicated: the route set by connection approval. Listen-host: the only local match.
        /// </summary>
        private bool TryResolveSession(ulong clientId, out MatchSession session)
        {
            if (sessions == null)
            {
                session = null;
                return false;
            }

            if (sessions.TryGetByConnection(clientId, out session))
            {
                return true;
            }

            if (MatchServerContext.IsDedicated || sessions.IsEmpty)
            {
                return false;
            }

            session = sessions.Sessions[0];
            sessions.Bind(clientId, session);
            return true;
        }

        private bool TryFindSeatedSession(ulong clientId, out MatchSession session)
        {
            if (sessions != null)
            {
                foreach (MatchSession candidate in sessions.Sessions)
                {
                    if (candidate.TryGetSeat(clientId, out _))
                    {
                        session = candidate;
                        return true;
                    }
                }
            }

            session = null;
            return false;
        }

        /// <summary>The connection's match and seat; false when it is not seated.</summary>
        private bool TryGetSeatedClient(ulong clientId, out MatchSession session, out SeatId seat)
        {
            seat = default;
            return TryResolveSession(clientId, out session) && session.TryGetSeat(clientId, out seat);
        }

        #endregion

        #region Snapshot Requests

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestSnapshotRpc(RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            Debug.Log($"Snapshot request received from client {senderClientId}.");

            if (!TryAssignClient(senderClientId, out MatchSession session))
            {
                return;
            }

            if (TryInitializeMatch(session))
            {
                BroadcastUpdate(session);
                return;
            }

            if (!session.TryGetSeat(senderClientId, out SeatId viewerSeat))
            {
                Debug.LogWarning($"No seat found for client {senderClientId}.");
                return;
            }

            SendUpdate(senderClientId, session, viewerSeat, null);
            ResendSettlement(session, senderClientId, viewerSeat);
        }

        private void QueueSnapshotWhenVisible(ulong clientId)
        {
            if (!IsServer || !pendingSnapshotClients.Add(clientId))
            {
                return;
            }

            StartCoroutine(SendSnapshotWhenVisible(clientId));
        }

        private IEnumerator SendSnapshotWhenVisible(ulong clientId)
        {
            while (
                IsSpawned
                && NetworkManager != null
                && NetworkManager.IsListening
                && NetworkManager.ConnectedClients.ContainsKey(clientId)
                && !NetworkObject.IsNetworkVisibleTo(clientId)
            )
            {
                yield return null;
            }

            pendingSnapshotClients.Remove(clientId);

            if (
                !IsSpawned
                || NetworkManager == null
                || !NetworkManager.ConnectedClients.ContainsKey(clientId)
            )
            {
                yield break;
            }

            if (!TryGetSeatedClient(clientId, out MatchSession session, out SeatId viewerSeat))
            {
                Debug.LogWarning($"Cannot send snapshot: client {clientId} has no seat.");
                yield break;
            }

            Debug.Log($"NetworkMatchBridge is now visible to client {clientId}.");

            SendUpdate(clientId, session, viewerSeat, null);
        }

        private IEnumerator RequestInitialSnapshot()
        {
            yield return null;

            for (
                int attempt = 1;
                attempt <= SnapshotRequestAttempts && !hasReceivedSnapshot;
                attempt++
            )
            {
                if (
                    !IsSpawned
                    || NetworkManager == null
                    || !NetworkManager.IsConnectedClient
                )
                {
                    yield break;
                }

                Debug.Log(
                    $"Requesting initial snapshot. "
                        + $"Attempt={attempt}/{SnapshotRequestAttempts}, "
                        + $"ClientId={NetworkManager.LocalClientId}."
                );

                RequestSnapshotRpc();

                yield return new WaitForSecondsRealtime(SnapshotRequestIntervalSeconds);
            }

            initialSnapshotCoroutine = null;

            if (!hasReceivedSnapshot)
            {
                Debug.LogError(
                    $"Initial snapshot was not received after "
                        + $"{SnapshotRequestAttempts} attempts."
                );
            }
        }

        #endregion
    }
}
