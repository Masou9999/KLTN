#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using CMCMProductions;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Sandbox.Editor
{
    /// <summary>
    /// Checks fixture invariants and exercises the real Play Mode UI/animation.
    /// </summary>
    [InitializeOnLoad]
    public static class AbilitySandboxValidation
    {
        #region Batch Lifecycle

        private const string Key = "AbilitySandbox.Validation";
        private static double deadline;
        private static int step;
        private static int frame;
        private static int errorCount;
        private static double resumeAfter;

        static AbilitySandboxValidation()
        {
            if (SessionState.GetBool(Key, false))
            {
                deadline = EditorApplication.timeSinceStartup + 120;
                EditorApplication.update += Tick;
                Application.logMessageReceived += OnLog;
            }
        }

        public static void Run()
        {
            ValidateModel();
            EditorSceneManager.OpenScene(AbilitySandboxBuilder.ScenePath);
            AbilitySandboxBuilder.ValidateScene();
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error
                || type == LogType.Assert)
            {
                errorCount++;
            }
        }

        private static void Finish(bool success, string detail)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory("Logs/AbilitySandbox");
            File.WriteAllText("Logs/AbilitySandbox/result.txt",
                (success ? "PASS: " : "FAIL: ") + detail);
            Debug.Log("[AbilitySandbox] " + detail);
            EditorApplication.Exit(success ? 0 : 1);
        }

        #endregion

        #region Model Regression Checks

        private static AbilitySandboxModel Create()
        {
            return new AbilitySandboxModel(Resources.LoadAll<Card>("Cards")
                .Select(CardDefinitionMapper.Create).ToDictionary(card => card.Id));
        }

        private static void Check(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static int[] Counts(AbilitySandboxModel model, bool reserve)
        {
            return (reserve
                    ? AbilitySandboxModel.ReserveIds : AbilitySandboxModel.HandIds)
                .Select(id => model.Count(id, reserve)).ToArray();
        }

        private static void AddHand(AbilitySandboxModel model, string id)
        {
            int[] counts = Counts(model, false);
            counts[Array.IndexOf(AbilitySandboxModel.HandIds, id)]++;
            Check(model.ApplyCounts(Counts(model, true), counts), "Add hand " + id);
        }

        private static void Play(AbilitySandboxModel model, string id)
        {
            AddHand(model, id);
            CardInstance source = model.State.Host.DrawHand
                .Last(c => c.DefinitionId == id);
            Check(model.Summon(source.InstanceId).Accepted, "Summon " + id);
            if (model.State.PendingSelection != null)
            {
                ulong ally = model.State.Host.Reserve
                    .First(c => c.DefinitionId == "MaLon")
                    .InstanceId;
                ulong enemy = model.State.Guest.Reserve
                    .First(c => c.DefinitionId == "MaLon")
                    .InstanceId;
                Check(model.Select(model.State.PendingSelection.RequestId,
                    new[] { ally }, id == "ThanTrung"
                        ? new[] { enemy } : Array.Empty<ulong>())
                    .Accepted, "Targets " + id);
            }
            Check(model.State.Host.Mana == 10 && model.State.RoundNumber == 1
                && model.State.ActiveSeat == SeatId.Host, "Unlimited turn / 10 mana");
        }

        [MenuItem("Tools/KLTN/Temporary/Validate Sandbox Model")]
        public static void ValidateModel()
        {
            AbilitySandboxModel model = Create();
            Check(model.State.Host.Reserve.Count == 3
                && model.State.Guest.Reserve.Count == 3
                && model.State.Host.DrawHand.Count == 0
                && model.State.Guest.DrawHand.Count == 0, "Initial fixture");
            Check(!model.ApplyCounts(new[] { 4, 1, 0, 0, 0, 0, 0 }, new int[5]),
                "Reject reserve overflow");
            Check(!model.ApplyCounts(Counts(model, true), new[] { 11, 0, 0, 0, 0 }),
                "Reject hand overflow");
            Check(!model.ApplyCounts(Counts(model, true), new[] { -1, 0, 0, 0, 0 }),
                "Reject negative counts");
            Check(model.ApplyCounts(new[] { 0, 1, 0, 0, 0, 0, 0 }, new int[5]),
                "Remove fixture cards");
            Check(model.State.Host.Graveyard.Count == 0
                && model.State.Guest.NexusHealth == 20
                && !model.BuildUpdate().hasAbilityResolution, "Removal is not death");

            foreach (string id in new[] { "MaDoi", "ThanTrung", "ThienLinhCai", "OngKe" })
            {
                model = Create();
                Play(model, id);
                MatchUpdateDto update = model.BuildUpdate();
                Check(update.hasAbilityResolution
                    && update.abilityResolution.events.Length > 0,
                    "Real ability DTO: " + id);
                Check(model.State.Guest.NexusHealth == 19, "Vong Nhi damage: " + id);
                if (id == "ThienLinhCai")
                {
                    Check(model.State.Host.Reserve.Count == 4
                        && model.State.Host.Graveyard.Count == 0, "Revive target");
                }
                if (id == "OngKe")
                {
                    Check(model.State.Host.DrawHand.Count == 2, "Draw two cards");
                }
                if (id == "ThanTrung")
                {
                    Check(model.State.Guest.Reserve.Count == 2
                        && model.State.Host.NexusHealth == 19, "Two-sided kill chain");
                }
            }
            model = Create();
            Play(model, "MaDoi");
            model.BuildUpdate();
            Play(model, "MaTranh");
            Check(model.State.Guest.Reserve.Count == 1, "Ma Tranh same-round condition");
            model = Create();
            Play(model, "MaTranh");
            Check(model.State.Guest.Reserve.Count == 3, "Ma Tranh no death condition");
            model = Create();
            AddHand(model, "ThanTrung");
            ulong sourceId = model.State.Host.DrawHand[0].InstanceId;
            Check(model.Summon(sourceId).Accepted, "Open targeted selection");
            Check(!model.Select(model.State.PendingSelection.RequestId,
                Array.Empty<ulong>(), Array.Empty<ulong>()).Accepted,
                "Reject incomplete targets");
            Check(model.Cancel(model.State.PendingSelection.RequestId).Accepted
                && model.State.Host.DrawHand[0].InstanceId == sourceId
                && model.State.Host.Mana == 10, "Cancel without spending/moving");
            ulong previousRevision = model.BuildUpdate().snapshot.revision;
            model.Reset();
            Check(model.BuildUpdate().snapshot.revision > previousRevision
                && model.State.Host.Reserve.Count == 3
                && model.State.Host.DrawHand.Count == 0, "Reset and revision order");
            Debug.Log("[AbilitySandbox] Model validation passed: caps, removal, all six "
                + "abilities, target cancellation, 10 mana and reset.");
        }

        #endregion

        #region Play Mode Integration

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                {
                    throw new TimeoutException(
                        "Play validation timed out at step " + step);
                }
                if (!EditorApplication.isPlaying || ++frame % 5 != 0
                    || EditorApplication.timeSinceStartup < resumeAfter)
                {
                    return;
                }
                var runner = Object.FindAnyObjectByType<AbilitySandboxRunner>();
                if (runner == null || runner.IsBusy && step != 5)
                {
                    return;
                }
                switch (step)
                {
                    case 0:
                        Check(Object.FindAnyObjectByType<AuthManager>() == null
                            && Object.FindAnyObjectByType<Unity.Netcode.NetworkManager>()
                                == null, "No login or network bootstrap");
                        Check(MatchProjectionRegistry.Current.Current.self.mana == 10,
                            "Initial HUD mana");
                        Capture("initial");
                        runner.addButton.onClick.Invoke();
                        Capture("modal");
                        Row(runner, "MaLon", true).minus.onClick.Invoke();
                        Check(Row(runner, "MaLon", true).value == 1,
                            "Minus removes one from draft");
                        Row(runner, "MaDoi", false).plus.onClick.Invoke();
                        runner.modal.cancelButton.onClick.Invoke();
                        Check(runner.Model.State.Host.DrawHand.Count == 0
                            && runner.Model.Count("MaLon", true) == 2, "Cancel UI draft");
                        runner.addButton.onClick.Invoke();
                        Row(runner, "MaDoi", false).plus.onClick.Invoke();
                        runner.modal.confirmButton.onClick.Invoke();
                        break;
                    case 1:
                        StageAndSubmit(runner);
                        Check(runner.Model.State.PendingSelection != null,
                            "Normal drag opens target selection");
                        string request = runner.Model.State.PendingSelection
                            .RequestId.ToString();
                        Check(runner.controls.CancelPendingAbilitySelection(request),
                            "Normal selection cancel");
                        break;
                    case 2:
                        StageAndSubmit(runner);
                        SelectAlly(runner);
                        resumeAfter = EditorApplication.timeSinceStartup + 0.2;
                        break;
                    case 3:
                        Check(runner.Model.State.Guest.NexusHealth == 19
                            && MatchProjectionRegistry.Current.Current.self.mana == 10,
                            "Animation applied final snapshot");
                        Capture("after-ability");
                        runner.resetButton.onClick.Invoke();
                        runner.OpenModal();
                        Row(runner, "ThienLinhCai", false).plus.onClick.Invoke();
                        runner.modal.Confirm();
                        break;
                    case 4:
                        StageAndSubmit(runner);
                        SelectAlly(runner);
                        resumeAfter = EditorApplication.timeSinceStartup + 0.9;
                        break;
                    case 5:
                        Capture("projectile");
                        runner.resetButton.onClick.Invoke();
                        Check(!Object.FindObjectsByType<ParticleSystem>()
                            .Any(particle => particle.isPlaying),
                            "Reset clears pooled particles");
                        Check(runner.Model.State.Host.Reserve.Count == 3
                            && runner.Model.State.Host.DrawHand.Count == 0
                            && runner.Model.State.Guest.NexusHealth == 20,
                            "Reset during animation");
                        resumeAfter = EditorApplication.timeSinceStartup + 2;
                        break;
                    case 6:
                        Check(!runner.IsBusy
                            && MatchProjectionRegistry.Current.Current.self
                                .handCount == 0,
                            "No stale snapshot after reset");
                        Check(!Object.FindObjectsByType<Transform>().Any(t =>
                            t.name == "AbilityImpactFlash"
                                || t.name == "ReviveLightningFlash"),
                            "No leftover VFX");
                        Check(errorCount == 0, "Runtime logged errors: " + errorCount);
                        Finish(true, "Model and Play Mode checks passed: "
                            + "modal +/−, cancel, "
                            + "confirm, normal drag, target cancel, real projectile, "
                            + "10 mana, reset during animation; zero runtime errors.");
                        return;
                }
                step++;
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static AbilitySandboxModal.Row Row(AbilitySandboxRunner runner,
            string id, bool reserve)
        {
            return runner.modal.rows.Single(row =>
                row.id == id && row.reserve == reserve);
        }

        private static void StageAndSubmit(AbilitySandboxRunner runner)
        {
            CardInstance card = runner.Model.State.Host.DrawHand[0];
            Check(runner.board.TryGetFaceUpVisual(
                card.InstanceId.ToString(), out var view),
                "Hand visual exists");
            var drop = Object.FindAnyObjectByType<ReserveDropZone>();
            var pointer = new PointerEventData(EventSystem.current)
            {
                pointerDrag = view.gameObject,
                position = new Vector2(Screen.width / 2f, Screen.height / 2f)
            };
            var draggable = view.GetComponent<DraggableHandCard>();
            draggable.OnBeginDrag(pointer);
            drop.OnDrop(pointer);
            draggable.OnEndDrag(pointer);
            Check(view.GetComponent<DraggableHandCard>().IsPending, "Drag accepted");
            runner.controls.EditorSubmitOverride();
        }

        private static void SelectAlly(AbilitySandboxRunner runner)
        {
            CardInstance ally = runner.Model.State.Host.Reserve.First(c =>
                c.DefinitionId == "MaLon");
            Check(runner.board.TryGetFaceUpVisual(
                ally.InstanceId.ToString(), out var view),
                "Ally visual exists");
            view.OnPointerClick(new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            });
            runner.controls.EditorSubmitOverride();
        }

        private static void Capture(string name)
        {
            KLTN.Game.Editor.MenuPolishInstaller.Preview("sandbox-" + name);
        }

        #endregion
    }
}
#endif
