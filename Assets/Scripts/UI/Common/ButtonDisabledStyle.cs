using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>
    /// One disabled look for MainMenu and GameScene buttons. A button that is only
    /// locked because a modal is open keeps its normal colour; a button that is
    /// genuinely unavailable (Lưu with nothing to save, a busy rename, match actions)
    /// fades to <see cref="UnavailableColor"/>.
    /// </summary>
    public static class ButtonDisabledStyle
    {
        #region Configuration

        /// <summary>Tint for genuinely unavailable buttons: same hue, reduced opacity.</summary>
        public static readonly Color UnavailableColor = new Color(1, 1, 1, .4f);

        // MainMenu buttons that can be disabled for their own reasons; every other
        // MainMenu button is only ever locked behind a modal.
        private static readonly HashSet<string> MainMenuUnavailable = new HashSet<string>
        {
            "SaveAudioButton", "CloseButton", "ChangeUsername", "Accept", "Confirm", "Back",
            "ExitButton", "Cancel",
        };

        #endregion

        #region Public API

        /// <summary>Keeps the normal colour while a modal locks the button.</summary>
        public static void LockOnly(Selectable selectable)
        {
            if (selectable == null || selectable.transition != Selectable.Transition.ColorTint)
                return;
            var colors = selectable.colors;
            colors.disabledColor = colors.normalColor;
            selectable.colors = colors;
        }

        /// <summary>Fades the button by opacity when it is genuinely unavailable.</summary>
        public static void Unavailable(Selectable selectable)
        {
            if (selectable == null || selectable.transition != Selectable.Transition.ColorTint)
                return;
            var colors = selectable.colors;
            colors.disabledColor = UnavailableColor;
            selectable.colors = colors;
        }

        #endregion

        #region Scene Pass

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= Apply;
            SceneManager.sceneLoaded += Apply;
        }

        /// <summary>
        /// Restyles authored buttons once per load; runtime-built modals set their own
        /// style through <see cref="ModalChrome"/>.
        /// </summary>
        private static void Apply(Scene scene, LoadSceneMode mode)
        {
            bool menu = scene.name == "MainMenu";
            if (!menu && scene.name != "GameScene") return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                {
                    if (menu && !MainMenuUnavailable.Contains(button.name)) LockOnly(button);
                    else Unavailable(button);
                }
            }
        }

        #endregion
    }
}
