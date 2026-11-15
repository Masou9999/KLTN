using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>Resolution-independent circular button, matching the scene's pale outline icons.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MatchLogIcon : MaskableGraphic
    {
        public bool downArrow;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 center = r.center;
            float radius = Mathf.Min(r.width, r.height) * .44f;
            const int segments = 64;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 v = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Quad(vh, center, center + u * radius, center + v * radius, center, new Color32(13, 12, 17, 245));
                Quad(vh, center + u * radius, center + v * radius, center + v * radius * .84f, center + u * radius * .84f, color);
            }
            float scale = radius;
            if (downArrow)
            {
                Line(vh, center + new Vector2(0, .43f) * scale, center + new Vector2(0, -.35f) * scale, scale * .11f);
                Line(vh, center + new Vector2(-.32f, -.04f) * scale, center + new Vector2(0, -.38f) * scale, scale * .11f);
                Line(vh, center + new Vector2(.32f, -.04f) * scale, center + new Vector2(0, -.38f) * scale, scale * .11f);
            }
            else
            {
                Vector2 bl = center + new Vector2(-.35f, -.47f) * scale;
                Vector2 tr = center + new Vector2(.35f, .47f) * scale;
                Line(vh, bl, new Vector2(bl.x, tr.y), scale * .08f);
                Line(vh, new Vector2(bl.x, tr.y), tr, scale * .08f);
                Line(vh, tr, new Vector2(tr.x, bl.y), scale * .08f);
                Line(vh, new Vector2(tr.x, bl.y), bl, scale * .08f);
                for (int i = 0; i < 3; i++)
                    Line(vh, center + new Vector2(-.19f, .24f - i * .23f) * scale,
                        center + new Vector2(.19f, .24f - i * .23f) * scale, scale * .065f);
            }
        }
        private void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
        {
            Vector2 d = (b - a).normalized;
            Vector2 n = new Vector2(-d.y, d.x) * width * .5f;
            Quad(vh, a - n, a + n, b + n, b - n, color);
        }
        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
