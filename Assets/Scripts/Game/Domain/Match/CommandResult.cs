namespace KLTN.Game.Domain
{
    public enum CommandRejectionReason : byte
    {
        None,
        MatchFinished,
        NotYourTurn,
        CardNotInHand,
        InsufficientMana,
        InvalidBoardSlot,
        BoardSlotOccupied,
        BoardFull,
        MissingCardDefinition,
        MatchNotReady,
        InvalidSeat,
        DuplicateCommand,
        InvalidPhase,
        ActiveRosterFull,
        CardNotInReserve,
        NotAttackTokenOwner,
        AttackTokenUnavailable,
        InvalidAttackDeclaration,
        BoardNotEmpty,
        InvalidBlockDeclaration,
        NotDefendingPlayer,
        BlockerHasNoAttacker,
        NoPendingAbilitySelection,
        InvalidAbilitySelectionRequest,
        NotAbilitySelector,
        InvalidAbilityTarget,
        NoValidAbilityTargets,
        AbilitySelectionCannotBeCancelled,
        CardCannotBlock,
        FearsomeBlockerTooWeak,

        // A player is disconnected and the server is holding their seat (FR-NET-06).
        MatchPaused,
    }

    public sealed class CommandResult
    {
        #region Properties

        public bool Accepted { get; }
        public CommandRejectionReason RejectionReason { get; }
        public RoundResolution Resolution { get; }
        public RoundTransition RoundTransition { get; }

        #endregion

        #region Construction

        private CommandResult(
            bool accepted,
            CommandRejectionReason rejectionReason,
            RoundResolution resolution,
            RoundTransition roundTransition
        )
        {
            Accepted = accepted;
            RejectionReason = rejectionReason;
            Resolution = resolution;
            RoundTransition = roundTransition;
        }

        #endregion

        #region Factory Methods

        public static CommandResult Success(
            RoundResolution resolution = null,
            RoundTransition roundTransition = null
        )
        {
            return new CommandResult(
                true,
                CommandRejectionReason.None,
                resolution,
                roundTransition
            );
        }

        public static CommandResult Reject(CommandRejectionReason reason)
        {
            return new CommandResult(false, reason, null, null);
        }

        #endregion
    }
}
