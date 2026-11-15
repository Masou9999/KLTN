#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using KLTN.Audio;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Sandbox.Editor
{
    /// <summary>Explicitly invoked by the user in the running sandbox; never opens/saves scenes.</summary>
    public static class ThienLinhCaiSpiritValidation
    {
        private static int scenario, step, errors;
        private static bool sawKill, sawRevive, sawTail, sawDrain;
        private static double deadline;
        private static AudioSource observedAudio;

        [MenuItem("Tools/KLTN/Temporary/Validate Thien Linh Cai Spirit (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || Object.FindAnyObjectByType<AbilitySandboxRunner>() == null)
            {
                Debug.LogWarning("Open AbilityAnimationSandbox, enter Play Mode, then run this command.");
                return;
            }
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            try
            {
                var p = Resources.Load<SpiritSpellProfile>("VFX/ThienLinhCaiSpirit");
                Check(p != null && p.IsUsable && p.whoosh != null && p.explosion != null && p.healing != null, "Imported spell assets");
                Check(p.reviveSheet.width == 3456 && p.reviveSheet.height == 1920, "Original 9x5 atlas resolution");
                AbilitySandboxValidation.ValidateModel();
                ValidateRibbonRenderer();
                scenario = step = errors = 0;
                deadline = EditorApplication.timeSinceStartup + 90;
                EditorApplication.update += Tick;
                Application.logMessageReceived += OnLog;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        private static void ValidateRibbonRenderer()
        {
            var root = new GameObject("SpiritRibbonRenderProbe", typeof(RectTransform), typeof(Canvas));
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var go = new GameObject("ProbeStrip", typeof(RectTransform), typeof(SpiritRibbonGraphic));
                go.transform.SetParent(root.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(200, 100);
                var graphic = go.GetComponent<SpiritRibbonGraphic>();
                Check(go.GetComponent<CanvasRenderer>() != null, "Ribbon creates its CanvasRenderer");
                graphic.Draw(new[] { Vector2.zero, new Vector2(50, 0), new Vector2(100, 0) },
                    10, 90, 20, Color.white);
                Canvas.ForceUpdateCanvases();
                Mesh mesh = graphic.canvasRenderer.GetMesh();
                Check(mesh != null && mesh.vertexCount > 0 && mesh.triangles.Length > 0, "Ribbon submits a nonempty render mesh");
                Check(mesh.bounds.size.x > 70 && mesh.bounds.size.y > 0, "Clipped ribbon has visible bounds");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        }

        private static void Tick()
        {
            try
            {
                Check(EditorApplication.isPlaying, "Play Mode interrupted");
                Check(EditorApplication.timeSinceStartup < deadline, "Timeout " + scenario + "/" + step);
                var runner = Object.FindAnyObjectByType<AbilitySandboxRunner>();
                Check(runner != null, "Sandbox still open");
                if (step == 0)
                {
                    runner.ResetScene();
                    sawKill = sawRevive = sawTail = sawDrain = false;
                    observedAudio = null;
                    step++;
                }
                else if (step == 1 && !runner.IsBusy)
                {
                    Check(runner.ApplyCounts(new[] { 3, 0, 0, 0, 0, 0, 0 }, new[] { 0, 1, 0, 0, 0 }), "Prepare fixture");
                    step++;
                }
                else if (step == 2 && !runner.IsBusy)
                {
                    runner.Summon(runner.Model.State.Host.DrawHand.Single().InstanceId.ToString());
                    step++;
                }
                else if (step == 3 && !runner.IsBusy)
                {
                    var pending = runner.Model.State.PendingSelection;
                    Check(pending != null, "Ally selection requested");
                    var ally = runner.Model.State.Host.Reserve.Where(x => x.DefinitionId == "MaLon")
                        .ElementAt(scenario % 3);
                    runner.Select(pending.RequestId.ToString(), new[] { ally.InstanceId.ToString() }, Array.Empty<string>());
                    step++;
                }
                else if (step == 4)
                {
                    var view = Object.FindAnyObjectByType<SpiritSpellView>();
                    if (view != null)
                    {
                        Check(!Object.FindObjectsByType<Transform>().Any(x => x.name.StartsWith("SpiritAftermath") || x.name == "SpiritBurningRim"),
                            "Aftermath removed");
                        if (view.Phase == "Charge")
                        {
                            var charge = Object.FindObjectsByType<RawImage>().Single(x => x.name == "SpiritChargeSprite");
                            var source = runner.Model.State.Host.Reserve.Single(c => c.DefinitionId == "ThienLinhCai");
                            Check(runner.board.TryGetFaceUpVisual(source.InstanceId.ToString(), out var sourceView), "Source view");
                            Check(Vector3.Distance(charge.rectTransform.position,
                                sourceView.CardRect.TransformPoint(sourceView.CardRect.rect.center)) < 1f, "Charge centered on source");
                            Check(charge.uvRect.x > .3f || charge.uvRect.y < .5f, "Skip empty charge cell");
                        }
                        if (view.Phase == "Impact")
                            Check(Object.FindObjectsByType<RawImage>().Any(x => x.name == "SpiritExplosionSprite" && x.enabled), "Explosion sprite at impact");
                        sawKill |= view.Phase == "Flight" || view.Phase == "Impact";
                        sawRevive |= view.Phase == "Coiling" || view.Phase == "TailFlow";
                        sawTail |= view.Phase == "TailFlow" && view.SourceCutOff && !view.StreamDrained;
                        sawDrain |= view.StreamDrained;
                        observedAudio = view.GetComponents<AudioSource>().FirstOrDefault();
                        Check(!Object.FindObjectsByType<Transform>().Any(x => x.name == "AbilityFlameTrail" || x.name == "ReviveLightningBolt"), "No generic projectile/revive lightning");
                        if ((scenario == 2 && view.Phase == "Charge") || (scenario == 3 && view.Phase == "Stream")
                            || (scenario == 4 && view.Phase == "TailFlow"))
                        {
                            runner.ResetScene(); step = 5;
                        }
                    }
                    if (step == 4 && !runner.IsBusy)
                    {
                        Check(sawKill && sawRevive && sawTail && sawDrain, "Kill, revive, source cutoff and drained tail observed");
                        Check(runner.Model.State.Host.DrawHand.Count == 0, "No extra draw");
                        Check(runner.Model.State.Host.Graveyard.Count == 0 && runner.Model.State.Host.Reserve.Count == 4, "Selected ally revived");
                        step = 5;
                    }
                }
                else if (step == 5 && !runner.IsBusy)
                {
                    if (scenario < 2)
                    {
                        var completed = Object.FindAnyObjectByType<SpiritSpellView>();
                        var profile = Resources.Load<SpiritSpellProfile>("VFX/ThienLinhCaiSpirit");
                        Check(completed != null && completed.GetComponents<AudioSource>().Any(x => x.clip == profile.healing && x.isPlaying),
                            "Healing sound continues without blocking input");
                        runner.ResetScene();
                    }
                    Check(!Object.FindObjectsByType<Transform>().Any(x => x.name == "SpiritSpellGraphics" || x.name == "SpiritCoilBehind"), "Front/back visual cleanup");
                    var spell = Object.FindAnyObjectByType<SpiritSpellView>();
                    Check(spell == null || spell.GetComponents<AudioSource>().All(x => !x.isPlaying), "All spell audio stopped");
                    Check(observedAudio == null || !observedAudio.isPlaying, "Audio cleanup");
                    scenario++;
                    step = 0;
                    if (scenario == 5)
                    {
                        Check(errors == 0, "No runtime errors");
                        Finish(true, "Two target positions; blue-fire kill and reconstruction; source cutoff and tail drain; reset during charge/stream/tail; model regression passed.");
                    }
                }
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory("Logs/ThienLinhCaiSpirit");
            File.WriteAllText("Logs/ThienLinhCaiSpirit/result.txt", (success ? "PASS: " : "FAIL: ") + message);
            Debug.Log("[ThienLinhCaiSpirit] " + (success ? "PASS: " : "FAIL: ") + message);
        }
    }
}
#endif
