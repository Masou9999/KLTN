using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>One slot-owned flame which follows the occupant across UI reparenting.</summary>
    public sealed class QuyCauFlameView
    {
        #region Runtime State

        private readonly RawImage image;
        private readonly RectTransform layer;
        public QuyCauFlamePlayback Playback { get; } = new QuyCauFlamePlayback();

        #endregion

        #region Construction And Rendering

        public QuyCauFlameView(RectTransform parent, Texture2D texture, int slot)
        {
            layer = parent;
            var go = new GameObject("SupportFlame_" + slot,
                typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.maskable = false;
            image.enabled = false;
        }

        public void Render(RectTransform anchor, QuyCauFlameProfile profile)
        {
            image.enabled = Playback.IsVisible;
            if (!Playback.IsVisible) return;
            int frame = Playback.Frame;
            // Size the visible flame, not the transparent padding of the atlas cell.
            // One shared crop preserves frame alignment throughout intro/loop/outro.
            Rect content = profile.ContentPixels;
            float cellWidth = profile.sheet.width / 4f;
            float cellHeight = profile.sheet.height / 6f;
            image.uvRect = new Rect(
                (frame % 4 * cellWidth + content.x) / profile.sheet.width,
                1f - (frame / 4 * cellHeight + content.yMax) / profile.sheet.height,
                content.width / profile.sheet.width,
                content.height / profile.sheet.height);
            if (anchor == null) return;

            // Keep the same 5:7 frame on empty slots and occupied cards, while
            // inheriting the anchor's animated scale rather than its rectangle size.
            Vector3 horizontal = layer.InverseTransformVector(
                anchor.TransformVector(Vector3.right * profile.frameSize.x));
            Vector3 vertical = layer.InverseTransformVector(
                anchor.TransformVector(Vector3.up * profile.frameSize.y));
            RectTransform rect = image.rectTransform;
            rect.position = anchor.TransformPoint(anchor.rect.center
                + profile.offset);
            rect.rotation = anchor.rotation;
            rect.sizeDelta = new Vector2(horizontal.magnitude, vertical.magnitude)
                * profile.sizeMultiplier;
        }

        #endregion
    }
}
