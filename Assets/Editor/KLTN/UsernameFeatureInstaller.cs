#if UNITY_EDITOR
using System;
using System.Linq;
using KLTN.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Editor
{
    /// <summary>Installs and validates the rename controls without rebuilding unrelated menu UI.</summary>
    public static class UsernameFeatureInstaller
    {
        [MenuItem("Tools/KLTN/Install Username Change")]
        public static void Install()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var name = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include)
                .Single(t => t.name == "NameUser" && t.transform.parent.name == "ProfileGroup");
            UsernameChangeView.Attach(name);
            var button = name.transform.parent.Find("ChangeUsername").GetComponent<Button>();
            var icon = button.transform.Find("Icon").GetComponent<Image>();
            if (icon.sprite == null) throw new InvalidOperationException("Rename sprite missing.");
            var view = name.transform.parent.GetComponent<UsernameChangeView>();
            var serialized = new SerializedObject(view);
            foreach (string field in new[] { "editButton", "modal", "input", "feedback", "accept", "cancel", "acceptLabel", "confirmationModal", "confirmationText", "confirm", "back" })
                if (serialized.FindProperty(field).objectReferenceValue == null)
                    throw new InvalidOperationException("Missing rename reference: " + field);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ShopCatalogInstaller.ExportRules();
            AssetDatabase.SaveAssets();
            string previous = name.text;
            name.text = "user745820";
            MenuPolishInstaller.Preview("UsernameChange-Profile");
            var modal = (GameObject)serialized.FindProperty("modal").objectReferenceValue;
            var feedback = (TMP_Text)serialized.FindProperty("feedback").objectReferenceValue;
            feedback.text = "Bạn còn 1 lần đổi tên";
            modal.SetActive(true);
            MenuPolishInstaller.Preview("UsernameChange-Modal");
            modal.SetActive(false);
            name.text = previous;
            Debug.Log("[UsernameChange] Installed, references validated, two previews rendered.");
        }
    }
}
#endif
