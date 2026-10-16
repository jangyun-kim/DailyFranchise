using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 34: 1:1 트레이드 — CPU 가치 평가(WAR·폼·연봉·나이 가중치, 5% 프리미엄), 타 구단 명단, 교환(장부 스왑·김헌곤 룰·
    /// 로테이션 승계·퓨처스), 페이롤 차액, 샐러리캡 차단, 거절 무변화, [오피셜] 뉴스, 저장. 2개 구단 가상 선수단(삼성 = 내 구단, LG).
    /// </summary>
    public class TradeTests
    {
        static readonly MasterDatabase Db = TestLeague.TwoTeams;
        const string Samsung = TestLeague.Samsung;
        const string Lg = TestLeague.Lg;

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Db, s.Signings, s.Progression, s.Rookies);

        static TradeOutcome Trade(SeasonState s, string mine, string theirs) => TradeDesk.Propose(s, Book(s), mine, theirs);

        static long Payroll(SeasonState s) => StoveLeague.Payroll(Book(s).Get(Samsung), s.Signings);

        static PlayerData P(double war, int age, double form = 0) => new PlayerData { Id = "X", War = war, Age = age, Form = form };

        static long Salary(double war) => FrontOfficeTrust.SalaryOf(war);

        // ---------- 가치 평가 ----------

        [Test]
        public void 가치는_생산력에_적정_연봉_대비_실제_연봉을_곱하고_나이로_가중한다()
        {
            Assert.That(TradeEvaluator.Value(P(2.08, 26), Salary(2.08)), Is.EqualTo(2.08).Within(1e-9), "WAR 연봉이면 가치 = WAR");
            Assert.That(TradeEvaluator.Value(P(1.70, 22), Salary(1.70)), Is.EqualTo(2.55).Within(1e-9), "24세 이하 × 1.5");
            Assert.That(TradeEvaluator.Value(P(1.70, 24), Salary(1.70)), Is.EqualTo(2.55).Within(1e-9), "24세까지");
            Assert.That(TradeEvaluator.Value(P(1.66, 34), Salary(1.66)), Is.EqualTo(1.328).Within(1e-9), "32세 이상 × 0.8");
            Assert.That(TradeEvaluator.Value(P(1.66, 32), Salary(1.66)), Is.EqualTo(1.328).Within(1e-9));
            Assert.That(TradeEvaluator.Value(P(1.66, 31), Salary(1.66)), Is.EqualTo(1.66).Within(1e-9));

            // 비싼 계약: WAR 1.0 (적정 ₩1억 8,000만)을 ₩3억 6,000만에 → 절반
            Assert.That(TradeEvaluator.Value(P(1.0, 28), 360_000_000), Is.EqualTo(0.5).Within(1e-9));
            // 폼 +5% = WAR +0.5 → 생산력 1.5, 적정 ₩2억 5,500만 ÷ 실제(WAR 연봉) ₩1억 8,000만
            Assert.That(TradeEvaluator.Production(P(1.0, 28, 0.05)), Is.EqualTo(1.5).Within(1e-9));
            Assert.That(TradeEvaluator.Value(P(1.0, 28, 0.05), Salary(1.0)), Is.EqualTo(1.5 * 255_000_000 / 180_000_000.0).Within(1e-9));
            Assert.That(TradeEvaluator.Value(P(1.0, 28, -0.05), Salary(1.0)), Is.EqualTo(0.5 * 105_000_000 / 180_000_000.0).Within(1e-9));

            Assert.That(TradeEvaluator.Value(P(-0.5, 22), Salary(-0.5)), Is.Zero, "생산력 0 이하는 0 (어려도)");
            Assert.That(TradeEvaluator.Value(P(1.0, 28), 0), Is.Zero);
            Assert.That(TradeEvaluator.AgeMultiplier(0), Is.EqualTo(1.0), "나이 모름");
            Assert.That(TradeEvaluator.AgeMultiplier(25), Is.EqualTo(1.0));
        }

        [Test]
        public void CPU는_내_가치가_상대_가치의_105퍼센트_이상일_때만_수락한다()
        {
            Assert.That(TradeEvaluator.RequiredValue(2.0), Is.EqualTo(2.1).Within(1e-9));
            Assert.That(TradeEvaluator.Accepts(2.1, 2.0), Is.True, "경계 포함");
            Assert.That(TradeEvaluator.Accepts(2.09, 2.0), Is.False);
            Assert.That(TradeEvaluator.Accepts(2.0, 2.0), Is.False, "같은 가치는 거절 (프리미엄)");
            Assert.That(TradeEvaluator.Accepts(0, 0), Is.True);
        }

        // ---------- 타 구단 명단 ----------

        [Test]
        public void 타_구단_명단은_내_구단과_자유계약을_빼고_구단별로_보인다()
        {
            var s = SeasonState.CreateDummy();
            s.AddSigning(new SigningRecord("WV010", Samsung, LeagueRosterBook.FreeAgentTeam, 0), isBatter: true); // 방출 → 자유계약
            var targets = TradeDesk.Targets(Book(s), Samsung, s.Signings);
            Assert.That(targets.Count, Is.EqualTo(15));
            Assert.That(targets.All(t => t.Team == Lg && !t.Futures), Is.True);
            Assert.That(targets.Any(t => t.Id == "WV010"), Is.False, "자유계약 선수는 FA 명단에만");
            Assert.That(targets[0].Id, Is.EqualTo("BW009"), "22세 WAR 1.70 → 가치 2.55가 1위");
            Assert.That(targets.Select(t => t.Value), Is.Ordered.Descending);
            Assert.That(TradeDesk.Targets(null, Samsung, null), Is.Empty);

            var offers = TradeDesk.Offers(Book(s), Samsung, s.Signings);
            Assert.That(offers.Count, Is.EqualTo(19), "1군 20명 − 방출 1");
            Assert.That(offers.All(o => o.Team == Samsung), Is.True);
        }

        // ---------- 성사 ----------

        [Test]
        public void 성사되면_두_선수가_맞바뀌고_같은_위치면_받은_선수가_그_타순에_선다()
        {
            var s = SeasonState.CreateDummy();
            long before = Payroll(s);
            // 표준혁(36세 1B WAR 2.98, 연봉 ₩4억 7,700만): 2.98 × 0.8 = 2.384 ≥ 진세훈(26세 1B WAR 2.08) 2.08 × 1.05 = 2.184
            var outcome = Trade(s, "WV004", "BW004");
            Assert.That(outcome.Accepted, Is.True);
            var q = outcome.Quote;
            Assert.That((q.MySalary, q.TheirSalary), Is.EqualTo((477_000_000L, 342_000_000L)));
            Assert.That(q.MyValue, Is.EqualTo(2.384).Within(1e-9));
            Assert.That(q.RequiredValue, Is.EqualTo(2.184).Within(1e-9));
            Assert.That(q.Arrival, Is.EqualTo(TradeArrival.Lineup));
            Assert.That(q.Partner, Is.EqualTo(Lg));

            Assert.That(s.Signings.Count, Is.EqualTo(2));
            Assert.That(s.Signings.All(r => r.IsTrade), Is.True);
            var book = Book(s);
            var mine = book.Get(Samsung);
            var lg = book.Get(Lg);
            Assert.That(mine.AtOrder(4).Id, Is.EqualTo("BW004"), "1루수 자리를 받은 1루수가 바로 메운다 (김헌곤 룰, 받은 선수 우선)");
            Assert.That(mine.AtOrder(4).Position, Is.EqualTo("1B"));
            Assert.That(mine.AtOrder(4).Team, Is.EqualTo(Samsung));
            Assert.That(lg.AtOrder(4).Id, Is.EqualTo("WV004"), "상대 구단도 같은 규칙");
            Assert.That(LeagueRosterBook.AllOf(mine).Any(p => p.Id == "WV004"), Is.False);
            Assert.That((RosterLimits.CountOf(mine), RosterLimits.CountOf(lg)), Is.EqualTo((20, 15)), "1:1이라 정원 그대로");
            Assert.That(mine.Bench.Count, Is.EqualTo(3));

            // 페이롤: 보낸 선수 연봉이 빠지고 받은 선수 연봉(따라온 계약)이 더해진다
            Assert.That(Payroll(s), Is.EqualTo(before - 477_000_000 + 342_000_000));
            Assert.That(q.PayrollDelta, Is.EqualTo(-135_000_000L));
            Assert.That(StoveLeague.SalaryOf(mine.AtOrder(4), Samsung, s.Signings), Is.EqualTo(342_000_000L));

            var news = s.RecentNews.Last();
            Assert.That(news.Kind, Is.EqualTo(NewsKind.Trade));
            Assert.That(news.Text, Is.EqualTo("[오피셜] 삼성 라이온즈, LG 트윈스와 1:1 대형 트레이드 성사! (표준혁\u00A0내주고\u00A0진세훈\u00A0영입)"),
                "Phase 35: 괄호 안은 줄바꿈 없는 공백으로 한 덩어리");
            Assert.That(NewsWriter.DisplayText(news.Headline), Does.EndWith("성사!\n(표준혁\u00A0내주고\u00A0진세훈\u00A0영입)"),
                "화면에서는 덩어리 앞에서 줄을 바꿔 통째로 다음 줄에");
            Assert.That(NewsWriter.DisplayText("보통 뉴스 한 줄"), Is.EqualTo("보통 뉴스 한 줄"), "묶은 덩어리가 없으면 그대로");

            Assert.That(TradeDesk.Targets(book, Samsung, s.Signings).Any(t => t.Id == "WV004"), Is.True, "보낸 선수는 이제 LG 명단");
            Assert.That(TradeDesk.Quote(s, book, "WV004", "BW005").Block, Is.EqualTo(TradeBlock.NotMine), "이미 보낸 선수");
            Assert.That(TradeDesk.Quote(s, book, "BW004", "BW004").Block, Is.EqualTo(TradeBlock.NotAvailable), "이미 내 선수");
        }

        [Test]
        public void 같은_위치가_없으면_같은_수비군인_받은_선수가_먼저_메운다()
        {
            var s = SeasonState.CreateDummy();
            // 길태윤(25세 3B 1.06) ↔ 반석호(29세 2B 0.72 → 요구 0.756)
            var outcome = Trade(s, "WV005", "BW002");
            Assert.That(outcome.Accepted, Is.True);
            Assert.That(outcome.Quote.Arrival, Is.EqualTo(TradeArrival.Lineup));
            var book = Book(s);
            Assert.That(book.Get(Samsung).AtOrder(5).Id, Is.EqualTo("BW002"));
            Assert.That(book.Get(Samsung).AtOrder(5).Position, Is.EqualTo("3B"), "타순·수비 위치는 빠진 자리 그대로");
            Assert.That(book.Get(Lg).AtOrder(2).Id, Is.EqualTo("WV005"));
            Assert.That(book.Get(Lg).AtOrder(2).Position, Is.EqualTo("2B"));
        }

        [Test]
        public void 포수를_내주고_외야수를_받으면_포수_자리는_대체_선수가_받은_선수는_벤치로()
        {
            var s = SeasonState.CreateDummy();
            // 마상훈(30세 C 0.91) ↔ 육민재(29세 LF 0.86 → 요구 0.903)
            var outcome = Trade(s, "WV007", "BW005");
            Assert.That(outcome.Accepted, Is.True);
            Assert.That(outcome.Quote.Arrival, Is.EqualTo(TradeArrival.Bench));
            var mine = Book(s).Get(Samsung);
            Assert.That(LeagueRosterBook.IsReplacement(mine.AtOrder(7)), Is.True, "포수 자리에 외야수를 세우지 않는다");
            Assert.That(mine.AtOrder(7).Position, Is.EqualTo("C"));
            Assert.That(mine.Bench.Last().Id, Is.EqualTo("BW005"));
            Assert.That(RosterLimits.GameReadinessProblem(mine), Is.Null, "경기는 치를 수 있다");
        }

        [Test]
        public void 로테이션_투수를_내주고_투수를_받으면_받은_투수가_그_순번을_잇는다()
        {
            var s = SeasonState.CreateDummy();
            // 어준석(23세 선발 0.73 → 1.095) ↔ 남기범(22세 셋업 0.64 → 0.96, 요구 1.008)
            var outcome = Trade(s, "WV020", "BW011");
            Assert.That(outcome.Accepted, Is.True);
            Assert.That(outcome.Quote.Arrival, Is.EqualTo(TradeArrival.Rotation));
            var book = Book(s);
            var mine = book.Get(Samsung);
            Assert.That(mine.Starters.Count, Is.EqualTo(5));
            Assert.That(mine.Starters[4].Id, Is.EqualTo("BW011"));
            Assert.That((mine.Starters[4].Role, mine.Starters[4].Order), Is.EqualTo((RosterRole.Starter, 5)));
            var lg = book.Get(Lg);
            Assert.That(lg.Bullpen.Select(p => p.Id), Is.EqualTo(new[] { "WV020" }), "상대는 불펜 투수를 내줘 받은 투수가 불펜으로");
        }

        [Test]
        public void 퓨처스_유망주도_보낼_수_있고_1군_빈자리가_없으면_받은_선수는_퓨처스로()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.AddRookie(new PlayerData
            {
                Id = "p_rookie_2026_test", Name = "한유망", Team = Samsung, Role = RosterRole.Bench, Order = 1,
                Position = "SS", Age = 19, War = 0.40, Year = 2026,
            }), Is.True);
            Assert.That(TradeDesk.Offers(Book(s), Samsung, s.Signings).Single(o => o.Id == "p_rookie_2026_test").Futures, Is.True);

            // 퓨처스 신인(19세 0.40 → 0.6) ↔ 봉현우(27세 C 0.30 → 요구 0.315): 1군 20명·벤치 3명이라 벤치로
            long before = Payroll(s);
            var outcome = Trade(s, "p_rookie_2026_test", "BW008");
            Assert.That(outcome.Accepted, Is.True);
            Assert.That(outcome.Quote.Arrival, Is.EqualTo(TradeArrival.Bench));
            var book = Book(s);
            Assert.That(book.Get(Samsung).Reserves, Is.Empty);
            Assert.That(book.Get(Samsung).Bench.Last().Id, Is.EqualTo("BW008"));
            Assert.That(Payroll(s), Is.EqualTo(before + Salary(0.30)), "퓨처스 선수 연봉은 원래 페이롤 밖");
            var rookieAtLg = book.Get(Lg).Bench.Single(p => p.Id == "p_rookie_2026_test");
            Assert.That(rookieAtLg.Team, Is.EqualTo(Lg));
            Assert.That(LeagueRosterBook.IsReplacement(book.Get(Lg).AtOrder(8)), Is.True, "LG 포수 자리에 유격수를 세우지 않는다");

            // 벤치를 5명으로 채운 뒤 투수를 내주고 타자를 받으면 → 퓨처스
            var s2 = SeasonState.CreateDummy();
            s2.AddSigning(new SigningRecord("BW001", Lg, Samsung, Salary(0.93)), isBatter: true);
            s2.AddSigning(new SigningRecord("BW002", Lg, Samsung, Salary(0.72)), isBatter: true);
            Assert.That(Book(s2).Get(Samsung).Bench.Count, Is.EqualTo(RosterLimits.MaxBench));
            long before2 = Payroll(s2);
            // 왕도현(29세 불펜 0.80) ↔ 봉현우(C 0.30)
            var outcome2 = Trade(s2, "WV015", "BW008");
            Assert.That(outcome2.Accepted, Is.True);
            Assert.That(outcome2.Quote.Arrival, Is.EqualTo(TradeArrival.Futures));
            var mine2 = Book(s2).Get(Samsung);
            Assert.That(mine2.Reserves.Select(p => p.Id), Is.EqualTo(new[] { "BW008" }));
            Assert.That(mine2.Reserves[0].Team, Is.EqualTo(Samsung));
            Assert.That(mine2.Bench.Count, Is.EqualTo(RosterLimits.MaxBench), "벤치 정원을 넘지 않는다");
            Assert.That(RosterLimits.CountOf(mine2), Is.LessThanOrEqualTo(RosterLimits.MaxRoster));
            Assert.That(Payroll(s2), Is.EqualTo(before2 - Salary(0.80)), "퓨처스로 간 선수는 페이롤 밖");
        }

        // ---------- 거절·차단 ----------

        [Test]
        public void 거절되면_아무것도_바뀌지_않는다()
        {
            var s = SeasonState.CreateDummy();
            int news = s.RecentNews.Count;
            // 피준서(23세 WAR −0.50 → 0) ↔ 진세훈
            var outcome = Trade(s, "WV011", "BW004");
            Assert.That(outcome.Accepted, Is.False);
            Assert.That(outcome.Rejected, Is.True);
            Assert.That(outcome.Block, Is.EqualTo(TradeBlock.None));
            Assert.That(s.Signings, Is.Empty);
            Assert.That(s.RecentNews.Count, Is.EqualTo(news), "거절은 뉴스도 없다 (패널티 없음)");
            Assert.That(TradeDesk.RejectedText, Is.EqualTo("[거절] 상대 구단이 제안을 매몰차게 거절했습니다."));

            // 가치가 더 높아도 5% 프리미엄을 못 넘으면 거절: 하윤재(23세 1.49 → 2.235) ↔ 황보찬(31세 2.17 → 요구 2.2785)
            var premium = Trade(s, "WV001", "BW013");
            Assert.That(premium.Quote.MyValue, Is.GreaterThan(premium.Quote.TheirValue));
            Assert.That(premium.Rejected, Is.True);
            Assert.That(s.Signings, Is.Empty);
        }

        [Test]
        public void 교환_뒤_페이롤이_샐러리캡을_넘으면_제안_자체를_막는다()
        {
            var s = SeasonState.CreateDummy();
            s.SetSalaryCapBase(4_100_000_000); // 페이롤 ₩40억 2,000만 → 여유 ₩8,000만
            // 하윤재(1번 CF ₩2억 5,350만, 가치 2.235) ↔ 탁민규(26세 투수 2.06, ₩3억 3,900만, 요구 2.163): CPU는 수락하지만
            // 중견수를 맡을 선수가 없어 대체 선수(최저 ₩3,000만)가 서므로 +₩3억 3,900만 − ₩2억 5,350만 + ₩3,000만 = +₩1억 1,550만
            var q = TradeDesk.Quote(s, Book(s), "WV001", "BW014");
            Assert.That(q.WouldAccept, Is.True);
            Assert.That(q.PayrollDelta, Is.EqualTo(115_500_000L));
            Assert.That(q.Block, Is.EqualTo(TradeBlock.SalaryCap));
            var outcome = Trade(s, "WV001", "BW014");
            Assert.That(outcome.Accepted, Is.False);
            Assert.That(outcome.Rejected, Is.False, "거절이 아니라 차단");
            Assert.That(s.Signings, Is.Empty);

            // 이미 캡을 넘었어도 페이롤이 줄어드는 교환은 막지 않는다
            s.SetSalaryCapBase(3_800_000_000); // 페이롤 ₩40억 2,000만 → 교환 뒤 ₩38억 8,500만 (여전히 초과지만 줄어든다)
            Assert.That(Trade(s, "WV004", "BW004").Accepted, Is.True);
        }

        [Test]
        public void 경기_중이거나_대상이_맞지_않으면_막는다()
        {
            var s = SeasonState.CreateDummy();
            var book = Book(s);
            Assert.That(TradeDesk.Quote(s, book, "BW001", "BW004").Block, Is.EqualTo(TradeBlock.NotMine), "LG 선수를 보낼 수 없다");
            Assert.That(TradeDesk.Quote(s, book, "WV001", "WV002").Block, Is.EqualTo(TradeBlock.NotAvailable), "내 선수를 받을 수 없다");
            s.AddSigning(new SigningRecord("WV010", Samsung, LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            Assert.That(TradeDesk.Quote(s, Book(s), "WV001", "WV010").Block, Is.EqualTo(TradeBlock.NotAvailable), "자유계약 선수는 FA 협상으로");
            Assert.That(TradeDesk.Quote(s, book, "REPL-WV007", "BW004").Block, Is.EqualTo(TradeBlock.NotMine));
            s.StartNextGame();
            Assert.That(TradeDesk.Quote(s, Book(s), "WV004", "BW004").Block, Is.EqualTo(TradeBlock.InGame));
            Assert.That(TradeDesk.BlockText(TradeBlock.SalaryCap), Is.Not.Empty);
        }

        // ---------- 저장 ----------

        [Test]
        public void 트레이드는_저장되고_불러와도_같은_선수단이다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(Trade(s, "WV004", "BW004").Accepted, Is.True);
            Assert.That(Trade(s, "WV020", "BW011").Accepted, Is.True);
            var loaded = SeasonState.FromSaveData(s.ToSaveData());
            Assert.That(loaded.Signings.Select(r => (r.PlayerId, r.FromTeam, r.ToTeam, r.Salary, r.IsTrade)),
                Is.EqualTo(s.Signings.Select(r => (r.PlayerId, r.FromTeam, r.ToTeam, r.Salary, r.IsTrade))));
            var a = Book(s).Get(Samsung);
            var b = Book(loaded).Get(Samsung);
            Assert.That(b.Lineup.Select(p => p.Id), Is.EqualTo(a.Lineup.Select(p => p.Id)));
            Assert.That(b.Starters.Select(p => p.Id), Is.EqualTo(a.Starters.Select(p => p.Id)));
            Assert.That(Payroll(loaded), Is.EqualTo(Payroll(s)));

            // 트레이드 표시가 없는 이전 파일 형식(한 건씩)이어도 깨지지 않는다
            var data = s.ToSaveData();
            foreach (var r in data.Signings) r.Trade = false;
            var old = SeasonState.FromSaveData(data);
            Assert.That(Book(old).Applied.Count, Is.EqualTo(4));
            Assert.That(LeagueRosterBook.AllOf(Book(old).Get(Samsung)).Any(p => p.Id == "BW004"), Is.True);
        }
    }
}
