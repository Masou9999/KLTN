using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed partial class MatchRulesEngine
    {
        #region Block Declaration and Passing

        /// <summary>
        /// Atomically validates blocker ownership, slot alignment and combat keywords,
        /// then resolves combat using the authoritative board state.
        /// </summary>
        public CommandResult TryDeclareBlock(
            MatchState state,
            SeatId actor,
            ulong[] blockerIdsBySlot
        )
        {
            CommandResult actorValidation = ValidateBlockActor(state, actor);

            if (!actorValidation.Accepted)
            {
                return actorValidation;
            }

            if (
                blockerIdsBySlot == null
                || blockerIdsBySlot.Length != MatchState.BoardSlotCount
            )
            {
                return CommandResult.Reject(
                    CommandRejectionReason.InvalidBlockDeclaration
                );
            }

            PlayerState attacker = state.Player(state.AttackTokenOwner);

            PlayerState defender = state.Player(actor);

            if (defender.Board.Count > 0)
            {
                return CommandResult.Reject(CommandRejectionReason.BoardNotEmpty);
            }

            var selectedBlockers = new CardInstance[MatchState.BoardSlotCount];

            var uniqueIds = new HashSet<ulong>();

            int blockerCount = 0;

            for (int slotIndex = 0; slotIndex < blockerIdsBySlot.Length; slotIndex++)
            {
                ulong blockerId = blockerIdsBySlot[slotIndex];

                if (blockerId == 0)
                {
                    continue;
                }

                if (attacker.FindBoardCard(slotIndex) == null)
                {
                    return CommandResult.Reject(
                        CommandRejectionReason.BlockerHasNoAttacker
                    );
                }

                if (!uniqueIds.Add(blockerId))
                {
                    return CommandResult.Reject(
                        CommandRejectionReason.InvalidBlockDeclaration
                    );
                }

                CardInstance blocker = defender.FindCardInReserve(blockerId);

                if (blocker == null || blocker.IsDead)
                {
                    return CommandResult.Reject(CommandRejectionReason.CardNotInReserve);
                }

                CardInstance attackingCard = attacker.FindBoardCard(slotIndex);

                CommandRejectionReason keywordRejection = ValidateBlockKeywords(
                    attackingCard,
                    blocker
                );

                if (keywordRejection != CommandRejectionReason.None)
                {
                    return CommandResult.Reject(keywordRejection);
                }

                selectedBlockers[slotIndex] = blocker;
                blockerCount++;
            }

            for (int slotIndex = 0; slotIndex < selectedBlockers.Length; slotIndex++)
            {
                CardInstance blocker = selectedBlockers[slotIndex];

                if (blocker == null)
                {
                    continue;
                }

                bool moved = defender.TryMoveReserveCardToBoard(blocker, slotIndex);

                if (!moved)
                {
                    throw new InvalidOperationException(
                        "Validated block declaration could not be applied."
                    );
                }
            }

            state.ConsecutivePasses = 0;
            state.Phase = MatchPhase.CombatResolution;

            // In this game's Support variant, adjacent blockers support each other
            // before damage is calculated, just as adjacent attackers do.
            EnqueueAndResolveAbilities(
                state,
                BuildDeclarationEventBatch(
                    state.RoundNumber,
                    selectedBlockers,
                    includeAttackEvents: false
                )
            );

            UpdateOutcome(state);

            if (state.IsFinished)
            {
                state.Phase = MatchPhase.Finished;
                return CommandResult.Success();
            }

            GameEventBatch deathEventBatch;

            RoundResolution resolution = ResolveCombat(
                state,
                state.AttackTokenOwner,
                out deathEventBatch
            );

            // Combat damage and its death triggers form one resolution chain.
            // While-on-board buffs (Support) end as survivors leave the board.
            ClearBoardModifiers(state.Host);
            ClearBoardModifiers(state.Guest);
            state.Host.ReturnBoardCardsToReserve();
            state.Guest.ReturnBoardCardsToReserve();

            state.ActiveSeat = actor;
            state.Phase = MatchPhase.Priority;

            EnqueueAndResolveAbilities(state, deathEventBatch);

            UpdateOutcome(state);

            if (state.IsFinished)
            {
                state.Phase = MatchPhase.Finished;

                state.LastEvent =
                    $"Combat và các skill đã kết thúc. " + $"Kết quả: {state.Outcome}.";

                return CommandResult.Success(resolution);
            }

            if (state.PendingSelection == null)
            {
                state.LastEvent =
                    $"Combat đã kết thúc với {blockerCount} blocker. "
                    + $"{actor} nhận priority sau combat.";
            }

            return CommandResult.Success(resolution);
        }

        /// <summary>
        /// Passes priority. Two consecutive passes end the round; a pass following an
        /// opponent's pass is exposed to the client as the End Round action.
        /// </summary>
        public CommandResult TryPass(MatchState state, SeatId actor)
        {
            CommandResult actorValidation = ValidatePriorityActor(state, actor);

            if (!actorValidation.Accepted)
            {
                return actorValidation;
            }

            state.ConsecutivePasses++;

            if (state.ConsecutivePasses < 2)
            {
                state.ActiveSeat = actor.Opponent();

                state.LastEvent =
                    $"{actor} đã pass. "
                    + $"{state.ActiveSeat} có thể hành động "
                    + $"hoặc kết thúc vòng.";

                return CommandResult.Success();
            }

            RoundTransition roundTransition = CompleteRound(state);

            return CommandResult.Success(roundTransition: roundTransition);
        }

        #endregion

        private void ClearBoardModifiers(PlayerState player)
        {
            foreach (CardInstance card in player.Board)
            {
                if (!card.IsDead
                    && definitionsById.TryGetValue(card.DefinitionId, out CardDefinition definition))
                {
                    card.ClearBoardModifiers(definition);
                }
            }
        }
    }
}
