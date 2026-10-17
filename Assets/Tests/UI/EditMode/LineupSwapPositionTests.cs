using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 40: 이종 포지션 맞바꾸기 차단 — 포수는 포수끼리, 내야·외야는 같은 수비군, 투수는 타순 자리에 서지 않는다.</summary>
    public class LineupSwapPositionTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;
        static TeamRoster Mine => Real.GetRoster("삼성 라이온즈");

        [Test]
        public void 주전_포수의_맞바꾸기_후보는_벤치_포수뿐이고_외야수_김헌곤은_없다()
        {
            var s = SeasonState.CreateDummy();
            var catcher = Mine.Lineup.Single(p => p.Position == "C");
            var candidates = PlayerDetailView.SwapCandidatesFor(Mine, s, catcher.Id);
            Assert.That(candidates, Is.Not.Empty);
            Assert.That(candidates.Select(p => p.Position), Has.All.EqualTo("C"));
            Assert.That(candidates.Select(p => p.Name), Has.No.Member("김헌곤"));
            Assert.That(candidates.Select(p => p.Name), Is.EqualTo(new[] { "이병헌" }));
        }

        [Test]
        public void 내야수는_내야끼리_외야수는_외야끼리_같은_위치가_먼저다()
        {
            var s = SeasonState.CreateDummy();
            var third = Mine.Lineup.Single(p => p.Position == "3B");
            var infield = PlayerDetailView.SwapCandidatesFor(Mine, s, third.Id);
            Assert.That(infield.Select(p => PositionMatcher.GroupOf(p.Position)), Has.All.EqualTo("IF"));
            var left = Mine.Lineup.Single(p => p.Position == "LF");
            var outfield = PlayerDetailView.SwapCandidatesFor(Mine, s, left.Id);
            Assert.That(outfield.Select(p => PositionMatcher.GroupOf(p.Position)), Has.All.EqualTo("OF"));
            Assert.That(outfield.First().Position, Is.EqualTo("LF"), "같은 위치(김헌곤 LF) 먼저");
        }

        [Test]
        public void 벤치_포수가_설_수_있는_선발_자리는_포수뿐이다()
        {
            var s = SeasonState.CreateDummy();
            var backup = Mine.Bench.Single(p => p.Position == "C");
            Assert.That(PlayerDetailView.SwapCandidatesFor(Mine, s, backup.Id).Select(p => p.Position), Is.EqualTo(new[] { "C" }));
            var outfielder = Mine.Bench.First(p => p.Position == "LF");
            Assert.That(PlayerDetailView.SwapCandidatesFor(Mine, s, outfielder.Id).Select(p => p.Position), Has.No.Member("C"),
                "외야수는 포수 자리에 못 선다");
        }

        [Test]
        public void 수비_위치_규칙_투수는_타순_자리에_서지_않는다()
        {
            var lf = new PlayerData { Position = "LF", Role = RosterRole.Bench };
            var c = new PlayerData { Position = "C", Role = RosterRole.Bench };
            var ss = new PlayerData { Position = "SS", Role = RosterRole.Bench };
            var p = new PlayerData { Position = "P", Role = RosterRole.Bullpen };
            Assert.That(LineupFit.Allows("C", c), Is.True);
            Assert.That(LineupFit.Allows("C", lf), Is.False);
            Assert.That(LineupFit.Allows("LF", c), Is.False, "포수는 다른 자리에 서지 않는다");
            Assert.That(LineupFit.Allows("2B", ss), Is.True);
            Assert.That(LineupFit.Allows("RF", ss), Is.False);
            Assert.That(LineupFit.Allows("DH", lf) && LineupFit.Allows("DH", ss), Is.True);
            Assert.That(LineupFit.Allows("DH", c), Is.False);
            Assert.That(LineupFit.Allows("DH", p) || LineupFit.Allows("P", p), Is.False, "투수는 타순 자리 불가");
            Assert.That(PlayerDetailView.SwapCandidatesFor(Mine, SeasonState.CreateDummy(), Mine.Starters[0].Id), Is.Empty);
        }
    }
}
