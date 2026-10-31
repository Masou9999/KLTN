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
    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Connection Handling and Seat Assignment

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            AssignClient(clientId);
            TryInitializeMatch();
            BroadcastUpdate();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            if (!seatByClient.TryGetValue(clientId, out SeatId leavingSeat))
            {
                return;
            }

            seatByClient.Remove(clientId);
            loadoutBySeat.Remove(leavingSeat);
            currentMatchId = null;
            loadoutTimeoutElapsed = false;
            lastCommandIdByClient.Remove(clientId);
            Debug.Log($"Removed seat assignment for client {clientId}.");
            matchState = null;
            rulesEngine = null;
            snapshotBuilder = null;
            resolutionDtoMapper = null;
            definitionsById.Clear();
            BroadcastUpdate();
        }

        private void AssignConnectedClients()
        {
            if (NetworkManager.IsHost)
            {
                AssignClientToSeat(NetworkManager.LocalClientId, SeatId.Host);
            }

            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                AssignClient(clientId);
            }
        }

        private void AssignClient(ulong clientId)
        {
            if (seatByClient.ContainsKey(clientId))
            {
                return;
            }

            if (!HasSeat(SeatId.Host))
            {
                AssignClientToSeat(clientId, SeatId.Host);
                return;
            }

            if (!HasSeat(SeatId.Guest))
            {
                AssignClientToSeat(clientId, SeatId.Guest);
                return;
            }

            Debug.LogWarning(
                $"Client {clientId} could not be assigned. " + "Both seats are occupied."
            );
        }

        private void AssignClientToSeat(ulong clientId, SeatId seat)
        {
            if (seatByClient.ContainsKey(clientId) || HasSeat(seat))
            {
                return;
            }

            seatByClient.Add(clientId, seat);
            Debug.Log($"Assigned client {clientId} to {seat}.");
        }

        private bool HasSeat(SeatId seat)
        {
            foreach (SeatId assignedSeat in seatByClient.Values)
            {
                if (assignedSeat == seat)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Snapshot Requests

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestSnapshotRpc(RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            Debug.Log($"Snapshot request received from client {senderClientId}.");

            AssignClient(senderClientId);
            bool matchCreated = TryInitializeMatch();

            if (matchCreated)
            {
                BroadcastUpdate();
                return;
            }

            if (!seatByClient.TryGetValue(senderClientId, out SeatId viewerSeat))
            {
                Debug.LogWarning($"No seat found for client {senderClientId}.");
                return;
            }

            SendUpdate(senderClientId, viewerSeat, null);
            ResendSettlement(senderClientId, viewerSeat);
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

            if (!seatByClient.TryGetValue(clientId, out SeatId viewerSeat))
            {
                Debug.LogWarning($"Cannot send snapshot: client {clientId} has no seat.");
                yield break;
            }

            Debug.Log($"NetworkMatchBridge is now visible to client {clientId}.");

            SendUpdate(clientId, viewerSeat, null);
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
