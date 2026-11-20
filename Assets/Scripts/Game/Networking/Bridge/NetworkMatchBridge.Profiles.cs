using System;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    public sealed partial class NetworkMatchBridge
    {
        #region Public Profiles

        /// <summary>Caches by match/seat, retaining identity through a disconnect.</summary>
        private void ApplyPublicProfile(
            MatchSession session, SeatId seat, PlayerViewDto player)
        {
            if (player == null) return;
            MatchRuntime runtime = RuntimeOf(session);
            if (MatchServerContext.IsDedicated && !runtime.Profiles.ContainsKey(seat))
            {
                foreach (ulong clientId in session.ConnectionsOf(seat))
                {
                    if (!MatchServerContext.TryGetAdmission(clientId, out var admission))
                        continue;
                    // Old backends can provide a name, but must not invent season points.
                    runtime.Profiles[seat] = admission.Profile ?? new PlayerProfileDto
                    {
                        username = admission.Username,
                    };
                    break;
                }
            }
            if (!runtime.Profiles.TryGetValue(seat, out var profile)) return;
            player.profile = profile;
            if (!string.IsNullOrWhiteSpace(profile.username))
                player.displayName = profile.username;
        }

        /// <summary>
        /// Resolves identities from the authenticated backend roster, never from ACK payloads.
        /// A lost guest ACK still permits lookup; lifetime guards reject late responses.
        /// </summary>
        private async Task LoadListenProfilesAsync(
            MatchSession session, ListenRegistration registration,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.WhenAny(registration.Guest.Task,
                    Task.Delay(55000, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                var response = await ListenMatchClient.GetProfilesAsync(
                    session.MatchId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (this == null || !IsSpawned || !IsServer || session.IsClosed
                    || !sessions.TryGet(session.MatchId, out var current)
                    || !ReferenceEquals(current, session)) return;
                var host = response?["host"]?.ToObject<PlayerProfileDto>();
                var guest = response?["guest"]?.ToObject<PlayerProfileDto>();
                if (host == null || guest == null) return;
                var runtime = RuntimeOf(session);
                runtime.Profiles[SeatId.Host] = host;
                runtime.Profiles[SeatId.Guest] = guest;
                BroadcastUpdate(session);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!cancellationToken.IsCancellationRequested && this != null)
                    Debug.LogWarning("[Profile] Opponent profile is unavailable.");
            }
        }

        #endregion
    }
}
