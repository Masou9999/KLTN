#if UNITY_EDITOR
using System;
using System.Linq;
using KLTN.Game.Content;
using KLTN.Game.Domain.Ranking;
using KLTN.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    public static class MainMenuProfileInstaller
    {
        // Safe dark interiors: do not measure the transparent canvas or the protruding ornaments.
        private static readonly Rect[] Interiors =
        {
            new Rect(240, 96, 248, 99), new Rect(237, 90, 252, 98),
            new Rect(224, 82, 249, 98), new Rect(216, 94, 244, 92),
            new Rect(246, 96, 244, 90), new Rect(216, 83, 244, 91),
            new Rect(237, 86, 244, 91),
        };

        [MenuItem("Tools/KLTN/Install Main Menu Profile")]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string catalogPath = "Assets/Resources/ProfileFrameCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<ProfileFrameCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ProfileFrameCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            catalog.entries = new ProfileFrameEntry[RankPolicy.TierCount];
            for (int i = 0; i < catalog.entries.Length; i++)
            {
                var tier = (RankTier)i;
                string path = $"Assets/Sprites/ProfileFrames/{tier}.png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.spritePivot = new Vector2(.5f, .5f);
                importer.spriteBorder = new Vector4(Interiors[i].x, 0, 580 - Interiors[i].xMax, 0);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
                catalog.entries[i] = new ProfileFrameEntry
                {
                    tier = tier, informationRect = Interiors[i],
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path),
                };
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var panel = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include)
                .Single(t => t.name == "ProfilePanel");
            panel.anchoredPosition = new Vector2(20, -10);
            panel.sizeDelta = new Vector2(740, 280);
            panel.GetComponent<Image>().raycastTarget = false;
            var group = (RectTransform)panel.Find("ProfileGroup");
            group.anchorMin = Vector2.zero;
            group.anchorMax = Vector2.one;
            group.offsetMin = group.offsetMax = Vector2.zero;
            group.Find("AvatarFrame").gameObject.SetActive(false);
            var frame = (group.Find("ProfileFrame") ?? group.Find("BackGroundButton")).GetComponent<Image>();
            frame.name = "ProfileFrame";
            var username = ConfigureText(group, "NameUser", new Rect(275, 85, 360, 48), 36);
            var coins = ConfigureText(group, "Coins", new Rect(275, 138, 175, 42), 28);
            var silver = ConfigureText(group, "Silvers", new Rect(460, 138, 175, 42), 28);
            username.text = "Đang tải…";
            coins.text = "— Xu";
            silver.text = "— Bạc";
            catalog.Apply(frame, RankTier.Bronze);
            var view = group.GetComponent<MainMenuProfileView>() ?? group.gameObject.AddComponent<MainMenuProfileView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("username").objectReferenceValue = username;
            serialized.FindProperty("coins").objectReferenceValue = coins;
            serialized.FindProperty("silver").objectReferenceValue = silver;
            serialized.FindProperty("frame").objectReferenceValue = frame;
            serialized.FindProperty("catalog").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            UsernameChangeView.Attach(username);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateAndPreview();
        }

        private static TMP_Text ConfigureText(Transform parent, string name, Rect area, float fontSize)
        {
            var text = parent.Find(name).GetComponent<TMP_Text>();
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
            text.fontSize = text.fontSizeMax = fontSize;
            text.fontSizeMin = 18;
            text.enableAutoSizing = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        public static void ValidateAndPreview()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var view = Object.FindAnyObjectByType<MainMenuProfileView>();
            if (view == null) throw new Exception("MainMenu profile view is missing.");
            var serialized = new SerializedObject(view);
            foreach (var field in new[] { "username", "coins", "silver", "frame", "catalog" })
                if (serialized.FindProperty(field).objectReferenceValue == null)
                    throw new Exception($"Missing profile reference: {field}");
            var catalog = (ProfileFrameCatalog)serialized.FindProperty("catalog").objectReferenceValue;
            var frame = (Image)serialized.FindProperty("frame").objectReferenceValue;
            ((TMP_Text)serialized.FindProperty("username").objectReferenceValue).text = "user123456";
            ((TMP_Text)serialized.FindProperty("coins").objectReferenceValue).text = "123.456 Xu";
            ((TMP_Text)serialized.FindProperty("silver").objectReferenceValue).text = "12.345 Bạc";
            foreach (var entry in catalog.entries)
            {
                catalog.Apply(frame, entry.tier);
                float scale = ProfileFrameCatalog.InformationArea.height / entry.informationRect.height;
                var rect = frame.rectTransform;
                float left = rect.anchoredPosition.x + entry.informationRect.x * scale;
                float top = -rect.anchoredPosition.y + entry.informationRect.y * scale;
                float width = rect.rect.width - (entry.sprite.rect.width - entry.informationRect.width) * scale;
                if (Mathf.Abs(left - 275) > .01f || Mathf.Abs(top - 85) > .01f || Mathf.Abs(width - 360) > .01f)
                    throw new Exception($"Information area is inconsistent: {entry.tier}");
                MenuPolishInstaller.Preview("Profile-" + entry.tier);
            }
            // Preview data is never saved into the scene.
            EditorSceneManager.OpenScene(scene.path);
            Debug.Log("[MainMenuProfile] Validated 7 normalized information areas, scene references and rendered previews.");
        }
    }
}
#endif
