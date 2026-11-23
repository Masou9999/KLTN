using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Loads the Firebase Web API key from <c>StreamingAssets/firebase-auth-config.json</c>.
/// Desktop reads the file directly; WebGL must download it because StreamingAssets is a URL
/// in the browser and <see cref="File"/> cannot reach it.
/// </summary>
public static class FirebaseAuthConfig
{
    #region Constants

    public const string ConfigFileName = "firebase-auth-config.json";
    private const int RequestTimeoutSeconds = 15;

    #endregion

    #region Runtime State

    private static Task<string> pendingLoad;

    #endregion

    #region Public API

    /// <summary>
    /// Returns the API key, or an empty string when the file is missing or invalid.
    /// The first successful download is cached; a failed one is retried on the next call.
    /// </summary>
    public static async Task<string> LoadApiKeyAsync()
    {
        pendingLoad ??= LoadCoreAsync();
        string apiKey = await pendingLoad;

        if (string.IsNullOrEmpty(apiKey))
        {
            pendingLoad = null;
        }

        return apiKey;
    }

    /// <summary>
    /// Reads the API key synchronously. Always empty on WebGL, where only
    /// <see cref="LoadApiKeyAsync"/> can reach StreamingAssets.
    /// </summary>
    public static string LoadApiKey()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return string.Empty;
#else
        string configPath = Path.Combine(Application.streamingAssetsPath, ConfigFileName);

        if (!File.Exists(configPath))
        {
            Debug.LogError($"[FirebaseAuth] Không tìm thấy StreamingAssets/{ConfigFileName}.");
            return string.Empty;
        }

        try
        {
            return Parse(File.ReadAllText(configPath));
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FirebaseAuth] Không thể đọc cấu hình: {exception.Message}");
            return string.Empty;
        }
#endif
    }

    #endregion

    #region Helpers

    private static async Task<string> LoadCoreAsync()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string url = Application.streamingAssetsPath + "/" + ConfigFileName;
        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = RequestTimeoutSeconds;
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            // responseCode 0 means the browser got no HTTP response (blocked/aborted request).
            Debug.LogError(
                $"[FirebaseAuth] Không tải được {url}: HTTP {request.responseCode}, "
                    + $"{request.result}, {request.error}");
            return string.Empty;
        }

        return Parse(request.downloadHandler.text);
#else
        await Task.CompletedTask;
        return LoadApiKey();
#endif
    }

    private static string Parse(string json)
    {
        FirebaseAuthConfigData data = JsonUtility.FromJson<FirebaseAuthConfigData>(json);

        if (data == null || string.IsNullOrWhiteSpace(data.apiKey))
        {
            Debug.LogError($"[FirebaseAuth] {ConfigFileName} không chứa apiKey hợp lệ.");
            return string.Empty;
        }

        Debug.Log($"[FirebaseAuth] Đã tải cấu hình từ StreamingAssets/{ConfigFileName}.");
        return data.apiKey.Trim();
    }

    // JsonUtility populates this DTO through reflection.
#pragma warning disable CS0649
    [Serializable]
    private class FirebaseAuthConfigData
    {
        public string apiKey;
    }
#pragma warning restore CS0649

    #endregion
}
