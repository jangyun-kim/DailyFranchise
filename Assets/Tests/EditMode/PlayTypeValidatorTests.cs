using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class PlayTypeValidatorTests
    {
        static readonly StrikeZone Zone = new StrikeZone(23.59, 100, 50, 0);

        /// <summary>1사 1·3루에서 우전 단타: 3루 주자 득점, 1루 주자 2루, 타자 1루.</summary>
        static PitchEvent Single() => new PitchEvent
        {
            Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = 1,
            CountBefore = new Count(1, 2),
            PitcherId = "p", BatterId = "bat", PitchType = "슬라이더", VelocityKmh = 135,
            LocationX = 0, LocationY = 75, Zone = Zone,
            Result = PitchResult.InPlay, IsStrikeByRule = true,
            CountAfter = new Count(1, 2), Outcome = PlateAppearanceOutcome.InPlay,
            PlayResult = "우전 적시타", PlayType = PlayType.Single, SprayAngleDeg = 25,
            RunnersBefore = new BaseState("r1", null, "r3"),
            RunnersAfter = new BaseState("bat", "r1", null),
            RunsScored = 1, OutsAfter = 1,
        };

        static void AssertValid(PitchEvent e) =>
            Assert.That(PitchEventValidator.Validate(e), Is.Empty, string.Join("\n", PitchEventValidator.Validate(e)));

        static void AssertInvalid(PitchEvent e, string fragment) =>
            Assert.That(PitchEventValidator.Validate(e), Has.Some.Contains(fragment));

        [Test]
        public void 정상_적시타는_통과() => AssertValid(Single());

        [Test]
        public void 고정_데이터_5구는_우측_단타()
        {
            var e = Fixture8thBottom.Create().Events[4];
            Assert.That(e.PlayType, Is.EqualTo(PlayType.Single));
            Assert.That(e.SprayAngleDeg, Is.GreaterThan(0), "우전 = 1루·우익 쪽(+)");
        }

        [Test]
        public void 인플레이에는_PlayType과_방향이_필요()
        {
            var e = Single();
            e.PlayType = PlayType.None;
            AssertInvalid(e, "PlayType이 None");

            e = Single();
            e.SprayAngleDeg = null;
            AssertInvalid(e, "SprayAngleDeg가 없음");
        }

        [Test]
        public void 인플레이가_아니면_PlayType과_방향이_없어야_함()
        {
            var e = new PitchEvent
            {
                Inning = 1, OutsBefore = 0, CountBefore = Count.Zero,
                PitcherId = "p", BatterId = "b", PitchType = "포심", VelocityKmh = 140,
                LocationX = 0, LocationY = 75, Zone = Zone,
                Result = PitchResult.Foul, IsStrikeByRule = true, CountAfter = new Count(0, 1),
                PlayType = PlayType.Single, SprayAngleDeg = 10,
            };
            AssertInvalid(e, "인플레이가 아닌데 PlayType");
            AssertInvalid(e, "인플레이가 아닌데 SprayAngleDeg");
        }

        [Test]
        public void 안타는_파울라인_안쪽_각도()
        {
            var e = Single();
            e.SprayAngleDeg = 50;
            AssertInvalid(e, "허용 범위");
        }

        [Test]
        public void 단타_타자는_1루에()
        {
            var e = Single();
            e.PlayType = PlayType.Double;
            AssertInvalid(e, "Double 타자는 2루");
        }

        [Test]
        public void 안타_득점_수는_홈인한_주자_수와_같아야_함()
        {
            var e = Single();
            e.RunsScored = 2;
            e.OutsAfter = 1;
            AssertInvalid(e, "안타 득점 불일치");
        }

        [Test]
        public void 안타_중_아웃은_미지원()
        {
            var e = Single();
            e.RunnersAfter = new BaseState("bat", null, null);
            e.OutsAfter = 2;
            AssertInvalid(e, "안타인데 아웃");
        }

        [Test]
        public void 주자는_추월할_수_없다()
        {
            // 1루 주자가 3루 주자보다 앞(홈인)인데 3루 주자는 3루에 남음
            var e = Single();
            e.RunnersAfter = new BaseState("bat", null, "r3");
            e.RunsScored = 1;
            AssertInvalid(e, "주자 추월");
        }

        [Test]
        public void 주자는_뒤로_갈_수_없다()
        {
            var e = Single();
            e.RunnersBefore = new BaseState(null, "r2", null);
            e.RunnersAfter = new BaseState("bat", null, null);
            e.RunsScored = 1;
            AssertValid(e); // 2루 주자 홈인은 정상

            e.RunnersBefore = new BaseState(null, null, "r3");
            e.RunnersAfter = new BaseState("bat", "r3", null);
            e.RunsScored = 0;
            AssertInvalid(e, "역주행");
        }

        [Test]
        public void 홈런은_전원_득점()
        {
            var e = Single();
            e.PlayType = PlayType.HomeRun;
            e.RunnersAfter = BaseState.Empty;
            e.RunsScored = 3;
            AssertValid(e);

            e.RunsScored = 2;
            AssertInvalid(e, "안타 득점 불일치");
        }

        [Test]
        public void 희생플라이는_통과_3아웃_뜬공의_득점은_거부()
        {
            var e = Single();
            e.PlayType = PlayType.FlyOut;
            e.SprayAngleDeg = 30;
            e.RunnersAfter = new BaseState("r1", null, null);
            e.RunsScored = 1;
            e.OutsAfter = 2;
            AssertValid(e);

            e.OutsBefore = 2;
            e.OutsAfter = 3;
            AssertInvalid(e, "득점이 인정되지 않음");
        }

        [Test]
        public void 아웃_타구에서_타자는_루상에_없다()
        {
            var e = Single();
            e.PlayType = PlayType.GroundOut;
            e.RunsScored = 1;
            e.OutsAfter = 1;
            AssertInvalid(e, "아웃이 늘지 않음");
            AssertInvalid(e, "타자가 루상에 있음");
        }

        [Test]
        public void 투구_전에_없던_주자는_나타날_수_없다()
        {
            var e = Single();
            e.RunnersAfter = new BaseState("bat", "ghost", null);
            AssertInvalid(e, "없던 주자 ghost");
        }

        [Test]
        public void 타수_집계_규칙()
        {
            var e = Single();
            Assert.That(PlayTypeRules.CountsAsAtBat(e), Is.True);

            e.PlayType = PlayType.FlyOut;
            e.RunsScored = 1;
            e.OutsAfter = 2;
            Assert.That(PlayTypeRules.CountsAsAtBat(e), Is.False, "희생플라이는 타수 아님");

            e.Outcome = PlateAppearanceOutcome.Walk;
            Assert.That(PlayTypeRules.CountsAsAtBat(e), Is.False, "볼넷은 타수 아님");

            e.Outcome = PlateAppearanceOutcome.Strikeout;
            Assert.That(PlayTypeRules.CountsAsAtBat(e), Is.True);
        }

        [Test]
        public void 안타_종류별_진루_수()
        {
            Assert.That(PlayTypeRules.BasesForHit(PlayType.Single), Is.EqualTo(1));
            Assert.That(PlayTypeRules.BasesForHit(PlayType.HomeRun), Is.EqualTo(4));
            Assert.That(PlayTypeRules.BasesForHit(PlayType.FlyOut), Is.EqualTo(0));
            Assert.That(PlayTypeRules.IsHit(PlayType.Triple), Is.True);
            Assert.That(PlayTypeRules.IsOut(PlayType.LineOut), Is.True);
        }
    }
}
