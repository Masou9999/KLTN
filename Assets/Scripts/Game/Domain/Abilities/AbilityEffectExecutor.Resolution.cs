namespace KLTN.Game.Domain
{
    public sealed partial class AbilityEffectExecutor
    {
        private AbilityCardState CaptureCard(CardInstance card, MatchState state = null)
        {
            if (card == null)
            {
                return null;
            }

            definitionsById.TryGetValue(card.DefinitionId, out CardDefinition definition);
            return AbilityCardState.Capture(card, definition, state == null ? (int?)null : CardCostCalculator.GetEffectiveCost(state, card, definition));
        }

        private AbilityCardState CaptureSource(
            MatchState state,
            TriggeredAbility triggeredAbility
        )
        {
            CardInstance source = state
                .Player(triggeredAbility.ListenerOwner)
                .FindCardAnywhere(triggeredAbility.ListenerCardInstanceId);

            return CaptureCard(source, state);
        }

        private void RecordCardEffect(
            MatchState state,
            TriggeredAbility triggeredAbility,
            EffectKind kind,
            AbilityCardState before,
            AbilityCardState after
        )
        {
            state.RecordAbilityResolution(
                new AbilityResolutionEvent(
                    state.NextAbilityResolutionSequence(),
                    triggeredAbility.Ability.Id,
                    kind,
                    CaptureSource(state, triggeredAbility),
                    before,
                    after,
                    journalContext: JournalContext(state, triggeredAbility)
                )
            );
        }

        private void RecordNexusEffect(
            MatchState state,
            TriggeredAbility triggeredAbility,
            EffectKind kind,
            SeatId targetOwner,
            int healthBefore
        )
        {
            int healthAfter = state.Player(targetOwner).NexusHealth;

            if (healthBefore == healthAfter)
            {
                return;
            }

            state.RecordAbilityResolution(
                new AbilityResolutionEvent(
                    state.NextAbilityResolutionSequence(),
                    triggeredAbility.Ability.Id,
                    kind,
                    CaptureSource(state, triggeredAbility),
                    null,
                    null,
                    targetOwner,
                    healthBefore,
                    healthAfter,
                    journalContext: JournalContext(state, triggeredAbility)
                )
            );
        }
        private string JournalContext(MatchState state, TriggeredAbility triggered)
        {
            string context;
            switch (triggered.Ability.Trigger)
            {
                case AbilityTrigger.Play: context = " khi được chơi"; break;
                case AbilityTrigger.Summon: context = " khi được triệu hồi"; break;
                case AbilityTrigger.Death: context = " kích hoạt Trăn trối"; break;
                case AbilityTrigger.Support: context = " hỗ trợ đồng minh bên phải"; break;
                case AbilityTrigger.AllyDeath:
                    var subject = state.Host.FindCardAnywhere(triggered.TriggerEvent.SubjectCardInstanceId)
                        ?? state.Guest.FindCardAnywhere(triggered.TriggerEvent.SubjectCardInstanceId);
                    context = subject != null && definitionsById.TryGetValue(subject.DefinitionId, out var definition)
                        ? $" do đồng minh {definition.DisplayName} chết" : " do đồng minh chết";
                    break;
                default: context = " kích hoạt kỹ năng"; break;
            }
            if (triggered.Ability.Condition == AbilityCondition.AllyDiedThisRound)
                context += " (đã có đồng minh chết trong vòng này)";
            foreach (var effect in triggered.Ability.Effects)
            {
                if (effect.Target == EffectTarget.WeakestEnemies) { context += " — chọn kẻ địch yếu nhất"; break; }
                if (effect.Target == EffectTarget.StrongestDeadAlly) { context += " — chọn đồng minh mạnh nhất trong nghĩa địa"; break; }
                if (effect.Target == EffectTarget.RandomDeadAlly) { context += " — chọn ngẫu nhiên từ đồng minh đã chết"; break; }
            }
            if (triggered.Ability.OncePerMatch) context += " (một lần trong trận)";
            return context;
        }

        // Journal-only effects do not enter the combat animation pipeline.
        private void RecordJournalCard(MatchState state, TriggeredAbility triggered, EffectKind kind,
            AbilityCardState before, AbilityCardState after, EffectDuration duration = EffectDuration.Permanent)
        {
            state.Journal.Ability(state.RoundNumber,
                new AbilityResolutionEvent(0, triggered.Ability.Id, kind, CaptureSource(state, triggered), before, after),
                JournalContext(state, triggered), duration);
        }
    }
}
