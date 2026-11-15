using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed class UnitPassiveRules
    {
        #region Defaults and Properties

        public static UnitPassiveRules None { get; } = new UnitPassiveRules();

        public int CostReductionPerAlliedDeathThisGame { get; }

        public bool RevivesAtRoundStart { get; }

        public int PowerAndHealthPerDeath { get; }

        public bool JoinsAttackFromGraveyard { get; }

        public bool RequiresEphemeralAttacker { get; }

        #endregion

        #region Construction

        public UnitPassiveRules(
            int costReductionPerAlliedDeathThisGame = 0,
            bool revivesAtRoundStart = false,
            int powerAndHealthPerDeath = 0,
            bool joinsAttackFromGraveyard = false,
            bool requiresEphemeralAttacker = false
        )
        {
            CostReductionPerAlliedDeathThisGame = Math.Max(
                0,
                costReductionPerAlliedDeathThisGame
            );

            RevivesAtRoundStart = revivesAtRoundStart;

            PowerAndHealthPerDeath = Math.Max(0, powerAndHealthPerDeath);

            JoinsAttackFromGraveyard = joinsAttackFromGraveyard;

            RequiresEphemeralAttacker = requiresEphemeralAttacker;
        }

        #endregion
    }

    public static class CardCostCalculator
    {
        #region Cost Calculation

        public static int GetEffectiveCost(
            MatchState state,
            CardInstance card,
            CardDefinition definition
        )
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (card == null || definition == null)
            {
                return 0;
            }

            int alliedDeaths = state.RoundHistory.CountDeathsThisGame(card.Owner);

            long passiveReduction =
                (long)alliedDeaths
                * definition.PassiveRules.CostReductionPerAlliedDeathThisGame;

            long effectiveCost = (long)card.GetCost(definition) - passiveReduction;

            return effectiveCost <= 0 ? 0 : (int)Math.Min(int.MaxValue, effectiveCost);
        }

        #endregion
    }

    internal static class ReviveStatCalculator
    {
        /// <summary>
        /// Reapplies stat growth derived from the card's complete death history after a
        /// revive reset it to base stats. Calling this once per successful revive keeps
        /// normal effects and RoundStart passive revives consistent.
        /// </summary>
        public static void ApplyDeathScaling(
            MatchState state,
            CardInstance card,
            CardDefinition definition
        )
        {
            if (state == null || card == null || definition == null)
            {
                return;
            }

            int bonus =
                state.RoundHistory.CountDeathsThisGame(card.InstanceId)
                * definition.PassiveRules.PowerAndHealthPerDeath;

            if (bonus > 0)
            {
                card.ApplyBuff(definition, bonus, bonus, EffectDuration.Permanent);
            }
        }
    }

    /// <summary>
    /// Handles passive rules that need round history or zone coordination but do not map
    /// cleanly to a single immediate effect operation.
    /// </summary>
    public sealed class UnitPassiveRuleResolver
    {
        #region Dependencies

        private readonly IReadOnlyDictionary<string, CardDefinition> definitionsById;

        #endregion

        #region Construction

        public UnitPassiveRuleResolver(
            IReadOnlyDictionary<string, CardDefinition> definitionsById
        )
        {
            this.definitionsById =
                definitionsById
                ?? throw new ArgumentNullException(nameof(definitionsById));
        }

        #endregion

        #region Public Resolution

        public GameEventBatch ReviveAtRoundStart(MatchState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var events = new List<GameEvent>();

            ReviveForPlayer(state, state.Host, events);

            ReviveForPlayer(state, state.Guest, events);

            return new GameEventBatch(events);
        }

        public GameEventBatch JoinAttackFromGraveyard(
            MatchState state,
            SeatId attackerSeat,
            CardInstance[] attackersBySlot
        )
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (
                attackersBySlot == null
                || attackersBySlot.Length != MatchState.BoardSlotCount
            )
            {
                throw new ArgumentException(
                    "Attacker array must match board size.",
                    nameof(attackersBySlot)
                );
            }

            bool hasEphemeralAttacker = ContainsEphemeralAttacker(attackersBySlot);

            PlayerState player = state.Player(attackerSeat);

            var candidates = new List<CardInstance>(player.Graveyard);

            candidates.Sort((left, right) => left.InstanceId.CompareTo(right.InstanceId));

            var events = new List<GameEvent>();

            int candidateIndex = 0;

            for (int slotIndex = 0; slotIndex < attackersBySlot.Length; slotIndex++)
            {
                if (attackersBySlot[slotIndex] != null)
                {
                    continue;
                }

                while (candidateIndex < candidates.Count)
                {
                    CardInstance candidate = candidates[candidateIndex++];

                    if (
                        !definitionsById.TryGetValue(
                            candidate.DefinitionId,
                            out CardDefinition definition
                        )
                    )
                    {
                        continue;
                    }

                    UnitPassiveRules rules = definition.PassiveRules;

                    if (!rules.JoinsAttackFromGraveyard)
                    {
                        continue;
                    }

                    if (rules.RequiresEphemeralAttacker && !hasEphemeralAttacker)
                    {
                        continue;
                    }

                    bool revived = player.TryReviveCardToBoardForTriggeredAttack(
                        candidate,
                        definition,
                        slotIndex
                    );

                    if (!revived)
                    {
                        continue;
                    }

                    ReviveStatCalculator.ApplyDeathScaling(
                        state,
                        candidate,
                        definition
                    );

                    var joined = AbilityCardState.Capture(candidate, definition);
                    state.Journal.Add(state.RoundNumber, MatchLogCategory.Ability, v =>
                        $"{MatchJournal.Card(joined, v)} hồi sinh từ nghĩa địa để cùng tấn công" +
                        (rules.RequiresEphemeralAttacker ? " với đồng minh có hiệu ứng Phù Du." : "."));
                    attackersBySlot[slotIndex] = candidate;

                    events.Add(
                        GameEvent.FromCard(
                            GameEventType.UnitSummoned,
                            state.RoundNumber,
                            candidate
                        )
                    );

                    break;
                }
            }

            return new GameEventBatch(events);
        }

        #endregion

        #region Resolution Helpers

        private void ReviveForPlayer(
            MatchState state,
            PlayerState player,
            List<GameEvent> events
        )
        {
            var candidates = new List<CardInstance>(player.Graveyard);

            candidates.Sort((left, right) => left.InstanceId.CompareTo(right.InstanceId));

            foreach (CardInstance card in candidates)
            {
                if (
                    !definitionsById.TryGetValue(
                        card.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    continue;
                }

                UnitPassiveRules rules = definition.PassiveRules;

                if (!rules.RevivesAtRoundStart)
                {
                    continue;
                }

                int latestDeathRound = state.RoundHistory.GetLatestDeathRound(
                    card.InstanceId
                );

                if (latestDeathRound <= 0 || latestDeathRound >= state.RoundNumber)
                {
                    continue;
                }

                if (!player.HasActiveRosterSpace)
                {
                    break;
                }

                AbilityCardState before = AbilityCardState.Capture(card, definition);

                if (!player.TryReviveCardToReserve(card, definition))
                {
                    continue;
                }

                ReviveStatCalculator.ApplyDeathScaling(state, card, definition);

                state.RecordAbilityResolution(
                    new AbilityResolutionEvent(
                        state.NextAbilityResolutionSequence(),
                        null,
                        EffectKind.Revive,
                        null,
                        before,
                        AbilityCardState.Capture(card, definition),
                        isRoundStartPassive: true,
                        journalContext: $"Đã chết {state.RoundHistory.CountDeathsThisGame(card.InstanceId)} lần, " +
                            $"nhận +{state.RoundHistory.CountDeathsThisGame(card.InstanceId) * rules.PowerAndHealthPerDeath}|+{state.RoundHistory.CountDeathsThisGame(card.InstanceId) * rules.PowerAndHealthPerDeath}."
                    )
                );

                events.Add(
                    GameEvent.FromCard(
                        GameEventType.UnitSummoned,
                        state.RoundNumber,
                        card
                    )
                );
            }
        }

        private bool ContainsEphemeralAttacker(CardInstance[] attackersBySlot)
        {
            foreach (CardInstance attacker in attackersBySlot)
            {
                if (
                    attacker == null
                    || !definitionsById.TryGetValue(
                        attacker.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    continue;
                }

                if ((definition.Keywords & UnitKeyword.Ephemeral) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
