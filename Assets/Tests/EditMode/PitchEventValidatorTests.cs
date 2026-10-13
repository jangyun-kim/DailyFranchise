using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class PitchEventValidatorTests
    {
        static readonly StrikeZone Zone = new StrikeZone(23.59, 100, 50, 0);

        static PitchEvent ValidBall() => new PitchEvent
        {
            Inning = 3, HalfInning = HalfInning.Top, OutsBefore = 0,
            CountBefore = Count.Zero,
            PitcherId = "p", BatterId = "b", PitchType = "포심", VelocityKmh = 145,
            LocationX = -30, LocationY = 75, Zone = Zone,
            Result = PitchResult.Ball, IsStrikeByRule = false,
            CountAfter = new Count(1, 0), Outcome = PlateAppearanceOutcome.None,
            RunnersBefore = BaseState.Empty, RunnersAfter = BaseState.Empty,
            RunsScored = 0, OutsAfter = 0,
        };

        static void AssertValid(PitchEvent e) =>
            Assert.That(PitchEventValidator.Validate(e), Is.Empty);

        static void AssertInvalid(PitchEvent e, string fragment) =>
            Assert.That(PitchEventValidator.Validate(e), Has.Some.Contains(fragment));

        [Test]
        public void 정상_볼은_통과() => AssertValid(ValidBall());

        [Test]
        public void IsStrikeByRule이_위치와_다르면_실패()
        {
            var e = ValidBall();
            e.IsStrikeByRule = true;
            AssertInvalid(e, "IsStrikeByRule");
        }

        [Test]
        public void 존_안의_공을_Ball로_판정하면_실패()
        {
            var e = ValidBall();
            e.LocationX = 0;
            e.IsStrikeByRule = true;
            AssertInvalid(e, "Ball인데 공이 존 안");
        }

        [Test]
        public void 존_밖의_공을_CalledStrike로_판정하면_실패()
        {
            var e = ValidBall();
            e.Result = PitchResult.CalledStrike;
            e.CountAfter = new Count(0, 1);
            AssertInvalid(e, "CalledStrike인데 공이 존 밖");
        }

        [Test]
        public void 존_밖_헛스윙은_정상()
        {
            var e = ValidBall();
            e.Result = PitchResult.SwingingStrike;
            e.CountAfter = new Count(0, 1);
            AssertValid(e);
        }

        [Test]
        public void 카운트_변화가_틀리면_실패()
        {
            var e = ValidBall();
            e.CountAfter = new Count(0, 1);
            AssertInvalid(e, "카운트 변화 불일치");
        }

        [Test]
        public void 타석_중간에_주자가_움직이면_실패()
        {
            var e = ValidBall();
            e.RunnersAfter = new BaseState("x", null, null);
            AssertInvalid(e, "주자·아웃·득점이 변함");
        }

        [Test]
        public void 삼진은_아웃_1개()
        {
            var e = ValidBall();
            e.CountBefore = new Count(1, 2);
            e.Result = PitchResult.SwingingStrike;
            e.CountAfter = new Count(1, 3);
            e.Outcome = PlateAppearanceOutcome.Strikeout;
            e.OutsAfter = 1;
            AssertValid(e);

            e.OutsAfter = 0;
            AssertInvalid(e, "삼진인데 아웃이 1 늘지 않음");
        }

        [Test]
        public void 볼넷_밀어내기_검증()
        {
            var e = ValidBall();
            e.CountBefore = new Count(3, 1);
            e.CountAfter = new Count(4, 1);
            e.Outcome = PlateAppearanceOutcome.Walk;
            e.RunnersBefore = new BaseState("r1", "r2", "r3");
            e.RunnersAfter = new BaseState("b", "r1", "r2");
            e.RunsScored = 1;
            AssertValid(e);

            e.RunsScored = 0;
            AssertInvalid(e, "볼넷 진루 불일치");
        }

        [Test]
        public void 인플레이_주자_보존_법칙()
        {
            var e = ValidBall();
            e.LocationX = 0;
            e.IsStrikeByRule = true;
            e.Result = PitchResult.InPlay;
            e.CountAfter = Count.Zero;
            e.Outcome = PlateAppearanceOutcome.InPlay;
            e.PlayResult = "희생플라이";
            e.RunnersBefore = new BaseState(null, null, "r3");
            e.RunnersAfter = BaseState.Empty;
            e.RunsScored = 1;
            e.OutsAfter = 1;
            AssertValid(e);

            e.RunsScored = 0;
            AssertInvalid(e, "주자 수 보존 위반");
        }

        [Test]
        public void 인플레이에는_PlayResult가_필요()
        {
            var e = ValidBall();
            e.LocationX = 0;
            e.IsStrikeByRule = true;
            e.Result = PitchResult.InPlay;
            e.CountAfter = Count.Zero;
            e.Outcome = PlateAppearanceOutcome.InPlay;
            e.RunnersAfter = new BaseState("b", null, null);
            AssertInvalid(e, "PlayResult가 없음");
        }

        // ── 연속 이벤트 ──

        [Test]
        public void 카운트_연결이_끊기면_실패()
        {
            var a = ValidBall();
            var b = ValidBall();
            b.CountBefore = new Count(0, 1);
            b.CountAfter = new Count(1, 1);
            Assert.That(PitchEventValidator.ValidateSequence(new[] { a, b }),
                Has.Some.Contains("카운트 연결 불일치"));
        }

        [Test]
        public void 새_타석은_0대0에서_시작()
        {
            var a = ValidBall();
            a.CountBefore = new Count(0, 2);
            a.Result = PitchResult.CalledStrike;
            a.LocationX = 0;
            a.IsStrikeByRule = true;
            a.CountAfter = new Count(0, 3);
            a.Outcome = PlateAppearanceOutcome.Strikeout;
            a.OutsAfter = 1;

            var b = ValidBall();
            b.BatterId = "b2";
            b.OutsBefore = 1;
            b.OutsAfter = 1;
            Assert.That(PitchEventValidator.ValidateSequence(new[] { a, b }), Is.Empty);

            b.CountBefore = new Count(1, 0);
            b.CountAfter = new Count(2, 0);
            Assert.That(PitchEventValidator.ValidateSequence(new[] { a, b }),
                Has.Some.Contains("새 타석이 0-0으로 시작하지 않음"));
        }

        // ── 테스트 장면 고정 데이터 ──

        static IReadOnlyList<PitchEvent> FixtureEvents => Fixture8thBottom.Create().Events;

        [Test]
        public void 고정_데이터_전체가_일관성_검사를_통과()
        {
            var errors = PitchEventValidator.ValidateSequence(FixtureEvents);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void 고정_데이터는_8회말_1사_1_3루에서_시작()
        {
            var first = FixtureEvents[0];
            Assert.That(first.Inning, Is.EqualTo(8));
            Assert.That(first.HalfInning, Is.EqualTo(HalfInning.Bottom));
            Assert.That(first.OutsBefore, Is.EqualTo(1));
            Assert.That(first.RunnersBefore.ToString(), Is.EqualTo("1·3루"));
            var f = Fixture8thBottom.Create();
            Assert.That(Math.Abs(f.AwayScoreBefore - f.HomeScoreBefore), Is.EqualTo(1));
        }

        [Test]
        public void 고정_데이터가_요구된_장면을_모두_포함()
        {
            var ev = FixtureEvents;
            var zone = ev[0].Zone;

            // 1. 존 중앙 스트라이크
            Assert.That(ev[0].Result, Is.EqualTo(PitchResult.CalledStrike));
            Assert.That(Math.Abs(ev[0].LocationX), Is.LessThan(zone.HalfWidthCm * 0.2));
            Assert.That(Math.Abs(ev[0].LocationY - (zone.TopCm + zone.BottomCm) / 2), Is.LessThan(5));

            // 2. 바깥쪽 낮은 볼
            Assert.That(ev[1].Result, Is.EqualTo(PitchResult.Ball));
            Assert.That(Math.Abs(ev[1].LocationX), Is.GreaterThan(zone.HalfWidthCm));
            Assert.That(ev[1].LocationY, Is.LessThan(zone.BottomCm));

            // 3. 경계 스트라이크: 경계에서 1cm 이내 안쪽
            Assert.That(ev[2].Result, Is.EqualTo(PitchResult.CalledStrike));
            Assert.That(zone.HalfWidthCm - Math.Abs(ev[2].LocationX), Is.InRange(0.0, 1.0));

            // 4. 파울
            Assert.That(ev[3].Result, Is.EqualTo(PitchResult.Foul));

            // 5. 인플레이 적시타
            Assert.That(ev[4].Outcome, Is.EqualTo(PlateAppearanceOutcome.InPlay));
            Assert.That(ev[4].RunsScored, Is.GreaterThan(0));

            // 6. 삼진 (마지막 투구)
            Assert.That(ev.Last().Outcome, Is.EqualTo(PlateAppearanceOutcome.Strikeout));
        }

        [Test]
        public void 고정_데이터의_존_밖_헛스윙은_IsStrikeByRule_false()
        {
            var swingsOutside = FixtureEvents.Where(e => e.Result == PitchResult.SwingingStrike && !e.IsStrikeByRule);
            Assert.That(swingsOutside, Is.Not.Empty);
        }
    }
}
