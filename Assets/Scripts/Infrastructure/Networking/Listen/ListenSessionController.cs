using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ListenSessionController : MonoBehaviour
{
    #region UI

    [Header("UI")]
    [SerializeField]
    private TMP_InputField joinCodeInput;

    [SerializeField]
    private TMP_InputField joinCodeLabel;

    [SerializeField]
    private TMP_Text statusLabel;

    #endregion

    #region Runtime State

    private bool loadingGameScene;
    private bool leaving;

    #endregion

    #region Unity Lifecycle

    private void OnEnable()
    {
        UgsSessionService.Instance.PlayerCountChanged += OnPlayerCountChanged;
        UgsSessionService.Instance.HostSessionEnded += OnHostSessionEnded;
    }

    private void OnDisable()
    {
        UgsSessionService.Instance.PlayerCountChanged -= OnPlayerCountChanged;
        UgsSessionService.Instance.HostSessionEnded -= OnHostSessionEnded;
    }

    #endregion

    #region UI Events

    public async void Host()
    {
        try
        {
            SetStatus("Đang tạo phòng...");
            string joinCode = await UgsSessionService.Instance.HostAsync();
            MatchConnectionController.RememberListenHost();

            if (joinCodeLabel != null)
            {
                joinCodeLabel.text = joinCode;
            }

            SetStatus("Đã tạo phòng. Đang chờ người chơi thứ hai.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus($"Không thể tạo phòng: {exception.Message}");
        }
    }

    public async void Join()
    {
        try
        {
            string joinCode = joinCodeInput?.text;
            SetStatus("Đang tham gia phòng...");
            await UgsSessionService.Instance.JoinByCodeAsync(joinCode);

            // The guest can rejoin the same room with this code after a lost connection.
            MatchConnectionController.RememberListenGuest(joinCode);
            SetStatus("Đã tham gia. Chờ host bắt đầu.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus($"Không thể tham gia: {exception.Message}");
        }
    }

    public async void Leave()
    {
        await LeaveAndReturnAsync();
    }

    #endregion

    #region Session Events

    private async void OnPlayerCountChanged(int playerCount)
    {
        SetStatus($"Người chơi: {playerCount}/2");

        if (playerCount != 2 || !UgsSessionService.Instance.IsHost || loadingGameScene)
        {
            return;
        }

        loadingGameScene = true;
        NetworkManager networkManager = NetworkManager.Singleton;

        // Wait briefly for the Relay-backed host to start.
        for (int frame = 0; frame < 120 && !networkManager.IsHost; frame++)
        {
            await Task.Yield();
        }

        if (!networkManager.IsHost)
        {
            loadingGameScene = false;
            SetStatus("Host network chưa sẵn sàng.");
            return;
        }

        SceneEventProgressStatus result = networkManager.SceneManager.LoadScene(
            SceneNames.GameScene,
            LoadSceneMode.Single
        );

        if (result != SceneEventProgressStatus.Started)
        {
            loadingGameScene = false;
            SetStatus($"Không thể mở GameScene: {result}");
        }
    }

    private void OnHostSessionEnded()
    {
        if (leaving)
        {
            return;
        }

        SetStatus("Host đã đóng phòng.");
        _ = ReturnToMainMenuAsync();
    }

    #endregion

    #region Navigation

    private async Task LeaveAndReturnAsync()
    {
        if (leaving)
        {
            return;
        }

        leaving = true;
        MatchConnectionController.Forget();

        try
        {
            await UgsSessionService.Instance.LeaveAsync();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(exception.Message);
        }

        await ReturnToMainMenuAsync();
    }

    private static Task ReturnToMainMenuAsync()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        SceneManager.LoadScene(SceneNames.MainMenu);
        return Task.CompletedTask;
    }

    #endregion

    #region UI Rendering

    private void SetStatus(string message)
    {
        if (statusLabel != null)
        {
            statusLabel.text = message;
        }
    }

    #endregion
}
