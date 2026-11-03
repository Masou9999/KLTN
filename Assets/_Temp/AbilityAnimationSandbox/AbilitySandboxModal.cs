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
            foreach (Row row in rows)
            {
                Row captured = row;
                row.minus.onClick.AddListener(() => Change(captured, -1));
                row.plus.onClick.AddListener(() => Change(captured, 1));
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
                    ? (row.id == "MaLon" || row.id == "VongNhi")
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
