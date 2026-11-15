#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KLTN.Audio;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Sandbox.Editor
{
    /// <summary>Batch Play Mode regression for the real two-target skill and cancellation.</summary>
    [InitializeOnLoad]
    public static class ThanTrungLightningValidation
    {
        private const string Key = "ThanTrungLightning.Validation";
        private static int step;
        private static int errors;
        private static double deadline;
        private static readonly HashSet<LightningBoltGraphic> strikes = new HashSet<LightningBoltGraphic>();
        private static readonly HashSet<LightningBoltGraphic> captures = new HashSet<LightningBoltGraphic>();
        private static RectTransform sourceRect;
        private static Vector3 sourceScale;
        private static double resetAt;

        static ThanTrungLightningValidation()
        {
            if (!SessionState.GetBool(Key, false)) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        public static void Run()
        {
            var profile = Resources.Load<LightningEffectProfile>("VFX/ThanTrungLightning");
            Check(profile != null && profile.IsUsable, "Profile and atlas load");
            Check(profile.strikeSfx != null && profile.strikeSfx.length > 0f, "SFX imported");
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(profile.atlas));
            Check(importer.alphaIsTransparency && !importer.mipmapEnabled,
                "Texture preserves alpha without cross-frame mip bleeding");
            AbilitySandboxValidation.ValidateModel();
            EditorSceneManager.OpenScene(AbilitySandboxBuilder.ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        }

        private static void Tick()
        {
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Timeout at step " + step);
                if (!EditorApplication.isPlaying) return;
                var runner = Object.FindAnyObjectByType<AbilitySandboxRunner>();
                if (runner == null || runner.Model == null) return;
                ObserveStrikes();
                switch (step)
                {
                    case 0:
                        if (runner.IsBusy) return;
                        AddCard(runner);
                        step++;
                        break;
                    case 1:
                    case 5:
                    case 9:
                        if (runner.IsBusy) return;
                        runner.Summon(runner.Model.State.Host.DrawHand[0].InstanceId.ToString());
                        step++;
                        break;
                    case 2:
                    case 6:
                    case 10:
                        if (runner.IsBusy) return;
                        SelectTargets(runner);
                        step++;
                        break;
                    case 3:
                        if (runner.IsBusy) return;
                        Check(strikes.Count == 2, "Exactly two lightning graphics sequences");
                        Check(captures.Count == 2, "Both strikes reach impact");
                        Check(runner.Model.State.Guest.Reserve.Count == 2
                            && runner.Model.State.Host.Reserve.Count == 3, "Both selected units killed");
                        Check(runner.Model.State.Host.NexusHealth == 19
                            && runner.Model.State.Guest.NexusHealth == 19, "Death-trigger chain preserved");
                        Check(!Object.FindObjectsByType<LightningBoltGraphic>().Any(), "No remaining bolt graphics");
                        runner.ResetScene();
                        step++;
                        break;
                    case 4:
                    case 8:
                        if (runner.IsBusy) return;
                        AddCard(runner);
                        step++;
                        break;
                    case 7:
                        if (GameObject.Find("LightningGraphics") == null) return;
                        runner.ResetScene();
                        Check(sourceRect == null || Vector3.Distance(sourceRect.localScale, sourceScale) < 0.001f,
                            "Reset restores source pulse scale");
                        step++;
                        break;
                    case 11:
                        var view = Object.FindAnyObjectByType<LightningEffectView>();
                        var impact = GameObject.Find("LightningGraphics/Impact");
                        if (view == null || impact == null || !impact.GetComponent<Image>().enabled) return;
                        var audio = view.GetComponent<AudioSource>();
                        float gain = Resources.Load<LightningEffectProfile>("VFX/ThanTrungLightning").sfxVolume;
                        Check(audio != null && Mathf.Approximately(audio.volume, AudioVolumeSettings.SfxVolume * gain),
                            "Lightning follows SFX volume");
                        runner.ResetScene();
                        Check(audio == null || !audio.isPlaying, "Reset stops lightning audio");
                        resetAt = EditorApplication.timeSinceStartup;
                        step++;
                        break;
                    case 12:
                        if (EditorApplication.timeSinceStartup - resetAt < 1.5) return;
                        Check(!runner.IsBusy && runner.Model.State.Host.DrawHand.Count == 0,
                            "Reset does not apply a stale snapshot");
                        Check(!Object.FindObjectsByType<LightningBoltGraphic>().Any()
                            && Object.FindAnyObjectByType<LightningEffectView>() == null,
                            "Reset leaves no lightning objects");
                        Check(errors == 0, "Runtime errors: " + errors);
                        Finish(true, "Two target strikes + impacts, death-trigger chain, alpha import, "
                            + "SFX volume, reset during charge and impact, no stale snapshot or leftover VFX; zero runtime errors.");
                        break;
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static void ObserveStrikes()
        {
            if (step != 3) return;
            var graphic = Object.FindObjectsByType<LightningBoltGraphic>()
                .FirstOrDefault(g => g.name == "MainBolt");
            if (graphic == null) return;
            strikes.Add(graphic);
            Image impact = graphic.transform.parent.Find("Impact").GetComponent<Image>();
            if (!impact.enabled || !captures.Add(graphic)) return;
            Check(!graphic.raycastTarget, "Lightning never blocks clicks");
            Capture("than-trung-strike-" + captures.Count);
        }

        internal static void Capture(string name)
        {
            Canvas canvas = Object.FindObjectsByType<Canvas>().First(c => c.isRootCanvas);
            Camera camera = Camera.main;
            Check(camera != null, "Preview camera exists");
            RenderMode previousMode = canvas.renderMode;
            Camera previousCamera = canvas.worldCamera;
            float previousDistance = canvas.planeDistance;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var texture = new RenderTexture(1920, 1080, 24);
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                texture.Create();
                camera.targetTexture = texture;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                // Camera capture can change CanvasScaler/layout. Re-anchor the VFX
                // to the new card centres before rendering this same animation frame.
                Object.FindAnyObjectByType<LightningEffectView>()?.RefreshLayout();
                Object.FindAnyObjectByType<BiteEffectView>()?.RefreshLayout();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                Directory.CreateDirectory("Logs/MenuPolish");
                File.WriteAllBytes("Logs/MenuPolish/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = previousDistance;
                Canvas.ForceUpdateCanvases();
                Object.FindAnyObjectByType<LightningEffectView>()?.RefreshLayout();
                Object.FindAnyObjectByType<BiteEffectView>()?.RefreshLayout();
                Object.DestroyImmediate(image);
                texture.Release();
                Object.DestroyImmediate(texture);
            }
        }

        private static void AddCard(AbilitySandboxRunner runner)
        {
            int[] reserve = AbilitySandboxModel.ReserveIds.Select(id => runner.Model.Count(id, true)).ToArray();
            int[] hand = AbilitySandboxModel.HandIds.Select(id => id == "ThanTrung" ? 1 : 0).ToArray();
            Check(runner.ApplyCounts(reserve, hand), "Add Than Trung");
        }

        private static void SelectTargets(AbilitySandboxRunner runner)
        {
            var source = runner.Model.State.Host.DrawHand.FirstOrDefault(c => c.DefinitionId == "ThanTrung")
                ?? runner.Model.State.Host.Reserve.First(c => c.DefinitionId == "ThanTrung");
            if (runner.board.TryGetFaceUpVisual(source.InstanceId.ToString(), out var visual))
            {
                sourceRect = visual.CardRect;
                sourceScale = sourceRect.localScale;
            }
            var ally = runner.Model.State.Host.Reserve.First(c => c.DefinitionId == "MaLon");
            var enemy = runner.Model.State.Guest.Reserve.First(c => c.DefinitionId == "MaLon");
            runner.Select(runner.Model.State.PendingSelection.RequestId.ToString(),
                new[] { ally.InstanceId.ToString() }, new[] { enemy.InstanceId.ToString() });
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Finish(bool success, string message)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory("Logs/ThanTrungLightning");
            File.WriteAllText("Logs/ThanTrungLightning/result.txt", (success ? "PASS: " : "FAIL: ") + message);
            Debug.Log("[ThanTrungLightning] " + message);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
