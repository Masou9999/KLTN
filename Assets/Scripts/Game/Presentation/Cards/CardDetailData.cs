using KLTN.Game.Domain;

namespace KLTN.Game.Presentation
{
    /// <summary>Display snapshot shared by live match cards and owned-card previews.</summary>
    public readonly struct CardDetailData
    {
        public string Name { get; }
        public int Energy { get; }
        public int Damage { get; }
        public int Health { get; }
        public CardArtworkView Artwork { get; }
        public string Rules { get; }

        /// <summary>Passive keywords listed under [Nội tại] in the detail modal.</summary>
        public UnitKeyword Keywords { get; }

        public CardDetailData(string name, int energy, int damage, int health,
            CardArtworkView artwork, string rules = null,
            UnitKeyword keywords = UnitKeyword.None)
        {
            Name = name ?? string.Empty;
            Energy = energy;
            Damage = damage;
            Health = health;
            Artwork = artwork;
            Rules = rules;
            Keywords = keywords;
        }
    }
}
