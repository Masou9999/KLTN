#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using CMCMProductions;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Sandbox.Editor
{
    /// <summary>Exercises real hand-cost effects, projection privacy and reset cleanup.</summary>
    [InitializeOnLoad]
    public static class ManaReductionValidation
    {
        #region Validation Lifecycle

        private const string Key = "ManaReduction.Validation";
        private const string Folder = "Logs/ManaReduction";
        private static int scenario, step, errors;
        private static double deadline;
        private static string targetId;
        private static Vector3 originalScale;
        private static bool sawBefore, sawAfter;
        private static float largestScale;

        static ManaReductionValidation()
        {
            if (SessionState.GetBool(Key, false)) Attach();
        }

        /// <summary>Batch entry; opens only the offline sandbox and exits on completion.</summary>
        public static void RunBatch()
        {
            try
            {
                ValidateProjection();
                EditorSceneManager.OpenScene(AbilitySandboxBuilder.ScenePath);
                SessionState.SetBool(Key, true);
                Attach();
                EditorApplication.isPlaying = true;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        [MenuItem("Tools/KLTN/Temporary/Validate Mana Reduction (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying
                || Object.FindAnyObjectByType<AbilitySandboxRunner>() == null)
            {
                Debug.LogWarning("Enter Play Mode in AbilityAnimationSandbox first.");
                return;
            }
            try
            {
                ValidateProjection();
                Attach();
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        private static void Attach()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            scenario = step = errors = 0;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error
                || type == LogType.Assert) errors++;
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
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/result.txt",
                (success ? "PASS: " : "FAIL: ") + message);
            Debug.Log("[ManaReduction] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        #endregion

        #region Real Animation Scenarios

        private static void Tick()
        {
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline,
                    "Timeout at " + scenario + "/" + step);
                if (!EditorApplication.isPlaying) return;
                var runner = Object.FindAnyObjectByType<AbilitySandboxRunner>();
                if (runner == null || runner.Model == null) return;
                if (step == 0 && !runner.IsBusy)
                {
                    runner.ResetScene();
                    sawBefore = sawAfter = false;
                    largestScale = 1f;
                    step = 1;
                }
                else if (step == 1 && !runner.IsBusy)
                {
                    int[] reserve = new int[AbilitySandboxModel.ReserveIds.Length];
                    int[] hand = new int[AbilitySandboxModel.HandIds.Length];
                    reserve[Array.IndexOf(AbilitySandboxModel.ReserveIds, "MaLon")] = 2;
                    hand[Array.IndexOf(AbilitySandboxModel.HandIds, "MaTroi")] = 1;
                    hand[Array.IndexOf(AbilitySandboxModel.HandIds,
                        scenario == 0 ? "MaGa" : "MaDoi")] = 1;
                    Check(runner.ApplyCounts(reserve, hand), "Fixture counts");
                    step = 2;
                }
                else if (step == 2 && !runner.IsBusy)
                {
                    targetId = runner.Model.State.Host.DrawHand.Single(c =>
                        c.DefinitionId == "MaTroi").InstanceId.ToString();
                    originalScale = Target().CardRect.localScale;
                    runner.Summon(runner.Model.State.Host.DrawHand.Single(c =>
                        c.DefinitionId == (scenario == 0 ? "MaGa" : "MaDoi"))
                        .InstanceId.ToString());
                    step = 3;
                }
                else if (step == 3 && !runner.IsBusy)
                {
                    string selected = scenario == 0 ? targetId
                        : runner.Model.State.Host.Reserve.First(c =>
                            c.DefinitionId == "MaLon").InstanceId.ToString();
                    runner.Select(runner.Model.State.PendingSelection.RequestId.ToString(),
                        new[] { selected }, Array.Empty<string>());
                    step = 4;
                }
                else if (step == 4)
                {
                    Observe(runner);
                    if (step != 4 || runner.IsBusy) return;
                    Check(sawBefore && sawAfter, "Digits before/after cost-change frame");
                    NetworkCardVisual card = Target();
                    Check(card.Energy == 9 && card.ManaAnchor.GetComponent<TMPro.TMP_Text>()
                        .text == "9", "Final projected mana digit");
                    Check(Vector3.Distance(card.CardRect.localScale, originalScale) < .001f,
                        "Exact scale restoration after completion");
                    Check(scenario == 0 ? largestScale < 1.01f : largestScale > 1.24f,
                        "MaGa remains still; MaTroi reaches 1.3 pulse");
                    Check(runner.Model.State.Host.DrawHand.Any(c =>
                        c.InstanceId.ToString() == targetId), "Target stays in DrawHand");
                    var view = Object.FindAnyObjectByType<ManaReductionView>();
                    Check(view != null && view.GetComponents<AudioSource>()
                        .Any(a => a.isPlaying), "Nonblocking sound tail survives projection");
                    runner.ResetScene();
                    step = 5;
                }
                else if (step == 5 && !runner.IsBusy)
                {
                    Check(!Object.FindObjectsByType<RawImage>().Any(i =>
                        i.name == "ManaReductionSwirl"), "Reset removes badge effect");
                    Check(Object.FindObjectsByType<ManaReductionView>().All(v =>
                        v.GetComponents<AudioSource>().All(a => !a.isPlaying)),
                        "Reset stops sound tails");
                    scenario++;
                    step = 0;
                    if (scenario == 4)
                    {
                        Check(errors == 0, "Runtime errors: " + errors);
                        Finish(true, "Owner/opponent DTO and journal privacy; captured costs; "
                            + "real MaGa/MaTroi effects; badge anchor; frame-20 digit switch; "
                            + "pulse/scale restoration; no projectile; audio tails; "
                            + "reset during early/late swirl; zero runtime errors.");
                    }
                }
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }

        private static NetworkCardVisual Target()
        {
            return Object.FindObjectsByType<NetworkCardVisual>()
                .Single(c => c.InstanceId == targetId);
        }

        private static void Observe(AbilitySandboxRunner runner)
        {
            var image = Object.FindObjectsByType<RawImage>()
                .SingleOrDefault(i => i.name == "ManaReductionSwirl");
            if (image == null) return;
            var view = Object.FindAnyObjectByType<ManaReductionView>();
            NetworkCardVisual card = Target();
            int frame = view.CurrentFrame;
            Check(!image.raycastTarget, "Decorative effect must not block input");
            Check(Vector3.Distance(image.rectTransform.position,
                card.ManaAnchor.TransformPoint(card.ManaAnchor.rect.center)) < 1f,
                "Swirl remains centered on the mana badge");
            Check(!Object.FindObjectsByType<Transform>().Any(t =>
                t.name == "AbilityFlameTrail" || t.name == "AbilityProjectile"),
                "No generic projectile during mana reduction");
            largestScale = Mathf.Max(largestScale,
                card.CardRect.localScale.x / originalScale.x);
            if (frame < 20)
            {
                Check(card.Energy == 10, "Captured cost before frame 20");
                sawBefore = true;
            }
            else
            {
                Check(card.Energy == 9, "Captured cost from frame 20");
                sawAfter = true;
            }
            if ((scenario == 2 && frame >= 2 && frame < 10)
                || (scenario == 3 && frame >= 21))
            {
                Vector3 expected = originalScale;
                view.ClearVisuals(true);
                Check(Vector3.Distance(card.CardRect.localScale, expected) < .001f,
                    "Interrupted pulse restores exact original scale");
                Check(card.Energy == 10, "Interrupted effect restores staged original cost");
                runner.ResetScene();
                step = 5;
            }
        }

        #endregion

        #region Projection Privacy

        public static void ValidateProjection()
        {
            var profile = Resources.Load<ManaReductionProfile>("VFX/ManaReduction");
            Check(profile != null && profile.IsUsable && profile.sound != null,
                "Imported atlas and original sound");
            Check(profile.sheet.width == 1536 && profile.sheet.height == 1024,
                "Original 24-cell atlas resolution");
            Check(Mathf.Approximately(profile.frameSeconds, .05f)
                && profile.costChangeFrame == 20
                && Mathf.Abs(profile.widthRelativeToCard - .403f) < .0001f,
                "Reference timing/size (+30%)");
            var definitions = Resources.LoadAll<Card>("Cards")
                .Select(CardDefinitionMapper.Create).ToDictionary(c => c.Id);
            ManaReductionProjectionChecks.Validate(definitions);
        }

        #endregion
    }
}
#endif
