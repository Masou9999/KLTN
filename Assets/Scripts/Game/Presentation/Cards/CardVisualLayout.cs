using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    public enum CardVisualLocation
    {
        Hand,
        Reserve,
        Board,
        AbilitySelection,
        Mulligan,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(LayoutElement))]
    public sealed class CardVisualLayout : MonoBehaviour
    {
        #region Configuration

        [Header("Design")]
        [SerializeField]
        private RectTransform visualRoot;

        [SerializeField]
        private Vector2 designSize = new Vector2(240f, 320f);

        [Header("Rendered Sizes")]
        [SerializeField]
        private Vector2 handSize = new Vector2(150f, 210f);

        [SerializeField]
        private Vector2 reserveSize = new Vector2(150f, 210f);

        [SerializeField]
        private Vector2 boardSize = new Vector2(250f, 350f);

        [SerializeField]
        private Vector2 abilitySelectionSize = new Vector2(200f, 280f);
        
        [SerializeField]
        private Vector2 mulliganSize = new Vector2(260f, 360f);

        #endregion

        #region Component References

        private RectTransform rectTransform;
        private LayoutElement layoutElement;
        private Vector3 normalVisualScale = Vector3.one;

        #endregion

        #region Properties

        public CardVisualLocation CurrentLocation { get; private set; }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            layoutElement = GetComponent<LayoutElement>();
            ConfigureVisualRoot();
        }

        private void EnsureInitialized()
        {
            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
            }
            
            if (layoutElement == null)
            {
                layoutElement = GetComponent<LayoutElement>();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (designSize.x <= 0f)
            {
                designSize.x = 240f;
            }

            if (designSize.y <= 0f)
            {
                designSize.y = 320f;
            }
        }
#endif

        #endregion

        #region Public Layout API

        public void Apply(CardVisualLocation location)
        {
            EnsureInitialized();
            
            CurrentLocation = location;

            Vector2 targetSize = location switch
            {
                CardVisualLocation.Hand => handSize,
                CardVisualLocation.Reserve => reserveSize,
                CardVisualLocation.Board => boardSize,
                CardVisualLocation.AbilitySelection => abilitySelectionSize,
                CardVisualLocation.Mulligan => mulliganSize,
                _ => boardSize,
            };
            ApplyRootSize(targetSize);
            ApplyVisualScale(targetSize);
            RebuildParentLayout();
        }

        /// <summary>
        /// Emphasizes only the rendered artwork; the layout root keeps its normal size
        /// so neighbouring Hand and Reserve cards do not jump when a target is hovered.
        /// </summary>
        public void SetSelectionEmphasis(
            bool emphasized,
            bool isOpponent,
            float scaleBoost,
            float lift
        )
        {
            if (visualRoot == null)
            {
                return;
            }

            visualRoot.localScale = normalVisualScale
                * (emphasized ? 1f + Mathf.Max(0f, scaleBoost) : 1f);
            visualRoot.anchoredPosition = emphasized
                ? Vector2.up * (isOpponent ? -lift : lift)
                : Vector2.zero;
        }

        #endregion

        #region Root Layout

        private void ApplyRootSize(Vector2 targetSize)
        {
            layoutElement.preferredWidth = targetSize.x;
            layoutElement.preferredHeight = targetSize.y;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
            rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                targetSize.x
            );
            rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                targetSize.y
            );

            // The layout root always remains unscaled.
            rectTransform.localScale = Vector3.one;
        }

        #endregion

        #region Visual Scaling

        private void ConfigureVisualRoot()
        {
            if (visualRoot == null)
            {
                return;
            }

            visualRoot.anchorMin = new Vector2(0.5f, 0.5f);
            visualRoot.anchorMax = new Vector2(0.5f, 0.5f);
            visualRoot.pivot = new Vector2(0.5f, 0.5f);
            visualRoot.anchoredPosition = Vector2.zero;
            visualRoot.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                designSize.x
            );
            visualRoot.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                designSize.y
            );
        }

        private void ApplyVisualScale(Vector2 targetSize)
        {
            if (visualRoot == null)
            {
                return;
            }

            ConfigureVisualRoot();
            float widthScale = targetSize.x / designSize.x;
            float heightScale = targetSize.y / designSize.y;

            // Uniform scale prevents text and artwork distortion.
            float uniformScale = Mathf.Min(widthScale, heightScale);
            normalVisualScale = new Vector3(uniformScale, uniformScale, 1f);
            visualRoot.localScale = normalVisualScale;
        }

        #endregion

        #region Layout Rebuild

        private void RebuildParentLayout()
        {
            if (rectTransform.parent is RectTransform parent)
            {
                LayoutRebuilder.MarkLayoutForRebuild(parent);
            }
        }

        #endregion
    }
}
