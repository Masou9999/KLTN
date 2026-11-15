using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public enum MatchLogCategory { Ability, Combat }

    /// <summary>Viewer-specific text is projected on the server; hidden identities never cross the wire.</summary>
    public sealed class MatchLogEntry
    {
        public long Sequence { get; }
        public int Round { get; }
        public MatchLogCategory Category { get; }
        private readonly string hostText;
        private readonly string guestText;

        public MatchLogEntry(long sequence, int round, MatchLogCategory category, string host, string guest)
        { Sequence = sequence; Round = round; Category = category; hostText = host; guestText = guest; }

        public string TextFor(SeatId viewer) => viewer == SeatId.Host ? hostText : guestText;
    }

    /// <summary>Append-only per-match history, independent of the disposable animation events.</summary>
    public sealed class MatchJournal
    {
        private readonly List<MatchLogEntry> entries = new List<MatchLogEntry>();
        private int publishedCount;
        public IReadOnlyList<MatchLogEntry> Entries => entries.AsReadOnly();

        public IReadOnlyList<MatchLogEntry> TakePending()
        {
            var pending = entries.GetRange(publishedCount, entries.Count - publishedCount);
            publishedCount = entries.Count;
            return pending;
        }

        public void Add(int round, MatchLogCategory category, Func<SeatId, string> format)
        {
            string host = format(SeatId.Host), guest = format(SeatId.Guest);
            if (string.IsNullOrEmpty(host) && string.IsNullOrEmpty(guest)) return;
            entries.Add(new MatchLogEntry(entries.Count + 1L, round, category, host, guest));
        }

        public static string Owner(SeatId owner, SeatId viewer) => owner == viewer ? "bạn" : "đối thủ";
        public static string Card(AbilityCardState card, SeatId viewer, bool publicIdentity = false)
        {
            if (card == null) return "kỹ năng";
            if (!publicIdentity && card.Owner != viewer &&
                (card.Zone == CardZone.DrawHand || card.Zone == CardZone.Deck))
                return "một lá bài của đối thủ";
            string place = card.Zone == CardZone.Board ? $" (ô {card.BoardSlotIndex + 1})" : "";
            return $"{card.DisplayName} của {Owner(card.Owner, viewer)}{place}";
        }

        public void Ability(int round, AbilityResolutionEvent e, string context = "", EffectDuration duration = EffectDuration.Permanent)
        {
            Add(round, MatchLogCategory.Ability, viewer =>
            {
                string source = Card(e.Source, viewer);
                var target = e.TargetBefore ?? e.TargetAfter;
                string name = Card(target, viewer);
                string prefix = source + context + ": ";
                if (e.IsRoundStartPassive)
                    return $"{name} hồi sinh về dự bị vào đầu vòng; chỉ số {e.TargetAfter.Damage}|{e.TargetAfter.Health}. {e.JournalContext}";
                if (e.NexusOwner.HasValue)
                {
                    int delta = e.NexusHealthAfter - e.NexusHealthBefore;
                    if (e.Kind == EffectKind.HalfNexus)
                        return $"{prefix}giảm một nửa máu của {Owner(e.NexusOwner.Value, viewer)} (làm tròn lên): {e.NexusHealthBefore} → {e.NexusHealthAfter}.";
                    string action = delta > 0 ? $"hồi {delta} máu cho" : $"gây {-delta} sát thương lên";
                    return $"{prefix}{action} {Owner(e.NexusOwner.Value, viewer)}: {e.NexusHealthBefore} → {e.NexusHealthAfter}.";
                }
                switch (e.Kind)
                {
                    case EffectKind.Kill:
                    case EffectKind.KillAllUnits:
                    case EffectKind.Sacrifice:
                        return $"{prefix}tiêu diệt {name}, chuyển vào nghĩa địa.";
                    case EffectKind.Damage:
                        return $"{prefix}gây {e.TargetBefore.Health - e.TargetAfter.Health} sát thương lên {name}: {e.TargetBefore.Health} → {e.TargetAfter.Health}." +
                            (e.TargetAfter.Health <= 0 ? " Lá bài bị tiêu diệt." : "");
                    case EffectKind.Heal:
                        return $"{prefix}hồi {e.TargetAfter.Health - e.TargetBefore.Health} máu cho {name}: {e.TargetBefore.Health} → {e.TargetAfter.Health}.";
                    case EffectKind.Buff:
                        return $"{prefix}tăng chỉ số {name}: {e.TargetBefore.Damage}|{e.TargetBefore.Health} → {e.TargetAfter.Damage}|{e.TargetAfter.Health}" +
                            (duration == EffectDuration.ThisRound ? " trong vòng này." : " lâu dài.");
                    case EffectKind.ReduceCost:
                        if (target.Owner != viewer && target.Zone == CardZone.DrawHand)
                            return $"{prefix}giảm năng lượng tiêu hao của một lá bài trên tay đối thủ.";
                        return $"{prefix}giảm năng lượng tiêu hao của {name}: {e.TargetBefore.Cost} → {e.TargetAfter.Cost}.";
                    case EffectKind.Revive:
                        return $"{prefix}hồi sinh {name} về dự bị; chỉ số {e.TargetAfter.Damage}|{e.TargetAfter.Health}.";
                    case EffectKind.ReturnToDrawHand:
                        return $"{prefix}{Card(target, viewer, true)} trở về tay chủ sở hữu.";
                    case EffectKind.ReturnToReserve:
                        return $"{prefix}{name} trở về dự bị.";
                    case EffectKind.Copy:
                        return $"{prefix}tạo bản sao {name} trên tay.";
                    case EffectKind.Draw:
                        return $"{prefix}rút {name}.";
                    case EffectKind.Summon:
                        return $"{prefix}triệu hồi {name} về dự bị.";
                    default: return null;
                }
            });
        }

        public void CombatDamage(int round, AbilityCardState source, AbilityCardState target, int before, int after, SeatId nexusOwner)
        {
            if (before == after) return;
            Add(round, MatchLogCategory.Combat, viewer =>
                $"{Card(source, viewer)} gây {before - after} sát thương lên " +
                (target == null ? Owner(nexusOwner, viewer) : Card(target, viewer)) +
                $": {before} → {after}." + (target != null && after <= 0 ? " Lá bài bị tiêu diệt, chuyển vào nghĩa địa." : ""));
        }

        public void Ephemeral(int round, AbilityCardState card, bool roundEnd)
        {
            Add(round, MatchLogCategory.Ability, v => $"{Card(card, v)} chết do hiệu ứng Phù Du " +
                (roundEnd ? "khi kết thúc vòng." : "sau khi ra đòn."));
        }

        public void PassiveCosts(MatchState state, IReadOnlyDictionary<string, CardDefinition> definitions, SeatId owner)
        {
            foreach (var card in state.Player(owner).DrawHand)
            {
                if (!definitions.TryGetValue(card.DefinitionId, out var definition)) continue;
                int reduction = definition.PassiveRules.CostReductionPerAlliedDeathThisGame;
                if (reduction <= 0) continue;
                int after = CardCostCalculator.GetEffectiveCost(state, card, definition);
                int deaths = state.RoundHistory.CountDeathsThisGame(owner);
                int before = Math.Max(0, card.GetCost(definition) - (deaths - 1) * reduction);
                if (before == after) continue;
                var captured = AbilityCardState.Capture(card, definition);
                Add(state.RoundNumber, MatchLogCategory.Ability, v => v != owner ? null :
                    $"Thêm một đồng minh chết: giảm năng lượng tiêu hao của {Card(captured, v)}: {before} → {after}.");
            }
        }
    }
}
