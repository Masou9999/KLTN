using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public class AuthManager : MonoBehaviour
{
    #region Constants

    private const string PREF_LOGIN_METHOD = "login_method";
    private const string PREF_FIREBASE_REFRESH_TOKEN = "firebase_refresh_token";

    #endregion

    #region Singleton

    public static AuthManager Instance { get; private set; }

    #endregion

    #region Events

    public static event Action OnLoginSuccess;
    public static event Action OnLogoutSuccess;

    #endregion

    #region Dependencies

    private IAuthService authService;

    #endregion

    #region Static Lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
        OnLoginSuccess = null;
        OnLogoutSuccess = null;
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeAuth();
    }

    private void OnDestroy()
    {
        if (Instance == this) { Instance = null; }
    }

    #endregion

    #region Service Selection

    private void InitializeAuth()
    {
#if UNITY_STANDALONE || UNITY_WEBGL
        // WebGL also uses REST: the key is downloaded from StreamingAssets on first use.
        authService = new FirebaseRestAuthService();
        Debug.Log("Initialized Firebase REST Auth for Standalone/WebGL platform.");

#elif UNITY_ANDROID || UNITY_IOS
        authService = new FirebaseSDKAuthService();
        Debug.Log("Initialized Firebase SDK Auth for Mobile platform.");

#else
        Debug.LogWarning("Current platform is not configured for Firebase REST Auth.");
        authService = new FirebaseRestAuthService();

#endif
    }

    #endregion

    #region Session Access

    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(5);
    private Task<AuthResultData> pendingTokenRefresh;

    /// <summary>Firebase UID of the signed-in player, or null.</summary>
    public string CurrentUserId => authService != null && authService.IsLoggedIn ? authService.UserId : null;

    /// <summary>Reads display-only email from this authenticated Firebase session.</summary>
    public async Task<string> GetCurrentEmailAsync()
    {
        string uid = CurrentUserId;
        if (string.IsNullOrEmpty(uid)) return null;
        if (!string.IsNullOrEmpty(authService.Email)) return authService.Email;
        // Refresh-token responses omit email. The Firebase-issued ID token retains it.
        string token = await GetValidIdTokenAsync();
        if (uid != CurrentUserId || string.IsNullOrEmpty(token)) return null;
        try
        {
            string[] parts = token.Split('.');
            if (parts.Length != 3) return null;
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var claims = Newtonsoft.Json.Linq.JObject.Parse(
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            // This is display data, never an authorization source.
            return (string)claims["sub"] == uid ? (string)claims["email"] : null;
        }
        catch (FormatException) { return null; }
        catch (Newtonsoft.Json.JsonException) { return null; }
    }

    /// <summary>
    /// Returns a Firebase ID token valid for at least a few more minutes,
    /// refreshing it through the secure token endpoint when needed.
    /// Returns null when no player is signed in or the refresh fails.
    /// </summary>
    public async Task<string> GetValidIdTokenAsync()
    {
        if (authService == null || !authService.IsLoggedIn)
        {
            return null;
        }

        if (DateTime.UtcNow + TokenRefreshMargin < authService.IdTokenExpiresAtUtc)
        {
            return authService.IdToken;
        }

        if (string.IsNullOrEmpty(authService.RefreshToken))
        {
            Debug.LogWarning("[Auth] ID token is about to expire and no refresh token is available.");
            return authService.IdToken;
        }

        // Share one in-flight refresh between concurrent callers.
        pendingTokenRefresh ??= authService.LoginWithRefreshTokenAsync(authService.RefreshToken);

        try
        {
            AuthResultData result = await pendingTokenRefresh;

            if (!result.Success)
            {
                Debug.LogWarning($"[Auth] ID token refresh failed: {result.ErrorMessage}");
                return null;
            }

            if (PlayerPrefs.GetString(PREF_LOGIN_METHOD, "") == "email" && !string.IsNullOrEmpty(result.RefreshToken))
            {
                PlayerPrefs.SetString(PREF_FIREBASE_REFRESH_TOKEN, result.RefreshToken);
                PlayerPrefs.Save();
            }

            return result.IdToken;
        }
        finally
        {
            pendingTokenRefresh = null;
        }
    }

    #endregion

    #region Authentication Commands

    public async Task<AuthResultData> ProcessRegister(string email, string password)
    {
        try
        {
            AuthResultData result = await authService.RegisterAsync(email, password);

            if (!result.Success)
            {
                Debug.LogWarning($"Firebase registration failed: {result.ErrorCode}");
                return result;
            }

            // Firebase sign-up also creates an authenticated Firebase session.
            // The current flow requires the player to log in explicitly afterward.
            authService.Logout();

            return new AuthResultData
            {
                Success = true,
                UserId = result.UserId,
                Email = result.Email,
            };
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            authService?.Logout();
            return AuthResultData.Failure("REGISTER_EXCEPTION", "Đã xảy ra lỗi khi đăng ký. Vui lòng thử lại.");
        }
    }

    public async Task<AuthResultData> ProcessLogin(string email, string password)
    {
        try
        {
            AuthResultData result = await authService.LoginAsync(email, password);

            if (!result.Success ||
                string.IsNullOrEmpty(result.IdToken))
            {
                Debug.LogWarning($"Firebase login failed: {result.ErrorCode}");
                return result;
            }

            bool ugsSuccess = await LoginFirebaseToUGS(result.IdToken);

            if (!ugsSuccess)
            {
                authService.Logout();
                return AuthResultData.Failure("UGS_LOGIN_FAILED", "Không thể kết nối dịch vụ game. Vui lòng thử lại.");
            }

            PlayerPrefs.SetString(PREF_LOGIN_METHOD, "email");

            if (!string.IsNullOrEmpty(result.RefreshToken)) { PlayerPrefs.SetString(PREF_FIREBASE_REFRESH_TOKEN, result.RefreshToken); }

            PlayerPrefs.Save();
            Debug.Log($"Authentication completed for user {result.UserId}.");
            int listenerCount = OnLoginSuccess?.GetInvocationList().Length ?? 0;
            Debug.Log($"OnLoginSuccess listener count: {listenerCount}");
            if (await KLTN.Audio.AudioVolumeSettings.LoadAccountAsync(CurrentUserId))
            {
                OnLoginSuccess?.Invoke();
            }
            return result;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            authService?.Logout();
            return AuthResultData.Failure("LOGIN_EXCEPTION", "Đã xảy ra lỗi khi đăng nhập. Vui lòng thử lại.");
        }
    }

    public async Task<bool> ProcessGoogleLogin(string googleIdToken)
    {
        try
        {
            Debug.Log("Bắt đầu đăng nhập Google qua Firebase...");

            // Exchange the Google token for a Firebase identity.
            AuthResultData result = await authService.LoginWithGoogleAsync(googleIdToken);

            if (!result.Success || string.IsNullOrEmpty(result.IdToken))
            {
                Debug.LogError($"Firebase Google Login failed: {result.ErrorMessage}");
                return false;
            }

            Debug.Log($"Firebase Google Login success. User ID: {result.UserId}");

            // Exchange the Firebase identity for a UGS identity.
            bool ugsSuccess = await LoginFirebaseToUGS(result.IdToken);
            if (!ugsSuccess)
            {
                authService.Logout();
                return false;
            }
            Debug.Log("Xác thực Google -> Firebase -> UGS thành công!");
            PlayerPrefs.SetString(PREF_LOGIN_METHOD, "google");
            PlayerPrefs.Save();
            if (!await KLTN.Audio.AudioVolumeSettings.LoadAccountAsync(CurrentUserId)) return false;
            OnLoginSuccess?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Lỗi hệ thống khi đăng nhập Google: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sends a Firebase password reset email (FR-AUTH-10).
    /// An unknown email is reported as success so the result never reveals
    /// whether an account exists.
    /// </summary>
    public async Task<AuthResultData> ProcessPasswordReset(string email)
    {
        try
        {
            AuthResultData result = await authService.SendPasswordResetEmailAsync(email);

            if (result.Success || result.ErrorCode == "EMAIL_NOT_FOUND")
            {
                return new AuthResultData { Success = true, Email = email };
            }

            Debug.LogWarning($"Firebase password reset failed: {result.ErrorCode}");
            return result;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return AuthResultData.Failure(
                "PASSWORD_RESET_EXCEPTION",
                "Không thể gửi email đặt lại mật khẩu. Vui lòng thử lại.");
        }
    }

    #endregion

    #region Automatic Login

    private Task<bool> pendingAutoLogin;

    public Task<bool> TryAutoLogin()
    {
        return pendingAutoLogin ??= RestoreSessionAsync();
    }

    private async Task<bool> RestoreSessionAsync()
    {
        bool success = false;
        try
        {
            string method = PlayerPrefs.GetString(PREF_LOGIN_METHOD, "");
            if (method == "google" && GoogleAuthService.Instance != null)
                success = await GoogleAuthService.Instance.TryAutoLogin();
            else if (method == "email")
            {
                string token = PlayerPrefs.GetString(PREF_FIREBASE_REFRESH_TOKEN, "");
                if (!string.IsNullOrEmpty(token)) success = await ProcessAutoLoginEmail(token);
            }
            return success;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return false;
        }
        finally
        {
            // A failed UGS exchange must not leave a partially authenticated Firebase session.
            // Preserve saved credentials on transient failures for the next launch.
            if (!success)
            {
                authService?.Logout();
                if (UnityServices.State == ServicesInitializationState.Initialized &&
                    AuthenticationService.Instance.IsSignedIn)
                    AuthenticationService.Instance.SignOut();
                KLTN.Audio.AudioVolumeSettings.UseGuest();
            }
        }
    }

    private async Task<bool> ProcessAutoLoginEmail(string refreshToken)
    {
        AuthResultData result = await authService.LoginWithRefreshTokenAsync(refreshToken);
        if (!result.Success || string.IsNullOrEmpty(result.IdToken))
        {
            // Only discard credentials when the server explicitly rejects the saved session.
            if (result.ErrorCode == "TOKEN_EXPIRED" || result.ErrorCode == "INVALID_REFRESH_TOKEN" ||
                result.ErrorCode == "USER_DISABLED" || result.ErrorCode == "USER_NOT_FOUND")
            {
                PlayerPrefs.DeleteKey(PREF_LOGIN_METHOD);
                PlayerPrefs.DeleteKey(PREF_FIREBASE_REFRESH_TOKEN);
                PlayerPrefs.Save();
            }
            return false;
        }

        if (!string.IsNullOrEmpty(result.RefreshToken))
        {
            PlayerPrefs.SetString(PREF_FIREBASE_REFRESH_TOKEN, result.RefreshToken);
            PlayerPrefs.Save();
        }
        if (!await LoginFirebaseToUGS(result.IdToken)) return false;
        if (!await KLTN.Audio.AudioVolumeSettings.LoadAccountAsync(CurrentUserId)) return false;
        OnLoginSuccess?.Invoke();
        return true;
    }

    #endregion

    #region UGS Identity Bridge

    private async Task<bool> LoginFirebaseToUGS(string firebaseIdToken)
    {
        if (string.IsNullOrEmpty(firebaseIdToken))
        {
            Debug.LogError("Firebase ID Token is null or empty.");
            return false;
        }

        try
        {
            await UGSInitializer.Initialize();

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                Debug.LogError("UGS initialization failed.");
                return false;
            }

            await AuthenticationService.Instance.SignInWithOpenIdConnectAsync("oidc-firebase", firebaseIdToken);
            Debug.Log($"UGS login successful. Player ID: {AuthenticationService.Instance.PlayerId}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("UGS OIDC login failed: " + e.Message);
            return false;
        }
    }

    #endregion

    #region Logout

    public void SignOut()
    {
        pendingAutoLogin = null;
        PlayerPrefs.DeleteKey(PREF_LOGIN_METHOD);
        PlayerPrefs.DeleteKey(PREF_FIREBASE_REFRESH_TOKEN);
        PlayerPrefs.Save();

        // Sign out of Firebase for both email/password and Google flows.
        authService?.Logout();

        // Sign out of Unity Gaming Services.
        if (UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn) { AuthenticationService.Instance.SignOut(); }

        if (GoogleAuthService.Instance != null) { GoogleAuthService.Instance.ClearSession(); }

        Debug.Log("Đã đăng xuất toàn bộ hệ thống thành công.");
        KLTN.Audio.AudioVolumeSettings.UseGuest();
        OnLogoutSuccess?.Invoke();
    }

    #endregion
}
