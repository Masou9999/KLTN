using System;
using System.Collections;
using System.Collections.Generic;
using CMCMProductions;
using KLTN.Game.ApplicationLayer;
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

        private bool TryInitializeMatch(MatchSession session)
        {
            if (session == null || session.IsStarted || session.IsClosed)
            {
                return false;
            }

            if (!session.HasConnectedSeat(SeatId.Host) || !session.HasConnectedSeat(SeatId.Guest))
            {
                return false;
            }

            if (!AreLoadoutsReady(session))
            {
                return false;
            }

            if (!TryLoadDefinitions())
            {
                return false;
            }

            try
            {
                IReadOnlyList<CardDefinition> hostDeck = BuildDeckForSeat(session, SeatId.Host);
                IReadOnlyList<CardDefinition> guestDeck = BuildDeckForSeat(session, SeatId.Guest);

                IRandomSource random = new SeededRandomSource(Environment.TickCount ^ session.MatchId.GetHashCode());

                SeatId firstSeat = random.Next(0, 2) == 0 ? SeatId.Host : SeatId.Guest;

                var factory = new MatchFactory(random);

                MatchState state = factory.Create(
                    hostDeck,
                    guestDeck,
                    OpeningHandSize,
                    firstSeat
                );

                // Dedicated matches are ranked when the backend reporter is configured; they
                // report under the allocator's match ID so the backend finds matches/{matchId}.
                // Listen-host (Host/Join) matches are private and never scored; the
                // RankingSettings flag only marks them as ranked in the HUD for dev testing.
                bool ranked = MatchServerContext.IsDedicated
                    ? MatchServerContext.Reporter.IsRanked
                    : RankingSettings.Load().HostClientPointsEnabled;

                session.Start(
                    state,
                    new MatchRulesEngine(definitionsById, random),
                    ranked,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );

                Debug.Log(
                    $"Match initialized. "
                        + $"MatchId: {session.MatchId}. "
                        + $"Ranked: {session.IsRanked}. "
                        + $"First seat: {state.FirstSeat}. "
                        + $"Deck size: {DeckRules.RequiredCardCount}. "
                        + $"Host hand: {state.Host.DrawHand.Count}, "
                        + $"Guest hand: {state.Guest.DrawHand.Count}. "
                        + $"Matches on server: {sessions.Count}/{sessions.Capacity}."
                );

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Match {session.MatchId} initialization failed: " + exception.Message);

                return false;
            }
        }

        /// <summary>Card definitions are loaded once and shared by every match of the process.</summary>
        private bool TryLoadDefinitions()
        {
            if (snapshotBuilder != null)
            {
                return true;
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

                snapshotBuilder = new MatchSnapshotBuilder(definitionsById);

                resolutionDtoMapper = new RoundResolutionDtoMapper(definitionsById);

                return true;
            }
            catch (Exception exception)
            {
                snapshotBuilder = null;
                resolutionDtoMapper = null;

                definitionsById.Clear();

                Debug.LogError("Card definitions could not be loaded: " + exception.Message);

                return false;
            }
        }

        #endregion

        #region Match Update Publication

        /// <summary>Sends the match's new revision to its own players only (never a broadcast).</summary>
        private void BroadcastUpdate(
            MatchSession session,
            RoundResolution resolution = null,
            RoundTransition roundTransition = null
        )
        {
            if (!IsServer || session == null)
            {
                return;
            }

            session.NextRevision();

            MatchState state = session.State;

            RoundResolutionDto resolutionDto =
                resolution == null || resolutionDtoMapper == null
                    ? null
                    : resolutionDtoMapper.Build(resolution, state);

            AbilityResolution abilityResolution = state?.TakeAbilityResolution();
            var abilityMapper = new AbilityResolutionDtoMapper(definitionsById);

            RoundTransitionDto roundTransitionDto = BuildRoundTransitionDto(
                roundTransition
            );

            foreach (KeyValuePair<ulong, SeatId> pair in new List<KeyValuePair<ulong, SeatId>>(session.Connections))
            {
                AbilityResolutionDto abilityDto = abilityMapper.Build(
                    abilityResolution,
                    pair.Value
                );

                SendUpdate(
                    pair.Key,
                    session,
                    pair.Value,
                    resolutionDto,
                    roundTransitionDto,
                    abilityDto
                );
            }
        }

        private void SendUpdate(
            ulong targetClientId,
            MatchSession session,
            SeatId viewerSeat,
            RoundResolutionDto resolution,
            RoundTransitionDto roundTransition = null,
            AbilityResolutionDto abilityResolution = null
        )
        {
            bool isVisible = NetworkObject.IsNetworkVisibleTo(targetClientId);

            Debug.Log(
                $"Preparing match update. Match={session.MatchId}, Target={targetClientId}, "
                    + $"Viewer={viewerSeat}, Visible={isVisible}, Revision={session.Revision}."
            );

            if (!isVisible)
            {
                QueueSnapshotWhenVisible(targetClientId);
                return;
            }

            var update = new MatchUpdateDto
            {
                snapshot = BuildSnapshot(session, viewerSeat),

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

        private MatchSnapshotDto BuildSnapshot(MatchSession session, SeatId viewerSeat)
        {
            SeatId opponentSeat = viewerSeat.Opponent();
            bool opponentConnected = session.HasConnectedSeat(opponentSeat);

            MatchSnapshotDto snapshot;

            if (!session.IsStarted || snapshotBuilder == null)
            {
                snapshot = MatchSnapshotBuilder.BuildWaiting(
                    viewerSeat,
                    opponentConnected,
                    session.Revision
                );
            }
            else
            {
                snapshot = snapshotBuilder.Build(
                    session.State,
                    viewerSeat,
                    opponentConnected,
                    session.Revision
                );

                snapshot.matchId = session.MatchId;
                snapshot.isRanked = session.IsRanked;
                snapshot.matchStartedUtcMs = session.StartedUtcMs;
            }

            ApplyConnectionState(session, snapshot, viewerSeat);
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
