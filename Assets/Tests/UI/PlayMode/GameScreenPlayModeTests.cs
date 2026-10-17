using System.Collections;
using System.Collections.Generic;
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
            // 작전은 기본적으로 항상 성공 (0.0 < 성공률). 실패 경로 테스트는 따로 바꿔 넣는다.
            _controller.TacticRandom = new ScriptedRandom(0.0);
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
                Assert.That(Text(root, "tactic-desc"), Does.StartWith(TacticText.Detail(t)));
                Assert.That(Text(root, "tactic-desc"), Does.EndWith(
                    TacticText.Odds(Fixture8thBottomTactics.Create().Branch[t])), "설명 끝에 성공 확률");
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
            Assert.That(notes[0].text, Is.EqualTo("감독 작전 · 스퀴즈 번트 · 성공"));
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
        public IEnumerator 스퀴즈가_실패하면_무득점으로_이닝이_끝나고_실패가_표시된다()
        {
            var root = _doc.rootVisualElement;
            _controller.TacticRandom = new ScriptedRandom(0.99); // 성공률 60% 이상 → 실패
            _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
            _controller.Playback.SetSpeed(4);
            _controller.Playback.Play();
            yield return WaitForDecision();
            Assert.That(_controller.FinalScore(), Is.EqualTo(new GameScore(3, 3)), "작전 전 종료는 기본 진행(적시타 동점)으로 계산");

            Submit(root.Q<Button>("tactic-squeeze"));
            Assert.That(_controller.TacticResult, Is.EqualTo(TacticOutcome.Failure));
            Assert.That(_controller.Playback.Count, Is.EqualTo(3));
            Assert.That(Text(root, "tactic-desc"), Does.EndWith("결과 대기 중"), "결과 투구 전에는 성패를 밝히지 않음");
            yield return WaitForPlaybackEnd();
            yield return WaitForField();

            Assert.That(Text(root, "home-score"), Is.EqualTo("2"), "무득점");
            Assert.That(Text(root, "status-inning"), Is.EqualTo("8회말 · 3아웃 · 이닝 종료"));
            Assert.That(Text(root, "play-type"), Does.StartWith("뜬공 더블 아웃"));
            Assert.That(_controller.Field.TokenCenters().Keys, Is.EquivalentTo(new[] { "home_b1" }), "1루 잔루");
            Assert.That(LineupNote(root, "home_b3"), Is.EqualTo("1-4"), "실패한 번트 뜬공은 타수");
            Assert.That(Text(root, "tactic-desc"), Does.EndWith("결과 · 스퀴즈 번트 실패"));
            var notes = root.Q("log").Query<Label>(className: "log-entry--tactic").ToList();
            Assert.That(notes.Select(n => n.text), Is.EqualTo(new[] { "감독 작전 · 스퀴즈 번트 · 실패" }));
            Assert.That(_controller.FinalScore(), Is.EqualTo(new GameScore(3, 2)));
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator 강공이_실패하면_병살로_이닝이_끝난다()
        {
            var root = _doc.rootVisualElement;
            _controller.TacticRandom = new ScriptedRandom(0.5); // 강공 성공률 50% → 실패
            _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
            _controller.Playback.SetSpeed(4);
            _controller.Playback.Play();
            yield return WaitForDecision();
            Assert.That(_controller.ChooseTactic(TacticKind.SwingAway), Is.True);
            yield return WaitForPlaybackEnd();
            yield return WaitForField();

            Assert.That(_controller.TacticResult, Is.EqualTo(TacticOutcome.Failure));
            Assert.That(Text(root, "home-score"), Is.EqualTo("2"));
            Assert.That(Text(root, "play-type"), Does.StartWith("땅볼 병살"));
            Assert.That(_controller.Field.TokenCenters().Keys, Is.EquivalentTo(new[] { "home_b2" }), "3루 잔루");
            Assert.That(_controller.FinalScore(), Is.EqualTo(new GameScore(3, 2)));
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator 같은_Seed를_넣으면_작전_결과가_같다()
        {
            TacticOutcome? Choose(int seed)
            {
                _controller.TacticRandom = new SeededRandom(seed);
                _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
                _controller.Playback.Pause();
                _controller.Playback.SkipToEnd();
                _controller.ChooseTactic(TacticKind.SqueezeBunt);
                _controller.Playback.Pause();
                return _controller.TacticResult;
            }

            var first = Enumerable.Range(0, 10).Select(i => Choose(100 + i)).ToList();
            var again = Enumerable.Range(0, 10).Select(i => Choose(100 + i)).ToList();
            Assert.That(again, Is.EqualTo(first));
            Assert.That(first, Has.Some.EqualTo(TacticOutcome.Failure).And.Some.EqualTo(TacticOutcome.Success),
                "Seed 100~109에서 성공·실패가 모두 나온다");
            yield return null;
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

        // ── 퀵 시뮬레이션 경기 (Phase 13) ──

        /// <summary>라인스코어 한 행의 1~9회 칸 글자. 행: [1]=원정 [2]=홈.</summary>
        static string[] InningCells(VisualElement root, int row) =>
            root.Q("linescore").Children().ElementAt(row).Children().Skip(1).Take(9).Select(c => ((Label)c).text).ToArray();

        [UnityTest]
        public IEnumerator 퀵_시뮬_경기는_생성된_이닝별_득점과_승부처_상황으로_시작한다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadGeneratedScenario(Data.Matchup.Default, new SeededRandom(20261014));
            _controller.Playback.Pause();
            _controller.Playback.Restart();
            yield return null;

            var game = _controller.Generated;
            Assert.That(game, Is.Not.Null);
            Assert.That(_controller.ScenarioId, Is.EqualTo(FixtureGenerator.ScenarioId));
            Assert.That(_controller.Fixture, Is.SameAs(game.Fixture), "홈 경기는 뒤집지 않음");

            // 원정: 1~8회 확정 + 9회 빈칸. 홈: 1~7회 확정 + 8회말 진행 중(0) + 9회 빈칸.
            var away = InningCells(root, 1);
            Assert.That(away.Take(8), Is.EqualTo(game.AwayRunsByInning.Select(r => r.ToString())));
            Assert.That(away[8], Is.EqualTo(""));
            var home = InningCells(root, 2);
            Assert.That(home.Take(7), Is.EqualTo(game.HomeRunsByInning.Take(7).Select(r => r.ToString())));
            Assert.That(home[7], Is.EqualTo("0"));
            Assert.That(home[8], Is.EqualTo(""));
            Assert.That(Text(root, "away-score"), Is.EqualTo(game.Fixture.AwayScoreBefore.ToString()));
            Assert.That(Text(root, "home-score"), Is.EqualTo(game.Fixture.HomeScoreBefore.ToString()));
            Assert.That(Text(root, "ls-away-h"), Is.EqualTo(game.AwayHits.ToString()));
            Assert.That(Text(root, "ls-home-b"), Is.EqualTo(game.HomeWalks.ToString()));

            var s = game.Situation;
            Assert.That(Text(root, "status-inning"), Is.EqualTo($"8회말 · {PitchPresentation.OutsText(s.Outs)} · {s.Runners}"));
            Assert.That(_controller.Field.TokenCount, Is.EqualTo(s.Runners.RunnerCount), "필드 주자 토큰");
            Assert.That(root.Q<Button>("scenario-" + FixtureGenerator.ScenarioId).ClassListContains("debug-strip__btn--active"), Is.True);

            // 같은 Seed로 다시 불러오면 같은 경기
            _controller.LoadGeneratedScenario(Data.Matchup.Default, new SeededRandom(20261014));
            _controller.Playback.Pause();
            yield return null;
            Assert.That(_controller.Generated.AwayRunsByInning, Is.EqualTo(game.AwayRunsByInning));
            Assert.That(_controller.Generated.HomeRunsByInning, Is.EqualTo(game.HomeRunsByInning));
            Assert.That(_controller.Generated.Situation, Is.EqualTo(s));
        }

        [UnityTest]
        public IEnumerator 퀵_시뮬_경기는_승부처에서_멈추고_상황에_맞는_작전만_켜진다()
        {
            var root = _doc.rootVisualElement;
            // 스퀴즈 가능·불가 상황이 모두 나올 때까지 Seed를 바꿔 본다.
            bool sawSqueeze = false, sawNoSqueeze = false;
            for (int seed = 1; seed <= 20 && !(sawSqueeze && sawNoSqueeze); seed++)
            {
                _controller.LoadGeneratedScenario(Data.Matchup.Default, new SeededRandom(seed));
                _controller.Playback.Pause();
                _controller.Playback.SkipToEnd();
                yield return null;

                var s = _controller.Generated.Situation;
                Assert.That(_controller.Playback.AwaitingDecision, Is.True);
                Assert.That(Text(root, "headline-main"), Does.Contain($"{PitchPresentation.OutsText(s.Outs)} {s.Runners} · 작전 선택 대기"));
                Assert.That(root.Q<Button>("tactic-default").enabledSelf, Is.True);
                Assert.That(root.Q<Button>("tactic-swing").enabledSelf, Is.True);
                Assert.That(root.Q<Button>("tactic-squeeze").enabledSelf, Is.EqualTo(s.AllowsSqueeze), s.ToString());
                if (s.AllowsSqueeze) sawSqueeze = true; else sawNoSqueeze = true;

                var issues = LayoutChecker.Check(Screen);
                Assert.That(issues, Is.Empty, $"[seed {seed} {s}]\n" + string.Join("\n", issues));
            }
            Assert.That(sawSqueeze && sawNoSqueeze, Is.True, "두 종류 상황이 모두 나와야 함");
        }

        [UnityTest]
        public IEnumerator 퀵_시뮬_경기의_모든_재생_위치에서_레이아웃이_유지되고_처음으로는_같은_경기로_돌아간다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadGeneratedScenario(Data.Matchup.Default, new SeededRandom(7));
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;
            var game = _controller.Generated;
            int awayBefore = game.Fixture.AwayScoreBefore, homeBefore = game.Fixture.HomeScoreBefore;
            int sceneRuns = game.Fixture.Branch.PathFor(game.Fixture.Events, TacticKind.Default).Sum(e => e.RunsScored);

            Assert.That(_controller.ChooseTactic(TacticKind.Default), Is.True);
            _controller.Playback.Pause();
            var playback = _controller.Playback;
            for (int index = -1; index < playback.Count; index++)
            {
                playback.SeekTo(index);
                yield return null;
                var issues = LayoutChecker.Check(Screen);
                Assert.That(issues, Is.Empty, $"[위치 {index}]\n" + string.Join("\n", issues));
            }
            Assert.That(_controller.FinalScore(), Is.EqualTo(new GameScore(awayBefore, homeBefore + sceneRuns)));

            Submit(root.Q<Button>("ctl-restart"));
            yield return null;
            Assert.That(_controller.Generated, Is.SameAs(game), "처음으로는 다시 굴리지 않음");
            Assert.That(_controller.ChosenTactic, Is.Null);
            Assert.That(_controller.Playback.Index, Is.EqualTo(-1));
            Assert.That(Text(root, "home-score"), Is.EqualTo(homeBefore.ToString()));
        }

        [UnityTest]
        public IEnumerator 원정_대진의_퀵_시뮬_경기는_9회초로_뒤집어_그린다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadGeneratedScenario(Data.Matchup.FromNames("부산 웨이브스", "인천 하버스", Data.TeamSide.Away), new SeededRandom(3));
            _controller.Playback.Pause();
            _controller.Playback.Restart();
            yield return null;

            var game = _controller.Generated;
            Assert.That(_controller.Fixture.Events[0].Inning, Is.EqualTo(9));
            Assert.That(_controller.Fixture.Events[0].HalfInning, Is.EqualTo(HalfInning.Top));
            Assert.That(Text(root, "away-score"), Is.EqualTo(game.Fixture.HomeScoreBefore.ToString()), "내 구단(원정) 점수");
            Assert.That(Text(root, "home-score"), Is.EqualTo(game.Fixture.AwayScoreBefore.ToString()));
            Assert.That(InningCells(root, 1).Take(8), Is.EqualTo(game.HomeRunsByInning.Select(r => r.ToString())));
            Assert.That(InningCells(root, 2).Take(8), Is.EqualTo(game.AwayRunsByInning.Select(r => r.ToString())));
            Assert.That(Text(root, "status-inning"), Does.StartWith("9회초 · "));

            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        // ── 인게임 Play-by-Play 순환 (Phase 14) ──

        /// <summary>3루 주자가 있는 승부처가 나올 때까지 Seed를 바꿔 퀵 시뮬 경기를 불러온다.</summary>
        void LoadGeneratedWithRunnerOnThird()
        {
            for (int seed = 1; seed < 100; seed++)
            {
                _controller.LoadGeneratedScenario(Data.Matchup.Default, new SeededRandom(seed));
                _controller.Playback.Pause();
                if (_controller.Generated.Situation.Runners.Third != null) return;
            }
            Assert.Fail("3루 주자 상황을 찾지 못함");
        }

        [UnityTest]
        public IEnumerator 퀵_시뮬_강공_안타_뒤_득점이_오르고_4번_타자가_들어서며_작전이_다시_켜진다()
        {
            var root = _doc.rootVisualElement;
            LoadGeneratedWithRunnerOnThird();
            var game = _controller.Generated;
            var s = game.Situation;
            int runs = (s.Runners.Second != null ? 1 : 0) + 1;
            _controller.Playback.SkipToEnd();
            yield return null;
            Assert.That(_controller.PlayByPlay, Is.True);
            Assert.That(Text(root, "batter-name"), Is.EqualTo(_controller.Roster.GetPlayer("home_b3").Name));

            // 0.0: 강공 성공 → 우전 적시타
            _controller.Playback.SetSpeed(4);
            Assert.That(_controller.ChooseTactic(TacticKind.SwingAway), Is.True);
            yield return WaitForDecision();
            yield return WaitForField();

            Assert.That(_controller.TacticResult, Is.EqualTo(TacticOutcome.Success));
            Assert.That(_controller.CurrentBranch, Is.Not.Null, "다음 타자 분기점");
            Assert.That(_controller.Events.Count(e => e.BatterId == "home_b4"), Is.EqualTo(2), "각본의 4번 타자 삼진 대신 1-1까지만");
            Assert.That(Text(root, "home-score"), Is.EqualTo((game.Fixture.HomeScoreBefore + runs).ToString()), "적시타 득점");
            Assert.That(InningCells(root, 2)[7], Is.EqualTo(runs.ToString()), "8회말 칸");
            Assert.That(Text(root, "batter-name"), Is.EqualTo(_controller.Roster.GetPlayer("home_b4").Name), "4번 타자 등장");
            var current = root.Q("lineup").Children().Where(r => r.ClassListContains("roster-row--current")).Select(r => (string)r.userData);
            Assert.That(current, Is.EqualTo(new[] { "home_b4" }), "좌측 타순 하이라이트");
            Assert.That(Text(root, "status-inning"), Is.EqualTo($"8회말 · {PitchPresentation.OutsText(s.Outs)} · {new BaseState(FixtureGenerator.Batter1, s.Runners.First, null)}"));
            Assert.That(Text(root, "headline-main"), Does.Contain("작전 선택 대기"));
            Assert.That(root.Q<Button>("tactic-default").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("tactic-swing").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("tactic-squeeze").enabledSelf, Is.False, "단타 뒤 3루가 비어 스퀴즈 없음");
            Assert.That(_controller.TacticSuccessRate(TacticKind.SwingAway), Is.InRange(0.05, 0.95));
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));

            // 로그(최근 5개)에서 작전 기록은 적시타 시점에 보인다. 다시 끝으로 가면 작전 대기로 돌아온다.
            int hitIndex = _controller.Events.Select((e, i) => (e, i)).First(x => x.e.PlayType == PlayType.Single).i;
            _controller.Playback.SeekTo(hitIndex);
            yield return null;
            Assert.That(root.Q("log").Children().OfType<Label>().Any(l => l.text == "감독 작전 · 강공 지시 · 성공"), Is.True);
            _controller.Playback.SkipToEnd();
            yield return null;
            Assert.That(_controller.Playback.AwaitingDecision, Is.True);

            // 두 번째 타석도 작전을 고를 수 있다
            Assert.That(_controller.ChooseTactic(TacticKind.SwingAway), Is.True);
            Assert.That(_controller.Decisions.Count, Is.EqualTo(2));
            Assert.That(_controller.Decisions[1].AfterIndex, Is.GreaterThan(_controller.Decisions[0].AfterIndex));
        }

        [UnityTest]
        public IEnumerator 퀵_시뮬_순환은_야구_규칙대로_9회말_연장까지_돌고_경기_종료로_이어진다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadGeneratedScenario(Data.Matchup.Default, new SeededRandom(7));
            _controller.TacticRandom = new ScriptedRandom(0.99); // 강공 실패 → 병살·땅볼, 이후 삼진
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;

            // Phase 19·20: 공격(강공) 3아웃 뒤 9회초 수비(기본 진행만), 홈이 앞서지 않으면 9회말·연장까지 이어진다.
            int guard = 0;
            while (_controller.CurrentBranch != null)
            {
                Assert.That(++guard, Is.LessThanOrEqualTo(2 * PlayByPlayGenerator.MaxInnings * PlayByPlayGenerator.MaxPlateAppearancesPerHalf),
                    "순환이 끝나지 않음");
                Assert.That(_controller.Playback.AwaitingDecision, Is.True);
                var tactic = _controller.AwaitingDefenseDecision ? TacticKind.Default : TacticKind.SwingAway;
                Assert.That(_controller.ChooseTactic(tactic), Is.True);
                _controller.Playback.Pause();
                _controller.Playback.SkipToEnd();
                yield return null;
            }
            yield return WaitForField();

            // 0.99: 강공 실패·기본 진행 범타만 → 9회초 이후 득점이 없다. 끝나는 반 이닝은 점수로 정해진다:
            // 홈 리드 → 9회초에서 종료, 원정 리드 → 9회말 뒤 종료, 동점 → 12회말까지 연장 뒤 무승부.
            var last = _controller.Events[_controller.Events.Count - 1];
            var final = _controller.FinalScore();
            var expectedEnd = final.Home > final.Away ? (9, HalfInning.Top)
                : final.Away > final.Home ? (9, HalfInning.Bottom) : (PlayByPlayGenerator.MaxInnings, HalfInning.Bottom);
            Assert.That(last.OutsAfter, Is.EqualTo(3));
            Assert.That((last.Inning, last.HalfInning), Is.EqualTo(expectedEnd), $"최종 {final}");
            Assert.That(_controller.Events.Any(e => e.Inning == 9 && e.HalfInning == HalfInning.Bottom),
                Is.EqualTo(final.Home <= final.Away), "홈이 9회초 뒤 앞서지 않으면 반드시 9회말 공격");
            Assert.That(_controller.DefenseStarted, Is.True);
            Assert.That(_controller.Playback.IsAtEnd, Is.True);
            Assert.That(_controller.Playback.AwaitingDecision, Is.False);
            Assert.That(_controller.InningEndAwaitingExit, Is.True);
            Assert.That(_controller.GameOver, Is.True);
            var offense = _controller.Decisions.Count(d => _controller.Events[d.AfterIndex].Inning == 8);
            Assert.That(offense, Is.InRange(1, 3), "8회말 공격 작전");
            Assert.That(_controller.Decisions.Count(d => _controller.Events[d.AfterIndex] is var e && e.Inning == 9 && e.HalfInning == HalfInning.Top),
                Is.EqualTo(3), "0.99 → 9회초 세 타자 범타");
            string half = $"{expectedEnd.Item1}회{(expectedEnd.Item2 == HalfInning.Top ? "초" : "말")}";
            Assert.That(Text(root, "status-inning"), Is.EqualTo($"{half} · 3아웃 · 이닝 종료"));
            Assert.That(Text(root, "headline-main"), Is.EqualTo($"{half} 종료 · 경기 종료 대기"));
            Assert.That(root.Q<Button>("tactic-default").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("tactic-swing").enabledSelf, Is.False);
            // 씬 단독 실행(로비 흐름 없음)이라 [경기 종료]는 기존처럼 꺼진다. 로비 복귀는 SceneFlow 테스트에서 확인.
            Assert.That(_controller.HasExitHandler, Is.False);
            Assert.That(root.Q<Button>("ctl-game-end").ClassListContains("btn--active"), Is.False);
            Assert.That(_controller.FinalScore(),
                Is.EqualTo(new GameScore(int.Parse(Text(root, "away-score")), int.Parse(Text(root, "home-score")))));
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));

            // 처음으로: 같은 경기 첫 승부처로 복귀
            Submit(root.Q<Button>("ctl-restart"));
            yield return null;
            Assert.That(_controller.Decisions, Is.Empty);
            Assert.That(_controller.CurrentBranch, Is.SameAs(_controller.Fixture.Branch));
            Assert.That(_controller.InningEndAwaitingExit, Is.False);
        }

        [UnityTest]
        public IEnumerator 고정_작전_장면은_순환하지_않고_각본대로_끝난다()
        {
            _controller.LoadScenario(ScenarioCatalog.EighthBottomTactics);
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;
            Assert.That(_controller.PlayByPlay, Is.False);
            Assert.That(_controller.ChooseTactic(TacticKind.SwingAway), Is.True);
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;
            Assert.That(_controller.CurrentBranch, Is.Null);
            Assert.That(_controller.Playback.IsAtEnd, Is.True);
            Assert.That(_controller.Events.Count, Is.EqualTo(Fixture8thBottom.Create().Events.Count), "각본 8구 그대로");
            Assert.That(_controller.InningEndAwaitingExit, Is.False);
        }

        /// <summary>퀵 시뮬 경기를 공격 강공 실패(0.99)로 넘겨 9회초 수비 첫 작전 대기까지 간다.</summary>
        IEnumerator ReachDefense()
        {
            _controller.LoadGeneratedScenario(Data.Matchup.Default, new SeededRandom(7));
            _controller.TacticRandom = new ScriptedRandom(0.99);
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;
            int guard = 0;
            while (!_controller.AwaitingDefenseDecision)
            {
                Assert.That(++guard, Is.LessThanOrEqualTo(PlayByPlayGenerator.MaxPlateAppearancesPerHalf), "수비 이닝에 닿지 않음");
                Assert.That(_controller.Playback.AwaitingDecision, Is.True);
                Assert.That(_controller.CanChangePitcher, Is.False, "공격 중에는 투수 교체 불가");
                Assert.That(_doc.rootVisualElement.Q<Button>("tactic-pitching").enabledSelf, Is.False);
                Assert.That(_controller.ChooseTactic(TacticKind.SwingAway), Is.True);
                _controller.Playback.Pause();
                _controller.Playback.SkipToEnd();
                yield return null;
            }
            yield return null;
        }

        static string[] BullpenNames(VisualElement root) =>
            root.Q("bullpen-list").Query<Label>(className: "bullpen-row__name").ToList().Select(l => l.text.Split(' ')[0]).ToArray();

        [UnityTest]
        public IEnumerator 수비_이닝에서_투수_교체로_불펜_투수를_올리면_투수_카드가_바로_바뀐다()
        {
            var root = _doc.rootVisualElement;
            yield return ReachDefense();
            var roster = _controller.Roster;
            var decision = _controller.Events[_controller.CurrentBranch.AfterIndex];
            // Phase 38: 퀵 시뮬 구간(1~8회초)에서 선발이 내려가고 더미 구원(마무리 제외)이 이어 던졌다 → 그 마지막 투수가 9회초 마운드
            var staff = _controller.Generated.MyStaff;
            string mound = staff.CurrentPitcherId;
            Assert.That(mound, Is.Not.EqualTo(GameScreenController.MyStarterId), "선발은 퀵 시뮬 구간에서 강판");
            Assert.That(_controller.MyPitching.CurrentPitcherId, Is.EqualTo(mound));
            Assert.That(_controller.MyPitching.UsedPitchers, Is.EqualTo(staff.Appearances.Take(staff.Appearances.Count - 1).Select(a => a.PitcherId)));
            Assert.That((decision.Inning, decision.HalfInning), Is.EqualTo((9, HalfInning.Top)));
            Assert.That(decision.PitcherId, Is.EqualTo(mound));
            Assert.That(decision.BatterId, Is.EqualTo(BattingOrder.IdFor("away_b1", _controller.Generated.AwayNinthLeadoffOrder)));
            Assert.That(Text(root, "headline-main"), Does.Contain("9회초 · 수비").And.Contain("작전 선택 대기"));
            Assert.That(Text(root, "pitcher-name"), Is.EqualTo(roster.GetPlayer(mound).Name));
            Assert.That(roster.GetPlayer(mound).PitchesBeforeScene, Is.EqualTo(staff.Find(mound).Pitches), "퀵 시뮬 가상 투구 수가 장면 전 투구 수");
            Assert.That(Text(root, "pitcher-fitness"), Is.EqualTo(TacticText.StaminaText(_controller.StateOf(mound))));
            Assert.That(root.Q<Button>("tactic-default").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("tactic-swing").enabledSelf, Is.False, "수비에는 강공 없음");
            Assert.That(root.Q<Button>("tactic-squeeze").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("tactic-pinch").enabledSelf, Is.False, "대타는 범위 밖");
            Assert.That(root.Q<Button>("tactic-pitching").enabledSelf, Is.True);

            // 툴팁: 지금 투수의 투구 수·체력과 출루 허용 확률
            _controller.PreviewTactic(TacticKind.Default);
            yield return null;
            var state = _controller.StateOf(mound);
            Assert.That(state.PitchCount, Is.EqualTo(roster.GetPlayer(mound).PitchesBeforeScene + 2));
            Assert.That(Text(root, "tactic-desc"), Does.StartWith(TacticText.DefenseDetail)
                .And.Contain($"투수 · {roster.GetPlayer(mound).Name} {state.PitchCount}구 · {TacticText.StaminaText(state)}")
                .And.Contain("출루 허용 확률"));
            _controller.PreviewTactic(null);

            // [투수 교체] → 불펜 모달: 투수만 (대타·대주자 야수 없음)
            Submit(root.Q<Button>("tactic-pitching"));
            yield return null;
            Assert.That(_controller.BullpenOpen, Is.True);
            Assert.That(root.Q("bullpen-modal").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            var bullpenIds = roster.GetBullpen(roster.MyTeam).Select(e => e.PlayerId).ToArray();
            string[] Names(IEnumerable<string> ids) => ids.Select(id => roster.GetPlayer(id).Name).ToArray();
            var bench = roster.GetBench(roster.MyTeam).Select(e => roster.GetPlayer(e.PlayerId).Name).ToArray();
            Assert.That(BullpenNames(root), Is.EqualTo(Names(bullpenIds.Where(id => id != mound))), "지금 투수 빼고 불펜 전원 (퀵 시뮬에서 던진 투수 포함)");
            Assert.That(BullpenNames(root).Intersect(bench), Is.Empty, "야수는 목록에 없다");
            Assert.That(Text(root, "bullpen-current"), Does.Contain(roster.GetPlayer(mound).Name));
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, "[불펜 모달]\n" + string.Join("\n", issues));

            // 취소하면 그대로
            Submit(root.Q<Button>("bullpen-cancel"));
            yield return null;
            Assert.That(_controller.BullpenOpen, Is.False);
            Assert.That(_controller.MyPitching.CurrentPitcherId, Is.EqualTo(mound));
            Assert.That(_controller.ChangePitcher(staff.Appearances[0].PitcherId), Is.False, "퀵 시뮬에서 강판된 선발은 재등판 불가");

            // 다시 열어 아껴 둔 마무리(home_rp1)를 고른다 → 투수 카드 즉시 교체, 지금 투수 강판
            Submit(root.Q<Button>("tactic-pitching"));
            yield return null;
            Submit(root.Q<Button>("bullpen-pick-home_rp1"));
            yield return null;
            Assert.That(_controller.BullpenOpen, Is.False);
            Assert.That(_controller.MyPitching.CurrentPitcherId, Is.EqualTo("home_rp1"));
            Assert.That(_controller.MyPitching.UsedPitchers, Is.EqualTo(staff.Appearances.Select(a => a.PitcherId)));
            Assert.That(Text(root, "pitcher-name"), Is.EqualTo(roster.GetPlayer("home_rp1").Name), "투수 카드 즉시 리렌더링");
            Assert.That(Text(root, "pitcher-fitness"), Is.EqualTo("체력 100%"));
            Assert.That(_controller.Playback.AwaitingDecision, Is.True, "교체 후에도 작전 대기");
            Assert.That(root.Q("log").Children().OfType<Label>().Select(l => l.text),
                Has.Member($"투수 교체 · {roster.GetPlayer(mound).Name} → {roster.GetPlayer("home_rp1").Name}"));
            var bullpenRow = root.Q("bullpen").Children().First(r => (string)r.userData == "home_rp1");
            Assert.That(bullpenRow.Q<Label>(className: "roster-state").text, Is.EqualTo("등판 중"));

            // 강판된 선발·야수·지금 투수는 올릴 수 없다
            Assert.That(_controller.ChangePitcher("home_sp"), Is.False, "강판된 투수 재등판 불가");
            Assert.That(_controller.ChangePitcher("home_ph1"), Is.False, "야수");
            Assert.That(_controller.ChangePitcher("home_rp1"), Is.False, "지금 투수");
            // 더미 불펜 3명(퀵 시뮬에서 구원 2명 + 마무리)을 모두 썼다 → 더 바꿀 투수가 없어 [투수 교체]가 닫힌다
            Assert.That(_controller.MyPitching.ReliefCandidates, Is.Empty);
            Assert.That(_controller.CanChangePitcher, Is.False);
            Assert.That(root.Q<Button>("tactic-pitching").enabledSelf, Is.False);
            Submit(root.Q<Button>("tactic-pitching"));
            yield return null;
            Assert.That(_controller.BullpenOpen, Is.False);

            // 기본 진행: 남은 투구는 새 투수가 던진다
            int at = _controller.CurrentBranch.AfterIndex;
            Assert.That(_controller.ChooseTactic(TacticKind.Default), Is.True);
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;
            Assert.That(_controller.Events.Skip(at + 1).Select(e => e.PitcherId), Is.All.EqualTo("home_rp1"));
            Assert.That(_controller.Events.Take(at + 1).Count(e => e.PitcherId == "home_rp1"), Is.Zero);
            issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator 처음으로_돌아가면_투수_교체도_초기화된다()
        {
            yield return ReachDefense();
            var staff = _controller.Generated.MyStaff;
            var quickSimUsed = staff.Appearances.Take(staff.Appearances.Count - 1).Select(a => a.PitcherId).ToArray();
            Assert.That(_controller.ChangePitcher("home_rp1"), Is.True);
            Assert.That(_controller.PitchingChanges, Has.Count.EqualTo(1));
            Submit(_doc.rootVisualElement.Q<Button>("ctl-restart"));
            yield return null;
            Assert.That(_controller.PitchingChanges, Is.Empty);
            Assert.That(_controller.MyPitching.CurrentPitcherId, Is.EqualTo(staff.CurrentPitcherId), "퀵 시뮬 구간 교체 상태로 돌아간다");
            Assert.That(_controller.MyPitching.UsedPitchers, Is.EqualTo(quickSimUsed));
            Assert.That(_controller.DefenseStarted, Is.False);
        }

        /// <summary>기본 진행 타석은 난수 2개(출루 판정 0.99 → 범타, 결과 0.1 → 삼진)를 쓴다 → 모든 타석 4구 삼진.</summary>
        static ScriptedRandom AllStrikeouts() => new ScriptedRandom(0.99, 0.1);

        IEnumerator PlayToDecisionEnd()
        {
            _controller.Playback.Pause();
            _controller.Playback.SkipToEnd();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 풀_이닝은_1회초부터_공수를_바꾸고_동점이면_12회말_무승부로_끝나며_CPU가_지친_선발을_바꾼다()
        {
            var root = _doc.rootVisualElement;
            _controller.LoadFullGame(Data.Matchup.Default); // 내 구단 = 홈 → 초는 내 수비, 말은 내 공격
            _controller.TacticRandom = AllStrikeouts();
            yield return PlayToDecisionEnd();

            Assert.That(_controller.IsFullGame, Is.True);
            Assert.That(_controller.PlayByPlay, Is.True);
            var first = _controller.Events[_controller.CurrentBranch.AfterIndex];
            Assert.That((first.Inning, first.HalfInning, first.BatterId, first.PitcherId), Is.EqualTo((1, HalfInning.Top, "away_b1", "home_sp")));
            Assert.That(_controller.AwaitingDefenseDecision, Is.True, "1회초는 내 수비");
            Assert.That(Text(root, "away-score") + ":" + Text(root, "home-score"), Is.EqualTo("0:0"));
            Assert.That(Text(root, "pitcher-line1"), Does.EndWith(" · 2구"), "풀 이닝은 투구 수 0에서 시작");

            int guard = 0;
            while (_controller.CurrentBranch != null)
            {
                Assert.That(++guard, Is.LessThanOrEqualTo(2 * PlayByPlayGenerator.MaxInnings * PlayByPlayGenerator.MaxPlateAppearancesPerHalf), "무한 루프");
                Assert.That(_controller.ChooseTactic(TacticKind.Default), Is.True);
                yield return PlayToDecisionEnd();
            }
            yield return WaitForField();

            var events = _controller.Events;
            var halves = events.Select(e => (e.Inning, e.HalfInning)).Distinct().ToList();
            Assert.That(halves, Has.Count.EqualTo(24), "1회초 ~ 12회말");
            Assert.That(halves.First(), Is.EqualTo((1, HalfInning.Top)));
            Assert.That(halves.Last(), Is.EqualTo((PlayByPlayGenerator.MaxInnings, HalfInning.Bottom)));
            foreach (var h in halves)
            {
                var pa = events.Where(e => (e.Inning, e.HalfInning) == h && e.Outcome != PlateAppearanceOutcome.None).ToList();
                Assert.That(pa.Select(e => e.Outcome), Is.EqualTo(Enumerable.Repeat(PlateAppearanceOutcome.Strikeout, 3)), $"{h} 삼자 삼진");
                bool userBatting = h.HalfInning == HalfInning.Bottom;
                Assert.That(pa.All(e => e.BatterId.StartsWith(userBatting ? "home_b" : "away_b")), Is.True, $"{h} 공격 팀");
            }
            Assert.That(events.Where(e => e.HalfInning == HalfInning.Top && e.Outcome != PlateAppearanceOutcome.None)
                .Select(e => e.BatterId).Take(10), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 1 }.Select(n => "away_b" + n)), "타순이 이닝을 넘어 이어진다");
            Assert.That(_controller.FinalScore(), Is.EqualTo(new GameScore(0, 0)), "12회 동점 → 무승부");
            Assert.That(_controller.GameOver, Is.True);
            Assert.That(_controller.InningEndAwaitingExit, Is.True);
            Assert.That(Text(root, "headline-main"), Is.EqualTo("12회말 종료 · 경기 종료 대기"));

            // CPU: 말마다 12구 → 9회말까지 108구로 고갈 → 10회말 시작 전에 마무리(9회 이후 우선)로 교체.
            var cpuChanges = _controller.PitchingChanges.Where(c => c.ByCpu).ToList();
            Assert.That(cpuChanges, Has.Count.EqualTo(1));
            var change = cpuChanges[0];
            Assert.That((change.From, change.To, change.Reason), Is.EqualTo(("away_sp", "away_rp2", "체력 고갈")));
            Assert.That(events[change.AfterIndex].Inning, Is.EqualTo(10));
            Assert.That(events[change.AfterIndex].HalfInning, Is.EqualTo(HalfInning.Top), "10회초 마지막 투구 뒤(이닝 교대)");
            Assert.That(events.Where(e => e.HalfInning == HalfInning.Bottom && e.Inning <= 9).Select(e => e.PitcherId).Distinct(), Is.EqualTo(new[] { "away_sp" }));
            Assert.That(events.Where(e => e.HalfInning == HalfInning.Bottom && e.Inning >= 10).Select(e => e.PitcherId).Distinct(), Is.EqualTo(new[] { "away_rp2" }));
            Assert.That(_controller.CpuPitching.UsedPitchers, Is.EqualTo(new[] { "away_sp" }));
            Assert.That(_controller.MyPitching.CurrentPitcherId, Is.EqualTo("home_sp"), "내 투수는 자동으로 바꾸지 않는다");

            // 12회까지 늘어난 스코어보드도 화면에 맞는다.
            Assert.That(root.Q("linescore").Children().First().Query<Label>().ToList().Select(l => l.text), Has.Member("12"), "12회 칸");
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));

            // 10회말 첫 투구: 투수 카드 = 새 CPU 투수, 중계 로그에 교체 기록
            int firstRelief = events.Select((e, i) => (e, i)).First(x => x.e.PitcherId == "away_rp2").i;
            _controller.Playback.SeekTo(firstRelief);
            yield return null;
            var roster = _controller.Roster;
            Assert.That(Text(root, "pitcher-name"), Is.EqualTo(roster.GetPlayer("away_rp2").Name));
            Assert.That(root.Q("log").Children().OfType<Label>().Select(l => l.text),
                Has.Member($"상대 투수 교체 · {roster.GetPlayer("away_sp").Name} → {roster.GetPlayer("away_rp2").Name} (체력 고갈)"));
            _controller.Playback.SeekTo(firstRelief - 1);
            yield return null;
            Assert.That(root.Q("log").Children().OfType<Label>().Select(l => l.text), Has.None.StartsWith("상대 투수 교체"),
                "교체 전(10회초 마지막 투구)에는 아직 안 보인다");
        }

        [UnityTest]
        public IEnumerator 같은_난수면_풀_이닝_경기도_같은_경로가_나온다()
        {
            var runs = new List<string>();
            for (int round = 0; round < 2; round++)
            {
                _controller.LoadFullGame(Data.Matchup.Default);
                _controller.TacticRandom = new SeededRandom(20261016);
                yield return PlayToDecisionEnd();
                int guard = 0;
                while (_controller.CurrentBranch != null && ++guard < 400)
                {
                    var tactic = _controller.CurrentBranch.Offers(TacticKind.SwingAway) && guard % 3 == 0 ? TacticKind.SwingAway : TacticKind.Default;
                    Assert.That(_controller.ChooseTactic(tactic), Is.True);
                    _controller.Playback.Pause();
                    _controller.Playback.SkipToEnd();
                }
                Assert.That(_controller.GameOver, Is.True);
                runs.Add(string.Join("|", _controller.Events.Select(e => $"{e.Inning}{e.HalfInning}{e.PitcherId}{e.BatterId}{e.Result}{e.RunsScored}")) +
                         _controller.FinalScore());
            }
            Assert.That(runs[1], Is.EqualTo(runs[0]));
            yield return null;
        }

        [UnityTest]
        public IEnumerator 이닝_종료는_작전_대기에서_남은_타석을_기본_진행으로_처리하고_다음_반_이닝에서_멈춘다()
        {
            _controller.LoadFullGame(Data.Matchup.Default);
            _controller.TacticRandom = AllStrikeouts();
            _controller.Playback.SetSpeed(4);
            yield return PlayToDecisionEnd();
            var inningEnd = _doc.rootVisualElement.Q<Button>("ctl-inning-end");
            Assert.That(inningEnd.enabledSelf, Is.True, "작전 대기 중에도 [이닝 종료] 가능");

            Submit(inningEnd);
            yield return null;
            Assert.That(_controller.Decisions.Count, Is.EqualTo(3), "1회초 세 타석");
            Assert.That(_controller.Decisions.All(d => d.Tactic == TacticKind.Default), Is.True);
            var shown = _controller.Events[_controller.Playback.Index];
            Assert.That((shown.Inning, shown.HalfInning, shown.OutsAfter), Is.EqualTo((1, HalfInning.Top, 3)), "1회초 마지막 투구로 이동");
            yield return WaitForDecision();
            var next = _controller.Events[_controller.CurrentBranch.AfterIndex];
            Assert.That((next.Inning, next.HalfInning, next.BatterId), Is.EqualTo((1, HalfInning.Bottom, "home_b1")), "1회말 내 공격 선두");
            Assert.That(_controller.AwaitingDefenseDecision, Is.False);
            Assert.That(_doc.rootVisualElement.Q<Button>("tactic-swing").enabledSelf, Is.True, "공격에는 강공");
        }
    }
}
