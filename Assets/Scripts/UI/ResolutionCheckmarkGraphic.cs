using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>Draws a checkbox tick without depending on font glyph coverage.</summary>
    public sealed class ResolutionCheckmarkGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            Vector2 start = rect.min + Vector2.Scale(rect.size, new Vector2(0.18f, 0.5f));
            Vector2 bend = rect.min + Vector2.Scale(rect.size, new Vector2(0.4f, 0.28f));
            Vector2 end = rect.min + Vector2.Scale(rect.size, new Vector2(0.82f, 0.78f));
            float thickness = Mathf.Min(rect.width, rect.height) * 0.12f;
            Segment(mesh, start, bend, thickness);
            Segment(mesh, bend, end, thickness);
        }

        private void Segment(VertexHelper mesh, Vector2 start, Vector2 end,
            float thickness)
        {
            Vector2 direction = (end - start).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * thickness * 0.5f;
            int first = mesh.currentVertCount;
            mesh.AddVert(start - normal, color, Vector2.zero);
            mesh.AddVert(start + normal, color, Vector2.zero);
            mesh.AddVert(end + normal, color, Vector2.zero);
            mesh.AddVert(end - normal, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
