using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed partial class MatchRulesEngine
    {
        /// <summary>Clears only the actor's inactivity streak after a completed manual decision.</summary>
        public void RecordTimelyDecision(MatchState state, SeatId actor)
        {
            state.Player(actor).ConsecutiveTimeouts = 0;
        }

        /// <summary>
        /// Server-only expiry intent. The second consecutive timeout forfeits BEFORE the
        /// fallback action; round transitions never clear a player's timeout streak.
        /// </summary>
        public CommandResult TryTimeout(MatchState state, SeatId actor)
        {
            if (state == null) return CommandResult.Reject(CommandRejectionReason.MatchNotReady);
            if (state.IsFinished) return CommandResult.Reject(CommandRejectionReason.MatchFinished);
            if (actor != SeatId.Host && actor != SeatId.Guest)
                return CommandResult.Reject(CommandRejectionReason.InvalidSeat);
            if (state.Phase == MatchPhase.Mulligan)
                return TryMulligan(state, actor, Array.Empty<ulong>());

            PendingAbilitySelection pending = state.PendingSelection;
            if (state.Phase == MatchPhase.AbilitySelection)
            {
                if (pending == null || pending.ChoosingSeat != actor)
                    return CommandResult.Reject(CommandRejectionReason.NotAbilitySelector);
            }
            else
            {
                CommandResult validation = state.Phase == MatchPhase.BlockDeclaration
                    ? ValidateBlockActor(state, actor) : ValidatePriorityActor(state, actor);
                if (!validation.Accepted) return validation;
            }

            int count = ++state.Player(actor).ConsecutiveTimeouts;
            state.Journal.Add(state.RoundNumber, MatchLogCategory.Combat,
                viewer => $"{MatchJournal.Owner(actor, viewer)} hết thời gian ({count}/2 lượt liên tiếp).");
            if (count >= 2)
            {
                if (pending != null) state.TryClosePendingSelection(pending.RequestId);
                triggeredAbilityQueue.Clear();
                // Preserve the existing early-forfeit reward policy, but expose the real reason.
                CommandResult forfeited = TrySurrender(state, actor);
                state.TimedOutSeat = actor;
                state.LastEvent = $"{actor} hết giờ 2 lượt liên tiếp. {actor.Opponent()} chiến thắng.";
                return forfeited;
            }

            if (pending != null)
            {
                if (pending.CanCancel)
                {
                    CommandResult cancelled = TryCancelAbilitySelection(state, actor, pending.RequestId);
                    return cancelled.Accepted ? TryPass(state, actor) : cancelled;
                }

                AbilityTargetSelection selection = FindTimeoutSelection(state, pending);
                if (selection != null)
                    return TrySubmitAbilitySelection(state, actor, pending.RequestId, selection);

                // No joint legal selection (e.g. overlapping target requirements): skip the
                // impossible queued effect, just as normal resolution skips missing targets.
                state.TryClosePendingSelection(pending.RequestId);
                triggeredAbilityQueue.TryDequeue(out _);
                ResolveQueuedAbilities(state, 0, null, null);
                return CommandResult.Success(roundTransition: ContinueAutomaticPhase(state));
            }

            return state.Phase == MatchPhase.BlockDeclaration
                ? TryDeclareBlock(state, actor, new ulong[MatchState.BoardSlotCount])
                : TryPass(state, actor);
        }

        private AbilityTargetSelection FindTimeoutSelection(MatchState state, PendingAbilitySelection pending)
        {
            var slots = new List<AbilityTargetSlot>();
            var candidates = new List<IReadOnlyList<CardInstance>>();
            foreach (AbilityTargetRequirement requirement in pending.Ability.RequiredTargets)
            {
                var valid = abilityTargetValidator.GetValidTargets(state, pending.ChoosingSeat,
                    pending.SourceCardInstanceId, requirement);
                for (int i = 0; i < requirement.Count; i++)
                {
                    slots.Add(requirement.Slot);
                    candidates.Add(valid);
                }
            }
            var primary = new List<ulong>();
            var secondary = new List<ulong>();
            var used = new HashSet<ulong>();
            AbilityTargetSelection found = null;
            bool Search(int index)
            {
                if (index == slots.Count)
                {
                    var selection = new AbilityTargetSelection(primary, secondary);
                    if (!abilityTargetValidator.Validate(state, pending, selection)) return false;
                    found = selection;
                    return true;
                }
                List<ulong> target = slots[index] == AbilityTargetSlot.Primary ? primary : secondary;
                foreach (CardInstance card in candidates[index])
                {
                    if (!used.Add(card.InstanceId)) continue;
                    target.Add(card.InstanceId);
                    if (Search(index + 1)) return true;
                    target.RemoveAt(target.Count - 1);
                    used.Remove(card.InstanceId);
                }
                return false;
            }
            Search(0);
            return found;
        }

        /// <summary>Validates and commits one seat's mulligan, including the timeout keep-hand fallback.</summary>
        public CommandResult TryMulligan(MatchState state, SeatId actor, ulong[] cardIdsToReplace)
        {
            if (state == null) return CommandResult.Reject(CommandRejectionReason.MatchNotReady);
            if (state.IsFinished) return CommandResult.Reject(CommandRejectionReason.MatchFinished);
            if (actor != SeatId.Host && actor != SeatId.Guest)
                return CommandResult.Reject(CommandRejectionReason.InvalidSeat);
            if (state.Phase != MatchPhase.Mulligan || state.RoundNumber != 0
                || (actor == SeatId.Host ? state.HostMulliganDone : state.GuestMulliganDone))
                return CommandResult.Reject(CommandRejectionReason.InvalidPhase);
            var unique = new HashSet<ulong>();
            PlayerState player = state.Player(actor);
            foreach (ulong id in cardIdsToReplace ?? Array.Empty<ulong>())
                if (!unique.Add(id) || !player.DrawHand.Exists(card => card.InstanceId == id))
                    return CommandResult.Reject(CommandRejectionReason.CardNotInHand);
            player.PerformMulligan(cardIdsToReplace, new Random());
            if (actor == SeatId.Host) state.HostMulliganDone = true;
            else state.GuestMulliganDone = true;
            if (state.IsMulliganPhaseComplete)
            {
                state.BeginFirstRound();
                state.LastEvent = "Giai đoạn đổi bài kết thúc. Trận đấu bắt đầu!";
            }
            return CommandResult.Success();
        }
    }
}
