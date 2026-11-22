using System;
using System.Collections.Generic;
using KLTN.Game.Domain;

namespace KLTN.Game.Networking
{
    /// <summary>
    /// Projects authoritative domain state into a privacy-filtered snapshot for one
    /// viewer. Hidden opponent hand identities and deck order never enter the DTO.
    /// </summary>
    public sealed class MatchSnapshotBuilder
    {
        #region Fields

        private readonly IReadOnlyDictionary<string, CardDefinition> definitionsById;

        private readonly AbilityTargetValidator abilityTargetValidator =
            new AbilityTargetValidator();

        #endregion

        #region Construction

        public MatchSnapshotBuilder(
            IReadOnlyDictionary<string, CardDefinition> definitionsById
        )
        {
            this.definitionsById =
                definitionsById
                ?? throw new ArgumentNullException(nameof(definitionsById));

            passiveRuleResolver = new UnitPassiveRuleResolver(definitionsById);
        }

        private readonly UnitPassiveRuleResolver passiveRuleResolver;

        #endregion

        #region Snapshot Construction

        /// <summary>
        /// Builds a complete revisioned projection for <paramref name="viewerSeat"/>.
        /// Command affordances are derived from phase and authority, not trusted client
        /// flags.
        /// </summary>
        public MatchSnapshotDto Build(
            MatchState state,
            SeatId viewerSeat,
            bool opponentConnected,
            ulong revision
        )
        {
            PlayerState self = state.Player(viewerSeat);
            PlayerState opponent = state.Player(viewerSeat.Opponent());

            bool viewerCanAct =
                opponentConnected
                && !state.IsFinished
                && state.Phase == MatchPhase.Priority
                && state.ActiveSeat == viewerSeat;

            bool viewerCanDeclareAttack =
                viewerCanAct
                && state.AttackTokenAvailable
                && state.AttackTokenOwner == viewerSeat
                && self.Reserve.Count > 0
                && self.Board.Count == 0;

            bool viewerCanDeclareBlock =
                opponentConnected
                && !state.IsFinished
                && state.Phase == MatchPhase.BlockDeclaration
                && state.ActiveSeat == viewerSeat
                && state.AttackTokenOwner != viewerSeat
                && opponent.Board.Count > 0;

            PendingAbilitySelectionDto pendingSelection = BuildPendingSelection(
                state,
                viewerSeat
            );

            bool viewerMustSelectAbilityTargets = pendingSelection != null;

            return new MatchSnapshotDto
            {
                revision = revision,
                viewerSeat = (int)viewerSeat,

                firstSeat = (int)state.FirstSeat,
                activeSeat = (int)state.ActiveSeat,
                roundNumber = state.RoundNumber,

                phase = (int)state.Phase,
                attackTokenOwner = (int)state.AttackTokenOwner,
                attackTokenAvailable = state.AttackTokenAvailable,
                consecutivePasses = state.ConsecutivePasses,

                outcome = (int)state.Outcome,
                surrenderedSeat = state.SurrenderedSeat.HasValue ? (int)state.SurrenderedSeat.Value : -1,
                viewerCanAct = viewerCanAct,

                viewerCanEndRound = viewerCanAct && state.CanEndRound,

                viewerCanDeclareAttack = viewerCanDeclareAttack,
                viewerCanDeclareBlock = viewerCanDeclareBlock,

                viewerMustSelectAbilityTargets = viewerMustSelectAbilityTargets,

                pendingAbilitySelection = pendingSelection,

                self = BuildPlayer(state, self, revealHand: true, connected: true),

                opponent = BuildPlayer(
                    state,
                    opponent,
                    revealHand: false,
                    connected: opponentConnected
                ),

                status = state.LastEvent,
            };
        }

        public static MatchSnapshotDto BuildWaiting(
            SeatId viewerSeat,
            bool opponentConnected,
            ulong revision
        )
        {
            SeatId opponentSeat = viewerSeat.Opponent();

            return new MatchSnapshotDto
            {
                revision = revision,
                viewerSeat = (int)viewerSeat,

                firstSeat = -1,
                activeSeat = -1,
                roundNumber = 0,

                phase = (int)MatchPhase.Mulligan,
                attackTokenOwner = -1,
                attackTokenAvailable = false,
                consecutivePasses = 0,

                outcome = (int)MatchOutcome.Running,
                viewerCanAct = false,
                viewerCanEndRound = false,
                viewerCanDeclareAttack = false,
                viewerCanDeclareBlock = false,

                viewerMustSelectAbilityTargets = false,
                pendingAbilitySelection = null,

                self = BuildWaitingPlayer(viewerSeat, connected: true),

                opponent = BuildWaitingPlayer(opponentSeat, opponentConnected),

                status = opponentConnected
                    ? "Đang khởi tạo trận đấu."
                    : "Đang chờ người chơi còn lại.",
            };
        }

