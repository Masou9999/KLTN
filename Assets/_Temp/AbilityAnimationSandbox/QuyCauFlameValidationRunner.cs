#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Sandbox
{
    /// <summary>Offline Play Mode checks of real staging, board views and flame lifecycle.</summary>
    public sealed class QuyCauFlameValidationRunner : MonoBehaviour
    {
        #region Batch Lifecycle

        private const string Key = "QuyCauFlame.Validation";
        private const string Folder = "Logs/QuyCauFlame";
        private MatchBoardPresenter board;
        private MatchDebugControls controls;
        private RectTransform canvas, motion;
        private RectTransform[] selfSlots, otherSlots;
        private ulong revision;
        private int checks;
        private bool finished;

        public static void RunBatch()
        {
            Directory.CreateDirectory(Folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void ResumeBatch()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode
                    && SessionState.GetBool(Key, false))
                    new GameObject("QuyCauFlameValidation")
                        .AddComponent<QuyCauFlameValidationRunner>();
            };
        }

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            IEnumerator run = Run();
            while (!finished)
            {
                object current;
                try
                {
                    if (!run.MoveNext()) break;
                    current = run.Current;
                }
                catch (Exception error)
                {
                    Finish(false, error.ToString());
                    yield break;
                }
                yield return current;
            }
            if (!finished) Finish(true, checks + " assertions passed.");
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
                Finish(false, message + "\n" + stack);
        }

        private void Finish(bool success, string message)
        {
            if (finished) return;
            finished = true;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(Key, false);
            File.WriteAllText(Folder + "/result.txt",
                (success ? "PASS: " : "FAIL: ") + message);
            EditorApplication.Exit(success ? 0 : 1);
        }

        #endregion

        #region Scenarios

        private IEnumerator Run()
        {
            CheckPlayback();
            BuildFixture();
            var q = Card("q", "QuyCau");
            var target = Card("t", "MaLon");
            Apply(new[] { q, target }, Array.Empty<CardViewDto>(), true);
            yield return null;
            Check(Flames().Length == 0, "Reserve alone has no flames");
            var qDrag = Visual("q").GetComponent<DraggableHandCard>();
            Check(controls.TryStageBoardCard(qDrag, 0, selfSlots[0]), "Stage source");
            yield return new WaitForSecondsRealtime(.55f);
            RawImage flame = Flame(1);
            Check(flame != null && flame.enabled, "Adjacent empty slot burns");
            Check(Flames().Count(x => x.enabled) == 1, "Exactly one adjacent slot");
            Check(Vector3.Distance(flame.transform.position,
                selfSlots[1].TransformPoint(selfSlots[1].rect.center)) < .1f,
                "Empty slot anchor");
            AudioSource sound = flame.GetComponentInParent<AudioSource>();
            Check(sound != null && sound.loop && sound.isPlaying, "Shared looping SFX");
            Check(sound.clip.length > 0 && sound.clip.loadState == AudioDataLoadState.Loaded,
                "MP3 imported and loaded");

            Check(controls.TryStageBoardCard(Visual("t").GetComponent<DraggableHandCard>(),
                1, selfSlots[1]), "Stage supported card");
            yield return new WaitForSecondsRealtime(.6f);
            AssertAnchor(flame, Visual("t"));
            Check(flame.GetComponentInParent<Canvas>().sortingOrder
                > MatchSorting.Order(MatchSorting.CardMotion, 99), "Above moving cards");
            Check(!flame.raycastTarget && !flame.maskable, "Non-blocking VFX");

            q.boardSlotIndex = 0;
            target.boardSlotIndex = 1;
            Apply(Array.Empty<CardViewDto>(), new[] { q, target }, false);
            yield return null;
            yield return null;
            Check(Flame(1) == flame, "Snapshot rebuild preserves effect identity");
            Check(Frame(flame) >= 4 && Frame(flame) < 16, "Snapshot does not restart intro");
            AssertAnchor(flame, Visual("t"));

            var resolution = new RoundResolutionDto { steps = new[] {
                new CombatStepDto { slotIndex = 0,
                    hostCard = new ResolvedCardDto { seat = 0, cardBefore = q } },
                new CombatStepDto { slotIndex = 1,
                    hostCard = new ResolvedCardDto { seat = 0, cardBefore = target } }
            } };
            board.PrepareCombat(resolution, 0);
            var animator = Visual("t").GetComponent<CombatCardAnimator>();
            animator.Prepare(motion, true, 90f);
            yield return animator.MoveToRetreat(.2f);
            yield return null;
            AssertAnchor(flame, Visual("t"));
            yield return animator.Strike(animator.HomeWorldPosition + Vector3.up * 100, .2f);
            yield return null;
            AssertAnchor(flame, Visual("t"));
            yield return animator.ReturnHome(.2f);

            // Exercise the actual combat lethal hook, before delayed death animation.
            var directorObject = new GameObject("Director");
            directorObject.SetActive(false);
            var director = directorObject.AddComponent<CombatAnimationDirector>();
            Set(director, "boardPresenter", board);
            typeof(CombatAnimationDirector).GetMethod("RegisterLethalHighlight", Flags)
                .Invoke(director, new object[] {
                    new ResolvedCardDto { seat = 0, cardBefore = q, died = true },
                    new System.Collections.Generic.List<IEnumerator>() });
            yield return null;
            yield return null;
            Check(Frame(flame) >= 16, "Lethal hook starts outro before final snapshot");
            yield return new WaitForSecondsRealtime(1f);
            Check(!flame.enabled && !sound.isPlaying, "Death cleans visual and sound");

            q.boardSlotIndex = -1;
            target.boardSlotIndex = -1;
            Apply(new[] { q, target }, Array.Empty<CardViewDto>(), true);
            yield return null;
            qDrag = Visual("q").GetComponent<DraggableHandCard>();
            Check(controls.TryStageBoardCard(qDrag, 2, selfSlots[2]), "Stage right edge");
            yield return new WaitForSecondsRealtime(.2f);
            Check(!Flames().Any(x => x.enabled), "Right edge never burns");
            controls.TryReturnBoardCardToReserve(qDrag);
            for (int i = 0; i < 35; i++)
            {
                controls.TryStageBoardCard(qDrag, 0, selfSlots[0]);
                yield return null;
                controls.TryReturnBoardCardToReserve(qDrag);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            Check(Flames().Length == 1 && !flame.enabled, "Spam remains bounded and drains");
            Check(!sound.isPlaying, "Spam leaves no audio tail");

            controls.TryStageBoardCard(qDrag, 0, selfSlots[0]);
            yield return new WaitForSecondsRealtime(.1f);
            controls.ClearPendingSelection();
            yield return new WaitForSecondsRealtime(1.4f);
            Check(!flame.enabled, "Cancel draft drains intro/outro");

            controls.TryStageBoardCard(qDrag, 0, selfSlots[0]);
            yield return new WaitForSecondsRealtime(.1f);
            MatchProjectionRegistry.Current.Reject(CommandRejectionReason.NoValidAbilityTargets);
            yield return new WaitForSecondsRealtime(1.4f);
            Check(!flame.enabled, "Rejected draft drains effect");

            var enemyQ = Card("eq", "QuyCau");
            enemyQ.boardSlotIndex = 0;
            q.boardSlotIndex = 0;
            target.boardSlotIndex = 1;
            Apply(Array.Empty<CardViewDto>(), new[] { q, target }, false, new[] { enemyQ });
            yield return new WaitForSecondsRealtime(.6f);
            Check(Flames().Count(x => x.enabled) == 2, "Both sides burn independently");
            Check(canvas.GetComponentsInChildren<AudioSource>()
                .Count(x => x.clip != null && x.clip.name == "flaming-sound") == 1,
                "Multiple effects share one audio source");
            board.NotifyFlameDeparture("q", false);
            yield return new WaitForSecondsRealtime(1f);
            Check(!Flame(1).enabled && Flame(4).enabled && sound.isPlaying,
                "Survivor return removes only its source; remaining SFX continues");

            // Exercise the shared ability sacrifice/death entry point.
            IEnumerator death = (IEnumerator)typeof(CombatAnimationDirector)
                .GetMethod("PlayCardDeath", Flags).Invoke(director,
                    new object[] { Visual("eq"), "eq" });
            death.MoveNext();
            yield return new WaitForSecondsRealtime(1f);
            Check(!Flames().Any(x => x.enabled) && !sound.isPlaying,
                "Ability death ends final effect and shared audio");

            Apply(Array.Empty<CardViewDto>(), new[] { q, target }, false);
            yield return new WaitForSecondsRealtime(.6f);
            ScreenCapture.CaptureScreenshot(Folder + "/flame-board.png");
            yield return null;
            board.enabled = false;
            yield return null;
            Check(Flames().Length == 0, "Disable destroys independent VFX root");
            board.enabled = true;
            yield return new WaitForSecondsRealtime(.2f);
            Check(Flames().Length == 1, "Reconnect reconstructs one effect");
            MatchProjectionRegistry.Current.Reset();
            yield return null;
            yield return null;
            Check(Flames().Length == 0, "Registry reset cleans VFX without snapshot event");
        }

        private void CheckPlayback()
        {
            var p = new QuyCauFlamePlayback();
            p.SetDesired(true);
            p.SetDesired(false);
            int[] expected = { 0, 1, 2, 3, 16, 17, 18, 19, 20, 21, 22, 23, -1 };
            foreach (int frame in expected)
            {
                Check(p.Frame == frame, "Fast withdrawal frame " + frame);
                p.Tick(.125f, .125f);
            }
            p.SetDesired(true);
            p.Tick(.5f, .125f);
            Check(p.Frame == 4, "Intro enters loop");
            p.Tick(120f, .125f);
            Check(p.Frame == 4, "Long loop stays in 12-frame region");
            p.SetDesired(false);
            p.SetDesired(true);
            Check(p.Frame == 16, "Re-entry preserves running outro");
            p.Tick(1f, .125f);
            Check(p.Frame == 0, "Latest desired state restarts exactly once");
            p.SetDesired(false, true);
            Check(p.Frame == 16, "Death interrupts intro immediately");
            p.Tick(1f, .125f);
            Check(!p.IsVisible, "Death outro terminates");
        }

        #endregion

        #region Fixture Helpers

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Set(object owner, string name, object value) =>
            owner.GetType().GetField(name, Flags).SetValue(owner, value);

        private void BuildFixture()
        {
            MatchProjectionRegistry.Current.Reset();
            canvas = Rect("Canvas", null);
            var c = canvas.gameObject.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            MatchSorting.Apply(c, MatchSorting.Board);
            canvas.gameObject.AddComponent<CanvasScaler>();
            var background = Rect("Background", canvas);
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.sizeDelta = Vector2.zero;
            background.gameObject.AddComponent<Image>().color = new Color(.08f, .1f, .13f);
            motion = Rect("Motion", canvas);
            MatchSorting.Apply(motion.gameObject.AddComponent<Canvas>(), MatchSorting.CardMotion, 10);
            selfSlots = Slots(-180f);
            otherSlots = Slots(180f);
            var host = Rect("Presenters", canvas).gameObject;
            host.SetActive(false);
            controls = host.AddComponent<MatchDebugControls>();
            board = host.AddComponent<MatchBoardPresenter>();
            Set(board, "actionControls", controls);
            Set(board, "selfBoardSlots", selfSlots);
            Set(board, "opponentBoardSlots", otherSlots);
            Set(board, "selfReserveRoot", Rect("Reserve", canvas));
            Set(board, "opponentReserveRoot", Rect("OtherReserve", canvas));
            Set(board, "dragLayer", motion);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath("c69e6023186abf741b99242a29336351"));
            Set(board, "cardPrefab", prefab.GetComponent<NetworkCardVisual>());
            Set(board, "presentationCatalog", AssetDatabase.LoadAssetAtPath<CardPresentationCatalog>(
                AssetDatabase.GUIDToAssetPath("fe3b600fb29f4474d8e8da0ba44a7d4e")));
            host.SetActive(true);
        }

        private RectTransform[] Slots(float y)
        {
            return Enumerable.Range(0, 3).Select(i =>
            {
                var slot = Rect("Slot" + y + "_" + i, canvas);
                slot.anchoredPosition = new Vector2((i - 1) * 230f, y);
                return slot;
            }).ToArray();
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(180f, 252f);
            return rect;
        }

        private static CardViewDto Card(string id, string definition) => new CardViewDto {
            instanceId = id, definitionId = definition, displayName = definition,
            health = 3, damage = 3, supportDamageBonus = definition == "QuyCau" ? 2 : 0
        };

        private void Apply(CardViewDto[] reserve, CardViewDto[] cards, bool draft,
            CardViewDto[] opponents = null)
        {
            MatchProjectionRegistry.Current.Apply(new MatchSnapshotDto {
                revision = ++revision, roundNumber = 1, viewerSeat = 0,
                viewerCanDeclareAttack = draft,
                self = new PlayerViewDto { seat = 0, mana = 10, reserve = reserve,
                    hand = Array.Empty<CardViewDto>(), board = cards },
                opponent = new PlayerViewDto { seat = 1,
                    board = opponents ?? Array.Empty<CardViewDto>() }
            });
        }

        private NetworkCardVisual Visual(string id)
        {
            Check(board.TryGetFaceUpVisual(id, out var view), "View exists: " + id);
            return view;
        }

        private RawImage[] Flames() => canvas.GetComponentsInChildren<RawImage>()
            .Where(x => x.name.StartsWith("SupportFlame_")).ToArray();
        private RawImage Flame(int index) => Flames()
            .FirstOrDefault(x => x.name == "SupportFlame_" + index);
        private static int Frame(RawImage image) =>
            (5 - Mathf.RoundToInt(image.uvRect.y * 6)) * 4
            + Mathf.RoundToInt(image.uvRect.x * 4);

        private void AssertAnchor(RawImage flame, NetworkCardVisual target)
        {
            Check(Vector3.Distance(flame.transform.position,
                target.CardRect.TransformPoint(target.CardRect.rect.center)) < .1f,
                "Flame follows card after motion/reparenting");
        }

        private void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
            checks++;
        }

        #endregion
    }
}
#endif
