using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using KLTN.Infrastructure.Resilience;
using UnityEngine;
using UnityEngine.Networking;

public class FirebaseRestAuthService : IAuthService
{
    #region Endpoints and Configuration

    private const string ConfigFileName = "firebase-auth-config.json";
    private const string SIGN_UP_URL = "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=";
    private const string SIGN_IN_URL = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=";
    private const string SIGN_IN_IDP_URL = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=";
    private const string SECURE_TOKEN_URL = "https://securetoken.googleapis.com/v1/token?key=";
    private const string SEND_OOB_CODE_URL = "https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key=";
    private const string PasswordResetRequestType = "PASSWORD_RESET";
    private const string EmailLocale = "vi";
    private const int RequestTimeoutSeconds = 15;

    private static readonly NetworkRetryPolicy RetryPolicy = NetworkRetryPolicy.Default;
    private static readonly System.Random RetryJitter = new();

    private readonly string apiKey;

    #endregion

    #region Authentication State

    private string userId;
    private string email;
    private string idToken;
    private string refreshToken;
    private DateTime idTokenExpiresAtUtc = DateTime.MinValue;

    #endregion

    #region Construction

    public FirebaseRestAuthService()
    {
        apiKey = LoadApiKey();
    }

    #endregion

    #region Properties

    public bool IsLoggedIn => !string.IsNullOrEmpty(idToken);
    public string UserId => userId;
    public string Email => email;
    public string IdToken => idToken;
    public string RefreshToken => refreshToken;
    public DateTime IdTokenExpiresAtUtc => idTokenExpiresAtUtc;

    #endregion

    #region IAuthService Commands

