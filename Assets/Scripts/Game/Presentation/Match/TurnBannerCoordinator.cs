using System.Collections;
using KLTN.Game.Networking;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public sealed class TurnBannerCoordinator : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField]
        private MatchTransitionBanner transitionBanner;
        private int lastRoundNumber = -1;

        #endregion

        #region Runtime State

        private MatchClientProjection projection;
        private Coroutine playbackCoroutine;
        private bool hasSnapshot;
        private bool wasOwnTurn;
        private bool pendingYourTurn;
        private ulong lastQueuedRevision = ulong.MaxValue;
        private int pendingRoundStartNumber;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            projection = MatchProjectionRegistry.Current;
            projection.SnapshotChanged += HandleSnapshotChanged;

            if (projection.Current != null)
            {
                HandleSnapshotChanged(projection.Current);
            }
        }

        private void OnDisable()
        {
            if (projection != null)
            {
                projection.SnapshotChanged -= HandleSnapshotChanged;
            }

            if (playbackCoroutine != null)
            {
                StopCoroutine(playbackCoroutine);
                playbackCoroutine = null;
            }

            pendingYourTurn = false;

            hasSnapshot = false;
            wasOwnTurn = false;
            lastRoundNumber = -1;
            lastQueuedRevision = ulong.MaxValue;
            pendingRoundStartNumber = 0;
        }

        #endregion

        #region Snapshot Handling

        private void HandleSnapshotChanged(MatchSnapshotDto snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            int previousRoundNumber = lastRoundNumber;

            bool roundChanged = hasSnapshot && snapshot.roundNumber > previousRoundNumber;

            bool shouldShowFirstRound =
                snapshot.roundNumber == 1 && (!hasSnapshot || previousRoundNumber < 1);

            bool isOwnTurn =
                snapshot.viewerCanAct && snapshot.activeSeat == snapshot.viewerSeat;

            bool shouldShowYourTurn =
                isOwnTurn && (!hasSnapshot || !wasOwnTurn || roundChanged);

            // A cancellable Play selection temporarily disables normal actions without
            // ending the current turn. Returning from it must not replay the turn banner.
            bool continuingOwnTurnSelection =
                wasOwnTurn
                && snapshot.roundNumber == previousRoundNumber
                && snapshot.activeSeat == snapshot.viewerSeat
                && snapshot.viewerMustSelectAbilityTargets
                && snapshot.pendingAbilitySelection != null
                && snapshot.pendingAbilitySelection.canCancel;

            hasSnapshot = true;
            wasOwnTurn = isOwnTurn || continuingOwnTurnSelection;
            lastRoundNumber = snapshot.roundNumber;

            if (!shouldShowFirstRound && !shouldShowYourTurn)
            {
                return;
            }

            if (snapshot.revision == lastQueuedRevision)
            {
                return;
            }

            lastQueuedRevision = snapshot.revision;

            if (shouldShowFirstRound)
            {
                pendingRoundStartNumber = 1;
            }

            if (shouldShowYourTurn)
            {
                pendingYourTurn = true;
            }

            if (playbackCoroutine == null)
            {
                playbackCoroutine = StartCoroutine(PlayPendingBanners());
            }
        }

        private IEnumerator PlayPendingBanners()
        {
            while (pendingRoundStartNumber > 0 || pendingYourTurn)
            {
                int roundNumber = pendingRoundStartNumber;

                pendingRoundStartNumber = 0;

                if (roundNumber > 0 && transitionBanner != null)
                {
                    yield return transitionBanner.PlayRoundStart(roundNumber);
                }

                if (pendingYourTurn)
                {
                    pendingYourTurn = false;

                    if (transitionBanner != null)
                    {
                        yield return transitionBanner.PlayYourTurn();
                    }
                }
            }

            playbackCoroutine = null;
        }

        #endregion
    }
}
