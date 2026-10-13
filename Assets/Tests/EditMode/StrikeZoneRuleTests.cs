using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class StrikeZoneRuleTests
    {
        static readonly StrikeZone Zone = new StrikeZone(halfWidthCm: 23.59, topCm: 100, bottomCm: 50, ballRadiusCm: 0);

        [TestCase(0, 75, true, TestName = "중앙")]
        [TestCase(23.59, 75, true, TestName = "우측 경계선 위")]
        [TestCase(-23.59, 75, true, TestName = "좌측 경계선 위")]
        [TestCase(0, 100, true, TestName = "상단 경계선 위")]
        [TestCase(0, 50, true, TestName = "하단 경계선 위")]
        [TestCase(23.6, 75, false, TestName = "우측 경계 바로 밖")]
        [TestCase(-23.6, 75, false, TestName = "좌측 경계 바로 밖")]
        [TestCase(0, 100.01, false, TestName = "상단 바로 위")]
        [TestCase(0, 49.99, false, TestName = "하단 바로 아래")]
        [TestCase(-30, 42, false, TestName = "바깥쪽 낮은 공")]
        public void IsStrike_경계_판정(double x, double y, bool expected)
        {
            Assert.That(StrikeZoneRule.IsStrike(Zone, x, y), Is.EqualTo(expected));
        }

        [Test]
        public void 공_반지름만큼_존이_넓어진다()
        {
            var zone = new StrikeZone(23.59, 100, 50, ballRadiusCm: 3.7);
            Assert.That(StrikeZoneRule.IsStrike(zone, 27.2, 75), Is.True);
            Assert.That(StrikeZoneRule.IsStrike(zone, 0, 103.6), Is.True);
            Assert.That(StrikeZoneRule.IsStrike(zone, 27.4, 75), Is.False);
        }

        [Test]
        public void 신장_비율로_존을_계산한다()
        {
            var rule = new ZoneRuleConfig("test", 43.18, 2.0, 0.5, 0.25, 0, isProvisional: true);
            var zone = rule.ZoneFor(200);
            Assert.That(zone.HalfWidthCm, Is.EqualTo(23.59).Within(1e-9));
            Assert.That(zone.TopCm, Is.EqualTo(100).Within(1e-9));
            Assert.That(zone.BottomCm, Is.EqualTo(50).Within(1e-9));
        }

        [Test]
        public void 키가_클수록_존이_높다()
        {
            var rule = ZoneRuleConfig.ProvisionalKbo;
            var tall = rule.ZoneFor(190);
            var short_ = rule.ZoneFor(170);
            Assert.That(tall.TopCm, Is.GreaterThan(short_.TopCm));
            Assert.That(tall.BottomCm, Is.GreaterThan(short_.BottomCm));
            Assert.That(tall.HalfWidthCm, Is.EqualTo(short_.HalfWidthCm));
        }

        [Test]
        public void 임시_KBO_규칙은_Provisional로_표시된다()
        {
            Assert.That(ZoneRuleConfig.ProvisionalKbo.IsProvisional, Is.True);
        }
    }
}
