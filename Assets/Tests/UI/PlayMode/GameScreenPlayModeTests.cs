using System.Collections;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// GameScreen 씬을 1920×1080 RenderTexture에 렌더링해 레이아웃과 재생 연출을 검증한다.
    /// PanelSettings 에셋을 직접 바꾸지 않도록 복제본에 RenderTexture를 연결한다.
    /// </summary>
    public class GameScreenPlayModeTests
    {
        GameScreenController _controller;
        UIDocument _doc;
        PanelSettings _panelCopy;
        RenderTexture _rt;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("GameScreen");
            yield return null;

            _controller = Object.FindAnyObjectByType<GameScreenController>();
            Assert.That(_controller, Is.Not.Null, "GameScreen 씬에 GameScreenController가 없음");
            _doc = _controller.GetComponent<UIDocument>();

            _rt = new RenderTexture((int)LayoutChecker.ScreenWidth, (int)LayoutChecker.ScreenHeight, 24);
            _rt.Create();
            _panelCopy = Object.Instantiate(_doc.panelSettings);
            _panelCopy.targetTexture = _rt;
            _doc.panelSettings = _panelCopy;

            _controller.Playback.Pause();
            _controller.Playback.Restart();
            for (int i = 0; i < 3; i++) yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_panelCopy != null) Object.Destroy(_panelCopy);
        }

        VisualElement Screen => _doc.rootVisualElement.Q("screen");

        [UnityTest]
        public IEnumerator 모든_재생_위치에서_레이아웃_넘침과_텍스트_잘림이_없다()
        {
            var playback = _controller.Playback;
            for (int index = -1; index < playback.Count; index++)
            {
                playback.SeekTo(index);
                yield return null;
                yield return null;
                var issues = LayoutChecker.Check(Screen);
                Assert.That(issues, Is.Empty, $"[위치 {index}]\n" + string.Join("\n", issues));
            }
        }

        [UnityTest]
        public IEnumerator x4_재생_중에는_현재_공만_표시하고_레이아웃이_유지된다()
        {
            var playback = _controller.Playback;
            playback.SeekTo(3);
            playback.SetSpeed(4);
            playback.Play();
            yield return null;
            yield return null;

            Assert.That(_doc.rootVisualElement.Q("abs-markers").childCount, Is.EqualTo(1));
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator x2_자동_재생이_끝까지_진행되고_판정_점등이_해제된다()
        {
            var playback = _controller.Playback;
            int flashes = 0;
            playback.IndexChanged += _ => { if (_controller.AbsZone.IsFlashing) flashes++; };
            playback.SetSpeed(2);
            playback.Play();

            float timeout = Time.realtimeSinceStartup + 20f;
            while (playback.IsPlaying && Time.realtimeSinceStartup < timeout) yield return null;

            Assert.That(playback.IsPlaying, Is.False, "제한 시간 안에 끝나지 않음");
            Assert.That(playback.Index, Is.EqualTo(playback.Count - 1));
            Assert.That(_controller.AbsZone.IsFlashing, Is.True, "마지막 투구 직후 점등 중이어야 함");

            yield return new WaitForSecondsRealtime(GameScreenController.FlashSecondsFor(2) + 0.3f);
            Assert.That(_controller.AbsZone.IsFlashing, Is.False, "점등이 해제되지 않음");
            Assert.That(flashes, Is.EqualTo(playback.Count), "모든 투구 직후 점등되어야 함");
        }

        [UnityTest]
        public IEnumerator x4에서는_판정_점등을_생략한다()
        {
            var playback = _controller.Playback;
            playback.SetSpeed(4);
            playback.StepPitch();
            yield return null;
            Assert.That(_controller.AbsZone.IsFlashing, Is.False);
        }

        [UnityTest]
        public IEnumerator 적시타_재생_시_타구선과_주자_이동이_진행되고_최종_상태로_끝난다()
        {
            var field = _controller.Field;
            _controller.Playback.SeekTo(4);
            yield return null;

            Assert.That(field.IsAnimating, Is.True);
            Assert.That(field.BattedBallVisible, Is.True);
            Assert.That(field.BattedBallEnd.x, Is.GreaterThan(FieldGeometry.Home.x), "우전 타구는 오른쪽");
            Assert.That(_doc.rootVisualElement.Q("base-3").ClassListContains("base--occupied"), Is.True,
                "이동 중에는 투구 전 루 상태(1·3루)");

            // 애니메이션 중간에도 레이아웃이 깨지지 않아야 한다.
            yield return new WaitForSeconds(FieldView.LineSeconds + FieldView.RunSeconds * 0.5f);
            Assert.That(field.IsAnimating, Is.True);
            var mid = LayoutChecker.Check(Screen);
            Assert.That(mid, Is.Empty, string.Join("\n", mid));

            float timeout = Time.realtimeSinceStartup + 5f;
            while (field.IsAnimating && Time.realtimeSinceStartup < timeout) yield return null;
            yield return null;

            Assert.That(field.IsAnimating, Is.False);
            Assert.That(field.LineProgress, Is.EqualTo(1f));
            var tokens = field.TokenCenters();
            Assert.That(tokens.Keys, Is.EquivalentTo(new[] { "home_b3", "home_b1" }), "득점한 3루 주자 토큰은 사라짐");
            Assert.That(tokens["home_b3"], Is.EqualTo(FieldGeometry.First));
            Assert.That(tokens["home_b1"], Is.EqualTo(FieldGeometry.Second));
            Assert.That(_doc.rootVisualElement.Q("base-1").ClassListContains("base--occupied"), Is.True);
            Assert.That(_doc.rootVisualElement.Q("base-2").ClassListContains("base--occupied"), Is.True);
            Assert.That(_doc.rootVisualElement.Q("base-3").ClassListContains("base--occupied"), Is.False);
            var end = LayoutChecker.Check(Screen);
            Assert.That(end, Is.Empty, string.Join("\n", end));
        }

        [UnityTest]
        public IEnumerator 타구가_아닌_투구에서는_타구선이_없다()
        {
            _controller.Playback.SeekTo(4);
            yield return null;
            _controller.Playback.SeekTo(5);
            yield return null;
            Assert.That(_controller.Field.BattedBallVisible, Is.False);
            Assert.That(_controller.Field.IsAnimating, Is.False);
        }

        [UnityTest]
        public IEnumerator 적시타로_H와_타자_오늘_기록이_1씩_오른다()
        {
            var root = _doc.rootVisualElement;
            _controller.Playback.SeekTo(3);
            yield return null;
            Assert.That(root.Q<Label>("ls-home-h").text, Is.EqualTo("5"));
            Assert.That(LineupNote(root, "home_b3"), Is.EqualTo("1-3"));
            Assert.That(root.Q<Label>("batter-line1").text, Does.StartWith("오늘 3타수 1안타"));

            _controller.Playback.SeekTo(4);
            yield return null;
            // Phase 5: 기록은 필드 연출(주자 도착)이 끝난 뒤 반영된다.
            Assert.That(_controller.Field.IsAnimating, Is.True);
            Assert.That(root.Q<Label>("ls-home-h").text, Is.EqualTo("5"), "연출 중에는 아직 반영 전");
            Assert.That(LineupNote(root, "home_b3"), Is.EqualTo("1-3"));
            yield return WaitForField();
            Assert.That(root.Q<Label>("ls-home-h").text, Is.EqualTo("6"));
            Assert.That(root.Q<Label>("ls-away-h").text, Is.EqualTo("7"), "수비팀 H는 그대로");
            Assert.That(LineupNote(root, "home_b3"), Is.EqualTo("2-4"));
            Assert.That(root.Q<Label>("batter-line1").text, Does.StartWith("오늘 4타수 2안타"));
            Assert.That(root.Q<Label>("play-type").text, Is.EqualTo("단타 · 우측 25°"));

            // 삼진도 타수에 들어간다: 한지후 0-3 → 0-4
            _controller.Playback.SeekTo(7);
            yield return null;
            Assert.That(LineupNote(root, "home_b4"), Is.EqualTo("0-4"));
        }

        IEnumerator WaitForField()
        {
            float timeout = Time.realtimeSinceStartup + 5f;
            while (_controller.Field.IsAnimating && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(_controller.Field.IsAnimating, Is.False, "필드 연출이 끝나지 않음");
            yield return null;
        }

        static string Text(VisualElement root, string name) => root.Q<Label>(name).text;

        [UnityTest]
        public IEnumerator 득점과_H는_주자가_홈을_밟는_순간에_반영된다()
        {
            var root = _doc.rootVisualElement;
            string scoreAtCompletion = null, hitsAtCompletion = null;
            int completions = 0;
            // 컨트롤러가 먼저 구독했으므로, 이 핸들러가 불릴 때는 컨트롤러 반영이 끝난 상태다.
            _controller.Field.AnimationCompleted += () =>
            {
                completions++;
                scoreAtCompletion = Text(root, "home-score");
                hitsAtCompletion = Text(root, "ls-home-h");
            };

            _controller.Playback.SeekTo(4);
            string lastScoreWhileRunning = null;
            float timeout = Time.realtimeSinceStartup + 5f;
            while (_controller.Field.IsAnimating && Time.realtimeSinceStartup < timeout)
            {
                lastScoreWhileRunning = Text(root, "home-score");
                yield return null;
            }

            Assert.That(lastScoreWhileRunning, Is.EqualTo("2"), "주자가 홈에 닿기 직전 프레임까지는 반영 전");
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(scoreAtCompletion, Is.EqualTo("3"), "연출 완료 콜백 시점에 득점 반영");
            Assert.That(hitsAtCompletion, Is.EqualTo("6"), "연출 완료 콜백 시점에 H 반영");
            Assert.That(Text(root, "play-runs"), Is.EqualTo("득점 +1"));
        }

        static readonly string[] ScenarioIds = ScenarioCatalog.All.Select(e => e.Id).ToArray();

        [UnityTest]
        public IEnumerator 모든_시나리오를_끝까지_재생해도_레이아웃과_필드_상태가_맞다(
            [ValueSource(nameof(ScenarioIds))] string scenarioId)
        {
            _controller.LoadScenario(scenarioId);
            _controller.Playback.Pause();
            yield return null;

            var root = _doc.rootVisualElement;
            var playback = _controller.Playback;
            var f = ScenarioCatalog.Find(scenarioId).Create();
            for (int i = 0; i < playback.Count; i++)
            {
                playback.SeekTo(i);
                yield return null;
                var mid = LayoutChecker.Check(Screen);
                Assert.That(mid, Is.Empty, $"[{scenarioId} {i}구 직후]\n" + string.Join("\n", mid));
                yield return WaitForField();

                var issues = LayoutChecker.Check(Screen);
                Assert.That(issues, Is.Empty, $"[{scenarioId} {i}구]\n" + string.Join("\n", issues));
                Assert.That(_controller.Sync.IsPending, Is.False);

                var expect = ScreenState.Build(f.Events, f.AwayScoreBefore, f.HomeScoreBefore, i);
                Assert.That(Text(root, "away-score"), Is.EqualTo(expect.AwayScore.ToString()), $"{scenarioId} {i}구 원정 점수");
                Assert.That(Text(root, "home-score"), Is.EqualTo(expect.HomeScore.ToString()), $"{scenarioId} {i}구 홈 점수");

                var tokens = _controller.Field.TokenCenters();
                var r = expect.Runners;
                var expectedIds = new[] { r.First, r.Second, r.Third }.Where(x => x != null).ToArray();
                Assert.That(tokens.Keys, Is.EquivalentTo(expectedIds), $"{scenarioId} {i}구 주자 토큰");
                if (r.First != null) Assert.That(tokens[r.First], Is.EqualTo(FieldGeometry.First));
                if (r.Second != null) Assert.That(tokens[r.Second], Is.EqualTo(FieldGeometry.Second));
                if (r.Third != null) Assert.That(tokens[r.Third], Is.EqualTo(FieldGeometry.Third));
            }
        }

        [UnityTest]
        public IEnumerator 이닝_교대_시나리오는_3아웃_반영_후_다음_반_이닝으로_넘어간다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadScenario(ScenarioCatalog.InningEnd);
            _controller.Playback.Pause();
            _controller.Playback.SeekTo(2);
            yield return null;
            Assert.That(Text(root, "status-inning"), Does.Contain("2사"), "뜬공 연출 중에는 아직 2사");
            yield return WaitForField();
            Assert.That(Text(root, "status-inning"), Is.EqualTo("8회말 · 3아웃 · 이닝 종료"));
            Assert.That(Text(root, "status-count"), Is.EqualTo("공수교대 · 다음 9회초 서울 공격"));
            Assert.That(Text(root, "headline-main"), Is.EqualTo("8회말 종료 · 공수교대"));

            _controller.Playback.StepPitch();
            yield return null;
            Assert.That(Text(root, "status-inning"), Is.EqualTo("9회초 · 무사 · 주자 없음"));
            Assert.That(Text(root, "pitcher-name"), Is.EqualTo("진도겸"));
            Assert.That(Text(root, "batter-name"), Is.EqualTo("김라온"));
            Assert.That(_controller.Field.TokenCount, Is.EqualTo(0));

            // 라인스코어 행: [0]=헤더 [1]=원정 [2]=홈. 셀: [0]=팀명, [i]=i회
            var rows = root.Q("linescore").Children().ToList();
            var homeEighth = (Label)rows[2].Children().ElementAt(8);
            var awayNinth = (Label)rows[1].Children().ElementAt(9);
            Assert.That(homeEighth.text, Is.EqualTo("0"), "장면 안에서 끝난 8회말 확정");
            Assert.That(awayNinth.ClassListContains("ls-cell--current"), Is.True, "9회초가 현재 이닝");
        }

        [UnityTest]
        public IEnumerator 시나리오_전환_시_이전_연출과_UI_찌꺼기가_남지_않는다()
        {
            var root = _doc.rootVisualElement;
            _controller.Playback.SeekTo(4); // 적시타 연출·점등 진행 중
            yield return null;
            Assert.That(_controller.Field.IsAnimating, Is.True);
            Assert.That(_controller.AbsZone.IsFlashing, Is.True);

            _controller.LoadScenario(ScenarioCatalog.HomeRun);
            _controller.Playback.Pause();
            yield return null;

            Assert.That(_controller.ScenarioId, Is.EqualTo(ScenarioCatalog.HomeRun));
            Assert.That(_controller.Playback.Index, Is.EqualTo(-1));
            Assert.That(_controller.Field.IsAnimating, Is.False);
            Assert.That(_controller.Field.BattedBallVisible, Is.False);
            Assert.That(_controller.Field.TokenCenters().Keys, Is.EquivalentTo(new[] { "home_b4" }), "새 장면의 1루 주자만");
            Assert.That(_controller.AbsZone.IsFlashing, Is.False);
            Assert.That(root.Q("abs-markers").childCount, Is.EqualTo(0));
            Assert.That(root.Q("log").childCount, Is.EqualTo(1), "대기 문구만");
            Assert.That(_controller.Sync.IsPending, Is.False);
            Assert.That(Text(root, "home-score"), Is.EqualTo("2"));
            Assert.That(root.Q<Button>("scenario-home-run").ClassListContains("debug-strip__btn--active"), Is.True);
            Assert.That(root.Q<Button>("scenario-8th-bottom").ClassListContains("debug-strip__btn--active"), Is.False);

            // 여러 번 전환해도 요소 수가 늘지 않는다.
            int baseline = CountElements(root);
            for (int round = 0; round < 3; round++)
            {
                foreach (var id in ScenarioIds)
                {
                    _controller.LoadScenario(id);
                    _controller.Playback.Pause();
                    _controller.Playback.SkipToEnd();
                    yield return null;
                }
            }
            _controller.LoadScenario(ScenarioCatalog.HomeRun);
            _controller.Playback.Pause();
            yield return null;
            Assert.That(CountElements(root), Is.EqualTo(baseline));
            Assert.That(root.Query("field-fx").ToList().Count, Is.EqualTo(1));
            Assert.That(root.Q("lineup").childCount, Is.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator 디버그_탭으로_시나리오가_바뀐다()
        {
            var root = _doc.rootVisualElement;
            var tab = root.Q<Button>("scenario-double-play");
            Assert.That(tab, Is.Not.Null);
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = tab;
                tab.SendEvent(submit);
            }
            yield return null;
            Assert.That(_controller.ScenarioId, Is.EqualTo(ScenarioCatalog.DoublePlay));
            Assert.That(Text(root, "batter-name"), Is.EqualTo("탁성호"));
        }

        IEnumerator WaitForDecision()
        {
            float timeout = Time.realtimeSinceStartup + 10f;
            while (!_controller.Playback.AwaitingDecision && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(_controller.Playback.AwaitingDecision, Is.True, "승부처에 도달하지 않음");
        }

        IEnumerator WaitForPlaybackEnd()
        {
            float timeout = Time.realtimeSinceStartup + 20f;
            while ((_controller.Playback.IsPlaying || _controller.Field.IsAnimating) && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.That(_controller.Playback.IsAtEnd, Is.True, "끝까지 재생되지 않음");
            yield return null;
        }

        static void Submit(Button button)
        {
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        [UnityTest]
        public IEnumerator 승부처에서_재생이_멈추고_작전_버튼이_켜진다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
            _controller.Playback.SetSpeed(4);
            _controller.Playback.Play();
            yield return WaitForDecision();
            yield return null;

            Assert.That(_controller.Playback.IsPlaying, Is.False);
            Assert.That(_controller.Playback.Index, Is.EqualTo(1));
            Assert.That(root.Q("tactics").ClassListContains("tactics--open"), Is.True);
            Assert.That(Text(root, "tactics-title"), Is.EqualTo("감독 개입 · 작전을 선택하세요"));
            foreach (var name in new[] { "tactic-default", "tactic-swing", "tactic-squeeze" })
                Assert.That(root.Q<Button>(name).enabledSelf, Is.True, name);
            foreach (var name in new[] { "tactic-pinch", "tactic-pitching" })
                Assert.That(root.Q<Button>(name).enabledSelf, Is.False, name + "는 범위 밖");
            Assert.That(root.Q<Button>("ctl-next-pitch").enabledSelf, Is.False, "작전 전에는 다음 투구 불가");
            Assert.That(Text(root, "headline-main"), Does.Contain("작전 선택 대기"));
            Assert.That(Text(root, "playback-position"), Is.EqualTo("투구 2 · 작전 대기"));

            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));

            // 작전 설명 미리보기도 넘치지 않아야 한다.
            foreach (TacticKind t in new[] { TacticKind.Default, TacticKind.SwingAway, TacticKind.SqueezeBunt })
            {
                _controller.PreviewTactic(t);
                yield return null;
                Assert.That(Text(root, "tactic-desc"), Is.EqualTo(TacticText.Detail(t)));
                var preview = LayoutChecker.Check(Screen);
                Assert.That(preview, Is.Empty, $"[{t} 설명]\n" + string.Join("\n", preview));
            }
            _controller.PreviewTactic(null);
        }

        [UnityTest]
        public IEnumerator 스퀴즈_번트를_고르면_기록을_유지한_채_분기_장면으로_이어진다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
            _controller.Playback.SetSpeed(4);
            _controller.Playback.Play();
            yield return WaitForDecision();
            // 이음새 비교는 x1에서 한다 (x4 재생 중에는 ABS가 현재 공만 보이는 별도 규칙이 있다).
            _controller.Playback.SetSpeed(1);
            yield return null;

            var before = _controller.Events.Take(2).ToArray();
            int markersBefore = root.Q("abs-markers").childCount;
            string countBefore = Text(root, "abs-count");

            Submit(root.Q<Button>("tactic-squeeze"));
            yield return null;

            // 이음새: 같은 위치, 같은 투구 객체, 같은 ABS 표시에서 재생 재개
            Assert.That(_controller.ChosenTactic, Is.EqualTo(TacticKind.SqueezeBunt));
            Assert.That(_controller.Playback.AwaitingDecision, Is.False);
            Assert.That(_controller.Playback.Count, Is.EqualTo(6));
            Assert.That(_controller.Events[0], Is.SameAs(before[0]));
            Assert.That(_controller.Events[1], Is.SameAs(before[1]));
            Assert.That(root.Q("tactics").ClassListContains("tactics--open"), Is.False);
            Assert.That(root.Q<Button>("tactic-squeeze").ClassListContains("btn--active"), Is.True);
            Assert.That(root.Q<Button>("tactic-squeeze").enabledSelf, Is.False, "고른 뒤에는 다시 누를 수 없음");
            if (_controller.Playback.Index == 1)
            {
                Assert.That(root.Q("abs-markers").childCount, Is.EqualTo(markersBefore));
                Assert.That(Text(root, "abs-count"), Is.EqualTo(countBefore));
            }

            // 분기 장면 전체를 재생하며 매 투구 레이아웃 확인
            int lastChecked = -1;
            float timeout = Time.realtimeSinceStartup + 20f;
            while ((_controller.Playback.IsPlaying || _controller.Field.IsAnimating) && Time.realtimeSinceStartup < timeout)
            {
                if (_controller.Playback.Index != lastChecked)
                {
                    lastChecked = _controller.Playback.Index;
                    yield return null;
                    var issues = LayoutChecker.Check(Screen);
                    Assert.That(issues, Is.Empty, $"[스퀴즈 {lastChecked}]\n" + string.Join("\n", issues));
                }
                yield return null;
            }
            yield return WaitForPlaybackEnd();

            Assert.That(Text(root, "home-score"), Is.EqualTo("3"), "스퀴즈로 동점");
            Assert.That(Text(root, "status-inning"), Is.EqualTo("8회말 · 3아웃 · 이닝 종료"));
            Assert.That(_controller.Field.TokenCenters().Keys, Is.EquivalentTo(new[] { "home_b1" }), "2루 잔루");
            Assert.That(Text(root, "ls-home-h"), Is.EqualTo("5"), "희생 번트·직선타는 안타 아님");
            Assert.That(LineupNote(root, "home_b3"), Is.EqualTo("1-3"), "희생 번트는 타수 아님");
            Assert.That(root.Q("log").Query<Label>(className: "log-entry--tactic").ToList().Count, Is.EqualTo(1),
                "분기점 직후 위치에 작전 기록 1건 (로그 5칸 안)");
            var end = LayoutChecker.Check(Screen);
            Assert.That(end, Is.Empty, string.Join("\n", end));
        }

        [UnityTest]
        public IEnumerator 작전_직후_중계에_작전_기록이_보인다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;
            Assert.That(_controller.ChooseTactic(TacticKind.SqueezeBunt), Is.True);
            _controller.Playback.Pause();
            _controller.Playback.StepPitch();
            yield return null;
            var notes = root.Q("log").Query<Label>(className: "log-entry--tactic").ToList();
            Assert.That(notes.Count, Is.EqualTo(1));
            Assert.That(notes[0].text, Is.EqualTo("감독 작전 · 스퀴즈 번트"));
            Assert.That(Text(root, "play-type"), Does.StartWith("스퀴즈 번트"));
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator 강공을_고르면_기존_8회말_장면과_같은_결과로_끝난다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
            _controller.Playback.SetSpeed(4);
            _controller.Playback.Play();
            yield return WaitForDecision();
            Assert.That(_controller.ChooseTactic(TacticKind.SwingAway), Is.True);
            yield return WaitForPlaybackEnd();

            Assert.That(Text(root, "home-score"), Is.EqualTo("3"));
            Assert.That(Text(root, "ls-home-h"), Is.EqualTo("6"));
            Assert.That(LineupNote(root, "home_b3"), Is.EqualTo("2-4"));
            Assert.That(Text(root, "status-inning"), Is.EqualTo("8회말 · 2사 · 1·2루"));
        }

        [UnityTest]
        public IEnumerator 처음으로와_시나리오_전환은_작전_상태를_초기화한다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;
            _controller.ChooseTactic(TacticKind.SqueezeBunt);
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return WaitForField();

            Submit(root.Q<Button>("ctl-restart"));
            yield return null;
            Assert.That(_controller.ChosenTactic, Is.Null);
            Assert.That(_controller.Playback.Index, Is.EqualTo(-1));
            Assert.That(_controller.Playback.Count, Is.EqualTo(2), "기본 구간으로 복귀");
            Assert.That(_controller.Playback.DecisionIndex, Is.EqualTo(1));
            Assert.That(root.Q<Button>("tactic-squeeze").ClassListContains("btn--active"), Is.False);
            Assert.That(Text(root, "home-score"), Is.EqualTo("2"));

            // 작전 대기 중 다른 시나리오로 전환
            _controller.Playback.SkipToEnd();
            yield return null;
            Assert.That(root.Q("tactics").ClassListContains("tactics--open"), Is.True);
            _controller.LoadScenario(ScenarioCatalog.HomeRun);
            _controller.Playback.Pause();
            yield return null;
            Assert.That(root.Q("tactics").ClassListContains("tactics--open"), Is.False);
            Assert.That(root.Q<Button>("tactic-squeeze").enabledSelf, Is.False);
            Assert.That(_controller.ChooseTactic(TacticKind.SqueezeBunt), Is.False, "분기점 없는 장면에서는 무시");
            Assert.That(_controller.Playback.AwaitingDecision, Is.False);
        }

        [UnityTest]
        public IEnumerator 분기점이_없는_장면에서는_작전_버튼이_꺼져_있다()
        {
            var root = _doc.rootVisualElement;
            _controller.Playback.SkipToEnd();
            yield return null;
            foreach (var name in new[] { "tactic-default", "tactic-swing", "tactic-squeeze", "tactic-pinch", "tactic-pitching" })
                Assert.That(root.Q<Button>(name).enabledSelf, Is.False, name);
        }

        static int CountElements(VisualElement root)
        {
            int n = 0;
            root.Query<VisualElement>().ForEach(_ => n++);
            return n;
        }

        static string LineupNote(VisualElement root, string playerId)
        {
            foreach (var row in root.Q("lineup").Children())
                if ((string)row.userData == playerId)
                    return row.Q<Label>(className: "roster-state").text;
            return null;
        }

        [UnityTest]
        public IEnumerator 더미_데이터로_미연결_문구가_사라진다()
        {
            _controller.Playback.SeekTo(4);
            yield return null;
            _doc.rootVisualElement.Query<Label>().ForEach(l =>
                Assert.That(l.text, Does.Not.Contain("미연결"), $"'{l.name}' 라벨에 미연결 문구"));
            Assert.That(_doc.rootVisualElement.Q<Label>("batter-name").text, Is.EqualTo("최도윤"));
        }
    }
}
