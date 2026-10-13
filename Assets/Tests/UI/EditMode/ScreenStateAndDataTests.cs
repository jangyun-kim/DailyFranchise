using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    public class ScreenStateTests
    {
        static readonly ScenarioFixture F = Fixture8thBottom.Create();
        static ScreenState At(int i) => ScreenState.Build(F.Events, F.AwayScoreBefore, F.HomeScoreBefore, i);

        [Test]
        public void 첫_투구_전에는_첫_이벤트의_투구_전_상태()
        {
            var s = At(-1);
            Assert.That(s.Pitch, Is.Null);
            Assert.That(s.Outs, Is.EqualTo(1));
            Assert.That(s.Runners.ToString(), Is.EqualTo("1·3루"));
            Assert.That(s.Count, Is.EqualTo(Count.Zero));
            Assert.That(s.BatterId, Is.EqualTo(Fixture8thBottom.Batter1));
            Assert.That(s.PitchNumberInPlateAppearance, Is.EqualTo(0));
            Assert.That((s.AwayScore, s.HomeScore), Is.EqualTo((3, 2)));
        }

        [Test]
        public void 적시타_후_동점과_주자_변화()
        {
            var s = At(4);
            Assert.That((s.AwayScore, s.HomeScore), Is.EqualTo((3, 3)));
            Assert.That(s.Runners.ToString(), Is.EqualTo("1·2루"));
            Assert.That(s.RunnersBefore.ToString(), Is.EqualTo("1·3루"));
            Assert.That(s.PlateAppearanceEnded, Is.True);
            Assert.That(s.PitchNumberInPlateAppearance, Is.EqualTo(5));
        }

        [Test]
        public void 새_타석은_1구째부터()
        {
            var s = At(5);
            Assert.That(s.BatterId, Is.EqualTo(Fixture8thBottom.Batter2));
            Assert.That(s.PitchNumberInPlateAppearance, Is.EqualTo(1));
            Assert.That(s.PlateAppearanceStart, Is.EqualTo(5));
        }

        [Test]
        public void 삼진_후_2사()
        {
            var s = At(7);
            Assert.That(s.OutsBefore, Is.EqualTo(1));
            Assert.That(s.Outs, Is.EqualTo(2));
            Assert.That(s.PlateAppearanceEnded, Is.True);
            Assert.That(s.PitcherPitchesInScene, Is.EqualTo(8));
        }
    }

    public class DummyScenarioDataTests
    {
        static readonly DummyScenarioData Data = new DummyScenarioData();
        static readonly ScenarioFixture F = Fixture8thBottom.Create();

        [Test]
        public void 고정_장면의_모든_선수_ID에_이름이_있다()
        {
            var ids = F.Events.SelectMany(e => new[]
            {
                e.PitcherId, e.BatterId,
                e.RunnersBefore.First, e.RunnersBefore.Second, e.RunnersBefore.Third,
                e.RunnersAfter.First, e.RunnersAfter.Second, e.RunnersAfter.Third,
            }).Where(id => id != null).Distinct();

            foreach (var id in ids)
                Assert.That(Data.GetPlayer(id).Name, Is.Not.EqualTo(id), $"{id} 이름 없음");
        }

        [Test]
        public void 선수단_목록의_모든_ID에_이름이_있다()
        {
            var entries = Data.GetLineup(Data.MyTeam)
                .Concat(Data.GetBench(Data.MyTeam))
                .Concat(Data.GetBullpen(Data.MyTeam));
            foreach (var entry in entries)
                Assert.That(Data.GetPlayer(entry.PlayerId).Name, Is.Not.EqualTo(entry.PlayerId));
            Assert.That(Data.GetLineup(Data.MyTeam).Count, Is.EqualTo(9));
        }

        [Test]
        public void 이닝별_득점_합이_장면_시작_점수와_같다()
        {
            Assert.That(Data.GetBoxSeed(TeamSide.Away).RunsByInning.Sum(), Is.EqualTo(F.AwayScoreBefore));
            Assert.That(Data.GetBoxSeed(TeamSide.Home).RunsByInning.Sum(), Is.EqualTo(F.HomeScoreBefore));
        }

        [Test]
        public void 모르는_ID는_ID를_이름으로_대체()
        {
            var p = Data.GetPlayer("nobody");
            Assert.That(p, Is.Not.Null);
            Assert.That(p.Name, Is.EqualTo("nobody"));
        }

        [Test]
        public void 주목_선수가_존재한다()
        {
            Assert.That(Data.GetPlayer(Data.FeaturedPlayerId).Name, Is.Not.EqualTo(Data.FeaturedPlayerId));
        }
    }
}
