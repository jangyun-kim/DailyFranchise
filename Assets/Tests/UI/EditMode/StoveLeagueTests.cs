using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 25: FA 시장(요구액·페이롤·캡), 협상 성공/결렬/확정 영입과 선수단 장부, 정원·예산·캡 차단, 저장.
    /// Phase 26: 화폐 현실화(₩), 연봉은 영입 때가 아니라 페이롤로, 리그 개막 캡 기준, 방출(위약금·자유계약), 경기 가능 검사.
    /// </summary>
    public class StoveLeagueTests
    {
        static readonly MasterDatabase Db = MasterDatabase.LoadFromResources();
        const string Busan = "부산 웨이브스";
        const string Seoul = "서울 블루윙스";
        const string Fa = LeagueRosterBook.FreeAgentTeam;
        const long Budget0 = 5_000_000_000;

        /// <summary>항상 같은 값을 주고 뽑은 횟수를 센다.</summary>
        sealed class FixedRandom : IRandomSource
        {
            readonly double _value;
            public FixedRandom(double value) => _value = value;
            public int Draws { get; private set; }
            public double NextDouble() { Draws++; return _value; }
            public int Next(int maxExclusive) { Draws++; return 0; }
        }

        static readonly IRandomSource Win = new FixedRandom(0.0);
        static readonly IRandomSource Lose = new FixedRandom(0.999);

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Db, s.Signings);

        static FreeAgent Agent(SeasonState s, string id) =>
            StoveLeague.Market(Book(s), s.MyTeam, FranchiseFinance.MoodOf(s.FanApproval)).First(a => a.Id == id);

        static SigningOutcome Sign(SeasonState s, string id) =>
            StoveLeague.Negotiate(s, Book(s), Agent(s, id), Agent(s, id).Ask, Win);

        static long Payroll(SeasonState s) => StoveLeague.Payroll(Book(s).Get(Busan), s.Signings);

        // ---------- 시장 ----------

        [Test]
        public void 시장은_내_구단이_아닌_선수를_WAR_순으로_보이고_요구액은_연봉에_인기_보정을_더한다()
        {
            var s = SeasonState.CreateDummy();
            var market = StoveLeague.Market(Book(s), s.MyTeam, FanMood.Normal);
            Assert.That(market.Count, Is.EqualTo(15));
            Assert.That(market.All(a => a.Team == Seoul), Is.True);
            Assert.That(market[0].Id, Is.EqualTo("BW010"), "WAR 2.52");
            Assert.That(market.Select(a => a.Player.War), Is.Ordered.Descending);

            var bw004 = market.First(a => a.Id == "BW004"); // WAR 2.08
            Assert.That((bw004.Salary, bw004.Ask), Is.EqualTo((342_000_000L, 376_200_000L)), "Normal +10%");
            Assert.That(StoveLeague.Market(Book(s), s.MyTeam, FanMood.Happy).First(a => a.Id == "BW004").Ask, Is.EqualTo(342_000_000L));
            Assert.That(StoveLeague.Market(Book(s), s.MyTeam, FanMood.Angry).First(a => a.Id == "BW004").Ask, Is.EqualTo(410_400_000L));
            Assert.That(StoveLeague.OfferAt(376_200_000, 100), Is.EqualTo(376_200_000L));
            Assert.That(StoveLeague.OfferAt(376_200_000, 90), Is.EqualTo(338_580_000L));
            Assert.That(StoveLeague.Market(null, Busan, FanMood.Normal), Is.Empty);
        }

        [Test]
        public void 페이롤과_캡_여유()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(Payroll(s), Is.EqualTo(4_020_000_000L));
            Assert.That(StoveLeague.CapRoom(s, Book(s)), Is.EqualTo(1_980_000_000L), "DB를 모르는 시즌은 캡 기준 ₩60억");
            Assert.That(StoveLeague.Payroll(null, null), Is.Zero);
        }

        [Test]
        public void 캡_기준은_리그_개막_평균_페이롤의_125퍼센트다()
        {
            // 부산 40.2억 · 서울 34.785억 → 평균 37.4925억 × 1.25
            Assert.That(StoveLeague.OpeningCap(Db), Is.EqualTo(4_686_562_500L));
            Assert.That(StoveLeague.OpeningCap(null), Is.EqualTo(FrontOfficeTrust.DefaultSalaryCap));

            var s = SeasonState.CreateDummy();
            s.SetSalaryCapBase(StoveLeague.OpeningCap(Db));
            Assert.That(StoveLeague.CapRoom(s, Book(s)), Is.EqualTo(666_562_500L));
            Sign(s, "BW004");
            Assert.That(s.SalaryCap, Is.EqualTo(4_686_562_500L), "영입해도 캡 기준은 그대로 (개막 시점에 묶음)");
            Assert.That(StoveLeague.CapRoom(s, Book(s)), Is.EqualTo(666_562_500L - 376_200_000L));
        }

        [Test]
        public void 시장_핵심_기록()
        {
            Assert.That(StoveLeague.KeyStat(new PlayerData { Role = RosterRole.Lineup, WrcPlus = 140.4 }), Is.EqualTo("wRC+ 140"));
            Assert.That(StoveLeague.KeyStat(new PlayerData { Role = RosterRole.Lineup, Ops = 0.85 }), Is.EqualTo("OPS .850"));
            Assert.That(StoveLeague.KeyStat(new PlayerData { Role = RosterRole.Starter, Fip = 3.214 }), Is.EqualTo("FIP 3.21"));
            Assert.That(StoveLeague.KeyStat(new PlayerData { Role = RosterRole.Bullpen, Era = 3.5 }), Is.EqualTo("ERA 3.50"));
        }

        // ---------- 협상 ----------

        [Test]
        public void 협상_성공이면_수수료만_내고_연봉은_페이롤에_잡히며_원_소속은_대체_선수로_채운다()
        {
            var s = SeasonState.CreateDummy();
            var agent = Agent(s, "BW004"); // 서울 4번 1B, 요구액 3억 7,620만 · 수수료 1,881만
            var o = StoveLeague.Negotiate(s, Book(s), agent, agent.Ask, Win);
            Assert.That((o.Block, o.Signed, o.Salary), Is.EqualTo((SigningBlock.None, true, 376_200_000L)));
            Assert.That(o.Negotiation.SuccessRate, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(s.Budget, Is.EqualTo(Budget0 - 18_810_000), "계약금은 영입 때 내지 않는다 (Phase 26)");
            Assert.That(s.Signings.Single().PlayerId, Is.EqualTo("BW004"));

            var book = Book(s);
            var busan = book.Get(Busan);
            var signed = busan.Bench.Last();
            Assert.That((signed.Id, signed.Team, signed.Role, signed.Order, signed.Position),
                Is.EqualTo(("BW004", Busan, RosterRole.Bench, 4, "1B")));
            Assert.That(RosterLimits.CountOf(busan), Is.EqualTo(21));

            var seoul = book.Get(Seoul);
            var hole = seoul.AtOrder(4);
            Assert.That((hole.Id, hole.Name, hole.Position, hole.Team), Is.EqualTo(("REPL-BW004", "대체 선수", "1B", Seoul)));
            Assert.That(hole.BattingAverage, Is.EqualTo(MatchupCalculator.LeagueAverage));
            Assert.That(LineupCard.FromRoster(seoul), Is.Not.Null, "서울 타순 9명 유지");
            Assert.That(Db.GetRoster(Seoul).AtOrder(4).Id, Is.EqualTo("BW004"), "선수 DB 원본은 그대로");

            Assert.That(Payroll(s), Is.EqualTo(4_020_000_000L + 376_200_000L), "계약 연봉으로 잡힌다");
            Assert.That(FranchiseFinance.PayrollPerGame(Payroll(s)) - FranchiseFinance.PayrollPerGame(4_020_000_000), Is.EqualTo(2_612_500L),
                "경기당 유지비가 연봉 ÷ 144만큼 는다");
            Assert.That(StoveLeague.Market(book, Busan, FanMood.Normal).Any(a => a.Id == "BW004" || a.Id.StartsWith("REPL-")), Is.False);
        }

        [Test]
        public void 결렬이면_수수료만_내고_TP가_환급된다()
        {
            var s = SeasonState.CreateDummy();
            var agent = Agent(s, "BW004");
            var o = StoveLeague.Negotiate(s, Book(s), agent, agent.Ask, Lose);
            Assert.That((o.Block, o.Signed, o.Salary), Is.EqualTo((SigningBlock.None, false, 0L)));
            Assert.That((s.Budget, s.TrustPoints), Is.EqualTo((Budget0 - 18_810_000, 18)), "floor(1,881만 / 100만)");
            Assert.That(s.Signings, Is.Empty);
            Assert.That(Book(s).Get(Busan), Is.SameAs(Db.GetRoster(Busan)), "영입이 없으면 DB 선수단 그대로");

            // 헐값(85%) 결렬은 환급 없음
            var low = StoveLeague.Negotiate(s, Book(s), agent, StoveLeague.OfferAt(agent.Ask, 85), Lose);
            Assert.That((low.Signed, low.Negotiation.TrustDelta, s.TrustPoints), Is.EqualTo((false, 0, 18)));
        }

        [Test]
        public void 확정_영입은_TP_100을_쓰고_요구액_연봉으로_투수를_불펜에_넣는다()
        {
            var s = SeasonState.CreateDummy();
            s.SetTrust(120, 0, 0, 0);
            var agent = Agent(s, "BW010"); // 서울 선발 1번, 연봉 4억 800만 → 요구액 4억 4,880만 · 수수료 2,244만
            var o = StoveLeague.SignWithCeiling(s, Book(s), agent);
            Assert.That((o.Signed, o.Negotiation.CeilingUsed, o.Salary), Is.EqualTo((true, true, 448_800_000L)));
            Assert.That((s.TrustPoints, s.Budget), Is.EqualTo((20, Budget0 - 22_440_000)));

            var book = Book(s);
            var pen = book.Get(Busan).Bullpen.Last();
            Assert.That((pen.Id, pen.Role, pen.Order, pen.Team), Is.EqualTo(("BW010", RosterRole.Bullpen, 4, Busan)));
            Assert.That(book.Get(Seoul).Starters.Select(p => p.Order), Is.EqualTo(new[] { 1, 2, 3, 4 }), "남은 선발 순번을 다시 매긴다");
            Assert.That(book.Get(Seoul).Starters.Any(p => p.Id == "BW010"), Is.False);
        }

        [Test]
        public void 확정_영입은_TP가_100_미만이면_막고_아무것도_바꾸지_않는다()
        {
            var s = SeasonState.CreateDummy();
            s.SetTrust(99, 0, 0, 0);
            var o = StoveLeague.SignWithCeiling(s, Book(s), Agent(s, "BW010"));
            Assert.That((o.Block, o.Signed), Is.EqualTo((SigningBlock.Trust, false)));
            Assert.That((s.TrustPoints, s.Budget, s.Signings.Count), Is.EqualTo((99, Budget0, 0)));
        }

        // ---------- 차단 ----------

        [Test]
        public void 예산이_수수료보다_적거나_캡_여유가_모자라면_막는다()
        {
            var s = SeasonState.CreateDummy();
            var agent = Agent(s, "BW004");
            s.SetFinance(18_810_000 - 1, 50);
            Assert.That(StoveLeague.Check(s, Book(s), agent, agent.Ask), Is.EqualTo(SigningBlock.Budget));
            s.SetFinance(18_810_000, 50);
            Assert.That(StoveLeague.Check(s, Book(s), agent, agent.Ask), Is.EqualTo(SigningBlock.None), "연봉은 페이롤로 나가므로 수수료만 있으면 된다");
            Assert.That(StoveLeague.Check(s, Book(s), agent, 0), Is.EqualTo(SigningBlock.InvalidOffer));

            s.SetFinance(Budget0, 50);
            s.SetTrust(0, 0, 2_000_000_000, 0); // 시즌 캡 40억 < 페이롤 40.2억
            Assert.That(StoveLeague.CapRoom(s, Book(s)), Is.EqualTo(-20_000_000L));
            var blocked = StoveLeague.Negotiate(s, Book(s), agent, agent.Ask, Win);
            Assert.That(blocked.Block, Is.EqualTo(SigningBlock.SalaryCap));
            Assert.That((s.Budget, s.Signings.Count), Is.EqualTo((Budget0, 0)), "막히면 수수료도 내지 않는다");
        }

        [Test]
        public void 벤치_5명_불펜_7명_1군_26명이_차면_막는다()
        {
            var s = SeasonState.CreateDummy();
            foreach (var id in new[] { "BW008", "BW007" }) Assert.That(Sign(s, id).Signed, Is.True, id);
            Assert.That(Book(s).Get(Busan).Bench.Count, Is.EqualTo(5));
            Assert.That(StoveLeague.Check(s, Book(s), Agent(s, "BW006"), 1), Is.EqualTo(SigningBlock.BenchFull));
            Assert.That(StoveLeague.BlockText(SigningBlock.BenchFull), Does.Contain("벤치 정원 5명").And.Contain("방출"));

            foreach (var id in new[] { "BW011", "BW015", "BW012", "BW014" }) Assert.That(Sign(s, id).Signed, Is.True, id);
            var busan = Book(s).Get(Busan);
            Assert.That((busan.Bullpen.Count, RosterLimits.CountOf(busan)), Is.EqualTo((7, 26)));
            Assert.That(StoveLeague.Check(s, Book(s), Agent(s, "BW013"), 1), Is.EqualTo(SigningBlock.RosterFull));
            Assert.That(StoveLeague.Check(s, Book(s), Agent(s, "BW006"), 1), Is.EqualTo(SigningBlock.RosterFull));

            // 서울: 7·8번 대체 선수, 남은 선발 BW010·BW013, 불펜 없음
            var seoul = Book(s).Get(Seoul);
            Assert.That(new[] { seoul.AtOrder(7).Id, seoul.AtOrder(8).Id }, Is.EqualTo(new[] { "REPL-BW007", "REPL-BW008" }));
            Assert.That(seoul.Starters.Select(p => p.Id), Is.EqualTo(new[] { "BW010", "BW013" }));
            Assert.That(seoul.Bullpen, Is.Empty);

            // 방출하면 자리가 난다
            Assert.That(StoveLeague.Release(s, Book(s), "BW008").Released, Is.True);
            Assert.That(StoveLeague.Check(s, Book(s), Agent(s, "BW006"), 1), Is.EqualTo(SigningBlock.None));
        }

        [Test]
        public void 이미_내_선수이거나_경기_중이면_막는다()
        {
            var s = SeasonState.CreateDummy();
            var agent = Agent(s, "BW004");
            Assert.That(Sign(s, "BW004").Signed, Is.True);
            Assert.That(StoveLeague.Check(s, Book(s), agent, agent.Ask), Is.EqualTo(SigningBlock.NotAvailable));

            var t = SeasonState.CreateDummy();
            t.StartNextGame();
            Assert.That(StoveLeague.Check(t, Book(t), Agent(t, "BW004"), 1), Is.EqualTo(SigningBlock.InGame));
        }

        // ---------- 방출 (Phase 26) ----------

        [Test]
        public void 벤치_선수를_방출하면_연봉_50퍼센트_위약금을_내고_자유계약으로_시장에_돌아간다()
        {
            var s = SeasonState.CreateDummy();
            var wv010 = Db.FindPlayer("WV010");
            long salary = FrontOfficeTrust.SalaryOf(wv010.War);
            long payroll = Payroll(s);

            Assert.That(StoveLeague.ReleasePenalty(s, Book(s), "WV010"), Is.EqualTo(salary / 2));
            var o = StoveLeague.Release(s, Book(s), "WV010");
            Assert.That((o.Block, o.Released, o.Penalty, o.Player.Id), Is.EqualTo((ReleaseBlock.None, true, salary / 2, "WV010")));
            Assert.That(s.Budget, Is.EqualTo(Budget0 - salary / 2));
            Assert.That(s.Signings.Single().IsRelease, Is.True);

            var book = Book(s);
            Assert.That(book.Get(Busan).Bench.Select(p => p.Id), Is.EqualTo(new[] { "WV011", "WV012" }));
            Assert.That(book.Get(Busan).Bench.Select(p => p.Order), Is.EqualTo(new[] { 1, 2 }), "순번을 다시 매긴다");
            var freed = book.Get(Fa).Bench.Single();
            Assert.That((freed.Id, freed.Team), Is.EqualTo(("WV010", Fa)));
            Assert.That(Payroll(s), Is.EqualTo(payroll - salary), "페이롤에서 빠진다");

            var market = StoveLeague.Market(book, Busan, FanMood.Normal);
            Assert.That(market.Count, Is.EqualTo(16));
            Assert.That(market.Single(a => a.Id == "WV010").Team, Is.EqualTo(Fa));
        }

        [Test]
        public void 방출한_선수를_다시_영입할_수_있고_영입한_선수의_위약금은_계약_연봉_기준이다()
        {
            var s = SeasonState.CreateDummy();
            Sign(s, "BW004"); // 계약 연봉 3억 7,620만
            Assert.That(StoveLeague.ReleasePenalty(s, Book(s), "BW004"), Is.EqualTo(188_100_000L));
            Assert.That(StoveLeague.Release(s, Book(s), "BW004").Released, Is.True);
            Assert.That(Book(s).Get(Fa).Bench.Single().Id, Is.EqualTo("BW004"));
            Assert.That(Book(s).Get(Seoul).AtOrder(4).Id, Is.EqualTo("REPL-BW004"), "원 소속으로 돌아가지 않는다");

            var again = Agent(s, "BW004");
            Assert.That(again.Team, Is.EqualTo(Fa));
            var o = StoveLeague.Negotiate(s, Book(s), again, StoveLeague.OfferAt(again.Ask, 95), Win);
            Assert.That(o.Signed, Is.True);
            Assert.That(s.Signings.Last().FromTeam, Is.EqualTo(Fa));
            Assert.That(Book(s).Get(Busan).Bench.Last().Id, Is.EqualTo("BW004"));
            Assert.That(StoveLeague.SalaryOf(Book(s).Get(Busan).Bench.Last(), Busan, s.Signings), Is.EqualTo(357_390_000L), "새 계약 연봉 (95%)");
        }

        [Test]
        public void 불펜_투수도_방출할_수_있다()
        {
            var s = SeasonState.CreateDummy();
            var pen = Db.GetRoster(Busan).Bullpen[0];
            Assert.That(StoveLeague.Release(s, Book(s), pen.Id).Released, Is.True);
            Assert.That(Book(s).Get(Busan).Bullpen.Count, Is.EqualTo(2));
            Assert.That(Book(s).Get(Fa).Bullpen.Single().Id, Is.EqualTo(pen.Id));
            Assert.That(RosterLimits.GameReadinessProblem(Book(s).Get(Busan)), Is.Null, "불펜이 줄어도 경기는 된다");
        }

        [Test]
        public void 선발_타순과_로테이션_선수는_방출할_수_없고_예산이_위약금보다_적어도_막는다()
        {
            var s = SeasonState.CreateDummy();
            var busan = Db.GetRoster(Busan);
            Assert.That(StoveLeague.CheckRelease(s, Book(s), busan.AtOrder(1).Id), Is.EqualTo(ReleaseBlock.Starter));
            Assert.That(StoveLeague.CheckRelease(s, Book(s), busan.Starters[0].Id), Is.EqualTo(ReleaseBlock.Starter));
            Assert.That(StoveLeague.CheckRelease(s, Book(s), "BW004"), Is.EqualTo(ReleaseBlock.NotMine));
            Assert.That(StoveLeague.CheckRelease(s, Book(s), null), Is.EqualTo(ReleaseBlock.NotMine));
            Assert.That(StoveLeague.ReleasePenalty(s, Book(s), "BW004"), Is.Zero);

            long penalty = StoveLeague.ReleasePenalty(s, Book(s), "WV010");
            s.SetFinance(penalty - 1, 50);
            var blocked = StoveLeague.Release(s, Book(s), "WV010");
            Assert.That((blocked.Block, blocked.Released), Is.EqualTo((ReleaseBlock.Budget, false)));
            Assert.That((s.Budget, s.Signings.Count), Is.EqualTo((penalty - 1, 0)), "막히면 아무것도 바뀌지 않는다");

            var t = SeasonState.CreateDummy();
            t.StartNextGame();
            Assert.That(StoveLeague.CheckRelease(t, Book(t), "WV010"), Is.EqualTo(ReleaseBlock.InGame));
            foreach (ReleaseBlock b in System.Enum.GetValues(typeof(ReleaseBlock)))
                Assert.That(StoveLeague.ReleaseBlockText(b), b == ReleaseBlock.None ? Is.Empty : Is.Not.Empty, b.ToString());
        }

        [Test]
        public void 사용자_라인업이_있으면_방출한_타자를_카드_벤치에서도_뺀다()
        {
            var s = SeasonState.CreateDummy();
            var card = LineupCard.FromRoster(Db.GetRoster(Busan));
            card.TrySwap(LineupSpot.Batting(3), LineupSpot.Batting(5));
            s.MyLineup = card;
            StoveLeague.Release(s, Book(s), "WV011");
            Assert.That(s.MyLineup.Bench, Is.EqualTo(new[] { "WV010", "WV012" }));
            Assert.That(s.MyLineup.Matches(Book(s).Get(Busan)), Is.True, "카드가 새 선수단과 맞는다");
            Assert.That(s.MyLineup.Batting[2].PlayerId, Is.EqualTo("WV005"), "바꾼 타순 유지");
        }

        // ---------- 경기 가능 검사 (Phase 26) ----------

        [Test]
        public void 타순_9명이나_선발_투수가_없으면_경기를_시작할_수_없다()
        {
            var busan = Db.GetRoster(Busan);
            Assert.That(RosterLimits.GameReadinessProblem(busan), Is.Null);
            Assert.That(RosterLimits.GameReadinessProblem(null), Is.Null);

            var eight = new TeamRoster(Busan, busan.Lineup.Take(8).ToList(), busan.Bench, busan.Starters, busan.Bullpen);
            Assert.That(RosterLimits.GameReadinessProblem(eight), Does.StartWith("선발 타순이 8명입니다"));
            var dup = new TeamRoster(Busan, busan.Lineup.Take(8).Append(busan.Lineup[0]).ToList(), busan.Bench, busan.Starters, busan.Bullpen);
            Assert.That(RosterLimits.GameReadinessProblem(dup), Is.Not.Null, "같은 타순 중복");
            var noStarter = new TeamRoster(Busan, busan.Lineup, busan.Bench, null, busan.Bullpen);
            Assert.That(RosterLimits.GameReadinessProblem(noStarter), Does.StartWith("선발 투수가 없습니다"));
        }

        // ---------- 장부 ----------

        [Test]
        public void 선발이_모두_빠지면_불펜이_선발로_가고_불펜도_없으면_대체_투수가_선다()
        {
            var records = new[] { "BW010", "BW012", "BW013", "BW014", "BW015" }
                .Select(id => new SigningRecord(id, Seoul, Busan, 1)).ToList();
            var seoul = LeagueRosterBook.Build(Db, records).Get(Seoul);
            Assert.That(seoul.Starters.Select(p => (p.Id, p.Role, p.Order)), Is.EqualTo(new[] { ("BW011", RosterRole.Starter, 1) }));
            Assert.That(seoul.Bullpen, Is.Empty);

            records.Add(new SigningRecord("BW011", Seoul, Busan, 1));
            var book = LeagueRosterBook.Build(Db, records);
            var sp = book.Get(Seoul).StartingPitcher;
            Assert.That((sp.Id, sp.Name, sp.Role, sp.Era), Is.EqualTo(("REPL-BW011", "대체 투수", RosterRole.Starter, MatchupCalculator.LeagueEra)));
            Assert.That(book.Get(Busan).Bullpen.Count, Is.EqualTo(9), "장부는 정원을 보지 않는다 (정원은 협상 단계에서)");
        }

        [Test]
        public void 장부와_맞지_않는_기록은_건너뛴다()
        {
            var records = new[]
            {
                new SigningRecord("nobody", Seoul, Busan, 1),
                new SigningRecord("BW004", "인천 하버스", Busan, 1), // 원 소속이 틀림
                new SigningRecord("BW004", Seoul, Seoul, 1),         // 같은 구단
                new SigningRecord("BW005", Seoul, Busan, 1),
                new SigningRecord("BW005", Seoul, Busan, 1),         // 이미 옮김
            };
            var book = LeagueRosterBook.Build(Db, records);
            Assert.That(book.Applied.Select(r => r.PlayerId), Is.EqualTo(new[] { "BW005" }));
            Assert.That(book.Find("BW005").roster.Team, Is.EqualTo(Busan));
            Assert.That(book.Find("nobody"), Is.EqualTo(((TeamRoster)null, (PlayerData)null)));
            Assert.That(LeagueRosterBook.Build(null, records).Rosters.Select(r => r.Team), Is.EqualTo(new[] { Fa }), "자유계약 명단만");
        }

        // ---------- 라인업·저장 ----------

        [Test]
        public void 사용자_라인업이_있으면_영입한_타자를_카드_벤치에_넣어_라인업을_지킨다()
        {
            var s = SeasonState.CreateDummy();
            var card = LineupCard.FromRoster(Db.GetRoster(Busan));
            card.TrySwap(LineupSpot.Batting(3), LineupSpot.Batting(5));
            s.MyLineup = card;
            Sign(s, "BW004");
            Assert.That(s.MyLineup.Bench.Last(), Is.EqualTo("BW004"));
            Assert.That(s.MyLineup.Matches(Book(s).Get(Busan)), Is.True, "카드가 새 선수단과 맞는다");
            Assert.That(s.MyLineup.Batting[2].PlayerId, Is.EqualTo("WV005"), "바꾼 타순 유지");

            Sign(s, "BW011"); // 투수는 카드와 무관
            Assert.That(s.MyLineup.Bench.Count, Is.EqualTo(4));
            Assert.That(s.MyLineup.Matches(Book(s).Get(Busan)), Is.True);
        }

        [Test]
        public void 이적_기록은_저장했다_불러와도_같고_없는_이전_파일은_빈_목록이다()
        {
            var s = SeasonState.CreateDummy();
            Sign(s, "BW004");
            Sign(s, "BW011");
            StoveLeague.Release(s, Book(s), "WV010");
            string json = SaveManager.ToJson(s);
            var loaded = SaveManager.FromJson(json);
            Assert.That(loaded.Signings.Select(r => (r.PlayerId, r.FromTeam, r.ToTeam, r.Salary)),
                Is.EqualTo(s.Signings.Select(r => (r.PlayerId, r.FromTeam, r.ToTeam, r.Salary))));
            Assert.That(Book(loaded).Get(Busan).Bench.Last().Id, Is.EqualTo("BW004"));
            Assert.That(Book(loaded).Get(Fa).Bench.Single().Id, Is.EqualTo("WV010"));
            Assert.That(loaded.Budget, Is.EqualTo(s.Budget));

            string legacy = System.Text.RegularExpressions.Regex.Replace(json, @",\s*""Signings"":\s*\[[^\]]*\]", "");
            Assert.That(legacy, Does.Not.Contain("Signings"));
            Assert.That(SaveManager.FromJson(legacy).Signings, Is.Empty);

            var data = s.ToSaveData();
            data.Signings.Add(new SigningData { PlayerId = "", FromTeam = Seoul, ToTeam = Busan });
            data.Signings.Add(null);
            Assert.That(SeasonState.FromSaveData(data).Signings.Count, Is.EqualTo(3), "빈 칸 항목은 건너뛴다");
        }

        [Test]
        public void 막는_이유_문구()
        {
            foreach (SigningBlock b in System.Enum.GetValues(typeof(SigningBlock)))
                Assert.That(StoveLeague.BlockText(b), b == SigningBlock.None ? Is.Empty : Is.Not.Empty, b.ToString());
        }
    }
}
