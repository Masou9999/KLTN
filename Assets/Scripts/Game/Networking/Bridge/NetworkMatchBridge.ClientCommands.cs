using System;
using System.Collections;
using System.Collections.Generic;
using CMCMProductions;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Client Command API

        /// <summary>
        /// Sends only the selected card instance ID; cost, zone and Play requirements are
        /// recomputed by the host.
        /// </summary>
        public bool RequestSummonUnit(string instanceId)
        {
            if (!IsSpawned || !IsClient)
            {
                return false;
            }

            if (!ulong.TryParse(instanceId, out ulong parsedId))
            {
                MatchProjectionRegistry.Current.Reject(
                    CommandRejectionReason.CardNotInHand
                );

                return false;
            }

            MatchTurnClockRegistry.MarkSubmitted(CurrentDecisionId);
            SubmitSummonUnitRpc(nextLocalCommandId++, CurrentDecisionId, parsedId);

            return true;
        }

        /// <summary>
        /// Sends the ordered attack-slot intent after parsing stable instance IDs locally.
        /// </summary>
        public bool RequestDeclareAttack(string[] attackerIdsBySlot)
        {
            if (!IsSpawned || !IsClient)
            {
                return false;
            }

            if (
                attackerIdsBySlot == null
                || attackerIdsBySlot.Length != MatchState.BoardSlotCount
            )
            {
                MatchProjectionRegistry.Current.Reject(
                    CommandRejectionReason.InvalidAttackDeclaration
                );

                return false;
            }

            var parsedIds = new ulong[MatchState.BoardSlotCount];

            for (int slotIndex = 0; slotIndex < attackerIdsBySlot.Length; slotIndex++)
            {
                string instanceId = attackerIdsBySlot[slotIndex];

                if (string.IsNullOrWhiteSpace(instanceId))
                {
                    parsedIds[slotIndex] = 0;
                    continue;
                }

                if (!ulong.TryParse(instanceId, out parsedIds[slotIndex]))
                {
                    MatchProjectionRegistry.Current.Reject(
                        CommandRejectionReason.InvalidAttackDeclaration
                    );

                    return false;
                }
            }

            MatchTurnClockRegistry.MarkSubmitted(CurrentDecisionId);
            SubmitDeclareAttackRpc(nextLocalCommandId++, CurrentDecisionId, parsedIds);

            return true;
        }

        /// <summary>
        /// Sends the ordered block-slot intent; keyword and alignment validation remain
        /// server-authoritative.
        /// </summary>
        public bool RequestDeclareBlock(string[] blockerIdsBySlot)
        {
            if (!IsSpawned || !IsClient)
            {
                return false;
            }

            if (
                blockerIdsBySlot == null
                || blockerIdsBySlot.Length != MatchState.BoardSlotCount
            )
            {
                MatchProjectionRegistry.Current.Reject(
                    CommandRejectionReason.InvalidBlockDeclaration
                );

                return false;
            }

            var parsedIds = new ulong[MatchState.BoardSlotCount];

            for (int slotIndex = 0; slotIndex < blockerIdsBySlot.Length; slotIndex++)
            {
                string instanceId = blockerIdsBySlot[slotIndex];

                if (string.IsNullOrWhiteSpace(instanceId))
                {
                    parsedIds[slotIndex] = 0;
                    continue;
                }

                if (!ulong.TryParse(instanceId, out parsedIds[slotIndex]))
                {
                    MatchProjectionRegistry.Current.Reject(
                        CommandRejectionReason.InvalidBlockDeclaration
                    );

                    return false;
                }
            }

            MatchTurnClockRegistry.MarkSubmitted(CurrentDecisionId);
            SubmitDeclareBlockRpc(nextLocalCommandId++, CurrentDecisionId, parsedIds);

            return true;
        }

        public bool RequestSubmitAbilitySelection(
            string requestId,
            string[] primaryTargetIds,
            string[] secondaryTargetIds
        )
        {
            if (
                !IsSpawned
                || !IsClient
                || !ulong.TryParse(requestId, out ulong parsedRequestId)
            )
            {
                return false;
            }

            if (
                !TryParseOptionalCardIds(
                    primaryTargetIds,
                    out ulong[] parsedPrimaryTargets
                )
                || !TryParseOptionalCardIds(
                    secondaryTargetIds,
                    out ulong[] parsedSecondaryTargets
                )
            )
            {
                MatchProjectionRegistry.Current.Reject(
                    CommandRejectionReason.InvalidAbilityTarget
                );

                return false;
            }

            MatchTurnClockRegistry.MarkSubmitted(CurrentDecisionId);
            SubmitAbilitySelectionRpc(
                nextLocalCommandId++,
                CurrentDecisionId,
                parsedRequestId,
                parsedPrimaryTargets,
                parsedSecondaryTargets
            );

            return true;
        }

        public bool RequestCancelAbilitySelection(string requestId)
        {
            if (
                !IsSpawned
                || !IsClient
                || !ulong.TryParse(requestId, out ulong parsedRequestId)
            )
            {
                return false;
            }

            MatchTurnClockRegistry.MarkSubmitted(CurrentDecisionId);
            SubmitCancelAbilitySelectionRpc(nextLocalCommandId++, CurrentDecisionId, parsedRequestId);

            return true;
        }

        public bool RequestPass()
        {
            if (!IsSpawned || !IsClient)
            {
                return false;
            }

            MatchTurnClockRegistry.MarkSubmitted(CurrentDecisionId);
            SubmitPassRpc(nextLocalCommandId++, CurrentDecisionId);
            return true;
        }

        public bool RequestSurrender()
        {
            if (!IsSpawned || !IsClient)
            {
                return false;
            }

            SubmitSurrenderRpc(nextLocalCommandId++);
            return true;
        }

        public bool RequestMulligan(ulong[] cardIdsToReplace)
        {
            if (!IsSpawned || !IsClient)
            {
                return false;
            }

            MatchTurnClockRegistry.MarkSubmitted(CurrentDecisionId);
            SubmitMulliganRpc(nextLocalCommandId++, CurrentDecisionId, cardIdsToReplace);
            return true;
        }

        private static bool TryParseOptionalCardIds(
            string[] instanceIds,
            out ulong[] parsedIds
        )
        {
            if (instanceIds == null || instanceIds.Length == 0)
            {
                parsedIds = Array.Empty<ulong>();

                return true;
            }

            parsedIds = new ulong[instanceIds.Length];

            for (int i = 0; i < instanceIds.Length; i++)
            {
                if (!ulong.TryParse(instanceIds[i], out parsedIds[i]))
                {
                    parsedIds = Array.Empty<ulong>();

                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
