using KLTN.Game.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Editor
{
    /// <summary>Creates the reusable selection-beam prefab without overwriting edits.</summary>
    public static class AbilityTargetLinkPrefabInstaller
    {
        private const string PrefabPath = "Assets/Prefabs/UI/AbilityTargetLinks.prefab";

        [MenuItem("Tools/KLTN/Create Ability Target Link Prefab")]
        public static void CreatePrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            if (existing != null)
            {
                if (existing.GetComponentInChildren<AbilityTargetLinkGraphic>() == null)
                {
                    Debug.LogError($"{PrefabPath} exists without AbilityTargetLinkGraphic.");
                    return;
                }

                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var root = new GameObject(
                "AbilityTargetLinks",
                typeof(RectTransform),
                typeof(Canvas)
            );

            try
            {
                root.layer = LayerMask.NameToLayer("UI");

                var rect = (RectTransform)root.transform;
                // Give Prefab Mode a visible editing surface. The runtime controller
                // stretches this rect to the match canvas after instantiation.
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(1000f, 600f);

                Canvas canvas = root.GetComponent<Canvas>();
                MatchSorting.Apply(canvas, MatchSorting.Targeting);

                var beam = new GameObject(
                    "Beams",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(AbilityTargetLinkGraphic)
                );
                beam.layer = root.layer;
                beam.transform.SetParent(root.transform, false);

                var beamRect = (RectTransform)beam.transform;
                beamRect.anchorMin = Vector2.zero;
                beamRect.anchorMax = Vector2.one;
                beamRect.offsetMin = Vector2.zero;
                beamRect.offsetMax = Vector2.zero;

                AbilityTargetLinkGraphic graphic =
                    beam.GetComponent<AbilityTargetLinkGraphic>();
                graphic.raycastTarget = false;
                graphic.maskable = false;

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = saved;
                EditorGUIUtility.PingObject(saved);
                Debug.Log($"Created {PrefabPath}. Assign it to Game's Target Link Prefab field.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
