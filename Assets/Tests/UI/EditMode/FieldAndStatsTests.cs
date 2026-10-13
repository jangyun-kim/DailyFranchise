using System;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;
using UnityEngine;

namespace DailyFranchise.UI.Tests
{
    public class FieldGeometryTests
    {
        [Test]
        public void 진루_경로는_베이스를_차례로_거친다()
        {
            var path = FieldGeometry.RunPath(1, 4);
            Assert.That(path, Is.EqualTo(new[]
            {
                FieldGeometry.First, FieldGeometry.Second, FieldGeometry.Third, FieldGeometry.Home,
            }));
            Assert.That(FieldGeometry.RunPath(2, 2), Has.Count.EqualTo(1));
            Assert.Throws<ArgumentException>(() => FieldGeometry.RunPath(3, 1));
        }

        [Test]
        public void 경로_진행률은_구간을_균등하게_나눈다()
        {
            var path = FieldGeometry.RunPath(0, 2); // 홈 → 1루 → 2루
            Assert.That(FieldGeometry.PointOnPath(path, 0f), Is.EqualTo(FieldGeometry.Home));
            Assert.That(FieldGeometry.PointOnPath(path, 0.5f), Is.EqualTo(FieldGeometry.First));
            Assert.That(FieldGeometry.PointOnPath(path, 1f), Is.EqualTo(FieldGeometry.Second));
        }

        [Test]
        public void 타구_방향은_양수가_1루_쪽()
        {
            var right = FieldGeometry.BattedBallEnd(25, PlayType.Single);
            var left = FieldGeometry.BattedBallEnd(-25, PlayType.Single);
            var center = FieldGeometry.BattedBallEnd(0, PlayType.Single);
            Assert.That(right.x, Is.GreaterThan(FieldGeometry.Home.x));
            Assert.That(left.x, Is.LessThan(FieldGeometry.Home.x));
            Assert.That(center.x, Is.EqualTo(FieldGeometry.Home.x).Within(0.01f));
            Assert.That(center.y, Is.LessThan(FieldGeometry.Home.y), "중견 방향은 화면 위");
        }

        [Test]
        public void 파울라인_45도는_1루_베이스를_지난다()
        {
            var end = FieldGeometry.BattedBallEnd(45, PlayType.Double);
            var toFirst = (FieldGeometry.First - FieldGeometry.Home).normalized;
            var toEnd = (end - FieldGeometry.Home).normalized;
            Assert.That(Vector2.Dot(toFirst, toEnd), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void 모든_타구_끝점은_무대_안()
        {
            foreach (PlayType t in Enum.GetValues(typeof(PlayType)))
            {
                if (t == PlayType.None) continue;
                for (int a = -90; a <= 90; a += 5)
                {
                    var p = FieldGeometry.BattedBallEnd(a, t);
                    Assert.That(p.x, Is.InRange(FieldGeometry.EdgeMargin - 0.01f, FieldGeometry.StageWidth - FieldGeometry.EdgeMargin + 0.01f), $"{t} {a}°");
                    Assert.That(p.y, Is.InRange(FieldGeometry.EdgeMargin - 0.01f, FieldGeometry.StageHeight - FieldGeometry.EdgeMargin + 0.01f), $"{t} {a}°");
                }
            }
        }

        [Test]
        public void 내야_경계_거리는_2루_방향이_가장_멀고_1루_방향이_가장_가깝다()
        {
            Assert.That(FieldGeometry.InfieldEdgeDistance(0), Is.EqualTo(Vector2.Distance(FieldGeometry.Home, FieldGeometry.Second)).Within(0.01f));
            Assert.That(FieldGeometry.InfieldEdgeDistance(45), Is.EqualTo(Vector2.Distance(FieldGeometry.Home, FieldGeometry.First)).Within(0.01f));
        }

        static bool InsideDiamond(Vector2 p)
        {
            var center = (FieldGeometry.Home + FieldGeometry.Second) / 2f;
            float half = (FieldGeometry.Home.y - FieldGeometry.Second.y) / 2f;
            return Mathf.Abs(p.x - center.x) + Mathf.Abs(p.y - center.y) <= half + 0.01f;
        }

        [TestCase(-40)]
        [TestCase(-25)]
        [TestCase(0)]
        [TestCase(25)]
        [TestCase(40)]
        public void 안타는_외야까지_땅볼은_내야_안(double angle)
        {
            Assert.That(InsideDiamond(FieldGeometry.BattedBallEnd(angle, PlayType.Single)), Is.False);
            Assert.That(InsideDiamond(FieldGeometry.BattedBallEnd(angle, PlayType.GroundOut)), Is.True);
        }

        [Test]
        public void 같은_방향이면_장타일수록_타구선이_길다()
        {
            Assert.That(FieldGeometry.BattedBallLength(10, PlayType.Single), Is.LessThan(FieldGeometry.BattedBallLength(10, PlayType.Double)));
            Assert.That(FieldGeometry.BattedBallLength(10, PlayType.Double), Is.LessThan(FieldGeometry.BattedBallLength(10, PlayType.HomeRun)));
        }
    }

    public class BattingStatsTests
    {
        static readonly ScenarioFixture F = Fixture8thBottom.Create();

        [Test]
        public void 적시타_전후_홈팀_안타_수()
        {
            Assert.That(ScenarioReadModel.HitsThrough(F.Events, 3, HalfInning.Bottom), Is.EqualTo(0));
            Assert.That(ScenarioReadModel.HitsThrough(F.Events, 4, HalfInning.Bottom), Is.EqualTo(1));
            Assert.That(ScenarioReadModel.HitsThrough(F.Events, 7, HalfInning.Top), Is.EqualTo(0));
        }

        [Test]
        public void 타자별_장면_타수와_안타()
        {
            Assert.That(ScenarioReadModel.BatterLineThrough(F.Events, 3, Fixture8thBottom.Batter1), Is.EqualTo((0, 0)));
            Assert.That(ScenarioReadModel.BatterLineThrough(F.Events, 4, Fixture8thBottom.Batter1), Is.EqualTo((1, 1)));
            Assert.That(ScenarioReadModel.BatterLineThrough(F.Events, 6, Fixture8thBottom.Batter2), Is.EqualTo((0, 0)));
            Assert.That(ScenarioReadModel.BatterLineThrough(F.Events, 7, Fixture8thBottom.Batter2), Is.EqualTo((1, 0)));
        }

        [Test]
        public void 오늘_기록_문구()
        {
            Assert.That(PitchPresentation.TodayBattingText(4, 2, null), Is.EqualTo("오늘 4타수 2안타"));
            Assert.That(PitchPresentation.TodayBattingText(3, 0, "1볼넷"), Is.EqualTo("오늘 3타수 무안타 1볼넷"));
        }

        [Test]
        public void 타구_종류와_방향_문구()
        {
            Assert.That(PitchPresentation.PlayTypeText(PlayType.Single), Is.EqualTo("단타"));
            Assert.That(PitchPresentation.PlayTypeText(PlayType.HomeRun), Is.EqualTo("홈런"));
            Assert.That(PitchPresentation.DirectionText(25), Is.EqualTo("우측 25°"));
            Assert.That(PitchPresentation.DirectionText(-30), Is.EqualTo("좌측 30°"));
            Assert.That(PitchPresentation.DirectionText(5), Is.EqualTo("중앙"));
        }
    }
}
