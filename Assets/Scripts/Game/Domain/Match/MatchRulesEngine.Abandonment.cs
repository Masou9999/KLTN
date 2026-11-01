namespace KLTN.Game.Domain
{
    public sealed partial class MatchRulesEngine
    {
        /// <summary>
        /// Ends the match when a disconnected player did not return in time (FR-NET-06).
        /// The absent seat loses by the surrender rule, so rewards and points follow the
        /// same path as a normal surrender.
        /// </summary>
        public CommandResult TryForfeitAbandonedSeat(MatchState state, SeatId absentSeat)
        {
            CommandResult result = TrySurrender(state, absentSeat);

            if (result.Accepted)
            {
                state.LastEvent =
                    $"{absentSeat} mất kết nối quá lâu. {absentSeat.Opponent()} chiến thắng.";
            }

            return result;
        }

        /// <summary>Ends the match as a draw when both players stayed away past the grace period.</summary>
        public CommandResult TryDrawAbandonedMatch(MatchState state)
        {
            if (state == null)
            {
                return CommandResult.Reject(CommandRejectionReason.MatchNotReady);
            }

            if (state.IsFinished)
            {
                return CommandResult.Reject(CommandRejectionReason.MatchFinished);
            }

            state.Outcome = MatchOutcome.Draw;
            state.SurrenderedSeat = null;
            state.Phase = MatchPhase.Finished;
            state.ConsecutivePasses = 0;
            state.LastEvent = "Cả hai người chơi mất kết nối. Trận đấu hòa.";

            return CommandResult.Success();
        }
    }
}
