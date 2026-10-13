using System.Linq;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class ScenarioCatalogTests
    {
        static readonly string[] Ids = ScenarioCatalog.All.Select(e => e.Id).ToArray();

        [TestCaseSource(nameof(Ids))]
        public void 모든_시나리오가_일관성_검사를_통과(string id)
        {
            var f = ScenarioCatalog.Find(id).Create();
            var errors = PitchEventValidator.ValidateSequence(f.Events);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(f.Id, Is.EqualTo(id));
        }

        [Test]
        public void 시나리오_ID는_중복되지_않는다()
        {
            Assert.That(Ids, Is.Unique);
            Assert.That(Ids[0], Is.EqualTo(ScenarioCatalog.EighthBottom), "기본 장면이 첫 항목");
        }

        [Test]
        public void 홈런_시나리오는_좌중간_투런()
        {
            var hr = FixtureHomeRun.Create().Events.Last();
            Assert.That(hr.PlayType, Is.EqualTo(PlayType.HomeRun));
            Assert.That(hr.SprayAngleDeg, Is.LessThan(0), "좌중간 = 3루 쪽(-)");
            Assert.That(hr.RunsScored, Is.EqualTo(2));
            Assert.That(hr.RunnersAfter, Is.EqualTo(BaseState.Empty));
        }

        [Test]
        public void 병살_시나리오는_아웃이_2개_늘어난다()
        {
            var dp = FixtureDoublePlay.Create().Events.Last();
            Assert.That(dp.PlayType, Is.EqualTo(PlayType.GroundOut));
            Assert.That(dp.OutsAfter - dp.OutsBefore, Is.EqualTo(2));
            Assert.That(dp.RunnersAfter, Is.EqualTo(BaseState.Empty));
        }

        [Test]
        public void 이닝_교대_시나리오는_3아웃_뒤_다음_반_이닝으로_이어진다()
        {
            var events = FixtureInningEnd.Create().Events;
            int third = events.ToList().FindIndex(e => e.OutsAfter == 3);
            Assert.That(third, Is.GreaterThanOrEqualTo(0));
            Assert.That(third, Is.LessThan(events.Count - 1), "3아웃 뒤에 다음 반 이닝 투구가 있어야 함");
            var next = events[third + 1];
            Assert.That((next.Inning, next.HalfInning), Is.EqualTo((9, HalfInning.Top)));
            Assert.That(next.OutsBefore, Is.EqualTo(0));
            Assert.That(next.RunnersBefore, Is.EqualTo(BaseState.Empty));
        }
    }
}
