using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>
    /// Controls the mode picker and isolates keyboard navigation from the menu.
    /// </summary>
    public sealed class PlayModeModal : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private GameObject modalRoot;
        [SerializeField] private CanvasGroup menuGroup;
        [SerializeField] private Button playButton;
        [SerializeField] private Button firstOption;

        #endregion

        #region Public Commands

        public void Open()
        {
            menuGroup.interactable = false;
            modalRoot.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(firstOption.gameObject);
        }

        public void Close()
        {
            modalRoot.SetActive(false);
            menuGroup.interactable = true;
            EventSystem.current?.SetSelectedGameObject(playButton.gameObject);
        }

        #endregion
    }
}
