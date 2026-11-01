using System;
using KLTN.Game.Domain;

namespace KLTN.Game.ApplicationLayer
{
    public enum SeatConnectionState : byte
    {
        Empty = 0,
        Connected,
        Disconnected,
        Abandoned,
    }

    public enum ReconnectResult : byte
    {
        Reconnected = 0,
        UnknownPlayer,
        AlreadyConnected,
        Expired,
    }

    public enum GraceExpiryKind : byte
    {
        None = 0,
        Forfeit,
        Draw,
    }

    /// <summary>What the server must do once a held seat runs out of time.</summary>
    public readonly struct GraceExpiryDecision
    {
        public static readonly GraceExpiryDecision None = new GraceExpiryDecision(GraceExpiryKind.None, default);

        public GraceExpiryDecision(GraceExpiryKind kind, SeatId forfeitingSeat)
        {
            Kind = kind;
            ForfeitingSeat = forfeitingSeat;
        }

        public GraceExpiryKind Kind { get; }

        /// <summary>Seat that loses by the surrender rule; only meaningful for Forfeit.</summary>
        public SeatId ForfeitingSeat { get; }
    }

    /// <summary>
    /// Holds each seat for a grace period after its player drops (FR-NET-06/07, FR-MM-08).
    /// Seats are bound to a stable player key (the UGS PlayerId on a dedicated server), not
    /// to the transport client ID, so a returning player gets the same seat on a new
    /// connection. Time is passed in as Unix milliseconds to keep the class testable.
    /// </summary>
    public sealed class SeatConnectionTracker
    {
        #region Constants

        public const long DefaultGracePeriodMilliseconds = 30000;

        #endregion

        #region State

        private readonly SeatRecord host = new SeatRecord();
        private readonly SeatRecord guest = new SeatRecord();

        #endregion

        #region Construction

        public SeatConnectionTracker(long gracePeriodMilliseconds = DefaultGracePeriodMilliseconds)
        {
            if (gracePeriodMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gracePeriodMilliseconds));
            }

