using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Owns one player's public resources and card zones. Zone transitions are kept here
    /// so callers cannot update a collection without updating the card's zone metadata.
    /// </summary>
    public sealed class PlayerState
    {
        #region Properties and Collections

        public SeatId Seat { get; }

        /// <summary>Timeouts on this player's successive decisions, preserved across rounds.</summary>
        public int ConsecutiveTimeouts { get; internal set; }

        public int NexusHealth { get; private set; } = 20;
        public int MaxMana { get; private set; }
        public int Mana { get; private set; }

        public List<CardInstance> Deck { get; } = new List<CardInstance>();

        public List<CardInstance> DrawHand { get; } = new List<CardInstance>();

        public List<CardInstance> Reserve { get; } = new List<CardInstance>();

        public List<CardInstance> Board { get; } = new List<CardInstance>();

        public List<CardInstance> Graveyard { get; } = new List<CardInstance>();

        public int ActiveRosterCount => Reserve.Count + Board.Count;

        public bool HasActiveRosterSpace =>
            ActiveRosterCount < MatchState.MaximumActiveRosterSize;

        #endregion

        #region Construction

        public PlayerState(SeatId seat)
        {
            Seat = seat;
        }

        #endregion

        #region Resources and Health

        public void InitializeMana(int amount)
        {
            MaxMana = Math.Max(0, amount);
            Mana = MaxMana;
        }

        public void AdvanceAndRefillMana(int maximumMana)
        {
            MaxMana = Math.Min(maximumMana, MaxMana + 1);
            Mana = MaxMana;
        }

        public bool TrySpendMana(int amount)
        {
            if (amount < 0 || Mana < amount)
            {
                return false;
            }

            Mana -= amount;
            return true;
        }

        public void ApplyNexusDamage(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            NexusHealth = Math.Max(0, NexusHealth - amount);
        }

        public void HealNexus(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            NexusHealth = Math.Min(20, NexusHealth + amount);
        }

        #endregion

        #region Card Queries and Zone Transitions

        public CardInstance FindCardInDrawHand(ulong instanceId)
        {
            return DrawHand.Find(card => card.InstanceId == instanceId);
        }

        public CardInstance FindCardInReserve(ulong instanceId)
        {
            return Reserve.Find(card => card.InstanceId == instanceId);
        }

        public CardInstance FindBoardCard(int slotIndex)
        {
            return Board.Find(card => card.BoardSlotIndex == slotIndex);
        }

        /// <summary>
        /// Finds an instance across every authoritative zone, including Graveyard. This is
        /// intended for host-side resolution and must not be used to reveal hidden cards.
        /// </summary>
        public CardInstance FindCardAnywhere(ulong instanceId)
        {
            CardInstance card = Deck.Find(item => item.InstanceId == instanceId);

            if (card != null)
            {
                return card;
            }

            card = DrawHand.Find(item => item.InstanceId == instanceId);

            if (card != null)
            {
                return card;
            }

            card = Reserve.Find(item => item.InstanceId == instanceId);

            if (card != null)
            {
                return card;
            }

            card = Board.Find(item => item.InstanceId == instanceId);

            if (card != null)
            {
                return card;
            }

            return Graveyard.Find(item => item.InstanceId == instanceId);
        }

        public bool CanAddToActiveRoster(int incomingCount = 1)
        {
            if (incomingCount < 0)
            {
                return false;
            }

            return ActiveRosterCount + incomingCount
                <= MatchState.MaximumActiveRosterSize;
        }

        public bool TryMoveDrawHandCardToReserve(CardInstance card)
        {
            if (
                card == null
                || card.Owner != Seat
                || card.Zone != CardZone.DrawHand
                || card.IsDead
                || !DrawHand.Contains(card)
            )
            {
                return false;
            }

            if (!CanAddToActiveRoster())
            {
                return false;
            }

            DrawHand.Remove(card);
            card.MoveTo(CardZone.Reserve);
            Reserve.Add(card);

            return true;
        }

        public bool TrySummonDrawHandCard(CardInstance card, int manaCost)
        {
            if (manaCost < 0 || Mana < manaCost)
            {
                return false;
            }

            if (!TryMoveDrawHandCardToReserve(card))
            {
                return false;
            }

            Mana -= manaCost;
            return true;
        }

        public bool TryAddCreatedCardToDrawHand(CardInstance card)
        {
            if (
                card == null
                || card.Owner != Seat
                || card.Zone != CardZone.DrawHand
                || card.IsDead
                || FindCardAnywhere(card.InstanceId) != null
            )
            {
                return false;
            }

            DrawHand.Add(card);
            return true;
        }

        public bool TryAddCreatedCardToReserve(CardInstance card)
        {
            if (
                card == null
                || card.Owner != Seat
                || card.Zone != CardZone.Reserve
                || card.IsDead
                || FindCardAnywhere(card.InstanceId) != null
                || !HasActiveRosterSpace
            )
            {
                return false;
            }

            Reserve.Add(card);
            return true;
        }

        /// <summary>
        /// Revives a dead owned card into Reserve when active-roster capacity permits and
        /// resets its runtime stats to the supplied immutable definition.
        /// </summary>
        public bool TryReviveCardToReserve(CardInstance card, CardDefinition definition)
        {
            if (
                card == null
                || definition == null
                || card.Owner != Seat
                || card.Zone != CardZone.Graveyard
                || !card.IsDead
                || !Graveyard.Contains(card)
                || !string.Equals(
                    card.DefinitionId,
                    definition.Id,
                    StringComparison.Ordinal
                )
                || !HasActiveRosterSpace
            )
            {
                return false;
            }

            Graveyard.Remove(card);

            card.ResetToBase(definition);
            card.MoveTo(CardZone.Reserve);

            Reserve.Add(card);
            return true;
        }

        public bool TryReviveCardToBoard(
            CardInstance card,
            CardDefinition definition,
            int slotIndex
        )
        {
            return TryReviveCardToBoard(
                card,
                definition,
                slotIndex,
                requireActiveRosterSpace: true
            );
        }

        /// <summary>
        /// Revives a unit directly into an empty attack slot for a triggered attack.
        /// This special transition may temporarily exceed the active-roster cap because
        /// the unit participates only in the current combat; Board capacity still applies.
        /// </summary>
        internal bool TryReviveCardToBoardForTriggeredAttack(
            CardInstance card,
            CardDefinition definition,
            int slotIndex
        )
        {
            return TryReviveCardToBoard(
                card,
                definition,
                slotIndex,
                requireActiveRosterSpace: false
            );
        }

        private bool TryReviveCardToBoard(
            CardInstance card,
            CardDefinition definition,
            int slotIndex,
            bool requireActiveRosterSpace
        )
        {
            if (
                card == null
                || definition == null
                || card.Owner != Seat
                || card.Zone != CardZone.Graveyard
                || !card.IsDead
                || !Graveyard.Contains(card)
                || !string.Equals(
                    card.DefinitionId,
                    definition.Id,
                    StringComparison.Ordinal
                )
                || (requireActiveRosterSpace && !HasActiveRosterSpace)
            )
            {
                return false;
            }

            if (
                slotIndex < 0
                || slotIndex >= MatchState.BoardSlotCount
                || Board.Count >= MatchState.BoardSlotCount
                || FindBoardCard(slotIndex) != null
            )
            {
                return false;
            }

            Graveyard.Remove(card);

            card.ResetToBase(definition);
            card.MoveTo(CardZone.Board, slotIndex);

            Board.Add(card);
            return true;
        }

        public bool TryReturnCardToDrawHand(CardInstance card, CardDefinition definition)
        {
            if (
                card == null
                || definition == null
                || card.Owner != Seat
                || !string.Equals(
                    card.DefinitionId,
                    definition.Id,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            bool removed;

            switch (card.Zone)
            {
                case CardZone.Reserve:
                    removed = Reserve.Remove(card);
                    break;

                case CardZone.Board:
                    removed = Board.Remove(card);
                    break;

                case CardZone.Graveyard:
                    removed = Graveyard.Remove(card);
                    break;

                default:
                    return false;
            }

            if (!removed)
            {
                return false;
            }

            card.ResetToBase(definition);
            card.MoveTo(CardZone.DrawHand);

            DrawHand.Add(card);
            return true;
        }

        public bool TryMoveReserveCardToBoard(CardInstance card, int slotIndex)
        {
            if (
                card == null
                || card.Owner != Seat
                || card.Zone != CardZone.Reserve
                || card.IsDead
                || !Reserve.Contains(card)
            )
            {
                return false;
            }

            if (slotIndex < 0 || slotIndex >= MatchState.BoardSlotCount)
            {
                return false;
            }

            if (
                Board.Count >= MatchState.BoardSlotCount
                || FindBoardCard(slotIndex) != null
            )
            {
                return false;
            }

            Reserve.Remove(card);
            card.MoveTo(CardZone.Board, slotIndex);
            Board.Add(card);

            return true;
        }

        public bool TryMoveBoardCardToReserve(CardInstance card)
        {
            if (
                card == null
                || card.Owner != Seat
                || card.Zone != CardZone.Board
                || card.IsDead
                || !Board.Contains(card)
            )
            {
                return false;
            }

            if (Reserve.Count >= MatchState.MaximumActiveRosterSize)
            {
                return false;
            }

            Board.Remove(card);
            card.MoveTo(CardZone.Reserve);
            Reserve.Add(card);

            return true;
        }

        /// <summary>
        /// Returns all surviving combat participants to Reserve before priority resumes.
        /// </summary>
        public void ReturnBoardCardsToReserve()
        {
            // Dead cards must never return to Reserve.
            MoveDeadBoardCardsToGraveyard();

            // Preserve the visual left-to-right board order.
            Board.Sort(
                (left, right) => left.BoardSlotIndex.CompareTo(right.BoardSlotIndex)
            );

            while (Board.Count > 0)
            {
                CardInstance card = Board[0];

                Board.RemoveAt(0);
                card.MoveTo(CardZone.Reserve);
                Reserve.Add(card);
            }
        }

        public bool DrawOne()
        {
            if (Deck.Count == 0)
            {
                return false;
            }

            int topIndex = Deck.Count - 1;
            CardInstance card = Deck[topIndex];

            Deck.RemoveAt(topIndex);
            card.MoveTo(CardZone.DrawHand);
            DrawHand.Add(card);

            return true;
        }

        /// <summary>
        /// Moves every dead board card to Graveyard in stable instance order and clears
        /// temporary round modifiers before death history is recorded.
        /// </summary>
        public IReadOnlyList<CardInstance> MoveDeadBoardCardsToGraveyard()
        {
            var deadCards = new List<CardInstance>();

            foreach (CardInstance card in Board)
            {
                if (card.IsDead)
                {
                    deadCards.Add(card);
                }
            }

            deadCards.Sort(
                (left, right) => left.BoardSlotIndex.CompareTo(right.BoardSlotIndex)
            );

            foreach (CardInstance card in deadCards)
            {
                Board.Remove(card);

                card.ClearRoundModifiersOnDeath();
                card.MoveTo(CardZone.Graveyard);

                Graveyard.Add(card);
            }

            return deadCards;
        }

        public bool TryMoveDeadActiveCardToGraveyard(CardInstance card)
        {
            if (card == null || card.Owner != Seat || !card.IsDead)
            {
                return false;
            }

            bool removed;

            switch (card.Zone)
            {
                case CardZone.Reserve:
                    removed = Reserve.Remove(card);
                    break;

                case CardZone.Board:
                    removed = Board.Remove(card);
                    break;

                default:
                    return false;
            }

            if (!removed)
            {
                return false;
            }

            card.ClearRoundModifiersOnDeath();
            card.MoveTo(CardZone.Graveyard);

            Graveyard.Add(card);

            return true;
        }

        #endregion

        #region Mulligan Logic

        /// <summary>
        /// Replaces selected opening cards atomically: selected instances leave the draw
        /// pool, replacements are drawn, then returned instances are shuffled back.
        /// </summary>
        public void PerformMulligan(ulong[] cardIdsToReplace, Random rng)
        {
            if (cardIdsToReplace == null || cardIdsToReplace.Length == 0)
                return;

            int replaceCount = 0;

            for (int i = DrawHand.Count - 1; i >= 0; i--)
            {
                bool shouldReplace = Array.Exists(
                    cardIdsToReplace,
                    id => id == DrawHand[i].InstanceId
                );

                if (!shouldReplace)
                {
                    continue;
                }

                CardInstance card = DrawHand[i];

                DrawHand.RemoveAt(i);
                card.MoveTo(CardZone.Deck);
                Deck.Add(card);
                replaceCount++;
            }

            ShuffleDeck(rng);

            for (int i = 0; i < replaceCount; i++)
            {
                DrawOne();
            }
        }

        private void ShuffleDeck(Random rng)
        {
            int n = Deck.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                CardInstance value = Deck[k];
                Deck[k] = Deck[n];
                Deck[n] = value;
            }
        }

        #endregion
    }
}
