#if UNITY_EDITOR
using System;
using System.Linq;
using KLTN.Game.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Sandbox
{
    /// <summary>
    /// Edits a local count draft; confirm applies it, cancel discards it.
    /// </summary>
    public sealed class AbilitySandboxModal : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public string id;
            public bool reserve;
            public Button minus;
            public Button plus;
            public TMP_Text count;
            [NonSerialized] public int value;
        }

        #region Scene References

        public Row[] rows;
        public TMP_Text reserveTitle;
        public TMP_Text handTitle;
        public TMP_Text errorLabel;
        public Button confirmButton;
        public Button cancelButton;

        #endregion

        #region Runtime And Lifecycle

        private AbilitySandboxRunner runner;

        private void Awake()
        {
            // Upgrade existing sandbox scenes in memory, without rebuilding/saving them.
            foreach (bool reserve in new[] { true, false })
            {
                string[] ids = reserve
                    ? AbilitySandboxModel.ReserveIds : AbilitySandboxModel.HandIds;
                foreach (string id in ids)
                {
                    if (rows.Any(r => r.id == id && r.reserve == reserve)) continue;
                    Row template = rows.Last(r => r.reserve == reserve);
                    var rect = Instantiate((RectTransform)template.count.transform.parent,
                        template.count.transform.parent.parent);
                    rect.name = (reserve ? "Reserve_" : "Hand_") + id;
                    var card = Resources.Load<CMCMProductions.Card>("Cards/" + id);
                    rect.Find("Name").GetComponent<TMP_Text>().text = card.cardName;
                    var row = new Row { id = id, reserve = reserve,
                        minus = rect.Find("Minus").GetComponent<Button>(),
                        plus = rect.Find("Plus").GetComponent<Button>(), count = rect.Find("Count").GetComponent<TMP_Text>() };
                    row.minus.onClick = new Button.ButtonClickedEvent();
                    row.plus.onClick = new Button.ButtonClickedEvent();
                    rows = rows.Concat(new[] { row }).ToArray();
                }
            }
            foreach (bool reserve in new[] { true, false })
            {
                int index = 0;
                foreach (Row row in rows.Where(r => r.reserve == reserve))
                {
                    var rect = (RectTransform)row.count.transform.parent;
                    rect.anchoredPosition = new Vector2(reserve ? -330 : 330, 170 - index++ * 52);
                    rect.sizeDelta = new Vector2(600, 50);
                }
            }
            foreach (Row row in rows)
            {
                Row captured = row;
                row.minus.onClick.AddListener(() => Change(captured, -1));
                row.plus.onClick.AddListener(() => Change(captured, 1));
            }
            var hint = handTitle.transform.parent.Find("ReserveHint") as RectTransform;
            if (hint != null)
            {
                hint.anchoredPosition = new Vector2(330, -230);
                hint.sizeDelta = new Vector2(590, 80);
                hint.GetComponent<TMP_Text>().text =
                    "Ma Da có thể thêm thẳng vào Reserve.\n"
                    + "Nút − chỉ xóa, không kích hoạt chết.";
            }
            confirmButton.onClick.AddListener(Confirm);
            cancelButton.onClick.AddListener(Cancel);
        }

        public void Open(AbilitySandboxRunner owner)
        {
            runner = owner;
            foreach (Row row in rows)
            {
                row.value = runner.Model.Count(row.id, row.reserve);
            }
            errorLabel.text = string.Empty;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
        }

        #endregion

        #region Draft Editing

        private void Change(Row row, int delta)
        {
            if (delta < 0 ? row.minus.interactable : row.plus.interactable)
            {
                row.value += delta;
                Refresh();
            }
        }

        private void Refresh()
        {
            int reserve = rows.Where(row => row.reserve).Sum(row => row.value);
            int hand = rows.Where(row => !row.reserve).Sum(row => row.value);
            reserveTitle.text = $"RESERVE ZONE  {reserve}/4";
            handTitle.text = $"DRAW HAND  {hand}/{AbilitySandboxModel.HandLimit}";
            foreach (Row row in rows)
            {
                row.count.text = row.value.ToString();
                row.minus.interactable = row.value > 0;
                row.plus.interactable = row.reserve
                    ? AbilitySandboxModel.CanAddDirectlyToReserve(row.id)
                        && reserve + runner.Model.State.Host.Board.Count
                            < MatchState.MaximumActiveRosterSize
                    : hand < AbilitySandboxModel.HandLimit;
            }
        }

        public void Confirm()
        {
            int[] reserve = rows.Where(row => row.reserve)
                .Select(row => row.value).ToArray();
            int[] hand = rows.Where(row => !row.reserve)
                .Select(row => row.value).ToArray();
            if (runner.ApplyCounts(reserve, hand))
            {
                gameObject.SetActive(false);
            }
            else
            {
                errorLabel.text = "Không thể áp dụng. Đóng và mở lại để cập nhật.";
            }
        }

        public void Cancel()
        {
            gameObject.SetActive(false);
        }

        #endregion
    }
}
#endif
