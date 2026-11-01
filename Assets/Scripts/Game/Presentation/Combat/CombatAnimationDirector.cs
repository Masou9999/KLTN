using System;
using System.Collections;
using System.Collections.Generic;
using KLTN.Game.Networking;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Consumes ordered authoritative match updates and coordinates movement, impact,
    /// damage, lethal highlights and cleanup without changing gameplay state.
    /// </summary>
    public sealed partial class CombatAnimationDirector : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [SerializeField]
        private MatchBoardPresenter boardPresenter;

        [SerializeField]
        private RectTransform animationLayer;

        [SerializeField]
        private RectTransform selfNexusTarget;

        [SerializeField]
        private RectTransform opponentNexusTarget;

        [SerializeField]
        private MatchTransitionBanner transitionBanner;

        [Header("Movement")]
        [Min(0f)]
        [SerializeField]
        private float retreatDistance = 40f;

        [Min(0f)]
        [SerializeField]
        private float directAttackDistance = 350f;

        [Min(0f)]
        [SerializeField]
        private float collisionGap = 12f;

        [Min(0f)]
        [SerializeField]
        private float nexusContactGap = 4f;

        [Header("VFX")]
        [SerializeField]
        private CombatVfxPresenter vfxPresenter;

        [Header("Audio")]
        [SerializeField]
        private AudioSource sfxAudioSource;

        [SerializeField]
        private AudioClip attackSfx;

        [Tooltip("Ability impact on a card or nexus. Falls back to Attack Sfx when empty.")]
        [SerializeField]
        private AudioClip abilityHitSfx;

        [Header("Damage Feedback")]
        [SerializeField]
        private DamageFeedbackView selfNexusFeedback;

        [SerializeField]
        private DamageFeedbackView opponentNexusFeedback;

        [Header("Timing")]
        [Min(0f)]
        [SerializeField]
        private float retreatDuration = 0.18f;

        [Min(0f)]
        [SerializeField]
        private float strikeDuration = 0.22f;

        [Min(0f)]
        [SerializeField]
        private float impactHoldDuration = 0.06f;

        [Min(0f)]
        [SerializeField]
        private float returnDuration = 0.18f;

        [Min(0f)]
        [SerializeField]
        private float intervalBetweenSlots = 0.08f;

        [Min(0f)]
        [SerializeField]
        private float combatPreviewDuration = 1.5f;

        [Min(0f)]
        [SerializeField]
        private float deathDelayAfterCombat = 0.5f;

        #endregion

        #region Runtime State

        private readonly HashSet<CombatCardAnimator> preparedAnimators =
            new HashSet<CombatCardAnimator>();
        private readonly HashSet<DamageFeedbackView> activeFeedbackViews =
            new HashSet<DamageFeedbackView>();
        private readonly HashSet<CardDeathFeedback> activeDeathFeedbacks =
            new HashSet<CardDeathFeedback>();
        private readonly Dictionary<string, CardDeathFeedback> pendingDeathsById =
            new Dictionary<string, CardDeathFeedback>();
        private readonly Vector3[] nexusTargetCorners = new Vector3[4];

        private MatchUpdateInbox inbox;
        private MatchClientProjection projection;
        private Coroutine processingCoroutine;
        private MatchUpdateDto currentUpdate;
        private int activeDamageFeedbackCount;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            inbox = MatchUpdateInboxRegistry.Current;
            projection = MatchProjectionRegistry.Current;

            inbox.UpdateAvailable += HandleUpdateAvailable;
            TryStartProcessing();
        }

        private void OnDisable()
        {
            if (inbox != null)
            {
                inbox.UpdateAvailable -= HandleUpdateAvailable;
            }

            StopAllCoroutines();

            CleanupAbilityPresentation();

            activeDamageFeedbackCount = 0;
            RestorePreparedCards();
            ResetFeedbackViews();
            ResetDeathFeedbacks();
            pendingDeathsById.Clear();

            if (currentUpdate?.snapshot != null)
            {
                projection?.Apply(currentUpdate.snapshot);
            }

            currentUpdate = null;
            processingCoroutine = null;
        }

        #endregion

        #region Update Processing

        private void HandleUpdateAvailable()
        {
            TryStartProcessing();
        }

        private void TryStartProcessing()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            if (processingCoroutine != null)
            {
                return;
            }

            processingCoroutine = StartCoroutine(ProcessUpdates());
        }

        private IEnumerator ProcessUpdates()
        {
            while (inbox.TryDequeue(out currentUpdate))
            {
                bool hasAbilities = currentUpdate.hasAbilityResolution
                    && currentUpdate.abilityResolution?.events != null
                    && currentUpdate.abilityResolution.events.Length > 0;

                if (hasAbilities)
                {
                    // Projection is applied only after all effects finish. Hide the
                    // selection beam now, before the target's death animation starts.
                    boardPresenter?.GetComponent<AbilityTargetSelectionController>()
                        ?.SuspendLinksForResolution();
                    SetAbilityInputBlocked(true);
                }

                try
                {
                    if (HasCombatResolution(currentUpdate))
                    {
                        yield return AnimateResolution(currentUpdate);
                    }

                    if (HasRoundTransition(currentUpdate))
                    {
                        yield return AnimateRoundTransition(currentUpdate);
                    }

                    if (hasAbilities)
                    {
                        yield return AnimateAbilityResolution(currentUpdate);
                    }

                    projection.Apply(currentUpdate.snapshot);
                }
                finally
                {
                    if (hasAbilities)
                    {
                        SetAbilityInputBlocked(false);
                        ClearAbilityGraphics();
                    }

                    currentUpdate = null;
                }
            }

            processingCoroutine = null;
        }

        private void PlayAttackSfx()
        {
            if (sfxAudioSource == null || attackSfx == null)
            {
                return;
            }

            sfxAudioSource.PlayOneShot(attackSfx);
        }

        /// <summary>
        /// Plays the ability impact sound on the shared SFX source, so the SFX slider
        /// (<c>GameSettingsUI</c>) controls it like the attack sound.
        /// </summary>
        private void PlayAbilityHitSfx()
        {
            AudioClip clip = abilityHitSfx != null ? abilityHitSfx : attackSfx;

            if (sfxAudioSource == null || clip == null)
            {
                return;
            }

            sfxAudioSource.PlayOneShot(clip);
        }

        #endregion
    }
}
