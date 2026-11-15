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
    private bool connecting;

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
        if (connecting || leaving || loadingGameScene || MatchConnectionController.IsLeaving) return;
        connecting = true;
        try
        {
            SetStatus("Đang tạo phòng...");
            string joinCode = await UgsSessionService.Instance.HostAsync();
            if (leaving || MatchConnectionController.IsLeaving) return;
            MatchConnectionController.RememberListenHost();
            if (this == null) return;

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
        finally { connecting = false; }
    }

    public async void Join()
    {
        if (connecting || leaving || loadingGameScene || MatchConnectionController.IsLeaving) return;
        connecting = true;
        try
        {
            string joinCode = joinCodeInput?.text;
            SetStatus("Đang tham gia phòng...");
            await UgsSessionService.Instance.JoinByCodeAsync(joinCode);

            if (leaving || MatchConnectionController.IsLeaving) return;
            // The guest can rejoin the same room with this code after a lost connection.
            MatchConnectionController.RememberListenGuest(joinCode);
            if (this == null) return;
            SetStatus("Đã tham gia. Chờ host bắt đầu.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetStatus($"Không thể tham gia: {exception.Message}");
        }
        finally { connecting = false; }
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

        if (leaving || MatchConnectionController.IsLeaving || playerCount != 2 || !UgsSessionService.Instance.IsHost || loadingGameScene)
        {
            return;
        }

        loadingGameScene = true;
        NetworkManager networkManager = NetworkManager.Singleton;

        // Wait briefly for the Relay-backed host to start.
        for (int frame = 0; frame < 120 && networkManager != null && !networkManager.IsHost && !leaving && !MatchConnectionController.IsLeaving; frame++)
        {
            await Task.Yield();
        }

        if (this == null || !isActiveAndEnabled || leaving || MatchConnectionController.IsLeaving) return;

        if (networkManager == null || !networkManager.IsHost || UgsSessionService.Instance.PlayerCount != 2)
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
        _ = LeaveAndReturnAsync();
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
        NetworkManager networkManager = NetworkManager.Singleton;
        bool needsCleanup = connecting
            || loadingGameScene
            || UgsSessionService.Instance.HasSession
            || UgsSessionService.Instance.IsBusy
            || MatchConnectionController.IsLeaving
            || (networkManager != null && (networkManager.IsListening || networkManager.ShutdownInProgress));

        // Back from an idle menu is navigation, not leaving a room.
        if (!needsCleanup)
        {
            MatchConnectionController.Forget();
            SceneManager.LoadScene(SceneNames.MainMenu);
            return;
        }

        SetStatus("Đang rời phòng...");
        bool completed = await MatchConnectionController.ReturnToMainMenuAsync();
        if (this != null && !completed)
        {
            leaving = false;
            SetStatus("Chưa thể rời phòng. Vui lòng thử lại.");
        }
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
