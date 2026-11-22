using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Draws one mana slot as an annular sector by warping the sprite's wedge onto the
    /// slot's arc in polar space. Slanted sides become radial and the rims become
    /// concentric with the frame, so neighbouring slots tile without gaps. Swapping the
    /// sprite only changes the texture; the mesh depends on geometry alone.
    /// </summary>
    [AddComponentMenu("KLTN/Mana Arc Slot Graphic")]
    public sealed class ManaArcSlotGraphic : MaskableGraphic
    {
        #region Configuration

        private const int AngularSegments = 8;
        private const int RadialSegments = 4;

        #endregion

        #region Serialized Fields

        [SerializeField]
        private Sprite sprite;

        [SerializeField, HideInInspector]
        private float centerAngle = 180f;

        [SerializeField, HideInInspector]
        private float halfAngle = 3.55f;

        [SerializeField, HideInInspector]
        private float innerRadius = 0.33f;

        [SerializeField, HideInInspector]
        private float outerRadius = 0.459f;

        [SerializeField, HideInInspector]
        private Vector2 centerOffset;

        [SerializeField, HideInInspector]
        private ManaSlotWedge wedge = ManaSlotWedge.Default;

        #endregion

        #region Events / Properties

        public Sprite Sprite => sprite;

        public override Texture mainTexture =>
            sprite != null ? sprite.texture : s_WhiteTexture;

        #endregion

        #region Public API

        /// <summary>Places the slot; rebuilds the mesh only on change.</summary>
        public void SetShape(
            ManaSlotGeometry geometry,
            Vector2 offset,
            ManaSlotWedge shape
        )
        {
            if (
                Mathf.Approximately(centerAngle, geometry.CenterAngle)
                && Mathf.Approximately(halfAngle, geometry.HalfAngle)
                && Mathf.Approximately(innerRadius, geometry.InnerRadius)
                && Mathf.Approximately(outerRadius, geometry.OuterRadius)
                && centerOffset == offset
                && wedge.Equals(shape)
            )
            {
                return;
            }

            centerAngle = geometry.CenterAngle;
            halfAngle = geometry.HalfAngle;
            innerRadius = geometry.InnerRadius;
            outerRadius = geometry.OuterRadius;
            centerOffset = offset;
            wedge = shape;
            SetVerticesDirty();
        }

        /// <summary>
        /// Swaps the sprite. All slot sprites share one wedge, so the vertices stay put;
        /// only the UVs are refreshed for atlas-packed sprites.
        /// </summary>
        public void SetSprite(Sprite value)
        {
            if (sprite == value)
            {
                return;
            }

            sprite = value;
            SetMaterialDirty();
            SetVerticesDirty();
        }

        #endregion

        #region Mesh Generation

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (sprite == null || !wedge.IsValid || halfAngle <= 0f)
            {
                return;
            }

            Rect rect = rectTransform.rect;
            float scale = rect.width;
            Vector2 center = rect.center + centerOffset * scale;
            Vector4 outerUv = DataUtility.GetOuterUV(sprite);
            Color32 tint = color;

            float sideExtent = 1f + Mathf.Max(0f, wedge.sideGlow);
            float rimSpan = wedge.rimOuterRadius - wedge.rimInnerRadius;
            float vMin = -Mathf.Max(0f, wedge.innerGlow) / rimSpan;
            float vMax = 1f + Mathf.Max(0f, wedge.outerGlow) / rimSpan;

            for (int r = 0; r <= RadialSegments; r++)
            {
                float v = Mathf.Lerp(vMin, vMax, r / (float)RadialSegments);
                float targetRadius =
                    Mathf.LerpUnclamped(innerRadius, outerRadius, v) * scale;
                float sourceRadius = Mathf.LerpUnclamped(
                    wedge.rimInnerRadius,
                    wedge.rimOuterRadius,
                    v
                );

                for (int a = 0; a <= AngularSegments; a++)
                {
                    float u = Mathf.Lerp(
                        -sideExtent,
                        sideExtent,
                        a / (float)AngularSegments
                    );

                    // Sprite "up" points away from the ring centre; sprite "right"
                    // maps to decreasing ring angle so the art is not mirrored.
                    float theta = (centerAngle - u * halfAngle) * Mathf.Deg2Rad;
                    float sourceAngle = u * wedge.halfAngle * Mathf.Deg2Rad;

                    Vector2 position = center
                        + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * targetRadius;
                    Vector2 spriteUv = wedge.apex
                        + new Vector2(Mathf.Sin(sourceAngle), Mathf.Cos(sourceAngle))
                            * sourceRadius;
                    Vector2 uv = new Vector2(
                        Mathf.Lerp(outerUv.x, outerUv.z, spriteUv.x),
                        Mathf.Lerp(outerUv.y, outerUv.w, spriteUv.y)
                    );

                    vh.AddVert(position, tint, uv);
                }
            }

            int stride = AngularSegments + 1;

            for (int r = 0; r < RadialSegments; r++)
            {
                for (int a = 0; a < AngularSegments; a++)
                {
                    int i0 = r * stride + a;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride + 1;
                    int i3 = i0 + stride;
                    vh.AddTriangle(i0, i3, i2);
                    vh.AddTriangle(i0, i2, i1);
                }
            }
        }

        #endregion
    }
}
