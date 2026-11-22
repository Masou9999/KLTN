#if UNITY_EDITOR
using System;
using System.IO;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Installs the mana ring under the GameScene action orb. The frame image moves from
    /// ActionSprite into a "Frame" child placed after "ManaRing", so the frame draws over
    /// the inner ends of the slots inside the same canvas. Re-running is idempotent and
    /// keeps the MatchDebugControls/MatchHudPresenter references wired.
    /// </summary>
    public static class ManaRingInstaller
    {
        #region Configuration

        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string FilledPath = "Assets/Ui/Mana/ManaSlot_Fill.png";
        private const string ReservedPath = "Assets/Ui/Mana/ManaSlot_Reserved.png";
        private const string EmptyPath = "Assets/Ui/Mana/ManaSlot_Empty.png";
        private const string FrameAvailablePath = "Assets/Ui/Mana/ActionButton_Available.png";
        private const string FrameUnavailablePath = "Assets/Ui/Mana/ActionButton_Unavailable.png";
        private const string RingName = "ManaRing";
        private const string FrameName = "Frame";
        private const string PreviewFolder = "Temp/ManaRingPreview";
        private const int PreviewLayer = 31;
        private const int PreviewSize = 1024;

        #endregion

        #region Public Commands

        [MenuItem("Tools/KLTN/Install Mana Ring")]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            InstallIntoOpenScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ManaRing] Installed under ActionSprite in GameScene.");
        }

        /// <summary>Batch entry: install, then render preview PNGs into Temp/.</summary>
        public static void InstallAndCaptureBatch()
        {
            try
            {
                Install();
                CapturePreviews();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Tools/KLTN/Capture Mana Ring Previews")]
        public static void CapturePreviews()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RectTransform actionRoot = FindActionRoot();
            Directory.CreateDirectory(PreviewFolder);

            var cameraObject = new GameObject("ManaRingPreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(10, 12, 16, 255);
            camera.cullingMask = 1 << PreviewLayer;
            camera.orthographic = true;
            var target = new RenderTexture(PreviewSize, PreviewSize, 24);
            camera.targetTexture = target;

            var canvasObject = new GameObject("ManaRingPreviewCanvas", typeof(Canvas));
            canvasObject.layer = PreviewLayer;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;

            GameObject copy = Object.Instantiate(actionRoot.gameObject, canvasObject.transform);
            foreach (Transform child in copy.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = PreviewLayer;
            foreach (Transform child in copy.transform)
            {
                if (child.name != RingName && child.name != FrameName)
                    child.gameObject.SetActive(false);
            }

            var rect = (RectTransform)copy.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(100f, 100f);
            rect.localScale = Vector3.one * (PreviewSize * 0.9f / 100f);

            var ring = copy.GetComponentInChildren<ManaRingView>(true);
            ring.EnsureSlots();
            Capture(camera, target, ring, "self0_opp0", 0, 0, 0);
            Capture(camera, target, ring, "self5_opp5", 5, 0, 5);
            Capture(camera, target, ring, "self8res2_opp3", 8, 2, 3);
            Capture(camera, target, ring, "self10_opp10", 10, 0, 10);

            // Responsive check: same ring at a quarter of the size.
            rect.localScale = Vector3.one * (PreviewSize * 0.3f / 100f);
            Capture(camera, target, ring, "small_self8res2_opp3", 8, 2, 3);

            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(canvasObject);
            Object.DestroyImmediate(cameraObject);
            Debug.Log($"[ManaRing] Previews written to {Path.GetFullPath(PreviewFolder)} ({scene.name}).");
        }

        #endregion

        #region Installation

        private static void InstallIntoOpenScene()
        {
            RectTransform actionRoot = FindActionRoot();
            var controls = Object.FindAnyObjectByType<MatchDebugControls>(FindObjectsInactive.Include);
            Image frame = EnsureFrame(actionRoot);

            var controlsObject = new SerializedObject(controls);
            controlsObject.FindProperty("actionBackground").objectReferenceValue = frame;
            controlsObject.ApplyModifiedPropertiesWithoutUndo();

            ManaRingView ring = EnsureRing(actionRoot);
            ring.transform.SetSiblingIndex(0);
            frame.transform.SetSiblingIndex(1);

            var hud = Object.FindAnyObjectByType<MatchHudPresenter>(FindObjectsInactive.Include);
            if (hud == null) throw new InvalidOperationException("MatchHudPresenter is missing.");
            var hudObject = new SerializedObject(hud);
            hudObject.FindProperty("manaRing").objectReferenceValue = ring;
            hudObject.ApplyModifiedPropertiesWithoutUndo();

            UpdateMulliganFrame();
        }

        /// <summary>
        /// The Mulligan modal draws its own copy of the action orb above the HUD. Give it
        /// the new frame (no painted mana pips) and let its button swap to the Unavailable
        /// art whenever it is not interactable, matching the in-match action button.
        /// </summary>
        private static void UpdateMulliganFrame()
        {
            var mulligan = Object.FindAnyObjectByType<MulliganPresenter>(
                FindObjectsInactive.Include);
            if (mulligan == null) return;

            var button = new SerializedObject(mulligan).FindProperty("actionButton")
                .objectReferenceValue as Button;
            if (button == null || button.transform.parent == null) return;

            var frame = button.transform.parent.GetComponent<Image>();
            if (frame == null) return;

            frame.sprite = LoadSprite(FrameAvailablePath);
            button.transition = Selectable.Transition.SpriteSwap;
            button.targetGraphic = frame;
            SpriteState state = button.spriteState;
            state.disabledSprite = LoadSprite(FrameUnavailablePath);
            button.spriteState = state;
            EditorUtility.SetDirty(frame);
            EditorUtility.SetDirty(button);
        }

        private static RectTransform FindActionRoot()
        {
            var controls = Object.FindAnyObjectByType<MatchDebugControls>(FindObjectsInactive.Include);
            if (controls == null) throw new InvalidOperationException("MatchDebugControls is missing.");
            var button = new SerializedObject(controls).FindProperty("actionButton")
                .objectReferenceValue as Button;
            if (button == null || button.transform.parent == null)
                throw new InvalidOperationException("Action button is not wired.");
            return (RectTransform)button.transform.parent;
        }

        private static Image EnsureFrame(RectTransform actionRoot)
        {
            Transform existing = actionRoot.Find(FrameName);
            if (existing != null) return existing.GetComponent<Image>();

            var source = actionRoot.GetComponent<Image>();
            if (source == null) throw new InvalidOperationException("ActionSprite has no frame Image.");

            RectTransform frameRect = CreateStretched(FrameName, actionRoot);
            frameRect.gameObject.AddComponent<CanvasRenderer>();
            var frame = frameRect.gameObject.AddComponent<Image>();
            frame.sprite = source.sprite;
            frame.color = source.color;
            frame.type = source.type;
            frame.preserveAspect = source.preserveAspect;
            frame.raycastTarget = source.raycastTarget;
            frame.material = source.material == source.defaultMaterial ? null : source.material;

            Object.DestroyImmediate(source);
            return frame;
        }

        private static ManaRingView EnsureRing(RectTransform actionRoot)
        {
            Transform existing = actionRoot.Find(RingName);
            RectTransform ringRect = existing != null
                ? (RectTransform)existing
                : CreateStretched(RingName, actionRoot);

            var ring = ringRect.GetComponent<ManaRingView>();
            if (ring == null) ring = ringRect.gameObject.AddComponent<ManaRingView>();

            var ringObject = new SerializedObject(ring);
            ringObject.FindProperty("filledSprite").objectReferenceValue = LoadSprite(FilledPath);
            ringObject.FindProperty("reservedSprite").objectReferenceValue = LoadSprite(ReservedPath);
            ringObject.FindProperty("emptySprite").objectReferenceValue = LoadSprite(EmptyPath);
            ringObject.ApplyModifiedPropertiesWithoutUndo();

            ring.EnsureSlots();
            ring.RefreshAll();
            return ring;
        }

        #endregion

        #region Helpers

        private static void Capture(
            Camera camera,
            RenderTexture target,
            ManaRingView ring,
            string name,
            int selfMana,
            int selfReserved,
            int opponentMana)
        {
            ring.Show(selfMana, selfReserved, opponentMana);
            ring.RefreshAll();
            Canvas.ForceUpdateCanvases();
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(Path.Combine(PreviewFolder, name + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        private static RectTransform CreateStretched(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Sprite LoadSprite(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException($"Sprite missing at {path}.");
            return sprite;
        }

        #endregion
    }
}
#endif
