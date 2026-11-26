
using UnityEngine;
using KLTN.Game.Networking;
using KLTN.Game.Domain;
using KLTN.Game.Presentation;

public class ReserveTurnGlowController : MonoBehaviour
{
    [SerializeField] private GameObject selfReserveTurnGlow;
    [SerializeField] private GameObject opponentReserveTurnGlow;
    [SerializeField] private MatchTransitionBanner transitionBanner;

    private MatchClientProjection projection;

    private void OnEnable()
{
    projection = MatchProjectionRegistry.Current;

    projection.SnapshotChanged += OnSnapshotChanged;

    CombatAnimationDirector.CombatAnimationStateChanged +=
        OnCombatAnimationStateChanged;
    
    if (transitionBanner != null)
    transitionBanner.PlayingStateChanged += OnBannerPlayingStateChanged;

    UpdateGlow(projection.Current);
}

    private void OnDisable()
{
    if (projection != null)
    {
        projection.SnapshotChanged -= OnSnapshotChanged;
    }

    CombatAnimationDirector.CombatAnimationStateChanged -=
        OnCombatAnimationStateChanged;

    if (transitionBanner != null)
    {
        transitionBanner.PlayingStateChanged -=
            OnBannerPlayingStateChanged;
    }
}

    private void OnSnapshotChanged(MatchSnapshotDto snapshot)
    {
        UpdateGlow(snapshot);
    }
    private void OnCombatAnimationStateChanged(bool isAnimating)
{
    UpdateGlow(projection?.Current);
}

private void OnBannerPlayingStateChanged(bool isPlaying)
{
    UpdateGlow(projection?.Current);
}

    private void UpdateGlow(MatchSnapshotDto snapshot)
{
    bool selfTurn = false;
    bool opponentTurn = false;

    if (snapshot != null && !snapshot.isPaused && snapshot.outcome == 0)
    {
        MatchPhase phase = (MatchPhase)snapshot.phase;

        bool showTurnGlow =
    !CombatAnimationDirector.IsCombatAnimating &&
    (transitionBanner == null || !transitionBanner.IsPlaying) &&
    (phase == MatchPhase.Priority ||
     phase == MatchPhase.BlockDeclaration);

        if (showTurnGlow)
        {
            selfTurn = snapshot.activeSeat == snapshot.viewerSeat;
            opponentTurn = snapshot.activeSeat != snapshot.viewerSeat;
        }
    }

    if (selfReserveTurnGlow != null)
    {
        selfReserveTurnGlow.SetActive(selfTurn);
    }

    if (opponentReserveTurnGlow != null)
    {
        opponentReserveTurnGlow.SetActive(opponentTurn);
    }
}
}
