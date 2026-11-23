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

    [SerializeField]
    private KLTN.UI.HostClientMenuView menuView;

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
        if (connecting || leaving || loadingGameScene
            || MatchConnectionController.IsLeaving) return;
        connecting = true;
        menuView?.SetBusy(true);
        menuView?.HideRoomCode();
        try
        {
            SetStatus("Đang tạo phòng...");
            string joinCode = await UgsSessionService.Instance.HostAsync();
            if (leaving || MatchConnectionController.IsLeaving) return;
            MatchConnectionController.RememberListenHost();
            if (this == null) return;

            if (joinCodeLabel != null)
            {
                joinCodeLabel.readOnly = true;
                joinCodeLabel.SetTextWithoutNotify(joinCode);
            }
            menuView?.ShowRoomCode(joinCode);
            SetStatus("Đã tạo phòng. Đang chờ người chơi thứ hai.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (this != null && !leaving)
                SetStatus("Không thể tạo phòng. Vui lòng thử lại.");
        }
        finally
        {
            connecting = false;
            if (this != null) menuView?.SetBusy(false);
        }
    }

    public async void Join()
    {
        if (connecting || leaving || loadingGameScene
            || MatchConnectionController.IsLeaving) return;
        string joinCode = joinCodeInput?.text?.Trim();
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            SetStatus("Vui lòng nhập mã phòng.");
            return;
        }
        connecting = true;
        bool joined = false;
        menuView?.SetBusy(true);
        menuView?.HideRoomCode();
        try
        {
            SetStatus("Đang tham gia phòng...");
            await UgsSessionService.Instance.JoinByCodeAsync(joinCode);
            if (leaving || MatchConnectionController.IsLeaving) return;
            // Preserve the code for the existing reconnect flow.
            MatchConnectionController.RememberListenGuest(joinCode);
            joined = true;
            if (this == null) return;
            SetStatus("Đã tham gia. Chờ host bắt đầu.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (this != null && !leaving)
                SetStatus("Không thể tham gia. Kiểm tra mã phòng và kết nối rồi thử lại.");
        }
        finally
        {
            connecting = false;
            // A joined guest must not start another operation while scene sync is pending.
            if (this != null) menuView?.SetBusy(joined);
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

        if (leaving || MatchConnectionController.IsLeaving || playerCount != 2 || !UgsSessionService.Instance.IsHost || loadingGameScene)
        {
            return;
        }

        loadingGameScene = true;
        NetworkManager networkManager = NetworkManager.Singleton;

        // Wait briefly for the Relay-backed host to start.
        for (int frame = 0; frame < 120 && networkManager != null && !networkManager.IsHost && !leaving && !MatchConnectionController.IsLeaving; frame++)
        {
            await FrameDelay.NextFrame();
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
        menuView?.SetLeaving(true);
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
            menuView?.SetLeaving(false);
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
