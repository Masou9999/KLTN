using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns startup navigation while AppFlowManager defers authentication events.</summary>
public sealed class LoginStateCheckingController : MonoBehaviour
{
    [SerializeField] private RectTransform spinner;
    [SerializeField] private float rotationSpeed = 180f;

    private void Update()
    {
        if (spinner != null)
            spinner.Rotate(0f, 0f, -rotationSpeed * Time.unscaledDeltaTime);
    }

    private async void Start()
    {
        bool signedIn = false;
        try
        {
            // Let the loading UI render before starting authentication.
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            if (AuthManager.Instance != null)
                signedIn = await AuthManager.Instance.TryAutoLogin();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }

        if (this == null || !isActiveAndEnabled ||
            SceneManager.GetActiveScene().name != SceneNames.LoginStateChecking) return;

        string destination = signedIn ? SceneNames.MainMenu : SceneNames.Login;
        if (!Application.CanStreamedLevelBeLoaded(destination))
        {
            Debug.LogError($"Startup destination '{destination}' is missing from Build Settings.");
            return;
        }
        try
        {
            await SceneManager.LoadSceneAsync(destination, LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
