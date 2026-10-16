using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 35: 1군 등록·말소 (Call-up / Send-down) — 벤치 타자 ↔ 퓨처스 타자, 불펜 투수 ↔ 퓨처스 투수 1:1 맞바꾸기.
    /// 정원 유지·소속 배열 스왑, 선발 타순·로테이션·종류 차단, 라인업 카드, 연봉 기록, 저장, 트레이드와 순서가 섞인 기록.
    /// 2개 구단 가상 선수단 (삼성 = 내 구단 · 벤치 WV010~012 · 불펜 WV014~016 · 퓨처스는 신인으로 채운다).
    /// </summary>
    public class RosterMoveTests
    {
        static readonly MasterDatabase Db = TestLeague.TwoTeams;
        const string Samsung = TestLeague.Samsung;
        const string Lg = TestLeague.Lg;
        const string Shortstop = "p_rookie_2026_ss";
        const string Catcher = "p_rookie_2026_c";
        const string Arm = "p_rookie_2026_p";

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Db, s.Signings, s.Progression, s.Rookies);

        static TeamRoster Mine(SeasonState s) => Book(s).Get(Samsung);

        static long Payroll(SeasonState s) => StoveLeague.Payroll(Mine(s), s.Signings);

        static RosterMoveOutcome Move(SeasonState s, string a, string b) => RosterMoves.Apply(s, Mine(s), a, b);

        /// <summary>퓨처스 신인 셋: 유격수 · 포수 · 불펜 투수.</summary>
        static SeasonState WithFutures()
        {
            var s = SeasonState.CreateDummy();
            s.AddRookie(new PlayerData { Id = Shortstop, Name = "한유망", Team = Samsung, Role = RosterRole.Bench, Order = 1, Position = "SS", Age = 19, War = 0.40, Year = 2026 });
            s.AddRookie(new PlayerData { Id = Catcher, Name = "백포수", Team = Samsung, Role = RosterRole.Bench, Order = 2, Position = "C", Age = 20, War = 0.10, Year = 2026 });
            s.AddRookie(new PlayerData { Id = Arm, Name = "강속구", Team = Samsung, Role = RosterRole.Bullpen, Order = 3, Position = "불펜", Age = 21, War = 0.30, Year = 2026 });
            return s;
        }

        // ---------- 맞바꾸기 ----------

        [Test]
        public void 벤치_타자와_퓨처스_타자를_맞바꾸면_정원은_그대로이고_서로의_자리를_잇는다()
        {
            var s = WithFutures();
            var before = Mine(s);
            Assert.That(before.Bench.Select(p => p.Id), Is.EqualTo(new[] { "WV010", "WV011", "WV012" }));
            Assert.That(before.Reserves.Select(p => p.Id), Is.EqualTo(new[] { Shortstop, Catcher, Arm }));
            long payroll = Payroll(s);

            // 피준서(벤치 2번) 말소 ↔ 한유망(퓨처스 1번) 등록
            var o = Move(s, "WV011", Shortstop);
            Assert.That((o.Block, o.Moved, o.Down.Id, o.Up.Id), Is.EqualTo((RosterMoveBlock.None, true, "WV011", Shortstop)));
            var r = s.Signings.Single();
            Assert.That((r.IsRosterMove, r.PlayerId, r.CallUpId, r.FromTeam, r.ToTeam, r.Salary, r.IsRelease, r.IsTrade),
                Is.EqualTo((true, "WV011", Shortstop, Samsung, Samsung, 0L, false, false)));

            var after = Mine(s);
            Assert.That(after.Bench.Select(p => p.Id), Is.EqualTo(new[] { "WV010", Shortstop, "WV012" }), "올라온 선수가 그 벤치 자리");
            var up = after.Bench[1];
            Assert.That((up.Role, up.Order, up.Team, up.Position), Is.EqualTo((RosterRole.Bench, 2, Samsung, "SS")));
            Assert.That(after.Reserves.Select(p => p.Id), Is.EqualTo(new[] { "WV011", Catcher, Arm }), "내려간 선수가 그 퓨처스 자리");
            Assert.That((after.Reserves[0].Role, after.Reserves[0].Team), Is.EqualTo((RosterRole.Bench, Samsung)));
            Assert.That((RosterLimits.CountOf(after), after.Bench.Count, after.Bullpen.Count, after.Reserves.Count),
                Is.EqualTo((RosterLimits.CountOf(before), 3, 3, 3)), "1군·벤치·불펜·퓨처스 수가 그대로");
            Assert.That(after.Lineup.Select(p => p.Id), Is.EqualTo(before.Lineup.Select(p => p.Id)), "선발 타순은 건드리지 않는다");

            // 페이롤 = 1군 연봉 합계 (Phase 26 규칙) → 내려간 선수 연봉이 빠지고 올라온 선수 연봉이 들어온다. 연봉 기록은 그대로.
            Assert.That(Payroll(s), Is.EqualTo(payroll - FrontOfficeTrust.SalaryOf(-0.50) + FrontOfficeTrust.SalaryOf(0.40)));
        }

        [Test]
        public void 순서와_무관하고_퓨처스에서_다시_올리면_원래_연봉으로_돌아온다()
        {
            var s = WithFutures();
            long payroll = Payroll(s);
            Assert.That(Move(s, Catcher, "WV012").Moved, Is.True, "퓨처스 쪽을 먼저 줘도 된다");
            Assert.That(Mine(s).Bench.Select(p => p.Id), Is.EqualTo(new[] { "WV010", "WV011", Catcher }));
            Assert.That(Move(s, "WV012", Catcher).Moved, Is.True, "다시 맞바꾸면 돌아온다");
            Assert.That(Mine(s).Bench.Select(p => p.Id), Is.EqualTo(new[] { "WV010", "WV011", "WV012" }));
            Assert.That(Mine(s).Reserves.Select(p => p.Id), Is.EqualTo(new[] { Shortstop, Catcher, Arm }));
            Assert.That(Payroll(s), Is.EqualTo(payroll), "맞바꾸기 기록(연봉 0)이 계약 연봉으로 잡히지 않는다");
            Assert.That(StoveLeague.SalaryOf(Mine(s).Bench[2], Samsung, s.Signings), Is.EqualTo(FrontOfficeTrust.SalaryOf(-0.76)));
        }

        [Test]
        public void 불펜_투수와_퓨처스_투수를_맞바꾼다()
        {
            var s = WithFutures();
            var before = Mine(s);
            var o = Move(s, "WV016", Arm); // 옥승민(롱릴리프) ↔ 강속구
            Assert.That(o.Moved, Is.True);
            var after = Mine(s);
            Assert.That(after.Bullpen.Select(p => p.Id), Is.EqualTo(new[] { "WV014", "WV015", Arm }));
            Assert.That((after.Bullpen[2].Role, after.Bullpen[2].Order), Is.EqualTo((RosterRole.Bullpen, 3)));
            Assert.That(after.Reserves.Select(p => p.Id), Is.EqualTo(new[] { Shortstop, Catcher, "WV016" }));
            Assert.That(after.Reserves[2].IsPitcher, Is.True);
            Assert.That(after.Starters.Select(p => p.Id), Is.EqualTo(before.Starters.Select(p => p.Id)), "로테이션은 그대로");
            Assert.That((after.Bullpen.Count, RosterLimits.CountOf(after)), Is.EqualTo((before.Bullpen.Count, RosterLimits.CountOf(before))));
        }

        // ---------- 차단 ----------

        [Test]
        public void 선발_타순_로테이션_종류가_다르거나_같은_쪽끼리면_막고_아무것도_바꾸지_않는다()
        {
            var s = WithFutures();
            var my = Mine(s);
            Assert.That(RosterMoves.Check(s, my, "WV004", Shortstop), Is.EqualTo(RosterMoveBlock.Starter), "선발 4번 타자");
            Assert.That(RosterMoves.Check(s, my, "WV013", Arm), Is.EqualTo(RosterMoveBlock.Starter), "선발 로테이션");
            Assert.That(RosterMoves.Check(s, my, Arm, "WV013"), Is.EqualTo(RosterMoveBlock.Starter));
            Assert.That(RosterMoves.Check(s, my, "WV010", Arm), Is.EqualTo(RosterMoveBlock.Mismatch), "벤치 타자 ↔ 퓨처스 투수");
            Assert.That(RosterMoves.Check(s, my, "WV014", Shortstop), Is.EqualTo(RosterMoveBlock.Mismatch), "불펜 투수 ↔ 퓨처스 타자");
            Assert.That(RosterMoves.Check(s, my, "WV010", "WV011"), Is.EqualTo(RosterMoveBlock.SameSide), "둘 다 1군");
            Assert.That(RosterMoves.Check(s, my, Shortstop, Catcher), Is.EqualTo(RosterMoveBlock.SameSide), "둘 다 퓨처스");
            Assert.That(RosterMoves.Check(s, my, "BW004", Shortstop), Is.EqualTo(RosterMoveBlock.NotMine), "LG 선수");
            Assert.That(RosterMoves.Check(s, my, "WV010", null), Is.EqualTo(RosterMoveBlock.NotMine));
            Assert.That(RosterMoves.Check(s, my, "WV010", "WV010"), Is.EqualTo(RosterMoveBlock.NotMine));
            Assert.That(RosterMoves.Apply(s, my, "WV004", Shortstop).Moved, Is.False);
            Assert.That(s.Signings, Is.Empty, "막히면 기록이 없다");
            Assert.That(RosterMoves.BlockText(RosterMoveBlock.Starter), Does.Contain("먼저 벤치·불펜으로"));

            var t = WithFutures();
            t.StartNextGame();
            Assert.That(RosterMoves.Check(t, Mine(t), "WV010", Shortstop), Is.EqualTo(RosterMoveBlock.InGame));
        }

        [Test]
        public void 맞바꿀_상대는_같은_종류의_반대쪽_선수만()
        {
            var s = WithFutures();
            var my = Mine(s);
            Assert.That(RosterMoves.Partners(my, "WV011").Select(p => p.Id), Is.EqualTo(new[] { Shortstop, Catcher }), "벤치 타자 → 퓨처스 타자");
            Assert.That(RosterMoves.Partners(my, "WV015").Select(p => p.Id), Is.EqualTo(new[] { Arm }), "불펜 → 퓨처스 투수");
            Assert.That(RosterMoves.Partners(my, Shortstop).Select(p => p.Id), Is.EqualTo(new[] { "WV010", "WV011", "WV012" }), "퓨처스 타자 → 벤치");
            Assert.That(RosterMoves.Partners(my, Arm).Select(p => p.Id), Is.EqualTo(new[] { "WV014", "WV015", "WV016" }), "퓨처스 투수 → 불펜");
            Assert.That(RosterMoves.Partners(my, "WV004"), Is.Empty, "선발 타자");
            Assert.That(RosterMoves.Partners(my, "WV013"), Is.Empty, "로테이션");
            Assert.That((RosterMoves.IsStarter(my, "WV004"), RosterMoves.IsMovable(my, "WV004"), RosterMoves.IsFutures(my, Arm)), Is.EqualTo((true, false, true)));
            Assert.That(RosterMoves.Partners(Mine(SeasonState.CreateDummy()), "WV011"), Is.Empty, "퓨처스가 비면 없다");
        }

        // ---------- 라인업 카드 · 저장 · 기록 순서 ----------

        [Test]
        public void 사용자_라인업_카드의_벤치도_그_자리에서_바뀐다()
        {
            var s = WithFutures();
            s.MyLineup = LineupCard.FromRoster(Mine(s));
            Assert.That(Move(s, "WV010", Catcher).Moved, Is.True);
            Assert.That(s.MyLineup.Bench, Is.EqualTo(new[] { Catcher, "WV011", "WV012" }));
            Assert.That(s.MyLineup.Repair(Mine(s)), Is.Not.Null, "카드가 새 선수단과 맞는다");

            // 카드에서 벤치로 뺀 원래 선발 타자(장부 선발)도 벤치 선수로 보고 말소할 수 있다
            var card = s.MyLineup;
            Assert.That(card.TrySwap(LineupSpot.Batting(9), LineupSpot.Bench(1)), Is.True); // 탁현서(DH) ↔ 피준서
            s.MyLineup = card;
            var my = s.MyLineup.ApplyTo(Mine(s));
            Assert.That(my.Bench.Any(p => p.Id == "WV009"), Is.True);
            var o = RosterMoves.Apply(s, my, "WV009", Shortstop);
            Assert.That(o.Moved, Is.True);
            Assert.That(Mine(s).Reserves.Any(p => p.Id == "WV009"), Is.True);
            Assert.That(s.MyLineup.Bench.Contains(Shortstop), Is.True);
            Assert.That(s.MyLineup.Batting.Any(b => b.PlayerId == "WV011"), Is.True, "카드 선발 타순은 그대로");
            Assert.That(s.MyLineup.Repair(Mine(s)), Is.Not.Null);
        }

        [Test]
        public void 저장하고_불러와도_맞바꾸기가_남고_이전_형식은_이적_기록이다()
        {
            var s = WithFutures();
            Move(s, "WV011", Shortstop);
            var loaded = SeasonState.FromSaveData(s.ToSaveData());
            var r = loaded.Signings.Single();
            Assert.That((r.IsRosterMove, r.PlayerId, r.CallUpId, r.FromTeam), Is.EqualTo((true, "WV011", Shortstop, Samsung)));
            Assert.That(Mine(loaded).Bench.Select(p => p.Id), Is.EqualTo(Mine(s).Bench.Select(p => p.Id)));

            var data = s.ToSaveData();
            data.Signings[0].CallUp = null;
            Assert.That(SeasonState.FromSaveData(data).Signings.Single().IsRosterMove, Is.False, "CallUp이 없는 기록은 이적 기록");
        }

        [Test]
        public void 트레이드와_순서가_섞여도_그때와_같은_선수단이_나오고_맞지_않는_기록은_건너뛴다()
        {
            // 한유망을 벤치로 올린 뒤 그를 트레이드로 보내면, 받은 선수는 그 벤치 빈자리로 (퓨처스가 아니라)
            var s = WithFutures();
            Assert.That(Move(s, "WV011", Shortstop).Moved, Is.True);
            var trade = TradeDesk.Propose(s, Book(s), Shortstop, "BW008"); // 19세 유격수(0.6) ↔ 봉현우(C 0.30)
            Assert.That(trade.Accepted, Is.True);
            var mine = Mine(s);
            Assert.That(mine.Bench.Select(p => p.Id), Is.EqualTo(new[] { "WV010", "WV012", "BW008" }));
            Assert.That(mine.Reserves.Select(p => p.Id), Is.EqualTo(new[] { "WV011", Catcher, Arm }));
            Assert.That(Book(s).Applied.Count, Is.EqualTo(3));

            // 이미 퓨처스에 없는 선수를 올리는 기록은 건너뛴다 (DB가 바뀐 이전 저장 파일 대비)
            s.AddRosterMove(SigningRecord.Move(Samsung, "WV010", Shortstop));
            Assert.That(Book(s).Applied.Count, Is.EqualTo(3));
            Assert.That(Mine(s).Bench.Select(p => p.Id), Is.EqualTo(new[] { "WV010", "WV012", "BW008" }));
        }
    }
}
