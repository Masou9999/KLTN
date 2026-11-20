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
            if (MatchServerContext.IsDedicated) return;
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
                    session.MatchId, registration.HostPlayerId, cancellationToken, session.IsRanked);
                cancellationToken.ThrowIfCancellationRequested();
                if (this == null || !IsSpawned) return;
                registration.HostReady = true;
                InviteListenGuest(session);
                _ = LoadListenProfilesAsync(session, registration, cancellationToken);
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
                    RegisterListenGuestRpc(session.MatchId, registration.HostPlayerId, session.IsRanked,
                        RpcTarget.Single(clientId, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void RegisterListenGuestRpc(
            string matchId, string hostPlayerId, bool ranked, RpcParams rpcParams = default)
        {
            if (joiningListenMatch == matchId) return;
            joiningListenMatch = matchId;
            _ = RegisterListenGuestAsync(matchId, hostPlayerId, ranked);
        }

        private async Task RegisterListenGuestAsync(string matchId, string hostPlayerId, bool ranked)
        {
            CancellationToken cancellationToken = listenLifetime.Token;
            string playerId = null;
            try
            {
                playerId = await ListenMatchClient.RegisterAsync(
                    matchId, hostPlayerId, cancellationToken, ranked);
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

        private static CancellationTokenSource applicationLifetime;

        /// <summary>
        /// Cancelled only when the application quits. A finished listen match keeps
        /// reporting after the host leaves the scene (the result skip button), because
        /// only the registered host can settle it and nobody else would retry.
        /// </summary>
        private static CancellationToken ApplicationLifetime
        {
            get
            {
                if (applicationLifetime == null)
                {
                    applicationLifetime = new CancellationTokenSource();
                    Application.quitting += applicationLifetime.Cancel;
                }
                return applicationLifetime.Token;
            }
        }

        /// <summary>
        /// Reports a finished listen match to Cloud Code and forwards each seat's result.
        /// The report itself survives bridge despawn; only publication needs the bridge.
        /// Retries reuse the match ID, so a lost response cannot pay twice.
        /// </summary>
        private async Task SettleListenAsync(MatchSession session)
        {
            CancellationToken cancellationToken = ApplicationLifetime;
            // Captured now: the session may be torn down while the report is in flight.
            string matchId = session.MatchId;
            MatchOutcome outcome = session.State.Outcome;
            SeatId? surrenderedSeat = session.State.SurrenderedSeat;
            int roundNumber = session.State.RoundNumber;
            try
            {
                ListenRegistration registration = RuntimeOf(session).Listen;
                if (registration == null) throw new InvalidOperationException();
                await registration.Ready;
                if (IsBridgeAlive()) InviteListenGuest(session);
                if (!registration.HostReady) throw new InvalidOperationException();
                // The guest may disconnect after backend registration but before its ACK.
                // The backend roster remains authoritative even when that ACK was lost.
                await Task.WhenAny(registration.Guest.Task,
                    Task.Delay(55000, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                MatchSettlementResult result = await ListenMatchClient.ReportAsync(
                    matchId, outcome, surrenderedSeat, roundNumber, cancellationToken);
                if (!IsBridgeAlive())
                {
                    if (result.Success) await RefreshLocalStandingAsync();
                    return;
                }
                if (!result.Success)
                {
                    PublishSettlementToAll(session, MatchSettlementState.Failed, result.Message);
                    return;
                }
                PublishSeat(session, SeatId.Host,
                    Build(matchId, result.For(registration.HostPlayerId)));
                PlayerSettlement guest = null;
                foreach (PlayerSettlement player in result.Players)
                {
                    if (player.PlayerId != registration.HostPlayerId) guest = player;
                }
                PublishSeat(session, SeatId.Guest, Build(matchId, guest));
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (IsBridgeAlive())
                    PublishSettlementToAll(session, MatchSettlementState.Failed,
                        "Không thể xác thực hoặc lưu kết quả trận. Vui lòng kiểm tra kết nối.");
                else
                    Debug.LogWarning(
                        $"[Settlement] Detached listen report failed: {exception.Message}");
            }
        }

        private bool IsBridgeAlive()
        {
            return this != null && IsSpawned && IsServer;
        }

        /// <summary>
        /// Re-reads the wallet and season row after a report that finished once the
        /// host had already left the match, so the main menu shows the new values.
        /// </summary>
        private static async Task RefreshLocalStandingAsync()
        {
            try
            {
                var profiles = KLTN.Infrastructure.Economy.PlayerProfileService.Instance;
                if (profiles != null) await profiles.EnsureLoadedAsync(forceReload: true);
                var leaderboard = KLTN.Infrastructure.Ranking.LeaderboardService.Instance;
                if (leaderboard != null) await leaderboard.GetMyCurrentTierAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Settlement] Could not refresh standing: {exception.Message}");
            }
        }

        #endregion
    }
}
