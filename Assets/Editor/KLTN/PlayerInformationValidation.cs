#if UNITY_EDITOR
using System;
using System.Linq;
using KLTN.Game.Content;
using KLTN.Game.Presentation;
using KLTN.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>Validates imported profile bindings and the shared detail prefab without signing in.</summary>
    public static class PlayerInformationValidation
    {
        [MenuItem("Tools/KLTN/Validate Player Information")]
        public static void Run()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
                var view = Object.FindAnyObjectByType<PlayerInformationView>();
                Require(view != null, "Missing player information view");
                var fields = new SerializedObject(view);
                foreach (string name in new[] { "avatar", "presentation", "detailPrefab",
                    "bodyFont", "profileFrame", "frameCatalog", "coinIcon", "silverIcon",
                    "theme.fill", "theme.border", "theme.outline", "theme.corner", "theme.lotus",
                    "theme.titleLine", "theme.divider", "theme.close", "theme.tint",
                    "theme.titleFont" })
                    Require(fields.FindProperty(name).objectReferenceValue != null, "Missing " + name);
                var tabs = fields.FindProperty("tabIcons");
                Require(tabs.arraySize == 3, "Expected three tab icons");
                for (int i = 0; i < tabs.arraySize; i++)
                    Require(tabs.GetArrayElementAtIndex(i).objectReferenceValue != null,
                        "Missing tab icon " + i);
                // Currency art must stay shared with the match result screen.
                Require(AssetDatabase.GetAssetPath(fields.FindProperty("coinIcon")
                    .objectReferenceValue).EndsWith("MatchResult/RewardCoin.png"), "Wrong gold icon");
                Require(AssetDatabase.GetAssetPath(fields.FindProperty("silverIcon")
                    .objectReferenceValue).EndsWith("MatchResult/RewardSilver.png"), "Wrong silver icon");
                var avatar = (Image)fields.FindProperty("avatar").objectReferenceValue;
                Require(avatar.gameObject.activeInHierarchy && avatar.sprite != null, "Avatar hidden/missing");
                var holder = avatar.transform.parent;
                Require(holder.name == "Avatar" && holder.GetComponent<Image>() != null &&
                    holder.parent == ((Image)fields.FindProperty("profileFrame").objectReferenceValue)
                    .transform, "Avatar must sit on its background inside ProfileFrame");
                Require(AssetDatabase.GetAssetPath(avatar.sprite) == "Assets/Ui/PlayerInformation/Sprites/TabProfile.png",
                    "Wrong avatar asset");
                var catalog = (ProfileFrameCatalog)fields.FindProperty("frameCatalog").objectReferenceValue;
                Require(catalog.entries.All(e => e.avatarRect.width > 0), "Missing portrait bounds");
                var detail = (CardDetailModal)fields.FindProperty("detailPrefab").objectReferenceValue;
                Require(PrefabUtility.IsPartOfPrefabAsset(detail), "Detail is not a shared prefab");
                var detailFields = new SerializedObject(detail);
                foreach (string name in new[] { "modalRoot", "closeButton", "backdropCloseButton",
                    "previewArtworkMount", "previewCostText", "previewDamageText", "previewHealthText",
                    "previewFallbackNameText", "titleText", "roleAndStatsText", "skillText", "loreText" })
                    Require(detailFields.FindProperty(name).objectReferenceValue != null, "Missing detail " + name);
                Require(!detail.gameObject.activeSelf, "Detail must start hidden");
                EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
                var matchDetail = Object.FindAnyObjectByType<CardDetailModal>(FindObjectsInactive.Include);
                Require(matchDetail != null && PrefabUtility.IsPartOfPrefabInstance(matchDetail),
                    "GameScene must use shared detail prefab");
                Require(matchDetail.GetComponent<Canvas>().sortingOrder == MatchSorting.Order(MatchSorting.Modal, 10),
                    "Detail modal sorting changed");
                Debug.Log("[PlayerInformation] PASS: imported MainMenu bindings, avatar, all rank bounds, shared prefab and GameScene sorting.");
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
