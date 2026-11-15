using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed partial class AbilityEffectExecutor
    {
        #region Summon, Copy and Revive

        private void ExecuteSummon(
            MatchState state,
            TriggeredAbility triggeredAbility,
            EffectDefinition effect,
            List<GameEvent> emittedEvents
        )
        {
            if (
                string.IsNullOrWhiteSpace(effect.CardDefinitionId)
                || !definitionsById.TryGetValue(
                    effect.CardDefinitionId,
                    out CardDefinition definition
                )
            )
            {
                return;
            }

            PlayerState owner = state.Player(triggeredAbility.ListenerOwner);

            for (int i = 0; i < effect.Count; i++)
            {
                if (!owner.HasActiveRosterSpace)
                {
                    break;
                }

                var created = new CardInstance(
                    state.AllocateCardInstanceId(),
                    definition.Id,
                    owner.Seat,
                    CardZone.Reserve,
                    definition.BaseHealth
                );

                if (!owner.TryAddCreatedCardToReserve(created))
                {
                    break;
                }

                RecordJournalCard(state, triggeredAbility, EffectKind.Summon, null, CaptureCard(created));

                emittedEvents.Add(
                    GameEvent.FromCard(
                        GameEventType.UnitSummoned,
                        state.RoundNumber,
                        created
                    )
                );
            }
        }

        private void ExecuteCopy(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            PlayerState owner = state.Player(triggeredAbility.ListenerOwner);

            if (!string.IsNullOrWhiteSpace(effect.CardDefinitionId))
            {
                for (int i = 0; i < effect.Count; i++)
                {
                    TryCreateCopyInDrawHand(state, triggeredAbility, owner, effect.CardDefinitionId);
                }

                return;
            }

            if (effect.Target == EffectTarget.RandomDeadAlly)
            {
                var candidates = new List<DeathRecord>();

                foreach (DeathRecord death in state.RoundHistory.AllDeaths)
                {
                    if (
                        death.Owner == owner.Seat
                        && definitionsById.ContainsKey(death.DefinitionId)
                    )
                    {
                        candidates.Add(death);
                    }
                }

                for (int i = 0; i < effect.Count && candidates.Count > 0; i++)
                {
                    int index = random.Next(0, candidates.Count);

                    DeathRecord selected = candidates[index];

                    candidates.RemoveAt(index);

                    TryCreateCopyInDrawHand(state, triggeredAbility, owner, selected.DefinitionId);
                }

                return;
            }

            IReadOnlyList<CardInstance> targets = ResolveCardTargets(
                state,
                triggeredAbility,
                selection,
                effect
            );

            int copyCount = Math.Min(effect.Count, targets.Count);

            for (int i = 0; i < copyCount; i++)
            {
                TryCreateCopyInDrawHand(state, triggeredAbility, owner, targets[i].DefinitionId);
            }
        }

        private bool TryCreateCopyInDrawHand(
            MatchState state,
            TriggeredAbility triggeredAbility,
            PlayerState owner,
            string definitionId
        )
        {
            if (!definitionsById.TryGetValue(definitionId, out CardDefinition definition))
            {
                return false;
            }

            var copy = new CardInstance(
                state.AllocateCardInstanceId(),
                definition.Id,
                owner.Seat,
                CardZone.DrawHand,
                definition.BaseHealth
            );

            if (!owner.TryAddCreatedCardToDrawHand(copy)) return false;
            RecordJournalCard(state, triggeredAbility, EffectKind.Copy, null, CaptureCard(copy));
            return true;
        }

        private void ExecuteRevive(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect,
            List<GameEvent> emittedEvents
        )
        {
            PlayerState owner = state.Player(triggeredAbility.ListenerOwner);

            IReadOnlyList<CardInstance> targets = ResolveReviveTargets(
                state,
                triggeredAbility,
                selection,
                effect
            );

            foreach (CardInstance target in targets)
            {
                if (!owner.HasActiveRosterSpace)
                {
                    break;
                }

                if (
                    target.Owner != owner.Seat
                    || !definitionsById.TryGetValue(
                        target.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    continue;
                }

                AbilityCardState before = CaptureCard(target);

                if (!owner.TryReviveCardToReserve(target, definition))
                {
                    continue;
                }

                ReviveStatCalculator.ApplyDeathScaling(state, target, definition);

                RecordCardEffect(
                    state,
                    triggeredAbility,
                    EffectKind.Revive,
                    before,
                    CaptureCard(target)
                );

                emittedEvents.Add(
                    GameEvent.FromCard(
                        GameEventType.UnitSummoned,
                        state.RoundNumber,
                        target
                    )
                );
            }
        }

        private IReadOnlyList<CardInstance> ResolveReviveTargets(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            var result = new List<CardInstance>();

            PlayerState owner = state.Player(triggeredAbility.ListenerOwner);

            if (
                effect.Target == EffectTarget.PrimarySelection
                || effect.Target == EffectTarget.SecondarySelection
            )
            {
                IReadOnlyList<CardInstance> selected = ResolveCardTargets(
                    state,
                    triggeredAbility,
                    selection,
                    effect
                );

                foreach (CardInstance card in selected)
                {
                    if (
                        card.Owner == owner.Seat
                        && card.Zone == CardZone.Graveyard
                        && owner.Graveyard.Contains(card)
                    )
                    {
                        AddUnique(card, result);
                    }

                    if (result.Count >= effect.Count)
                    {
                        break;
                    }
                }

                return result.AsReadOnly();
            }

            if (effect.Target == EffectTarget.StrongestDeadAlly)
            {
                var records = new List<DeathRecord>();

                foreach (CardInstance card in owner.Graveyard)
                {
                    DeathRecord latestRecord = FindLatestDeathRecord(
                        state.RoundHistory.AllDeaths,
                        card.InstanceId
                    );

                    if (latestRecord == null || latestRecord.Owner != owner.Seat)
                    {
                        continue;
                    }

                    records.Add(latestRecord);
                }

                records.Sort(CompareStrongestDeath);

                foreach (DeathRecord record in records)
                {
                    CardInstance card = owner.Graveyard.Find(item =>
                        item.InstanceId == record.CardInstanceId
                    );

                    AddUnique(card, result);

                    if (result.Count >= effect.Count)
                    {
                        break;
                    }
                }

                return result.AsReadOnly();
            }

            if (effect.Target == EffectTarget.RandomDeadAlly)
            {
                var candidates = new List<CardInstance>();

                foreach (CardInstance card in owner.Graveyard)
                {
                    if (WasRecordedAsDead(state.RoundHistory.AllDeaths, card.InstanceId))
                    {
                        candidates.Add(card);
                    }
                }

                while (result.Count < effect.Count && candidates.Count > 0)
                {
                    int index = random.Next(0, candidates.Count);

                    AddUnique(candidates[index], result);

                    candidates.RemoveAt(index);
                }
            }

            return result.AsReadOnly();
        }

        private static DeathRecord FindLatestDeathRecord(
            IReadOnlyList<DeathRecord> records,
            ulong instanceId
        )
        {
            DeathRecord latest = null;

            foreach (DeathRecord record in records)
            {
                if (
                    record.CardInstanceId == instanceId
                    && (latest == null || record.Sequence > latest.Sequence)
                )
                {
                    latest = record;
                }
            }

            return latest;
        }

        private static int CompareStrongestDeath(DeathRecord left, DeathRecord right)
        {
            int damageComparison = right.DamageAtDeath.CompareTo(left.DamageAtDeath);

            if (damageComparison != 0)
            {
                return damageComparison;
            }

            int healthComparison = right.MaximumHealthAtDeath.CompareTo(
                left.MaximumHealthAtDeath
            );

            if (healthComparison != 0)
            {
                return healthComparison;
            }

            return right.Sequence.CompareTo(left.Sequence);
        }

        private static bool WasRecordedAsDead(
            IReadOnlyList<DeathRecord> records,
            ulong instanceId
        )
        {
            foreach (DeathRecord record in records)
            {
                if (record.CardInstanceId == instanceId)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
