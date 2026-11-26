using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Shows a rounded text tip while the pointer is over a &lt;link&gt; in a TMP text.
    /// The tip's top-right corner follows the cursor, so it opens to the cursor's
    /// bottom-left, and it hides as soon as the pointer leaves the link.
    /// </summary>
    public sealed class KeywordTooltip : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        #region Configuration

        private const float MaxWidth = 340;
        private static readonly Vector2 Padding = new Vector2(44, 32);
        private static readonly Vector2 CursorGap = new Vector2(-6, -6);

        #endregion

        #region Runtime State

        private TMP_Text source;
        private RectTransform tip;
        private TMP_Text label;
        private Canvas canvas;
        private Func<string, string> describe;
        private bool inside;
        private int shownLink = -1;

        #endregion

        #region Public Commands

        public void Initialize(TMP_Text text, RectTransform tipRoot, TMP_Text tipLabel,
            Func<string, string> description)
        {
            source = text;
            tip = tipRoot;
            label = tipLabel;
            describe = description;
            canvas = text.canvas != null ? text.canvas.rootCanvas : null;
            Hide();
        }

        public void OnPointerEnter(PointerEventData eventData) => inside = true;

        public void OnPointerExit(PointerEventData eventData)
        {
            inside = false;
            Hide();
        }

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            if (!inside || source == null || tip == null || Mouse.current == null) return;
            if (canvas == null) canvas = source.canvas != null ? source.canvas.rootCanvas : null;
            if (canvas == null) return;
            Vector2 mouse = Mouse.current.position.ReadValue();
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            int link = TMP_TextUtilities.FindIntersectingLink(source, mouse, cam);
            if (link < 0)
            {
                Hide();
                return;
            }
            if (link != shownLink)
            {
                Show(source.textInfo.linkInfo[link].GetLinkID());
                shownLink = link;
            }
            Follow(mouse, cam);
        }

        private void OnDisable()
        {
            inside = false;
            Hide();
        }

        #endregion

        #region Helpers

        private void Show(string id)
        {
            string text = describe?.Invoke(id);
            if (string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }
            label.text = text;
            Vector2 size = label.GetPreferredValues(text);
            if (size.x > MaxWidth) size = label.GetPreferredValues(text, MaxWidth, 0);
            size.x = Mathf.Min(size.x, MaxWidth);
            tip.sizeDelta = size + Padding;
            tip.SetAsLastSibling();
            tip.gameObject.SetActive(true);
        }

        private void Follow(Vector2 mouse, Camera cam)
        {
            var area = (RectTransform)tip.parent;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, mouse, cam,
                out Vector2 local)) return;
            // Keep the whole tip on screen when the cursor is near the left/bottom edge.
            Rect bounds = area.rect;
            Vector2 size = tip.sizeDelta;
            local += CursorGap;
            local.x = Mathf.Max(local.x, bounds.xMin + size.x);
            local.y = Mathf.Max(local.y, bounds.yMin + size.y);
            tip.anchoredPosition = local;
        }

        private void Hide()
        {
            shownLink = -1;
            if (tip != null) tip.gameObject.SetActive(false);
        }

        #endregion
    }
}
