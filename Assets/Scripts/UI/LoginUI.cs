using System;
using TMPro;
using UnityEngine;

public sealed class LoginUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("Inputs")]
    [SerializeField]
    private TMP_InputField emailInput;

    [SerializeField]
    private TMP_InputField passwordInput;

    [Header("UI State")]
    [SerializeField]
    private AuthUIManager authUIManager;

    [SerializeField]
    private AuthFeedbackView feedbackView;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        emailInput.contentType = TMP_InputField.ContentType.EmailAddress;
        passwordInput.contentType = TMP_InputField.ContentType.Password;
    }

    private void OnEnable()
    {
        feedbackView?.Clear();

        if (passwordInput != null)
        {
            passwordInput.onSubmit.AddListener(OnPasswordSubmitted);
        }
    }

    private void OnDisable()
    {
        if (passwordInput != null)
        {
            passwordInput.onSubmit.RemoveListener(OnPasswordSubmitted);
        }
    }

    #endregion

    #region UI Events

    public async void OnLoginButtonClicked()
    {
        if (authUIManager == null || authUIManager.IsBusy)
        {
            return;
        }

        string email = emailInput.text.Trim();
        string password = passwordInput.text;
        feedbackView?.Clear();

        if (
            !AuthInputValidator.TryValidateLogin(
                email,
                password,
                out string validationError
            )
        )
        {
            feedbackView?.ShowError(validationError);
            return;
        }

        if (!authUIManager.TryBeginOperation("Đang đăng nhập..."))
        {
            return;
        }

        try
        {
            AuthResultData result = await AuthManager.Instance.ProcessLogin(
                email,
                password
            );

            // Login success causes AppFlowManager to load MainMenu.
            if (this == null)
            {
                return;
            }

            if (!result.Success)
            {
                feedbackView?.ShowError(GetSafeErrorMessage(result));
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);

            if (this != null)
            {
                feedbackView?.ShowError("Đăng nhập thất bại. Vui lòng thử lại.");
            }
        }
        finally
        {
            if (authUIManager != null)
            {
                authUIManager.EndOperation();
            }
        }
    }

    /// <summary>
    /// Sends a password reset email to the address typed in the email field (UC-21).
    /// The confirmation is the same whether or not the account exists.
    /// </summary>
    public async void OnForgotPasswordButtonClicked()
    {
        if (authUIManager == null || authUIManager.IsBusy)
        {
            return;
        }

        string email = emailInput.text.Trim();
        feedbackView?.Clear();

        if (!AuthInputValidator.TryValidatePasswordReset(email, out string validationError))
        {
            feedbackView?.ShowError(validationError);
            return;
        }

        if (!authUIManager.TryBeginOperation("Đang gửi email đặt lại mật khẩu..."))
        {
            return;
        }

        try
        {
            AuthResultData result = await AuthManager.Instance.ProcessPasswordReset(email);

            if (this == null)
            {
                return;
            }

            if (result.Success)
            {
                feedbackView?.ShowSuccess(
                    "Đã gửi email đặt lại mật khẩu (nếu email đã đăng ký). " +
                    "Vui lòng kiểm tra hộp thư.");
            }
            else
            {
                feedbackView?.ShowError(GetSafeResetErrorMessage(result));
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);

            if (this != null)
            {
                feedbackView?.ShowError("Không thể gửi email đặt lại mật khẩu. Vui lòng thử lại.");
            }
        }
        finally
        {
            if (authUIManager != null)
            {
                authUIManager.EndOperation();
            }
        }
    }

    #endregion

    #region Registration Handoff

    public void PrepareAfterRegistration(string email)
    {
        emailInput.text = email;
        passwordInput.text = string.Empty;
        feedbackView?.ShowSuccess("Đăng ký thành công. Bạn có thể đăng nhập ngay.");
    }

    #endregion

    #region Input Events

    private void OnPasswordSubmitted(string _)
    {
        OnLoginButtonClicked();
    }

    #endregion

    #region Error Presentation

    private static string GetSafeErrorMessage(AuthResultData result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        return "Đăng nhập thất bại. Vui lòng thử lại.";
    }

    private static string GetSafeResetErrorMessage(AuthResultData result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        return "Không thể gửi email đặt lại mật khẩu. Vui lòng thử lại.";
    }

    #endregion
}
