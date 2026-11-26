using KLTN.Game.Networking;
using KLTN.UI;
using UnityEngine;

public class SurrenderUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject surrenderConfirmPanel;
    [SerializeField] private GameObject settingsPanel;

    [Header("Style")]
    [Tooltip("LoginScene modal sprites; leave empty to keep the authored look.")]
    [SerializeField] private ModalTheme theme = new ModalTheme();

    private void Awake()
    {
        var window = surrenderConfirmPanel != null
            ? surrenderConfirmPanel.transform.Find("ConfirmWindow") as RectTransform : null;
        SettingsModalStyle.ApplySurrender(window, theme);
    }

    public void OpenConfirmation()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (surrenderConfirmPanel != null) surrenderConfirmPanel.SetActive(true);
    }

    public void CloseConfirmation()
    {
        if (surrenderConfirmPanel != null) surrenderConfirmPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void Surrender()
    {
        NetworkMatchBridge bridge = FindAnyObjectByType<NetworkMatchBridge>();
        if (bridge != null)
        {
            bridge.RequestSurrender();
        }
        
        if (surrenderConfirmPanel != null) surrenderConfirmPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }
}