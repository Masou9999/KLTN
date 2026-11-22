using System;
using System.Collections.Generic;
using CMCMProductions;
using KLTN.Game.Domain;

namespace KLTN.Game.Content
{
    /// <summary>
    /// Validates Unity authoring assets and converts them into immutable, engine-facing
    /// definitions before a match begins.
    /// </summary>
    public static class CardDefinitionMapper
    {
        public static CardDefinition Create(Card cardAsset)
        {
            if (cardAsset == null)
            {
                throw new ArgumentNullException(nameof(cardAsset));
            }

            var abilities = new List<AbilityDefinition>();

            var abilityIds = new HashSet<string>(StringComparer.Ordinal);

            if (cardAsset.abilities != null)
            {
                foreach (CardAbilityData abilityData in cardAsset.abilities)
                {
                    AbilityDefinition ability = CreateAbility(cardAsset, abilityData);

                    if (!abilityIds.Add(ability.Id))
                    {
                        throw new InvalidOperationException(
                            $"Card '{cardAsset.name}' contains "
                                + $"duplicate ability ID '{ability.Id}'."
                        );
                    }

                    abilities.Add(ability);
                }
            }

            return new CardDefinition(
                cardAsset.name,
                cardAsset.cardName,
                cardAsset.health,
                cardAsset.damage,
                cardAsset.energy,
                cardAsset.keywords,
                abilities,
                cardAsset.rulesText,
                CreatePassiveRules(cardAsset.passiveRules),
                cardAsset.maximumCopiesPerDeck,
                cardAsset.drawCostOverride
            );
        }

        private static UnitPassiveRules CreatePassiveRules(CardPassiveData data)
        {
            if (data == null)
            {
                return UnitPassiveRules.None;
            }

            return new UnitPassiveRules(
                data.costReductionPerAlliedDeathThisGame,
                data.revivesAtRoundStart,
                data.powerAndHealthPerDeath,
                data.joinsAttackFromGraveyard,
                data.requiresEphemeralAttacker
            );
        }

        private static AbilityDefinition CreateAbility(
            Card cardAsset,
            CardAbilityData data
        )
        {
            if (data == null)
            {
                throw new InvalidOperationException(
                    $"Card '{cardAsset.name}' contains " + "a null ability."
                );
            }

            var requiredTargets = new List<AbilityTargetRequirement>();

            var usedSlots = new HashSet<AbilityTargetSlot>();

            if (data.requiredTargets != null)
            {
                foreach (AbilityTargetRequirementData targetData in data.requiredTargets)
                {
                    if (targetData == null)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{data.abilityId}' contains "
                                + "a null target requirement."
                        );
                    }

                    if (!usedSlots.Add(targetData.slot))
                    {
                        throw new InvalidOperationException(
                            $"Ability '{data.abilityId}' declares "
                                + $"target slot '{targetData.slot}' twice."
                        );
                    }

                    requiredTargets.Add(
                        new AbilityTargetRequirement(
                            targetData.slot,
                            targetData.relation,
                            targetData.zones,
                            targetData.count,
                            targetData.excludeSource
                        )
                    );
                }
            }

            var effects = new List<EffectDefinition>();

            if (data.effects != null)
            {
                foreach (CardEffectData effectData in data.effects)
                {
                    if (effectData == null)
                    {
                        throw new InvalidOperationException(
                            $"Ability '{data.abilityId}' contains " + "a null effect."
                        );
                    }

                    ValidateSelectedTarget(data.abilityId, effectData.target, usedSlots);

                    effects.Add(
                        new EffectDefinition(
                            effectData.kind,
                            effectData.target,
                            effectData.amount,
                            effectData.secondaryAmount,
                            effectData.count,
                            effectData.duration,
                            effectData.cardDefinitionId
                        )
                    );
                }
            }

            return new AbilityDefinition(
                data.abilityId,
                data.trigger,
                effects,
                data.condition,
                data.oncePerRound,
                requiredTargets,
                data.oncePerMatch,
                data.missingTargetPolicy
            );
        }

        private static void ValidateSelectedTarget(
            string abilityId,
            EffectTarget effectTarget,
            HashSet<AbilityTargetSlot> usedSlots
        )
        {
            if (
                effectTarget == EffectTarget.PrimarySelection
                && !usedSlots.Contains(AbilityTargetSlot.Primary)
            )
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' uses PrimarySelection "
                        + "without declaring a Primary target."
                );
            }

            if (
                effectTarget == EffectTarget.SecondarySelection
                && !usedSlots.Contains(AbilityTargetSlot.Secondary)
            )
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityId}' uses SecondarySelection "
                        + "without declaring a Secondary target."
                );
            }
        }
    }
}
