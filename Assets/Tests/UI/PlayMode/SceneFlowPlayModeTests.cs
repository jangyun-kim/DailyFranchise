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
            Assert.That(root.Q<Label>("last-result").text, Does.StartWith("경기 1 · 서울 블루윙스 vs 부산 웨이브스 · 패 3:2 · "));
            Assert.That(SeasonManager.Instance.LastReport.MyOutcome, Is.EqualTo(GameOutcome.Loss));
            Assert.That(SeasonManager.Instance.LastReport.Saved, Is.True, "로비 복귀 전에 저장");
            Assert.That(root.Q<Label>("next-game").text, Does.StartWith("경기 2 · "), "일정 전진");

            // 대시보드: 이번 경기 득실점(홈 2 : 원정 3)이 시즌 누적에 더해져 바로 보인다.
            Assert.That((SeasonManager.Instance.LastReport.MyRunsScored, SeasonManager.Instance.LastReport.MyRunsAllowed), Is.EqualTo((2, 3)));
            Assert.That(root.Q<Label>("stat-rs").text, Is.EqualTo(me.RunsScored.ToString()));
            Assert.That(root.Q<Label>("stat-ra").text, Is.EqualTo(me.RunsAllowed.ToString()));
            Assert.That(root.Q<Label>("stat-diff").text, Is.EqualTo(me.RunDifferentialText));
            Assert.That(root.Q<Label>("stat-l10").text, Is.EqualTo("0승 1패"));
            Assert.That(root.Q("team-leaders").childCount, Is.EqualTo(4), "타율·홈런·평균자책·세이브 1위");
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

            // 2경기: 부산(원정) vs 인천(홈). 화면은 장면을 뒤집어 9회초 부산 공격으로 그리고, 스퀴즈 실패로 2:3 패.
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
            Assert.That(gameRoot.Q<Label>("away-name").text, Is.EqualTo("부산 웨이브스"));
            Assert.That(gameRoot.Q<Label>("home-name").text, Is.EqualTo("인천 하버스"));
            Assert.That(gameRoot.Query<Label>(className: "team-badge").ToList().Select(l => l.text), Is.EqualTo(new[] { "부산", "인천" }));
            Assert.That(gameRoot.Query<Label>(className: "ls-cell--team").ToList().Select(l => l.text).Where(t => t != ""),
                Is.EqualTo(new[] { "부산", "인천" }), "스코어보드 행: 원정 → 홈");
            Assert.That(gameRoot.Q<Label>("roster-title").text, Is.EqualTo("내 선수단 · 부산"));
            Assert.That(gameRoot.Q<Label>("pitcher-team").text, Does.StartWith("인천 · "), "상대 투수");
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
            Assert.That(root.Q<Label>("roster-source").text, Does.Contain(MasterDatabase.RosterFileName));
        }

        [UnityTest]
        public IEnumerator 경기_화면의_좌측_선수단과_타석_카드가_DB_선수_이름으로_표시된다()
        {
            var db = SeasonManager.Instance.Database;
            var mine = db.GetRoster("부산 웨이브스");
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
            Assert.That(root.Q<Label>("batter-name").text, Is.EqualTo("성지환"), "타석 카드: 최도윤 → 성지환");
            Assert.That(root.Q<Label>("batter-team").text, Does.Contain("좌투좌타").And.Contain("RF"));
            Assert.That(root.Q<Label>("pitcher-name").text, Is.EqualTo("염태준"), "상대 선발: 강민혁 → 염태준");
            Assert.That(root.Q<Label>("featured-name").text, Does.StartWith("성지환"));
            Assert.That(root.Q<Label>("roster-source").text, Is.EqualTo($"표시 데이터: {MasterDatabase.RosterFileName} (2026)"));

            // Phase 18: 카드 확장 지표 (등번호·나이, 타자 wRC+·WAR, 투수 WHIP·FIP·승패세홀)
            var batter = db.FindPlayer("WV003");
            var pitcher = db.FindPlayer("BW010");
            Assert.That(root.Q<Label>("batter-profile").text, Is.EqualTo($"No.{batter.Number} · {batter.Age}세"));
            Assert.That(ChipTexts(root, "batter-stats"), Is.EqualTo(new[] { $"wRC+ {batter.WrcPlus:0}", $"WAR {batter.War:0.00}" }));
            Assert.That(ChipTexts(root, "pitcher-stats"), Is.EqualTo(new[] { $"WHIP {pitcher.Whip:0.00}", $"FIP {pitcher.Fip:0.00}" }));
            Assert.That(root.Q<Label>("pitcher-record").text,
                Is.EqualTo($"{pitcher.Wins}승 {pitcher.Losses}패 · {pitcher.Saves}세이브 · {pitcher.Holds}홀드"));

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
                Assert.That(desc, Does.Contain("근거 · 성지환 타율 .318 vs 염태준 평균자책 3.21 · 득점권"));
                Assert.That(desc, Does.Contain("보정 · 플래툰 열세(좌투 vs 좌타) -5% · 득점권 클러치 +7%"), "Phase 17 보정 내역");
                Assert.That(desc, Does.EndWith(TacticText.Odds(branch[tactic], rate)));
                var issues = LayoutChecker.Check(root.Q("screen"));
                Assert.That(issues, Is.Empty, $"[{tactic} 설명]\n" + string.Join("\n", issues));
            }
            // Phase 17 기대값 51%에서 Phase 20 체력 저하(염태준 89구, 체력 11% → 피안타 +5%)를 더해 53%.
            // Phase 24 케미스트리(태그 상위 20%): 1루 주자 하윤재(Speed) 발야구 +4%. 서울은 Glove가 2B·SS·DH라
            // 센터라인 2명뿐이라 철벽 센터라인이 없고(카운터 없음), 성지환은 태그가 없어 거포 클린업도 없다 → +4%, 55%.
            // (Phase 23 상위 30%에서는 발야구 +2 · 철벽 −1.5 · 거포 +2 = +2.5%로 같은 55%였다.)
            Assert.That(root.Q<Label>("tactic-desc").text, Does.EndWith("성공 확률 55% · 실패 45%"),
                "좌투 vs 좌타 -5%, 클러치 +7%, 체력 저하 +5%, 케미 +4%");
            Assert.That(root.Q<Label>("tactic-desc").text, Does.Contain("· 케미 발야구 +4%"));
            Assert.That(root.Q<Label>("tactic-desc").text, Does.Contain("· 체력 저하 +5%").And.Contain("투수 · 염태준 89구 · 체력 11% (저하)"));
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
            Assert.That(root.Q("bench").childCount, Is.EqualTo(3));
            Assert.That(Names(root.Q("roster"))[2], Is.EqualTo("성지환"));
            Assert.That(Names(root.Q("bench"))[0], Is.EqualTo("소윤호"));

            // 1) 3번 타자 클릭 → 강조
            Click(root.Q("lineup-3"));
            yield return null;
            Assert.That(root.Q("lineup-3").ClassListContains("lobby-row--selected"), Is.True);
            Assert.That(root.Q("bench-1").ClassListContains("lobby-row--swappable"), Is.True);
            Assert.That(root.Q<Label>("lineup-hint").text, Does.StartWith("성지환 선택됨"));

            // 2) 벤치 1번 클릭 → 맞바꾸고 바로 다시 그림
            Click(root.Q("bench-1"));
            yield return null;
            Assert.That(Names(root.Q("roster"))[2], Is.EqualTo("소윤호"));
            Assert.That(root.Q("lineup-3").Q<Label>(className: "col-pos").text, Is.EqualTo("RF"));
            Assert.That(Names(root.Q("bench"))[0], Is.EqualTo("성지환"));
            Assert.That(root.Query(className: "lobby-row--selected").ToList(), Is.Empty, "교체 후 선택 해제");
            Assert.That(File.ReadAllText(TestSavePath), Does.Contain("WV010"), "세이브 파일에 덮어씀");

            // 벤치끼리는 바꾸지 않고 선택만 옮긴다.
            Click(root.Q("bench-1"));
            Click(root.Q("bench-2"));
            yield return null;
            Assert.That(root.Q("bench-2").ClassListContains("lobby-row--selected"), Is.True);
            Assert.That(root.Q("bench-1").ClassListContains("lobby-row--selected"), Is.False);
            Assert.That(Names(root.Q("bench")), Is.EqualTo(new[] { "성지환", "피준서", "국태민" }));
            Click(root.Q("bench-2")); // 취소
            yield return null;

            // 껐다 켜도 유지
            yield return RestartFromLobby();
            root = LobbyRoot();
            Assert.That(Names(root.Q("roster"))[2], Is.EqualTo("소윤호"));
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
            Assert.That(lineupNames[2], Is.EqualTo("소윤호"));
            Assert.That(gameRoot.Q("bench").Query<Label>(className: "roster-name").First().text, Is.EqualTo("성지환"));
            Assert.That(gameRoot.Q<Label>("batter-name").text, Is.EqualTo("소윤호"), "장면의 3번 타자 자리");
            Assert.That(game.Calculator.DecisionContext(game.Fixture.Branch, game.Fixture.Events).Batter.Name, Is.EqualTo("소윤호"));
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

            // CPU 선발이 108구로 고갈되자 상대 DB 불펜 투수로 바뀐다.
            var cpu = game.PitchingChanges.Single(c => c.ByCpu);
            var opponentBullpen = manager.Database.GetRoster(opponent).Bullpen.Select(p => p.Name).ToArray();
            Assert.That(cpu.Reason, Is.EqualTo("체력 고갈"));
            Assert.That(opponentBullpen, Has.Member(game.Roster.GetPlayer(cpu.To).Name));

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
            Assert.That(root.Q<Label>("today").text, Is.EqualTo("오늘 " + FranchiseFinance.TodayText(System.DateTime.Now)));
            Assert.That(root.Q<Label>("fo-budget").text, Is.EqualTo("₩ 5,000,000"));
            Assert.That(root.Q<Label>("fo-fan").text, Is.EqualTo("50%"));
            Assert.That(root.Q("fo-fan-fill").style.width.value, Is.EqualTo(Length.Percent(50)));
            Assert.That(root.Q("fo-fan-fill").ClassListContains("fo-fill--happy") || root.Q("fo-fan-fill").ClassListContains("fo-fill--angry"),
                Is.False, "50%는 보통");
            Assert.That(root.Q<Label>("fo-season").text, Is.EqualTo("0 / 10경기"));
            Assert.That(root.Q<Label>("fo-budget-delta").text, Is.EqualTo("승 +₩50,000 · 패 +₩20,000"), "아직 치른 경기가 없으면 규칙 안내");
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
            var change = FranchiseFinance.For(report.MyOutcome);
            Assert.That(report.Finance, Is.EqualTo(change));
            root = LobbyRoot();
            Assert.That(root.Q<Label>("fo-budget").text, Is.EqualTo(FranchiseFinance.BudgetText(5_000_000 + change.BudgetDelta)), "즉시 갱신");
            Assert.That(root.Q<Label>("fo-fan").text, Is.EqualTo($"{50 + change.FanDelta}%"));
            Assert.That(root.Q("fo-fan-fill").style.width.value, Is.EqualTo(Length.Percent(50 + change.FanDelta)));
            Assert.That(root.Q<Label>("fo-budget-delta").text, Does.Contain(FranchiseFinance.BudgetDeltaText(change.BudgetDelta)));
            Assert.That(root.Q<Label>("fo-fan-delta").text, Does.EndWith(FranchiseFinance.FanDeltaText(change.FanDelta)));
            Assert.That(root.Q<Label>("fo-season").text, Is.EqualTo("1 / 10경기"));
            Assert.That((manager.Season.Budget, manager.Season.FanApproval), Is.EqualTo((5_000_000 + change.BudgetDelta, 50 + change.FanDelta)));
            yield return CheckLobbyLayout("경기 후 로비");

            // 재시작해도 저장된 재정으로 이어진다.
            yield return RestartFromLobby();
            Assert.That(LobbyRoot().Q<Label>("fo-budget").text, Is.EqualTo(FranchiseFinance.BudgetText(5_000_000 + change.BudgetDelta)));
            Assert.That(LobbyRoot().Q<Label>("fo-fan").text, Is.EqualTo($"{50 + change.FanDelta}%"));
        }

        [UnityTest]
        public IEnumerator 팬_만족도_80_이상은_초록_30_미만은_빨강으로_보인다()
        {
            var manager = SeasonManager.Instance;
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            foreach (var (fan, cls) in new[] { (85, "fo-fill--happy"), (20, "fo-fill--angry") })
            {
                manager.Season.SetFinance(1_234_567, fan);
                lobby.enabled = false;
                lobby.enabled = true; // 다시 그리기
                yield return null;
                var root = LobbyRoot();
                Assert.That(root.Q("fo-fan-fill").ClassListContains(cls), Is.True, $"{fan}% → {cls}");
                Assert.That(root.Q<Label>("fo-fan").ClassListContains(cls.Replace("fill", "percent")), Is.True);
                Assert.That(root.Q<Label>("fo-budget").text, Is.EqualTo("₩ 1,234,567"));
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
        public IEnumerator 프런트_오피스에_신뢰도_TP와_팬덤_특전_해금_상태가_보이고_FA_버튼은_잠겨_있다()
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
            Assert.That((fa.text, fa.enabledSelf), Is.EqualTo(("스카우트 / FA", false)));

            manager.Season.SetTrust(150, 0, 0, 0);
            manager.Season.SetUnlockedSlots(FandomSlot.Uniform | FandomSlot.Cheerleader);
            lobby.enabled = false;
            lobby.enabled = true;
            yield return null;
            yield return null;
            root = LobbyRoot();
            Assert.That(root.Q<Label>("fo-trust").text, Is.EqualTo("150 / 200"));
            Assert.That(root.Q("fo-trust-fill").style.width.value, Is.EqualTo(Length.Percent(75)));
            Assert.That(root.Q("fo-trust-gauge").tooltip, Is.EqualTo("천장 도달 · 다음 협상 확정 영입"));
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
            var active = root.Q<Label>("chem-active");
            Assert.That(active.text, Is.EqualTo("활성화됨: 발야구"));
            Assert.That(active.ClassListContains("chem-active--on"), Is.True);
            Assert.That(root.Q("lineup-1").Q<Label>(className: "chem-badge").text, Is.EqualTo("주"));
            Assert.That(root.Q("lineup-4").Q<Label>(className: "chem-badge").text, Is.EqualTo("장"));
            Assert.That(root.Q("lineup-7").Q<Label>(className: "chem-badge").text, Is.EqualTo("수"));
            Assert.That(root.Q("lineup-3").Q<Label>(className: "chem-badge").ClassListContains("chem-badge--none"), Is.True);
            var issues = LayoutChecker.Check(root.Q("screen"));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
            int renders = lobby.ChemistryRenderCount;

            // 태그 없는 3번·5번 타순 맞바꿈 → 케미스트리 그대로, 다시 그리지 않는다.
            lobby.ClickLineupSpot(LineupSpot.Batting(3));
            lobby.ClickLineupSpot(LineupSpot.Batting(5));
            yield return null;
            Assert.That(LobbyRoot().Q("lineup-3").Q<Label>(className: "col-name").text, Is.EqualTo("길태윤"), "교체는 됐다");
            Assert.That(lobby.ChemistryRenderCount, Is.EqualTo(renders));

            // 8번 여민규(Speed) → 벤치 2번(태그 없음): 발야구 꺼짐
            lobby.ClickLineupSpot(LineupSpot.Batting(8));
            lobby.ClickLineupSpot(LineupSpot.Bench(1));
            yield return null;
            active = LobbyRoot().Q<Label>("chem-active");
            Assert.That(active.text, Is.EqualTo("활성화된 케미스트리 없음"));
            Assert.That(active.ClassListContains("chem-active--on"), Is.False);
            Assert.That(lobby.ChemistryRenderCount, Is.EqualTo(renders + 1));
            Assert.That(lobby.Chemistry.Kinds, Is.Empty);

            // 대주자 국태민(Speed)을 8번에 넣으면 다시 켜진다.
            lobby.ClickLineupSpot(LineupSpot.Batting(8));
            lobby.ClickLineupSpot(LineupSpot.Bench(2));
            yield return null;
            Assert.That(LobbyRoot().Q<Label>("chem-active").text, Is.EqualTo("활성화됨: 발야구"));
            Assert.That(lobby.Chemistry.Members(ChemistryKind.SpeedBall), Is.EquivalentTo(new[] { "WV001", "WV002", "WV012" }));
            Assert.That(lobby.ChemistryRenderCount, Is.EqualTo(renders + 2));

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
            Assert.That(ConditionClasses(root.Q("bench")), Is.EqualTo(Enumerable.Repeat("cond--3", 3)));
            Assert.That(root.Q("rotation").childCount, Is.EqualTo(5), "선발 5명");
            Assert.That(root.Q("rotation-1").ClassListContains("lobby-row--today"), Is.True);
            Assert.That(root.Q("rotation-1").Query<Label>().ToList().Select(l => l.text), Has.Member("오늘 선발"));
            yield return CheckLobbyLayout("컨디션 첫 로비");

            // 1경기 (부산 홈): 라인업 9명 출장 → 지침, 벤치 → 좋음, 선발 차은호(퀵 시뮬 78구+) → 위험
            yield return PlayDefaultGameAndReturn();
            root = LobbyRoot();
            Assert.That(manager.LastReport.Conditions, Is.Not.Empty);
            Assert.That(ConditionClasses(root.Q("roster")), Is.EqualTo(Enumerable.Repeat("cond--2", 9)));
            Assert.That(root.Q("roster").Query<Label>(className: "cond-text").First().text, Is.EqualTo("지침"));
            Assert.That(ConditionClasses(root.Q("bench")), Is.EqualTo(Enumerable.Repeat("cond--4", 3)));
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
