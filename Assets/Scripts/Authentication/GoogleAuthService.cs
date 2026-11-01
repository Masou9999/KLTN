using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class GoogleAuthService : MonoBehaviour
{
    #region PlayerPrefs Keys

    private const string PREF_ID_TOKEN = "google_id_token";
    private const string PREF_REFRESH_TOKEN = "google_refresh_token";
    private const string PREF_DISPLAY_NAME = "google_display_name";
    private const string PREF_EMAIL = "google_email";

    #endregion

    #region Constants

    private const int RequestTimeoutSeconds = 15;
    private const int BrowserLoginTimeoutSeconds = 180;
    private const string NetworkErrorMessage =
        "Không thể kết nối đến máy chủ Google. Vui lòng kiểm tra kết nối mạng.";

    #endregion

    #region Singleton

    public static GoogleAuthService Instance { get; private set; }

    #endregion

    #region Serialized Configuration

    [Header("Google OAuth Config")]
    [SerializeField] private string clientId = "CLIENT_ID.apps.googleusercontent.com";
    [SerializeField] private string clientSecret = "CLIENT_SECRET";
    [SerializeField] private string scopes = "openid email profile";

    #endregion

    #region Events and Properties

    public event Action<string, string> OnGoogleLoginSuccess;
    public event Action OnGoogleLoginFailed;
    public event Action OnGoogleLogout;
    public bool IsSignedIn { get; private set; } = false;
    public string DisplayName { get; private set; } = "";
    public string Email { get; private set; } = "";

    /// <summary>Reason for the last failed attempt, or empty when no specific reason is known.</summary>
    public string LastErrorMessage { get; private set; } = "";

    #endregion

    #region Runtime State

    private string codeVerifier;

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
        LoadAuthConfig();
    }

    #endregion

    #region Configuration

    private void LoadAuthConfig()
    {
        string configPath = Path.Combine(Application.streamingAssetsPath, "google-auth-config.json");
        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                GoogleAuthConfigData data = JsonUtility.FromJson<GoogleAuthConfigData>(json);
                if (data != null)
                {
                    if (!string.IsNullOrEmpty(data.clientId)) clientId = data.clientId;
                    if (!string.IsNullOrEmpty(data.clientSecret)) clientSecret = data.clientSecret;
                    if (!string.IsNullOrEmpty(data.scopes)) scopes = data.scopes;
                    Debug.Log("[GoogleAuth] Đã tải cấu hình OAuth từ StreamingAssets/google-auth-config.json");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GoogleAuth] Lỗi khi đọc file config: {ex.Message}");
            }
        }
    }

    #endregion

    #region Public Authentication Flow

    public async Task<bool> TryAutoLogin()
    {
        LastErrorMessage = "";
        string savedIdToken = PlayerPrefs.GetString(PREF_ID_TOKEN, "");
        if (!string.IsNullOrEmpty(savedIdToken))
        {
            Debug.Log("[GoogleAuth] Tìm thấy id_token cũ, thử auto-login...");
            bool success = await TrySignInWithIdToken(savedIdToken);
            if (success) return true;

            Debug.Log("[GoogleAuth] id_token hết hạn, thử refresh_token...");
            string savedRefreshToken = PlayerPrefs.GetString(PREF_REFRESH_TOKEN, "");
            if (!string.IsNullOrEmpty(savedRefreshToken))
            {
                success = await TryRefreshAndSignIn(savedRefreshToken);
                if (success) return true;
            }

            if (LastErrorMessage == NetworkErrorMessage)
            {
                // Mất mạng: giữ token đã lưu để lần sau thử lại thay vì bắt đăng nhập lại.
                Debug.LogWarning("[GoogleAuth] Auto-login thất bại do mất kết nối, giữ phiên đã lưu.");
            }
            else
            {
                Debug.Log("[GoogleAuth] Không thể auto-login, cần đăng nhập lại.");
                ClearSavedTokens();
            }
        }

        OnGoogleLoginFailed?.Invoke();
        return false;
    }

    public async void StartGoogleLogin()
    {
        LastErrorMessage = "";
        int port = GetRandomUnusedPort();
        string redirectUri = $"http://127.0.0.1:{port}/";
        codeVerifier = GenerateRandomString(64);
        string codeChallenge = GenerateCodeChallenge(codeVerifier);
        using HttpListener listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                         $"client_id={Uri.EscapeDataString(clientId)}&" +
                         $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                         $"response_type=code&" +
                         $"scope={Uri.EscapeDataString(scopes)}&" +
                         $"code_challenge={codeChallenge}&" +
                         $"code_challenge_method=S256&" +
                         $"access_type=offline&" +
                         $"prompt=consent";
        Application.OpenURL(authUrl);
        Debug.Log("[GoogleAuth] Đã mở trình duyệt để đăng nhập Google...");
        HttpListenerContext context;
        try
        {
            // Không chờ vô hạn khi người chơi đóng trình duyệt mà không đăng nhập.
            Task<HttpListenerContext> contextTask = listener.GetContextAsync();
            Task timeout = Task.Delay(TimeSpan.FromSeconds(BrowserLoginTimeoutSeconds));

            if (await Task.WhenAny(contextTask, timeout) != contextTask)
            {
                Debug.LogWarning("[GoogleAuth] Hết thời gian chờ đăng nhập Google trên trình duyệt.");
                ObserveFault(contextTask);
                listener.Stop();
                LastErrorMessage = "Hết thời gian chờ đăng nhập Google. Vui lòng thử lại.";
                OnGoogleLoginFailed?.Invoke();
                return;
            }

            context = await contextTask;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GoogleAuth] Lỗi HttpListener: {ex.Message}");
            OnGoogleLoginFailed?.Invoke();
            return;
        }

        string authCode = context.Request.QueryString.Get("code");
        string error = context.Request.QueryString.Get("error");
        string responseHtml;
        if (!string.IsNullOrEmpty(error))
        {
            responseHtml = "<html><body style='font-family:sans-serif;text-align:center;padding-top:50px;'>" +
                           "<h2 style='color:red;'>Đăng nhập thất bại!</h2>" +
                           $"<p>Lỗi: {error}</p>" +
                           "<p>Bạn có thể đóng cửa sổ này.</p></body></html>";
        }
        else
        {
            responseHtml = "<html><body style='font-family:sans-serif;text-align:center;padding-top:50px;'>" +
                           "<h2 style='color:green;'>&#10004; Đăng nhập thành công!</h2>" +
                           "<p>Bạn có thể đóng cửa sổ này và quay lại game.</p></body></html>";
        }

        byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        context.Response.OutputStream.Close();
        listener.Stop();

        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"[GoogleAuth] Google trả về lỗi: {error}");
            OnGoogleLoginFailed?.Invoke();
            return;
        }

        if (string.IsNullOrEmpty(authCode))
        {
            Debug.LogError("[GoogleAuth] Không nhận được auth code từ Google.");
            OnGoogleLoginFailed?.Invoke();
            return;
        }

        await ExchangeCodeForTokenAsync(authCode, redirectUri);
    }

    public void ClearSession()
    {
        ClearSavedTokens();
        IsSignedIn = false;
        DisplayName = "";
        Email = "";
        Debug.Log("[GoogleAuth] Đã xóa dữ liệu phiên đăng nhập Google cục bộ.");
        OnGoogleLogout?.Invoke();
    }

    #endregion

    #region Token Exchange & Sign In

    private async Task ExchangeCodeForTokenAsync(string code, string redirectUri)
    {
        WWWForm form = new WWWForm();
        form.AddField("client_id", clientId);
        form.AddField("client_secret", clientSecret);
        form.AddField("code", code);
        form.AddField("code_verifier", codeVerifier);
        form.AddField("grant_type", "authorization_code");
        form.AddField("redirect_uri", redirectUri);
        using UnityWebRequest request = UnityWebRequest.Post("https://oauth2.googleapis.com/token", form);
        request.timeout = RequestTimeoutSeconds;
        var asyncOp = request.SendWebRequest();
        while (!asyncOp.isDone) await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[GoogleAuth] Lỗi đổi token: {request.error} | {request.downloadHandler.text}");

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                LastErrorMessage = NetworkErrorMessage;
            }

            OnGoogleLoginFailed?.Invoke();
            return;
        }

        string jsonResult = request.downloadHandler.text;
        GoogleTokenResponse tokenData = JsonUtility.FromJson<GoogleTokenResponse>(jsonResult);
        Debug.Log("[GoogleAuth] Đổi token thành công!");
        ExtractUserInfoFromIdToken(tokenData.id_token);

        // Lưu tokens vào PlayerPrefs
        PlayerPrefs.SetString(PREF_ID_TOKEN, tokenData.id_token);
        if (!string.IsNullOrEmpty(tokenData.refresh_token)) { PlayerPrefs.SetString(PREF_REFRESH_TOKEN, tokenData.refresh_token); }
        PlayerPrefs.SetString(PREF_DISPLAY_NAME, DisplayName);
        PlayerPrefs.SetString(PREF_EMAIL, Email);
        PlayerPrefs.Save();
        bool success = await AuthManager.Instance.ProcessGoogleLogin(tokenData.id_token);

        if (success)
        {
            IsSignedIn = true;
            OnGoogleLoginSuccess?.Invoke(DisplayName, Email);
        }
        else
        {
            OnGoogleLoginFailed?.Invoke();
        }
    }

    private async Task<bool> TrySignInWithIdToken(string idToken)
    {
        bool success = await AuthManager.Instance.ProcessGoogleLogin(idToken);

        if (success)
        {
            IsSignedIn = true;
            DisplayName = PlayerPrefs.GetString(PREF_DISPLAY_NAME, "Player");
            Email = PlayerPrefs.GetString(PREF_EMAIL, "");
            Debug.Log($"[GoogleAuth] Auto-login thành công!");
            OnGoogleLoginSuccess?.Invoke(DisplayName, Email);
            return true;
        }
        else
        {
            Debug.LogWarning("[GoogleAuth] Auto-login bị từ chối hoặc thất bại từ AuthManager.");
            return false;
        }
    }

    private async Task<bool> TryRefreshAndSignIn(string refreshToken)
    {
        WWWForm form = new WWWForm();
        form.AddField("client_id", clientId);
        form.AddField("client_secret", clientSecret);
        form.AddField("refresh_token", refreshToken);
        form.AddField("grant_type", "refresh_token");
        using UnityWebRequest request = UnityWebRequest.Post("https://oauth2.googleapis.com/token", form);
        request.timeout = RequestTimeoutSeconds;
        var asyncOp = request.SendWebRequest();
        while (!asyncOp.isDone) await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[GoogleAuth] Refresh token thất bại: {request.error} | {request.downloadHandler.text}");

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                LastErrorMessage = NetworkErrorMessage;
            }

            return false;
        }

        string jsonResult = request.downloadHandler.text;
        GoogleTokenResponse tokenData = JsonUtility.FromJson<GoogleTokenResponse>(jsonResult);

        if (string.IsNullOrEmpty(tokenData.id_token))
        {
            Debug.LogWarning("[GoogleAuth] Refresh không trả về id_token mới.");
            return false;
        }

        ExtractUserInfoFromIdToken(tokenData.id_token);
        PlayerPrefs.SetString(PREF_ID_TOKEN, tokenData.id_token);
        PlayerPrefs.SetString(PREF_DISPLAY_NAME, DisplayName);
        PlayerPrefs.SetString(PREF_EMAIL, Email);
        PlayerPrefs.Save();
        Debug.Log("[GoogleAuth] Refresh token thành công, đang đăng nhập lại...");
        return await TrySignInWithIdToken(tokenData.id_token);
    }

    #endregion

    #region JWT Claims

    private void ExtractUserInfoFromIdToken(string idToken)
    {
        try
        {
            string[] parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                Debug.LogWarning("[GoogleAuth] id_token không hợp lệ (không đủ 3 phần JWT).");
                return;
            }

            string payload = parts[1];
            string base64 = payload.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            byte[] bytes = Convert.FromBase64String(base64);
            string json = Encoding.UTF8.GetString(bytes);
            GoogleIdTokenPayload data = JsonUtility.FromJson<GoogleIdTokenPayload>(json);
            DisplayName = !string.IsNullOrEmpty(data.name) ? data.name : data.email;
            Email = data.email ?? "";
            Debug.Log($"[GoogleAuth] Thông tin người dùng: Name={DisplayName}, Email={Email}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GoogleAuth] Không thể giải mã id_token: {ex.Message}");
            DisplayName = "Player";
            Email = "";
        }
    }

    // JsonUtility populates these DTO fields through reflection.
