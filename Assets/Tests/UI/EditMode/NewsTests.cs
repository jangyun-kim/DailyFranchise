using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 28: 리그 뉴스 큐(보관 15건), 경기·FA·방출·결산 뉴스 문장, 저장.</summary>
    public class NewsTests
    {
        const string Samsung = "삼성 라이온즈";
        const string Lg = "LG 트윈스";
        static readonly MasterDatabase Db = TestLeague.TwoTeams;

        sealed class FixedRandom : IRandomSource
        {
            readonly double _value;
            public FixedRandom(double value) => _value = value;
            public double NextDouble() => _value;
            public int Next(int maxExclusive) => 0;
        }

        static NewsItem Item(int i) => new NewsItem(NewsKind.General, "", "", "뉴스 " + i, 2026, "");

        static GameRecord Record(string away, string home, int awayRuns, int homeRuns) =>
            new GameRecord { AwayTeam = away, HomeTeam = home, Score = new GameScore(awayRuns, homeRuns) };

        static string GameText(GameRecord g, int inning = 9, bool walkOff = false, int streak = 0) =>
            NewsWriter.Game(Samsung, g, inning, walkOff, streak, 2026, "5.15 (금)").Text;

        // ---------- 큐 ----------

        [Test]
        public void 뉴스는_15건까지만_보관하고_넘치면_오래된_것부터_지운다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.RecentNews, Is.Empty);
            for (int i = 1; i <= 20; i++) s.AddNews(Item(i));
            Assert.That(SeasonState.MaxNews, Is.EqualTo(15));
            Assert.That(s.RecentNews.Count, Is.EqualTo(15));
            Assert.That(s.RecentNews.First().Headline, Is.EqualTo("뉴스 6"), "1~5는 밀려남");
            Assert.That(s.RecentNews.Last().Headline, Is.EqualTo("뉴스 20"));
            Assert.That(s.LatestNews(5).Select(n => n.Headline), Is.EqualTo(new[] { "뉴스 20", "뉴스 19", "뉴스 18", "뉴스 17", "뉴스 16" }));
            Assert.That(s.LatestNews(0), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => s.AddNews(null));
            Assert.Throws<ArgumentException>(() => new NewsItem(NewsKind.Game, "[경기]", Samsung, "", 2026, ""));
        }

        [Test]
        public void 말머리와_본문을_합친_글과_관련_구단은_CPU_구단도_담는다()
        {
            var n = new NewsItem(NewsKind.Signing, NewsWriter.OfficialTag, Lg, "LG 트윈스, 외국인 투수 영입", 2027, "스토브리그");
            Assert.That(n.Text, Is.EqualTo("[오피셜] LG 트윈스, 외국인 투수 영입"));
            Assert.That((n.Team, n.Year, n.Date), Is.EqualTo((Lg, 2027, "스토브리그")));
            Assert.That(Item(1).Text, Is.EqualTo("뉴스 1"), "말머리 없으면 본문만");
        }

        // ---------- 경기 뉴스 ----------

        [Test]
        public void 경기_종료_처리는_끝내기_승리_뉴스를_발행하고_바로_저장된다()
        {
            var s = SeasonState.CreateDummy(); // 1경기: LG(원정) vs 삼성(홈)
            s.StartNextGame();
            var report = PostGameProcessor.Process(s, new GameScore(2, 3), new SeededRandom(1), null, finalInning: 9, walkOff: true);
            var mine = report.News.First();
            Assert.That(mine.Text, Is.EqualTo("[끝내기] 삼성 라이온즈, LG 트윈스와의 접전 끝에 9회말 3:2 끝내기 승리!"));
            Assert.That((mine.Kind, mine.Team, mine.Year, mine.Date), Is.EqualTo((NewsKind.Game, Samsung, 2026, "5.15 (금)")));
            Assert.That(s.RecentNews, Is.EqualTo(report.News), "발행한 뉴스가 시즌 큐에 순서대로");
            Assert.That(SeasonState.FromSaveData(s.ToSaveData()).RecentNews.Select(n => n.Text), Is.EqualTo(report.News.Select(n => n.Text)));
        }

        [Test]
        public void 원정에서_상대_끝내기면_끝내기_패배()
        {
            Assert.That(GameText(Record(Samsung, "SSG 랜더스", 2, 3), 10, walkOff: true),
                Is.EqualTo("[끝내기] 삼성 라이온즈, SSG 랜더스에 10회말 3:2 끝내기 패배…"));
        }

        [Test]
        public void 경기_뉴스_문장은_무승부_난타전_대승_영봉_연장_일반_순으로_고른다()
        {
            Assert.That(GameText(Record(Lg, Samsung, 4, 4), 12), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스와 12회 연장 혈투 끝 4:4 무승부"));
            Assert.That(GameText(Record(Lg, Samsung, 1, 1)), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스와 1:1 무승부"));
            Assert.That(GameText(Record(Lg, Samsung, 7, 9)), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스와의 난타전 끝에 9:7 승리!"));
            Assert.That(GameText(Record(Lg, Samsung, 9, 7)), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스와의 난타전 끝에 9:7 패배"));
            Assert.That(GameText(Record(Samsung, "KT 위즈", 10, 1)), Is.EqualTo("[경기] 삼성 라이온즈, KT 위즈 상대로 10:1 대승!"));
            Assert.That(GameText(Record(Samsung, "KT 위즈", 0, 8)), Is.EqualTo("[경기] 삼성 라이온즈, KT 위즈에 8:0 대패… 마운드 붕괴"));
            Assert.That(GameText(Record(Lg, Samsung, 0, 2)), Is.EqualTo("[경기] 삼성 라이온즈 마운드, LG 트윈스 타선 봉쇄! 2:0 영봉승"));
            Assert.That(GameText(Record(Lg, Samsung, 3, 0)), Is.EqualTo("[경기] 삼성 라이온즈 타선 침묵… LG 트윈스에 3:0 영봉패"));
            Assert.That(GameText(Record(Samsung, Lg, 5, 4), 11), Is.EqualTo("[경기] 삼성 라이온즈, 연장 11회 승부 끝에 LG 트윈스 꺾고 5:4 승리"));
            Assert.That(GameText(Record(Samsung, Lg, 4, 5), 11), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스에 연장 11회 5:4 패배"));
            Assert.That(GameText(Record(Lg, Samsung, 3, 5)), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스에 5:3 승리"));
            Assert.That(GameText(Record(Lg, Samsung, 3, 5), streak: 4), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스에 5:3 승리 · 4연승 질주"));
            Assert.That(GameText(Record(Lg, Samsung, 5, 3), streak: -3), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스에 5:3 패배 · 3연패 수렁"));
            Assert.That(GameText(Record(Lg, Samsung, 3, 5), streak: 2), Is.EqualTo("[경기] 삼성 라이온즈, LG 트윈스에 5:3 승리"), "2연승은 생략");
            Assert.That(GameText(Record(Lg, Samsung, 3, 5), 8, walkOff: false), Does.Not.Contain("연장"), "고정 장면(8회 끝)은 일반 경기");
        }

        [Test]
        public void 연승_연패는_치른_경기에서_센다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.MyStreak(), Is.Zero);
            var random = new SeededRandom(3);
            foreach (var (my, their) in new[] { (1, 3), (5, 2), (4, 1), (6, 0) })
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(my, their), random);
            }
            Assert.That(s.MyStreak(), Is.EqualTo(3));
            s.StartNextGame();
            s.FinishCurrentGame(s.ScheduleScore(2, 2), random);
            Assert.That(s.MyStreak(), Is.Zero, "무승부가 끊는다");
            s.StartNextGame();
            var report = PostGameProcessor.Process(s, s.ScheduleScore(1, 4), random, null);
            Assert.That(s.MyStreak(), Is.EqualTo(-1));
            Assert.That(report.News.First().Text, Does.EndWith("4:1 패배"));
        }

        [Test]
        public void 타_구단_경기는_점수차가_가장_큰_한_경기를_리그_소식으로_낸다()
        {
            var games = new List<GameRecord>
            {
                Record("KIA 타이거즈", "한화 이글스", 11, 2),
                Record("KT 위즈", "NC 다이노스", 1, 8),
                Record("두산 베어스", "롯데 자이언츠", 3, 2),
            };
            var n = NewsWriter.LeagueHighlight(games, 2026, "5.15 (금)");
            Assert.That(n.Text, Is.EqualTo("[리그] KIA 타이거즈, 한화 이글스에 11:2 대승"));
            Assert.That((n.Kind, n.Team), Is.EqualTo((NewsKind.League, "KIA 타이거즈")), "CPU 구단 뉴스");
            Assert.That(NewsWriter.LeagueHighlight(games.Skip(2), 2026, ""), Is.Null, "점수 차 6 미만이면 없음");
            Assert.That(NewsWriter.LeagueHighlight(null, 2026, ""), Is.Null);
        }

        [Test]
        public void 선두에_오르면_순위_뉴스를_낸다()
        {
            var s = SeasonState.CreateDummy(); // 삼성 3위 (23-18), 1위 KIA 26-15
            var random = new SeededRandom(11);
            int guard = 0;
            while (s.MyStanding.Rank != 1 && !s.IsSeasonFinished && guard++ < 10)
            {
                s.StartNextGame();
                PostGameProcessor.Process(s, s.ScheduleScore(9, 0), random, null);
            }
            Assume.That(s.MyStanding.Rank, Is.EqualTo(1), "이 Seed로 10경기 안에 1위");
            var lead = s.RecentNews.Single(n => n.Kind == NewsKind.Standings);
            Assert.That(lead.Text, Is.EqualTo("[순위] 삼성 라이온즈, 단독 선두 등극!").Or.EqualTo("[순위] 삼성 라이온즈, 공동 선두 도약!"));
            Assert.That(NewsWriter.TookLead(Samsung, true, 2026, "").Text, Is.EqualTo("[순위] 삼성 라이온즈, 공동 선두 도약!"));
        }

        [Test]
        public void 팬덤_특전_해금_뉴스()
        {
            Assert.That(NewsWriter.FandomUnlocked(Samsung, FandomSlot.Uniform, 2026, "").Text, Is.EqualTo("[구단] 삼성 라이온즈 팬덤 특전 '유니폼' 해금!"));
        }

        // ---------- FA·방출 ----------

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Db, s.Signings);

        static FreeAgent Agent(SeasonState s, string id) =>
            StoveLeague.Market(Book(s), s.MyTeam, FranchiseFinance.MoodOf(s.FanApproval)).First(a => a.Id == id);

        [Test]
        public void FA_영입이면_오피셜_뉴스가_쌓인다()
        {
            var s = SeasonState.CreateDummy();
            var agent = Agent(s, "BW004");
            Assert.That(agent.Player.Name, Is.EqualTo("진세훈"));
            var o = StoveLeague.Negotiate(s, Book(s), agent, agent.Ask, new FixedRandom(0.0));
            Assert.That(o.Signed, Is.True);
            var n = s.RecentNews.Single();
            Assert.That(n.Text, Is.EqualTo("[오피셜] 삼성 라이온즈, FA 시장에서 진세훈과 ₩ 3억 7,620만에 전격 계약!"));
            Assert.That((n.Kind, n.Team, n.Year, n.Date), Is.EqualTo((NewsKind.Signing, Samsung, 2026, "5.15 (금)")), "날짜는 다음 경기일");
        }

        [Test]
        public void 확정_영입과_협상_결렬도_뉴스가_된다()
        {
            var s = SeasonState.CreateDummy();
            s.SetTrust(120, 0, 0, 0);
            var ace = Agent(s, "BW010");
            Assert.That(StoveLeague.SignWithCeiling(s, Book(s), ace).Signed, Is.True);
            Assert.That(s.RecentNews.Last().Text, Is.EqualTo("[오피셜] 삼성 라이온즈, 확정 영입 카드로 염태준 영입! (연봉 ₩ 4억 4,880만)"));

            var agent = Agent(s, "BW004");
            Assert.That(StoveLeague.Negotiate(s, Book(s), agent, agent.Ask, new FixedRandom(0.999)).Signed, Is.False);
            var failed = s.RecentNews.Last();
            Assert.That(failed.Text, Is.EqualTo("[속보] 진세훈, 삼성 라이온즈의 제안 거절… 협상 결렬"));
            Assert.That(failed.Kind, Is.EqualTo(NewsKind.NegotiationFailed));

            int count = s.RecentNews.Count;
            s.SetFinance(0, 50);
            StoveLeague.Negotiate(s, Book(s), agent, agent.Ask, new FixedRandom(0.0));
            Assert.That(s.RecentNews.Count, Is.EqualTo(count), "막힌 협상(예산 부족)은 뉴스 없음");
        }

        [Test]
        public void 선수_방출이면_단독_뉴스가_쌓이고_막히면_없다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(StoveLeague.Release(s, Book(s), Db.GetRoster(Samsung).Starters[0].Id).Released, Is.False,
                "로테이션 투수는 방출 불가 (Phase 30부터 선발 타자는 방출 가능)");
            Assert.That(s.RecentNews, Is.Empty);

            var o = StoveLeague.Release(s, Book(s), "WV010");
            Assert.That(o.Released, Is.True);
            var n = s.RecentNews.Single();
            Assert.That(n.Text, Is.EqualTo($"[단독] 삼성 라이온즈, 성적 부진으로 {o.Player.Name} 방출 결정."));
            Assert.That(o.Player.Name, Is.EqualTo("소윤호"));
            Assert.That(n.Kind, Is.EqualTo(NewsKind.Release));
        }

        [Test]
        public void 새로_발동한_케미스트리만_라인업_뉴스가_된다()
        {
            var after = new ActiveChemistry(Samsung, new Dictionary<ChemistryKind, IEnumerable<string>>
            {
                [ChemistryKind.SpeedBall] = new[] { "a", "b", "c" },
                [ChemistryKind.PowerCleanup] = new[] { "d", "e", "f" },
            });
            var news = NewsWriter.NewChemistry(Samsung, new[] { ChemistryKind.SpeedBall }, after, 2026, "");
            Assert.That(news.Select(n => n.Text), Is.EqualTo(new[]
            {
                $"[라인업] 삼성 라이온즈, '{ChemistryRules.Label(ChemistryKind.PowerCleanup)}' 케미스트리 발동!",
            }));
            Assert.That(NewsWriter.NewChemistry(Samsung, null, null, 2026, ""), Is.Empty);
        }

        // ---------- 결산·저장 ----------

        [Test]
        public void 시즌_결산은_스토브리그_개막_뉴스를_내고_지난_뉴스는_남긴다()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(5);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                PostGameProcessor.Process(s, s.ScheduleScore(3, 1), random, null);
            }
            int before = s.RecentNews.Count;
            var summary = s.TransitionToNextSeason();
            var n = s.RecentNews.Last();
            Assert.That(n.Text, Is.EqualTo($"[리그] 2026 시즌 종료! 2027 스토브리그 개막 · 삼성 라이온즈 최종 {summary.Rank}위"));
            Assert.That((n.Kind, n.Year, n.Date), Is.EqualTo((NewsKind.Offseason, 2027, NewsWriter.StoveLeagueDate)));
            Assert.That(s.RecentNews.Count, Is.EqualTo(Math.Min(SeasonState.MaxNews, before + 1)));
        }

        [Test]
        public void 뉴스는_저장되고_이전_저장_파일은_빈_피드로_읽힌다()
        {
            var s = SeasonState.CreateDummy();
            s.AddNews(new NewsItem(NewsKind.Release, NewsWriter.ExclusiveTag, Samsung, "삼성 라이온즈, 성적 부진으로 홍길동 방출 결정.", 2026, "5.15 (금)"));
            s.AddNews(new NewsItem(NewsKind.League, NewsWriter.LeagueTag, Lg, "LG 트윈스, KT 위즈에 9:1 대승", 2026, "5.15 (금)"));
            var loaded = SaveManager.FromJson(SaveManager.ToJson(s));
            Assert.That(loaded.RecentNews.Select(n => (n.Kind, n.Tag, n.Team, n.Headline, n.Year, n.Date)),
                Is.EqualTo(s.RecentNews.Select(n => (n.Kind, n.Tag, n.Team, n.Headline, n.Year, n.Date))));

            var data = s.ToSaveData();
            data.News = null;
            Assert.That(SeasonState.FromSaveData(data).RecentNews, Is.Empty, "Phase 28 이전 파일");

            data.News = Enumerable.Range(1, 20).Select(i => new NewsData { Kind = 99, Headline = "옛 뉴스 " + i, Year = 2026 })
                .Append(new NewsData { Kind = 1, Headline = "" }).Append(null).ToList();
            var trimmed = SeasonState.FromSaveData(data).RecentNews;
            Assert.That(trimmed.Count, Is.EqualTo(SeasonState.MaxNews));
            Assert.That(trimmed.First().Headline, Is.EqualTo("옛 뉴스 6"));
            Assert.That(trimmed.All(n => n.Kind == NewsKind.General), Is.True, "모르는 종류는 General");
        }

        [Test]
        public void 조사는_받침으로_고른다()
        {
            Assert.That(NewsWriter.WaGwa("구자욱"), Is.EqualTo("구자욱과"));
            Assert.That(NewsWriter.WaGwa("코너"), Is.EqualTo("코너와"));
            Assert.That(NewsWriter.WaGwa("LG 트윈스"), Is.EqualTo("LG 트윈스와"));
            Assert.That(NewsWriter.HasBatchim("3"), Is.True);
            Assert.That(NewsWriter.HasBatchim("2"), Is.False);
            Assert.That(NewsWriter.HasBatchim(""), Is.False);
        }
    }
}
