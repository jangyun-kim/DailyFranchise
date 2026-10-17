using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
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
        /// <summary>실제 저장 파일(persistentDataPath)을 건드리지 않도록 테스트 전용 경로를 쓴다.</summary>
        static readonly string TestSavePath = Path.Combine(Application.temporaryCachePath, "playmode_" + SaveManager.FileName);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyManager();
            SeasonManager.SavePathOverride = TestSavePath;
            SeasonManager.InjuryRandomOverride = new DailyFranchise.Sim.ScriptedRandom(0.999); // 우연한 부상 없이 (Phase 36)
            SeasonManager.LoadDatabaseSynchronously = true; // 로비 진입 즉시 시작 버튼이 열리도록
            DeleteSave();
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForScene(SeasonManager.LobbyScene);
            // 타 구단 결과는 고정 Seed로만 만든다.
            SeasonManager.Instance.LeagueRandom = new SeededRandom(20261014);
            // 점수를 확인하는 기존 시나리오 테스트는 고정 장면(원정 3 : 홈 2, 1사 1·3루)으로 돌린다.
            // 퀵 시뮬레이션 경기는 아래 Phase 13 테스트가 따로 켠다.
            SeasonManager.Instance.UseGeneratedScenario = false;
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

        /// <summary>Play 모드를 껐다 다시 켠 것처럼 SeasonManager를 없애고 로비부터 다시 시작한다.</summary>
        static IEnumerator RestartFromLobby()
        {
            DestroyManager();
            yield return null;
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForScene(SeasonManager.LobbyScene);
            SeasonManager.Instance.LeagueRandom = new SeededRandom(20261015);
            SeasonManager.Instance.UseGeneratedScenario = false;
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

        [UnityTest]
        public IEnumerator Phase27_일정을_모두_치르면_시즌_결산으로_2027_시즌이_되고_예산은_남는다()
        {
            var manager = SeasonManager.Instance;
            int total = manager.Season.Schedule.Count;
            for (int i = 0; i < total - 1; i++)
            {
                manager.Season.StartNextGame();
                manager.AdvanceSeason(manager.Season.ScheduleScore(4, 2));
            }
            // 마지막 경기는 실제 화면 흐름으로 치르고 로비로 돌아온다.
            yield return PlayGame(null, 0.5);

            var root = LobbyRoot();
            var start = root.Q<Button>("start-game");
            Assert.That(manager.Season.IsSeasonFinished, Is.True);
            // Phase 32: 정규시즌 뒤에는 한국시리즈 — 나가면 1차전 버튼, 탈락이면 결과 시뮬레이션 버튼
            Assert.That(manager.Season.Phase, Is.EqualTo(SeasonPhase.PostSeason));
            Assert.That(root.Q<Label>("season-title").text, Is.EqualTo("2026 시즌 · 한국시리즈"));
            Assert.That(start.text, Is.EqualTo(manager.Season.InPostseason ? "한국시리즈 1차전 시작" : LobbyController.PostseasonSkipButtonText));
            FinishPostseason(manager);
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            lobby.enabled = false;
            lobby.enabled = true;
            yield return null;
            root = LobbyRoot();
            start = root.Q<Button>("start-game");
            Assert.That(root.Q<Label>("season-title").text, Is.EqualTo("2026 시즌 · 시즌 종료"));
            Assert.That(start.text, Is.EqualTo("시즌 결산 및 2027 시즌 준비"));
            Assert.That(start.enabledSelf, Is.True);
            Assert.That(start.ClassListContains("btn--offseason"), Is.True);
            Assert.That(root.Q<Label>("next-game").text, Does.StartWith("2026 한국시리즈 ").And.Contains(" 우승. 결산 후 2027 시즌"));
            Assert.That(manager.StartNextGame(), Is.False, "오프시즌에는 인게임 진입 차단");

            long budget = manager.Season.Budget;
            int fan = manager.Season.FanApproval;
            int trust = manager.Season.TrustPoints;
            int signings = manager.Season.Signings.Count;
            var me = manager.Season.MyStanding;
            string final = $"2026 시즌 최종 {me.Rank}위 · {me.Wins}승 {me.Losses}패";

            Submit(start);
            yield return null;

            Assert.That(manager.Season.Year, Is.EqualTo(2027));
            Assert.That(root.Q<Label>("season-title").text, Is.EqualTo("2027 시즌 · 5월"));
            Assert.That(root.Q<Label>("my-record").text, Does.StartWith("1위 · 0승 0패 0무").And.Contains("진행 0 / 10경기"));
            for (int i = 0; i < 10; i++)
                Assert.That(StandingRowTexts(root, i).Skip(3).Take(3), Is.EqualTo(new[] { "0", "0", "0" }), $"순위표 {i + 1}행");
            Assert.That(root.Q<Label>("stat-l10").text, Is.EqualTo("—"));
            Assert.That(root.Q<Label>("last-result").text, Does.StartWith(final));
            Assert.That(root.Q<Label>("next-game").text, Does.StartWith("경기 1 · 5.15 (토) · LG 트윈스 vs 삼성 라이온즈"));
            Assert.That(start.text, Is.EqualTo("다음 경기 시작"));
            Assert.That(start.ClassListContains("btn--offseason"), Is.False);
            Assert.That(start.enabledSelf, Is.True);
            Assert.That((manager.Season.Budget, manager.Season.FanApproval, manager.Season.TrustPoints, manager.Season.Signings.Count),
                Is.EqualTo((budget, fan, Mathf.Min(trust, FrontOfficeTrust.CarryOver), signings)), "예산·팬·TP·이적 기록 이월");

            // 즉시 저장: 껐다 켜도 2027 시즌 첫 경기부터.
            var saved = SaveManager.FromJson(File.ReadAllText(TestSavePath));
            Assert.That((saved.Year, saved.GamesPlayed, saved.Budget), Is.EqualTo((2027, 0, budget)));
            yield return RestartFromLobby();
            Assert.That(LobbyRoot().Q<Label>("season-title").text, Is.EqualTo("2027 시즌 · 5월"));

            // 새 시즌 첫 경기도 그대로 치른다.
            yield return PlayGame(null, 0.5);
            Assert.That(SeasonManager.Instance.Season.GamesPlayed, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Phase29_시즌_결산_뒤_은퇴_뉴스가_뜨고_라인업_나이가_한_살씩_오른다()
        {
            var manager = SeasonManager.Instance;
            manager.AgingRandom = new SeededRandom(2027);
            var db = manager.Database;
            while (!manager.Season.IsSeasonFinished)
            {
                manager.Season.StartNextGame();
                manager.AdvanceSeason(manager.Season.ScheduleScore(4, 2));
            }
            FinishPostseason(manager); // Phase 32: 한국시리즈까지 끝내야 결산
            yield return RestartFromLobby();
            manager = SeasonManager.Instance;
            manager.AgingRandom = new SeededRandom(2027);
            var root = LobbyRoot();
            var lineupBefore = manager.MyRoster.Lineup.Select(p => p.Id).ToList();
            Assert.That(root.Q("lineup-1").Q<Label>(className: "col-age").text, Is.EqualTo(db.FindPlayer(lineupBefore[0]).Age + "세"));

            Submit(root.Q<Button>("start-game")); // [시즌 결산 및 2027 시즌 준비]
            yield return null;

            Assert.That(manager.Season.Year, Is.EqualTo(2027));
            var report = manager.LastRollover.Aging;
            Assert.That(report.Retired.Select(r => r.Player.Name), Does.Contain("오승환"));
            var newsTexts = Enumerable.Range(0, LobbyController.NewsShown)
                .Select(i => root.Q("news-" + i)).Where(r => r != null)
                .Select(r => string.Join(" ", r.Query<Label>().ToList().Select(l => l.text))).ToList();
            Assert.That(newsTexts.Any(t => t.StartsWith("[은퇴] ") && t.Contains("정든 그라운드를 떠나 현역 은퇴 선언!")), Is.True, string.Join(" | ", newsTexts));
            Assert.That(root.Q<Label>("last-result").text, Does.Contain($"은퇴 {report.Retired.Count}명"));

            // 라인업·벤치 나이: 원본 + 1 (마스터 DB는 그대로)
            var mine = manager.MyRoster;
            for (int i = 0; i < mine.Lineup.Count; i++)
            {
                var p = mine.Lineup[i];
                Assert.That(root.Q("lineup-" + (i + 1)).Q<Label>(className: "col-age").text, Is.EqualTo((db.FindPlayer(p.Id).Age + 1) + "세"), p.Name);
            }
            for (int i = 0; i < mine.Bench.Count; i++)
                Assert.That(root.Q("bench-" + (i + 1)).Q<Label>(className: "col-age").text, Is.EqualTo((db.FindPlayer(mine.Bench[i].Id).Age + 1) + "세"));
            Assert.That(manager.Rosters.Find(db.Players.First(p => p.Name == "오승환").Id).player, Is.Null, "은퇴 선수는 장부에서 빠진다");

            // 재시작해도 은퇴·나이 유지
            yield return RestartFromLobby();
            Assert.That(SeasonManager.Instance.Rosters.Find(db.Players.First(p => p.Name == "오승환").Id).player, Is.Null);
            Assert.That(SeasonManager.Instance.MyRoster.Lineup[0].Age, Is.EqualTo(SeasonManager.Instance.Database.FindPlayer(SeasonManager.Instance.MyRoster.Lineup[0].Id).Age + 1));
        }

        [UnityTest]
        public IEnumerator Phase30_결산_뒤_퓨처스_신인이_로비에_보이고_주전_포수를_방출하면_포수가_포수_자리를_메운다()
        {
            var manager = SeasonManager.Instance;
            while (!manager.Season.IsSeasonFinished)
            {
                manager.Season.StartNextGame();
                manager.AdvanceSeason(manager.Season.ScheduleScore(4, 2));
            }
            FinishPostseason(manager); // Phase 32: 한국시리즈까지 끝내야 결산
            yield return RestartFromLobby();
            manager = SeasonManager.Instance;
            manager.AgingRandom = new SeededRandom(3030);
            var root = LobbyRoot();
            Assert.That(root.Q<Label>("futures-rookies").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "결산 전에는 신인 없음");

            Submit(root.Q<Button>("start-game")); // [시즌 결산 및 2027 시즌 준비]
            yield return null;

            // 1) 퓨처스 신인 3~5명 (18~22세)이 로비 한 줄에, 드래프트 뉴스가 뉴스 패널에
            var rookies = manager.Season.Rookies;
            Assert.That(rookies.Count, Is.InRange(RookieDraft.MinPicks, RookieDraft.MaxPicks));
            Assert.That(rookies.All(r => r.Age >= 18 && r.Age <= 22), Is.True);
            var futures = root.Q<Label>("futures-rookies");
            Assert.That(futures.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(futures.text, Is.EqualTo($"퓨처스 신인 {rookies.Count}명 · {rookies[0].Name} {rookies[0].Position} {rookies[0].Age}세 외 {rookies.Count - 1}명"));
            foreach (var r in rookies) Assert.That(futures.tooltip, Does.Contain($"{r.Name} ({r.Memo})").And.Contain($"{r.Age}세"));
            var newsTexts = Enumerable.Range(0, LobbyController.NewsShown)
                .Select(i => root.Q("news-" + i)).Where(r => r != null)
                .Select(r => string.Join(" ", r.Query<Label>().ToList().Select(l => l.text))).ToList();
            Assert.That(newsTexts.Any(t => t.StartsWith($"[드래프트] 삼성 라이온즈, 1라운드에서 특급 유망주 {rookies[0].Name}(")), Is.True,
                string.Join(" | ", newsTexts));
            Assert.That(root.Q<Label>("last-result").text, Does.EndWith($"신인 {rookies.Count}명"));

            // 1920×1080에서 신인 줄·선발 타자 [방출] 버튼이 패널을 넘치지 않는다
            var doc = Object.FindAnyObjectByType<LobbyController>().GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panel = Object.Instantiate(doc.panelSettings);
            panel.targetTexture = rt;
            doc.panelSettings = panel;
            yield return null;
            yield return null;
            root = LobbyRoot();
            var issues = LayoutChecker.Check(root.Q("screen"));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));

            // 2) 주전 포수 [방출] → 포수 자리는 포수가 (외야수 김헌곤이 아니라)
            var mine = manager.MyRoster;
            var catcher = mine.Lineup.Single(p => p.Position == "C");
            Click(root.Q("lineup-" + catcher.Order)); // Phase 31: 방출은 선수 상세에서 두 번 눌러 확정
            yield return null;
            var release = root.Q<Button>("pd-release");
            Assert.That(release.enabledSelf, Is.True, release.tooltip);
            Submit(release);
            Submit(release);
            yield return null;

            var filler = manager.MyRoster.AtOrder(catcher.Order);
            Assert.That(filler.Id, Is.Not.EqualTo(catcher.Id));
            Assert.That(filler.Position, Is.EqualTo("C"));
            var original = manager.Rosters.Find(filler.Id).player ?? manager.Season.Rookies.FirstOrDefault(r => r.Id == filler.Id);
            Assert.That(original?.Position ?? "C", Is.EqualTo("C"), $"{filler.Name}의 원래 위치도 포수");
            Assert.That(filler.Name, Is.Not.EqualTo("김헌곤"));
            Assert.That(root.Q<Label>("lineup-hint").text, Does.Contain($"포수 자리: {filler.Name}"));
            Assert.That(root.Q("lineup-" + catcher.Order).Q<Label>(className: "col-name").text, Is.EqualTo(filler.Name));
            Assert.That(manager.RosterProblem, Is.Null, "경기를 치를 수 있다");

            // 3) 재시작해도 신인·방출이 남는다
            var ids = rookies.Select(r => r.Id).ToList();
            yield return RestartFromLobby();
            Assert.That(SeasonManager.Instance.Season.Rookies.Select(r => r.Id), Is.EqualTo(ids));
            Assert.That(SeasonManager.Instance.MyRoster.AtOrder(catcher.Order).Id, Is.EqualTo(filler.Id));
            Assert.That(SeasonManager.Instance.Rosters.Get("삼성 라이온즈").Reserves.Count(RookieGenerator.IsRookie), Is.EqualTo(ids.Count));
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panel);
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

        /// <summary>카드 지표 칩 "라벨 값" 목록.</summary>
        static string[] ChipTexts(VisualElement root, string container) =>
            root.Q(container).Query(className: "stat-chip").ToList()
                .Select(c => string.Join(" ", c.Query<Label>().ToList().Select(l => l.text))).ToArray();

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
            Assert.That(root.Q<Label>("last-result").text, Does.StartWith("경기 1 · LG 트윈스 vs 삼성 라이온즈 · 패 3:2 · "));
            Assert.That(SeasonManager.Instance.LastReport.MyOutcome, Is.EqualTo(GameOutcome.Loss));
            Assert.That(SeasonManager.Instance.LastReport.Saved, Is.True, "로비 복귀 전에 저장");
            Assert.That(root.Q<Label>("next-game").text, Does.StartWith("경기 2 · "), "일정 전진");

            // 대시보드: 이번 경기 득실점(홈 2 : 원정 3)이 시즌 누적에 더해져 바로 보인다.
            Assert.That((SeasonManager.Instance.LastReport.MyRunsScored, SeasonManager.Instance.LastReport.MyRunsAllowed), Is.EqualTo((2, 3)));
            Assert.That(root.Q<Label>("stat-rs").text, Is.EqualTo(me.RunsScored.ToString()));
            Assert.That(root.Q<Label>("stat-ra").text, Is.EqualTo(me.RunsAllowed.ToString()));
            Assert.That(root.Q<Label>("stat-diff").text, Is.EqualTo(me.RunDifferentialText));
            Assert.That(root.Q<Label>("stat-l10").text, Is.EqualTo("0승 1패"));
            // Phase 37: 이번 시즌 기록으로 — 타율·홈런·타점 | 다승·평균자책·세이브 (2열 6칸), 방금 경기 박스스코어가 쌓였다
            Assert.That(root.Q("team-leaders").childCount, Is.EqualTo(6), "타율·홈런·타점·다승·평균자책·세이브 1위");
            Assert.That(root.Q<Label>("leaders-title").text, Is.EqualTo($"팀 내 기록 1위 · {season.Year} 시즌 1경기"));
            Assert.That(SeasonManager.Instance.LastReport.Box, Is.Not.Null, "실제 경기 화면을 거치면 박스스코어가 온다");
            Assert.That(season.Stats.Batters.Values.Sum(b => b.PlateAppearances), Is.GreaterThan(0));
            var lobbyView = Object.FindAnyObjectByType<LobbyController>();
            var leadoff = SeasonManager.Instance.MyRoster.Lineup
                .Select(p => season.Stats.BatterOf(p.Id)).OrderByDescending(b => b.PlateAppearances).First();
            Assert.That(leadoff.HasRecord, Is.True);
            Assert.That(lobbyView.OpenPlayer(leadoff.PlayerId), Is.True);
            Assert.That(root.Q("pd-stat-0").Q<Label>(className: "pd-stat__value").text, Is.EqualTo(SeasonStatsText.Rate(leadoff.Average, leadoff.AtBats > 0)),
                "선수 상세 타율 타일 = 이번 시즌 누적");
            Assert.That(root.Q("pd-stat-5").Q<Label>(className: "pd-stat__value").text, Is.EqualTo(leadoff.PlateAppearances.ToString()));
            Assert.That(root.Q<Label>("pd-season").text, Does.StartWith($"{season.Year} 시즌 1경기 · {leadoff.PlateAppearances}타석 · 지난 시즌 · "));
            lobbyView.PlayerDetail.Close();
            Assert.That(root.Q<Label>("my-record").text, Does.StartWith($"{me.Rank}위 · {me.Wins}승 {me.Losses}패 {me.Draws}무 · 승률 {me.WinPctText}"));

            // 순위표 UI가 재정렬된 SeasonState와 행 단위로 일치하고, 승률 내림차순이다.
            for (int i = 0; i < season.Standings.Count; i++)
            {
                var row = season.Standings[i];
                Assert.That(StandingRowTexts(root, i),
                    Is.EqualTo(new[] { row.Rank.ToString(), row.RankChangeText, row.Team, row.Wins.ToString(), row.Losses.ToString(),
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

            // 2경기: 삼성(원정) vs SSG(홈). 화면은 장면을 뒤집어 9회초 삼성 공격으로 그리고, 스퀴즈 실패로 2:3 패.
            yield return PlayGame(TacticKind.SqueezeBunt, 0.99);
            Assert.That(SeasonManager.Instance.LastScore, Is.EqualTo(new GameScore(2, 3)));
            Assert.That(LobbyRoot().Q("game-2").Q<Label>(className: "col-status").text, Is.EqualTo("패 2:3"));
            Assert.That(me.Losses, Is.EqualTo(l + 1));
        }

        [UnityTest]
        public IEnumerator 경기를_마치면_저장되고_다시_시작하면_2경기부터_이어지며_대진이_일정표와_같다()
        {
            Assert.That(SeasonManager.Instance.LoadStatus, Is.EqualTo(SeasonLoadStatus.NewSeason));
            yield return PlayGame(null, 0.99);
            Assert.That(File.Exists(TestSavePath), Is.True, "경기 결과 반영 직후 저장");

            var before = SeasonManager.Instance.Season;
            var standings = before.Standings.Select(r => (r.Rank, r.Team, r.Wins, r.Losses, r.Draws)).ToList();

            yield return RestartFromLobby();
            var manager = SeasonManager.Instance;
            Assert.That(manager.LoadStatus, Is.EqualTo(SeasonLoadStatus.Loaded));
            Assert.That(manager.Season, Is.Not.SameAs(before), "새로 불러온 시즌");
            Assert.That(manager.Season.GamesPlayed, Is.EqualTo(1));
            Assert.That(manager.Season.Standings.Select(r => (r.Rank, r.Team, r.Wins, r.Losses, r.Draws)), Is.EqualTo(standings));
            var root = LobbyRoot();
            Assert.That(root.Q<Label>("next-game").text, Does.StartWith("경기 2 · "));
            Assert.That(root.Q("game-1").Q<Label>(className: "col-status").text, Is.EqualTo("무 3:3"));

            // 2경기 진입: 부산(원정, 내 구단) vs 인천(홈)
            var scheduled = manager.Season.NextGame;
            Submit(root.Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            var gameRoot = game.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(game.Matchup.Away.Name, Is.EqualTo(scheduled.AwayTeam));
            Assert.That(game.Matchup.Home.Name, Is.EqualTo(scheduled.HomeTeam));
            Assert.That(game.Matchup.MySide, Is.EqualTo(Data.TeamSide.Away));
            Assert.That(gameRoot.Q<Label>("away-name").text, Is.EqualTo("삼성 라이온즈"));
            Assert.That(gameRoot.Q<Label>("home-name").text, Is.EqualTo("SSG 랜더스"));
            Assert.That(gameRoot.Query<Label>(className: "team-badge").ToList().Select(l => l.text), Is.EqualTo(new[] { "삼성", "SSG" }));
            Assert.That(gameRoot.Query<Label>(className: "ls-cell--team").ToList().Select(l => l.text).Where(t => t != ""),
                Is.EqualTo(new[] { "삼성", "SSG" }), "스코어보드 행: 원정 → 홈");
            Assert.That(gameRoot.Q<Label>("roster-title").text, Is.EqualTo("내 선수단 · 삼성"));
            Assert.That(gameRoot.Q<Label>("pitcher-team").text, Does.StartWith("SSG · "), "상대 투수");
            Assert.That(gameRoot.Q<Label>("pitcher-name").text, Is.EqualTo("SSG 선발 투수"), "데이터 없는 상대는 이름을 지어내지 않는다");
            Assert.That(game.Fixture.Events[0].HalfInning, Is.EqualTo(HalfInning.Top), "원정 공격 = 9회초");

            // 화면 원정 2 : 홈 3 → 스퀴즈 실패로 그대로 끝나면 일정 기준도 2:3 패.
            game.TacticRandom = new ScriptedRandom(0.99);
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            Assert.That(game.ChooseTactic(TacticKind.SqueezeBunt), Is.True);
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            yield return null;
            Submit(gameRoot.Q<Button>("ctl-game-end"));
            yield return WaitForScene(SeasonManager.LobbyScene);
            Assert.That(manager.LastScore, Is.EqualTo(new GameScore(2, 3)));
            Assert.That(LobbyRoot().Q("game-2").Q<Label>(className: "col-status").text, Is.EqualTo("패 2:3"));

            yield return RestartFromLobby();
            Assert.That(SeasonManager.Instance.Season.GamesPlayed, Is.EqualTo(2));
            Assert.That(LobbyRoot().Q<Label>("next-game").text, Does.StartWith("경기 3 · "));
        }

        [UnityTest]
        public IEnumerator 손상된_저장_파일은_경고만_남기고_새_시즌으로_덮어쓴다()
        {
            File.WriteAllText(TestSavePath, "{ \"Version\": 1, \"Schedule\": [ { broken");
            LogAssert.Expect(LogType.Warning, new Regex("손상"));
            yield return RestartFromLobby();

            Assert.That(SeasonManager.Instance.LoadStatus, Is.EqualTo(SeasonLoadStatus.RecoveredFromCorrupt));
            Assert.That(LobbyRoot().Q<Label>("next-game").text, Does.StartWith("경기 1 · "));
            Assert.That(SaveManager.FromJson(File.ReadAllText(TestSavePath)).GamesPlayed, Is.EqualTo(0), "새 시즌으로 덮어씀");
        }

        [UnityTest]
        public IEnumerator 선수_DB는_기본적으로_비동기로_읽고_다_읽기_전에는_경기를_시작하지_않는다()
        {
            DestroyManager();
            yield return null;
            SeasonManager.LoadDatabaseSynchronously = false;
            var manager = SeasonManager.EnsureExists();
            Assert.That(manager.IsDatabaseReady, Is.False, "Awake 직후에는 아직 읽는 중");
            Assert.That(manager.StartNextGame(), Is.False, "DB 준비 전 경기 시작 거부");

            int readyEvents = 0;
            manager.DatabaseReady += () => readyEvents++;
            float timeout = Time.realtimeSinceStartup + 10f;
            while (!manager.IsDatabaseReady && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(manager.IsDatabaseReady, Is.True, "비동기 로딩 완료");
            Assert.That(readyEvents, Is.EqualTo(1));
            Assert.That(manager.Database.IsEmpty, Is.False);
            Assert.That(manager.Database.GetRoster(manager.Season.MyTeam), Is.Not.Null);

            // 이미 만들어진 매니저로 로비를 다시 그리면 DB 선수단과 열린 시작 버튼이 보인다.
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return null;
            yield return null;
            yield return null;
            var root = LobbyRoot();
            Assert.That(root.Q<Button>("start-game").enabledSelf, Is.True);
            Assert.That(root.Q("roster").Query<Label>(className: "col-name").ToList().Select(l => l.text),
                Is.EqualTo(manager.Database.GetRoster(manager.Season.MyTeam).Lineup.Select(p => p.Name)));
            Assert.That(root.Q("roster-source"), Is.Null, "Phase 31: 원천 데이터 파일·연도 표기는 로비에 없다");
        }

        [UnityTest]
        public IEnumerator 경기_화면의_좌측_선수단과_타석_카드가_DB_선수_이름으로_표시된다()
        {
            var db = SeasonManager.Instance.Database;
            var mine = db.GetRoster("삼성 라이온즈");
            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            game.Playback.Pause();
            yield return null;
            var root = game.GetComponent<UIDocument>().rootVisualElement;

            Assert.That(root.Q("lineup").Query<Label>(className: "roster-name").ToList().Select(l => l.text),
                Is.EqualTo(mine.Lineup.Select(p => p.Name)), "좌측 라인업 = DB 타순");
            Assert.That(root.Q("lineup").Query<Label>(className: "roster-pos").ToList().Select(l => l.text),
                Is.EqualTo(mine.Lineup.Select(p => p.Position)));
            Assert.That(root.Q<Label>("batter-name").text, Is.EqualTo("김영웅"), "타석 카드: 삼성 3번 타자 (실제 2024 데이터)");
            Assert.That(root.Q<Label>("batter-team").text, Does.Contain("우투좌타").And.Contain("3B"));
            Assert.That(root.Q<Label>("pitcher-name").text, Is.EqualTo("LG 선발 투수"), "선수 데이터가 없는 상대: 구단 약칭 + 자리");
            Assert.That(root.Q<Label>("pitcher-season").text, Is.EqualTo(Data.ScenarioRosterSource.NoDataLine));
            Assert.That(root.Q<Label>("featured-name").text, Does.StartWith("김영웅"));
            Assert.That(root.Q<Label>("roster-source").text, Is.EqualTo($"표시 데이터: {MasterDatabase.RosterFileName} (2024)"));
            Assert.That(root.Q("bench").Query<Label>(className: "roster-name").ToList().Select(l => l.text),
                Is.EqualTo(mine.Bench.Select(p => p.Name)), "1군 벤치 5명 전원");
            Assert.That(root.Q("bullpen").Query<Label>(className: "roster-name").ToList().Select(l => l.text),
                Is.EqualTo(mine.Bullpen.Select(p => p.Name)), "1군 불펜 7명 전원 (장면 자리 3개에 묶이지 않는다)");

            // Phase 18: 카드 확장 지표 (등번호·나이, 타자 wRC+·WAR). 데이터 없는 상대 투수는 지표·전적 칩이 없다.
            var batter = db.FindPlayer("p_삼성_2024_김영웅");
            Assert.That(root.Q<Label>("batter-profile").text, Is.EqualTo($"No.{batter.Number} · {batter.Age}세"));
            Assert.That(ChipTexts(root, "batter-stats"), Is.EqualTo(new[] { $"wRC+ {batter.WrcPlus:0}", $"WAR {batter.War:0.00}" }));
            Assert.That(ChipTexts(root, "pitcher-stats"), Is.Empty);

            // 1920×1080 렌더 타깃에서 새 칩·전적 줄이 카드 밖으로 나가거나 잘리지 않는다.
            var doc = game.GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panelCopy = Object.Instantiate(doc.panelSettings);
            panelCopy.targetTexture = rt;
            doc.panelSettings = panelCopy;
            yield return null;
            yield return null;
            var layout = LayoutChecker.Check(doc.rootVisualElement.Q("screen"));
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panelCopy);
            Assert.That(layout, Is.Empty, string.Join("\n", layout));
            root = doc.rootVisualElement;

            var texts = root.Query<Label>().ToList().Select(l => l.text).ToList();
            foreach (var dummyName in new[] { "최도윤", "강민혁", "윤재민", "한지후" })
                Assert.That(texts.Any(t => t.Contains(dummyName)), Is.False, $"더미 이름 '{dummyName}'이 남아 있음");
        }

        [UnityTest]
        public IEnumerator 작전_버튼_설명에_타자_투수_기록으로_계산한_성공률이_표시되고_그_확률로_판정한다()
        {
            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            var doc = game.GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panelCopy = Object.Instantiate(doc.panelSettings);
            panelCopy.targetTexture = rt;
            doc.panelSettings = panelCopy;
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            Assert.That(game.Playback.AwaitingDecision, Is.True);
            yield return null;
            yield return null;
            var root = doc.rootVisualElement;
            var branch = game.Fixture.Branch;

            double squeeze = game.TacticSuccessRate(TacticKind.SqueezeBunt);
            double swing = game.TacticSuccessRate(TacticKind.SwingAway);
            Assert.That(squeeze, Is.Not.EqualTo(Fixture8thBottomTactics.SqueezeSuccessRate), "DB 기록 반영");
            Assert.That(swing, Is.Not.EqualTo(Fixture8thBottomTactics.SwingAwaySuccessRate));

            // 버튼에 포인터를 올리면(미리보기) 근거와 동적 성공률이 보인다.
            foreach (var (tactic, rate) in new[] { (TacticKind.SqueezeBunt, squeeze), (TacticKind.SwingAway, swing) })
            {
                game.PreviewTactic(tactic);
                yield return null;
                var desc = root.Q<Label>("tactic-desc").text;
                // Phase 26.5: 김영웅(2024 삼성) vs 선수 데이터가 없는 LG 선발 → 리그 평균 투수로 계산
                Assert.That(desc, Does.Contain("근거 · 김영웅 타율 .252 vs 리그 평균 투수 평균자책 4.30 · 득점권"));
                Assert.That(desc, Does.Contain("보정 · 득점권 클러치 +15%"), "득점권 .310 / 타율 .252 → +23%를 +15%로 자른다");
                Assert.That(desc, Does.EndWith(TacticText.Odds(branch[tactic], rate)));
                var issues = LayoutChecker.Check(root.Q("screen"));
                Assert.That(issues, Is.Empty, $"[{tactic} 설명]\n" + string.Join("\n", issues));
            }
            // 마지막 미리보기는 강공: 클러치 +15% · 체력 저하(89구, 체력 11%) +5% → 58% (스퀴즈는 65%).
            // 1루 주자 구자욱은 Power라 발야구 없음, 기본 라인업은 케미스트리 없음.
            Assert.That(root.Q<Label>("tactic-desc").text, Does.EndWith("성공 확률 58% · 실패 42%"), "강공 · 클러치 +15%, 체력 저하 +5%");
            Assert.That(TacticText.Odds(branch[TacticKind.SqueezeBunt], squeeze), Is.EqualTo("성공 확률 65% · 실패 35%"));
            Assert.That(root.Q<Label>("tactic-desc").text, Does.Not.Contain("케미"));
            Assert.That(root.Q<Label>("tactic-desc").text, Does.Contain("· 체력 저하 +5%").And.Contain("투수 · LG 선발 투수 89구 · 체력 11% (저하)"));
            game.PreviewTactic(null);

            // 난수 0.6: 고정 60%였다면 실패지만, 계산된 성공률(Phase 17 약 60.4%, 체력 저하 반영 후 더 높음)로는 성공.
            Assert.That(squeeze, Is.GreaterThan(0.6));
            game.TacticRandom = new ScriptedRandom(0.6);
            Assert.That(game.ChooseTactic(TacticKind.SqueezeBunt), Is.True);
            Assert.That(game.ChosenTacticRate, Is.EqualTo(squeeze));
            Assert.That(game.TacticResult, Is.EqualTo(TacticOutcome.Success));

            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panelCopy);
        }

        static void Click(VisualElement element)
        {
            using (var click = ClickEvent.GetPooled())
            {
                click.target = element;
                element.SendEvent(click);
            }
        }

        static string[] Names(VisualElement container) =>
            container.Query<Label>(className: "col-name").ToList().Select(l => l.text).ToArray();

        [UnityTest]
        public IEnumerator 로비에서_3번_타자와_벤치_선수를_클릭해_바꾸면_저장되고_경기_화면에_반영된다()
        {
            var root = LobbyRoot();
            Assert.That(root.Q("roster").childCount, Is.EqualTo(9));
            Assert.That(root.Q("bench").childCount, Is.EqualTo(5), "1군 벤치 5명 (나머지는 퓨처스)");
            Assert.That(root.Q<Label>("bench-title").text, Is.EqualTo("벤치 · 퓨처스 35명 대기"));
            Assert.That(Names(root.Q("roster"))[2], Is.EqualTo("김영웅"));
            Assert.That(Names(root.Q("bench"))[0], Is.EqualTo("김헌곤"));

            // 1) 3번 타자 클릭 → 선수 상세 (Phase 31) → [직접 골라 맞바꾸기] → 강조
            var lobbyView = Object.FindAnyObjectByType<LobbyController>();
            Click(root.Q("lineup-3"));
            yield return null;
            Assert.That(lobbyView.PlayerDetail.IsOpen, Is.True);
            Assert.That(root.Q<Label>("pd-name").text, Is.EqualTo("김영웅"));
            Assert.That(root.Q("lineup-3").ClassListContains("lobby-row--selected"), Is.False, "행 클릭은 상세를 연다");
            Submit(root.Q<Button>("pd-pick"));
            yield return null;
            Assert.That(lobbyView.PlayerDetail.IsOpen, Is.False);
            Assert.That(root.Q("lineup-3").ClassListContains("lobby-row--selected"), Is.True);
            Assert.That(root.Q("bench-1").ClassListContains("lobby-row--swappable"), Is.True);
            Assert.That(root.Q<Label>("lineup-hint").text, Does.StartWith("김영웅 선택됨"));

            // 2) 벤치 1번 클릭 → 맞바꾸고 바로 다시 그림
            Click(root.Q("bench-1"));
            yield return null;
            Assert.That(Names(root.Q("roster"))[2], Is.EqualTo("김헌곤"));
            Assert.That(root.Q("lineup-3").Q<Label>(className: "col-pos").text, Is.EqualTo("3B"));
            Assert.That(Names(root.Q("bench"))[0], Is.EqualTo("김영웅"));
            Assert.That(root.Query(className: "lobby-row--selected").ToList(), Is.Empty, "교체 후 선택 해제");
            Assert.That(File.ReadAllText(TestSavePath), Does.Contain("p_삼성_2024_김헌곤"), "세이브 파일에 덮어씀");

            // 벤치끼리는 바꾸지 않고 선택만 옮긴다 (선택 중에는 행 클릭이 상세 대신 교체 대상 선택).
            lobbyView.ClickLineupSpot(LineupSpot.Bench(0));
            Click(root.Q("bench-2"));
            yield return null;
            Assert.That(root.Q("bench-2").ClassListContains("lobby-row--selected"), Is.True);
            Assert.That(root.Q("bench-1").ClassListContains("lobby-row--selected"), Is.False);
            Assert.That(Names(root.Q("bench")), Is.EqualTo(new[] { "김영웅", "김현준", "윤정빈", "이병헌", "안주형" }));
            Click(root.Q("bench-2")); // 취소
            yield return null;

            // 껐다 켜도 유지
            yield return RestartFromLobby();
            root = LobbyRoot();
            Assert.That(Names(root.Q("roster"))[2], Is.EqualTo("김헌곤"));
            Assert.That(SeasonManager.Instance.Season.MyLineup, Is.Not.Null);

            // 3) 경기 진입: 좌측 타순과 타석 카드
            Submit(root.Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            game.Playback.Pause();
            yield return null;
            var gameRoot = game.GetComponent<UIDocument>().rootVisualElement;
            var lineupNames = gameRoot.Q("lineup").Query<Label>(className: "roster-name").ToList().Select(l => l.text).ToArray();
            Assert.That(lineupNames, Is.EqualTo(SeasonManager.Instance.MyRoster.Lineup.Select(p => p.Name)));
            Assert.That(lineupNames[2], Is.EqualTo("김헌곤"));
            Assert.That(gameRoot.Q("bench").Query<Label>(className: "roster-name").First().text, Is.EqualTo("김영웅"));
            Assert.That(gameRoot.Q<Label>("batter-name").text, Is.EqualTo("김헌곤"), "장면의 3번 타자 자리");
            Assert.That(game.Calculator.DecisionContext(game.Fixture.Branch, game.Fixture.Events).Batter.Name, Is.EqualTo("김헌곤"));
        }

        [UnityTest]
        public IEnumerator 경기_중에는_라인업을_바꿀_수_없다()
        {
            var manager = SeasonManager.Instance;
            Assert.That(manager.SwapLineup(LineupSpot.Batting(1), LineupSpot.Batting(2)), Is.True);
            Assert.That(manager.SwapLineup(LineupSpot.Bench(0), LineupSpot.Bench(1)), Is.False, "벤치끼리");
            Assert.That(manager.SwapLineup(LineupSpot.Batting(0), LineupSpot.Bench(9)), Is.False, "범위 밖");
            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            Assert.That(manager.SwapLineup(LineupSpot.Batting(1), LineupSpot.Batting(2)), Is.False, "경기 중");
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

        // ── 퀵 시뮬레이션 시즌 경기 (Phase 13) ──

        static string[] InningCells(VisualElement root, int row) =>
            root.Q("linescore").Children().ElementAt(row).Children().Skip(1).Take(9).Select(c => ((Label)c).text).ToArray();

        static string Fingerprint(GeneratedGame g) =>
            $"{string.Join(",", g.AwayRunsByInning)}|{string.Join(",", g.HomeRunsByInning)}|{g.Situation}";

        [UnityTest]
        public IEnumerator 퀵_시뮬_시즌_경기는_매번_다른_스코어보드와_승부처로_시작하고_결과가_반영된다()
        {
            const int seed = 20261016;
            var manager = SeasonManager.Instance;
            manager.UseGeneratedScenario = true;
            manager.ScenarioRandom = new SeededRandom(seed);
            // 같은 Seed로 따로 굴린 기대값 (경기 1: 서울 원정 vs 부산 홈, 경기 2: 부산 원정 vs 인천 홈. 생성은 내 구단 = 홈 기준)
            var expectRandom = new SeededRandom(seed);
            var games = new System.Collections.Generic.List<GeneratedGame>();

            for (int round = 1; round <= 2; round++)
            {
                var scheduled = manager.Season.NextGame;
                string opponent = scheduled.HomeTeam == manager.Season.MyTeam ? scheduled.AwayTeam : scheduled.HomeTeam;
                // Phase 22: 경기에는 선발 로테이션을 적용한 선수단이 나간다.
                var expected = FixtureGenerator.Generate(manager.GameRosterOf(opponent), manager.GameRosterOf(manager.Season.MyTeam), expectRandom);

                Submit(LobbyRoot().Q<Button>("start-game"));
                yield return WaitForScene(SeasonManager.GameScene);
                var game = Object.FindAnyObjectByType<GameScreenController>();
                game.Playback.Pause();
                game.Playback.Restart();
                yield return null;
                var root = game.GetComponent<UIDocument>().rootVisualElement;

                var generated = game.Generated;
                Assert.That(game.ScenarioId, Is.EqualTo(FixtureGenerator.ScenarioId), $"{round}경기는 퀵 시뮬 장면");
                Assert.That(Fingerprint(generated), Is.EqualTo(Fingerprint(expected)), "같은 Seed → 같은 경기");
                games.Add(generated);

                // 스코어보드: 화면 원정·홈 = 일정 원정·홈. 내 구단이 홈이면 생성 기준 그대로, 원정이면 뒤집힘.
                bool home = game.Matchup.MySide == Data.TeamSide.Home;
                var awayRuns = home ? generated.AwayRunsByInning : generated.HomeRunsByInning;
                var homeRuns = home ? generated.HomeRunsByInning : generated.AwayRunsByInning;
                Assert.That(InningCells(root, 1).Take(7), Is.EqualTo(awayRuns.Take(7).Select(r => r.ToString())), "원정 1~7회");
                Assert.That(InningCells(root, 2).Take(7), Is.EqualTo(homeRuns.Take(7).Select(r => r.ToString())), "홈 1~7회");
                Assert.That(root.Q<Label>("away-score").text, Is.EqualTo(awayRuns.Sum().ToString()));
                Assert.That(root.Q<Label>("home-score").text, Is.EqualTo(homeRuns.Sum().ToString()));
                var s = generated.Situation;
                Assert.That(root.Q<Label>("status-inning").text,
                    Is.EqualTo($"{(home ? "8회말" : "9회초")} · {PitchPresentation.OutsText(s.Outs)} · {s.Runners}"));

                // 기본 진행으로 끝내고 [경기 종료] → 화면 최종 점수가 그대로 시즌에 반영된다.
                game.Playback.SkipToEnd();
                Assert.That(game.ChooseTactic(TacticKind.Default), Is.True);
                game.Playback.Pause();
                game.Playback.SkipToEnd();
                var final = game.FinalScore();
                yield return null;
                Submit(root.Q<Button>("ctl-game-end"));
                yield return WaitForScene(SeasonManager.LobbyScene);
                Assert.That(manager.LastScore, Is.EqualTo(final), $"{round}경기 결과 반영");
            }

            Assert.That(Fingerprint(games[0]), Is.Not.EqualTo(Fingerprint(games[1])), "경기마다 다른 스코어보드·상황");
        }

        [UnityTest]
        public IEnumerator 퀵_시뮬_순환이_3아웃으로_끝나면_경기_종료로_로비에_돌아가고_결과가_반영된다()
        {
            var manager = SeasonManager.Instance;
            manager.UseGeneratedScenario = true;
            manager.ScenarioRandom = new SeededRandom(20261017);

            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            game.TacticRandom = new ScriptedRandom(0.99); // 강공 실패만 → 3아웃
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            yield return null;

            // 공격 3아웃 뒤 수비 반 이닝(기본 진행만)까지 돌고 끝난다 (Phase 19).
            int guard = 0;
            while (game.CurrentBranch != null)
            {
                Assert.That(++guard, Is.LessThanOrEqualTo(2 * PlayByPlayGenerator.MaxInnings * PlayByPlayGenerator.MaxPlateAppearancesPerHalf));
                Assert.That(game.ChooseTactic(game.AwaitingDefenseDecision ? TacticKind.Default : TacticKind.SwingAway), Is.True);
                game.Playback.Pause();
                game.Playback.SkipToEnd();
                yield return null;
            }
            Assert.That(game.InningEndAwaitingExit, Is.True, "수비 3아웃 · 경기 종료 대기");
            Assert.That(game.DefenseStarted, Is.True);
            var final = game.FinalScore();
            var gameEnd = game.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ctl-game-end");
            Assert.That(gameEnd.enabledSelf, Is.True);
            Assert.That(gameEnd.ClassListContains("btn--active"), Is.True, "경기 종료 안내 강조");

            Submit(gameEnd);
            yield return WaitForScene(SeasonManager.LobbyScene);
            Assert.That(manager.LastScore, Is.EqualTo(final));
            Assert.That(manager.Season.GamesPlayed, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator 시즌_경기는_기본으로_퀵_시뮬_장면을_쓴다()
        {
            DestroyManager();
            yield return null;
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForScene(SeasonManager.LobbyScene);
            var manager = SeasonManager.Instance;
            Assert.That(manager.UseGeneratedScenario, Is.True, "기본값");
            manager.LeagueRandom = new SeededRandom(1);

            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            Assert.That(game.ScenarioId, Is.EqualTo(FixtureGenerator.ScenarioId));
            Assert.That(game.Generated, Is.Not.Null);
            Assert.That(game.Fixture.Branch, Is.Not.Null, "승부처 작전 분기 유지");
        }

        [UnityTest]
        public IEnumerator 인게임_투수_교체는_DB_불펜으로_하고_로비_복귀_뒤_원본_선수단은_그대로다()
        {
            var manager = SeasonManager.Instance;
            manager.UseGeneratedScenario = true;
            manager.ScenarioRandom = new SeededRandom(20261017);
            var before = manager.MyRoster;
            var starters = before.Starters.Select(p => p.Id).ToArray();
            var bullpen = before.Bullpen.Select(p => (p.Id, p.Role, p.Order, p.Position)).ToArray();

            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            game.TacticRandom = new ScriptedRandom(0.99);
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            yield return null;
            int guard = 0;
            while (!game.AwaitingDefenseDecision)
            {
                Assert.That(++guard, Is.LessThanOrEqualTo(PlayByPlayGenerator.MaxPlateAppearancesPerHalf), "수비 이닝에 닿지 않음");
                Assert.That(game.ChooseTactic(TacticKind.SwingAway), Is.True);
                game.Playback.Pause();
                game.Playback.SkipToEnd();
                yield return null;
            }
            yield return null;
            var root = game.GetComponent<UIDocument>().rootVisualElement;

            Assert.That(root.Q<Label>("pitcher-name").text, Is.EqualTo(before.StartingPitcher.Name), "내 선발이 마운드에");
            Assert.That(game.MyPitching.ReliefCandidates.Select(p => p.Name), Is.EqualTo(before.Bullpen.Select(p => p.Name)),
                "불펜 후보 = DB 불펜 (벤치 야수 없음)");
            Assert.That(game.MyPitching.Pool.Where(p => !p.IsPitcher).Select(p => p.Name), Is.EqualTo(before.Bench.Select(p => p.Name)),
                "벤치 야수는 대기 명단에 있지만 후보가 아니다");

            Assert.That(game.ChangePitcher("home_rp1"), Is.True);
            yield return null;
            var reliever = before.Bullpen[0];
            Assert.That(root.Q<Label>("pitcher-name").text, Is.EqualTo(reliever.Name));
            Assert.That(root.Q<Label>("pitcher-team").text, Does.Contain(reliever.Position));
            game.PreviewTactic(TacticKind.Default);
            yield return null;
            Assert.That(root.Q<Label>("tactic-desc").text, Does.Contain($"투수 · {reliever.Name} 0구 · 체력 100%"));
            game.PreviewTactic(null);

            guard = 0;
            while (game.CurrentBranch != null)
            {
                Assert.That(++guard, Is.LessThanOrEqualTo(2 * PlayByPlayGenerator.MaxInnings * PlayByPlayGenerator.MaxPlateAppearancesPerHalf));
                Assert.That(game.ChooseTactic(TacticKind.Default), Is.True);
                game.Playback.Pause();
                game.Playback.SkipToEnd();
                yield return null;
            }
            Assert.That(game.InningEndAwaitingExit, Is.True);
            var final = game.FinalScore();
            Submit(root.Q<Button>("ctl-game-end"));
            yield return WaitForScene(SeasonManager.LobbyScene);
            Assert.That(manager.LastScore, Is.EqualTo(final));

            // 인게임 교체는 일회성: 선발 로테이션·불펜 구성은 그대로
            var after = manager.MyRoster;
            Assert.That(after.Starters.Select(p => p.Id), Is.EqualTo(starters));
            Assert.That(after.Bullpen.Select(p => (p.Id, p.Role, p.Order, p.Position)), Is.EqualTo(bullpen));
            Assert.That(manager.Database.GetRoster(manager.Season.MyTeam).StartingPitcher.Id, Is.EqualTo(starters[0]));
        }

        [UnityTest]
        public IEnumerator 로비에서_풀_이닝을_고르면_1회부터_DB_선수단으로_끝까지_진행하고_결과가_반영된다()
        {
            var manager = SeasonManager.Instance;
            var mode = LobbyRoot().Q<Button>("game-mode");
            Assert.That(manager.FullInningMode, Is.False, "기본은 퀵 시뮬");
            Assert.That(mode.text, Is.EqualTo("진행: 퀵 시뮬 (8회부터)"));
            Submit(mode);
            yield return null;
            Assert.That(manager.FullInningMode, Is.True);
            Assert.That(LobbyRoot().Q<Button>("game-mode").text, Is.EqualTo("진행: 풀 이닝 (1회부터)"));

            var scheduled = manager.Season.NextGame;
            string opponent = scheduled.HomeTeam == manager.Season.MyTeam ? scheduled.AwayTeam : scheduled.HomeTeam;
            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            game.TacticRandom = new ScriptedRandom(0.99, 0.1); // 모든 타석 삼진 → 0 : 0, 12회 무승부
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            yield return null;
            var root = game.GetComponent<UIDocument>().rootVisualElement;

            Assert.That(game.IsFullGame, Is.True);
            Assert.That(game.Generated, Is.Null, "퀵 시뮬을 쓰지 않는다");
            var first = game.Events[game.CurrentBranch.AfterIndex];
            Assert.That((first.Inning, first.HalfInning), Is.EqualTo((1, HalfInning.Top)));
            bool myHome = game.Matchup.MySide == Data.TeamSide.Home;
            var homeStarter = myHome ? manager.MyRoster.StartingPitcher : manager.Database.GetRoster(opponent).StartingPitcher;
            Assert.That(root.Q<Label>("pitcher-name").text, Is.EqualTo(homeStarter.Name), "1회초는 홈 선발");

            int guard = 0;
            while (game.CurrentBranch != null)
            {
                Assert.That(++guard, Is.LessThanOrEqualTo(2 * PlayByPlayGenerator.MaxInnings * PlayByPlayGenerator.MaxPlateAppearancesPerHalf));
                Assert.That(game.ChooseTactic(TacticKind.Default), Is.True);
                game.Playback.Pause();
                game.Playback.SkipToEnd();
            }
            yield return null;
            Assert.That(game.FinalScore(), Is.EqualTo(new GameScore(0, 0)));
            var last = game.Events[game.Events.Count - 1];
            Assert.That((last.Inning, last.HalfInning), Is.EqualTo((PlayByPlayGenerator.MaxInnings, HalfInning.Bottom)));

            // CPU 선발이 108구로 고갈되자 상대 불펜으로 바뀐다. 상대(LG)는 선수 데이터가 없어 "LG 불펜 N"으로 보인다.
            var cpu = game.PitchingChanges.Single(c => c.ByCpu);
            Assert.That(cpu.Reason, Is.EqualTo("체력 고갈"));
            Assert.That(manager.Database.GetRoster(opponent), Is.Null);
            Assert.That(game.Roster.GetPlayer(cpu.To).Name, Does.StartWith("LG 불펜 "));

            Submit(root.Q<Button>("ctl-game-end"));
            yield return WaitForScene(SeasonManager.LobbyScene);
            Assert.That(manager.LastScore, Is.EqualTo(new GameScore(0, 0)));
            Assert.That(manager.Season.MyStanding.Draws, Is.EqualTo(1), "무승부 반영");
            Assert.That(manager.FullInningMode, Is.True, "진행 방식은 실행 중 유지");
        }

        static IEnumerator CheckLobbyLayout(string label)
        {
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
            Assert.That(issues, Is.Empty, $"[{label}]\n" + string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator 로비_프런트_오피스는_예산_팬_만족도_오늘_날짜를_보이고_경기_뒤_바로_갱신된다()
        {
            var manager = SeasonManager.Instance;
            var root = LobbyRoot();
            Assert.That(root.Q("today"), Is.Null, "Phase 31: 현실 날짜는 없다");
            Assert.That(root.Q<Label>("season-title").text, Is.EqualTo(manager.Season.SeasonTitle));
            Assert.That(root.Q<Label>("game-day").text, Is.EqualTo("다음 경기 " + manager.Season.NextGame.Date), "게임 내 날짜");
            Assert.That(root.Q<Label>("fo-budget").text, Is.EqualTo("₩ 50억"), "Phase 26: 억·만 단위 표기");
            Assert.That(root.Q<Label>("fo-budget").tooltip, Is.EqualTo("₩5,000,000,000"));
            Assert.That(manager.Season.Payroll, Is.EqualTo(6_294_000_000L), "로비가 열리면 페이롤이 삼성 1군 26명 연봉에 맞춰진다");
            Assert.That(manager.Season.SalaryCap, Is.EqualTo(7_867_500_000L), "캡 기준 = 리그 개막 평균 페이롤(데이터 있는 구단 = 삼성) × 1.25");
            Assert.That(root.Q<Label>("fo-fan").text, Is.EqualTo("50%"));
            Assert.That(root.Q("fo-fan-fill").style.width.value, Is.EqualTo(Length.Percent(50)));
            Assert.That(root.Q("fo-fan-fill").ClassListContains("fo-fill--happy") || root.Q("fo-fan-fill").ClassListContains("fo-fill--angry"),
                Is.False, "50%는 보통");
            Assert.That(root.Q<Label>("fo-season").text, Is.EqualTo("0 / 10경기"));
            Assert.That(root.Q<Label>("fo-budget-delta").text, Is.EqualTo("승 +₩5,000만 · 유지비 −₩4,370만"), "아직 치른 경기가 없으면 수입·유지비 안내");
            Assert.That(root.Q<Label>("last-finance").text, Is.Empty);
            yield return CheckLobbyLayout("첫 로비");

            // 고정 장면 1경기 → 로비 복귀
            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            Assert.That(game.ChooseTactic(TacticKind.Default), Is.True);
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            yield return null;
            Submit(game.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ctl-game-end"));
            yield return WaitForScene(SeasonManager.LobbyScene);

            var report = manager.LastReport;
            // Phase 26: 관중 수입에서 선수단 유지비(페이롤 62.94억 ÷ 144 = 4,370만 8,333원)를 뺀 순이익이 예산에 반영된다.
            var income = FranchiseFinance.For(report.MyOutcome);
            Assert.That((report.Income, report.PayrollExpense), Is.EqualTo((income.BudgetDelta, 43_708_333L)));
            var change = new FinanceChange(income.BudgetDelta - 43_708_333, income.FanDelta);
            Assert.That(report.Finance, Is.EqualTo(change));
            root = LobbyRoot();
            Assert.That(root.Q<Label>("fo-budget").text, Is.EqualTo(FranchiseFinance.BudgetText(5_000_000_000 + change.BudgetDelta)), "즉시 갱신");
            Assert.That(root.Q<Label>("fo-fan").text, Is.EqualTo($"{50 + change.FanDelta}%"));
            Assert.That(root.Q("fo-fan-fill").style.width.value, Is.EqualTo(Length.Percent(50 + change.FanDelta)));
            Assert.That(root.Q<Label>("fo-budget-delta").text, Does.Contain("순이익 " + FranchiseFinance.BudgetDeltaText(change.BudgetDelta)));
            Assert.That(root.Q<Label>("fo-budget-delta").tooltip, Is.EqualTo(report.FinanceText));
            Assert.That(root.Q<Label>("last-finance").text, Is.EqualTo(report.FinanceText));
            Assert.That(report.FinanceText, Does.Contain("유지비 −₩4,370만"));
            Assert.That(root.Q<Label>("fo-fan-delta").text, Does.EndWith(FranchiseFinance.FanDeltaText(change.FanDelta)));
            Assert.That(root.Q<Label>("fo-season").text, Is.EqualTo("1 / 10경기"));
            Assert.That((manager.Season.Budget, manager.Season.FanApproval), Is.EqualTo((5_000_000_000 + change.BudgetDelta, 50 + change.FanDelta)));
            yield return CheckLobbyLayout("경기 후 로비");

            // 재시작해도 저장된 재정으로 이어진다.
            yield return RestartFromLobby();
            Assert.That(LobbyRoot().Q<Label>("fo-budget").text, Is.EqualTo(FranchiseFinance.BudgetText(5_000_000_000 + change.BudgetDelta)));
            Assert.That(LobbyRoot().Q<Label>("fo-fan").text, Is.EqualTo($"{50 + change.FanDelta}%"));
        }

        [UnityTest]
        public IEnumerator 팬_만족도_80_이상은_초록_30_미만은_빨강으로_보인다()
        {
            var manager = SeasonManager.Instance;
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            foreach (var (fan, cls) in new[] { (85, "fo-fill--happy"), (20, "fo-fill--angry") })
            {
                manager.Season.SetFinance(12_345_678_901, fan);
                lobby.enabled = false;
                lobby.enabled = true; // 다시 그리기
                yield return null;
                var root = LobbyRoot();
                Assert.That(root.Q("fo-fan-fill").ClassListContains(cls), Is.True, $"{fan}% → {cls}");
                Assert.That(root.Q<Label>("fo-fan").ClassListContains(cls.Replace("fill", "percent")), Is.True);
                Assert.That(root.Q<Label>("fo-budget").text, Is.EqualTo("₩ 123억 4,567만"));
            }
        }

        /// <summary>로비를 1920×1080 렌더 텍스처에 그리게 한다 (레이아웃 검사용). 정리할 것들을 돌려준다.</summary>
        static (RenderTexture, PanelSettings) RenderAt1080(LobbyController lobby)
        {
            var doc = lobby.GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panelCopy = Object.Instantiate(doc.panelSettings);
            panelCopy.targetTexture = rt;
            doc.panelSettings = panelCopy;
            return (rt, panelCopy);
        }

        [UnityTest]
        public IEnumerator 프런트_오피스에_신뢰도_TP와_팬덤_특전_해금_상태가_보이고_FA_버튼이_열려_있다()
        {
            var manager = SeasonManager.Instance;
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            var (rt, panel) = RenderAt1080(lobby);
            yield return null;
            var root = LobbyRoot();
            Assert.That(root.Q<Label>("fo-trust").text, Is.EqualTo("0 / 200"));
            Assert.That(root.Q("fo-trust-fill").style.width.value, Is.EqualTo(Length.Percent(0)));
            foreach (var name in new[] { "uniform", "ceremony", "cheerleader" })
                Assert.That(root.Q("fo-slot-" + name).ClassListContains("fo-slot--locked"), Is.True, name);

            var fa = root.Q<Button>("scout-fa");
            Assert.That((fa.text, fa.enabledSelf), Is.EqualTo(("스카우트 / FA", true)), "Phase 25: 선수 DB가 있으면 FA 시장이 열린다");

            manager.Season.SetTrust(150, 0, 0, 0);
            manager.Season.SetUnlockedSlots(FandomSlot.Uniform | FandomSlot.Cheerleader);
            lobby.enabled = false;
            lobby.enabled = true;
            yield return null;
            yield return null;
            root = LobbyRoot();
            Assert.That(root.Q<Label>("fo-trust").text, Is.EqualTo("150 / 200"));
            Assert.That(root.Q("fo-trust-fill").style.width.value, Is.EqualTo(Length.Percent(75)));
            Assert.That(root.Q("fo-trust-gauge").tooltip, Is.EqualTo("천장 도달 · 확정 영입 가능"));
            Assert.That(root.Q("fo-slot-uniform").ClassListContains("fo-slot--unlocked"), Is.True);
            Assert.That(root.Q("fo-slot-ceremony").ClassListContains("fo-slot--locked"), Is.True);
            Assert.That(root.Q("fo-slot-cheerleader").ClassListContains("fo-slot--unlocked"), Is.True);
            Assert.That(root.Q("fo-slot-ceremony").tooltip, Does.StartWith("잠김 · 팬 만족도 70%"));

            var issues = LayoutChecker.Check(root.Q("screen"));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panel);
        }

        [UnityTest]
        public IEnumerator 라인업을_바꾸면_팀_케미스트리가_즉시_켜지고_꺼지며_결과가_같으면_다시_그리지_않는다()
        {
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            var (rt, panel) = RenderAt1080(lobby);
            yield return null;
            var root = LobbyRoot();
            // 실제 2024 삼성 (태그 상위 20%): 1 구자욱·3 김영웅·7 이성규 Power, 2 김지찬·8 류지혁 Speed, 4 이재현·5 강민호 Glove
            // → Power가 연속 타순이 아니고 Speed·센터라인 Glove가 2명씩이라 기본 라인업은 케미스트리 없음.
            var active = root.Q<Label>("chem-active");
            Assert.That(active.text, Is.EqualTo("활성화된 케미스트리 없음"));
            Assert.That(active.ClassListContains("chem-active--on"), Is.False);
            Assert.That(root.Q("lineup-1").Q<Label>(className: "chem-badge").text, Is.EqualTo("장"));
            Assert.That(root.Q("lineup-2").Q<Label>(className: "chem-badge").text, Is.EqualTo("주"));
            Assert.That(root.Q("lineup-4").Q<Label>(className: "chem-badge").text, Is.EqualTo("수"));
            Assert.That(root.Q("lineup-6").Q<Label>(className: "chem-badge").ClassListContains("chem-badge--none"), Is.True);
            var issues = LayoutChecker.Check(root.Q("screen"));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
            int renders = lobby.ChemistryRenderCount;

            // 태그 없는 6번 박병호·9번 맥키넌 맞바꿈 → 케미스트리 그대로, 다시 그리지 않는다.
            lobby.ClickLineupSpot(LineupSpot.Batting(6));
            lobby.ClickLineupSpot(LineupSpot.Batting(9));
            yield return null;
            Assert.That(LobbyRoot().Q("lineup-6").Q<Label>(className: "col-name").text, Is.EqualTo("맥키넌"), "교체는 됐다");
            Assert.That(lobby.ChemistryRenderCount, Is.EqualTo(renders));

            // 2번 김지찬 ↔ 3번 김영웅, 3번(김지찬) ↔ 7번 이성규 → 1·2·3번 Power 연속: 거포 클린업 켜짐
            lobby.ClickLineupSpot(LineupSpot.Batting(2));
            lobby.ClickLineupSpot(LineupSpot.Batting(3));
            yield return null;
            Assert.That(lobby.ChemistryRenderCount, Is.EqualTo(renders), "1·2번 Power 2명 — 아직 없음");
            lobby.ClickLineupSpot(LineupSpot.Batting(3));
            lobby.ClickLineupSpot(LineupSpot.Batting(7));
            yield return null;
            active = LobbyRoot().Q<Label>("chem-active");
            Assert.That(active.text, Is.EqualTo("활성화됨: 거포 클린업"));
            Assert.That(active.ClassListContains("chem-active--on"), Is.True);
            Assert.That(lobby.ChemistryRenderCount, Is.EqualTo(renders + 1));
            Assert.That(lobby.Chemistry.Members(ChemistryKind.PowerCleanup),
                Is.EquivalentTo(new[] { "p_삼성_2024_구자욱", "p_삼성_2024_김영웅", "p_삼성_2024_이성규" }));

            // 1번 구자욱 ↔ 벤치 김헌곤(태그 없음): 연속이 끊겨 꺼진다.
            lobby.ClickLineupSpot(LineupSpot.Batting(1));
            lobby.ClickLineupSpot(LineupSpot.Bench(0));
            yield return null;
            Assert.That(LobbyRoot().Q<Label>("chem-active").text, Is.EqualTo("활성화된 케미스트리 없음"));
            Assert.That(lobby.Chemistry.Kinds, Is.Empty);
            Assert.That(lobby.ChemistryRenderCount, Is.EqualTo(renders + 2));

            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panel);
        }

        [UnityTest]
        public IEnumerator Phase31_오늘의_결정에서_지친_주전을_발견해_상세에서_벤치와_바꾸면_경기_뒤_회복해_경고가_사라진다()
        {
            var manager = SeasonManager.Instance;
            manager.UseGeneratedScenario = true;
            manager.ScenarioRandom = new SeededRandom(20261031);
            manager.ConditionRandom = new ScriptedRandom(0.99); // 출장 야수는 하락하지 않는다 → 교체·휴식 효과만 본다
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            var doc = lobby.GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panel = Object.Instantiate(doc.panelSettings);
            panel.targetTexture = rt;
            doc.panelSettings = panel;
            yield return null;
            yield return null;
            var root = LobbyRoot();
            void Layout(string label)
            {
                var issues = LayoutChecker.Check(LobbyRoot().Q("screen"));
                Assert.That(issues, Is.Empty, $"[{label}]\n" + string.Join("\n", issues));
            }

            // 홈 대시보드: 팀 컬러 테마, 다음 경기 프리뷰, 경고 없음, 목록에는 방출 버튼·보통 컨디션 표시 없음
            Assert.That(root.Q("screen").ClassListContains("team--samsung"), Is.True);
            Assert.That(root.Q<Label>("team-mark").text, Is.EqualTo("삼성"));
            Assert.That(root.Q<Label>("pv-home-team").text, Is.EqualTo("삼성 라이온즈"));
            Assert.That(root.Q<Label>("pv-away-team").text, Is.EqualTo("LG 트윈스"));
            Assert.That(root.Q("pv-home").ClassListContains("pv-side--mine"), Is.True);
            Assert.That(root.Q<Label>("pv-home-sp").text, Is.EqualTo(manager.GameRosterOf("삼성 라이온즈").StartingPitcher.Name));
            Assert.That(root.Q<Label>("pv-away-sp").text, Is.EqualTo("선발 미정"), "상대 선수 데이터 없음");
            Assert.That(root.Q<Label>("pv-note").text, Is.EqualTo("시즌 첫 맞대결"));
            Assert.That(root.Q("decision-ok"), Is.Not.Null, "처음엔 경고 없음");
            Assert.That(root.Query<Button>(className: "lobby-release").ToList(), Is.Empty);
            Assert.That(root.Q("lineup-3").Q<Label>(className: "cond-text").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "보통은 숨긴다");
            Layout("Phase31 첫 로비");

            // 1) 문제: 3번 타자가 지침 → 오늘의 결정에 경고
            var tired = manager.MyRoster.AtOrder(3);
            manager.Season.SetCondition(tired.Id, PlayerCondition.Tired);
            lobby.enabled = false;
            lobby.enabled = true;
            yield return null;
            root = LobbyRoot();
            var warning = root.Q("decision-cond-" + tired.Id);
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning.ClassListContains("decision--caution"), Is.True);
            Assert.That(warning.Q<Label>(className: "decision__title").text, Is.EqualTo($"3번 {tired.Name} ({tired.Position}) · 컨디션 지침"));
            Assert.That(root.Q("decision-ok"), Is.Null);
            Assert.That(root.Q("lineup-3").Q<Label>(className: "cond-text").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "지침은 보인다");
            Layout("지침 경고");

            // 2) 결정: 경고를 눌러 선수 상세 → 상태 확인 → 추천 벤치 선수와 교체
            Click(warning);
            yield return null;
            Assert.That(lobby.PlayerDetail.IsOpen, Is.True);
            Assert.That(lobby.PlayerDetail.PlayerId, Is.EqualTo(tired.Id));
            Assert.That(root.Q<Label>("pd-condition").text, Is.EqualTo("컨디션 지침"));
            Assert.That(root.Q<Label>("pd-condition-note").text, Is.EqualTo("벤치에서 한 경기 쉬면 보통으로 회복합니다."));
            Assert.That(root.Q<Label>("pd-role").text, Is.EqualTo("선발 3번 타자"));
            var sub = lobby.PlayerDetail.SwapCandidates()[0];
            yield return null;
            Layout("선수 상세 모달");
            Submit(root.Q<Button>("pd-swap-" + sub.Id));
            yield return null;
            Assert.That(lobby.PlayerDetail.IsOpen, Is.False);
            Assert.That(manager.MyRoster.AtOrder(3).Id, Is.EqualTo(sub.Id));
            Assert.That(root.Q("decision-cond-" + tired.Id), Is.Null, "벤치로 내리면 경고에서 빠진다");

            // 3) 경기 진행
            yield return PlayDefaultGameAndReturn();

            // 4) 결과: 쉰 선수는 보통으로 회복, 로비 경고 없음
            Assert.That(manager.Season.ConditionOf(tired.Id), Is.EqualTo(PlayerCondition.Normal), "지침(2) + 휴식 1 → 보통");
            root = LobbyRoot();
            Assert.That(root.Q("decision-cond-" + tired.Id), Is.Null);
            Assert.That(root.Q("decision-ok"), Is.Not.Null, "경고 패널이 비었다");
            var benchRow = root.Q("bench").Children().First(r => r.Q<Label>(className: "col-name").text == tired.Name);
            Assert.That(benchRow.Q<Label>(className: "cond-text").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "보통이라 표시 없음");
            Assert.That(root.Q<Label>("pv-note").text, Does.StartWith("시즌 첫 맞대결").Or.StartWith("시즌 상대 전적"));
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panel);
        }

        /// <summary>한국시리즈를 끝까지 (내 구단이 나가면 4:2로 직접, 탈락이면 결과 시뮬레이션).</summary>
        static void FinishPostseason(SeasonManager manager)
        {
            manager.PostseasonRandom = new SeededRandom(32);
            while (manager.Season.Phase == SeasonPhase.PostSeason)
            {
                if (manager.Season.InPostseason)
                {
                    manager.Season.StartNextGame();
                    manager.AdvanceSeason(manager.Season.ScheduleScore(4, 2));
                }
                else Assert.That(manager.SimulatePostseason(), Is.True);
            }
            Assert.That(manager.Season.Phase, Is.EqualTo(SeasonPhase.OffSeason));
        }

        [UnityTest]
        public IEnumerator Phase32_정규시즌_상위권이면_한국시리즈가_열리고_4승하면_우승_뉴스와_상금을_받고_결산한다()
        {
            var manager = SeasonManager.Instance;
            while (manager.Season.Phase == SeasonPhase.RegularSeason)
            {
                manager.Season.StartNextGame();
                manager.AdvanceSeason(manager.Season.ScheduleScore(6, 1));
            }
            yield return RestartFromLobby();
            manager = SeasonManager.Instance;
            var season = manager.Season;
            Assert.That(season.Phase, Is.EqualTo(SeasonPhase.PostSeason));
            Assert.That(season.InPostseason, Is.True, $"10전 전승이면 상위 2위: {season.Postseason.TopSeed} / {season.Postseason.SecondSeed}");
            string opp = season.Postseason.OpponentOf(season.MyTeam);
            string oppShort = opp.Split(' ')[0];

            var doc = Object.FindAnyObjectByType<LobbyController>().GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panel = Object.Instantiate(doc.panelSettings);
            panel.targetTexture = rt;
            doc.panelSettings = panel;
            yield return null;
            yield return null;
            void Layout(string label)
            {
                var issues = LayoutChecker.Check(LobbyRoot().Q("screen"));
                Assert.That(issues, Is.Empty, $"[{label}]\n" + string.Join("\n", issues));
            }

            // 1) 한국시리즈 로비: 가을야구 테마, 헤더·프리뷰·일정이 시리즈 전적으로
            var root = LobbyRoot();
            Assert.That(root.Q("screen").ClassListContains("ps-theme"), Is.True);
            Assert.That(root.Q<Label>("season-title").text, Is.EqualTo("2026 시즌 · 한국시리즈"));
            Assert.That(root.Q<Label>("my-record").text, Does.StartWith($"한국시리즈 삼성 0승 - 0승 {oppShort} · 정규시즌 "));
            Assert.That(root.Q<Label>("pv-title").text, Is.EqualTo("KOREAN SERIES · 한국시리즈"));
            Assert.That(root.Q<Label>("pv-note").text, Is.EqualTo($"삼성 0승 - 0승 {oppShort} · 4승 선착 팀 우승"));
            Assert.That(root.Q<Label>("pv-meta").text, Does.StartWith("1차전 · "));
            Assert.That(root.Q<Label>("game-day").text, Does.StartWith("한국시리즈 1차전 "));
            Assert.That(root.Q("schedule").childCount, Is.EqualTo(7));
            Assert.That(root.Q("ks-1"), Is.Not.Null);
            Assert.That(root.Q<Button>("start-game").text, Is.EqualTo("한국시리즈 1차전 시작"));
            Assert.That(root.Q("decision-postseason"), Is.Not.Null);
            Layout("한국시리즈 로비");

            // 2) 1차전은 실제 경기 화면으로
            yield return PlayGame(null, 0.5);
            manager = SeasonManager.Instance;
            Assert.That(manager.LastReport.Postseason, Is.True);
            Assert.That(manager.Season.Postseason.NextIndex, Is.EqualTo(1));
            root = LobbyRoot();
            Assert.That(root.Q("game-1"), Is.Null, "정규시즌 일정 대신 시리즈 일정");
            Assert.That(root.Q("ks-1").Q<Label>(className: "col-status").text, Does.StartWith("승 ").Or.StartWith("패 ").Or.StartWith("무 "));
            Assert.That(root.Q<Label>("last-result").text, Does.StartWith("한국시리즈 1차전 · "));

            // 3) 남은 경기를 이겨 우승: [챔피언] 뉴스, 상금 ₩20억, 팬 만족도 100%
            while (manager.Season.Phase == SeasonPhase.PostSeason)
            {
                manager.Season.StartNextGame();
                manager.AdvanceSeason(manager.Season.ScheduleScore(7, 0));
            }
            var report = manager.LastReport;
            Assert.That(report.Champion, Is.EqualTo(manager.Season.MyTeam));
            Assert.That(report.ChampionAward.Value.BudgetDelta, Is.EqualTo(SeasonState.ChampionPrize));
            Assert.That(manager.Season.FanApproval, Is.EqualTo(100));
            Assert.That(manager.Season.Phase, Is.EqualTo(SeasonPhase.OffSeason));
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            lobby.GetComponent<UIDocument>().panelSettings = panel; // 경기 화면을 다녀와 새로 뜬 로비에도 1080p 렌더 대상
            lobby.enabled = false;
            lobby.enabled = true;
            yield return null;
            yield return null;
            root = LobbyRoot();
            var champNews = string.Join(" ", root.Q("news-0").Query<Label>().ToList().Select(l => l.text));
            Assert.That(champNews, Does.StartWith("[챔피언] 삼성 라이온즈, 2026 시즌 대망의 한국시리즈 우승!"));
            Assert.That(root.Q<Label>("my-record").text, Does.StartWith("한국시리즈 우승 · 삼성 4승"));
            Assert.That(root.Q("screen").ClassListContains("ps-theme"), Is.True, "우승 직후에도 금색");
            Assert.That(root.Q<Button>("start-game").text, Is.EqualTo("시즌 결산 및 2027 시즌 준비"));
            Assert.That(root.Q<Label>("last-result").text, Does.EndWith(" · 우승!"));
            Layout("우승 로비");

            // 4) 결산: 2027 정규시즌, 테마 해제, 요약에 우승
            Submit(root.Q<Button>("start-game"));
            yield return null;
            Assert.That((manager.Season.Year, manager.Season.Phase), Is.EqualTo((2027, SeasonPhase.RegularSeason)));
            Assert.That(root.Q("screen").ClassListContains("ps-theme"), Is.False);
            Assert.That(root.Q<Label>("last-result").text, Does.Contain("· 한국시리즈 우승"));
            Assert.That(root.Q("game-1"), Is.Not.Null);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panel);
        }

        [UnityTest]
        public IEnumerator Phase33_결산하면_스프링캠프_리포트가_뜨고_CP로_퓨처스_신인을_특별_훈련한다()
        {
            var manager = SeasonManager.Instance;
            while (!manager.Season.IsSeasonFinished)
            {
                manager.Season.StartNextGame();
                manager.AdvanceSeason(manager.Season.ScheduleScore(4, 2));
            }
            FinishPostseason(manager);
            yield return RestartFromLobby();
            manager = SeasonManager.Instance;
            manager.AgingRandom = new SeededRandom(3333);
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            var doc = lobby.GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panel = Object.Instantiate(doc.panelSettings);
            panel.targetTexture = rt;
            doc.panelSettings = panel;
            yield return null;
            yield return null;
            void Layout(string label)
            {
                var issues = LayoutChecker.Check(LobbyRoot().Q("screen"));
                Assert.That(issues, Is.Empty, $"[{label}]\n" + string.Join("\n", issues));
            }
            var root = LobbyRoot();
            Assert.That(lobby.CampReportOpen, Is.False);
            Assert.That(root.Q<Label>("camp-points").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "CP 0이면 숨김");

            // 1) 결산 → 스프링캠프 리포트
            Submit(root.Q<Button>("start-game"));
            yield return null;
            Assert.That(lobby.CampReportOpen, Is.True);
            var camp = manager.LastRollover.Camp;
            Assert.That(root.Q<Label>("camp-title").text, Is.EqualTo("2027 스프링캠프 리포트"));
            Assert.That(root.Q<Label>("camp-cp").text, Is.EqualTo("캠프 포인트 +5 CP 지급 · 보유 5 CP"));
            Assert.That(root.Q("camp-rookies").childCount, Is.EqualTo(camp.Rookies.Count));
            Assert.That(root.Q("camp-rookies").Q<Label>().text, Does.StartWith($"1R {camp.Rookies[0].Player.Name} · "));
            Assert.That(root.Q("camp-decline").childCount, Is.GreaterThan(0));
            Layout("스프링캠프 리포트");
            Submit(root.Q<Button>("camp-close"));
            yield return null;
            Assert.That(lobby.CampReportOpen, Is.False);
            Assert.That(manager.PendingCampReport, Is.Null, "한 번만");

            // 2) CP 확인 → 퓨처스 신인 칩 → 선수 상세
            Assert.That(root.Q<Label>("camp-points").text, Is.EqualTo("캠프 포인트 5 CP"));
            var rookie = manager.Season.Rookies.Last();
            var chip = root.Q("fut-" + rookie.Id);
            Assert.That(chip, Is.Not.Null);
            Click(chip);
            yield return null;
            Assert.That(lobby.PlayerDetail.PlayerId, Is.EqualTo(rookie.Id));
            Assert.That(root.Q<Label>("pd-role").text, Does.StartWith("퓨처스 · 2027 신인"));
            var train = root.Q<Button>("pd-train");
            Assert.That((train.resolvedStyle.display, train.enabledSelf), Is.EqualTo((DisplayStyle.Flex, true)));
            Assert.That(root.Q<Button>("pd-release").enabledSelf, Is.False, "퓨처스는 방출 불가");
            Assert.That(root.Q<Label>("pd-form").text, Is.EqualTo("에이징 폼 ±0%"));
            Layout("퓨처스 신인 상세");

            // 3) 특별 훈련 → CP·폼 즉시 갱신
            Submit(train);
            yield return null;
            Assert.That(manager.Season.CampPoints, Is.EqualTo(4));
            Assert.That(manager.Season.ProgressionOf(rookie.Id).Form, Is.EqualTo(0.01).Within(1e-9));
            Assert.That(root.Q<Label>("pd-form").text, Is.EqualTo("에이징 폼 +1%"));
            Assert.That(root.Q("pd-form-fill").ClassListContains("pd-form-fill--up"), Is.True);
            Assert.That(root.Q<Label>("pd-train-note").text, Does.Contain("보유 4 CP"));
            Assert.That(root.Q<Label>("camp-points").text, Is.EqualTo("캠프 포인트 4 CP"));
            Assert.That(lobby.PlayerDetail.IsOpen, Is.True, "모달은 열어 둔다");

            // 24세 넘는 선수는 훈련 버튼이 없다
            Submit(root.Q<Button>("pd-close"));
            Click(root.Q("lineup-1"));
            yield return null;
            Assert.That(root.Q<Button>("pd-train").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            // 재시작해도 CP·폼 유지, 리포트는 다시 뜨지 않는다
            yield return RestartFromLobby();
            Assert.That(SeasonManager.Instance.Season.CampPoints, Is.EqualTo(4));
            Assert.That(SeasonManager.Instance.Rosters.Get("삼성 라이온즈").Reserves.Single(p => p.Id == rookie.Id).Form, Is.EqualTo(0.01).Within(1e-9));
            Assert.That(Object.FindAnyObjectByType<LobbyController>().CampReportOpen, Is.False);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panel);
        }

        static string[] ConditionClasses(VisualElement container) =>
            container.Query(className: "col-cond").ToList()
                .Select(c => c.GetClasses().FirstOrDefault(n => n.StartsWith("cond--")) ?? "").ToArray();

        IEnumerator PlayDefaultGameAndReturn()
        {
            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            Assert.That(game.ChooseTactic(TacticKind.Default), Is.True);
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            yield return null;
            Submit(game.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ctl-game-end"));
            yield return WaitForScene(SeasonManager.LobbyScene);
        }

        [UnityTest]
        public IEnumerator 로비에_컨디션이_보이고_경기에_나선_선수는_지치며_쉬면_회복하고_선발은_로테이션이_돈다()
        {
            var manager = SeasonManager.Instance;
            manager.UseGeneratedScenario = true;
            manager.ScenarioRandom = new SeededRandom(20261017);
            manager.ConditionRandom = new ScriptedRandom(0.0); // 출장 야수는 모두 하락 판정
            var mine = manager.MyRoster;
            var root = LobbyRoot();

            Assert.That(ConditionClasses(root.Q("roster")), Is.EqualTo(Enumerable.Repeat("cond--3", 9)), "처음엔 전원 보통");
            Assert.That(ConditionClasses(root.Q("bench")), Is.EqualTo(Enumerable.Repeat("cond--3", 5)));
            Assert.That(root.Q("rotation").childCount, Is.EqualTo(5), "선발 5명");
            Assert.That(root.Q("rotation-1").ClassListContains("lobby-row--today"), Is.True);
            Assert.That(root.Q("rotation-1").Query<Label>().ToList().Select(l => l.text), Has.Member("오늘 선발"));
            yield return CheckLobbyLayout("컨디션 첫 로비");

            // 1경기 (삼성 홈): 라인업 9명 출장 → 지침, 벤치 → 좋음, 선발 코너(퀵 시뮬 78구+) → 위험
            yield return PlayDefaultGameAndReturn();
            root = LobbyRoot();
            Assert.That(manager.LastReport.Conditions, Is.Not.Empty);
            Assert.That(ConditionClasses(root.Q("roster")), Is.EqualTo(Enumerable.Repeat("cond--2", 9)));
            Assert.That(root.Q("roster").Query<Label>(className: "cond-text").First().text, Is.EqualTo("지침"));
            Assert.That(ConditionClasses(root.Q("bench")), Is.EqualTo(Enumerable.Repeat("cond--4", 5)));
            Assert.That(manager.Season.ConditionOf(mine.StartingPitcher.Id), Is.EqualTo(PlayerCondition.Exhausted));
            Assert.That(ConditionClasses(root.Q("rotation")), Is.EqualTo(new[] { "cond--1", "cond--4", "cond--4", "cond--4", "cond--4" }));
            Assert.That(manager.Season.LastStarterOf(manager.Season.MyTeam), Is.EqualTo(mine.Starters[0].Id));
            Assert.That(root.Q("rotation-2").ClassListContains("lobby-row--today"), Is.True, "다음 경기는 2번 선발");
            yield return CheckLobbyLayout("컨디션 1경기 후");

            // 지친 3번 타자를 벤치(좋음)와 바꾸고 2경기 → 벤치로 간 선수는 회복, 새로 나간 선수는 하락
            string tired = mine.AtOrder(3).Id, fresh = mine.Bench[0].Id;
            Assert.That(manager.SwapLineup(LineupSpot.Batting(3), LineupSpot.Bench(0)), Is.True);
            Assert.That(manager.GameRosterOf(manager.Season.MyTeam).StartingPitcher.Id, Is.EqualTo(mine.Starters[1].Id));
            yield return PlayDefaultGameAndReturn();
            Assert.That(manager.Season.ConditionOf(tired), Is.EqualTo(PlayerCondition.Normal), "지침(2) + 휴식 1 → 보통");
            Assert.That(manager.Season.ConditionOf(fresh), Is.EqualTo(PlayerCondition.Normal), "좋음(4) − 출장 1 → 보통");
            Assert.That(manager.Season.LastStarterOf(manager.Season.MyTeam), Is.EqualTo(mine.Starters[1].Id), "2번 선발이 던졌다");
            Assert.That(manager.Season.ConditionOf(mine.Starters[0].Id), Is.EqualTo(PlayerCondition.Tired), "1번 선발 휴식 회복");

            // 재시작해도 컨디션·순번이 이어진다.
            yield return RestartFromLobby();
            Assert.That(SeasonManager.Instance.Season.ConditionOf(tired), Is.EqualTo(PlayerCondition.Normal));
            Assert.That(LobbyRoot().Q("rotation-3").ClassListContains("lobby-row--today"), Is.True);
        }

        [UnityTest]
        public IEnumerator 경기_화면_타자_카드와_툴팁에_컨디션이_보인다()
        {
            var manager = SeasonManager.Instance;
            var batter = manager.MyRoster.AtOrder(3);
            manager.Season.SetCondition(batter.Id, PlayerCondition.Excellent);
            Submit(LobbyRoot().Q<Button>("start-game"));
            yield return WaitForScene(SeasonManager.GameScene);
            var game = Object.FindAnyObjectByType<GameScreenController>();
            game.Playback.Pause();
            game.Playback.SkipToEnd();
            yield return null;
            var root = game.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Q<Label>("batter-name").text, Is.EqualTo(batter.Name));
            Assert.That(root.Q<Label>("batter-fitness").text, Is.EqualTo("컨디션 최상 (5/5)"));
            game.PreviewTactic(TacticKind.SwingAway);
            yield return null;
            Assert.That(root.Q<Label>("tactic-desc").text, Does.Contain("타자 컨디션 최상 +4%"));
        }
    }
}
