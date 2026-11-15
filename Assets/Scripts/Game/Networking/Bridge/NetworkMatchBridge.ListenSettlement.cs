using System;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Listen Registration

        private sealed class ListenRegistration
        {
            public string HostPlayerId;
            public bool HostReady;
            public readonly TaskCompletionSource<string> Guest =
                new TaskCompletionSource<string>();
            public Task Ready;
        }

        private string joiningListenMatch;
        private CancellationTokenSource listenLifetime;

        private void BeginListenRegistration(MatchSession session)
        {
            if (MatchServerContext.IsDedicated || !session.IsRanked) return;
            var registration = new ListenRegistration();
            RuntimeOf(session).Listen = registration;
            registration.Ready = RegisterListenHostAsync(session, registration);
        }

        private async Task RegisterListenHostAsync(
            MatchSession session, ListenRegistration registration)
        {
            CancellationToken cancellationToken = listenLifetime.Token;
            try
            {
                registration.HostPlayerId = ListenMatchClient.PlayerId;
                await ListenMatchClient.RegisterAsync(
                    session.MatchId, registration.HostPlayerId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (this == null || !IsSpawned) return;
                registration.HostReady = true;
                InviteListenGuest(session);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // Keep failure observable by settlement without an unobserved faulted task.
                registration.Guest.TrySetResult(null);
                Debug.LogWarning("[Settlement] Could not register listen host.");
            }
        }

        private void InviteListenGuest(MatchSession session)
        {
            ListenRegistration registration = RuntimeOf(session).Listen;
            if (registration == null || !registration.HostReady || registration.Guest.Task.IsCompleted)
                return;
            foreach (ulong clientId in session.ConnectionsOf(SeatId.Guest))
            {
                if (NetworkObject.IsNetworkVisibleTo(clientId))
                    RegisterListenGuestRpc(session.MatchId, registration.HostPlayerId,
                        RpcTarget.Single(clientId, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void RegisterListenGuestRpc(
            string matchId, string hostPlayerId, RpcParams rpcParams = default)
        {
            if (joiningListenMatch == matchId) return;
            joiningListenMatch = matchId;
            _ = RegisterListenGuestAsync(matchId, hostPlayerId);
        }

        private async Task RegisterListenGuestAsync(string matchId, string hostPlayerId)
        {
            CancellationToken cancellationToken = listenLifetime.Token;
            string playerId = null;
            try
            {
                playerId = await ListenMatchClient.RegisterAsync(
                    matchId, hostPlayerId, cancellationToken);
            }
            catch (Exception)
            {
                Debug.LogWarning("[Settlement] Could not register listen guest.");
            }
            if (!cancellationToken.IsCancellationRequested && this != null && IsSpawned)
                ConfirmListenRegistrationRpc(matchId, playerId ?? string.Empty);
        }

        /// <summary>Completes only the guest seat's registration for its own match.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ConfirmListenRegistrationRpc(
            string matchId, string playerId, RpcParams rpcParams = default)
        {
            if (MatchServerContext.IsDedicated
                || !TryGetSeatedClient(rpcParams.Receive.SenderClientId, out MatchSession session, out SeatId seat)
                || seat != SeatId.Guest || session.MatchId != matchId) return;
            RuntimeOf(session).Listen?.Guest.TrySetResult(playerId);
        }

        #endregion

        #region Listen Settlement

        private async Task SettleListenAsync(MatchSession session)
        {
            CancellationToken cancellationToken = listenLifetime.Token;
            try
            {
                ListenRegistration registration = RuntimeOf(session).Listen;
                if (registration == null) throw new InvalidOperationException();
                await registration.Ready;
                cancellationToken.ThrowIfCancellationRequested();
                InviteListenGuest(session);
                if (!registration.HostReady) throw new InvalidOperationException();
                await Task.WhenAny(registration.Guest.Task, Task.Delay(55000, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                // The guest may disconnect after backend registration but before its ACK.
                // The backend roster remains authoritative even when that ACK was lost.
                // A closed host never initiates settlement; there is no backend forfeit timer.
                if (this == null || !IsSpawned || !IsServer) return;
                MatchSettlementResult result = await ListenMatchClient.ReportAsync(
                    session.MatchId, session.State.Outcome,
                    session.State.SurrenderedSeat, session.State.RoundNumber, cancellationToken);
                if (this == null || !IsSpawned || !IsServer) return;
                if (!result.Success)
                {
                    PublishSettlementToAll(session, MatchSettlementState.Failed, result.Message);
                    return;
                }
                PublishSeat(session, SeatId.Host, Build(session.MatchId, result.For(registration.HostPlayerId)));
                PlayerSettlement guest = null;
                foreach (PlayerSettlement player in result.Players)
                {
                    if (player.PlayerId != registration.HostPlayerId) guest = player;
                }
                PublishSeat(session, SeatId.Guest, Build(session.MatchId, guest));
            }
            catch (Exception)
            {
                if (!cancellationToken.IsCancellationRequested && this != null && IsSpawned && IsServer)
                    PublishSettlementToAll(session, MatchSettlementState.Failed,
                        "Không thể xác thực hoặc lưu kết quả trận. Vui lòng kiểm tra kết nối.");
            }
        }

        #endregion
    }
}
