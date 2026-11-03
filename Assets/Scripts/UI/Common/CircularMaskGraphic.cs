using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>
    /// Provides a resolution-independent circular stencil for menu icons.
    /// </summary>
    public sealed class CircularMaskGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            const int segments = 128;
            vertices.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            vertices.AddVert(center, color, new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices.AddVert(center + direction * radius, color,
                    new Vector2(0.5f, 0.5f) + direction * 0.5f);
                if (i > 0)
                {
                    vertices.AddTriangle(0, i, i + 1);
                }
            }
        }
    }
}
