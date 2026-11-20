using System;

namespace KLTN.Game.Networking
{
    /// <summary>Public identity only; never includes account IDs, wallets or loadouts.</summary>
    [Serializable]
    public sealed class PlayerProfileDto
    {
        public string username;
        // -1 means unavailable, including responses from an older backend.
        public int points = -1;
        public int seasonId;
    }
}
