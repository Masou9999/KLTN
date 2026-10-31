using System;
using System.Collections;
using System.Collections.Generic;
using CMCMProductions;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Match Initialization

        private bool TryInitializeMatch()
        {
            if (matchState != null)
            {
                return false;
            }

            if (!HasSeat(SeatId.Host) || !HasSeat(SeatId.Guest))
            {
                return false;
            }

            if (!AreLoadoutsReady())
            {
                return false;
            }

            Card[] cardAssets = Resources.LoadAll<Card>("Cards");

            if (cardAssets == null || cardAssets.Length == 0)
            {
                Debug.LogError("No Card assets were found in Resources/Cards.");

                return false;
            }

            definitionsById.Clear();

            try
            {
                var definitions = new List<CardDefinition>();

                foreach (Card cardAsset in cardAssets)
                {
                    if (cardAsset == null)
                    {
                        continue;
                    }

                    CardDefinition definition = CardDefinitionMapper.Create(cardAsset);

                    if (definitionsById.ContainsKey(definition.Id))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate card definition ID: {definition.Id}"
                        );
                    }

                    definitionsById.Add(definition.Id, definition);

                    definitions.Add(definition);
                }

                CardContentValidator.Validate(definitions);

                definitions.Sort(
                    (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id)
                );

                IReadOnlyList<CardDefinition> hostDeck = BuildDeckForSeat(SeatId.Host);
                IReadOnlyList<CardDefinition> guestDeck = BuildDeckForSeat(SeatId.Guest);

                IRandomSource random = new SeededRandomSource(Environment.TickCount);

                SeatId firstSeat = random.Next(0, 2) == 0 ? SeatId.Host : SeatId.Guest;

                var factory = new MatchFactory(random);

                matchState = factory.Create(
                    hostDeck,
                    guestDeck,
                    OpeningHandSize,
                    firstSeat
                );

                // A dedicated server reports under the allocator's MATCH_ID so the backend can
                // match it to matches/{matchId}; listen-host matches get a local ID.
                currentMatchId = MatchServerContext.IsDedicated
                    ? MatchServerContext.MatchId
                    : Guid.NewGuid().ToString("N");
                currentMatchStartedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Dedicated matches are ranked when the backend reporter is configured.
                // Listen-host (Host/Join) matches are private and never scored; the
                // RankingSettings flag only marks them as ranked in the HUD for dev testing.
                currentMatchRanked = MatchServerContext.IsDedicated
                    ? MatchServerContext.Reporter.IsRanked
                    : RankingSettings.Load().HostClientPointsEnabled;

                rulesEngine = new MatchRulesEngine(definitionsById, random);

                snapshotBuilder = new MatchSnapshotBuilder(definitionsById);

                resolutionDtoMapper = new RoundResolutionDtoMapper(definitionsById);

                Debug.Log(
                    $"Match initialized. "
                        + $"MatchId: {currentMatchId}. "
                        + $"Ranked: {currentMatchRanked}. "
                        + $"First seat: {matchState.FirstSeat}. "
                        + $"Deck size: {DeckRules.RequiredCardCount}. "
                        + $"Host hand: {matchState.Host.DrawHand.Count}, "
                        + $"Guest hand: {matchState.Guest.DrawHand.Count}."
                );

                return true;
            }
            catch (Exception exception)
            {
                matchState = null;
                rulesEngine = null;
                snapshotBuilder = null;
                resolutionDtoMapper = null;

                definitionsById.Clear();

                Debug.LogError("Match initialization failed: " + exception.Message);

                return false;
            }
        }

        #endregion

        #region Match Update Publication

        private void BroadcastUpdate(
            RoundResolution resolution = null,
            RoundTransition roundTransition = null
        )
        {
            if (!IsServer)
            {
                return;
            }

            revision++;

            RoundResolutionDto resolutionDto =
                resolution == null
                    ? null
                    : resolutionDtoMapper.Build(resolution, matchState);

            AbilityResolution abilityResolution = matchState?.TakeAbilityResolution();
            var abilityMapper = new AbilityResolutionDtoMapper(definitionsById);

            RoundTransitionDto roundTransitionDto = BuildRoundTransitionDto(
                roundTransition
            );

            foreach (KeyValuePair<ulong, SeatId> pair in seatByClient)
            {
                AbilityResolutionDto abilityDto = abilityMapper.Build(
                    abilityResolution,
                    pair.Value
                );

                SendUpdate(
                    pair.Key,
                    pair.Value,
                    resolutionDto,
                    roundTransitionDto,
                    abilityDto
                );
            }
        }

        private void SendUpdate(
            ulong targetClientId,
            SeatId viewerSeat,
            RoundResolutionDto resolution,
            RoundTransitionDto roundTransition = null,
            AbilityResolutionDto abilityResolution = null
        )
        {
            bool isVisible = NetworkObject.IsNetworkVisibleTo(targetClientId);

            Debug.Log(
                $"Preparing match update. Target={targetClientId}, "
                    + $"Viewer={viewerSeat}, Visible={isVisible}, Revision={revision}."
            );

            if (!isVisible)
            {
                QueueSnapshotWhenVisible(targetClientId);
                return;
            }

            var update = new MatchUpdateDto
            {
                snapshot = BuildSnapshot(viewerSeat),

                hasResolution = resolution != null,

                resolution = resolution,

                hasRoundTransition = roundTransition != null,

                roundTransition = roundTransition,

                hasAbilityResolution = abilityResolution != null,
                abilityResolution = abilityResolution,
            };

            string json = JsonUtility.ToJson(update);

            Debug.Log(
                $"Sending match update through Universal RPC. "
                    + $"Target={targetClientId}, JsonLength={json.Length}."
            );

            ReceiveMatchUpdateRpc(
                json,
                RpcTarget.Single(targetClientId, RpcTargetUse.Temp)
            );
        }

        private MatchSnapshotDto BuildSnapshot(SeatId viewerSeat)
        {
            SeatId opponentSeat = viewerSeat.Opponent();
            bool opponentConnected = HasSeat(opponentSeat);

            if (matchState == null || snapshotBuilder == null)
            {
                return MatchSnapshotBuilder.BuildWaiting(
                    viewerSeat,
                    opponentConnected,
                    revision
                );
            }

            MatchSnapshotDto snapshot = snapshotBuilder.Build(
                matchState,
                viewerSeat,
                opponentConnected,
                revision
            );

            snapshot.matchId = currentMatchId;
            snapshot.isRanked = currentMatchRanked;
            snapshot.matchStartedUtcMs = currentMatchStartedUtcMs;
            return snapshot;
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveMatchUpdateRpc(string json, RpcParams rpcParams = default)
        {
            Debug.Log(
                $"Universal match update arrived. "
                    + $"ClientId={NetworkManager.LocalClientId}, JsonLength={json?.Length ?? 0}."
            );

            MatchUpdateDto update = JsonUtility.FromJson<MatchUpdateDto>(json);

            if (update?.snapshot == null)
            {
                Debug.LogWarning("Received an invalid match update.");
                return;
            }

            hasReceivedSnapshot = true;
            MatchUpdateInboxRegistry.Current.Enqueue(update);

            Debug.Log(
                $"Received match update. "
                    + $"Viewer={update.snapshot.viewerSeat}, "
                    + $"Self={update.snapshot.self.seat}, "
                    + $"Opponent={update.snapshot.opponent.seat}, "
                    + $"Revision={update.snapshot.revision}, "
                    + $"HasResolution={update.hasResolution}."
            );
        }

        #endregion
    }
}
