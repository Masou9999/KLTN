using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Alpha-blended hand-painted atlas. Shears each row onto its authored endpoints
    /// so changing drawings never moves the source or target. Reveal clips UVs,
    /// rather than compressing the whole drawing into a moving projectile.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LightningBoltGraphic : MaskableGraphic
    {
        private LightningEffectProfile profile;
        private Vector2 start;
        private Vector2 end;
        private float width;
        private float reveal;
        private int frame;

        public override Texture mainTexture => profile != null && profile.atlas != null
            ? profile.atlas : Texture2D.whiteTexture;

        public void SetFrame(LightningEffectProfile effect, Vector2 from, Vector2 to,
            float thickness, int frameIndex, float progress, float alpha)
        {
            bool changedTexture = profile != effect;
            profile = effect;
            start = from;
            end = to;
            width = Mathf.Max(0f, thickness);
            frame = effect != null && effect.IsUsable
                ? Mathf.Clamp(frameIndex, 0, effect.endpointHeights.Length - 1) : 0;
            reveal = Mathf.Clamp01(progress);
            color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            raycastTarget = false;
            maskable = false;
            if (changedTexture) SetMaterialDirty();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (profile == null || !profile.IsUsable || reveal <= 0f) return;
            Vector2 direction = end - start;
            if (direction.sqrMagnitude < 0.01f) return;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
            Vector2 heights = profile.endpointHeights[frame];
            float rowHeight = 1f / profile.endpointHeights.Length;
            float bottom = 1f - (frame + 1) * rowHeight;
            float u0 = profile.horizontalBounds.x;
            float u1 = Mathf.Lerp(u0, profile.horizontalBounds.y, reveal);
            float baseline = Mathf.Lerp(heights.x, heights.y, reveal);
            Vector2 tip = Vector2.Lerp(start, end, reveal);
            vh.AddVert(start - normal * heights.x * width, color, new Vector2(u0, bottom));
            vh.AddVert(start + normal * (1f - heights.x) * width, color,
                new Vector2(u0, bottom + rowHeight));
            vh.AddVert(tip + normal * (1f - baseline) * width, color,
                new Vector2(u1, bottom + rowHeight));
            vh.AddVert(tip - normal * baseline * width, color, new Vector2(u1, bottom));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
