#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;
namespace KLTN.Sandbox.Editor
{
    public static class MaCangSungBatsValidation
    {
        private static int step, scenario;
        private static bool sawBite, sawReturn;
        private static double deadline, started;
        [MenuItem("Tools/KLTN/Temporary/Validate Ma Cang Sung Bats (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || Object.FindAnyObjectByType<AbilitySandboxRunner>() == null)
            { Debug.LogWarning("Enter Play Mode in AbilityAnimationSandbox first."); return; }
            EditorApplication.update -= Tick;
            step = scenario = 0; deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += Tick;
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Tick()
        {
            try
            {
                Check(EditorApplication.isPlaying && EditorApplication.timeSinceStartup < deadline, "Interrupted/timeout " + step);
                var r = Object.FindAnyObjectByType<AbilitySandboxRunner>();
                if (step == 0)
                {
                    r.ResetScene(); sawBite = sawReturn = false; started = 0;
                    if (scenario != 1) r.Model.State.Host.ApplyNexusDamage(3);
                    step++;
                }
                else if (step == 1 && !r.IsBusy)
                {
                    Check(r.ApplyCounts(new[] {2,0,0,0,0,0,0,0}, new[] {0,0,0,0,1,1}), "Fixture"); step++;
                }
                else if (step == 2 && !r.IsBusy)
                {
                    r.Summon(r.Model.State.Host.DrawHand.Single(c => c.DefinitionId == "MaCangSung").InstanceId.ToString()); step++;
                }
                else if (step == 3 && !r.IsBusy)
                {
                    r.Summon(r.Model.State.Host.DrawHand.Single(c => c.DefinitionId == "MaDoi").InstanceId.ToString()); step++;
                }
                else if (step == 4 && !r.IsBusy)
                {
                    r.Select(r.Model.State.PendingSelection.RequestId.ToString(),
                        new[] { r.Model.State.Host.Reserve.First(c => c.DefinitionId == "MaLon").InstanceId.ToString() }, Array.Empty<string>());
                    step++;
                }
                else if (step == 5)
                {
                    var view = Object.FindAnyObjectByType<BatSwarmView>();
                    var bats = Object.FindObjectsByType<RawImage>().Where(x => x.name.StartsWith("SwarmBat")).ToArray();
                    if (bats.Length > 0)
                    {
                        if (started == 0) started = EditorApplication.timeSinceStartup;
                        Check(bats.Length == 12 && bats.All(x => !x.raycastTarget), "12 non-interactive bats");
                        var labels = Object.FindObjectsByType<TMP_Text>();
                        var minus = labels.Single(x => x.name == "SwarmDamage");
                        var plus = labels.Single(x => x.name == "SwarmHeal");
                        Check(!Object.FindObjectsByType<Transform>().Any(x => x.name == "AbilityFlameTrail"), "No generic nexus projectile");
                        if (view.Phase == "Biting")
                        {
                            sawBite = true; Check(!minus.enabled && !plus.enabled, "No popup before dispersal");
                        }
                        if (view.Phase == "Returning")
                        {
                            sawReturn = true;
                            Check(minus.text == "-1", "Damage amount");
                            if (scenario == 1) Check(!plus.enabled, "No false heal popup at full health");
                        }
                        if ((scenario == 2 && sawBite) || (scenario == 3 && sawReturn))
                        { r.ResetScene(); step = 6; }
                    }
                    if (step == 5 && !r.IsBusy)
                    {
                        Check(sawBite && sawReturn, "Complete swarm");
                        Check(EditorApplication.timeSinceStartup-started < 2.8, "Approximately two seconds");
                        Check(r.Model.State.Guest.NexusHealth == 19 && r.Model.State.Host.NexusHealth == (scenario == 1 ? 20 : 18), "Authoritative drain/heal");
                        step = 6;
                    }
                }
                else if (step == 6 && !r.IsBusy)
                {
                    Check(!Object.FindObjectsByType<RawImage>().Any(x => x.name.StartsWith("SwarmBat")), "Bat cleanup");
                    var view = Object.FindAnyObjectByType<BatSwarmView>();
                    Check(view == null || view.GetComponents<AudioSource>().All(x => !x.isPlaying), "Audio cleanup");
                    scenario++; step = 0;
                    if (scenario == 4) Finish(true, "12 bats; biting/return; delayed damage; real heal and capped heal; two-second duration; reset during bite/return.");
                }
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        private static void Finish(bool pass, string message)
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory("Logs/MaCangSungBats");
            File.WriteAllText("Logs/MaCangSungBats/result.txt", (pass ? "PASS: " : "FAIL: ") + message);
            Debug.Log("[MaCangSungBats] " + (pass ? "PASS: " : "FAIL: ") + message);
        }
    }
}
#endif
