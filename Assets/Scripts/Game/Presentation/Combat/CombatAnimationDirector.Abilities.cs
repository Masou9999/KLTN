using System.Collections;
using System.Collections.Generic;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    public sealed partial class CombatAnimationDirector
    {
        [Header("Ability Animation")]
        [Min(0.05f)] [SerializeField] private float abilityProjectileDuration = 0.36f;
        [Min(0f)] [SerializeField] private float abilityProjectileArc = 80f;
        [Min(0.1f)] [SerializeField] private float abilityProjectileSizeMultiplier = 10f;
        [Min(0.05f)] [SerializeField] private float abilityCastPulseDuration = 0.2f;
        [Min(0.05f)] [SerializeField] private float abilityImpactDuration = 0.32f;
        [Min(0.05f)] [SerializeField] private float abilityReviveLightningDuration = 0.24f;
        [Min(0.05f)] [SerializeField] private float abilityReviveDuration = 0.68f;
        [SerializeField] private Color damageProjectileColor = new Color(1f, 0.47f, 0.08f, 1f);
        [SerializeField] private Color killProjectileColor = new Color(0.65f, 0.21f, 0.96f, 1f);
        [SerializeField] private Color reviveFlashColor = new Color(0.38f, 0.94f, 1f, 1f);
        [Header("Signature Ability VFX")]
        [SerializeField] private LightningEffectProfile thanTrungLightning;
        [SerializeField] private BiteEffectProfile maTranhBite;
        [SerializeField] private BonkEffectProfile ongKeBonk;
        [SerializeField] private FeastEffectProfile maDoiFeast;
        [Header("Ability Nexus HUD Targets")]
        [SerializeField] private RectTransform selfAbilityNexusTarget;
        [SerializeField] private RectTransform opponentAbilityNexusTarget;

        private readonly List<GameObject> activeAbilityGraphics = new List<GameObject>();
        private readonly List<CardReviveFeedback> activeReviveFeedbacks =
            new List<CardReviveFeedback>();

        private RectTransform abilityInputBlocker;
        private Sprite abilityGlowSprite;
        private Texture2D abilityGlowTexture;
        private LightningEffectView lightningView;
        private bool lightningProfileLoaded;
        private BiteEffectView biteView;
        private bool biteProfileLoaded;
        private BonkEffectView bonkView;
        private bool bonkProfileLoaded;
        private FeastEffectView feastView;
        private bool feastProfileLoaded;
        private BatSwarmProfile batProfile;
        private BatSwarmView batView;
        private SpiritSpellProfile spiritProfile;
        private SpiritSpellView spiritView;
        private ManaReductionProfile manaReductionProfile;
        private ManaReductionView manaReductionView;

        /// <summary>
        /// Replays only host-confirmed effects in their original execution order. The
        /// authoritative snapshot is applied by ProcessUpdates after this completes.
        /// </summary>
        private IEnumerator AnimateAbilityResolution(MatchUpdateDto update)
        {
            if (boardPresenter == null || animationLayer == null)
            {
                yield break;
            }

            int viewerSeat = update.snapshot.viewerSeat;
            bool reserveCanDrag = update.snapshot.viewerCanDeclareAttack
                || update.snapshot.viewerCanDeclareBlock;

            AbilityResolutionEventDto[] events = update.abilityResolution.events;
            bool transitionPending = HasRoundTransition(update);
            NetworkCardVisual abilitySource = null;

            for (int i = 0; i < events.Length; i++)
            {
                AbilityResolutionEventDto effect = events[i];

                if (effect == null)
                {
                    continue;
                }

                // Finish natural expiry deaths before announcing the next round.
                // Other ability sequences retain their existing post-banner ordering.
                if (transitionPending && !effect.isDeathOnly
                    && effect.abilityId != CardCostCalculator.AlliedDeathReductionId)
                {
                    yield return AnimateRoundTransition(update);
                    transitionPending = false;
                }

                if (i == 0 || !BelongsToSameAbility(events[i - 1], effect))
                {
                    PrepareAbilityLethalPreviews(events, i, viewerSeat);
                    bool centerSource = false;
                    for (int j = i; j < events.Length
                        && BelongsToSameAbility(effect, events[j]); j++)
                    {
                        centerSource |= IsTargetedAbilityFromReserve(events[j]);
                    }
                    AbilityPresentationTrace.Record(update, "stage-begin", effect, i);
                    if (IsSpiritSummon(effect)
                        && effect.sourceZone == (int)CardZone.Reserve)
                    {
                        // Stage newly revealed opponent summons in Reserve as well,
                        // before reusing the existing move-to-center animation.
                        boardPresenter.PrepareAbilitySource(effect.source,
                            effect.sourceOwner, viewerSeat, effect.sourceZone);
                        Canvas.ForceUpdateCanvases();
                    }
                    abilitySource = boardPresenter.PrepareAbilitySource(
                        effect.source, effect.sourceOwner, viewerSeat, effect.sourceZone,
                        centerSource, out bool requiresCenterMove);
                    if (requiresCenterMove && abilitySource != null)
                    {
                        AbilityPresentationTrace.Record(update, "move-begin", effect, i);
                        yield return boardPresenter.AnimateAbilitySourceMoveToCenter(
                            abilitySource, effect.sourceOwner, viewerSeat);
                        AbilityPresentationTrace.Record(update, "move-end", effect, i);
                    }
                    AbilityPresentationTrace.Record(update, "stage-end", effect, i);
                }

                AbilityPresentationTrace.Record(update, "effect-begin", effect, i);
                if (batProfile == null) batProfile = Resources.Load<BatSwarmProfile>("VFX/MaCangSungBats");
                if (batProfile != null && batProfile.IsUsable && effect.abilityId == batProfile.abilityId
                    && effect.hasNexusTarget && (EffectKind)effect.effectKind == EffectKind.Damage)
                {
                    AbilityResolutionEventDto heal = i + 1 < events.Length ? events[i+1] : null;
                    bool paired = BelongsToSameAbility(effect, heal) && heal.hasNexusTarget
                        && (EffectKind)heal.effectKind == EffectKind.Heal && heal.nexusOwner == effect.sourceOwner;
                    yield return AnimateBatSwarm(effect, paired ? heal : null, viewerSeat);
                    if (paired) i++;
                }
                else
                {
                    yield return AnimateAbilityEffect(effect, abilitySource, viewerSeat, reserveCanDrag);
                }
                AbilityPresentationTrace.Record(update, "effect-end", effect, i);
                bool isLastEventForAbility =
                    i + 1 >= events.Length || !BelongsToSameAbility(effect, events[i + 1]);

                if (isLastEventForAbility && effect.source != null
                    && effect.sourceZone == (int)CardZone.Reserve)
                {
                    CardViewDto[] finalReserve = effect.sourceOwner == viewerSeat
                        ? update.snapshot?.self?.reserve
                        : update.snapshot?.opponent?.reserve;

                    AbilityPresentationTrace.Record(update, "return-begin", effect, i);
                    yield return boardPresenter.AnimateAbilitySourceReturnToReserve(
                        effect.source,
                        effect.sourceOwner,
                        viewerSeat,
                        finalReserve
                    );
                    AbilityPresentationTrace.Record(update, "return-end", effect, i);
                }
            }

            if (transitionPending)
            {
                yield return AnimateRoundTransition(update);
            }

            foreach (CardReviveFeedback feedback in activeReviveFeedbacks)
            {
                if (feedback != null)
                {
                    feedback.ResetImmediately();
                }
            }

            activeReviveFeedbacks.Clear();
        }

        private void PrepareAbilityLethalPreviews(
            AbilityResolutionEventDto[] events,
            int firstIndex,
            int viewerSeat
        )
        {
            AbilityResolutionEventDto first = events[firstIndex];

            for (int i = firstIndex; i < events.Length; i++)
            {
                AbilityResolutionEventDto effect = events[i];

                if (!BelongsToSameAbility(first, effect))
                {
                    break;
                }

                if (
                    effect?.targetBefore == null
                    || !IsAbilityDeath(effect)
                )
                {
                    continue;
                }

                if (ResolveFeastProfile(effect) != null) continue;
                if (ResolveWaterDeath(effect.targetBefore.definitionId) != null) continue;

                NetworkCardVisual target = boardPresenter.PrepareAbilitySource(
                    effect.targetBefore,
                    effect.targetOwner,
                    viewerSeat,
                    effect.targetZoneBefore
                );

                target?.GetComponent<CardDeathFeedback>()?.ShowTargetPreviewCrack(
                    effect.targetBefore.instanceId
                );
            }
        }

        private static bool BelongsToSameAbility(
            AbilityResolutionEventDto first,
            AbilityResolutionEventDto second
        )
        {
            return first != null
                && second != null
                && first.executionId == second.executionId
                && first.abilityId == second.abilityId
                && first.source?.instanceId == second.source?.instanceId;
        }

        private static bool IsTargetedAbilityFromReserve(AbilityResolutionEventDto effect)
        {
            if (effect == null || effect.isDeathOnly || effect.hasNexusTarget)
            {
                return false;
            }

            if (effect.source == null || effect.sourceZone != (int)CardZone.Reserve)
            {
                return false;
            }

            return effect.targetBefore != null
                || (EffectKind)effect.effectKind == EffectKind.Revive
                || (EffectKind)effect.effectKind == EffectKind.Copy;
        }

        private static bool IsSpiritSummon(AbilityResolutionEventDto effect)
        {
            return effect.abilityId == "ma_co_summon"
                || effect.abilityId == "linh_mieu_summon";
        }

        private IEnumerator AnimateAbilityEffect(
            AbilityResolutionEventDto effect,
            NetworkCardVisual source,
            int viewerSeat,
            bool reserveCanDrag
        )
        {
            EffectKind kind = (EffectKind)effect.effectKind;

            if (kind == EffectKind.Copy
                && effect.targetZoneAfter == (int)CardZone.DrawHand)
            {
                yield return AnimateAbilityCopy(effect, source, viewerSeat);
                yield break;
            }

            if (kind == EffectKind.Revive && effect.targetAfter != null)
            {
                yield return AnimateAbilityRevive(effect, source, viewerSeat, reserveCanDrag);
                yield break;
            }

            if (effect.hasNexusTarget)
            {
                yield return AnimateAbilityNexusEffect(effect, source, viewerSeat);
                yield break;
            }

            if (effect.targetBefore != null)
            {
                yield return AnimateAbilityCardEffect(effect, source, viewerSeat);
            }
        }

        private IEnumerator AnimateAbilityCardEffect(
            AbilityResolutionEventDto effect,
            NetworkCardVisual source,
            int viewerSeat
        )
        {
            NetworkCardVisual target = boardPresenter.PrepareAbilitySource(
                effect.targetBefore,
                effect.targetOwner,
                viewerSeat,
                effect.targetZoneBefore
            );

            if (target == null)
            {
                yield break;
            }

            Canvas.ForceUpdateCanvases();

            if ((EffectKind)effect.effectKind == EffectKind.ReduceCost
                && effect.targetAfter != null
                && (effect.abilityId == "ma_ga_play"
                    || effect.abilityId == CardCostCalculator.AlliedDeathReductionId))
            {
                if (manaReductionProfile == null)
                    manaReductionProfile = Resources.Load<ManaReductionProfile>("VFX/ManaReduction");
                if (manaReductionProfile != null && manaReductionProfile.IsUsable)
                {
                    if (manaReductionView == null)
                    {
                        var go = new GameObject("ManaReduction", typeof(RectTransform),
                            typeof(ManaReductionView));
                        go.transform.SetParent(animationLayer, false);
                        manaReductionView = go.GetComponent<ManaReductionView>();
                    }
                    yield return manaReductionView.Play(manaReductionProfile, animationLayer,
                        target, effect.targetBefore.energy, effect.targetAfter.energy,
                        effect.abilityId == CardCostCalculator.AlliedDeathReductionId, sfxAudioSource);
                }
                else target.SetDisplayedEnergy(effect.targetAfter.energy);
                yield break;
            }

            if (effect.isDeathOnly)
            {
                yield return PlayCardDeath(target, effect.targetBefore.instanceId);
                yield break;
            }

            Vector3 targetPosition = CardCenter(target);
            Vector3 sourcePosition = source == null
                ? targetPosition + Vector3.down * 100f
                : CardCenter(source);

            AbilityPresentationTrace.Record(currentUpdate, "feast-profile-begin", effect);
            FeastEffectProfile feast = ResolveFeastProfile(effect);
            AbilityPresentationTrace.Record(currentUpdate, "feast-profile-end", effect);
            if (feast != null && source != null)
            {
                target.GetComponent<CardDeathFeedback>()?.ClearTargetPreviewCrack();
                if (feastView == null)
                {
                    var viewObject = new GameObject("MaDoiFeast", typeof(RectTransform), typeof(FeastEffectView));
                    viewObject.transform.SetParent(animationLayer, false);
                    feastView = viewObject.GetComponent<FeastEffectView>();
                }
                AbilityPresentationTrace.Record(currentUpdate, "vfx-begin", effect);
                yield return feastView.Play(feast, animationLayer, source.CardRect, target.CardRect, sfxAudioSource);
                AbilityPresentationTrace.Record(currentUpdate, "vfx-end", effect);
                yield return boardPresenter.AnimateCollapseCardInReserveLayout(target);
                // Transformation replaces the generic kill/shatter presentation only.
                // Subsequent death-trigger events still replay in authoritative order.
                yield break;
            }

            AbilityPresentationTrace.Record(currentUpdate, "profiles-begin", effect);
            LightningEffectProfile lightning = ResolveLightningProfile(effect);
            BiteEffectProfile bite = ResolveBiteProfile(effect);
            BonkEffectProfile bonk = ResolveBonkProfile(effect);
            SpiritSpellProfile spirit = ResolveSpiritProfile(effect);
            AbilityPresentationTrace.Record(currentUpdate, "profiles-end", effect);
            AbilityPresentationTrace.Record(currentUpdate, "vfx-begin", effect);
            if (spirit != null && source != null)
            {
                target.GetComponent<CardDeathFeedback>()?.ClearTargetPreviewCrack();
                yield return GetSpiritView().PlayKill(
                    spirit,
                    animationLayer,
                    source.CardRect,
                    target.CardRect,
                    sfxAudioSource
                );
            }
            else if (bonk != null)
            {
                if (bonkView == null)
                {
                    var viewObject = new GameObject("OngKeBonk", typeof(RectTransform),
                        typeof(BonkEffectView));
                    viewObject.transform.SetParent(animationLayer, false);
                    bonkView = viewObject.GetComponent<BonkEffectView>();
                }
                yield return bonkView.Play(bonk, animationLayer, target.CardRect, sfxAudioSource);
            }
            else if (bite != null)
            {
                if (biteView == null)
                {
                    var viewObject = new GameObject("MaTranhBite", typeof(RectTransform),
                        typeof(BiteEffectView));
                    viewObject.transform.SetParent(animationLayer, false);
                    biteView = viewObject.GetComponent<BiteEffectView>();
                }
                yield return biteView.Play(bite, animationLayer, target.CardRect, sfxAudioSource);
            }
            else if (lightning != null)
            {
                if (lightningView == null)
                {
                    var viewObject = new GameObject("ThanTrungLightning", typeof(RectTransform),
                        typeof(LightningEffectView));
                    viewObject.transform.SetParent(animationLayer, false);
                    lightningView = viewObject.GetComponent<LightningEffectView>();
                }
                yield return lightningView.Play(
                    lightning, animationLayer, source != null ? source.CardRect : null,
                    target.CardRect, GetAbilityGlowSprite(), sfxAudioSource
                );
            }
            else
            {
                yield return PlayAbilityCastPulse(source);

                if (source == null || source.InstanceId != target.InstanceId)
                {
                    yield return PlayAbilityProjectile(
                        sourcePosition,
                        targetPosition,
                        AbilityColor((EffectKind)effect.effectKind)
                    );
                }

                PlayAbilityHitSfx();
                vfxPresenter?.PlayCardHit(targetPosition);
                yield return PlayAbilityImpact(targetPosition);
            }

            AbilityPresentationTrace.Record(currentUpdate, "vfx-end", effect);
            bool killed = IsAbilityDeath(effect);

            if (killed)
            {
                yield return PlayCardDeath(target, effect.targetBefore.instanceId);
                yield return boardPresenter.AnimateCollapseCardInReserveLayout(target);
                yield break;
            }

            if ((EffectKind)effect.effectKind == EffectKind.Damage)
            {
                DamageFeedbackView feedback = target.GetComponent<DamageFeedbackView>();

                if (feedback != null && effect.targetAfter != null)
                {
                    int damage = Mathf.Max(
                        0,
                        effect.targetBefore.health - effect.targetAfter.health
                    );

                    yield return feedback.PlayDamage(damage, effect.targetAfter.health);
                }
            }
        }

        private static bool IsAbilityDeath(AbilityResolutionEventDto effect)
        {
            // Opponent Graveyard identities are filtered out of targetAfter.
            return effect != null && (effect.targetZoneAfter == (int)CardZone.Graveyard
                || (effect.targetAfter != null && effect.targetAfter.health <= 0));
        }

        private SpiritSpellProfile ResolveSpiritProfile(AbilityResolutionEventDto effect)
        {
            if (spiritProfile == null) spiritProfile = Resources.Load<SpiritSpellProfile>("VFX/ThienLinhCaiSpirit");
            EffectKind kind = (EffectKind)effect.effectKind;
            bool originalSpell = effect.abilityId == spiritProfile?.abilityId
                && (kind == EffectKind.Kill || kind == EffectKind.Revive);
            bool sharedRevive = (effect.abilityId == "linh_mieu_summon"
                    && kind == EffectKind.Revive)
                || (effect.abilityId == "ma_co_summon" && kind == EffectKind.Copy);
            return spiritProfile != null && spiritProfile.IsUsable
                && (originalSpell || sharedRevive)
                ? spiritProfile : null;
        }
        private SpiritSpellView GetSpiritView()
        {
            if (spiritView == null)
            {
                var go = new GameObject("ThienLinhCaiSpirit", typeof(RectTransform), typeof(SpiritSpellView));
                go.transform.SetParent(animationLayer, false);
                spiritView = go.GetComponent<SpiritSpellView>();
            }
            return spiritView;
        }

        private FeastEffectProfile ResolveFeastProfile(AbilityResolutionEventDto effect)
        {
            if (!feastProfileLoaded)
            {
                if (maDoiFeast == null) maDoiFeast = Resources.Load<FeastEffectProfile>("VFX/MaDoiFeast");
                feastProfileLoaded = true;
            }
            return maDoiFeast != null && maDoiFeast.IsUsable && effect != null
                && effect.abilityId == maDoiFeast.abilityId
                && (EffectKind)effect.effectKind == EffectKind.Kill ? maDoiFeast : null;
        }

        private BonkEffectProfile ResolveBonkProfile(AbilityResolutionEventDto effect)
        {
            if (!bonkProfileLoaded)
            {
                if (ongKeBonk == null)
                    ongKeBonk = Resources.Load<BonkEffectProfile>("VFX/OngKeBonk");
                bonkProfileLoaded = true;
            }
            return ongKeBonk != null && ongKeBonk.IsUsable
                && effect.abilityId == ongKeBonk.abilityId
                && (EffectKind)effect.effectKind == EffectKind.Kill ? ongKeBonk : null;
        }

        private BiteEffectProfile ResolveBiteProfile(AbilityResolutionEventDto effect)
        {
            if (!biteProfileLoaded)
            {
                if (maTranhBite == null)
                    maTranhBite = Resources.Load<BiteEffectProfile>("VFX/MaTranhBite");
                biteProfileLoaded = true;
            }
            return maTranhBite != null && maTranhBite.IsUsable
                && effect.abilityId == maTranhBite.abilityId
                && (EffectKind)effect.effectKind == EffectKind.Kill ? maTranhBite : null;
        }

        private LightningEffectProfile ResolveLightningProfile(AbilityResolutionEventDto effect)
        {
            if (!lightningProfileLoaded)
            {
                if (thanTrungLightning == null)
                    thanTrungLightning = Resources.Load<LightningEffectProfile>("VFX/ThanTrungLightning");
                lightningProfileLoaded = true;
            }
            return thanTrungLightning != null && thanTrungLightning.IsUsable
                && effect.abilityId == thanTrungLightning.abilityId
                && (EffectKind)effect.effectKind == EffectKind.Kill
                    ? thanTrungLightning : null;
        }

        private RectTransform SwarmNexus(int owner, int viewer)
        {
            bool self = owner == viewer;
            RectTransform target = self ? selfAbilityNexusTarget : opponentAbilityNexusTarget;
            if (target != null) return target;
            DamageFeedbackView feedback = self ? selfNexusFeedback : opponentNexusFeedback;
            return feedback != null ? feedback.transform as RectTransform : (self ? selfNexusTarget : opponentNexusTarget);
        }

        private IEnumerator AnimateBatSwarm(AbilityResolutionEventDto damage, AbilityResolutionEventDto heal, int viewer)
        {
            var source = boardPresenter.PrepareAbilitySource(damage.source, damage.sourceOwner, viewer, damage.sourceZone);
            RectTransform enemy = SwarmNexus(damage.nexusOwner, viewer);
            RectTransform ally = SwarmNexus(damage.sourceOwner, viewer);
            if (source == null || enemy == null || ally == null)
            {
                yield return AnimateAbilityNexusEffect(damage, source, viewer);
                if (heal != null) yield return AnimateAbilityNexusEffect(heal, source, viewer);
                yield break;
            }
            if (batView == null)
            {
                var go = new GameObject("MaCangSungBats", typeof(RectTransform), typeof(BatSwarmView));
                go.transform.SetParent(animationLayer, false);
                batView = go.GetComponent<BatSwarmView>();
            }
            Canvas.ForceUpdateCanvases();
            var enemyFeedback = damage.nexusOwner == viewer ? selfNexusFeedback : opponentNexusFeedback;
            var allyFeedback = damage.sourceOwner == viewer ? selfNexusFeedback : opponentNexusFeedback;
            yield return batView.Play(batProfile, animationLayer, source.CardRect, enemy, ally,
                Mathf.Max(0, damage.nexusHealthBefore-damage.nexusHealthAfter),
                heal == null ? 0 : Mathf.Max(0, heal.nexusHealthAfter-heal.nexusHealthBefore),
                () => enemyFeedback?.PresentHealth(damage.nexusHealthAfter),
                () => { if (heal != null) allyFeedback?.PresentHealth(heal.nexusHealthAfter); }, sfxAudioSource);
        }

        private IEnumerator AnimateAbilityNexusEffect(
            AbilityResolutionEventDto effect,
            NetworkCardVisual source,
            int viewerSeat
        )
        {
            bool targetIsSelf = effect.nexusOwner == viewerSeat;
            DamageFeedbackView feedback = targetIsSelf
                ? selfNexusFeedback
                : opponentNexusFeedback;

            if ((EffectKind)effect.effectKind == EffectKind.Heal)
            {
                feedback?.PresentHealth(effect.nexusHealthAfter);
                yield break;
            }

            RectTransform nexus = targetIsSelf
                ? selfAbilityNexusTarget
                : opponentAbilityNexusTarget;

            if (nexus == null && feedback != null)
            {
                nexus = feedback.transform as RectTransform;
            }

            if (nexus == null)
            {
                nexus = targetIsSelf ? selfNexusTarget : opponentNexusTarget;
            }

            if (nexus == null)
            {
                yield break;
            }

            Canvas.ForceUpdateCanvases();

            Vector3 targetPosition = nexus.TransformPoint(nexus.rect.center);
            Vector3 sourcePosition = source == null
                ? targetPosition + (targetIsSelf ? Vector3.up : Vector3.down) * 150f
                : CardCenter(source);

            yield return PlayAbilityCastPulse(source);
            if (effect.abilityId == "vong_nhi_ally_death")
            {
                if (vfxPresenter != null)
                {
                    yield return vfxPresenter.PlayVongNhiSoulBlast(
                        sourcePosition,
                        targetPosition
                    );
                }

                PlayAbilityHitSfx();
            }
            else
            {
                yield return PlayAbilityProjectile(
                    sourcePosition,
                    targetPosition,
                    AbilityColor((EffectKind)effect.effectKind)
                );

                PlayAbilityHitSfx();
                vfxPresenter?.PlayNexusHit(targetPosition, targetIsSelf);
                yield return PlayAbilityImpact(targetPosition);
            }

            if (feedback != null)
            {
                int damage = Mathf.Max(
                    0,
                    effect.nexusHealthBefore - effect.nexusHealthAfter
                );

                yield return feedback.PlayDamage(damage, effect.nexusHealthAfter);
            }
        }

        /// <summary>Reconstructs a copy in hand without moving it into the roster.</summary>
        private IEnumerator AnimateAbilityCopy(
            AbilityResolutionEventDto effect, NetworkCardVisual source, int viewerSeat)
        {
            NetworkCardVisual copy = boardPresenter.PrepareCopiedHandCard(
                effect.targetAfter, effect.targetOwner, viewerSeat);
            if (copy == null) yield break;

            CardReviveFeedback feedback = copy.GetComponent<CardReviveFeedback>();
            if (feedback == null) feedback = copy.gameObject.AddComponent<CardReviveFeedback>();
            activeReviveFeedbacks.Add(feedback);
            try
            {
                AbilityPresentationTrace.Record(currentUpdate, "vfx-begin", effect);
                SpiritSpellProfile spirit = ResolveSpiritProfile(effect);
                if (spirit != null && source != null)
                {
                    yield return GetSpiritView().PlayRevive(spirit, animationLayer,
                        source.CardRect, copy, feedback, 0, 0, 1f, sfxAudioSource);
                }
                else
                {
                    yield return feedback.Play(copy, 0, 0, abilityReviveDuration, 1f);
                }
                AbilityPresentationTrace.Record(currentUpdate, "vfx-end", effect);
            }
            finally
            {
                feedback.ResetImmediately();
                activeReviveFeedbacks.Remove(feedback);
            }
        }

        private IEnumerator AnimateAbilityRevive(
            AbilityResolutionEventDto effect,
            NetworkCardVisual source,
            int viewerSeat,
            bool reserveCanDrag
        )
        {
            NetworkCardVisual revived = boardPresenter.PrepareRevivedCard(
                effect.targetAfter,
                effect.targetOwner,
                viewerSeat,
                reserveCanDrag
            );

            if (revived == null)
            {
                yield break;
            }

            CanvasGroup group = revived.GetComponent<CanvasGroup>();
            float restingAlpha = group != null ? group.alpha : 1f;

            if (group != null)
            {
                group.alpha = 0f;
            }

            yield return boardPresenter.AnimateReserveSlotExpansionForCard(revived);

            Canvas.ForceUpdateCanvases();
            Vector3 position = CardCenter(revived);

            SpiritSpellProfile spirit = ResolveSpiritProfile(effect);
            if (spirit != null && source != null)
            {
                CardReviveFeedback spiritFeedback = revived.GetComponent<CardReviveFeedback>();
                if (spiritFeedback == null) spiritFeedback = revived.gameObject.AddComponent<CardReviveFeedback>();
                activeReviveFeedbacks.Add(spiritFeedback);
                try
                {
                    AbilityPresentationTrace.Record(currentUpdate, "vfx-begin", effect);
                    yield return GetSpiritView().PlayRevive(spirit, animationLayer, source.CardRect, revived,
                        spiritFeedback, effect.reviveDamageBonus, effect.reviveHealthBonus, restingAlpha, sfxAudioSource);
                    AbilityPresentationTrace.Record(currentUpdate, "vfx-end", effect);
                }
                finally
                {
                    spiritFeedback.ResetImmediately();
                    activeReviveFeedbacks.Remove(spiritFeedback);
                }
                yield break;
            }

            if (source != null)
            {
                yield return PlayAbilityCastPulse(source);
                yield return PlayAbilityProjectile(
                    CardCenter(source),
                    position,
                    reviveFlashColor
                );
            }

            yield return PlayReviveLightning(position);

            CardReviveFeedback feedback = revived.GetComponent<CardReviveFeedback>();

            if (feedback == null)
            {
                feedback = revived.gameObject.AddComponent<CardReviveFeedback>();
            }

            activeReviveFeedbacks.Add(feedback);

            yield return feedback.Play(
                revived,
                effect.reviveDamageBonus,
                effect.reviveHealthBonus,
                abilityReviveDuration,
                restingAlpha
            );

            activeReviveFeedbacks.Remove(feedback);
        }

        private IEnumerator PlayAbilityCastPulse(NetworkCardVisual source)
        {
            if (source?.CardRect == null)
            {
                yield break;
            }

            RectTransform rect = source.CardRect;
            Vector3 original = rect.localScale;
            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, abilityCastPulseDuration);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                rect.localScale = original * (1f + 0.1f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }

            if (rect != null)
            {
                rect.localScale = original;
            }
        }

        private IEnumerator PlayAbilityProjectile(
            Vector3 startWorld,
            Vector3 endWorld,
            Color tint
        )
        {
            Vector2 start = animationLayer.InverseTransformPoint(startWorld);
            Vector2 end = animationLayer.InverseTransformPoint(endWorld);
            Vector2 direction = end - start;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
            Vector2 control = (start + end) * 0.5f + normal * abilityProjectileArc;
            float duration = Mathf.Max(0.05f, abilityProjectileDuration);

            const int trailCount = 6;
            var trail = new Image[trailCount];

            for (int i = 0; i < trailCount; i++)
            {
                trail[i] = CreateAbilityGlow(
                    "AbilityFlameTrail",
                    tint,
                    (i == 0 ? 56f : 36f - i * 3f)
                        * abilityProjectileSizeMultiplier
                );
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                for (int i = 0; i < trailCount; i++)
                {
                    float delayed = Mathf.Clamp01(t - i * 0.045f);
                    Vector2 point = Quadratic(start, control, end, delayed);
                    trail[i].rectTransform.anchoredPosition = point;
                    Color color = tint;
                    color.a = i == 0 ? 1f : 0.65f * (1f - i / (float)trailCount);
                    trail[i].color = color;
                }

                yield return null;
            }

            for (int i = 0; i < trailCount; i++)
            {
                ReleaseAbilityGraphic(trail[i].gameObject);
            }
        }

        private IEnumerator PlayAbilityImpact(Vector3 worldPosition)
        {
            Vector2 center = animationLayer.InverseTransformPoint(worldPosition);
            Image flash = CreateAbilityGlow("AbilityImpactFlash", Color.white, 84f);
            flash.rectTransform.anchoredPosition = center;

            const int smokeCount = 7;
            var smoke = new Image[smokeCount];
            var directions = new Vector2[smokeCount];

            for (int i = 0; i < smokeCount; i++)
            {
                float angle = i * Mathf.PI * 2f / smokeCount;
                directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                smoke[i] = CreateAbilityGlow(
                    "AbilityBlackSmoke",
                    new Color(0.08f, 0.06f, 0.1f, 0.78f),
                    48f
                );
            }

            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, abilityImpactDuration);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                flash.rectTransform.anchoredPosition = center;
                flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.55f, t);
                flash.color = new Color(1f, 0.85f, 0.55f, 1f - t);

                for (int i = 0; i < smokeCount; i++)
                {
                    smoke[i].rectTransform.anchoredPosition = center
                        + directions[i] * Mathf.Lerp(2f, 55f, t);
                    smoke[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(0.45f, 1.45f, t);
                    smoke[i].color = new Color(0.08f, 0.06f, 0.1f, 0.75f * (1f - t));
                }

                yield return null;
            }

            ReleaseAbilityGraphic(flash.gameObject);

            for (int i = 0; i < smokeCount; i++)
            {
                ReleaseAbilityGraphic(smoke[i].gameObject);
            }
        }

        private IEnumerator PlayReviveLightning(Vector3 worldPosition)
        {
            Vector2 center = animationLayer.InverseTransformPoint(worldPosition);
            Image flash = CreateAbilityGlow("ReviveLightningFlash", reviveFlashColor, 120f);
            flash.rectTransform.anchoredPosition = center;

            const int boltCount = 4;
            const int segmentsPerBolt = 3;
            var bolts = new Image[boltCount * segmentsPerBolt];

            for (int i = 0; i < bolts.Length; i++)
            {
                bolts[i] = CreateAbilityBeam("ReviveLightningBolt", reviveFlashColor);
            }

            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, abilityReviveLightningDuration);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1.8f, t);
                flash.color = new Color(0.45f, 0.95f, 1f, 1f - t);

                for (int i = 0; i < boltCount; i++)
                {
                    float angle = i * Mathf.PI * 0.5f + t * 3f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    Vector2 outer = center + direction * Mathf.Lerp(28f, 86f, t);
                    Vector2 firstBend = center + direction * 52f
                        + normal * Mathf.Sin(t * 24f + i * 2f) * 13f;
                    Vector2 secondBend = center + direction * 25f
                        - normal * Mathf.Sin(t * 19f + i * 3f) * 10f;
                    Vector2 inner = center + direction * 4f;
                    Color boltColor = new Color(0.65f, 1f, 1f, 1f - t);

                    SetBeam(bolts[i * 3], outer, firstBend, boltColor);
                    SetBeam(bolts[i * 3 + 1], firstBend, secondBend, boltColor);
                    SetBeam(bolts[i * 3 + 2], secondBend, inner, boltColor);
                }

                yield return null;
            }

            ReleaseAbilityGraphic(flash.gameObject);

            foreach (Image bolt in bolts)
            {
                ReleaseAbilityGraphic(bolt.gameObject);
            }
        }

        private Image CreateAbilityGlow(string objectName, Color color, float size)
        {
            var gameObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image)
            );

            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(animationLayer, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * size;
            rect.SetAsLastSibling();

            Image image = gameObject.GetComponent<Image>();
            image.sprite = GetAbilityGlowSprite();
            image.color = color;
            image.raycastTarget = false;
            activeAbilityGraphics.Add(gameObject);
            return image;
        }

        private Image CreateAbilityBeam(string objectName, Color color)
        {
            var gameObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image)
            );

            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(animationLayer, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);

            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            activeAbilityGraphics.Add(gameObject);
            return image;
        }

        private static void SetBeam(
            Image image,
            Vector2 start,
            Vector2 end,
            Color tint
        )
        {
            Vector2 direction = end - start;
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = (start + end) * 0.5f;
            rect.sizeDelta = new Vector2(direction.magnitude, 3.5f);
            rect.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
            );
            image.color = tint;
        }

        private Sprite GetAbilityGlowSprite()
        {
            if (abilityGlowSprite != null)
            {
                return abilityGlowSprite;
            }

            const int size = 64;
            abilityGlowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            abilityGlowTexture.name = "RuntimeAbilityGlow";
            abilityGlowTexture.wrapMode = TextureWrapMode.Clamp;
            abilityGlowTexture.filterMode = FilterMode.Bilinear;

            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                    float dy = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                    float radial = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, radial * radial);
                }
            }

            abilityGlowTexture.SetPixels(pixels);
            abilityGlowTexture.Apply();
            abilityGlowSprite = Sprite.Create(
                abilityGlowTexture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f)
            );
            return abilityGlowSprite;
        }

        private static Vector2 Quadratic(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float t
        )
        {
            float inverse = 1f - t;
            return inverse * inverse * start
                + 2f * inverse * t * control
                + t * t * end;
        }

        private static Vector3 CardCenter(NetworkCardVisual card)
        {
            RectTransform rect = card.CardRect;
            return rect.TransformPoint(rect.rect.center);
        }

        private Color AbilityColor(EffectKind kind)
        {
            return kind == EffectKind.Damage || kind == EffectKind.HalfNexus
                ? damageProjectileColor
                : killProjectileColor;
        }

        private void ReleaseAbilityGraphic(GameObject gameObject)
        {
            activeAbilityGraphics.Remove(gameObject);

            if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        private void ClearAbilityGraphics()
        {
            if (manaReductionView != null) manaReductionView.ClearVisuals();
            if (spiritView != null) spiritView.ClearVisuals(stopAudio: false);
            if (batView != null) batView.ClearVisuals();
            if (feastView != null) feastView.ClearVisuals();
            if (bonkView != null) bonkView.ClearVisuals();
            if (biteView != null) biteView.ClearVisuals();
            if (lightningView != null) lightningView.ClearVisuals();
            foreach (GameObject graphic in activeAbilityGraphics)
            {
                if (graphic != null)
                {
                    Destroy(graphic);
                }
            }

            activeAbilityGraphics.Clear();
        }

        private void SetAbilityInputBlocked(bool blocked)
        {
            if (abilityInputBlocker == null)
            {
                if (!blocked)
                {
                    return;
                }

                Canvas canvas = animationLayer == null
                    ? null
                    : animationLayer.GetComponentInParent<Canvas>()?.rootCanvas;

                if (canvas == null)
                {
                    return;
                }

                var gameObject = new GameObject(
                    "AbilityGameplayInputBlocker",
                    typeof(RectTransform),
                    typeof(Image)
                );

                abilityInputBlocker = (RectTransform)gameObject.transform;

                Transform topBar = null;

                foreach (RectTransform child in canvas.GetComponentsInChildren<RectTransform>(true))
                {
                    if (child.name == "TopBar")
                    {
                        topBar = child;
                        break;
                    }
                }

                Transform parent = topBar == null ? canvas.transform : topBar.parent;
                abilityInputBlocker.SetParent(parent, false);
                abilityInputBlocker.anchorMin = Vector2.zero;
                abilityInputBlocker.anchorMax = Vector2.one;
                abilityInputBlocker.offsetMin = Vector2.zero;
                abilityInputBlocker.offsetMax = Vector2.zero;

                Image image = gameObject.GetComponent<Image>();
                image.color = Color.clear;
                image.raycastTarget = true;
                gameObject.layer = canvas.gameObject.layer;
                MatchSorting.Apply(gameObject.AddComponent<Canvas>(), MatchSorting.Hud, 10);
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                if (topBar != null)
                {
                    abilityInputBlocker.SetSiblingIndex(topBar.GetSiblingIndex());
                }
                else
                {
                    abilityInputBlocker.SetAsLastSibling();
                }
            }

            abilityInputBlocker.gameObject.SetActive(blocked);
        }

        private void CleanupAbilityPresentation()
        {
            if (manaReductionView != null)
            {
                manaReductionView.gameObject.SetActive(false);
                Destroy(manaReductionView.gameObject);
                manaReductionView = null;
            }
            if (spiritView != null)
            {
                spiritView.gameObject.SetActive(false);
                Destroy(spiritView.gameObject);
                spiritView = null;
            }
            if (batView != null)
            {
                batView.gameObject.SetActive(false);
                Destroy(batView.gameObject);
                batView = null;
            }
            if (feastView != null)
            {
                feastView.gameObject.SetActive(false);
                Destroy(feastView.gameObject);
                feastView = null;
            }
            if (bonkView != null)
            {
                bonkView.gameObject.SetActive(false);
                Destroy(bonkView.gameObject);
                bonkView = null;
            }
            if (biteView != null)
            {
                biteView.gameObject.SetActive(false);
                Destroy(biteView.gameObject);
                biteView = null;
            }
            if (lightningView != null)
            {
                lightningView.gameObject.SetActive(false);
                Destroy(lightningView.gameObject);
                lightningView = null;
            }
            SetAbilityInputBlocked(false);

            if (abilityInputBlocker != null)
            {
                Destroy(abilityInputBlocker.gameObject);
                abilityInputBlocker = null;
            }

            foreach (CardReviveFeedback feedback in activeReviveFeedbacks)
            {
                if (feedback != null)
                {
                    feedback.ResetImmediately();
                }
            }

            activeReviveFeedbacks.Clear();
            ClearAbilityGraphics();

            if (abilityGlowSprite != null)
            {
                Destroy(abilityGlowSprite);
                abilityGlowSprite = null;
            }

            if (abilityGlowTexture != null)
            {
                Destroy(abilityGlowTexture);
                abilityGlowTexture = null;
            }
        }
    }
}
