using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI.Authentication
{
    public sealed class PasswordVisibilityToggle : MonoBehaviour
    {
        [SerializeField] private TMP_InputField input;
        [SerializeField] private Image icon;
        [SerializeField] private Sprite hiddenIcon;
        [SerializeField] private Sprite visibleIcon;

        private void OnEnable() => SetVisible(false);
        private void OnDisable() => SetVisible(false);

        public void Toggle() => SetVisible(input.inputType == TMP_InputField.InputType.Password);

        private void SetVisible(bool visible)
        {
            if (input == null || icon == null) return;
            input.inputType = visible ? TMP_InputField.InputType.Standard : TMP_InputField.InputType.Password;
            icon.sprite = visible ? visibleIcon : hiddenIcon;
            input.ForceLabelUpdate();
        }
    }
}
