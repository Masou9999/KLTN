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
        #region Server Command Pipeline

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitSummonUnitRpc(
            ulong commandId,
            ulong cardInstanceId,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryBeginCommand(senderClientId, commandId, out SeatId actor))
            {
                return;
            }

            CommandResult result = rulesEngine.TrySummonUnit(
                matchState,
                actor,
                cardInstanceId
            );

            FinishCommand(senderClientId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitDeclareAttackRpc(
            ulong commandId,
            ulong[] attackerIdsBySlot,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryBeginCommand(senderClientId, commandId, out SeatId actor))
            {
                return;
            }

            CommandResult result = rulesEngine.TryDeclareAttack(
                matchState,
                actor,
                attackerIdsBySlot
            );

            FinishCommand(senderClientId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitDeclareBlockRpc(
            ulong commandId,
            ulong[] blockerIdsBySlot,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryBeginCommand(senderClientId, commandId, out SeatId actor))
            {
                return;
            }

            CommandResult result = rulesEngine.TryDeclareBlock(
                matchState,
                actor,
                blockerIdsBySlot
            );

            FinishCommand(senderClientId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitAbilitySelectionRpc(
            ulong commandId,
            ulong requestId,
            ulong[] primaryTargetIds,
            ulong[] secondaryTargetIds,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryBeginCommand(senderClientId, commandId, out SeatId actor))
            {
                return;
            }

            var selection = new AbilityTargetSelection(
                primaryTargetIds,
                secondaryTargetIds
            );

            CommandResult result = rulesEngine.TrySubmitAbilitySelection(
                matchState,
                actor,
                requestId,
                selection
            );

            FinishCommand(senderClientId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitCancelAbilitySelectionRpc(
            ulong commandId,
            ulong requestId,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryBeginCommand(senderClientId, commandId, out SeatId actor))
            {
                return;
            }

            CommandResult result = rulesEngine.TryCancelAbilitySelection(
                matchState,
                actor,
                requestId
            );

            FinishCommand(senderClientId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitPassRpc(ulong commandId, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryBeginCommand(senderClientId, commandId, out SeatId actor))
            {
                return;
            }

            CommandResult result = rulesEngine.TryPass(matchState, actor);
            FinishCommand(senderClientId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitSurrenderRpc(
            ulong commandId,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryBeginCommand(senderClientId, commandId, out SeatId actor))
            {
                return;
            }

            CommandResult result = rulesEngine.TrySurrender(
                matchState,
                actor
            );

            FinishCommand(senderClientId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitMulliganRpc(
            ulong commandId,
            ulong[] cardIdsToReplace,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryBeginCommand(senderClientId, commandId, out SeatId actor))
            {
                return;
            }

            PlayerState player = matchState.Player(actor);

            if (cardIdsToReplace != null && cardIdsToReplace.Length > 0)
            {
                System.Random rng = new System.Random();
                player.PerformMulligan(cardIdsToReplace, rng);
            }

            if (actor == SeatId.Host)
                matchState.HostMulliganDone = true;
            else
                matchState.GuestMulliganDone = true;

            if (matchState.IsMulliganPhaseComplete && matchState.RoundNumber == 0)
            {
                matchState.BeginFirstRound();

                matchState.LastEvent = "Giai đoạn đổi bài kết thúc. Trận đấu bắt đầu!";
            }

            FinishCommand(senderClientId, CommandResult.Success());
        }

        private bool TryBeginCommand(
            ulong senderClientId,
            ulong commandId,
            out SeatId actor
        )
        {
            actor = default;

            if (!seatByClient.TryGetValue(senderClientId, out actor))
            {
                RejectCommand(senderClientId, CommandRejectionReason.InvalidSeat);
                return false;
            }

            if (matchState == null || rulesEngine == null)
            {
                RejectCommand(senderClientId, CommandRejectionReason.MatchNotReady);
                return false;
            }

            if (
                lastCommandIdByClient.TryGetValue(senderClientId, out ulong lastCommandId)
                && commandId <= lastCommandId
            )
            {
                RejectCommand(senderClientId, CommandRejectionReason.DuplicateCommand);
                return false;
            }

            // A rejected domain command still consumes its command ID so a
            // client cannot replay the same request.
            lastCommandIdByClient[senderClientId] = commandId;
            return true;
        }

        private void FinishCommand(ulong senderClientId, CommandResult result)
        {
            if (!result.Accepted)
            {
                RejectCommand(senderClientId, result.RejectionReason);
                return;
            }

            if (matchState != null && result.Resolution != null)
            {
                Debug.Log(
                    $"[SERVER GRAVEYARD] " +
                    $"Host={matchState.Host.Graveyard.Count}, " +
                    $"Guest={matchState.Guest.Graveyard.Count}"
                );
            }

            BroadcastUpdate(result.Resolution, result.RoundTransition);
            TrySettleFinishedMatch();
        }

        #endregion

        #region Client Rejection Feedback

        private void RejectCommand(ulong targetClientId, CommandRejectionReason reason)
        {
            ReceiveCommandRejectedRpc(
                (int)reason,
                RpcTarget.Single(targetClientId, RpcTargetUse.Temp)
            );
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveCommandRejectedRpc(
            int reasonValue,
            RpcParams rpcParams = default
        )
        {
            var reason = (CommandRejectionReason)reasonValue;

            MatchProjectionRegistry.Current.Reject(reason);
            Debug.LogWarning($"Match command rejected: {reason}.");
        }

        #endregion

        private static RoundTransitionDto BuildRoundTransitionDto(
            RoundTransition transition
        )
        {
            if (transition == null)
            {
                return null;
            }

            return new RoundTransitionDto
            {
                completedRoundNumber = transition.CompletedRoundNumber,

                nextRoundNumber = transition.NextRoundNumber,

                nextAttackTokenOwner = (int)transition.NextAttackTokenOwner,
            };
        }
    }
}
