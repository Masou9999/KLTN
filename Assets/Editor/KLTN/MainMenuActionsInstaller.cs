#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    public static class MainMenuActionsInstaller
    {
        [MenuItem("Tools/KLTN/Update Logout Shortcut")]
        public static void UpdateLogoutShortcut()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var controller = Object.FindAnyObjectByType<MainMenuController>();
            var shortcuts = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .First(c => c.isRootCanvas).transform.Find("MenuShortcuts");
            BuildLogoutShortcut(shortcuts, controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            MenuPolishInstaller.Preview("MainMenuActions");
            Debug.Log("[MainMenuActions] Updated matching circular logout shortcut.");
        }

        private static Sprite ImportShortcutSprite(string name, bool alphaOnly = false)
        {
            string path = "Assets/ConMaConMa/UI/Menu/" + name + ".png";
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (alphaOnly)
            {
                // Use the supplied silhouette as a tintable UI sprite; keep source pixels intact.
                importer.swizzleR = importer.swizzleG = importer.swizzleB = TextureImporterSwizzle.One;
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void BuildLogoutShortcut(Transform shortcuts, MainMenuController controller)
        {
            var oldLogout = shortcuts.Find("LogoutButton");
            if (oldLogout != null) Object.DestroyImmediate(oldLogout.gameObject);
            var shop = shortcuts.Find("ShopIconButton").GetComponent<Button>();
            var logout = Object.Instantiate(shop, shortcuts);
            logout.name = "LogoutButton";
            SetAction(logout, controller.Logout);
            var rect = (RectTransform)logout.transform;
            rect.anchoredPosition = new Vector2(-80, -95);
            rect.sizeDelta = new Vector2(100, 100);
            var artwork = logout.transform.Find("Artwork").GetComponent<Image>();
            artwork.sprite = ImportShortcutSprite("ShortcutBackground");
            var iconObject = new GameObject("LogoutIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(logout.transform, false);
            var icon = iconObject.GetComponent<Image>();
            icon.sprite = ImportShortcutSprite("LogoutIcon", true);
            icon.color = new Color32(210, 200, 193, 255);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.rectTransform.sizeDelta = new Vector2(58, 58);
            ((RectTransform)shop.transform).anchoredPosition = new Vector2(-320, -95);
            ((RectTransform)shortcuts.Find("LeaderboardIconButton")).anchoredPosition = new Vector2(-200, -95);
        }

        public static void Validate()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var controller = Object.FindAnyObjectByType<MainMenuController>();
            var serialized = new SerializedObject(controller);
            var root = (GameObject)serialized.FindProperty("confirmationRoot").objectReferenceValue;
            var group = (CanvasGroup)serialized.FindProperty("menuGroup").objectReferenceValue;
            var message = (TMP_Text)serialized.FindProperty("confirmationMessage").objectReferenceValue;
            var cancel = (Button)serialized.FindProperty("cancelButton").objectReferenceValue;
            Require(!root.activeSelf, "Modal must start hidden");
            controller.Quit();
            Require(root.activeSelf && !group.interactable && cancel.IsInteractable(), "Quit modal must isolate menu input");
            Require(message.text.Contains("thoát"), "Quit prompt missing");
            controller.Logout();
            Require(message.text.Contains("thoát"), "An open confirmation must not change actions");
            controller.CancelConfirmation();
            Require(!root.activeSelf && group.interactable, "Cancel must restore menu");
            controller.ConfirmAction(); // A stale confirmation must do nothing.
            controller.Logout();
            Require(root.activeSelf && message.text.Contains("đăng xuất"), "Logout prompt missing");
            MenuPolishInstaller.Preview("MainMenuLogoutConfirmation");
            controller.CancelConfirmation();
            Require(!root.activeSelf && group.interactable, "Logout cancel must restore menu");
            Debug.Log("[MainMenuActions] PASS: modal isolation, prompts, cancel, duplicate and stale confirmation guards.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new System.InvalidOperationException(message);
        }

        [MenuItem("Tools/KLTN/Install Main Menu Exit and Logout")]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var controller = Object.FindAnyObjectByType<MainMenuController>();
            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(c => c.isRootCanvas);
            var buttons = canvas.GetComponentsInChildren<Button>(true);
            var quit = buttons.FirstOrDefault(b => b.name == "QuitButton") ?? buttons.First(b => b.name == "LogoutButton");
            quit.name = "QuitButton";
            quit.GetComponentInChildren<TMP_Text>().text = "Thoát";
            SetAction(quit, controller.Quit);
            var shortcuts = canvas.transform.Find("MenuShortcuts");
            BuildLogoutShortcut(shortcuts, controller);

            var oldModal = canvas.transform.Find("ActionConfirmationOverlay");
            if (oldModal != null) Object.DestroyImmediate(oldModal.gameObject);
            var overlay = Panel("ActionConfirmationOverlay", canvas.transform, new Color(0, 0, 0, .75f));
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.sizeDelta = Vector2.zero;
            overlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            var panel = Panel("ConfirmationPanel", overlay, new Color(.075f, .065f, .09f, 1));
            panel.sizeDelta = new Vector2(760, 360);
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.55f, .26f, .16f);
            outline.effectDistance = new Vector2(3, -3);
            var font = quit.GetComponentInChildren<TMP_Text>().font;
            Label("Title", panel, font, "XÁC NHẬN", new Vector2(0, 120), new Vector2(680, 60), 42);
            var message = Label("Message", panel, font, "Bạn có chắc muốn thoát trò chơi?", new Vector2(0, 20), new Vector2(680, 120), 32);
            var cancel = Object.Instantiate(quit, panel);
            cancel.name = "CancelButton";
            SetAction(cancel, controller.CancelConfirmation);
            PositionButton(cancel, -175, "Hủy");
            var accept = Object.Instantiate(quit, panel);
            accept.name = "ConfirmButton";
            SetAction(accept, controller.ConfirmAction);
            PositionButton(accept, 175, "Thoát");
            var serialized = new SerializedObject(controller);
            Set(serialized, "confirmationRoot", overlay.gameObject);
            Set(serialized, "menuGroup", canvas.GetComponent<CanvasGroup>() ?? canvas.gameObject.AddComponent<CanvasGroup>());
            Set(serialized, "confirmationMessage", message);
            Set(serialized, "confirmationAcceptLabel", accept.GetComponentInChildren<TMP_Text>());
            Set(serialized, "cancelButton", cancel);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            overlay.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            MenuPolishInstaller.Preview("MainMenuActions");
            overlay.gameObject.SetActive(true);
            MenuPolishInstaller.Preview("MainMenuConfirmation");
            overlay.gameObject.SetActive(false);
            Debug.Log("[MainMenuActions] Installed exit, logout and confirmation modal.");
        }

        private static void Set(SerializedObject target, string name, Object value) => target.FindProperty(name).objectReferenceValue = value;

        private static void SetAction(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick = new Button.ButtonClickedEvent();
            var sound = button.GetComponent<KLTN.Audio.UIButtonSound>();
            if (sound != null) UnityEventTools.AddPersistentListener(button.onClick, sound.PlayClick);
            UnityEventTools.AddPersistentListener(button.onClick, action);
        }

        private static void PositionButton(Button button, float x, string text)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, -110);
            rect.sizeDelta = new Vector2(280, 90);
            var label = button.GetComponentInChildren<TMP_Text>();
            label.text = text;
            label.fontSize = 34;
            label.rectTransform.sizeDelta = new Vector2(260, 80);
        }

        private static RectTransform Panel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string text, Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(.91f, .85f, .74f);
            label.raycastTarget = false;
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = size;
            return label;
        }
    }
}
#endif
