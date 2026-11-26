using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KLTN.UI.Credits
{
    /// <summary>Returns from the informational credits scene to the main menu.</summary>
    public sealed class CreditsController : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        private bool leaving;

        private void Start()
        {
            if (backButton != null)
                EventSystem.current?.SetSelectedGameObject(backButton.gameObject);
        }

        public void BackToMainMenu()
        {
            if (leaving) return;
            leaving = true;
            SceneManager.LoadScene(SceneNames.MainMenu);
        }
    }
}
