using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    /// <summary>작전 성공/실패 확률 분기. 난수는 고정 Seed 또는 고정 값으로만 쓴다.</summary>
    public class TacticOutcomeTests
    {
        static IEnumerable<TestCaseData> AllPaths()
        {
            var branch = Fixture8thBottomTactics.Create().Branch;
            foreach (var tactic in branch.Tactics)
            {
                yield return new TestCaseData(tactic, TacticOutcome.Success);
                if (branch[tactic].IsGamble) yield return new TestCaseData(tactic, TacticOutcome.Failure);
            }
        }

        [TestCaseSource(nameof(AllPaths))]
        public void 성공과_실패_경로_모두_일관성_검사를_통과(TacticKind tactic, TacticOutcome outcome)
        {
            var f = Fixture8thBottomTactics.Create();
            var path = f.Branch.PathFor(f.Events, tactic, outcome);
            var errors = PitchEventValidator.ValidateSequence(path);
            Assert.That(errors, Is.Empty, $"{tactic}/{outcome}\n" + string.Join("\n", errors));
        }

        [Test]
        public void 기본_진행은_확률_판정이_없고_난수를_쓰지_않는다()
        {
            var branch = Fixture8thBottomTactics.Create().Branch;
            Assert.That(branch[TacticKind.Default].IsGamble, Is.False);
            var random = new ScriptedRandom(0.99);
            Assert.That(branch.Resolve(TacticKind.Default, random), Is.EqualTo(TacticOutcome.Success));
            Assert.That(random.Draws, Is.Zero);
            Assert.Throws<System.ArgumentException>(() => branch[TacticKind.Default].For(TacticOutcome.Failure));
        }

        [TestCase(0.0, TacticOutcome.Success)]
        [TestCase(0.5999, TacticOutcome.Success)]
        [TestCase(0.6, TacticOutcome.Failure)]
        [TestCase(0.9999, TacticOutcome.Failure)]
        public void 스퀴즈는_난수가_성공률_미만이면_성공(double roll, TacticOutcome expected)
        {
            var branch = Fixture8thBottomTactics.Create().Branch;
            Assert.That(branch[TacticKind.SqueezeBunt].SuccessRate, Is.EqualTo(0.6));
            var random = new ScriptedRandom(roll);
            Assert.That(branch.Resolve(TacticKind.SqueezeBunt, random), Is.EqualTo(expected));
            Assert.That(random.Draws, Is.EqualTo(1), "확률 작전은 난수를 정확히 하나 쓴다");
        }

        [Test]
        public void 같은_Seed면_같은_결과_수열이_나온다()
        {
            var branch = Fixture8thBottomTactics.Create().Branch;
            List<TacticOutcome> Run(int seed)
            {
                var random = new SeededRandom(seed);
                return Enumerable.Range(0, 50).Select(_ => branch.Resolve(TacticKind.SqueezeBunt, random)).ToList();
            }

            var a = Run(20261014);
            Assert.That(Run(20261014), Is.EqualTo(a));
            Assert.That(a, Has.Some.EqualTo(TacticOutcome.Success));
            Assert.That(a, Has.Some.EqualTo(TacticOutcome.Failure));
        }

        [Test]
        public void 고정_Seed_1000회_스퀴즈_성공_비율은_설정값_근처()
        {
            var branch = Fixture8thBottomTactics.Create().Branch;
            var random = new SeededRandom(7);
            int success = Enumerable.Range(0, 1000)
                .Count(_ => branch.Resolve(TacticKind.SqueezeBunt, random) == TacticOutcome.Success);
            // Seed가 고정이므로 매번 같은 값이다. 범위는 설정 확률이 반영되는지만 본다.
            Assert.That(success / 1000.0, Is.InRange(0.55, 0.65));
        }

        [Test]
        public void 스퀴즈_실패는_무득점_더블_아웃으로_이닝_종료되고_타수에_들어간다()
        {
            var f = Fixture8thBottomTactics.Create();
            var path = f.Branch.PathFor(f.Events, TacticKind.SqueezeBunt, TacticOutcome.Failure);
            var bunt = path[f.Branch.AfterIndex + 1];
            Assert.That(path, Has.Count.EqualTo(f.Events.Count + 1));
            Assert.That(bunt.PlayType, Is.EqualTo(PlayType.PopOut));
            Assert.That(bunt.RunsScored, Is.Zero);
            Assert.That((bunt.OutsBefore, bunt.OutsAfter), Is.EqualTo((1, 3)));
            Assert.That(bunt.RunnersAfter.Third, Is.Null, "3루 주자 아웃");
            Assert.That(PlayTypeRules.IsOut(PlayType.PopOut), Is.True);
            Assert.That(PlayTypeRules.CountsAsAtBat(bunt), Is.True, "실패한 번트 뜬공은 희생타가 아님");
        }

        [Test]
        public void 강공_실패는_무득점_병살로_이닝_종료()
        {
            var f = Fixture8thBottomTactics.Create();
            var path = f.Branch.PathFor(f.Events, TacticKind.SwingAway, TacticOutcome.Failure);
            var dp = path.Last();
            Assert.That(dp.PlayType, Is.EqualTo(PlayType.GroundOut));
            Assert.That((dp.OutsAfter - dp.OutsBefore, dp.RunsScored), Is.EqualTo((2, 0)));
            Assert.That(path.Sum(e => e.RunsScored), Is.Zero);
        }

        [Test]
        public void 실패_경로도_기본_구간_이벤트를_그대로_이어_쓴다()
        {
            var f = Fixture8thBottomTactics.Create();
            var path = f.Branch.PathFor(f.Events, TacticKind.SqueezeBunt, TacticOutcome.Failure);
            for (int i = 0; i < f.Events.Count; i++)
                Assert.That(path[i], Is.SameAs(f.Events[i]));
        }

        [Test]
        public void 고정값_난수는_값을_차례로_되풀이하고_범위_밖_값은_거부()
        {
            var r = new ScriptedRandom(0.1, 0.75);
            Assert.That(new[] { r.NextDouble(), r.NextDouble(), r.NextDouble() }, Is.EqualTo(new[] { 0.1, 0.75, 0.1 }));
            Assert.That(r.Next(4), Is.EqualTo(3), "floor(0.75 × 4)");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ScriptedRandom(1.0));
            Assert.Throws<System.ArgumentException>(() => new ScriptedRandom());
        }
    }
}
