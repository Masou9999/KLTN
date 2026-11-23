using KLTN.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Adds a DropHintComet child to every board slot in the open scenes so the comet
    /// can be tuned in the Hierarchy. Existing comets are kept untouched.
    /// </summary>
    public static class BoardSlotDropHintInstaller
    {
        [MenuItem("Tools/KLTN/Install Board Slot Drop Hints (GameScene)")]
        public static void Install()
        {
            BoardSlotDropZone[] zones = Object.FindObjectsByType<BoardSlotDropZone>(
                FindObjectsInactive.Include
            );

            int created = 0;
            int linked = 0;

            foreach (BoardSlotDropZone zone in zones)
            {
                if (EditorUtility.IsPersistent(zone))
                {
                    continue;
                }

                var serializedZone = new SerializedObject(zone);
                SerializedProperty hintProperty = serializedZone.FindProperty("dropHint");

                if (hintProperty.objectReferenceValue != null)
                {
                    continue;
                }

                BoardSlotCometGraphic comet =
                    zone.GetComponentInChildren<BoardSlotCometGraphic>(true);

                if (comet == null)
                {
                    comet = BoardSlotDropZone.CreateDropHint(zone.transform);
                    comet.gameObject.SetActive(false);
                    Undo.RegisterCreatedObjectUndo(comet.gameObject, "Install Board Slot Drop Hint");
                    created++;
                }
                else
                {
                    linked++;
                }

                hintProperty.objectReferenceValue = comet;
                serializedZone.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            }

            Debug.Log(
                $"[BoardSlotDropHintInstaller] Created {created}, linked {linked}, "
                    + $"scanned {zones.Length} board slot(s). Save the scene to keep them."
            );
        }
    }
}
