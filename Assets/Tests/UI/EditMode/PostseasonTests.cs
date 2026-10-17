using System.Collections.Generic;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 32: 시즌 단계(정규시즌 → 포스트시즌 → 오프시즌), 정규시즌 1·2위 한국시리즈(7전 4선승제, 2-3-2 홈, 4승 선착 조기 종료),
    /// 우승 보상(₩20억·팬 100%)과 뉴스, 탈락 시 결과 시뮬레이션, 저장·이전 파일 호환, 결산.
    /// 순위가 흔들리지 않게 3개 구단 작은 리그(삼성 1위 · LG 2위 · KIA 3위, 정규시즌 2경기)로 검증한다.
    /// </summary>
    public class PostseasonTests
    {
        const string Samsung = "삼성 라이온즈";
        const string Lg = "LG 트윈스";
        const string Kia = "KIA 타이거즈";

        /// <summary>정규시즌 2경기 (삼성 vs LG). <paramref name="mineFirst"/>면 삼성 1위·LG 2위, 아니면 LG 1위·KIA 2위·삼성 3위.</summary>
        static SeasonState League(bool mineFirst = true)
        {
            var schedule = new List<ScheduledGame>
            {
                new ScheduledGame { Number = 1, Date = "9.28 (월)", AwayTeam = Lg, HomeTeam = Samsung, Stadium = SeasonState.KboStadiums[Samsung] },
                new ScheduledGame { Number = 2, Date = "9.29 (화)", AwayTeam = Samsung, HomeTeam = Lg, Stadium = SeasonState.KboStadiums[Lg] },
            };
            var standings = mineFirst
                ? new List<StandingRow>
                {
                    new StandingRow { Team = Samsung, Wins = 80, Losses = 40 },
                    new StandingRow { Team = Lg, Wins = 70, Losses = 50 },
                    new StandingRow { Team = Kia, Wins = 60, Losses = 60 },
                }
                : new List<StandingRow>
                {
                    new StandingRow { Team = Samsung, Wins = 50, Losses = 70 },
                    new StandingRow { Team = Lg, Wins = 80, Losses = 40 },
                    new StandingRow { Team = Kia, Wins = 75, Losses = 45 },
                };
            return new SeasonState("2026 시즌", Samsung, schedule, standings);
        }

        static void PlayRegular(SeasonState s)
        {
            var random = new SeededRandom(32);
            while (s.Phase == SeasonPhase.RegularSeason)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(5, 3), random);
            }
        }

        /// <summary>내 한국시리즈 경기 한 판 (내 구단 득점·상대 득점).</summary>
        static void PlaySeries(SeasonState s, int mine, int theirs)
        {
            s.StartNextGame();
            s.FinishCurrentGame(s.ScheduleScore(mine, theirs), new SeededRandom(1));
        }

        // ---------- 개막 ----------

        [Test]
        public void 정규시즌_마지막_경기_뒤_1위와_2위의_7전_시리즈가_2_3_2_홈으로_열린다()
        {
            var s = League();
            Assert.That(s.Phase, Is.EqualTo(SeasonPhase.RegularSeason));
            Assert.That(s.Postseason, Is.Null);
            PlayRegular(s);

            Assert.That(s.Phase, Is.EqualTo(SeasonPhase.PostSeason));
            var ps = s.Postseason;
            Assert.That((ps.TopSeed, ps.SecondSeed), Is.EqualTo((Samsung, Lg)));
            Assert.That(ps.Games.Count, Is.EqualTo(PostseasonSeries.ScheduledGames));
            Assert.That(ps.Games.Select(g => g.HomeTeam), Is.EqualTo(new[] { Samsung, Samsung, Lg, Lg, Lg, Samsung, Samsung }), "1·2·6·7차전 1위 홈");
            Assert.That(ps.Games.Select(g => g.Date),
                Is.EqualTo(new[] { "10.1 (목)", "10.2 (금)", "10.4 (일)", "10.5 (월)", "10.6 (화)", "10.8 (목)", "10.9 (금)" }),
                "정규시즌 마지막 경기 이틀 뒤부터, 2·5차전 뒤 이동일");
            Assert.That(ps.Games[2].Stadium, Is.EqualTo("잠실야구장"));
            Assert.That(s.InPostseason, Is.True);
            Assert.That(s.NextGame, Is.SameAs(ps.Games[0]), "다음 경기 = 한국시리즈 1차전");
            Assert.That(s.SeasonTitle, Is.EqualTo("2026 시즌 · 한국시리즈"));
            Assert.That(s.LatestNews(1)[0].Text, Is.EqualTo("[한국시리즈] 삼성 라이온즈, 2026 한국시리즈 진출! LG 트윈스와 7전 4선승제 격돌"));
            Assert.That(s.StandingOf(Samsung).Wins, Is.EqualTo(82), "정규시즌 성적은 그대로");
        }

        // ---------- 시리즈 ----------

        [Test]
        public void 한_팀이_4승에_닿으면_남은_경기를_지우고_우승_상금과_팬_만족도_100을_받는다()
        {
            var s = League();
            PlayRegular(s);
            long budget = s.Budget;
            int wins = s.StandingOf(Samsung).Wins;

            PlaySeries(s, 5, 3);
            PlaySeries(s, 1, 2);
            Assert.That(s.Postseason.RecordText(Samsung), Is.EqualTo("삼성 1승 - 1승 LG"));
            Assert.That(s.LatestNews(1)[0].Text, Is.EqualTo("[한국시리즈] 삼성 라이온즈, 한국시리즈 2차전 LG 트윈스에 2:1 패배 · 시리즈 1승 1패"));
            PlaySeries(s, 4, 0);
            PlaySeries(s, 6, 2);
            Assert.That(s.Phase, Is.EqualTo(SeasonPhase.PostSeason));
            Assert.That(s.Budget, Is.EqualTo(budget), "우승 전에는 보상 없음");
            PlaySeries(s, 3, 2); // 5차전, 4승 1패

            var ps = s.Postseason;
            Assert.That((ps.Champion, ps.TopWins, ps.SecondWins, ps.IsOver), Is.EqualTo((Samsung, 4, 1, true)));
            Assert.That(ps.Games.Count, Is.EqualTo(5), "6·7차전은 지운다");
            Assert.That(s.Phase, Is.EqualTo(SeasonPhase.OffSeason));
            Assert.That(s.NextGame, Is.Null);
            // Phase 42: 우승으로 팬 만족도 100% → 팬덤 레벨업 (굿즈 완판 특별 예산, 게이지 60%)
            Assert.That(s.Budget, Is.EqualTo(budget + SeasonState.ChampionPrize + FandomRules.LevelBudgetBonus), "우승 상금 ₩20억 + 팬덤 특별 예산");
            Assert.That(s.ChampionAward.Value.FanDelta, Is.GreaterThan(0), "우승이 팬 만족도를 100%까지 끌어올린 양");
            Assert.That(s.LastFandomLevelUp.Level, Is.EqualTo(1));
            Assert.That((s.FandomLevel, s.FanApproval), Is.EqualTo((1, FandomRules.LevelResetApproval)));
            Assert.That(s.ChampionAward.Value.BudgetDelta, Is.EqualTo(2_000_000_000L));
            Assert.That(s.LatestNews(1)[0].Text, Is.EqualTo("[챔피언] 삼성 라이온즈, 2026 시즌 대망의 한국시리즈 우승!"));
            Assert.That(s.LatestNews(1)[0].Kind, Is.EqualTo(NewsKind.Championship));
            Assert.That(s.StandingOf(Samsung).Wins, Is.EqualTo(wins), "시리즈는 순위표에 넣지 않는다");
            Assert.That(s.SeasonTitle, Is.EqualTo("2026 시즌 · 시즌 종료"));
            Assert.Throws<System.InvalidOperationException>(() => s.StartNextGame());
        }

        [Test]
        public void 시리즈에서_지면_준우승이고_보상은_없다()
        {
            var s = League();
            PlayRegular(s);
            long budget = s.Budget;
            int fan = s.FanApproval;
            for (int i = 0; i < 4; i++) PlaySeries(s, 1, 3);
            Assert.That((s.Postseason.Champion, s.Phase), Is.EqualTo((Lg, SeasonPhase.OffSeason)));
            Assert.That(s.Postseason.Games.Count, Is.EqualTo(4));
            Assert.That((s.Budget, s.FanApproval, s.ChampionAward), Is.EqualTo((budget, fan, (FinanceChange?)null)));
            Assert.That(s.LatestNews(1)[0].Text, Is.EqualTo("[챔피언] LG 트윈스, 2026 시즌 대망의 한국시리즈 우승!"));
            Assert.That(LobbyController.RecordText(s), Is.EqualTo("한국시리즈 준우승 · 삼성 0승 - 4승 LG"));
        }

        [Test]
        public void 무승부는_승리가_아니고_7경기로_모자라면_1위_구장에서_한_경기를_더한다()
        {
            var ps = PostseasonSeries.Create(Samsung, Lg, 2026, "9.29 (화)", SeasonState.KboStadiums);
            // 삼성(1위) 3승 · LG 3승 · 1무 → 8차전
            var scores = new[] { (3, 5), (1, 2), (3, 3), (1, 0), (0, 2), (4, 1), (5, 1) }; // 원정:홈 (1·2·6·7차전 삼성 홈) → 삼성 3승·LG 3승·1무
            foreach (var (a, h) in scores) ps.Record(new GameScore(a, h));
            Assert.That((ps.TopWins, ps.SecondWins, ps.Draws, ps.IsOver), Is.EqualTo((3, 3, 1, false)));
            Assert.That(ps.Games.Count, Is.EqualTo(8));
            var extra = ps.NextGame;
            Assert.That((extra.Number, extra.HomeTeam, extra.Date, extra.Stadium), Is.EqualTo((8, Samsung, "10.10 (토)", "대구 라이온즈파크")));
            Assert.That(ps.RecordText(), Is.EqualTo("삼성 3승 - 3승 LG (1무)"));
            ps.Record(new GameScore(2, 7));
            Assert.That((ps.Champion, ps.Games.Count), Is.EqualTo((Samsung, 8)));
        }

        // ---------- 탈락 ----------

        [Test]
        public void 내_구단이_탈락하면_다음_경기가_없고_결과_시뮬레이션으로_오프시즌에_간다()
        {
            var s = League(mineFirst: false);
            PlayRegular(s);
            Assert.That(s.Phase, Is.EqualTo(SeasonPhase.PostSeason));
            Assert.That((s.Postseason.TopSeed, s.Postseason.SecondSeed), Is.EqualTo((Lg, Kia)));
            Assert.That(s.InPostseason, Is.False);
            Assert.That(s.NextGame, Is.Null, "치를 경기가 없다");
            Assert.That(s.LatestNews(1)[0].Text, Is.EqualTo("[한국시리즈] 2026 한국시리즈 개막: LG 트윈스 vs KIA 타이거즈 · 삼성 라이온즈 가을야구 탈락"));
            Assert.That(LobbyController.NextGameText(s), Is.EqualTo("2026 한국시리즈 LG 트윈스 vs KIA 타이거즈 · 삼성 라이온즈 가을야구 탈락"));
            Assert.That(LobbyController.RecordText(s), Does.EndWith("가을야구 탈락"));
            long budget = s.Budget;

            Assert.That(s.SimulatePostseason(new SeededRandom(5)), Is.True);
            var ps = s.Postseason;
            Assert.That(s.Phase, Is.EqualTo(SeasonPhase.OffSeason));
            Assert.That(ps.Champion, Is.EqualTo(Lg).Or.EqualTo(Kia));
            Assert.That(ps.Games.Count, Is.InRange(4, 7));
            Assert.That(ps.Games.All(g => g.Score.HasValue && g.Score.Value.Away != g.Score.Value.Home), Is.True, "시뮬레이션은 무승부 없음");
            Assert.That(s.Budget, Is.EqualTo(budget), "남의 우승은 보상 없음");
            Assert.That(s.LatestNews(1)[0].Text, Is.EqualTo($"[챔피언] {ps.Champion}, 2026 시즌 대망의 한국시리즈 우승!"));
            Assert.That(s.SimulatePostseason(new SeededRandom(5)), Is.False, "이미 끝났다");
            Assert.That(LobbyController.LastResultText(s), Does.StartWith("한국시리즈 · ").And.EndWith(" 우승"));
        }

        // ---------- 경기 처리 (PostGameProcessor) ----------

        [Test]
        public void 시리즈_경기_정산은_순위표와_타_구단_경기를_건드리지_않고_우승이면_보상을_보고한다()
        {
            var s = League();
            PlayRegular(s);
            var standings = s.Standings.Select(r => (r.Team, r.Wins, r.Losses, r.Draws)).ToList();
            for (int i = 0; i < 3; i++)
            {
                s.StartNextGame();
                var r = PostGameProcessor.Process(s, s.ScheduleScore(5, 3), new SeededRandom(i), null);
                Assert.That(r.Postseason, Is.True);
                Assert.That(r.DayResults.Count, Is.EqualTo(1), "타 구단 경기 없음");
                Assert.That(r.News[0].Kind, Is.EqualTo(NewsKind.Postseason), "경기 결과 (새 팬덤 특전 뉴스가 뒤따를 수 있다)");
                Assert.That(r.News.Any(n => n.Kind == NewsKind.Game || n.Kind == NewsKind.League || n.Kind == NewsKind.Standings), Is.False);
                Assert.That(r.Champion, Is.Null);
            }
            Assert.That(s.LatestNews(1)[0].Text, Is.EqualTo("[한국시리즈] 삼성 라이온즈, 한국시리즈 3차전 LG 트윈스에 5:3 승리 · 시리즈 3승 0패"));
            s.StartNextGame();
            long before = s.Budget;
            var last = PostGameProcessor.Process(s, s.ScheduleScore(2, 0), new SeededRandom(9), null);
            Assert.That(last.Champion, Is.EqualTo(Samsung));
            Assert.That(last.ChampionAward.Value.BudgetDelta, Is.EqualTo(SeasonState.ChampionPrize));
            Assert.That(last.News.Select(n => n.Kind).Take(2), Is.EqualTo(new[] { NewsKind.Postseason, NewsKind.Championship }), "경기 결과 뒤 우승");
            Assert.That(s.Budget, Is.EqualTo(before + SeasonState.ChampionPrize + FandomRules.LevelBudgetBonus + last.Finance.BudgetDelta),
                "경기 정산 + 상금 + 팬덤 특별 예산 (Phase 42)");
            Assert.That(last.MoodBefore, Is.EqualTo(FranchiseFinance.MoodOf(last.FanApprovalBefore)), "경기 정산은 상금 전 팬 구간");
            Assert.That(last.Finance.FanDelta, Is.Zero, "우승 경기는 우승 보상이 팬 만족도를 정한다");
            Assert.That((last.FandomLevelUp?.Level, s.FanApproval), Is.EqualTo(((int?)1, FandomRules.LevelResetApproval)), "100% → 팬덤 Lv.1, 게이지 60%");
            Assert.That(s.Standings.Select(r => (r.Team, r.Wins, r.Losses, r.Draws)), Is.EqualTo(standings));
            Assert.That(LobbyController.LastResultText(s), Is.EqualTo("한국시리즈 4차전 · 승 2:0 · 삼성 4승 - 0승 LG · 우승!"));
        }

        // ---------- 저장 ----------

        [Test]
        public void 시리즈_중간과_끝을_저장하고_불러오며_단계가_없는_이전_파일은_정규시즌이다()
        {
            var s = League();
            PlayRegular(s);
            PlaySeries(s, 5, 3);
            PlaySeries(s, 1, 4);
            var loaded = SeasonState.FromSaveData(JsonUtility.FromJson<SeasonSaveData>(JsonUtility.ToJson(s.ToSaveData())));
            Assert.That(loaded.Phase, Is.EqualTo(SeasonPhase.PostSeason));
            Assert.That(loaded.Postseason.RecordText(Samsung), Is.EqualTo("삼성 1승 - 1승 LG"));
            Assert.That(loaded.Postseason.NextIndex, Is.EqualTo(2));
            Assert.That(loaded.NextGame.Number, Is.EqualTo(3));
            Assert.That(loaded.NextGame.HomeTeam, Is.EqualTo(Lg));

            for (int i = 0; i < 3; i++) PlaySeries(loaded, 6, 1);
            var done = SeasonState.FromSaveData(JsonUtility.FromJson<SeasonSaveData>(JsonUtility.ToJson(loaded.ToSaveData())));
            Assert.That((done.Phase, done.Postseason.Champion, done.Postseason.Games.Count), Is.EqualTo((SeasonPhase.OffSeason, Samsung, 5)));
            Assert.That(done.Budget, Is.EqualTo(loaded.Budget), "보상은 다시 주지 않는다");

            // 이전 파일: 단계·시리즈 필드 없음 → 정규시즌, 정규시즌을 다 치렀으면 BeginPostseasonIfDue로 연다
            var old = s.ToSaveData();
            old.Phase = 0;
            old.Postseason = null;
            var legacy = SeasonState.FromSaveData(old);
            Assert.That((legacy.Phase, legacy.Postseason), Is.EqualTo((SeasonPhase.RegularSeason, (PostseasonSeries)null)));
            Assert.That(legacy.BeginPostseasonIfDue(), Is.True);
            Assert.That(legacy.Phase, Is.EqualTo(SeasonPhase.PostSeason));
            Assert.That(legacy.BeginPostseasonIfDue(), Is.False, "한 번만");

            // 정규시즌 중 파일에 포스트시즌 단계가 적혀 있어도 정규시즌으로 읽는다
            var mid = League().ToSaveData();
            mid.Phase = (int)SeasonPhase.OffSeason;
            Assert.That(SeasonState.FromSaveData(mid).Phase, Is.EqualTo(SeasonPhase.RegularSeason));
        }

        // ---------- 결산 ----------

        [Test]
        public void 우승_뒤_결산하면_다음_시즌_정규시즌으로_돌아가고_요약에_우승이_남는다()
        {
            var s = League();
            PlayRegular(s);
            for (int i = 0; i < 4; i++) PlaySeries(s, 3, 1);
            var summary = s.TransitionToNextSeason();
            Assert.That((summary.Champion, summary.InChampionship, summary.WonChampionship), Is.EqualTo((Samsung, true, true)));
            Assert.That(summary.Text, Does.Contain("· 한국시리즈 우승"));
            Assert.That((s.Year, s.Phase, s.Postseason), Is.EqualTo((2027, SeasonPhase.RegularSeason, (PostseasonSeries)null)));
            Assert.That(s.NextGame.Number, Is.EqualTo(1));
        }

        [Test]
        public void 시리즈를_끝내지_않고_결산하면_남은_시리즈를_시뮬레이션한다()
        {
            var s = League();
            PlayRegular(s);
            var summary = s.TransitionToNextSeason();
            Assert.That(summary.Champion, Is.EqualTo(Samsung).Or.EqualTo(Lg));
            Assert.That(summary.Text, Does.Contain(summary.WonChampionship ? "한국시리즈 우승" : "한국시리즈 준우승"));
            Assert.That(s.Phase, Is.EqualTo(SeasonPhase.RegularSeason));
        }
    }
}
