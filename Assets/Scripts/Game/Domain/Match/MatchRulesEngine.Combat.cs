using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed partial class MatchRulesEngine
    {
        #region Combat Resolution

        private RoundResolution ResolveCombat(
            MatchState state,
            SeatId attackerSeat,
            out GameEventBatch deathEventBatch
        )
        {
            SeatId defenderSeat = attackerSeat.Opponent();

            PlayerState defender = state.Player(defenderSeat);

            var steps = new List<CombatStep>();

            for (int slotIndex = 0; slotIndex < MatchState.BoardSlotCount; slotIndex++)
            {
                CardInstance hostCard = state.Host.FindBoardCard(slotIndex);

                CardInstance guestCard = state.Guest.FindBoardCard(slotIndex);

                CardInstance attackerCard =
                    attackerSeat == SeatId.Host ? hostCard : guestCard;

                CardInstance blockerCard =
                    attackerSeat == SeatId.Host ? guestCard : hostCard;

                if (attackerCard == null)
                {
                    continue;
                }

                int hostCardHealthBefore = hostCard?.CurrentHealth ?? 0;

                int guestCardHealthBefore = guestCard?.CurrentHealth ?? 0;

                int hostNexusHealthBefore = state.Host.NexusHealth;

                int guestNexusHealthBefore = state.Guest.NexusHealth;

                int attackerDamageTaken = 0;
                int blockerDamageTaken = 0;

                bool attackerDiedFromEphemeral = false;

                bool blockerDiedFromEphemeral = false;

                if (blockerCard != null)
                {
                    int attackerDamage = DamageOf(attackerCard);

                    int blockerDamage = DamageOf(blockerCard);

                    attackerDamageTaken = CalculateActualDamage(
                        blockerDamage,
                        attackerCard.CurrentHealth
                    );

                    blockerDamageTaken = CalculateActualDamage(
                        attackerDamage,
                        blockerCard.CurrentHealth
                    );

                    var attackerBefore = CaptureJournalCard(attackerCard);
                    var blockerBefore = CaptureJournalCard(blockerCard);
                    attackerCard.ApplyDamage(blockerDamage);

                    blockerCard.ApplyDamage(attackerDamage);
                    state.Journal.CombatDamage(state.RoundNumber, attackerBefore, blockerBefore, blockerBefore.Health, blockerCard.CurrentHealth, defenderSeat);
                    state.Journal.CombatDamage(state.RoundNumber, blockerBefore, attackerBefore, attackerBefore.Health, attackerCard.CurrentHealth, attackerSeat);

                    ApplyLifesteal(state, attackerCard, blockerDamageTaken);

                    ApplyLifesteal(state, blockerCard, attackerDamageTaken);

                    if (
                        HasKeyword(attackerCard, UnitKeyword.Ephemeral)
                        && !attackerCard.IsDead
                    )
                    {
                        state.Journal.Ephemeral(state.RoundNumber, CaptureJournalCard(attackerCard), false);
                        attackerCard.Kill();

                        attackerDiedFromEphemeral = true;
                    }

                    if (
                        HasKeyword(blockerCard, UnitKeyword.Ephemeral)
                        && !blockerCard.IsDead
                    )
                    {
                        state.Journal.Ephemeral(state.RoundNumber, CaptureJournalCard(blockerCard), false);
                        blockerCard.Kill();

                        blockerDiedFromEphemeral = true;
                    }
                }
                else
                {
                    int nexusHealthBefore = defender.NexusHealth;

                    defender.ApplyNexusDamage(DamageOf(attackerCard));

                    int actualNexusDamage = Math.Max(
                        0,
                        nexusHealthBefore - defender.NexusHealth
                    );

                    state.Journal.CombatDamage(state.RoundNumber, CaptureJournalCard(attackerCard), null, nexusHealthBefore, defender.NexusHealth, defenderSeat);
                    ApplyLifesteal(state, attackerCard, actualNexusDamage);

                    if (
                        HasKeyword(attackerCard, UnitKeyword.Ephemeral)
                        && !attackerCard.IsDead
                    )
                    {
                        state.Journal.Ephemeral(state.RoundNumber, CaptureJournalCard(attackerCard), false);
                        attackerCard.Kill();

                        attackerDiedFromEphemeral = true;
                    }
                }

                bool hostIsAttacker = attackerSeat == SeatId.Host;

                CardCombatResolution hostResolution = BuildCardResolution(
                    hostCard,
                    SeatId.Host,
                    hostCardHealthBefore,
                    hostIsAttacker ? attackerDamageTaken : blockerDamageTaken,
                    hostIsAttacker ? attackerDiedFromEphemeral : blockerDiedFromEphemeral
                );

                CardCombatResolution guestResolution = BuildCardResolution(
                    guestCard,
                    SeatId.Guest,
                    guestCardHealthBefore,
                    hostIsAttacker ? blockerDamageTaken : attackerDamageTaken,
                    hostIsAttacker ? blockerDiedFromEphemeral : attackerDiedFromEphemeral
                );

                steps.Add(
                    new CombatStep(
                        slotIndex,
                        hostResolution,
                        guestResolution,
                        hostNexusHealthBefore,
                        state.Host.NexusHealth,
                        guestNexusHealthBefore,
                        state.Guest.NexusHealth
                    )
                );
            }

            List<CardInstance> deadCards = CollectDeadCardsInCombatOrder(
                state,
                attackerSeat
            );

            state.Host.MoveDeadBoardCardsToGraveyard();
            state.Guest.MoveDeadBoardCardsToGraveyard();

            var deathEvents = new List<GameEvent>();

            foreach (CardInstance deadCard in deadCards)
            {
                int damageAtDeath = 0;
                int maximumHealthAtDeath = 0;

                if (
                    definitionsById.TryGetValue(
                        deadCard.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    damageAtDeath = deadCard.GetDamage(definition);

                    maximumHealthAtDeath = deadCard.GetMaximumHealth(definition);
                }

                state.RoundHistory.RecordDeath(
                    deadCard,
                    damageAtDeath,
                    maximumHealthAtDeath
                );
                state.Journal.PassiveCosts(state, definitionsById, deadCard.Owner);

                deathEvents.Add(
                    GameEvent.FromCard(
                        GameEventType.UnitDied,
                        state.RoundNumber,
                        deadCard
                    )
                );
            }

            deathEventBatch = new GameEventBatch(deathEvents);

            return new RoundResolution(state.RoundNumber, steps);
        }

        private static List<CardInstance> CollectDeadCardsInCombatOrder(
            MatchState state,
            SeatId attackerSeat
        )
        {
            var result = new List<CardInstance>();

            PlayerState attacker = state.Player(attackerSeat);

            PlayerState defender = state.Player(attackerSeat.Opponent());

            for (int slotIndex = 0; slotIndex < MatchState.BoardSlotCount; slotIndex++)
            {
                CardInstance attackerCard = attacker.FindBoardCard(slotIndex);

                CardInstance defenderCard = defender.FindBoardCard(slotIndex);

                if (attackerCard != null && attackerCard.IsDead)
                {
                    result.Add(attackerCard);
                }

                if (defenderCard != null && defenderCard.IsDead)
                {
                    result.Add(defenderCard);
                }
            }

            return result;
        }

        private AbilityCardState CaptureJournalCard(CardInstance card)
        {
            if (card == null) return null;
            definitionsById.TryGetValue(card.DefinitionId, out var definition);
            return AbilityCardState.Capture(card, definition);
        }

        private int DamageOf(CardInstance card)
        {
            if (card == null)
            {
                return 0;
            }

            if (
                !definitionsById.TryGetValue(
                    card.DefinitionId,
                    out CardDefinition definition
                )
            )
            {
                return 0;
            }

            return card.GetDamage(definition);
        }

        private bool HasKeyword(CardInstance card, UnitKeyword keyword)
        {
            if (
                card == null
                || !definitionsById.TryGetValue(
                    card.DefinitionId,
                    out CardDefinition definition
                )
            )
            {
                return false;
            }

            return (definition.Keywords & keyword) != 0;
        }

        private static int CalculateActualDamage(int attemptedDamage, int healthBefore)
        {
            return Math.Min(Math.Max(0, attemptedDamage), Math.Max(0, healthBefore));
        }

        private void ApplyLifesteal(
            MatchState state,
            CardInstance source,
            int actualDamage
        )
        {
            if (actualDamage <= 0 || !HasKeyword(source, UnitKeyword.Lifesteal))
            {
                return;
            }

            int before = state.Player(source.Owner).NexusHealth;
            state.Player(source.Owner).HealNexus(actualDamage);
            int after = state.Player(source.Owner).NexusHealth;
            if (before != after)
                state.Journal.Ability(state.RoundNumber,
                    new AbilityResolutionEvent(0, null, EffectKind.Heal, CaptureJournalCard(source), null, null,
                        source.Owner, before, after), " kích hoạt Hút máu");
        }

        private CardCombatResolution BuildCardResolution(
            CardInstance card,
            SeatId seat,
            int healthBefore,
            int damageTaken,
            bool diedFromEphemeral
        )
        {
            if (card == null)
            {
                return null;
            }

            return new CardCombatResolution(
                seat,
                card.InstanceId,
                healthBefore,
                card.CurrentHealth,
                DamageOf(card),
                damageTaken,
                diedFromEphemeral
            );
        }

        private static void UpdateOutcome(MatchState state)
        {
            bool hostDead = state.Host.NexusHealth <= 0;

            bool guestDead = state.Guest.NexusHealth <= 0;

            if (hostDead && guestDead)
            {
                state.Outcome = MatchOutcome.Draw;
            }
            else if (hostDead)
            {
                state.Outcome = MatchOutcome.GuestWon;
            }
            else if (guestDead)
            {
                state.Outcome = MatchOutcome.HostWon;
            }
        }

        #endregion
    }
}