    public async Task<AuthResultData> RegisterAsync(string email, string password)
    {
        if (!IsConfigured) { return MissingConfigurationResult(); }

        FirebaseAuthRequest request = new FirebaseAuthRequest
        {
            email = email,
            password = password,
            returnSecureToken = true
        };
        string json = JsonUtility.ToJson(request);
        UnityWebRequest webRequest = null;

        try
        {
            // Not idempotent: a repeated sign-up after a lost response would report EMAIL_EXISTS.
            webRequest = await SendWithRetryAsync(() => CreatePostRequest(SIGN_UP_URL + apiKey, json), idempotent: false);

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                FirebaseAuthResponse response = JsonUtility.FromJson<FirebaseAuthResponse>(webRequest.downloadHandler.text);
                SaveUserData(response);

                return new AuthResultData
                {
                    Success = true,
                    UserId = userId,
                    Email = this.email,
                    IdToken = idToken,
                    RefreshToken = refreshToken
                };
            }

            return CreateErrorResult(webRequest);
        }
        catch (Exception e)
        {
            return new AuthResultData
            {
                Success = false,
                ErrorCode = "EXCEPTION",
                ErrorMessage = e.Message
            };
        }
        finally
        {
            webRequest?.Dispose();
        }
    }

    public async Task<AuthResultData> LoginAsync(string email, string password)
    {
        if (!IsConfigured) { return MissingConfigurationResult(); }

        FirebaseAuthRequest request = new FirebaseAuthRequest
        {
            email = email,
            password = password,
            returnSecureToken = true
        };
        string json = JsonUtility.ToJson(request);
        UnityWebRequest webRequest = null;

        try
        {
            webRequest = await SendWithRetryAsync(() => CreatePostRequest(SIGN_IN_URL + apiKey, json), idempotent: true);

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                FirebaseAuthResponse response = JsonUtility.FromJson<FirebaseAuthResponse>(webRequest.downloadHandler.text);
                SaveUserData(response);

                return new AuthResultData
                {
                    Success = true,
                    UserId = userId,
                    Email = this.email,
                    IdToken = idToken,
                    RefreshToken = refreshToken
                };
            }

            return CreateErrorResult(webRequest);
        }
        catch (Exception e)
        {
            return new AuthResultData
            {
                Success = false,
                ErrorCode = "EXCEPTION",
                ErrorMessage = e.Message
            };
        }
        finally
        {
            webRequest?.Dispose();
        }
    }

    public async Task<AuthResultData> LoginWithGoogleAsync(string googleIdToken)
    {
        if (!IsConfigured) { return MissingConfigurationResult(); }

        // Payload theo chuẩn REST API của Firebase cho Identity Providers
        FirebaseIdpRequest request = new FirebaseIdpRequest
        {
            postBody = $"id_token={googleIdToken}&providerId=google.com",
            requestUri = "http://localhost",
            returnSecureToken = true
        };
        string json = JsonUtility.ToJson(request);
        UnityWebRequest webRequest = null;

        try
        {
            webRequest = await SendWithRetryAsync(() => CreatePostRequest(SIGN_IN_IDP_URL + apiKey, json), idempotent: true);

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                // Response của signInWithIdp tương tự signInWithPassword nên dùng chung struct được
                FirebaseAuthResponse response = JsonUtility.FromJson<FirebaseAuthResponse>(webRequest.downloadHandler.text);
                SaveUserData(response);

                return new AuthResultData
                {
                    Success = true,
                    UserId = userId,
                    Email = this.email,
                    IdToken = idToken,
                    RefreshToken = refreshToken
                };
            }

            return CreateErrorResult(webRequest);
        }
        catch (Exception e)
        {
            return new AuthResultData
            {
                Success = false,
                ErrorCode = "EXCEPTION",
                ErrorMessage = e.Message
            };
        }
        finally
        {
            webRequest?.Dispose();
        }
    }

    public async Task<AuthResultData> LoginWithRefreshTokenAsync(string savedRefreshToken)
    {
        if (!IsConfigured) { return MissingConfigurationResult(); }

        UnityWebRequest request = null;

        try
        {
            request = await SendWithRetryAsync(() => CreateRefreshRequest(savedRefreshToken), idempotent: true);

            if (request.result == UnityWebRequest.Result.Success)
            {
                // Parse kết quả trả về
                FirebaseRefreshTokenResponse response = JsonUtility.FromJson<FirebaseRefreshTokenResponse>(request.downloadHandler.text);

                // Cập nhật biến bộ nhớ
                this.idToken = response.id_token;
                this.refreshToken = response.refresh_token;
                this.userId = response.user_id;
                this.idTokenExpiresAtUtc = ComputeExpiry(response.expires_in);

                return new AuthResultData
                {
                    Success = true,
                    UserId = this.userId,
                    IdToken = this.idToken,
                    RefreshToken = this.refreshToken
                };
            }

            return CreateErrorResult(request);
        }
        catch (Exception e)
        {
            return new AuthResultData { Success = false, ErrorMessage = e.Message };
        }
        finally
        {
            request?.Dispose();
        }
    }

    public async Task<AuthResultData> SendPasswordResetEmailAsync(string email)
    {
        if (!IsConfigured) { return MissingConfigurationResult(); }

        FirebaseOobCodeRequest request = new FirebaseOobCodeRequest
        {
            requestType = PasswordResetRequestType,
            email = email
        };
        string json = JsonUtility.ToJson(request);
        UnityWebRequest webRequest = null;

        try
        {
            // Not idempotent: every accepted request sends another email.
            webRequest = await SendWithRetryAsync(
                () =>
                {
                    UnityWebRequest post = CreatePostRequest(SEND_OOB_CODE_URL + apiKey, json);
                    post.SetRequestHeader("X-Firebase-Locale", EmailLocale);
                    return post;
                },
                idempotent: false);

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                return new AuthResultData { Success = true, Email = email };
            }

            return CreateErrorResult(webRequest);
        }
        catch (Exception e)
        {
            return new AuthResultData
            {
                Success = false,
                ErrorCode = "EXCEPTION",
                ErrorMessage = e.Message
            };
        }
        finally
        {
            webRequest?.Dispose();
        }
    }

    public void Logout()
    {
        userId = null;
        email = null;
        idToken = null;
        refreshToken = null;
        idTokenExpiresAtUtc = DateTime.MinValue;
        Debug.Log("Firebase logout successful.");
    }

    #endregion

    #region Configuration Loading

    private bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);

    private static string LoadApiKey()
    {
        string configPath = Path.Combine(Application.streamingAssetsPath, ConfigFileName);

        if (!File.Exists(configPath))
        {
            Debug.LogError($"[FirebaseAuth] Không tìm thấy StreamingAssets/{ConfigFileName}.");
            return string.Empty;
        }

        try
        {
            string json = File.ReadAllText(configPath);
            FirebaseAuthConfigData data = JsonUtility.FromJson<FirebaseAuthConfigData>(json);

            if (data == null || string.IsNullOrWhiteSpace(data.apiKey))
            {
                Debug.LogError($"[FirebaseAuth] {ConfigFileName} không chứa apiKey hợp lệ.");
                return string.Empty;
            }

            Debug.Log($"[FirebaseAuth] Đã tải cấu hình từ StreamingAssets/{ConfigFileName}.");
            return data.apiKey.Trim();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FirebaseAuth] Không thể đọc cấu hình: {exception.Message}");
            return string.Empty;
        }
    }

    private static AuthResultData MissingConfigurationResult()
    {
        return AuthResultData.Failure(
            "FIREBASE_CONFIG_MISSING",
            "Thiếu cấu hình Firebase. Vui lòng kiểm tra file StreamingAssets/firebase-auth-config.json.");
    }

    #endregion

    #region State Mapping

    private void SaveUserData(FirebaseAuthResponse response)
    {
        userId = response.localId;
        email = response.email;
        idToken = response.idToken;
        refreshToken = response.refreshToken;
        idTokenExpiresAtUtc = ComputeExpiry(response.expiresIn);
    }

    private static DateTime ComputeExpiry(string expiresInSeconds)
    {
        // Firebase ID tokens live 3600 s. Fall back to that when the field is missing.
        int seconds = int.TryParse(expiresInSeconds, out int parsed) && parsed > 0 ? parsed : 3600;
        return DateTime.UtcNow.AddSeconds(seconds);
    }

    #endregion

    #region HTTP Helpers

    private UnityWebRequest CreatePostRequest(string url, string json)
    {
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = RequestTimeoutSeconds;
        return request;
    }

    private UnityWebRequest CreateRefreshRequest(string savedRefreshToken)
    {
        WWWForm form = new WWWForm();
        form.AddField("grant_type", "refresh_token");
        form.AddField("refresh_token", savedRefreshToken);
        UnityWebRequest request = UnityWebRequest.Post(SECURE_TOKEN_URL + apiKey, form);
        request.timeout = RequestTimeoutSeconds;
        return request;
    }

    private async Task SendRequest(UnityWebRequest request)
    {
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            await Task.Yield();
        }
    }

    /// <summary>
    /// Sends a fresh request per attempt with bounded backoff (FR-NET-08) and returns the
    /// last one, which the caller disposes. Only idempotent calls retry on timeouts.
    /// </summary>
    private async Task<UnityWebRequest> SendWithRetryAsync(Func<UnityWebRequest> createRequest, bool idempotent)
    {
        for (int attempt = 1; ; attempt++)
        {
            UnityWebRequest request = createRequest();

            try
            {
                await SendRequest(request);
            }
            catch
            {
                request.Dispose();
                throw;
            }

            bool connectionError = request.result == UnityWebRequest.Result.ConnectionError;

            if (request.result == UnityWebRequest.Result.Success
                || !RetryPolicy.ShouldRetry(attempt, idempotent, connectionError, request.responseCode, null))
            {
                return request;
            }

            TimeSpan delay = RetryPolicy.GetDelay(attempt - 1, RetryJitter.NextDouble());
            Debug.LogWarning($"[FirebaseAuth] Request failed ({request.responseCode} {request.error}), retry {attempt}/{RetryPolicy.MaxAttempts - 1} in {delay.TotalSeconds:0.0} s.");
            request.Dispose();
            await Task.Delay(delay);
        }
    }

    #endregion

    #region Error Mapping

    private AuthResultData CreateErrorResult(UnityWebRequest request)
    {
        if (request.result == UnityWebRequest.Result.ConnectionError)
        {
            return AuthResultData.Failure(
                "NETWORK_ERROR",
                "Không thể kết nối đến máy chủ. Vui lòng kiểm tra kết nối mạng.");
        }

        string errorCode = "UNKNOWN_ERROR";
        string errorMessage = request.responseCode >= 500 || request.responseCode == 429
            ? "Máy chủ xác thực đang bận. Vui lòng thử lại sau."
            : "Yêu cầu xác thực thất bại. Vui lòng thử lại.";

        if (!string.IsNullOrEmpty(request.downloadHandler.text))
        {
            try
            {
                FirebaseErrorResponse errorResponse = JsonUtility.FromJson<FirebaseErrorResponse>(request.downloadHandler.text);

                if (errorResponse != null &&
                    errorResponse.error != null)
                {
                    errorCode = errorResponse.error.message;
                    errorMessage = ConvertFirebaseErrorToMessage(errorCode);
                }
            }
            catch
            {
                errorMessage = request.error;
            }
        }

        return new AuthResultData
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }

    private static string ConvertFirebaseErrorToMessage(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode)) { return "Yêu cầu xác thực thất bại."; }

        if (errorCode.StartsWith("WEAK_PASSWORD")) { return "Mật khẩu phải có ít nhất 6 ký tự."; }

        if (errorCode.StartsWith("EMAIL_EXISTS")) { return "Email này đã được đăng ký."; }

        switch (errorCode)
        {
            case "EMAIL_NOT_FOUND":
                return "Email không tồn tại.";

            case "INVALID_PASSWORD":
            case "INVALID_LOGIN_CREDENTIALS":
                return "Email hoặc mật khẩu không chính xác.";

            case "INVALID_EMAIL":
                return "Định dạng email không hợp lệ.";

            case "MISSING_EMAIL":
                return "Vui lòng nhập email.";

            case "MISSING_PASSWORD":
                return "Vui lòng nhập mật khẩu.";

            case "USER_DISABLED":
                return "Tài khoản đã bị vô hiệu hóa.";

            case "OPERATION_NOT_ALLOWED":
                return "Phương thức xác thực này chưa được bật.";

            case "TOO_MANY_ATTEMPTS_TRY_LATER":
                return "Có quá nhiều lần thử. Vui lòng thử lại sau.";

            case "RESET_PASSWORD_EXCEED_LIMIT":
                return "Bạn đã yêu cầu đặt lại mật khẩu quá nhiều lần. Vui lòng thử lại sau.";

            case "TOKEN_EXPIRED":
            case "INVALID_REFRESH_TOKEN":
            case "USER_NOT_FOUND":
                return "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";

            default:
                Debug.LogWarning($"Unhandled Firebase authentication error: {errorCode}");
                return "Yêu cầu xác thực thất bại. Vui lòng thử lại.";
        }
    }

    #endregion

    #region Request and Response DTOs

    // JsonUtility populates these DTO fields through reflection.
#pragma warning disable CS0649

    [Serializable]
    private class FirebaseAuthRequest
    {
        public string email;
        public string password;
        public bool returnSecureToken;
    }

    [Serializable]
    private class FirebaseAuthConfigData
    {
        public string apiKey;
    }

    [Serializable]
    private class FirebaseOobCodeRequest
    {
        public string requestType;
        public string email;
    }

    [Serializable]
    private class FirebaseIdpRequest
    {
        public string postBody;
        public string requestUri;
        public bool returnSecureToken;
    }

    [Serializable]
    private class FirebaseRefreshTokenResponse
    {
        public string access_token;
        public string expires_in;
        public string token_type;
        public string refresh_token;
        public string id_token;
        public string user_id;
        public string project_id;
    }

    [Serializable]
    private class FirebaseAuthResponse
    {
        public string idToken;
        public string email;
        public string refreshToken;
        public string expiresIn;
        public string localId;
    }

    [Serializable]
    private class FirebaseErrorResponse
    {
        public FirebaseError error;
    }

    [Serializable]
    private class FirebaseError
    {
        public int code;
        public string message;
    }

#pragma warning restore CS0649

    #endregion
}
