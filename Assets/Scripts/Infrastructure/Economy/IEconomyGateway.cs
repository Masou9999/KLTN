using System.Collections.Generic;
using System.Threading.Tasks;
using KLTN.Game.Domain.Economy;

namespace KLTN.Infrastructure.Economy
{
    public sealed class PlayerProfile
    {
        public string UserId { get; }

        /// <summary>Public unique name "userXXXXXX"; null only if claiming it failed.</summary>
        public string Username { get; }
        public Wallet Wallet { get; }
        public IReadOnlyCollection<string> OwnedCardIds => owned;

        private readonly HashSet<string> owned;

        public PlayerProfile(string userId, string username, Wallet wallet, IEnumerable<string> ownedCardIds)
        {
            UserId = userId;
            Username = username;
            Wallet = wallet;
            owned = new HashSet<string>(ownedCardIds ?? new string[0], System.StringComparer.Ordinal);
        }

        public bool Owns(string cardId)
        {
            return cardId != null && owned.Contains(cardId);
        }
    }

    public enum EconomyErrorCode
    {
        None = 0,
        NotSignedIn,
        Network,
        PermissionDenied,
        Rejected,
        Unknown,
    }

    public sealed class EconomyResult
    {
        public bool Success { get; private set; }
        public EconomyErrorCode Error { get; private set; }
        public string Message { get; private set; }
        public PlayerProfile Profile { get; private set; }

        /// <summary>True when a reward for this match had already been claimed.</summary>
        public bool AlreadyApplied { get; private set; }

        public static EconomyResult Ok(PlayerProfile profile, bool alreadyApplied = false)
        {
            return new EconomyResult { Success = true, Profile = profile, AlreadyApplied = alreadyApplied };
        }

        public static EconomyResult Fail(EconomyErrorCode error, string message)
        {
            return new EconomyResult { Success = false, Error = error, Message = message };
        }
    }

    /// <summary>
    /// Persistence boundary for currencies and owned cards.
    /// Match rewards are written only by Cloud Code (ReportResult from the dedicated server);
    /// the client can read the wallet but has no reward write path (FR-SHOP-09).
    /// </summary>
    public interface IEconomyGateway
    {
        Task<EconomyResult> LoadOrCreateProfileAsync(IReadOnlyList<string> starterCardIds);
        Task<EconomyResult> PurchaseAsync(ShopEntry entry);
    }
}
