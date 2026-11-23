using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Neon comet that runs clockwise around a board slot to mark it as a valid drop
    /// target. Visual-only: it never blocks raycasts.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class BoardSlotCometGraphic : MaskableGraphic
    {
        #region Serialized Fields

        [Header("Motion")]
        [Tooltip("Seconds per lap. The reference gif loops in 3.84 s; this runs at half speed.")]
        [Min(0.1f)] [SerializeField] private float loopDuration = 7.68f;
        [Min(0f)] [SerializeField] private float fadeDuration = 0.15f;

        [Header("Shape")]
        [Tooltip("Distance the path sits inside (+) or outside (-) the slot edge.")]
        [SerializeField] private float edgeInset = 0f;
        [Min(1f)] [SerializeField] private float trailLength = 180f;
        [Min(0.5f)] [SerializeField] private float coreWidth = 3f;
        [Min(0.5f)] [SerializeField] private float glowWidth = 12f;
        [Min(0.5f)] [SerializeField] private float headRadius = 6f;
        [Min(0.5f)] [SerializeField] private float haloRadius = 18f;

        [Header("Colors")]
        [SerializeField] private Color coreColor = new Color(0.78f, 1f, 1f, 1f);
        [SerializeField] private Color glowColor = new Color(0.1f, 0.9f, 1f, 0.55f);

        #endregion

        #region Runtime State

        private const int TrailSamples = 28;
        private const int CircleSegments = 20;

        private readonly List<float> sampleDistances = new List<float>(TrailSamples + 8);
        private float headDistance;
        private float fade;
        private bool playing;

        #endregion

        #region Public API

        public bool IsPlaying => playing;

        public void Play()
        {
            if (!gameObject.activeSelf)
            {
                headDistance = 0f;
                fade = 0f;
                gameObject.SetActive(true);
            }

            playing = true;
            SetVerticesDirty();
        }

        /// <summary>Fades out; Update deactivates the object once the fade ends.</summary>
        public void Stop()
        {
            playing = false;
        }

        /// <summary>
        /// Hides without toggling the GameObject, so it is safe to call from a parent's
        /// Awake/OnDisable. The next Update deactivates the object.
        /// </summary>
        public void ResetHidden()
        {
            playing = false;
            fade = 0f;
            SetVerticesDirty();
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            float step = fadeDuration > 0f ? dt / fadeDuration : 1f;
            fade = Mathf.Clamp01(fade + (playing ? step : -step));

            if (!playing && fade <= 0f)
            {
                gameObject.SetActive(false);
                return;
            }

            AdvanceHead(dt);
        }

        private void AdvanceHead(float deltaTime)
        {
            Rect area = rectTransform.rect;
            float length = BoardSlotPerimeter.Length(
                area.width - 2f * edgeInset,
                area.height - 2f * edgeInset
            );
            headDistance = BoardSlotPerimeter.Wrap(
                headDistance + length * deltaTime / loopDuration,
                length
            );

            SetVerticesDirty();
        }

#if UNITY_EDITOR
        // Edit Mode preview: while this GameObject is active in the Hierarchy, the comet
        // loops so its Inspector values can be tuned without entering Play Mode.
        private const double EditorPreviewInterval = 1.0 / 30.0;
        private double lastEditorPreviewTime;

        protected override void OnEnable()
        {
            base.OnEnable();
            lastEditorPreviewTime = 0d;
            UnityEditor.EditorApplication.update += TickEditorPreview;
        }

        protected override void OnDisable()
        {
            UnityEditor.EditorApplication.update -= TickEditorPreview;
            base.OnDisable();
        }

        private void TickEditorPreview()
        {
            if (Application.isPlaying || this == null)
            {
                return;
            }

            double now = UnityEditor.EditorApplication.timeSinceStartup;

            if (lastEditorPreviewTime > 0d && now - lastEditorPreviewTime < EditorPreviewInterval)
            {
                return;
            }

            float dt = lastEditorPreviewTime > 0d ? (float)(now - lastEditorPreviewTime) : 0f;
            lastEditorPreviewTime = now;
            fade = 1f;
            AdvanceHead(Mathf.Min(dt, 0.1f));
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            raycastTarget = false;
            SetVerticesDirty();
        }
#endif

        #endregion

        #region Mesh

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();

            if (fade <= 0f)
            {
                return;
            }

            Rect area = rectTransform.rect;
            float width = area.width - 2f * edgeInset;
            float height = area.height - 2f * edgeInset;
            float length = BoardSlotPerimeter.Length(width, height);

            if (length <= 0f)
            {
                return;
            }

            Vector2 center = area.center;
            float trail = Mathf.Min(trailLength, length * 0.95f);

            BuildSampleDistances(width, height, length, trail);

            DrawTrail(mesh, center, width, height, trail, glowWidth, glowColor);
            DrawTrail(mesh, center, width, height, trail, coreWidth, coreColor);

            Vector2 head = PointAt(center, width, height, headDistance);
            DrawRadialDisc(mesh, head, haloRadius, glowColor, 0f);
            DrawRadialDisc(mesh, head, headRadius, coreColor, coreColor.a * 0.85f);
        }

        /// <summary>
        /// Samples from tail to head, adding the corners inside the trail so the bend
        /// stays a crisp right angle like the reference.
        /// </summary>
        private void BuildSampleDistances(float width, float height, float length, float trail)
        {
            sampleDistances.Clear();
            float tail = headDistance - trail;

            for (int i = 0; i <= TrailSamples; i++)
            {
                sampleDistances.Add(tail + trail * i / TrailSamples);
            }

            for (int corner = 0; corner < 4; corner++)
            {
                float cornerDistance = BoardSlotPerimeter.CornerDistance(width, height, corner);

                for (int lap = -1; lap <= 1; lap++)
                {
                    float candidate = cornerDistance + lap * length;

                    if (candidate > tail && candidate < headDistance)
                    {
                        sampleDistances.Add(candidate);
                    }
                }
            }

            sampleDistances.Sort();
        }

        private void DrawTrail(
            VertexHelper mesh,
            Vector2 center,
            float width,
            float height,
            float trail,
            float thickness,
            Color baseColor
        )
        {
            float tail = headDistance - trail;

            for (int i = 0; i < sampleDistances.Count - 1; i++)
            {
                float d0 = sampleDistances[i];
                float d1 = sampleDistances[i + 1];

                if (d1 - d0 <= 1e-3f)
                {
                    continue;
                }

                Vector2 p0 = PointAt(center, width, height, d0);
                Vector2 p1 = PointAt(center, width, height, d1);
                Vector2 direction = (p1 - p0).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x);

                // 0 at the tail, 1 at the head.
                float t0 = (d0 - tail) / trail;
                float t1 = (d1 - tail) / trail;

                float w0 = thickness * 0.5f * Mathf.Lerp(0.15f, 1f, t0);
                float w1 = thickness * 0.5f * Mathf.Lerp(0.15f, 1f, t1);

                Color32 c0 = Faded(baseColor, t0 * t0);
                Color32 c1 = Faded(baseColor, t1 * t1);

                int start = mesh.currentVertCount;
                mesh.AddVert(p0 + normal * w0, c0, Vector4.zero);
                mesh.AddVert(p0 - normal * w0, c0, Vector4.zero);
                mesh.AddVert(p1 - normal * w1, c1, Vector4.zero);
                mesh.AddVert(p1 + normal * w1, c1, Vector4.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }
        }

        private void DrawRadialDisc(
            VertexHelper mesh,
            Vector2 centerPoint,
            float radius,
            Color baseColor,
            float edgeAlpha
        )
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(centerPoint, Faded(baseColor, 1f), Vector4.zero);

            Color edge = baseColor;
            edge.a = edgeAlpha;
            Color32 edgeColor = Faded(edge, 1f);

            for (int i = 0; i <= CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                mesh.AddVert(centerPoint + offset, edgeColor, Vector4.zero);
            }

            for (int i = 0; i < CircleSegments; i++)
            {
                mesh.AddTriangle(start, start + 1 + i, start + 2 + i);
            }
        }

        private static Vector2 PointAt(Vector2 center, float width, float height, float distance)
        {
            BoardSlotPerimeter.Evaluate(width, height, distance, out float x, out float y);
            return center + new Vector2(x, y);
        }

        private Color32 Faded(Color source, float strength)
        {
            Color tinted = source * color;
            tinted.a *= Mathf.Clamp01(strength) * fade;
            return tinted;
        }

        #endregion
    }
}