        #endregion

        #region Player and Card Mapping

        private PendingAbilitySelectionDto BuildPendingSelection(
            MatchState state,
            SeatId viewerSeat
        )
        {
            PendingAbilitySelection pending = state.PendingSelection;

            if (pending == null || pending.ChoosingSeat != viewerSeat)
            {
                return null;
            }

            var requirements = new AbilityTargetRequirementDto[
                pending.Ability.RequiredTargets.Count
            ];

            for (int i = 0; i < pending.Ability.RequiredTargets.Count; i++)
            {
                AbilityTargetRequirement requirement = pending.Ability.RequiredTargets[i];

                IReadOnlyList<CardInstance> validTargets =
                    abilityTargetValidator.GetValidTargets(
                        state,
                        pending.ChoosingSeat,
                        pending.SourceCardInstanceId,
                        requirement
                    );

                var validTargetIds = new string[validTargets.Count];
                var lethalTargetIds = new List<string>();

                for (int targetIndex = 0; targetIndex < validTargets.Count; targetIndex++)
                {
                    validTargetIds[targetIndex] = validTargets[targetIndex]
                        .InstanceId.ToString();

                    if (
                        WouldSelectedTargetDie(
                            pending.Ability,
                            requirement.Slot,
                            validTargets[targetIndex]
                        )
                    )
                    {
                        lethalTargetIds.Add(validTargetIds[targetIndex]);
                    }
                }

                requirements[i] = new AbilityTargetRequirementDto
                {
                    slot = (int)requirement.Slot,

                    relation = (int)requirement.Relation,

                    zones = (int)requirement.Zones,

                    count = requirement.Count,

                    excludeSource = requirement.ExcludeSource,

                    validTargetIds = validTargetIds,
                    lethalTargetIds = lethalTargetIds.ToArray(),
                };
            }

            return new PendingAbilitySelectionDto
            {
                requestId = pending.RequestId.ToString(),

                sourceCardInstanceId = pending.SourceCardInstanceId.ToString(),

                choosingSeat = (int)pending.ChoosingSeat,

                abilityId = pending.Ability.Id,

                canCancel = pending.CanCancel,

                requirements = requirements,
            };
        }

        private static bool WouldSelectedTargetDie(
            AbilityDefinition ability,
            AbilityTargetSlot slot,
            CardInstance target
        )
        {
            EffectTarget selectedTarget = slot == AbilityTargetSlot.Primary
                ? EffectTarget.PrimarySelection
                : EffectTarget.SecondarySelection;

            int totalDamage = 0;

            foreach (EffectDefinition effect in ability.Effects)
            {
                if (effect.Target != selectedTarget)
                {
                    continue;
                }

                if (effect.Kind == EffectKind.Kill || effect.Kind == EffectKind.Sacrifice)
                {
                    return true;
                }

                if (effect.Kind == EffectKind.Damage)
                {
                    totalDamage += Math.Max(0, effect.Amount);
                }
            }

            return totalDamage > 0 && totalDamage >= target.CurrentHealth;
        }

        private PlayerViewDto BuildPlayer(
            MatchState state,
            PlayerState player,
            bool revealHand,
            bool connected
        )
        {
            return new PlayerViewDto
            {
                seat = (int)player.Seat,
                displayName = SeatName(player.Seat),
                connected = connected,

                nexusHealth = player.NexusHealth,
                mana = player.Mana,
                maxMana = player.MaxMana,
                deckCount = player.Deck.Count,

                handCount = player.DrawHand.Count,
                reserveCount = player.Reserve.Count,
                activeRosterCount = player.ActiveRosterCount,

                hand = revealHand
                    ? ConvertCards(state, player.DrawHand)
                    : Array.Empty<CardViewDto>(),

                reserve = ConvertCards(state, player.Reserve),

                board = ConvertCards(state, player.Board),

                graveyard = ConvertCards(state, player.Graveyard),

                // Only the owner stages attacks, so only the owner needs the preview.
                attackCompanions = revealHand
                    ? BuildAttackCompanionPreviews(state, player.Seat)
                    : Array.Empty<CardViewDto>(),
            };
        }

