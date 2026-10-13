using System.Linq;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class BranchScenarioTests
    {
        static readonly TacticKind[] Tactics =
            Fixture8thBottomTactics.Create().Branch.Tactics.ToArray();

        [TestCaseSource(nameof(Tactics))]
        public void 모든_작전_경로가_일관성_검사를_통과(TacticKind tactic)
        {
            var f = Fixture8thBottomTactics.Create();
            var path = f.Branch.PathFor(f.Events, tactic);
            var errors = PitchEventValidator.ValidateSequence(path);
            Assert.That(errors, Is.Empty, $"{tactic}\n" + string.Join("\n", errors));
        }

        [Test]
        public void 기본_구간은_분기점에서_끝난다()
        {
            var f = Fixture8thBottomTactics.Create();
            Assert.That(f.Events.Count, Is.EqualTo(f.Branch.AfterIndex + 1));
            var last = f.Events.Last();
            Assert.That(last.Outcome, Is.EqualTo(PlateAppearanceOutcome.None), "타석 도중(1-1)에 작전 선택");
            Assert.That(last.CountAfter, Is.EqualTo(new Count(1, 1)));
            Assert.That(last.RunnersAfter.ToString(), Is.EqualTo("1·3루"));
            Assert.That(last.OutsAfter, Is.EqualTo(1));
        }

        [TestCase(TacticKind.Default)]
        [TestCase(TacticKind.SwingAway)]
        public void 강공_경로는_기존_8회말_장면과_투구별로_같다(TacticKind tactic)
        {
            var f = Fixture8thBottomTactics.Create();
            var path = f.Branch.PathFor(f.Events, tactic);
            var original = Fixture8thBottom.Create().Events;
            Assert.That(path.Count, Is.EqualTo(original.Count));
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i];
                var b = original[i];
                Assert.That((a.BatterId, a.Result, a.LocationX, a.LocationY, a.CountAfter, a.PlayType, a.RunsScored, a.OutsAfter),
                    Is.EqualTo((b.BatterId, b.Result, b.LocationX, b.LocationY, b.CountAfter, b.PlayType, b.RunsScored, b.OutsAfter)),
                    $"{i + 1}구");
            }
        }

        [Test]
        public void 스퀴즈_경로는_3루_주자_득점_후_이닝_종료()
        {
            var f = Fixture8thBottomTactics.Create();
            var path = f.Branch.PathFor(f.Events, TacticKind.SqueezeBunt);
            var bunt = path[f.Branch.AfterIndex + 1];
            Assert.That(bunt.PlayType, Is.EqualTo(PlayType.SacrificeBunt));
            Assert.That(bunt.RunsScored, Is.EqualTo(1));
            Assert.That(bunt.OutsAfter, Is.EqualTo(2));
            Assert.That(PlayTypeRules.CountsAsAtBat(bunt), Is.False, "희생 번트는 타수 아님");
            Assert.That(path.Last().OutsAfter, Is.EqualTo(3));
        }

        [Test]
        public void 분기_경로는_기본_구간_이벤트를_그대로_이어_쓴다()
        {
            var f = Fixture8thBottomTactics.Create();
            var path = f.Branch.PathFor(f.Events, TacticKind.SqueezeBunt);
            for (int i = 0; i < f.Events.Count; i++)
                Assert.That(path[i], Is.SameAs(f.Events[i]));
        }

        [Test]
        public void 분기점에_없는_작전과_길이가_맞지_않는_기본_구간은_거부()
        {
            var f = Fixture8thBottomTactics.Create();
            Assert.Throws<System.ArgumentException>(() => f.Branch.PathFor(f.Events.Take(1).ToList(), TacticKind.Default));
        }

        [Test]
        public void 희생_번트는_아웃_타구_규칙을_따른다()
        {
            Assert.That(PlayTypeRules.IsOut(PlayType.SacrificeBunt), Is.True);
            Assert.That(PlayTypeRules.IsHit(PlayType.SacrificeBunt), Is.False);

            var f = Fixture8thBottomTactics.Create();
            var bunt = f.Branch.PathFor(f.Events, TacticKind.SqueezeBunt)[2];
            bunt.RunnersAfter = new BaseState(bunt.BatterId, bunt.RunnersAfter.Second, null);
            bunt.OutsAfter = 1;
            Assert.That(PitchEventValidator.Validate(bunt), Has.Some.Contains("타자가 루상에 있음"));
        }
    }
}
