using System;

namespace KLTN.Game.Networking
{
    [Serializable]
    public sealed class CardViewDto
    {
        #region Identity

        public string instanceId;
        public string definitionId;
        public string displayName;

        #endregion

        #region Public Card State

        public int health;
        public int damage;
        public int energy;
        public int keywords;

        // Metadata used only to preview a Support buff while arranging attackers.
        public int supportDamageBonus;
        public int supportHealthBonus;

        // -1 while in hand, 0-2 while on the board.
        public int boardSlotIndex = -1;

        #endregion
    }

    [Serializable]
    public sealed class PlayerViewDto
    {
        #region Identity and Connection

        public int seat;
        public string displayName;
        public bool connected;

        #endregion

        #region Resources and Counts

        public int nexusHealth;
        public int mana;
        public int maxMana;
        public int deckCount;
        public int handCount;
        public int reserveCount;
        public int activeRosterCount;

        #endregion

        #region Viewer-Visible Cards

        public CardViewDto[] hand;
        public CardViewDto[] reserve;
        public CardViewDto[] board;
        public CardViewDto[] graveyard;

        #endregion
    }

    [Serializable]
    public sealed class AbilityTargetRequirementDto
    {
        public int slot;
        public int relation;
        public int zones;
        public int count;
        public bool excludeSource;

        public string[] validTargetIds;
        public string[] lethalTargetIds;
    }

    [Serializable]
    public sealed class PendingAbilitySelectionDto
    {
        public string requestId;
        public string sourceCardInstanceId;

        public int choosingSeat;
        public string abilityId;
        public bool canCancel;

        public AbilityTargetRequirementDto[] requirements;
    }

    [Serializable]
    public sealed class MatchSnapshotDto
    {
        #region Snapshot Identity

        public ulong revision;
        public int viewerSeat;

        #endregion

        #region Public Match State

        public int firstSeat;
        public int activeSeat;
        public int roundNumber;

        public int phase;
        public int attackTokenOwner;
        public bool attackTokenAvailable;
        public int consecutivePasses;

        public int outcome;

        // Economy: stable ID for reward idempotency, and who surrendered (-1 = nobody).
        public string matchId;
        public int surrenderedSeat = -1;

        // Ranking: whether this match changes season points, and when the server started it
        // (Unix ms, UTC) so clients can apply the end-of-season points lock.
        public bool isRanked;
        public long matchStartedUtcMs;

        // FR-NET-04/06: SeatConnectionState of both seats (0 empty, 1 connected,
        // 2 disconnected and held, 3 abandoned). Commands are refused while paused.
        public int selfConnection;
        public int opponentConnection;
        public bool isPaused;

        // How long the opponent's seat is still held: server clock deadline (Unix ms) and the
        // remaining time when the snapshot was built, which clients count down locally.
        public long opponentReconnectDeadlineUtcMs;
        public long opponentReconnectRemainingMs;

        public bool viewerCanAct;
        public bool viewerCanEndRound;
        public bool viewerCanDeclareAttack;
        public bool viewerCanDeclareBlock;
        public bool viewerMustSelectAbilityTargets;

        public PendingAbilitySelectionDto pendingAbilitySelection;

        #endregion

        #region Viewer Projection

        public PlayerViewDto self;
        public PlayerViewDto opponent;
        public string status;

        #endregion
    }

    [Serializable]
    public sealed class ResolvedCardDto
    {
        #region Card Identity and State

        public int seat;
        public CardViewDto cardBefore;
        public int healthAfter;
        public int damageTaken;

        public bool died;
        public bool diedFromEphemeral;

        #endregion
    }

    [Serializable]
    public sealed class CombatStepDto
    {
        #region Slot

        public int slotIndex;

        #endregion

        #region Cards

        public ResolvedCardDto hostCard;
        public ResolvedCardDto guestCard;

        #endregion

        #region Nexus Health

        public int hostNexusHealthBefore;
        public int hostNexusHealthAfter;
        public int guestNexusHealthBefore;
        public int guestNexusHealthAfter;

        #endregion
    }

    [Serializable]
    public sealed class RoundResolutionDto
    {
        #region Round Resolution

        public int roundNumber;
        public CombatStepDto[] steps;

        #endregion
    }

    [Serializable]
    public sealed class RoundTransitionDto
    {
        public int completedRoundNumber;
        public int nextRoundNumber;
        public int nextAttackTokenOwner;
    }

    [Serializable]
    public sealed class AbilityResolutionEventDto
    {
        public long sequence;
        public string abilityId;
        public int effectKind;
        public bool isRoundStartPassive;
        public CardViewDto source;
        public CardViewDto targetBefore;
        public CardViewDto targetAfter;
        public int sourceOwner = -1;
        public int sourceZone = -1;
        public int targetOwner = -1;
        public int targetZoneBefore = -1;
        public int targetZoneAfter = -1;
        public bool hasNexusTarget;
        public int nexusOwner = -1;
        public int nexusHealthBefore;
        public int nexusHealthAfter;
        public int reviveDamageBonus;
        public int reviveHealthBonus;
    }

    [Serializable]
    public sealed class AbilityResolutionDto
    {
        public AbilityResolutionEventDto[] events;
    }

    public enum MatchSettlementState
    {
        // Dedicated server is reporting the result to the backend.
        Saving = 0,
        Saved = 1,
        Failed = 2,

        // Listen-host (Host/Join): no rewards or season points.
        PrivateMatch = 3,
    }

    /// <summary>
    /// Rewards and season points the backend applied to one player, forwarded by the
    /// dedicated server (FR-SHOP-08/09, FR-RANK-10/11). Clients only display it.
    /// </summary>
    [Serializable]
    public sealed class MatchSettlementDto
    {
        public string matchId;

        // MatchSettlementState.
        public int state;
        public string message;

        // "win", "loss", "draw", "surrender_early" or "surrender".
        public string result;
        public int coins;
        public int silver;

        public bool ranked;
        public int rankDelta;
        public int pointsBefore;
        public int pointsAfter;
        public int tierBefore;
        public int tierAfter;
        public string notScoredReason;
    }

    [Serializable]
    public sealed class MatchUpdateDto
    {
        #region Authoritative Update

        public MatchSnapshotDto snapshot;

        public bool hasResolution;
        public RoundResolutionDto resolution;

        public bool hasRoundTransition;
        public RoundTransitionDto roundTransition;

        public bool hasAbilityResolution;
        public AbilityResolutionDto abilityResolution;

        #endregion
    }
}