        /// <summary>
        /// Describes each unused Last Breath in server fill order. The cards have no
        /// instance ID: they are previews and are never valid command arguments.
        /// </summary>
        private CardViewDto[] BuildAttackCompanionPreviews(MatchState state, SeatId seat)
        {
            IReadOnlyList<DeathRecord> deaths =
                passiveRuleResolver.GetPendingAttackCompanions(state, seat);

            var result = new CardViewDto[deaths.Count];

            for (int i = 0; i < deaths.Count; i++)
            {
                CardDefinition definition = definitionsById[deaths[i].DefinitionId];

                int bonus =
                    state.RoundHistory.CountDeathsThisGame(deaths[i].CardInstanceId)
                    * definition.PassiveRules.PowerAndHealthPerDeath;

                GetSupportBuffPreview(
                    definition,
                    out int supportDamageBonus,
                    out int supportHealthBonus
                );

                result[i] = new CardViewDto
                {
                    instanceId = string.Empty,
                    definitionId = definition.Id,
                    displayName = definition.DisplayName,
                    health = Math.Max(0, definition.BaseHealth + bonus),
                    damage = Math.Max(0, definition.BaseDamage + bonus),
                    energy = definition.Cost,
                    keywords = (int)definition.Keywords,
                    supportDamageBonus = supportDamageBonus,
                    supportHealthBonus = supportHealthBonus,
                    isAttackCompanion = true,
                    requiresEphemeralAttacker =
                        definition.PassiveRules.RequiresEphemeralAttacker,
                };
            }

            return result;
        }

        private CardViewDto[] ConvertCards(
            MatchState state,
            IReadOnlyList<CardInstance> cards
        )
        {
            var result = new CardViewDto[cards.Count];

            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance instance = cards[i];
                definitionsById.TryGetValue(
                    instance.DefinitionId,
                    out CardDefinition definition
                );

                GetSupportBuffPreview(
                    definition,
                    out int supportDamageBonus,
                    out int supportHealthBonus
                );

                result[i] = new CardViewDto
                {
                    instanceId = instance.InstanceId.ToString(),
                    definitionId = instance.DefinitionId,
                    displayName = definition?.DisplayName ?? "Unknown card",

                    health = instance.CurrentHealth,

                    damage = definition == null ? 0 : instance.GetDamage(definition),

                    energy =
                        definition == null
                            ? 0
                            : CardCostCalculator.GetEffectiveCost(
                                state,
                                instance,
                                definition
                            ),

                    keywords = definition == null ? 0 : (int)definition.Keywords,

                    supportDamageBonus = supportDamageBonus,
                    supportHealthBonus = supportHealthBonus,

                    boardSlotIndex = instance.BoardSlotIndex,

                    isAttackCompanion =
                        instance.IsAttackCompanion && instance.Zone == CardZone.Board,
                };
            }

            return result;
        }

        private static void GetSupportBuffPreview(
            CardDefinition definition,
            out int damageBonus,
            out int healthBonus
        )
        {
            damageBonus = 0;
            healthBonus = 0;

            if (definition == null)
            {
                return;
            }

            foreach (AbilityDefinition ability in definition.Abilities)
            {
                if (ability.Trigger != AbilityTrigger.Support)
                {
                    continue;
                }

                foreach (EffectDefinition effect in ability.Effects)
                {
                    bool isSupportedUnitBuff =
                        effect.Kind == EffectKind.Buff
                        && effect.Target == EffectTarget.TriggerSubject;

                    if (!isSupportedUnitBuff)
                    {
                        continue;
                    }

                    damageBonus += effect.Amount;
                    healthBonus += effect.SecondaryAmount;
                }
            }
        }

        private static PlayerViewDto BuildWaitingPlayer(SeatId seat, bool connected)
        {
            return new PlayerViewDto
            {
                seat = (int)seat,
                displayName = SeatName(seat),
                connected = connected,

                nexusHealth = 20,
                mana = 0,
                maxMana = 0,
                deckCount = 0,
                handCount = 0,
                reserveCount = 0,
                activeRosterCount = 0,

                hand = Array.Empty<CardViewDto>(),
                reserve = Array.Empty<CardViewDto>(),
                board = Array.Empty<CardViewDto>(),
                graveyard = Array.Empty<CardViewDto>(),
                attackCompanions = Array.Empty<CardViewDto>(),
            };
        }

        #endregion

        #region Helpers

        private static string SeatName(SeatId seat)
        {
            return seat == SeatId.Host ? "Host" : "Guest";
        }

        #endregion
    }
}
