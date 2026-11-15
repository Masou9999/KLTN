using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private GameObject confirmationRoot;
    [SerializeField] private CanvasGroup menuGroup;
    [SerializeField] private TMP_Text confirmationMessage;
    [SerializeField] private TMP_Text confirmationAcceptLabel;
    [SerializeField] private Button cancelButton;

    private enum PendingAction { None, Logout, Quit }
    private PendingAction pendingAction;
    private GameObject previousSelection;

    #region UI Events

    public void OpenFindMatch()
    {
        if (!EnsureSignedIn())
        {
            return;
        }

        SceneManager.LoadScene(SceneNames.FindMatch);
    }

    public void OpenHostClientMenu()
    {
        if (!EnsureSignedIn())
        {
            return;
        }

        SceneManager.LoadScene(SceneNames.HostClientMenu);
    }

    public void OpenShop()
    {
        if (!EnsureSignedIn())
        {
            return;
        }

        SceneManager.LoadScene(SceneNames.Shop);
    }

    public void OpenLeaderboard()
    {
        if (!EnsureSignedIn())
        {
            return;
        }

        SceneManager.LoadScene(SceneNames.Leaderboard);
    }

    public void Logout()
    {
        ShowConfirmation(PendingAction.Logout,
            "Bạn có chắc muốn đăng xuất tài khoản và quay về màn hình đăng nhập?", "Đăng xuất");
    }

    public void Quit()
    {
        ShowConfirmation(PendingAction.Quit, "Bạn có chắc muốn thoát trò chơi?", "Thoát");
    }

    private void ShowConfirmation(PendingAction action, string message, string acceptLabel)
    {
        if (pendingAction != PendingAction.None) return;
        previousSelection = EventSystem.current?.currentSelectedGameObject;
        pendingAction = action;
        confirmationMessage.text = message;
        confirmationAcceptLabel.text = acceptLabel;
        menuGroup.interactable = false;
        confirmationRoot.transform.SetAsLastSibling();
        confirmationRoot.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(cancelButton.gameObject);
    }

    public void CancelConfirmation()
    {
        pendingAction = PendingAction.None;
        confirmationRoot.SetActive(false);
        menuGroup.interactable = true;
        EventSystem.current?.SetSelectedGameObject(previousSelection);
    }

    public void ConfirmAction()
    {
        PendingAction action = pendingAction;
        CancelConfirmation();
        if (action == PendingAction.Logout)
        {
            AuthManager.Instance?.SignOut();
            // AppFlowManager normally handles navigation via OnLogoutSuccess.
            // Also support opening MainMenu directly in the Editor.
            if (AuthManager.Instance == null || AppFlowManager.Instance == null)
                SceneManager.LoadScene(SceneNames.Login);
        }
        else if (action == PendingAction.Quit)
        {
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    #endregion

    #region Validation

    private static bool EnsureSignedIn()
    {
        if (AuthenticationService.Instance.IsSignedIn)
        {
            return true;
        }

        Debug.LogError("UGS authentication is required before entering multiplayer.");
        SceneManager.LoadScene(SceneNames.Login);
        return false;
    }

    #endregion
}
