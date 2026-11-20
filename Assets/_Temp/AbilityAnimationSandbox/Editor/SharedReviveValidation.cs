#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KLTN.Sandbox.Editor
{
    /// <summary>Exercises shared spirit effects through real rules and both projections.</summary>
    [InitializeOnLoad]
    public static class SharedReviveValidation
    {
        private const string Key = "SharedRevive.Validation";
        private static int scenario, step;
        private static ulong sourceId, revision;
        private static bool sawSpirit;
        private static double deadline;
        private static SeatId viewer;

        static SharedReviveValidation()
        {
            if (SessionState.GetBool(Key, false)) Attach();
        }

        /// <summary>Runs the offline sandbox without modifying scene assets.</summary>
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(AbilitySandboxBuilder.ScenePath);
            SessionState.SetBool(Key, true);
            Attach();
            EditorApplication.isPlaying = true;
        }

        private static void Attach()
        {
            scenario = step = 0;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Timeout");
                var runner = Object.FindAnyObjectByType<AbilitySandboxRunner>();
                if (runner?.Model == null) return;
                bool copy = scenario % 4 < 2;
                bool reset = scenario >= 4;
                if (step == 0)
                {
                    runner.PrepareMaTranhPreview();
                    var state = runner.Model.State;
                    state.Host.DrawHand.Clear();
                    string id = copy ? "MaCo" : "LinhMieu";
                    sourceId = state.AllocateCardInstanceId();
                    state.Host.DrawHand.Add(new CardInstance(sourceId, id,
                        SeatId.Host, CardZone.DrawHand,
                        runner.Model.Definition(id).BaseHealth));
                    viewer = scenario % 2 == 0 ? SeatId.Host : SeatId.Guest;
                    MatchUpdateDto before = runner.Model.BuildUpdate(viewer);
                    // Fixture setup is a baseline hand, not a gameplay draw. Round zero
                    // seeds known hand identities without starting deck-flight coroutines.
                    before.snapshot.roundNumber = 0;
                    revision = before.snapshot.revision;
                    MatchUpdateInboxRegistry.Current.Enqueue(before);
                    sawSpirit = false;
                    step = 1;
                    return;
                }
                if (step == 1)
                {
                    if (MatchProjectionRegistry.Current.Current?.revision != revision) return;
                    Check(runner.Model.Summon(sourceId).Accepted, "Summon accepted");
                    MatchUpdateDto after = runner.Model.BuildUpdate(viewer);
                    Check(after.hasAbilityResolution, "Successful summon has an effect");
                    var effect = after.abilityResolution.events.Single(e =>
                        e.abilityId == (copy ? "ma_co_summon" : "linh_mieu_summon"));
                    if (copy)
                    {
                        Check(effect.targetBefore == null, "Copy has no old target");
                        Check((effect.targetAfter == null) == (viewer == SeatId.Guest),
                            "Opponent never receives copied identity");
                        Check(effect.targetZoneAfter == (int)CardZone.DrawHand,
                            "Copy retains hand destination");
                    }
                    revision = after.snapshot.revision;
                    MatchUpdateInboxRegistry.Current.Enqueue(after);
                    step = 2;
                    return;
                }
                var spirit = Object.FindAnyObjectByType<SpiritSpellView>();
                if (spirit != null && spirit.Phase != null && spirit.Phase != "Idle")
                {
                    sawSpirit = true;
                    Check(runner.board.TryGetFaceUpVisual(sourceId.ToString(), out var source),
                        "Source exists while spirit runs");
                    Check(source.transform.parent == runner.board.AbilitySelectionSourceRoot
                        && source.CardRect.localPosition.sqrMagnitude < 1,
                        "Spirit runs only after source reaches center");
                    if (reset)
                    {
                        runner.ResetScene();
                        Check(spirit.Phase == "Idle", "Reset clears spirit");
                        Check(!spirit.GetComponents<AudioSource>().Any(a => a.isPlaying),
                            "Reset stops spirit audio");
                        Advance();
                        return;
                    }
                }
                if (MatchProjectionRegistry.Current.Current?.revision != revision) return;
                Check(sawSpirit, "Observed shared spirit animation");
                Check(runner.board.TryGetFaceUpVisual(sourceId.ToString(), out var final),
                    "Final source exists");
                Check(final.transform.parent == Root(runner.board,
                    viewer == SeatId.Host ? "selfReserveRoot" : "opponentReserveRoot"),
                    "Source returned to Reserve");
                Check(spirit.Phase == "Idle", "Ribbon cleaned up");
                var stateAfter = runner.Model.State;
                Check(stateAfter.Host.DrawHand.Count == (copy ? 1 : 0), "Correct hand count");
                Check(stateAfter.Host.Graveyard.Count == (copy ? 1 : 0),
                    "Copy preserves graveyard; revive consumes it");
                var activeDraws = (System.Collections.ICollection)typeof(MatchBoardPresenter)
                    .GetField("activeDrawAnimationObjects", BindingFlags.Instance
                        | BindingFlags.NonPublic).GetValue(runner.board);
                Check(activeDraws.Count == 0, "No duplicate draw animation");
                Advance();
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static void Advance()
        {
            scenario++;
            step = 0;
            if (scenario == 8) Finish(true,
                "Both cards, both viewers, hidden copy, staging, no duplicate draw and reset.");
        }

        private static RectTransform Root(MatchBoardPresenter board, string name)
        {
            return (RectTransform)typeof(MatchBoardPresenter).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(board);
        }

        private static void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }

        private static void Finish(bool success, string message)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Directory.CreateDirectory("Logs/SharedRevive");
            File.WriteAllText("Logs/SharedRevive/result.txt",
                (success ? "PASS: " : "FAIL: ") + scenario + ": " + message);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
