#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KLTN.Audio;
using CMCMProductions;
using KLTN.Game.Content;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Sandbox.Editor
{
    [InitializeOnLoad]
    public static class MaTranhBiteValidation
    {
        private const string Key = "MaTranhBite.Validation";
        private const string Folder = "Logs/MaTranhBite";
        private static int scenario;
        private static int step;
        private static int errors;
        private static double deadline;
        private static double resetAt;
        private static readonly HashSet<RawImage> bites = new HashSet<RawImage>();
        private static readonly HashSet<RawImage> echoes = new HashSet<RawImage>();
        private static bool capturedOpen;

        static MaTranhBiteValidation()
        {
            EditorApplication.update += PollRequest;
            if (!SessionState.GetBool(Key, false)) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        // A one-shot local request runs only in the saved, idle sandbox. Never
        // switches scenes, stops the user's Play session, or saves their edits.
        private static void PollRequest()
        {
            string request = Folder + "/request.txt";
            if (!File.Exists(request) || SessionState.GetBool(Key, false)
                || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || SceneManager.sceneCount != 1
                || SceneManager.GetActiveScene().path != AbilitySandboxBuilder.ScenePath
                || SceneManager.GetActiveScene().isDirty)
            {
                File.WriteAllText(Folder + "/status.txt", "Waiting for saved, idle AbilityAnimationSandbox scene.");
                return;
            }
            try { File.Delete(request); }
            catch (IOException) { return; } // Writer may still be closing the request.
            try { Run(); }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        public static void Run()
        {
            var p = Resources.Load<BiteEffectProfile>("VFX/MaTranhBite");
            Check(p != null && p.IsUsable && p.biteSfx != null, "Profile, nine frames and sound import");
            Check(Mathf.Approximately(p.TimeBeforeFrame(6), 0.3f)
                && Mathf.Approximately(p.TimeBeforeFrame(8), 0.36f), "Impact and echo timing");
            foreach (Rect rect in p.frames)
                Check(rect.width > 0 && rect.height > 0 && rect.xMin >= 0 && rect.yMin >= 0
                    && rect.xMax <= p.sheet.width && rect.yMax <= p.sheet.height,
                    "Frame bounds " + rect + " texture " + p.sheet.width + "x" + p.sheet.height);
            AbilitySandboxValidation.ValidateModel();
            if (Application.isBatchMode) EditorSceneManager.OpenScene(AbilitySandboxBuilder.ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors++;
        }

        private static void Tick()
        {
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Timeout at " + scenario + "/" + step);
                if (!EditorApplication.isPlaying) return;
                var runner = Object.FindAnyObjectByType<AbilitySandboxRunner>();
                if (runner == null || runner.Model == null) return;
                if (step == 2) Observe();
                switch (step)
                {
                    case 0:
                        if (runner.IsBusy) return;
                        Setup(runner);
                        step = 1;
                        break;
                    case 1:
                        if (runner.IsBusy) return;
                        runner.Summon(runner.Model.State.Host.DrawHand.Single().InstanceId.ToString());
                        step = 2;
                        break;
                    case 2:
                        if (scenario >= 4)
                        {
                            RawImage jaw = Object.FindObjectsByType<RawImage>().FirstOrDefault(x => x.name == "BiteJaw");
                            RawImage echo = Object.FindObjectsByType<RawImage>().FirstOrDefault(x => x.name == "BiteEcho");
                            if (jaw == null || (scenario == 5 && (echo == null || !echo.enabled))) return;
                            AudioSource audio = Object.FindAnyObjectByType<BiteEffectView>().GetComponent<AudioSource>();
                            runner.ResetScene();
                            Check(audio == null || !audio.isPlaying, "Reset stops bite SFX");
                            resetAt = EditorApplication.timeSinceStartup;
                            step = 3;
                            return;
                        }
                        if (runner.IsBusy) return;
                        int expected = scenario == 0 ? 2 : scenario == 2 ? 1 : 0;
                        Check(bites.Count == expected && echoes.Count == expected,
                            $"Bite/echo count: scenario={scenario}, bites={bites.Count}, echoes={echoes.Count}, expected={expected}");
                        Check(runner.Model.State.Guest.Reserve.Count == (scenario == 0 ? 1 : scenario == 1 ? 3 : 0),
                            "Authoritative target deaths: " + scenario);
                        Check(!Object.FindObjectsByType<RawImage>().Any(x => x.name == "BiteJaw" || x.name == "BiteEcho"),
                            "Normal completion cleans graphics");
                        Next(runner);
                        break;
                    case 3:
                        if (EditorApplication.timeSinceStartup - resetAt < 0.8) return;
                        Check(!runner.IsBusy && runner.Model.State.Host.DrawHand.Count == 0,
                            "No stale snapshot after reset");
                        Check(Object.FindAnyObjectByType<BiteEffectView>() == null
                            && !Object.FindObjectsByType<RawImage>().Any(x => x.name == "BiteJaw" || x.name == "BiteEcho"),
                            "Reset cleans view and echo");
                        Next(runner);
                        break;
                }
            }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        private static void Setup(AbilitySandboxRunner runner)
        {
            if (scenario == 0)
            {
                var button = GameObject.Find("SandboxTestMaTranh")?.GetComponent<Button>();
                Check(button != null && button.interactable, "Ma Tranh preview shortcut exists");
                button.onClick.Invoke();
                Check(runner.Model.Count("MaTranh", false) == 1
                    && runner.Model.State.RoundHistory.HasDeathThisRound(KLTN.Game.Domain.SeatId.Host),
                    "Preview shortcut supplies card and death condition");
                return;
            }
            var state = runner.Model.State;
            // Fixture history only; the actual Ma Tranh summon and target selection
            // still run through the normal rules and authoritative DTO pipeline.
            if (scenario != 1)
            {
                var ally = state.Host.Reserve[0];
                var definition = CardDefinitionMapper.Create(Resources.Load<Card>("Cards/" + ally.DefinitionId));
                ally.Kill();
                state.RoundHistory.RecordDeath(ally, ally.GetDamage(definition), ally.GetMaximumHealth(definition));
                state.Host.Reserve.Remove(ally);
                ally.MoveTo(KLTN.Game.Domain.CardZone.Graveyard);
                state.Host.Graveyard.Add(ally);
            }
            if (scenario == 2) state.Guest.Reserve.RemoveRange(1, state.Guest.Reserve.Count - 1);
            if (scenario == 3) state.Guest.Reserve.Clear();
            int[] reserve = AbilitySandboxModel.ReserveIds.Select(id => runner.Model.Count(id, true)).ToArray();
            int[] hand = AbilitySandboxModel.HandIds.Select(id => id == "MaTranh" ? 1 : 0).ToArray();
            Check(runner.ApplyCounts(reserve, hand), "Fixture counts");
        }

        private static void Observe()
        {
            RawImage jaw = Object.FindObjectsByType<RawImage>().FirstOrDefault(x => x.name == "BiteJaw");
            if (jaw == null) return;
            bites.Add(jaw);
            Check(!jaw.raycastTarget, "Jaw does not intercept input");
            Check(!Object.FindObjectsByType<Transform>().Any(x => x.name == "AbilityFlameTrail"),
                "Ma Tranh does not launch a projectile");
            if (scenario == 0 && !capturedOpen)
            {
                capturedOpen = true;
                if (Application.isBatchMode) ThanTrungLightningValidation.Capture("ma-tranh-open");
            }
            RawImage echo = jaw.transform.parent.Find("BiteEcho").GetComponent<RawImage>();
            if (!echo.enabled) return;
            var p = Resources.Load<BiteEffectProfile>("VFX/MaTranhBite");
            Check(jaw.uvRect == echo.uvRect && echo.rectTransform.localScale.x >= 1f
                && echo.color.a <= p.echoOpacity, "Echo is enlarged final-frame copy");
            var audio = Object.FindAnyObjectByType<BiteEffectView>().GetComponent<AudioSource>();
            Check(audio != null && Mathf.Approximately(audio.volume, AudioVolumeSettings.SfxVolume * p.sfxVolume),
                "Bite uses SFX gain");
            if (echo.rectTransform.localScale.x < 1.15f || !echoes.Add(echo)) return;
            if (scenario == 0 && Application.isBatchMode)
                ThanTrungLightningValidation.Capture("ma-tranh-echo-" + echoes.Count);
        }

        private static void Next(AbilitySandboxRunner runner)
        {
            runner.ResetScene();
            bites.Clear();
            echoes.Clear();
            scenario++;
            step = 0;
            if (scenario < 6) return;
            Check(errors == 0, "Runtime errors: " + errors);
            Finish(true, "Two/one/zero targets, unmet condition, no projectile, frame-9 echo, SFX gain, "
                + "reset during opening and echo; model regression passed; zero runtime errors.");
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
            File.WriteAllText(Folder + "/result.txt", (success ? "PASS: " : "FAIL: ") + message);
            Debug.Log("[MaTranhBite] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
#endif
