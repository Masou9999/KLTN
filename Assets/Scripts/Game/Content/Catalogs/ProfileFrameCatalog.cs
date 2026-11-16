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
    }

    [CreateAssetMenu(fileName = "ProfileFrameCatalog", menuName = "KLTN/Profile Frame Catalog")]
    public sealed class ProfileFrameCatalog : ScriptableObject
    {
        public ProfileFrameEntry[] entries;

        // The same rectangle is occupied by text for every tier. Ornament extents may differ.
        public static readonly Rect InformationArea = new Rect(275, 85, 360, 100);

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
