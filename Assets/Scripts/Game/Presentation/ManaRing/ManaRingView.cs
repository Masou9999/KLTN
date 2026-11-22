using System.Collections.Generic;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Mana ring around the action orb: the upper arc shows the opponent's mana and the
    /// lower arc the local player's. Each arc owns a <see cref="ManaGauge"/>; slots are
    /// reusable <see cref="ManaArcSlotGraphic"/> children laid out procedurally from
    /// <see cref="ManaRingLayoutSettings"/> relative to this rect, so the ring scales
    /// with the frame. Keep this object before the frame image in the hierarchy so the
    /// frame draws over the inner end of every slot.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("KLTN/Mana Ring View")]
    public sealed class ManaRingView : MonoBehaviour
    {
        #region Configuration

        private const string UpperSlotPrefix = "OpponentMana_";
        private const string LowerSlotPrefix = "SelfMana_";

        #endregion

        #region Serialized Fields

        [Header("Sprites")]
        [SerializeField]
        private Sprite filledSprite;

        [SerializeField]
        private Sprite reservedSprite;

        [SerializeField]
        private Sprite emptySprite;

        [SerializeField]
        private Color slotTint = Color.white;

        [Header("Layout")]
        [SerializeField]
        private ManaRingLayoutSettings layout = new ManaRingLayoutSettings();

        [Tooltip("Ring centre offset from the rect centre, as a fraction of width.")]
        [SerializeField]
        private Vector2 centerOffset = new Vector2(0.0165f, -0.0057f);

        [SerializeField]
        private ManaSlotWedge slotWedge = ManaSlotWedge.Default;

        [Header("Preview (Inspector only)")]
        [SerializeField]
        private bool previewEnabled;

        [SerializeField, Range(0, 10)]
        private int previewSelfMana = 6;

        [SerializeField, Range(0, 10)]
        private int previewSelfReserved = 2;

        [SerializeField, Range(0, 10)]
        private int previewOpponentMana = 10;

        #endregion

        #region Runtime State

        private ManaGauge self;
        private ManaGauge opponent;
        private ManaArcSlotGraphic[] upperSlots;
        private ManaArcSlotGraphic[] lowerSlots;
        private ManaSlotGeometry[] geometryBuffer;

        #endregion

        #region Events / Properties

        /// <summary>Local player's mana, drawn on the lower arc.</summary>
        public ManaGauge Self => EnsureGauges().self;

        /// <summary>Opponent's mana, drawn on the upper arc.</summary>
        public ManaGauge Opponent => EnsureGauges().opponent;

        public ManaRingLayoutSettings Layout => layout;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            EnsureGauges();
            self.Changed += OnGaugeChanged;
            opponent.Changed += OnGaugeChanged;
            EnsureSlots();
            ApplyPreview();
            RefreshAll();
        }

        private void OnDisable()
        {
            if (self != null)
            {
                self.Changed -= OnGaugeChanged;
            }

            if (opponent != null)
            {
                opponent.Changed -= OnGaugeChanged;
            }
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            // OnValidate must not create objects; slots come from OnEnable/EnsureSlots.
            CollectSlots();
            ApplyPreview();
            RefreshAll();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Creates any missing slot children and drops surplus ones. Safe to call
        /// repeatedly; the editor installer calls it to persist slots in the scene.
        /// </summary>
        public void EnsureSlots()
        {
            int count = layout.SafeSlotsPerArc;
            upperSlots = EnsureArc(UpperSlotPrefix, count);
            lowerSlots = EnsureArc(LowerSlotPrefix, count);
        }

        /// <summary>Re-applies geometry and sprite states to every slot.</summary>
        public void RefreshAll()
        {
            if (upperSlots == null || lowerSlots == null)
            {
                return;
            }

            ApplyGeometry(upperSlots, ManaArcSide.Upper);
            ApplyGeometry(lowerSlots, ManaArcSide.Lower);
            ApplyStates(upperSlots, Opponent);
            ApplyStates(lowerSlots, Self);
        }

        /// <summary>Sets both arcs; reserved mana only applies to the player.</summary>
        public void Show(int selfMana, int selfReserved, int opponentMana)
        {
            Self.SetMana(selfMana);
            Self.SetReserved(selfReserved);
            Opponent.SetMana(opponentMana);
        }

        #endregion

        #region Preview

        [ContextMenu("Preview/Empty")]
        private void PreviewEmpty() => SetPreview(0, 0, 0);

        [ContextMenu("Preview/Half (5 + 5)")]
        private void PreviewHalf() => SetPreview(5, 0, 5);

        [ContextMenu("Preview/Self 8, reserve 2")]
        private void PreviewReserved() => SetPreview(8, 2, 3);

        [ContextMenu("Preview/Full")]
        private void PreviewFull() => SetPreview(10, 0, 10);

        private void SetPreview(int selfMana, int selfReserved, int opponentMana)
        {
            previewEnabled = true;
            previewSelfMana = selfMana;
            previewSelfReserved = selfReserved;
            previewOpponentMana = opponentMana;
            ApplyPreview();
            RefreshAll();
        }

        private void ApplyPreview()
        {
            if (previewEnabled)
            {
                Show(previewSelfMana, previewSelfReserved, previewOpponentMana);
            }
        }

        #endregion

        #region Helpers

        private ManaRingView EnsureGauges()
        {
            int capacity = layout.SafeSlotsPerArc;
            self ??= new ManaGauge(capacity);
            opponent ??= new ManaGauge(capacity);
            self.SetMaxMana(capacity);
            opponent.SetMaxMana(capacity);
            return this;
        }

        private void OnGaugeChanged(ManaGauge gauge)
        {
            if (upperSlots == null || lowerSlots == null)
            {
                return;
            }

            if (gauge == self)
            {
                ApplyStates(lowerSlots, self);
            }
            else
            {
                ApplyStates(upperSlots, opponent);
            }
        }

        private void CollectSlots()
        {
            int count = layout.SafeSlotsPerArc;
            upperSlots = FindArc(UpperSlotPrefix, count);
            lowerSlots = FindArc(LowerSlotPrefix, count);

            if (upperSlots == null || lowerSlots == null)
            {
                upperSlots = null;
                lowerSlots = null;
            }
        }

        private ManaArcSlotGraphic[] FindArc(string prefix, int count)
        {
            var slots = new ManaArcSlotGraphic[count];

            for (int i = 0; i < count; i++)
            {
                Transform child = transform.Find(prefix + i);
                slots[i] = child != null
                    ? child.GetComponent<ManaArcSlotGraphic>()
                    : null;

                if (slots[i] == null)
                {
                    return null;
                }
            }

            return slots;
        }

        private ManaArcSlotGraphic[] EnsureArc(string prefix, int count)
        {
            var slots = new ManaArcSlotGraphic[count];

            for (int i = 0; i < count; i++)
            {
                Transform child = transform.Find(prefix + i);

                if (child == null)
                {
                    var go = new GameObject(
                        prefix + i,
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(ManaArcSlotGraphic)
                    );
                    go.layer = gameObject.layer;
                    child = go.transform;
                    child.SetParent(transform, false);
                }

                var rect = (RectTransform)child;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                ManaArcSlotGraphic slot = child.GetComponent<ManaArcSlotGraphic>();

                if (slot == null)
                {
                    slot = child.gameObject.AddComponent<ManaArcSlotGraphic>();
                }

                slot.raycastTarget = false;
                slots[i] = slot;
            }

            // Drop slots left over from a larger slotsPerArc (collect first: Destroy is
            // deferred in Play Mode).
            var surplus = new List<GameObject>();

            for (int i = count; transform.Find(prefix + i) != null; i++)
            {
                surplus.Add(transform.Find(prefix + i).gameObject);
            }

            foreach (GameObject extra in surplus)
            {
                DestroySafely(extra);
            }

            return slots;
        }

        private void ApplyGeometry(ManaArcSlotGraphic[] slots, ManaArcSide side)
        {
            if (geometryBuffer == null || geometryBuffer.Length != slots.Length)
            {
                geometryBuffer = new ManaSlotGeometry[slots.Length];
            }

            ManaRingLayout.Fill(layout, side, geometryBuffer);

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].SetShape(geometryBuffer[i], centerOffset, slotWedge);
                }
            }
        }

        private void ApplyStates(ManaArcSlotGraphic[] slots, ManaGauge gauge)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                ManaArcSlotGraphic slot = slots[i];

                if (slot == null)
                {
                    continue;
                }

                slot.SetSprite(SpriteFor(gauge.StateOf(i)));
                slot.color = slotTint;
            }
        }

        private Sprite SpriteFor(ManaSlotState state)
        {
            switch (state)
            {
                case ManaSlotState.Filled:
                    return filledSprite;
                case ManaSlotState.Reserved:
                    return reservedSprite != null ? reservedSprite : filledSprite;
                default:
                    return emptySprite;
            }
        }

        private static void DestroySafely(Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        #endregion
    }
}
