#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using KLTN.Game.Domain;
using KLTN.Game.Networking;

namespace KLTN.Sandbox
{
    /// <summary>Checks captured mana deltas and privacy without Unity or a transport.</summary>
    public static class ManaReductionProjectionChecks
    {
        public static void Validate(IReadOnlyDictionary<string, CardDefinition> definitions)
        {
            var deck = Enumerable.Repeat(definitions["MaLon"],
                DeckRules.RequiredCardCount).ToArray();
            var state = new MatchFactory(new SeededRandomSource(731))
                .Create(deck, deck, 0);
            state.Host.Deck.Clear();
            state.Guest.Deck.Clear();
            state.HostMulliganDone = state.GuestMulliganDone = true;
            state.BeginFirstRound();
            state.Host.InitializeMana(10);
            CardInstance source = Add(state, definitions["MaGa"], CardZone.DrawHand);
            CardInstance target = Add(state, definitions["MaTroi"], CardZone.DrawHand);
            CardInstance victim = Add(state, definitions["MaLon"], CardZone.Reserve);
            var rules = new MatchRulesEngine(definitions, new SeededRandomSource(731));
            Check(rules.TrySummonUnit(state, SeatId.Host, source.InstanceId).Accepted,
                "MaGa summon");
            Check(rules.TrySubmitAbilitySelection(state, SeatId.Host,
                state.PendingSelection.RequestId, new AbilityTargetSelection(
                    new[] { target.InstanceId }, null)).Accepted, "MaGa selection");
            AssertPrivate(state, definitions, "ma_ga_play", 10, 9, false);
            var ability = new AbilityDefinition("validation_kill", AbilityTrigger.Play,
                new[] { new EffectDefinition(EffectKind.Kill, EffectTarget.Source) });
            new AbilityEffectExecutor(definitions).Execute(state,
                new TriggeredAbility(victim.InstanceId, victim.Owner, ability,
                    GameEvent.FromCard(GameEventType.UnitPlayed, 1, victim)), null);
            AssertPrivate(state, definitions,
                CardCostCalculator.AlliedDeathReductionId, 9, 8, true);
            ValidateMultiStep(definitions, false);
            ValidateMultiStep(definitions, true);
        }

        private static void ValidateMultiStep(
            IReadOnlyDictionary<string, CardDefinition> definitions, bool revive)
        {
            var state = new MatchState();
            state.HostMulliganDone = state.GuestMulliganDone = true;
            state.BeginFirstRound();
            var source = Add(state, definitions["MaLon"], CardZone.Reserve);
            var ally = Add(state, definitions["MaLon"], CardZone.Reserve);
            Add(state, definitions["MaTroi"], CardZone.DrawHand);
            var enemy = new CardInstance(state.AllocateCardInstanceId(), "MaLon",
                SeatId.Guest, CardZone.Reserve, definitions["MaLon"].BaseHealth);
            state.Guest.Reserve.Add(enemy);
            var ability = new AbilityDefinition("two_step", AbilityTrigger.Play, new[]
            {
                new EffectDefinition(EffectKind.Kill, EffectTarget.PrimarySelection),
                new EffectDefinition(revive ? EffectKind.Revive : EffectKind.Kill,
                    revive ? EffectTarget.PrimarySelection : EffectTarget.SecondarySelection),
            });
            new AbilityEffectExecutor(definitions).Execute(state,
                new TriggeredAbility(source.InstanceId, SeatId.Host, ability,
                    GameEvent.FromCard(GameEventType.UnitPlayed, 1, source)),
                new AbilityTargetSelection(new[] { ally.InstanceId },
                    new[] { enemy.InstanceId }));
            var resolution = state.TakeAbilityResolution();
            var mapper = new AbilityResolutionDtoMapper(definitions);
            foreach (SeatId viewer in new[] { SeatId.Host, SeatId.Guest })
            {
                var events = mapper.Build(resolution, viewer).events;
                Check(events.Length == (viewer == SeatId.Host ? 3 : 2),
                    "Owner sees deferred hand delta; opponent sees only the complete ability");
                Check(events[0].abilityId == "two_step" && events[1].abilityId == "two_step"
                    && events[0].executionId > 0
                    && events[0].executionId == events[1].executionId,
                    "DTO retains contiguous execution identity for both perspectives");
                if (viewer == SeatId.Host)
                    Check(events[2].abilityId == CardCostCalculator.AlliedDeathReductionId
                        && events[2].targetBefore.energy == 10
                        && events[2].targetAfter.energy == 9,
                        "Passive follows the complete ability with captured delta");
            }
        }

        private static CardInstance Add(MatchState state, CardDefinition definition,
            CardZone zone)
        {
            var card = new CardInstance(state.AllocateCardInstanceId(), definition.Id,
                SeatId.Host, zone, definition.BaseHealth);
            if (zone == CardZone.DrawHand) state.Host.DrawHand.Add(card);
            else state.Host.Reserve.Add(card);
            return card;
        }

        private static void AssertPrivate(MatchState state,
            IReadOnlyDictionary<string, CardDefinition> definitions, string abilityId,
            int before, int after, bool hiddenJournal)
        {
            var mapper = new AbilityResolutionDtoMapper(definitions);
            AbilityResolution resolution = state.TakeAbilityResolution();
            AbilityResolutionEventDto owner = mapper.Build(resolution, SeatId.Host)
                .events.Single(e => e.abilityId == abilityId);
            Check(owner.targetBefore.energy == before && owner.targetAfter.energy == after,
                "DTO uses captured effective cost rather than printed cost");
            AbilityResolutionDto opponent = mapper.Build(resolution, SeatId.Guest);
            Check(opponent == null || opponent.events.All(e => e.abilityId != abilityId),
                "Opponent receives no hidden target identity or cost event");
            string privateName = definitions["MaTroi"].DisplayName;
            Check(MatchJournalProjection.Build(state.Journal.Entries, SeatId.Host)
                .Any(e => e.text.Contains(privateName)), "Owner journal has target identity");
            Check(MatchJournalProjection.Build(state.Journal.Entries, SeatId.Guest)
                .All(e => !e.text.Contains(privateName)), "Opponent journal hides identity");
            if (hiddenJournal)
                Check(state.Journal.Entries.Any(e =>
                    e.TextFor(SeatId.Host)?.Contains("9 → 8") == true
                    && e.TextFor(SeatId.Guest) == null),
                    "Passive journal event exists only for owner");
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
#endif
