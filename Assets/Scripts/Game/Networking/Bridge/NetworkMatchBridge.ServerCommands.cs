using System;
using System.Collections;
using System.Collections.Generic;
using CMCMProductions;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Server Command Pipeline

        // Every command is routed by MatchCommandService from the sender's connection to its own
        // match (seat, started, not paused, growing command ID) before the rules engine runs.

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitSummonUnitRpc(
            ulong commandId,
            ulong cardInstanceId,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            MatchCommandOutcome outcome = commandService.Execute(
                senderClientId,
                commandId,
                (session, actor) => session.Rules.TrySummonUnit(session.State, actor, cardInstanceId)
            );

            FinishCommand(senderClientId, outcome);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitDeclareAttackRpc(
            ulong commandId,
            ulong[] attackerIdsBySlot,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            MatchCommandOutcome outcome = commandService.Execute(
                senderClientId,
                commandId,
                (session, actor) => session.Rules.TryDeclareAttack(session.State, actor, attackerIdsBySlot)
            );

            FinishCommand(senderClientId, outcome);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitDeclareBlockRpc(
            ulong commandId,
            ulong[] blockerIdsBySlot,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            MatchCommandOutcome outcome = commandService.Execute(
                senderClientId,
                commandId,
                (session, actor) => session.Rules.TryDeclareBlock(session.State, actor, blockerIdsBySlot)
            );

            FinishCommand(senderClientId, outcome);
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

            var selection = new AbilityTargetSelection(
                primaryTargetIds,
                secondaryTargetIds
            );

            MatchCommandOutcome outcome = commandService.Execute(
                senderClientId,
                commandId,
                (session, actor) => session.Rules.TrySubmitAbilitySelection(
                    session.State,
                    actor,
                    requestId,
                    selection
                )
            );

            FinishCommand(senderClientId, outcome);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitCancelAbilitySelectionRpc(
            ulong commandId,
            ulong requestId,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            MatchCommandOutcome outcome = commandService.Execute(
                senderClientId,
                commandId,
                (session, actor) => session.Rules.TryCancelAbilitySelection(session.State, actor, requestId)
            );

            FinishCommand(senderClientId, outcome);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitPassRpc(ulong commandId, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            MatchCommandOutcome outcome = commandService.Execute(
                senderClientId,
                commandId,
                (session, actor) => session.Rules.TryPass(session.State, actor)
            );

            FinishCommand(senderClientId, outcome);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitSurrenderRpc(
            ulong commandId,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            MatchCommandOutcome outcome = commandService.Execute(
                senderClientId,
                commandId,
                (session, actor) => session.Rules.TrySurrender(session.State, actor)
            );

            FinishCommand(senderClientId, outcome);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitMulliganRpc(
            ulong commandId,
            ulong[] cardIdsToReplace,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            MatchCommandOutcome outcome = commandService.Execute(
                senderClientId,
                commandId,
                (session, actor) => ApplyMulligan(session.State, actor, cardIdsToReplace)
            );

            FinishCommand(senderClientId, outcome);
        }

        private static CommandResult ApplyMulligan(MatchState matchState, SeatId actor, ulong[] cardIdsToReplace)
        {
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

            return CommandResult.Success();
        }

        private void FinishCommand(ulong senderClientId, MatchCommandOutcome outcome)
        {
            CommandResult result = outcome.Result;

            if (!outcome.Accepted)
            {
                RejectCommand(senderClientId, result.RejectionReason);
                return;
            }

            MatchSession session = outcome.Session;

            if (result.Resolution != null)
            {
                Debug.Log(
                    $"[SERVER GRAVEYARD] Match={session.MatchId}, " +
                    $"Host={session.State.Host.Graveyard.Count}, " +
                    $"Guest={session.State.Guest.Graveyard.Count}"
                );
            }

            BroadcastUpdate(session, result.Resolution, result.RoundTransition);
            TrySettleFinishedMatch(session);
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
