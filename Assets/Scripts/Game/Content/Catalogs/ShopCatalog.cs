using System;
using System.Collections.Generic;
using KLTN.Game.Domain.Economy;
using UnityEngine;

namespace KLTN.Game.Content
{
    [Serializable]
    public sealed class ShopCatalogEntry
    {
        [Tooltip("Card asset name in Resources/Cards (also the CardDefinition ID).")]
        public string cardId;

        [Tooltip("None = starter card (given on account creation, not for sale).")]
        public Currency currency = Currency.None;

        [Min(0)]
        public int price;

        public CardTier tier = CardTier.Weak;

        [Tooltip("Power score from the balancing sheet. Informational only.")]
        public float powerScore;

        public ShopEntry ToDomain()
        {
            return new ShopEntry(cardId, currency, price, tier, powerScore);
        }
    }

    /// <summary>
    /// Single source of truth for starter cards, shop prices and deck weight tiers.
    /// Lives in Resources so both clients and the match server can load it.
    /// The same data is seeded into Firestore /shopCatalog for Security Rules.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "KLTN/Shop Catalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        public const string ResourcesPath = "ShopCatalog";

        [SerializeField]
        private List<ShopCatalogEntry> entries = new List<ShopCatalogEntry>();

        private Dictionary<string, ShopEntry> byId;

        public IReadOnlyList<ShopCatalogEntry> RawEntries => entries;

        public static ShopCatalog Load()
        {
            ShopCatalog catalog = Resources.Load<ShopCatalog>(ResourcesPath);

            if (catalog == null)
            {
                Debug.LogError(
                    "[ShopCatalog] Missing Resources/ShopCatalog.asset. "
                        + "Run Tools/KLTN/Install Shop Catalog."
                );
            }

            return catalog;
        }

        public IEnumerable<ShopEntry> All
        {
            get
            {
                EnsureIndex();
                return byId.Values;
            }
        }

        public bool TryGet(string cardId, out ShopEntry entry)
        {
            EnsureIndex();
            entry = null;
            return !string.IsNullOrEmpty(cardId) && byId.TryGetValue(cardId, out entry);
        }

        public List<string> StarterCardIds()
        {
            var ids = new List<string>();

            foreach (ShopEntry entry in All)
            {
                if (entry.IsStarter)
                {
                    ids.Add(entry.CardId);
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>Deck weight used by WeightedDeckBuilder. Unknown cards count as Mid.</summary>
        public int WeightOf(string cardId)
        {
            return TryGet(cardId, out ShopEntry entry)
                ? CardTierWeights.Of(entry.Tier)
                : CardTierWeights.Of(CardTier.Mid);
        }

        /// <summary>
        /// Power score used by WeightedDeckBuilder to break copy ties (weaker card first).
        /// Unknown cards count as the strongest, so they never win a tie.
        /// </summary>
        public float PowerOf(string cardId)
        {
            return TryGet(cardId, out ShopEntry entry) ? entry.PowerScore : float.MaxValue;
        }

#if UNITY_EDITOR
        public void ReplaceEntries(List<ShopCatalogEntry> newEntries)
        {
            entries = newEntries;
            byId = null;
        }

        private void OnValidate()
        {
            byId = null;
        }
#endif

        private void EnsureIndex()
        {
            if (byId != null)
            {
                return;
            }

            byId = new Dictionary<string, ShopEntry>(StringComparer.Ordinal);

            foreach (ShopCatalogEntry raw in entries)
            {
                if (raw == null || string.IsNullOrWhiteSpace(raw.cardId) || byId.ContainsKey(raw.cardId))
                {
                    continue;
                }

                try
                {
                    byId.Add(raw.cardId, raw.ToDomain());
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[ShopCatalog] Invalid entry '{raw.cardId}': {exception.Message}", this);
                }
            }
        }
    }
}
