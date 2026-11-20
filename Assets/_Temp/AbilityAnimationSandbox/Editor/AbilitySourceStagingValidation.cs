#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// <summary>
    /// Drives the real staged summon, rules, viewer projection, inbox and director
    /// for both perspectives. Requires Unity Play Mode; no presentation doubles.
    /// </summary>
    [InitializeOnLoad]
    public static class AbilitySourceStagingValidation
    {
        #region Validation Lifecycle

        private const string Key = "AbilitySourceStaging.Validation";
        private const string Folder = "Logs/AbilitySync";
        private static int scenario, step, errors, expectedBites;
        private static double deadline;
        private static ulong sourceId, revision;
        private static SeatId viewer;
        private static bool sawCenter;
        private static bool contractChecked;
        private static bool opponentContractChecked;
        private static readonly HashSet<RawImage> jaws = new HashSet<RawImage>();

        static AbilitySourceStagingValidation()
        {
            if (SessionState.GetBool(Key, false)) Attach();
        }

        /// <summary>Batch entry; opens only the offline sandbox and exits on completion.</summary>
        public static void RunBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(AbilitySandboxBuilder.ScenePath);
                SessionState.SetBool(Key, true);
                Attach();
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        [MenuItem("Tools/KLTN/Temporary/Validate Ability Source Staging (Play Mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying
                || Object.FindAnyObjectByType<AbilitySandboxRunner>() == null)
            {
                Debug.LogWarning("Enter Play Mode in AbilityAnimationSandbox first.");
                return;
            }
            Attach();
        }

        [MenuItem("Tools/KLTN/Temporary/Trace Ability Presentation")]
        public static void ToggleTrace()
        {
            AbilityPresentationTrace.Enabled = !AbilityPresentationTrace.Enabled;
            Debug.Log("[AbilityTiming] tracing=" + AbilityPresentationTrace.Enabled);
        }

        [MenuItem("Tools/KLTN/Temporary/Trace Ability Presentation", true)]
        private static bool ValidateTraceMenu()
        {
            Menu.SetChecked("Tools/KLTN/Temporary/Trace Ability Presentation",
                AbilityPresentationTrace.Enabled);
            return true;
        }

        private static void Attach()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            scenario = step = errors = 0;
            contractChecked = false;
            opponentContractChecked = false;
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error
                || type == LogType.Assert) errors++;
        }

        private static void Finish(bool success, string message)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/source-staging-result.txt",
                (success ? "PASS: " : "FAIL: ") + message);
            Debug.Log("[AbilitySourceStaging] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        #endregion

        #region Real Pipeline Scenarios

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline,
                    "Timed out at scenario " + scenario + ", step " + step);
                AbilitySandboxRunner runner = Object.FindAnyObjectByType<AbilitySandboxRunner>();
                if (runner?.Model == null) return;
                if (step == 0)
                {
                    runner.PrepareMaTranhPreview();
                    viewer = scenario % 2 == 0 ? SeatId.Host : SeatId.Guest;
                    int enemyCount = scenario < 2 ? 3 : scenario < 4 ? 1 : 0;
                    expectedBites = Math.Min(2, enemyCount);
                    var targets = runner.Model.State.Guest.Reserve;
                    if (targets.Count > enemyCount)
                        targets.RemoveRange(enemyCount, targets.Count - enemyCount);
                    sourceId = runner.Model.State.Host.DrawHand
                        .Single(card => card.DefinitionId == "MaTranh").InstanceId;
                    MatchUpdateDto before = runner.Model.BuildUpdate(viewer);
                    revision = before.snapshot.revision;
                    MatchUpdateInboxRegistry.Current.Enqueue(before);
                    jaws.Clear();
                    sawCenter = false;
                    step = 1;
                    return;
                }
                if (step == 1)
                {
                    if (MatchProjectionRegistry.Current.Current?.revision != revision) return;
                    if (viewer == SeatId.Host)
                    {
                        Check(runner.board.TryGetFaceUpVisual(sourceId.ToString(),
                            out NetworkCardVisual source), "Local hand source exists");
                        Check(runner.controls.TryStageSummon(
                            source.GetComponent<DraggableHandCard>(),
                            Root(runner.board, "selfReserveRoot")), "Stage real summon");
                        Canvas.ForceUpdateCanvases();
                        Check(source.transform.parent != runner.board.AbilitySelectionSourceRoot,
                            "Local preview remains in Reserve before commit");
                        if (!contractChecked)
                        {
                            ValidatePoseContract(runner, source);
                            contractChecked = true;
                            // Reset the contract probe before testing the actual pipeline.
                            runner.PrepareMaTranhPreview();
                            step = 0;
                            return;
                        }
                    }
                    if (viewer == SeatId.Guest && !opponentContractChecked)
                    {
                        ValidateOpponentRevealPose(runner);
                        opponentContractChecked = true;
                        runner.PrepareMaTranhPreview();
                        step = 0;
                        return;
                    }
                    Check(runner.Model.Summon(sourceId).Accepted, "Authoritative summon accepted");
                    MatchUpdateDto after = runner.Model.BuildUpdate(viewer);
                    revision = after.snapshot.revision;
                    MatchUpdateInboxRegistry.Current.Enqueue(after);
                    step = 2;
                    return;
                }

                if (runner.board.TryGetFaceUpVisual(sourceId.ToString(),
                    out NetworkCardVisual activeSource))
                {
                    bool centered = activeSource.transform.parent
                        == runner.board.AbilitySelectionSourceRoot;
                    sawCenter |= centered;
                    foreach (RawImage jaw in Object.FindObjectsByType<RawImage>()
                        .Where(image => image.name == "BiteJaw"))
                    {
                        jaws.Add(jaw);
                        Check(centered, "Both viewers center source before Bite VFX");
                        Check(activeSource.CardRect.localPosition.sqrMagnitude < 1f,
                            "VFX starts only after source reaches center");
                    }
                }
                if (MatchProjectionRegistry.Current.Current?.revision != revision) return;
                Check(jaws.Count == expectedBites, "Observed every authoritative bite");
                Check(expectedBites == 0 || sawCenter, "Source used center staging");
                Check(runner.board.TryGetFaceUpVisual(sourceId.ToString(),
                    out NetworkCardVisual finalSource), "Final source exists");
                Check(finalSource.transform.parent == Root(runner.board,
                    viewer == SeatId.Host ? "selfReserveRoot" : "opponentReserveRoot"),
                    "Final snapshot docks source in authoritative Reserve");
                Check(!Object.FindObjectsByType<RawImage>()
                    .Any(image => image.name == "BiteJaw" || image.name == "BiteEcho"),
                    "No bite graphics remain after projection");
                scenario++;
                if (scenario >= 6)
                {
                    Check(errors == 0, "No runtime errors");
                    Finish(true, "Exact pose, idempotent staging, unchanged target parent, "
                        + "both viewers and 3/1/0 targets passed.");
                }
                else step = 0;
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static void ValidatePoseContract(AbilitySandboxRunner runner,
            NetworkCardVisual source)
        {
            RectTransform rect = source.CardRect;
            // A rotated and scaled actual pose catches hypothetical-slot restarts.
            rect.localRotation = Quaternion.Euler(0, 0, 7f);
            rect.localScale = new Vector3(0.91f, 0.91f, 1f);
            Vector3 center = rect.TransformPoint(rect.rect.center);
            Quaternion rotation = rect.rotation;
            float width = rect.TransformVector(Vector3.right * rect.rect.width).magnitude;
            CardViewDto dto = MatchProjectionRegistry.Current.Current.self.hand
                .Single(card => card.instanceId == sourceId.ToString());
            NetworkCardVisual prepared = runner.board.PrepareAbilitySource(dto,
                (int)SeatId.Host, (int)SeatId.Host, (int)CardZone.Reserve,
                true, out bool needsMove);
            Check(prepared == source && needsMove, "Existing source requests center movement");
            Check(rect.parent == runner.board.AbilitySelectionSourceRoot,
                "Existing Reserve source enters center root");
            Check(Vector3.Distance(center, rect.TransformPoint(rect.rect.center)) < 0.1f,
                "Preserves actual world center before movement");
            Check(Quaternion.Angle(rotation, rect.rotation) < 0.1f,
                "Preserves actual world rotation before movement");
            Check(Mathf.Abs(width - rect.TransformVector(
                Vector3.right * rect.rect.width).magnitude) < 0.1f,
                "Preserves actual world size before movement");
            runner.board.PrepareAbilitySource(dto, 0, 0, (int)CardZone.Reserve,
                true, out needsMove);
            Check(!needsMove, "Already-centered source is not staged twice");
            CardViewDto target = MatchProjectionRegistry.Current.Current.self.reserve[0];
            Check(runner.board.TryGetFaceUpVisual(target.instanceId,
                out NetworkCardVisual targetView), "Target exists");
            Transform targetParent = targetView.transform.parent;
            runner.board.PrepareAbilitySource(target, 0, 0, (int)CardZone.Reserve);
            Check(targetView.transform.parent == targetParent, "Target keeps its zone parent");
        }

        private static void ValidateOpponentRevealPose(AbilitySandboxRunner runner)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform hand = Root(runner.board, "opponentHandRoot");
            int originalBacks = hand.Cast<Transform>()
                .Count(child => child.gameObject.activeSelf);
            RectTransform back = (RectTransform)hand.GetChild(hand.childCount - 1);
            back.localRotation = Quaternion.Euler(0, 0, 11f);
            back.localScale = new Vector3(0.83f, 0.83f, 1f);
            Vector3 center = back.TransformPoint(back.rect.center);
            Quaternion rotation = back.rotation;
            float width = back.TransformVector(Vector3.right * back.rect.width).magnitude;
            float height = back.TransformVector(Vector3.up * back.rect.height).magnitude;
            CardViewDto card = runner.Model.BuildUpdate(SeatId.Host).snapshot.self.hand
                .Single(item => item.instanceId == sourceId.ToString());
            NetworkCardVisual source = runner.board.PrepareAbilitySource(card,
                (int)SeatId.Host, (int)SeatId.Guest, (int)CardZone.Reserve,
                true, out bool needsMove);
            Check(needsMove, "Opponent reveal requests movement from Draw to center");
            RectTransform rect = source.CardRect;
            Check(Vector3.Distance(center, rect.TransformPoint(rect.rect.center)) < 0.1f,
                "Opponent reveal starts at the consumed Draw card, not Reserve");
            Check(Quaternion.Angle(rotation, rect.rotation) < 0.1f,
                "Opponent reveal preserves Draw rotation");
            Check(Mathf.Abs(width - rect.TransformVector(
                Vector3.right * rect.rect.width).magnitude) < 0.1f,
                "Opponent reveal preserves Draw width");
            Check(Mathf.Abs(height - rect.TransformVector(
                Vector3.up * rect.rect.height).magnitude) < 0.1f,
                "Opponent reveal preserves Draw height");
            Check(!back.gameObject.activeSelf, "Consumed back is hidden immediately");
            int activeBacks = hand.Cast<Transform>()
                .Count(child => child.gameObject.activeSelf);
            Check(activeBacks == originalBacks - 1, "Reveal consumes exactly one back");
            NetworkCardVisual repeated = runner.board.PrepareAbilitySource(card,
                (int)SeatId.Host, (int)SeatId.Guest, (int)CardZone.Reserve,
                true, out needsMove);
            Check(repeated == source && !needsMove,
                "Opponent reveal is staged only once");
            Check(hand.Cast<Transform>().Count(child => child.gameObject.activeSelf)
                == activeBacks, "Repeated staging does not consume another back");

            // Re-render with no hand backs to exercise the missing-visual fallback.
            MatchSnapshotDto snapshot = runner.Model.BuildUpdate(SeatId.Guest).snapshot;
            snapshot.opponent.handCount = 0;
            MatchProjectionRegistry.Current.Apply(snapshot);
            Canvas.ForceUpdateCanvases();
            source = runner.board.PrepareAbilitySource(card,
                (int)SeatId.Host, (int)SeatId.Guest, (int)CardZone.Reserve,
                true, out needsMove);
            Check(needsMove && Vector3.Distance(
                source.CardRect.TransformPoint(source.CardRect.rect.center),
                hand.TransformPoint(hand.rect.center)) < 0.1f,
                "Missing hand back falls back to opponent Draw root");

            CardViewDto reserveCard = snapshot.opponent.reserve[0];
            Check(runner.board.TryGetFaceUpVisual(reserveCard.instanceId,
                out NetworkCardVisual reserveSource), "Existing opponent source exists");
            Vector3 reserveCenter = reserveSource.CardRect.TransformPoint(
                reserveSource.CardRect.rect.center);
            runner.board.PrepareAbilitySource(reserveCard,
                (int)SeatId.Host, (int)SeatId.Guest, (int)CardZone.Reserve,
                true, out needsMove);
            Check(needsMove && Vector3.Distance(reserveCenter,
                reserveSource.CardRect.TransformPoint(reserveSource.CardRect.rect.center))
                < 0.1f, "Existing opponent Reserve source keeps its visible pose");
        }

        private static RectTransform Root(MatchBoardPresenter board, string name)
        {
            return (RectTransform)typeof(MatchBoardPresenter)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(board);
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        #endregion
    }
}
#endif
