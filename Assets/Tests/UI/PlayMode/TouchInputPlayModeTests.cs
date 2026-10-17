using System.Collections;
using System.IO;
using DailyFranchise.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 43: 모바일 터치 대응. tooltip은 터치 길게 누르기로 보이고(손을 떼도 클릭은 실행되지 않음),
    /// 짧은 탭은 원래 클릭, 드래그는 길게 누르기를 취소한다. 마우스는 호버로 보인다.
    /// 로비·경기 화면 루트는 화면 비율과 무관하게 1920×1080으로 고정된다(레터박스).
    /// </summary>
    public class TouchInputPlayModeTests
    {
        static readonly string TestSavePath = Path.Combine(Application.temporaryCachePath, "playmode_touch_" + SaveManager.FileName);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyManager();
            SeasonManager.SavePathOverride = TestSavePath;
            SeasonManager.InjuryRandomOverride = new DailyFranchise.Sim.ScriptedRandom(0.999); // 우연한 부상 없이 (Phase 36)
            SeasonManager.LoadDatabaseSynchronously = true;
            DeleteSave();
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            float timeout = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().name != SeasonManager.LobbyScene && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SeasonManager.LobbyScene));
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyManager();
            DeleteSave();
            SeasonManager.SavePathOverride = null;
            SeasonManager.InjuryRandomOverride = null;
            SeasonManager.LoadDatabaseSynchronously = false;
            yield return null;
        }

        static void DeleteSave()
        {
            if (File.Exists(TestSavePath)) File.Delete(TestSavePath);
        }

        static void DestroyManager()
        {
            if (SeasonManager.Instance != null) Object.DestroyImmediate(SeasonManager.Instance.gameObject);
        }

        static LobbyController Lobby => Object.FindAnyObjectByType<LobbyController>();
        static VisualElement Root => Lobby.GetComponent<UIDocument>().rootVisualElement;

        static Touch Finger(Vector2 position, TouchPhase phase) => new Touch
        {
            fingerId = 0, position = position, rawPosition = position, phase = phase,
            type = TouchType.Direct, pressure = 1f, maximumPossiblePressure = 1f, tapCount = 1,
        };

        static void Send(VisualElement target, EventBase e)
        {
            using (e)
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        static void TouchDown(VisualElement e, Vector2 p) => Send(e, PointerDownEvent.GetPooled(Finger(p, TouchPhase.Began)));
        static void TouchMove(VisualElement e, Vector2 p) => Send(e, PointerMoveEvent.GetPooled(Finger(p, TouchPhase.Moved)));
        static void TouchUp(VisualElement e, Vector2 p) => Send(e, PointerUpEvent.GetPooled(Finger(p, TouchPhase.Ended)));
        static void Click(VisualElement e) => Send(e, ClickEvent.GetPooled());

        static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest]
        public IEnumerator 터치로_길게_누르면_툴팁이_뜨고_손을_떼도_클릭은_실행되지_않는다()
        {
            var tips = Lobby.Tooltips;
            var target = Root.Q("front-office");
            Assert.That(target.tooltip, Is.Not.Empty);
            Assert.That(tips.OwnerOf(target), Is.SameAs(target));

            var p = new Vector2(100, 100);
            TouchDown(target, p);
            Assert.That(tips.IsShowing, Is.False, "누르자마자 뜨지 않는다");
            yield return WaitSeconds(TouchTooltip.LongPressMs / 1000f + 0.3f);
            yield return null;

            Assert.That(tips.IsShowing, Is.True, "길게 누르면 툴팁");
            Assert.That(tips.Text, Is.EqualTo(target.tooltip));
            Assert.That(tips.ShownFor, Is.SameAs(target));

            TouchUp(target, p);
            Click(target); // 패널이 손 뗌 뒤에 만드는 클릭
            yield return null;
            Assert.That(Lobby.FrontOffice.IsOpen, Is.False, "길게 누른 뒤의 클릭은 삼킨다");
            Assert.That(tips.IsShowing, Is.True, "손을 떼도 다음 터치까지 남는다");

            // 툴팁 말풍선은 루트(1920×1080) 안에 있다.
            yield return null;
            var bubble = tips.Bubble.worldBound;
            var root = Root.worldBound;
            Assert.That(bubble.xMin, Is.GreaterThanOrEqualTo(root.xMin - 0.5f));
            Assert.That(bubble.xMax, Is.LessThanOrEqualTo(root.xMax + 0.5f));
            Assert.That(bubble.yMin, Is.GreaterThanOrEqualTo(root.yMin - 0.5f));
            Assert.That(bubble.yMax, Is.LessThanOrEqualTo(root.yMax + 0.5f));

            // 다음 터치(짧은 탭)는 툴팁을 닫고 원래 클릭을 실행한다.
            TouchDown(target, p);
            Assert.That(tips.IsShowing, Is.False);
            TouchUp(target, p);
            Click(target);
            yield return null;
            Assert.That(Lobby.FrontOffice.IsOpen, Is.True, "짧은 탭은 그대로 클릭");
            Lobby.FrontOffice.Close();
        }

        [UnityTest]
        public IEnumerator 짧게_탭하면_툴팁_없이_바로_클릭된다()
        {
            var tips = Lobby.Tooltips;
            var target = Root.Q("front-office");
            var p = new Vector2(100, 100);
            TouchDown(target, p);
            TouchUp(target, p);
            Click(target);
            yield return WaitSeconds(TouchTooltip.LongPressMs / 1000f + 0.3f);
            Assert.That(tips.IsShowing, Is.False, "손을 뗀 뒤에는 길게 누르기 타이머가 남지 않는다");
            Assert.That(Lobby.FrontOffice.IsOpen, Is.True);
            Lobby.FrontOffice.Close();
        }

        [UnityTest]
        public IEnumerator 누른_채로_드래그하면_길게_누르기가_취소된다()
        {
            var tips = Lobby.Tooltips;
            var target = Root.Q("front-office");
            TouchDown(target, new Vector2(100, 100));
            TouchMove(target, new Vector2(100 + TouchTooltip.MoveTolerance + 10f, 100));
            yield return WaitSeconds(TouchTooltip.LongPressMs / 1000f + 0.3f);
            Assert.That(tips.IsShowing, Is.False);
            TouchUp(target, new Vector2(100 + TouchTooltip.MoveTolerance + 10f, 100));
        }

        [UnityTest]
        public IEnumerator 마우스는_호버로_툴팁을_본다()
        {
            var tips = Lobby.Tooltips;
            var target = Root.Q("front-office");
            var move = new Event { type = EventType.MouseMove, mousePosition = new Vector2(100, 100) };
            Send(target, PointerOverEvent.GetPooled(move));
            yield return WaitSeconds(TouchTooltip.HoverDelayMs / 1000f + 0.3f);
            Assert.That(tips.IsShowing, Is.True);
            Assert.That(tips.Text, Is.EqualTo(target.tooltip));
        }

        [UnityTest]
        public IEnumerator 툴팁_없는_요소의_자식도_가장_가까운_툴팁_조상을_찾는다()
        {
            var tips = Lobby.Tooltips;
            var target = Root.Q("front-office");
            var child = new VisualElement();
            target.Add(child);
            Assert.That(tips.OwnerOf(child), Is.SameAs(target));
            Assert.That(tips.OwnerOf(Root), Is.Null);
            Assert.That(TouchTooltip.Install(Root), Is.SameAs(tips), "다시 설치해도 하나만");
            Assert.That(Root.Query<Label>(TouchTooltip.BubbleName).ToList().Count, Is.EqualTo(1));
            child.RemoveFromHierarchy();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 로비_루트는_1920x1080_가운데에_고정된다()
        {
            yield return null;
            var panelSize = Root.panel.visualTree.layout.size;
            var r = Root.layout;
            Assert.That(r.width, Is.EqualTo(ScreenLetterbox.ReferenceWidth).Within(0.5f));
            Assert.That(r.height, Is.EqualTo(ScreenLetterbox.ReferenceHeight).Within(0.5f));
            Assert.That(r.x, Is.EqualTo(Mathf.Max(0, (panelSize.x - 1920f) / 2f)).Within(0.5f));
            Assert.That(r.y, Is.EqualTo(Mathf.Max(0, (panelSize.y - 1080f) / 2f)).Within(0.5f));
            Assert.That(panelSize.x, Is.GreaterThanOrEqualTo(1920f - 0.5f), "Expand: 패널은 기준 해상도보다 작아지지 않는다");
            Assert.That(panelSize.y, Is.GreaterThanOrEqualTo(1080f - 0.5f));
        }
    }
}
