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
        [Header("Ability Nexus HUD Targets")]
        [SerializeField] private RectTransform selfAbilityNexusTarget;
        [SerializeField] private RectTransform opponentAbilityNexusTarget;

        private readonly List<GameObject> activeAbilityGraphics = new List<GameObject>();
        private readonly List<CardReviveFeedback> activeReviveFeedbacks =
            new List<CardReviveFeedback>();

        private RectTransform abilityInputBlocker;
        private Sprite abilityGlowSprite;
        private Texture2D abilityGlowTexture;

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

            for (int i = 0; i < events.Length; i++)
            {
                AbilityResolutionEventDto effect = events[i];

                if (effect == null)
                {
                    continue;
                }

                if (i == 0 || !BelongsToSameAbility(events[i - 1], effect))
                {
                    PrepareAbilityLethalPreviews(events, i, viewerSeat);
                }

                yield return AnimateAbilityEffect(effect, viewerSeat, reserveCanDrag);
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
                    || effect.targetAfter == null
                    || effect.targetAfter.health > 0
                )
                {
                    continue;
                }

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
                && first.abilityId == second.abilityId
                && first.source?.instanceId == second.source?.instanceId;
        }

        private IEnumerator AnimateAbilityEffect(
            AbilityResolutionEventDto effect,
            int viewerSeat,
            bool reserveCanDrag
        )
        {
            EffectKind kind = (EffectKind)effect.effectKind;

            if (kind == EffectKind.Revive && effect.targetAfter != null)
            {
                yield return AnimateAbilityRevive(effect, viewerSeat, reserveCanDrag);
                yield break;
            }

            NetworkCardVisual source = boardPresenter.PrepareAbilitySource(
                effect.source,
                effect.sourceOwner,
                viewerSeat,
                effect.sourceZone,
                centerNewSource: true
            );

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

            Vector3 targetPosition = CardCenter(target);
            Vector3 sourcePosition = source == null
                ? targetPosition + Vector3.down * 100f
                : CardCenter(source);

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

            bool killed = effect.targetAfter != null
                && effect.targetAfter.health <= 0;

            if (killed)
            {
                CardDeathFeedback death = target.GetComponent<CardDeathFeedback>();

                if (death != null)
                {
                    activeDeathFeedbacks.Add(death);
                    yield return death.PlayDeath(
                        effect.targetBefore.instanceId,
                        PlayDeathDust
                    );
                }

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
            yield return PlayAbilityProjectile(
                sourcePosition,
                targetPosition,
                AbilityColor((EffectKind)effect.effectKind)
            );

            PlayAbilityHitSfx();
            vfxPresenter?.PlayNexusHit(targetPosition, targetIsSelf);
            yield return PlayAbilityImpact(targetPosition);

            if (feedback != null)
            {
                int damage = Mathf.Max(
                    0,
                    effect.nexusHealthBefore - effect.nexusHealthAfter
                );

                yield return feedback.PlayDamage(damage, effect.nexusHealthAfter);
            }
        }

        private IEnumerator AnimateAbilityRevive(
            AbilityResolutionEventDto effect,
            int viewerSeat,
            bool reserveCanDrag
        )
        {
            NetworkCardVisual source = boardPresenter.PrepareAbilitySource(
                effect.source,
                effect.sourceOwner,
                viewerSeat,
                effect.sourceZone,
                centerNewSource: true
            );

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

            Canvas.ForceUpdateCanvases();
            Vector3 position = CardCenter(revived);

            CanvasGroup group = revived.GetComponent<CanvasGroup>();
            float restingAlpha = group != null ? group.alpha : 1f;

            if (group != null)
            {
                group.alpha = 0f;
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