#pragma warning disable CS0649

    [Serializable]
    private class GoogleIdTokenPayload
    {
        public string email;
        public string name;
        public string picture;
        public string sub;
    }

    #endregion

    #region Helpers

    private void ClearSavedTokens()
    {
        PlayerPrefs.DeleteKey(PREF_ID_TOKEN);
        PlayerPrefs.DeleteKey(PREF_REFRESH_TOKEN);
        PlayerPrefs.DeleteKey(PREF_DISPLAY_NAME);
        PlayerPrefs.DeleteKey(PREF_EMAIL);
        PlayerPrefs.Save();
    }

    private string GenerateRandomString(int length)
    {
        byte[] bytes = new byte[length];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "").Substring(0, length);
    }

    private string GenerateCodeChallenge(string verifier)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(verifier));
        return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private int GetRandomUnusedPort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void ObserveFault(Task task)
    {
        // Stopping the listener faults the pending GetContextAsync; mark it observed.
        task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    [Serializable]
    private class GoogleTokenResponse
    {
        public string access_token;
        public string id_token;
        public int expires_in;
        public string token_type;
        public string refresh_token;
    }

    [Serializable]
    private class GoogleAuthConfigData
    {
        public string clientId;
        public string clientSecret;
        public string scopes;
    }

#pragma warning restore CS0649

    #endregion
}
