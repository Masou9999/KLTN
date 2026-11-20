using System;
using System.Collections.Generic;
using KLTN.Game.Domain;

namespace KLTN.Game.Networking
{
    /// <summary>
    /// Projects semantic ability effects without disclosing an opponent's hidden cards.
    /// </summary>
    public sealed class AbilityResolutionDtoMapper
    {
        private readonly IReadOnlyDictionary<string, CardDefinition> definitionsById;

        public AbilityResolutionDtoMapper(
            IReadOnlyDictionary<string, CardDefinition> definitionsById
        )
        {
            this.definitionsById = definitionsById
                ?? throw new ArgumentNullException(nameof(definitionsById));
        }

        public AbilityResolutionDto Build(AbilityResolution resolution, SeatId viewer)
        {
            if (resolution?.Events == null || resolution.Events.Count == 0)
            {
                return null;
            }

            var events = new List<AbilityResolutionEventDto>();

            foreach (AbilityResolutionEvent effect in resolution.Events)
            {
                CardViewDto source = MapVisible(effect.Source, viewer);
                CardViewDto before = MapVisible(effect.TargetBefore, viewer);
                CardViewDto after = MapVisible(effect.TargetAfter, viewer);

                // Public copy effects retain only their destination for the opponent's
                // card-back animation. Never project the copied card's identity.
                bool hiddenCopy = effect.Kind == EffectKind.Copy
                    && effect.TargetAfter?.Zone == CardZone.DrawHand
                    && source != null;

                // Other hidden hand/deck targets must not leak through animation DTOs.
                if (
                    !effect.NexusOwner.HasValue
                    && (effect.TargetBefore != null || effect.TargetAfter != null)
                    && before == null
                    && after == null
                    && !hiddenCopy
                )
                {
                    continue;
                }

                int damageBonus = 0;
                int healthBonus = 0;

                if (
                    effect.Kind == EffectKind.Revive
                    && effect.TargetAfter != null
                    && definitionsById.TryGetValue(
                        effect.TargetAfter.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    damageBonus = Math.Max(
                        0,
                        effect.TargetAfter.Damage - definition.BaseDamage
                    );
                    healthBonus = Math.Max(
                        0,
                        effect.TargetAfter.MaximumHealth - definition.BaseHealth
                    );
                }

                events.Add(
                    new AbilityResolutionEventDto
                    {
                        sequence = effect.Sequence,
                        executionId = effect.ExecutionId,
                        abilityId = effect.AbilityId,
                        effectKind = (int)effect.Kind,
                        isRoundStartPassive = effect.IsRoundStartPassive,
                        isDeathOnly = effect.IsDeathOnly,
                        source = source,
                        targetBefore = before,
                        targetAfter = after,
                        sourceOwner = effect.Source == null
                            ? -1
                            : (int)effect.Source.Owner,
                        sourceZone = effect.Source == null
                            ? -1
                            : (int)effect.Source.Zone,
                        targetOwner = effect.TargetAfter != null
                            ? (int)effect.TargetAfter.Owner
                            : effect.TargetBefore == null
                                ? -1
                                : (int)effect.TargetBefore.Owner,
                        targetZoneBefore = effect.TargetBefore == null
                            ? -1
                            : (int)effect.TargetBefore.Zone,
                        targetZoneAfter = effect.TargetAfter == null
                            ? -1
                            : (int)effect.TargetAfter.Zone,
                        hasNexusTarget = effect.NexusOwner.HasValue,
                        nexusOwner = effect.NexusOwner.HasValue
                            ? (int)effect.NexusOwner.Value
                            : -1,
                        nexusHealthBefore = effect.NexusHealthBefore,
                        nexusHealthAfter = effect.NexusHealthAfter,
                        reviveDamageBonus = damageBonus,
                        reviveHealthBonus = healthBonus,
                    }
                );
            }

            return events.Count == 0
                ? null
                : new AbilityResolutionDto { events = events.ToArray() };
        }

        private CardViewDto MapVisible(AbilityCardState card, SeatId viewer)
        {
            if (card == null)
            {
                return null;
            }

            if (card.Zone == CardZone.Deck)
            {
                return null;
            }

            if (
                card.Owner != viewer
                && card.Zone != CardZone.Reserve
                && card.Zone != CardZone.Board
            )
            {
                return null;
            }

            definitionsById.TryGetValue(card.DefinitionId, out CardDefinition definition);

            return new CardViewDto
            {
                instanceId = card.InstanceId.ToString(),
                definitionId = card.DefinitionId,
                displayName = definition?.DisplayName ?? "Unknown card",
                health = card.Health,
                damage = card.Damage,
                energy = definition?.Cost ?? 0,
                keywords = definition == null ? 0 : (int)definition.Keywords,
                boardSlotIndex = card.BoardSlotIndex,
            };
        }
    }
}
