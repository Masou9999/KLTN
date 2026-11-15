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
    public static class MaDoiFeastValidation
    {
        private static int scenario, step, errors;
        private static bool sawSmoke, sawFlight, sawChew;
        private static double deadline;
        private static AudioSource observedAudio;
        private static RectTransform selectedTarget;

        [MenuItem("Tools/KLTN/Temporary/Validate Ma Doi Feast (Play Mode)")]
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
                var p = Resources.Load<FeastEffectProfile>("VFX/MaDoiFeast");
                Check(p != null && p.IsUsable && p.pop != null && p.swish != null && p.chew != null, "Imported assets");
                Check(p.SmokeFrame(0) == 0 && p.SmokeFrame(.609f) == 23, "24-frame timeline");
                Check(Mathf.Approximately(p.chewPitch, 1.5f) && Mathf.Approximately(p.chewDelay, .3f), "Chewing pitch/delay");
                Check(Mathf.Approximately(p.Duration - p.ImpactTime, .2f), "Release after 200 ms");
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
                    sawSmoke = sawFlight = sawChew = false;
                    observedAudio = null;
                    step++;
                }
                else if (step == 1 && !runner.IsBusy)
                {
                    Check(runner.ApplyCounts(new[] { 3, 0, 0, 0, 0, 0, 0 }, new[] { 0, 0, 0, 0, 1 }), "Prepare fixture");
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
                    Check(runner.board.TryGetFaceUpVisual(ally.InstanceId.ToString(), out var targetView), "Target view exists");
                    selectedTarget = targetView.CardRect;
                    runner.Select(pending.RequestId.ToString(), new[] { ally.InstanceId.ToString() }, Array.Empty<string>());
                    step++;
                }
                else if (step == 4)
                {
                    var chicken = Object.FindObjectsByType<RawImage>().FirstOrDefault(x => x.name == "FeastChicken");
                    if (chicken != null)
                    {
                        Check(!Object.FindObjectsByType<Transform>().Any(x => x.name == "AbilityFlameTrail"), "No generic projectile");
                        var view = Object.FindAnyObjectByType<FeastEffectView>();
                        var p = Resources.Load<FeastEffectProfile>("VFX/MaDoiFeast");
                        var smoke = chicken.transform.parent.Find("FeastSmoke").GetComponent<RawImage>();
                        Check(smoke.transform.GetSiblingIndex() > chicken.transform.GetSiblingIndex(), "Smoke above chicken");
                        Check(!chicken.raycastTarget && !smoke.raycastTarget, "Does not intercept input");
                        sawSmoke |= smoke.enabled;
                        sawFlight |= view.Phase == "Flight";
                        observedAudio = view.GetComponent<AudioSource>();
                        Check(observedAudio != null && Mathf.Approximately(observedAudio.volume,
                            AudioVolumeSettings.SfxVolume * p.volume), "SFX gain");
                        if (view.Phase == "Flight")
                        {
                            Check(!smoke.enabled && chicken.enabled, "Flight after smoke");
                            Check(selectedTarget != null && selectedTarget.GetComponent<CanvasGroup>().alpha == 0,
                                "Sacrificed card remains hidden during flight");
                        }
                        if ((scenario == 3 && sawSmoke) || (scenario == 4 && sawFlight))
                        {
                            runner.ResetScene();
                            step = 5;
                        }
                    }
                    if (step == 4 && !runner.IsBusy)
                    {
                        Check(sawSmoke && sawFlight, "Transform and flight observed");
                        Check(runner.Model.State.Host.DrawHand.Count == 0, "No draw added to Ma Doi");
                        Check(runner.Model.State.Host.Graveyard.Count == 1, "One selected ally killed");
                        step = 6;
                    }
                }
                else if (step == 6)
                {
                    Check(!runner.IsBusy, "Chewing does not block gameplay");
                    var view = Object.FindAnyObjectByType<FeastEffectView>();
                    var p = Resources.Load<FeastEffectProfile>("VFX/MaDoiFeast");
                    observedAudio = view.GetComponents<AudioSource>().FirstOrDefault(x => x.clip == p.chew);
                    if (observedAudio != null && observedAudio.isPlaying)
                    {
                        sawChew = true;
                        Check(Mathf.Approximately(observedAudio.pitch, 1.5f), "Independent chewing x1.5");
                        Check(Mathf.Approximately(observedAudio.volume, AudioVolumeSettings.SfxVolume * p.volume), "Chewing SFX gain");
                        if (scenario == 5) { runner.ResetScene(); step = 5; }
                    }
                    else if (sawChew) step = 5;
                }
                else if (step == 5 && !runner.IsBusy)
                {
                    Check(!Object.FindObjectsByType<RawImage>().Any(x => x.name == "FeastChicken" || x.name == "FeastSmoke"), "Visual cleanup");
                    Check(observedAudio == null || !observedAudio.isPlaying, "Audio cleanup");
                    scenario++;
                    step = 0;
                    if (scenario == 6)
                    {
                        Check(errors == 0, "No runtime errors");
                        Finish(true, "Three target positions; smoke above chicken; flight after smoke; consumption, 200 ms release, nonblocking chewing x1.5, SFX gain; reset during transform/flight/chew; model regression passed.");
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
            Directory.CreateDirectory("Logs/MaDoiFeast");
            File.WriteAllText("Logs/MaDoiFeast/result.txt", (success ? "PASS: " : "FAIL: ") + message);
            Debug.Log("[MaDoiFeast] " + (success ? "PASS: " : "FAIL: ") + message);
        }
    }
}
#endif