            GracePeriodMilliseconds = gracePeriodMilliseconds;
        }

        #endregion

        #region Properties

        public long GracePeriodMilliseconds { get; }

        /// <summary>True once an expiry decision was handed out; the seats are then final.</summary>
        public bool IsResolved { get; private set; }

        /// <summary>A bound seat is not connected, so the match must not accept commands.</summary>
        public bool IsPaused =>
            !IsResolved
            && (IsHeldOrLost(host) || IsHeldOrLost(guest))
            && host.State != SeatConnectionState.Empty
            && guest.State != SeatConnectionState.Empty;

        /// <summary>Some seat is still waiting for its player to come back.</summary>
        public bool IsHoldingSeats =>
            !IsResolved
            && (host.State == SeatConnectionState.Disconnected || guest.State == SeatConnectionState.Disconnected);

        #endregion

        #region Binding

        public void Bind(SeatId seat, string playerKey)
        {
            if (string.IsNullOrWhiteSpace(playerKey))
            {
                throw new ArgumentException("Player key is required.", nameof(playerKey));
            }

            SeatRecord record = Record(seat);
            record.PlayerKey = playerKey;
            record.State = SeatConnectionState.Connected;
            record.DeadlineMs = 0;
        }

        public bool IsBound(SeatId seat)
        {
            return Record(seat).State != SeatConnectionState.Empty;
        }

        public string PlayerKeyOf(SeatId seat)
        {
            return Record(seat).PlayerKey;
        }

        public SeatConnectionState StateOf(SeatId seat)
        {
            return Record(seat).State;
        }

        /// <summary>Deadline (Unix ms) of a disconnected seat; 0 when it is not being held.</summary>
        public long DeadlineOf(SeatId seat)
        {
            SeatRecord record = Record(seat);
            return record.State == SeatConnectionState.Disconnected ? record.DeadlineMs : 0;
        }

        public bool TryFindSeat(string playerKey, out SeatId seat)
        {
            if (!string.IsNullOrEmpty(playerKey))
            {
                if (string.Equals(host.PlayerKey, playerKey, StringComparison.Ordinal))
                {
                    seat = SeatId.Host;
                    return true;
                }

                if (string.Equals(guest.PlayerKey, playerKey, StringComparison.Ordinal))
                {
                    seat = SeatId.Guest;
                    return true;
                }
            }

            seat = default;
            return false;
        }

        #endregion

        #region Connection Changes

        /// <summary>Starts holding the seat. Returns false when it was not connected.</summary>
        public bool MarkDisconnected(SeatId seat, long nowMs)
        {
            SeatRecord record = Record(seat);

            if (IsResolved || record.State != SeatConnectionState.Connected)
            {
                return false;
            }

            record.State = SeatConnectionState.Disconnected;
            record.DeadlineMs = nowMs + GracePeriodMilliseconds;
            return true;
        }

        /// <summary>Whether this player could take back a held seat right now.</summary>
        public bool CanReconnect(string playerKey, long nowMs)
        {
            return !IsResolved
                && TryFindSeat(playerKey, out SeatId seat)
                && Record(seat).State == SeatConnectionState.Disconnected
                && nowMs < Record(seat).DeadlineMs;
        }

        public ReconnectResult TryReconnect(string playerKey, long nowMs, out SeatId seat)
        {
            if (!TryFindSeat(playerKey, out seat))
            {
                return ReconnectResult.UnknownPlayer;
            }

            SeatRecord record = Record(seat);

            switch (record.State)
            {
                case SeatConnectionState.Connected:
                    return ReconnectResult.AlreadyConnected;
                case SeatConnectionState.Disconnected when !IsResolved && nowMs < record.DeadlineMs:
                    record.State = SeatConnectionState.Connected;
                    record.DeadlineMs = 0;
                    return ReconnectResult.Reconnected;
                default:
                    return ReconnectResult.Expired;
            }
        }

        #endregion

        #region Expiry

        /// <summary>
        /// Marks seats whose deadline passed as abandoned and decides the outcome (HLD 10.1):
        /// an abandoned seat facing a connected opponent forfeits; two abandoned seats draw.
        /// While the other seat is still inside its own grace period nothing is decided yet.
        /// The decision is returned once; afterwards the tracker is resolved.
        /// </summary>
        public GraceExpiryDecision Evaluate(long nowMs)
        {
            if (IsResolved)
            {
                return GraceExpiryDecision.None;
            }

            Expire(host, nowMs);
            Expire(guest, nowMs);

            GraceExpiryDecision decision;

            if (host.State == SeatConnectionState.Abandoned && guest.State == SeatConnectionState.Abandoned)
            {
                decision = new GraceExpiryDecision(GraceExpiryKind.Draw, default);
            }
            else if (host.State == SeatConnectionState.Abandoned && guest.State == SeatConnectionState.Connected)
            {
                decision = new GraceExpiryDecision(GraceExpiryKind.Forfeit, SeatId.Host);
            }
            else if (guest.State == SeatConnectionState.Abandoned && host.State == SeatConnectionState.Connected)
            {
                decision = new GraceExpiryDecision(GraceExpiryKind.Forfeit, SeatId.Guest);
            }
            else
            {
                return GraceExpiryDecision.None;
            }

            IsResolved = true;
            return decision;
        }

        /// <summary>Freezes the seats, e.g. when the match ended normally.</summary>
        public void Resolve()
        {
            IsResolved = true;
        }

        public void Reset()
        {
            host.Clear();
            guest.Clear();
            IsResolved = false;
        }

        #endregion

        #region Helpers

        private static void Expire(SeatRecord record, long nowMs)
        {
            if (record.State == SeatConnectionState.Disconnected && nowMs >= record.DeadlineMs)
            {
                record.State = SeatConnectionState.Abandoned;
            }
        }

        private static bool IsHeldOrLost(SeatRecord record)
        {
            return record.State == SeatConnectionState.Disconnected || record.State == SeatConnectionState.Abandoned;
        }

        private SeatRecord Record(SeatId seat)
        {
            switch (seat)
            {
                case SeatId.Host:
                    return host;
                case SeatId.Guest:
                    return guest;
                default:
                    throw new ArgumentOutOfRangeException(nameof(seat), seat, "Unknown seat.");
            }
        }

        private sealed class SeatRecord
        {
            public string PlayerKey;
            public SeatConnectionState State;
            public long DeadlineMs;

            public void Clear()
            {
                PlayerKey = null;
                State = SeatConnectionState.Empty;
                DeadlineMs = 0;
            }
        }

        #endregion
    }
}
