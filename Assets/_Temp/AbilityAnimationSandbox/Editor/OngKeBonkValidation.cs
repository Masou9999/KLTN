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
    public static class OngKeBonkValidation
    {
        private static int scenario, step, errors;
        private static bool sawStick, sawBubble;
        private static double deadline;
        private static AudioSource observedAudio;

        [MenuItem("Tools/KLTN/Temporary/Validate Ong Ke Bonk (Play Mode)")]
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
                var p = Resources.Load<BonkEffectProfile>("VFX/OngKeBonk");
                Check(p != null && p.IsUsable && p.hitSfx != null, "Imported atlas/profile/audio");
                Check(Mathf.Approximately(p.ImpactTime, .4f), "Impact at 400 ms");
                for (int i = 0; i < 3; i++)
                {
                    Rect r = p.sticks[i];
                    Check(r.width > 0 && r.height > 0 && r.xMin >= 0 && r.yMin >= 0
                        && r.xMax <= 1 && r.yMax <= 1, "Valid stick UV " + i);
                    Check(Vector2.Distance(p.grips[i], p.tips[i]) > .5f, "Grip/tip " + i);
                }
                AbilitySandboxValidation.ValidateModel();
                scenario = step = errors = 0;
                deadline = EditorApplication.timeSinceStartup + 90;
                EditorApplication.update += Tick;
                Application.logMessageReceived += OnLog;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
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
                    sawStick = sawBubble = false;
                    observedAudio = null;
                    step++;
                }
                else if (step == 1 && !runner.IsBusy)
                {
                    Check(runner.ApplyCounts(new[] { 3, 0, 0, 0, 0, 0, 0 }, new[] { 0, 0, 1, 0, 0 }), "Prepare fixture");
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
                    var stick = Object.FindObjectsByType<RawImage>().FirstOrDefault(x => x.name == "BonkStick");
                    if (stick != null)
                    {
                        sawStick = true;
                        Check(!Object.FindObjectsByType<Transform>().Any(x => x.name == "AbilityFlameTrail"), "No projectile");
                        var view = Object.FindAnyObjectByType<BonkEffectView>();
                        var p = Resources.Load<BonkEffectProfile>("VFX/OngKeBonk");
                        Check(stick.uvRect == p.sticks[view.LastVariant] && !stick.raycastTarget, "Selected variant/input");
                        observedAudio = view.GetComponent<AudioSource>();
                        Check(observedAudio != null && Mathf.Approximately(observedAudio.volume,
                            AudioVolumeSettings.SfxVolume * p.sfxVolume), "SFX gain");
                        var bubble = stick.transform.parent.Find("BonkBubble").GetComponent<RawImage>();
                        sawBubble |= bubble.enabled && bubble.color.a > .1f;
                        if (scenario == 3 || (scenario == 4 && sawBubble))
                        {
                            runner.ResetScene();
                            step = 5;
                        }
                    }
                    if (step == 4 && !runner.IsBusy)
                    {
                        Check(sawStick && sawBubble, "Swing and bubble observed");
                        Check(runner.Model.State.Host.DrawHand.Count == 2, "Draw two after sacrifice");
                        Check(runner.Model.State.Host.Graveyard.Count == 1, "One selected ally killed");
                        step = 5;
                    }
                }
                else if (step == 5 && !runner.IsBusy)
                {
                    Check(!Object.FindObjectsByType<RawImage>().Any(x => x.name == "BonkStick" || x.name == "BonkBubble"), "Visual cleanup");
                    Check(observedAudio == null || !observedAudio.isPlaying, "Audio cleanup");
                    scenario++;
                    step = 0;
                    if (scenario == 5)
                    {
                        Check(errors == 0, "No runtime errors");
                        Finish(true, "Three target positions, no projectile, bubble, SFX gain, draw two, reset during windup/impact; model regression passed.");
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
            Directory.CreateDirectory("Logs/OngKeBonk");
            File.WriteAllText("Logs/OngKeBonk/result.txt", (success ? "PASS: " : "FAIL: ") + message);
            Debug.Log("[OngKeBonk] " + (success ? "PASS: " : "FAIL: ") + message);
        }
    }
}
#endif
