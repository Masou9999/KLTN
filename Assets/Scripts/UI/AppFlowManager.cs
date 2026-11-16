using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class AppFlowManager : MonoBehaviour
{
    #region Singleton

    public static AppFlowManager Instance { get; private set; }

    #endregion

    #region Static Lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
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

        // Defensive removal prevents duplicate subscriptions.
        AuthManager.OnLoginSuccess -= LoadMainMenu;
        AuthManager.OnLogoutSuccess -= LoadLogin;
        AuthManager.OnLoginSuccess += LoadMainMenu;
        AuthManager.OnLogoutSuccess += LoadLogin;
        Debug.Log("AppFlowManager subscribed to authentication events.");
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        AuthManager.OnLoginSuccess -= LoadMainMenu;
        AuthManager.OnLogoutSuccess -= LoadLogin;
        Instance = null;
    }

    #endregion

    #region Scene Navigation

    private void LoadMainMenu()
    {
        if (SceneManager.GetActiveScene().name != SceneNames.LoginStateChecking)
            LoadScene(SceneNames.MainMenu);
    }

    private void LoadLogin()
    {
        if (SceneManager.GetActiveScene().name != SceneNames.LoginStateChecking)
            LoadScene(SceneNames.Login);
    }

    private static void LoadScene(string sceneName)
    {
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"Scene '{sceneName}' is unavailable. "
                    + "Check Build Profiles scene list."
            );
            return;
        }

        Debug.Log($"Loading scene: {sceneName}");
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    #endregion
}
