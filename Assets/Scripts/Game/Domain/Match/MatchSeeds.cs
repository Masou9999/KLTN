namespace KLTN.Game.Domain
{
    /// <summary>
    /// Derives the independent random streams of one match from a single main seed, so
    /// the same main seed replays the same draws and random effects. Host and guest draw
    /// from different streams, so equal decks still produce different hands.
    /// </summary>
    public readonly struct MatchSeeds
    {
        public int Main { get; }

        public int HostDraw => unchecked(Main + 1);
        public int GuestDraw => unchecked(Main + 2);

        /// <summary>First-seat choice and random ability effects.</summary>
        public int Effects => unchecked(Main + 3);

        public MatchSeeds(int main)
        {
            Main = main;
        }
    }
}
