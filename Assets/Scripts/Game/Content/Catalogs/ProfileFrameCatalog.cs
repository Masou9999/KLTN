using System;
using KLTN.Game.Domain.Ranking;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Content
{
    [Serializable]
    public sealed class ProfileFrameEntry
    {
        public RankTier tier;
        public Sprite sprite;
        [Tooltip("Usable information rectangle in source pixels, measured from the TOP left.")]
        public Rect informationRect;
        [Tooltip("Avatar opening in source pixels, measured from the TOP left.")]
        public Rect avatarRect;
    }

    [CreateAssetMenu(fileName = "ProfileFrameCatalog", menuName = "KLTN/Profile Frame Catalog")]
    public sealed class ProfileFrameCatalog : ScriptableObject
    {
        public ProfileFrameEntry[] entries;

        // The same rectangle is occupied by text for every tier. Ornament extents may differ.
        public static readonly Rect InformationArea = new Rect(275, 85, 360, 100);

        /// <summary>Aligns the avatar to the selected frame's un-stretched left ornament.</summary>
        /// <summary>
        /// Centres a square avatar holder (a child of <paramref name="frame"/>) in the
        /// frame's measured portrait opening, slightly inset so it never covers the rim.
        /// </summary>
        public void AlignAvatar(Image frame, RectTransform holder)
        {
            var entry = Array.Find(entries, item => item.sprite == frame.sprite);
            if (entry == null || entry.avatarRect.width <= 0) return;
            float scale = InformationArea.height / entry.informationRect.height;
            Rect source = entry.avatarRect;
            // The left border of the sliced frame is drawn at uniform scale, so sprite
            // pixels map to frame-local units with the same factor.
            holder.anchorMin = holder.anchorMax = new Vector2(0, 1);
            holder.pivot = new Vector2(.5f, .5f);
            holder.anchoredPosition = new Vector2(source.center.x, -source.center.y) * scale;
            float side = Mathf.Min(source.width, source.height) * scale * .9f;
            holder.sizeDelta = new Vector2(side, side);
        }

        public void Apply(Image image, RankTier tier)
        {
            var entry = Array.Find(entries, item => item.tier == tier);
            if (entry == null || entry.sprite == null) return;
            Rect source = entry.informationRect;
            float scale = InformationArea.height / source.height;
            float width = InformationArea.width + (entry.sprite.rect.width - source.width) * scale;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(width, entry.sprite.rect.height * scale);
            rect.anchoredPosition = new Vector2(InformationArea.x - source.x * scale,
                -InformationArea.y + source.y * scale);
            image.sprite = entry.sprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            // Horizontal borders preserve the avatar and the right ornament at uniform scale.
            float referencePPU = image.canvas != null ? image.canvas.referencePixelsPerUnit : 100;
            image.pixelsPerUnitMultiplier = referencePPU / (entry.sprite.pixelsPerUnit * scale);
            image.color = Color.white;
            image.raycastTarget = false;
        }
    }
}
