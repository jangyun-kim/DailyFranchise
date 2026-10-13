using System;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class CountRulesTests
    {
        static CountTransition Apply(int b, int s, PitchResult r) => CountRules.Apply(new Count(b, s), r);

        [Test]
        public void 볼은_볼카운트를_올리고_4볼이면_볼넷()
        {
            Assert.That(Apply(0, 0, PitchResult.Ball).After, Is.EqualTo(new Count(1, 0)));
            Assert.That(Apply(2, 2, PitchResult.Ball).Outcome, Is.EqualTo(PlateAppearanceOutcome.None));
            var walk = Apply(3, 2, PitchResult.Ball);
            Assert.That(walk.After, Is.EqualTo(new Count(4, 2)));
            Assert.That(walk.Outcome, Is.EqualTo(PlateAppearanceOutcome.Walk));
        }

        [TestCase(PitchResult.CalledStrike)]
        [TestCase(PitchResult.SwingingStrike)]
        public void 스트라이크는_3개면_삼진(PitchResult result)
        {
            Assert.That(Apply(1, 1, result).After, Is.EqualTo(new Count(1, 2)));
            var k = Apply(3, 2, result);
            Assert.That(k.After, Is.EqualTo(new Count(3, 3)));
            Assert.That(k.Outcome, Is.EqualTo(PlateAppearanceOutcome.Strikeout));
        }

        [Test]
        public void 파울은_2스트라이크_전까지만_스트라이크()
        {
            Assert.That(Apply(0, 0, PitchResult.Foul).After, Is.EqualTo(new Count(0, 1)));
            Assert.That(Apply(0, 1, PitchResult.Foul).After, Is.EqualTo(new Count(0, 2)));
            var foul = Apply(2, 2, PitchResult.Foul);
            Assert.That(foul.After, Is.EqualTo(new Count(2, 2)));
            Assert.That(foul.Outcome, Is.EqualTo(PlateAppearanceOutcome.None));
        }

        [Test]
        public void 인플레이는_카운트를_유지하고_타석을_끝낸다()
        {
            var t = Apply(1, 2, PitchResult.InPlay);
            Assert.That(t.After, Is.EqualTo(new Count(1, 2)));
            Assert.That(t.Outcome, Is.EqualTo(PlateAppearanceOutcome.InPlay));
        }

        [Test]
        public void 끝난_카운트에는_적용할_수_없다()
        {
            Assert.Throws<ArgumentException>(() => Apply(4, 0, PitchResult.Ball));
            Assert.Throws<ArgumentException>(() => Apply(0, 3, PitchResult.Ball));
        }

        [Test]
        public void 볼넷_밀어내기_진루()
        {
            var (a, runs) = BaseRunning.ForceAdvanceOnWalk(BaseState.Empty, "bat");
            Assert.That(a, Is.EqualTo(new BaseState("bat", null, null)));
            Assert.That(runs, Is.EqualTo(0));

            // 1·3루: 1루 주자만 밀리고 3루 주자는 그대로
            (a, runs) = BaseRunning.ForceAdvanceOnWalk(new BaseState("r1", null, "r3"), "bat");
            Assert.That(a, Is.EqualTo(new BaseState("bat", "r1", "r3")));
            Assert.That(runs, Is.EqualTo(0));

            // 2루만: 밀리지 않음
            (a, runs) = BaseRunning.ForceAdvanceOnWalk(new BaseState(null, "r2", null), "bat");
            Assert.That(a, Is.EqualTo(new BaseState("bat", "r2", null)));

            // 만루: 밀어내기 1득점
            (a, runs) = BaseRunning.ForceAdvanceOnWalk(new BaseState("r1", "r2", "r3"), "bat");
            Assert.That(a, Is.EqualTo(new BaseState("bat", "r1", "r2")));
            Assert.That(runs, Is.EqualTo(1));
        }

        [Test]
        public void 주자_상황_표기()
        {
            Assert.That(BaseState.Empty.ToString(), Is.EqualTo("주자 없음"));
            Assert.That(new BaseState("a", null, "c").ToString(), Is.EqualTo("1·3루"));
            Assert.That(new BaseState("a", "b", "c").ToString(), Is.EqualTo("만루"));
        }
    }
}
