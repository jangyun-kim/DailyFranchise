using System;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class PlayResultResolverTests
    {
        const string Batter = "home_b4";
        const string R1 = "home_b3", R2 = "home_b2", R3 = "home_b1";

        static readonly BaseState Corners = new BaseState(R1, null, R3);
        static readonly BaseState Loaded = new BaseState(R1, R2, R3);

        static PlayResolution Resolve(int outs, BaseState runners, PlayOutcome outcome) =>
            PlayResultResolver.Resolve(outs, runners, Batter, outcome);

        [Test]
        public void 단타는_2_3루_주자가_득점하고_1루_주자는_2루로_간다()
        {
            var r = Resolve(1, Corners, PlayOutcome.Single);
            Assert.That(r.RunnersAfter, Is.EqualTo(new BaseState(Batter, R1, null)));
            Assert.That((r.RunsScored, r.OutsAfter), Is.EqualTo((1, 1)));
            Assert.That(r.PlayType, Is.EqualTo(PlayType.Single));
            Assert.That(r.PlateAppearance, Is.EqualTo(PlateAppearanceOutcome.InPlay));
            Assert.That(r.EndsHalfInning, Is.False);

            var loaded = Resolve(0, Loaded, PlayOutcome.Single);
            Assert.That(loaded.RunnersAfter, Is.EqualTo(new BaseState(Batter, R1, null)));
            Assert.That(loaded.RunsScored, Is.EqualTo(2));
        }

        [Test]
        public void 장타는_진루_규칙대로_득점한다()
        {
            var d = Resolve(2, Loaded, PlayOutcome.Double);
            Assert.That(d.RunnersAfter, Is.EqualTo(new BaseState(null, Batter, R1)), "1루 주자 3루, 타자 2루");
            Assert.That((d.RunsScored, d.OutsAfter), Is.EqualTo((2, 2)));

            var t = Resolve(0, Corners, PlayOutcome.Triple);
            Assert.That(t.RunnersAfter, Is.EqualTo(new BaseState(null, null, Batter)));
            Assert.That(t.RunsScored, Is.EqualTo(2));

            var grandSlam = Resolve(1, Loaded, PlayOutcome.HomeRun);
            Assert.That(grandSlam.RunnersAfter, Is.EqualTo(BaseState.Empty));
            Assert.That(grandSlam.RunsScored, Is.EqualTo(4));
            Assert.That(Resolve(0, BaseState.Empty, PlayOutcome.HomeRun).RunsScored, Is.EqualTo(1), "솔로");
        }

        [Test]
        public void 볼넷은_밀어내기만_진루한다()
        {
            var corners = Resolve(1, Corners, PlayOutcome.Walk);
            Assert.That(corners.RunnersAfter, Is.EqualTo(new BaseState(Batter, R1, R3)), "3루 주자는 밀리지 않음");
            Assert.That((corners.RunsScored, corners.OutsAfter), Is.EqualTo((0, 1)));
            Assert.That(corners.PlateAppearance, Is.EqualTo(PlateAppearanceOutcome.Walk));
            Assert.That(corners.PlayType, Is.EqualTo(PlayType.None));

            var loaded = Resolve(2, Loaded, PlayOutcome.Walk);
            Assert.That(loaded.RunnersAfter, Is.EqualTo(new BaseState(Batter, R1, R2)));
            Assert.That(loaded.RunsScored, Is.EqualTo(1), "만루 밀어내기");
        }

        [Test]
        public void 삼진과_일반_아웃은_타자만_아웃이고_주자는_묶인다()
        {
            foreach (var o in new[] { PlayOutcome.Strikeout, PlayOutcome.GroundOut, PlayOutcome.FlyOut, PlayOutcome.LineOut, PlayOutcome.PopOut })
            {
                var r = Resolve(1, Loaded, o);
                Assert.That(r.RunnersAfter, Is.EqualTo(Loaded), o.ToString());
                Assert.That((r.RunsScored, r.OutsAfter), Is.EqualTo((0, 2)), o.ToString());
            }
            Assert.That(Resolve(0, Corners, PlayOutcome.Strikeout).PlateAppearance, Is.EqualTo(PlateAppearanceOutcome.Strikeout));
        }

        [Test]
        public void 병살은_1루_주자와_타자를_잡고_조건이_안_되면_땅볼이다()
        {
            var dp = Resolve(0, Corners, PlayOutcome.DoublePlay);
            Assert.That(dp.Outcome, Is.EqualTo(PlayOutcome.DoublePlay));
            Assert.That(dp.PlayType, Is.EqualTo(PlayType.GroundOut));
            Assert.That(dp.RunnersAfter, Is.EqualTo(new BaseState(null, null, R3)), "3루 주자는 묶임");
            Assert.That((dp.RunsScored, dp.OutsAfter), Is.EqualTo((0, 2)));

            var endsInning = Resolve(1, Corners, PlayOutcome.DoublePlay);
            Assert.That(endsInning.OutsAfter, Is.EqualTo(3));
            Assert.That(endsInning.EndsHalfInning, Is.True);
            Assert.That(endsInning.RunsScored, Is.EqualTo(0));

            var noRunnerOnFirst = Resolve(0, new BaseState(null, R2, null), PlayOutcome.DoublePlay);
            Assert.That(noRunnerOnFirst.Outcome, Is.EqualTo(PlayOutcome.GroundOut));
            Assert.That(noRunnerOnFirst.OutsAfter, Is.EqualTo(1));

            var twoOuts = Resolve(2, Corners, PlayOutcome.DoublePlay);
            Assert.That(twoOuts.Outcome, Is.EqualTo(PlayOutcome.GroundOut), "2사면 타자만 잡아도 3아웃");
            Assert.That(twoOuts.OutsAfter, Is.EqualTo(3));
        }

        [Test]
        public void 희생플라이는_2아웃_전_3루_주자만_득점시킨다()
        {
            var sf = Resolve(1, Corners, PlayOutcome.SacrificeFly);
            Assert.That(sf.Outcome, Is.EqualTo(PlayOutcome.SacrificeFly));
            Assert.That(sf.PlayType, Is.EqualTo(PlayType.FlyOut));
            Assert.That(sf.RunnersAfter, Is.EqualTo(new BaseState(R1, null, null)));
            Assert.That((sf.RunsScored, sf.OutsAfter), Is.EqualTo((1, 2)));

            var twoOuts = Resolve(2, Corners, PlayOutcome.SacrificeFly);
            Assert.That(twoOuts.Outcome, Is.EqualTo(PlayOutcome.FlyOut), "3아웃 뜬공은 득점 없음");
            Assert.That((twoOuts.RunsScored, twoOuts.OutsAfter), Is.EqualTo((0, 3)));

            Assert.That(Resolve(0, new BaseState(R1, null, null), PlayOutcome.SacrificeFly).Outcome, Is.EqualTo(PlayOutcome.FlyOut));
        }

        [Test]
        public void 스퀴즈_성공과_실패()
        {
            var squeeze = Resolve(1, Corners, PlayOutcome.SacrificeBunt);
            Assert.That(squeeze.PlayType, Is.EqualTo(PlayType.SacrificeBunt));
            Assert.That(squeeze.RunnersAfter, Is.EqualTo(new BaseState(null, R1, null)), "1루 주자 2루");
            Assert.That((squeeze.RunsScored, squeeze.OutsAfter), Is.EqualTo((1, 2)));

            var failed = Resolve(0, Corners, PlayOutcome.FailedSqueeze);
            Assert.That(failed.PlayType, Is.EqualTo(PlayType.PopOut));
            Assert.That(failed.RunnersAfter, Is.EqualTo(new BaseState(R1, null, null)), "3루 주자 더블 아웃");
            Assert.That((failed.RunsScored, failed.OutsAfter), Is.EqualTo((0, 2)));

            var twoOutBunt = Resolve(2, Corners, PlayOutcome.SacrificeBunt);
            Assert.That((twoOutBunt.RunsScored, twoOutBunt.OutsAfter), Is.EqualTo((0, 3)), "2사 번트 아웃은 이닝 종료, 득점 없음");
        }

        [Test]
        public void 모든_상황_모든_결과가_일관성_규칙을_지킨다()
        {
            for (int outs = 0; outs <= 2; outs++)
            for (int mask = 0; mask < 8; mask++)
            {
                var runners = new BaseState((mask & 1) != 0 ? R1 : null, (mask & 2) != 0 ? R2 : null, (mask & 4) != 0 ? R3 : null);
                foreach (PlayOutcome o in Enum.GetValues(typeof(PlayOutcome)))
                {
                    var r = Resolve(outs, runners, o);
                    Assert.That(r.OutsAfter, Is.InRange(outs, 3));
                    // 타자 + 주자 = 루상 + 득점 + 아웃 (보존)
                    Assert.That(r.RunnersAfter.RunnerCount + r.RunsScored + (r.OutsAfter - outs), Is.EqualTo(runners.RunnerCount + 1),
                        $"{outs}사 {runners} {o}");
                    if (r.OutsAfter >= 3 && r.PlateAppearance == PlateAppearanceOutcome.InPlay)
                        Assert.That(r.RunsScored, Is.Zero, $"{outs}사 {runners} {o}: 3아웃 타구 득점");
                }
            }
        }

        [Test]
        public void 아웃_수가_범위_밖이면_예외()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(3, BaseState.Empty, PlayOutcome.Single));
            Assert.Throws<ArgumentException>(() => PlayResultResolver.Resolve(0, BaseState.Empty, null, PlayOutcome.Single));
        }

        [Test]
        public void 타순은_9번_다음_1번으로_돈다()
        {
            Assert.That(BattingOrder.Next("home_b3"), Is.EqualTo("home_b4"));
            Assert.That(BattingOrder.Next("home_b9"), Is.EqualTo("home_b1"));
            Assert.That(BattingOrder.Next("away_b1"), Is.EqualTo("away_b2"));
            Assert.That(BattingOrder.OrderOf("home_b7"), Is.EqualTo(7));
            Assert.That(BattingOrder.OrderOf("home_ph1"), Is.Null);
            Assert.That(BattingOrder.OrderOf("away_sp"), Is.Null);
            Assert.That(BattingOrder.OrderOf("home_b10"), Is.Null);
            Assert.Throws<ArgumentException>(() => BattingOrder.Next("home_rp1"));
        }
    }
}
