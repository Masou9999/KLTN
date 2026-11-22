using System;
using System.Collections.Generic;
using KLTN.Game.Domain;

namespace KLTN.Game.Networking
{
    /// <summary>
    /// Maps immutable combat results to presentation DTOs. Pre-combat stats are read from
    /// the resolution so animations remain correct after cards change zones or modifiers.
    /// </summary>
    public sealed class RoundResolutionDtoMapper
    {
        #region Fields

        private readonly IReadOnlyDictionary<string, CardDefinition> definitionsById;

        #endregion

        #region Construction

        public RoundResolutionDtoMapper(
            IReadOnlyDictionary<string, CardDefinition> definitionsById
        )
        {
            this.definitionsById =
                definitionsById
                ?? throw new ArgumentNullException(nameof(definitionsById));
        }

        #endregion

        #region Mapping

        public RoundResolutionDto Build(RoundResolution resolution, MatchState state)
        {
            if (resolution == null)
            {
                return null;
            }
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var steps = new CombatStepDto[resolution.Steps.Count];

            for (int i = 0; i < resolution.Steps.Count; i++)
            {
                CombatStep source = resolution.Steps[i];

                steps[i] = new CombatStepDto
                {
                    slotIndex = source.SlotIndex,
                    hostCard = BuildCard(source.HostCard, state, source.SlotIndex),
                    guestCard = BuildCard(source.GuestCard, state, source.SlotIndex),

                    hostNexusHealthBefore = source.HostNexusHealthBefore,
                    hostNexusHealthAfter = source.HostNexusHealthAfter,
                    guestNexusHealthBefore = source.GuestNexusHealthBefore,
                    guestNexusHealthAfter = source.GuestNexusHealthAfter,
                };
            }

            return new RoundResolutionDto
            {
                roundNumber = resolution.RoundNumber,
                steps = steps,
            };
        }

        private ResolvedCardDto BuildCard(
            CardCombatResolution resolution,
            MatchState state,
            int slotIndex
        )
        {
            if (resolution == null)
            {
                return null;
            }

            PlayerState player = state.Player(resolution.Seat);

            CardInstance instance = player.FindCardAnywhere(resolution.CardInstanceId);

            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Cannot map combat card {resolution.CardInstanceId}."
                );
            }

            definitionsById.TryGetValue(
                instance.DefinitionId,
                out CardDefinition definition
            );

            return new ResolvedCardDto
            {
                seat = (int)resolution.Seat,

                healthAfter = resolution.HealthAfter,

                damageTaken = resolution.DamageTaken,

                died = resolution.Died,

                diedFromEphemeral = resolution.DiedFromEphemeral,

                cardBefore = new CardViewDto
                {
                    instanceId = instance.InstanceId.ToString(),
                    definitionId = instance.DefinitionId,
                    displayName = definition?.DisplayName ?? "Unknown card",

                    health = resolution.HealthBefore,
                    damage = resolution.DamageBefore,
                    energy =
                        definition == null
                            ? 0
                            : CardCostCalculator.GetEffectiveCost(
                                state,
                                instance,
                                definition
                            ),
                    boardSlotIndex = slotIndex,
                    isAttackCompanion = resolution.IsAttackCompanion,
                },
            };
        }

        #endregion
    }
}
