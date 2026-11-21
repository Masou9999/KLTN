using UnityEngine;
using UnityEngine.InputSystem;

namespace KLTN.UI
{
    /// <summary>Handles F11 independently of scenes and settings panel visibility.</summary>
    public sealed class FullscreenShortcut : MonoBehaviour
    {
        private void Update()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR && !UNITY_SERVER
            if (!Application.isFocused || Application.isBatchMode) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f11Key.wasPressedThisFrame)
            {
                WindowResolutionSettings.ToggleFullscreenAndSave();
            }
#endif
        }
    }
}
