using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KLTN.UI.Authentication
{
    /// <summary>Optional navigation used only by ImproveLoginScene.</summary>
    public sealed class ImprovedAuthNavigation : MonoBehaviour
    {
        [SerializeField] private AuthUIManager manager;
        [SerializeField] private LoginUI login;
        [SerializeField] private AuthFeedbackView feedback;
        [SerializeField] private GameObject loginForm;
        [SerializeField] private GameObject loginMethods;
        [SerializeField] private GameObject registerForm;
        [SerializeField] private GameObject recoveryForm;
        [SerializeField] private TMP_InputField loginEmail;
        [SerializeField] private TMP_InputField registerEmail;
        [SerializeField] private TMP_InputField recoveryEmail;

        private void OnEnable() => recoveryEmail.onSubmit.AddListener(SubmitRecovery);
        private void OnDisable() => recoveryEmail.onSubmit.RemoveListener(SubmitRecovery);

        private void LateUpdate()
        {
            var keyboard = Keyboard.current;
            var events = EventSystem.current;
            if (manager.IsBusy || keyboard == null || events == null || !keyboard.tabKey.wasPressedThisFrame) return;
            var controls = GetComponentsInChildren<Selectable>()
                .Where(control => control.IsInteractable()).ToArray();
            if (controls.Length == 0) return;
            var selected = events.currentSelectedGameObject != null
                ? events.currentSelectedGameObject.GetComponent<Selectable>() : null;
            int direction = keyboard.shiftKey.isPressed ? -1 : 1;
            int current = Array.IndexOf(controls, selected);
            int next = current < 0 ? (direction > 0 ? 0 : controls.Length - 1)
                : (current + direction + controls.Length) % controls.Length;
            events.SetSelectedGameObject(controls[next].gameObject);
            if (controls[next] is TMP_InputField input) input.ActivateInputField();
        }

        public void ShowLogin()
        {
            if (manager.IsBusy) return;
            recoveryForm.SetActive(false);
            manager.ShowLogin();
            feedback.Clear();
            Select(loginEmail);
        }

        public void ShowRegister()
        {
            if (manager.IsBusy) return;
            recoveryForm.SetActive(false);
            manager.ShowRegister();
            feedback.Clear();
            Select(registerEmail);
        }

        public void ShowRecovery()
        {
            if (manager.IsBusy) return;
            recoveryEmail.text = loginEmail.text.Trim();
            loginForm.SetActive(false);
            loginMethods.SetActive(false);
            registerForm.SetActive(false);
            recoveryForm.SetActive(true);
            feedback.Clear();
            Select(recoveryEmail);
        }

        public void SendRecovery()
        {
            if (manager.IsBusy) return;
            // Reuse the existing validation, safe response and asynchronous operation lock.
            loginEmail.text = recoveryEmail.text.Trim();
            login.OnForgotPasswordButtonClicked();
        }

        private void SubmitRecovery(string _) => SendRecovery();

        private static void Select(TMP_InputField field)
        {
            if (!Application.isPlaying || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
        }
    }
}
