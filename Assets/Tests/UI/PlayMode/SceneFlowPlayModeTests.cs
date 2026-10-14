using System.Collections;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>로비 ↔ 경기 화면 왕복과 SeasonManager 수명 검증.</summary>
    public class SceneFlowPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyManager();
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForScene(SeasonManager.LobbyScene);
            // 타 구단 결과는 고정 Seed로만 만든다.
            SeasonManager.Instance.LeagueRandom = new SeededRandom(20261014);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyManager();
            yield return null;
        }

        static void DestroyManager()
        {
            if (SeasonManager.Instance != null) Object.DestroyImmediate(SeasonManager.Instance.gameObject);
        }

        static IEnumerator WaitForScene(string name)
        {
            float timeout = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name), $"{name} 씬으로 전환되지 않음");
            yield return null;
            yield return null;
        }

        static VisualElement LobbyRoot() =>
            Object.FindAnyObjectByType<LobbyController>().GetComponent<UIDocument>().rootVisualElement;

        static void Submit(Button button)
        {
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        [UnityTest]
        public IEnumerator 로비는_일정_순위_라인업과_다음_경기를_표시하고_1920x1080에_맞는다()
        {
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            Assert.That(lobby, Is.Not.Null);
            Assert.That(SeasonManager.Instance, Is.Not.Null, "로비 진입 시 SeasonManager 생성");

            var doc = lobby.GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panelCopy = Object.Instantiate(doc.panelSettings);
            panelCopy.targetTexture = rt;
            doc.panelSettings = panelCopy;
            yield return null;
            yield return null;

            var root = doc.rootVisualElement;
            Assert.That(root.Q("schedule").childCount, Is.EqualTo(10));
            Assert.That(root.Q("standings").childCount, Is.EqualTo(10));
            Assert.That(root.Q("roster").childCount, Is.EqualTo(9));
            Assert.That(root.Q<Label>("next-game").text, Does.StartWith("경기 1 · "));
            Assert.That(root.Q<Button>("start-game").enabledSelf, Is.True);

            var issues = LayoutChecker.Check(root.Q("screen"));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));

            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panelCopy);
        }

        [UnityTest]
        public IEnumerator 로비와_경기_화면을_세_번_왕복해도_상태가_이어지고_중복이_없다()
        {
            for (int round = 1; round <= 3; round++)
            {
                var lobbyRoot = LobbyRoot();
                Assert.That(lobbyRoot.Q<Label>("next-game").text, Does.StartWith($"경기 {round} · "), $"{round}회차 로비");

                Submit(lobbyRoot.Q<Button>("start-game"));
                Submit(lobbyRoot.Q<Button>("start-game")); // 연타해도 한 번만 시작
                yield return WaitForScene(SeasonManager.GameScene);

                var manager = SeasonManager.Instance;
                Assert.That(Object.FindObjectsByType<SeasonManager>().Length, Is.EqualTo(1));
                Assert.That(manager.Season.InProgress.Number, Is.EqualTo(round));

                var game = Object.FindAnyObjectByType<GameScreenController>();
                Assert.That(game, Is.Not.Null);
                Assert.That(game.HasExitHandler, Is.True, "SeasonManager가 경기 종료를 구독");

                // 자동 재생을 끝까지 보낸 뒤 [경기 종료]
                game.Playback.SetSpeed(4);
                game.Playback.SkipToEnd();
                yield return null;
                var exit = game.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ctl-game-end");
                Assert.That(exit.enabledSelf, Is.True, "장면 끝에서도 경기 종료 가능");
                Submit(exit);
                Submit(exit); // 연타해도 한 번만 처리
                yield return WaitForScene(SeasonManager.LobbyScene);

                Assert.That(manager.ExitsHandled, Is.EqualTo(round), "종료 요청이 회차당 정확히 한 번 처리됨");
                Assert.That(manager.Season.InProgress, Is.Null);
                Assert.That(manager.Season.GamesPlayed, Is.EqualTo(round));
                Assert.That(LobbyRoot().Q("game-" + round).ClassListContains("lobby-row--final"), Is.True);
            }

            Assert.That(LobbyRoot().Q<Label>("my-record").text, Does.Contain("진행 3 / 10경기"));
        }

        /// <summary>로비에서 경기를 시작해 승부처까지 간 뒤, 고정 난수로 작전을 고르고 끝까지 보낸 다음 [경기 종료].</summary>
        static IEnumerator PlayGame(TacticKind? tactic, double roll)
        {
            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            Assert.That(game.ScenarioId, Is.EqualTo(SeasonManager.SeasonGameScenario), "시즌 경기는 작전 승부처 장면");
            game.TacticRandom = new ScriptedRandom(roll);
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            Assert.That(game.Playback.AwaitingDecision, Is.True);
            if (tactic.HasValue)
            {
                Assert.That(game.ChooseTactic(tactic.Value), Is.True);
                game.Playback.Pause();
                game.Playback.SkipToEnd();
            }
            yield return null;
            Submit(game.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ctl-game-end"));
            yield return WaitForScene(SeasonManager.LobbyScene);
        }

        static string[] StandingRowTexts(VisualElement root, int index) =>
            root.Q("standings").ElementAt(index).Query<Label>().ToList().Select(l => l.text).ToArray();

        [UnityTest]
        public IEnumerator 스퀴즈_실패로_진_경기는_패로_기록되고_로비_순위표가_갱신된다()
        {
            var season = SeasonManager.Instance.Season;
            var me = season.MyStanding;
            var (w, l, d) = (me.Wins, me.Losses, me.Draws);

            yield return PlayGame(TacticKind.SqueezeBunt, 0.99);

            Assert.That(SeasonManager.Instance.LastScore, Is.EqualTo(new GameScore(3, 2)), "원정 3 : 홈 2");
            Assert.That((me.Wins, me.Losses, me.Draws), Is.EqualTo((w, l + 1, d)), "홈 구단 패");
            Assert.That(season.LastDayResults, Has.Count.EqualTo(5), "타 구단 4경기도 반영");

            var root = LobbyRoot();
            Assert.That(root.Q("game-1").Q<Label>(className: "col-status").text, Is.EqualTo("패 3:2"));
            Assert.That(root.Q<Label>("my-record").text, Does.StartWith($"{me.Rank}위 · {me.Wins}승 {me.Losses}패 {me.Draws}무 · 승률 {me.WinPctText}"));

            // 순위표 UI가 재정렬된 SeasonState와 행 단위로 일치하고, 승률 내림차순이다.
            for (int i = 0; i < season.Standings.Count; i++)
            {
                var row = season.Standings[i];
                Assert.That(StandingRowTexts(root, i),
                    Is.EqualTo(new[] { row.Rank.ToString(), row.Team, row.Wins.ToString(), row.Losses.ToString(),
                        row.Draws.ToString(), row.WinPctText, row.GamesBehind }), $"{i + 1}번째 행");
                if (i > 0) Assert.That(season.Standings[i - 1].WinPct, Is.GreaterThanOrEqualTo(row.WinPct));
            }
            Assert.That(root.Q("standings").Children().Count(r => r.ClassListContains("lobby-row--mine")), Is.EqualTo(1));

            // 갱신된 로비도 1920×1080 안에 맞는다 (PanelSettings 복제본에 RenderTexture 연결).
            var doc = Object.FindAnyObjectByType<LobbyController>().GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panelCopy = Object.Instantiate(doc.panelSettings);
            panelCopy.targetTexture = rt;
            doc.panelSettings = panelCopy;
            yield return null;
            yield return null;
            var issues = LayoutChecker.Check(doc.rootVisualElement.Q("screen"));
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panelCopy);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator 작전_없이_끝낸_경기는_무승부_원정_경기_점수는_일정_기준으로_뒤집어_반영된다()
        {
            var me = SeasonManager.Instance.Season.MyStanding;
            var (l, d) = (me.Losses, me.Draws);

            yield return PlayGame(null, 0.99);
            Assert.That(SeasonManager.Instance.LastScore, Is.EqualTo(new GameScore(3, 3)), "기본 진행 = 적시타 동점");
            Assert.That(LobbyRoot().Q("game-1").Q<Label>(className: "col-status").text, Is.EqualTo("무 3:3"));
            Assert.That(me.Draws, Is.EqualTo(d + 1));

            // 2경기: 부산(원정) vs 인천(홈). 화면은 부산을 홈으로 그리므로 화면 3:2(부산 2점)는 일정 기준 2:3.
            yield return PlayGame(TacticKind.SqueezeBunt, 0.99);
            Assert.That(SeasonManager.Instance.LastScore, Is.EqualTo(new GameScore(2, 3)));
            Assert.That(LobbyRoot().Q("game-2").Q<Label>(className: "col-status").text, Is.EqualTo("패 2:3"));
            Assert.That(me.Losses, Is.EqualTo(l + 1));
        }

        [UnityTest]
        public IEnumerator 경기_화면_단독_실행에서는_경기_종료가_장면_끝으로_이동한다()
        {
            DestroyManager();
            SceneManager.LoadScene(SeasonManager.GameScene);
            yield return WaitForScene(SeasonManager.GameScene);

            var game = Object.FindAnyObjectByType<GameScreenController>();
            Assert.That(game.HasExitHandler, Is.False);
            game.Playback.Pause();
            Submit(game.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ctl-game-end"));
            yield return null;
            Assert.That(game.Playback.IsAtEnd, Is.True);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SeasonManager.GameScene), "씬은 그대로");
        }
    }
}
