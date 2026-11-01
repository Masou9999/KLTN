using System;
using KLTN.Game.Domain;

namespace KLTN.Game.ApplicationLayer
{
    /// <summary>Outcome of one routed command: the match and seat it ran in, and the domain result.</summary>
    public readonly struct MatchCommandOutcome
    {
        public MatchCommandOutcome(MatchSession session, SeatId seat, CommandResult result)
        {
            Session = session;
            Seat = seat;
            Result = result;
        }

        /// <summary>Null when the connection has no match.</summary>
        public MatchSession Session { get; }

        public SeatId Seat { get; }

        public CommandResult Result { get; }

        public bool Accepted => Result != null && Result.Accepted;
    }

    /// <summary>
    /// Routes a player command from its connection to the right match aggregate (SDS HLD 10.2)
    /// and applies the checks shared by every command: seat, match started, not paused
    /// (FR-NET-06) and a growing command ID. A command can only ever touch its own match.
    /// </summary>
    public sealed class MatchCommandService
    {
        private readonly MatchSessionRegistry registry;

        public MatchCommandService(MatchSessionRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <param name="command">Runs against the caller's own match and seat.</param>
        public MatchCommandOutcome Execute(
            ulong connectionId,
            ulong commandId,
            Func<MatchSession, SeatId, CommandResult> command
        )
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (!registry.TryGetByConnection(connectionId, out MatchSession session)
                || !session.TryGetSeat(connectionId, out SeatId seat))
            {
                return new MatchCommandOutcome(session, default, CommandResult.Reject(CommandRejectionReason.InvalidSeat));
            }

            if (!session.IsStarted)
            {
                return new MatchCommandOutcome(session, seat, CommandResult.Reject(CommandRejectionReason.MatchNotReady));
            }

            if (session.IsPaused)
            {
                return new MatchCommandOutcome(session, seat, CommandResult.Reject(CommandRejectionReason.MatchPaused));
            }

            if (!session.TryAcceptCommandId(connectionId, commandId))
            {
                return new MatchCommandOutcome(session, seat, CommandResult.Reject(CommandRejectionReason.DuplicateCommand));
            }

            CommandResult result = command(session, seat)
                ?? CommandResult.Reject(CommandRejectionReason.MatchNotReady);
            return new MatchCommandOutcome(session, seat, result);
        }
    }
}
