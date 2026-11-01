using System.Text.RegularExpressions;

public static class AuthInputValidator
{
    #region Constants

    private const int MinimumPasswordLength = 6;
    private const int MaximumPasswordLength = 128;
    private const int MaximumEmailLength = 254;
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

    #endregion

    #region Login Validation

    public static bool TryValidateLogin(string email, string password, out string errorMessage)
    {
        if (!TryValidateEmail(email, out errorMessage)) { return false; }

        if (!TryValidatePassword(password, out errorMessage)) { return false; }

        errorMessage = string.Empty;
        return true;
    }

    #endregion

    #region Registration Validation

    public static bool TryValidateRegistration(
        string email,
        string password,
        string confirmPassword,
        out string errorMessage)
    {
        if (!TryValidateEmail(email, out errorMessage)) { return false; }

        if (!TryValidatePassword(password, out errorMessage)) { return false; }

        if (string.IsNullOrEmpty(confirmPassword))
        {
            errorMessage = "Vui lòng nhập lại mật khẩu.";
            return false;
        }

        if (password != confirmPassword)
        {
            errorMessage = "Mật khẩu nhập lại không khớp.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    #endregion

    #region Password Reset Validation

    public static bool TryValidatePasswordReset(string email, out string errorMessage)
    {
        return TryValidateEmail(email, out errorMessage);
    }

    #endregion

    #region Field Validation

    private static bool TryValidateEmail(string email, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            errorMessage = "Vui lòng nhập email.";
            return false;
        }

        if (email.Length > MaximumEmailLength)
        {
            errorMessage = "Email quá dài.";
            return false;
        }

        if (!EmailPattern.IsMatch(email))
        {
            errorMessage = "Định dạng email không hợp lệ.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryValidatePassword(string password, out string errorMessage)
    {
        if (string.IsNullOrEmpty(password))
        {
            errorMessage = "Vui lòng nhập mật khẩu.";
            return false;
        }

        if (password.Length < MinimumPasswordLength)
        {
            errorMessage = $"Mật khẩu phải có ít nhất {MinimumPasswordLength} ký tự.";
            return false;
        }

        if (password.Length > MaximumPasswordLength)
        {
            errorMessage =
                $"Mật khẩu không được vượt quá " +
                $"{MaximumPasswordLength} ký tự.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    #endregion
}
