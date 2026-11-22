using System.Collections.Generic;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public sealed partial class MatchBoardPresenter
    {
        #region Support Flame Observation

        private QuyCauFlamePresenter flames;
        private QuyCauFlameProfile flameProfile;
        private bool flameProfileLoaded;
        private string[][] flameCombatIds;
        private readonly HashSet<string> flameDeparted = new HashSet<string>();
        private readonly HashSet<string> flameDead = new HashSet<string>();

        /// <summary>
        /// Reconciles the small displayed board once after all draft/render mutations.
        /// Slot identity survives snapshot view rebuilds; transforms only anchor pixels.
        /// </summary>
        private void LateUpdate()
        {
            if (projection?.Current == null
                || projection.Current.outcome != (int)MatchOutcome.Running)
            {
                if (flames != null) ClearFlames();
                return;
            }
            if (!flameProfileLoaded)
            {
                flameProfileLoaded = true;
                flameProfile = Resources.Load<QuyCauFlameProfile>("VFX/QuyCauFlame");
            }
            if (flameProfile == null || !flameProfile.IsUsable) return;
            if (selfBoardSlots == null || opponentBoardSlots == null) return;
            if (flames == null)
            {
                Transform boardArea = FindFlameBoardArea();
                if (boardArea == null || boardArea.parent == null) return;
                flames = new QuyCauFlamePresenter(boardArea, flameProfile,
                    selfBoardSlots.Length + opponentBoardSlots.Length);
            }
            flames.SetRaised(CombatAnimationDirector.IsCombatAnimating);
            UpdateFlameSide(false, selfBoardSlots, 0);
            UpdateFlameSide(true, opponentBoardSlots, selfBoardSlots.Length);
            flames.RefreshSound();
        }

        /// <summary>Lowest common ancestor of both boards (GameScene: Game/BoardArea).</summary>
        private Transform FindFlameBoardArea()
        {
            Transform self = selfBoardSlots.Length > 0 ? selfBoardSlots[0] : null;
            Transform opponent = opponentBoardSlots.Length > 0 ? opponentBoardSlots[0] : null;
            if (self == null || opponent == null) return null;
            for (Transform node = self; node != null; node = node.parent)
                if (opponent.IsChildOf(node)) return node;
            return null;
        }

        private void UpdateFlameSide(bool opponent, RectTransform[] slots, int offset)
        {
            for (int slot = 0; slot < slots.Length; slot++)
            {
                NetworkCardVisual source = slot > 0
                    ? GetFlameOccupant(opponent, slot - 1) : null;
                NetworkCardVisual target = GetFlameOccupant(opponent, slot);
                bool dead = source != null && flameDead.Contains(source.InstanceId);
                bool desired = source != null && !dead
                    && !flameDeparted.Contains(source.InstanceId)
                    && source.DefinitionId == flameProfile.definitionId;
                RectTransform anchor = target != null
                    && !flameDead.Contains(target.InstanceId) ? target.CardRect : slots[slot];
                flames.UpdateSlot(offset + slot, desired, anchor,
                    Time.unscaledDeltaTime, dead);
            }
        }

        private NetworkCardVisual GetFlameOccupant(bool opponent, int slot)
        {
            if (flameCombatIds != null)
            {
                string[] ids = flameCombatIds[opponent ? 1 : 0];
                if (slot < ids.Length && TryGetFaceUpVisual(ids[slot], out var combat))
                    return combat;
                return null;
            }
            MatchSnapshotDto snapshot = projection.Current;
            if (!opponent && actionControls != null
                && (snapshot.viewerCanDeclareAttack || snapshot.viewerCanDeclareBlock))
            {
                NetworkCardVisual pending = actionControls.GetPendingBoardVisual(slot);
                if (pending != null) return pending;
                foreach (GameObject preview in attackCompanionPreviewViews)
                    if (preview != null && slot < selfBoardSlots.Length
                        && preview.transform.parent == selfBoardSlots[slot])
                        return preview.GetComponent<NetworkCardVisual>();
            }
            CardViewDto[] cards = opponent ? snapshot.opponent?.board : snapshot.self?.board;
            if (cards != null)
                foreach (CardViewDto card in cards)
                    if (card != null && card.boardSlotIndex == slot
                        && TryGetFaceUpVisual(card.instanceId, out var visual))
                        return visual;
            return null;
        }

        private void PrepareFlameCombat(RoundResolutionDto resolution, int viewerSeat)
        {
            flameCombatIds = new[] { new string[selfBoardSlots.Length],
                new string[opponentBoardSlots.Length] };
            foreach (CombatStepDto step in resolution.steps)
            {
                if (step == null) continue;
                SetFlameCombatCard(step.hostCard, step.slotIndex, viewerSeat);
                SetFlameCombatCard(step.guestCard, step.slotIndex, viewerSeat);
            }
        }

        private void SetFlameCombatCard(ResolvedCardDto card, int slot, int viewerSeat)
        {
            // JsonUtility cannot transmit null: an absent guest/host arrives as an empty
            // card whose seat defaults to Host (0). Skipping it keeps that default from
            // overwriting the real occupant of the same slot.
            if (string.IsNullOrWhiteSpace(card?.cardBefore?.instanceId)) return;
            string[] ids = flameCombatIds[card.seat == viewerSeat ? 0 : 1];
            if (slot >= 0 && slot < ids.Length) ids[slot] = card.cardBefore.instanceId;
        }

        /// <summary>Ends source coverage at the presented death/return, before final projection.</summary>
        public void NotifyFlameDeparture(string instanceId, bool died)
        {
            if (string.IsNullOrEmpty(instanceId)) return;
            flameDeparted.Add(instanceId);
            if (died) flameDead.Add(instanceId);
        }

        private void ResetFlameBoardState()
        {
            flameCombatIds = null;
            flameDeparted.Clear();
            flameDead.Clear();
        }

        /// <summary>Stops audio and visuals immediately on reset or presentation teardown.</summary>
        public void ClearFlames()
        {
            flames?.Dispose();
            flames = null;
            ResetFlameBoardState();
        }

        #endregion
    }
}
