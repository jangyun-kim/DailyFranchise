using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class PlayByPlayGeneratorTests
    {
        static readonly TacticKind[] AllTactics = { TacticKind.Default, TacticKind.SwingAway, TacticKind.SqueezeBunt };

        /// <summary>
        /// 생성 장면을 Play-by-Play로 끝까지 돌린다: 첫 승부처 작전 → 타석 끝까지 자르기 → 다음 타자 구간 → 다시 작전 …
        /// 작전 고르기·성공 판정 모두 <paramref name="random"/>을 쓴다 (화면과 같은 순서).
        /// </summary>
        static (List<PitchEvent> path, int decisions) RunLoop(ScenarioFixture fixture, IRandomSource random,
            System.Func<BranchPoint, TacticKind> pick = null)
        {
            var path = fixture.Events.ToList();
            var branch = fixture.Branch;
            int decisions = 0;
            while (branch != null)
            {
                Assert.That(decisions, Is.LessThan(PlayByPlayGenerator.MaxPlateAppearancesPerHalf + 1), "순환이 끝나지 않음");
                var offered = branch.Tactics.ToList();
                var tactic = pick?.Invoke(branch) ?? offered[random.Next(offered.Count)];
                var outcome = branch.Resolve(tactic, random);
                var next = PlayByPlayGenerator.UntilPlateAppearanceEnd(branch.PathFor(path, tactic, outcome), branch.AfterIndex + 1);
                var segment = new PlayByPlayGenerator(null, random).Next(next);
                decisions++;
                if (segment != null)
                {
                    Assert.That(segment.Branch.AfterIndex, Is.EqualTo(next.Count + 1), "작전 전 2구 뒤 분기");
                    next.AddRange(segment.Events);
                }
                path = next;
                branch = segment?.Branch;
            }
            return (path, decisions);
        }

        static IEnumerable<PitchEvent> PlateAppearanceEnds(IEnumerable<PitchEvent> events) =>
            events.Where(e => e.Outcome != PlateAppearanceOutcome.None);

        [Test]
        public void 승부처_후속_구간은_작전_타자의_타석_끝에서_잘린다()
        {
            var s = new ClutchSituation(1, FixtureGenerator.RunnersFor(5)); // 1사 1·3루
            var f = FixtureGenerator.CreateTactics(s, 3, 2);
            var full = f.Branch.PathFor(f.Events, TacticKind.SwingAway);
            var cut = PlayByPlayGenerator.UntilPlateAppearanceEnd(full, f.Branch.AfterIndex + 1);

            Assert.That(full.Any(e => e.BatterId == FixtureGenerator.Batter2), Is.True, "각본에는 4번 타자 삼진까지 있음");
            Assert.That(cut.Last().PlayType, Is.EqualTo(PlayType.Single));
            Assert.That(cut.All(e => e.BatterId == FixtureGenerator.Batter1), Is.True);
            Assert.That(cut.Take(f.Events.Count), Is.EqualTo(f.Events), "기본 구간 객체는 그대로");
        }

        [Test]
        public void 안타_뒤_다음_타순_타자가_갱신된_아웃_주자로_1_1까지_서고_작전을_기다린다()
        {
            var s = new ClutchSituation(1, FixtureGenerator.RunnersFor(5)); // 1사 1·3루
            var f = FixtureGenerator.CreateTactics(s, 3, 2);
            var path = PlayByPlayGenerator.UntilPlateAppearanceEnd(
                f.Branch.PathFor(f.Events, TacticKind.SwingAway), f.Branch.AfterIndex + 1);
            var hit = path.Last();
            Assert.That(hit.RunsScored, Is.EqualTo(1));

            var segment = new PlayByPlayGenerator(null, new SeededRandom(1)).Next(path);
            Assert.That(segment, Is.Not.Null);
            Assert.That(segment.BatterId, Is.EqualTo("home_b4"), "3번 다음 4번");
            Assert.That(segment.Events.Select(e => e.BatterId), Has.All.EqualTo("home_b4"));
            Assert.That(segment.Events.Last().CountAfter, Is.EqualTo(new Count(1, 1)));
            Assert.That(segment.Events[0].OutsBefore, Is.EqualTo(hit.OutsAfter));
            Assert.That(segment.Events[0].RunnersBefore, Is.EqualTo(hit.RunnersAfter), "1·2루");
            Assert.That(segment.Branch.Offers(TacticKind.Default) && segment.Branch.Offers(TacticKind.SwingAway), Is.True);
            Assert.That(segment.Branch.Offers(TacticKind.SqueezeBunt), Is.False, "3루 주자가 없으면 스퀴즈 없음");

            var joined = path.Concat(segment.Events).ToList();
            Assert.That(PitchEventValidator.ValidateSequence(joined), Is.Empty);
            foreach (var tactic in segment.Branch.Tactics)
            foreach (var outcome in new[] { TacticOutcome.Success, TacticOutcome.Failure })
            {
                if (outcome == TacticOutcome.Failure && !segment.Branch[tactic].IsGamble) continue;
                var errors = PitchEventValidator.ValidateSequence(segment.Branch.PathFor(joined, tactic, outcome));
                Assert.That(errors, Is.Empty, $"{tactic} {outcome}\n" + string.Join("\n", errors));
            }
        }

        [Test]
        public void 스퀴즈는_3루_주자가_있고_2아웃_전일_때만_제공된다()
        {
            bool sawSqueeze = false, sawNoSqueeze = false;
            foreach (var s in FixtureGenerator.AllSituations())
            foreach (var outcome in new[] { TacticOutcome.Success, TacticOutcome.Failure })
            {
                // 강공 성공(단타: 3루 비움) / 실패(땅볼: 3루 주자 묶임)로 다음 타자 상황을 만든다.
                var f = FixtureGenerator.CreateTactics(s, 0, 0);
                var cut = PlayByPlayGenerator.UntilPlateAppearanceEnd(
                    f.Branch.PathFor(f.Events, TacticKind.SwingAway, outcome), f.Branch.AfterIndex + 1);
                var segment = new PlayByPlayGenerator(null, new ScriptedRandom(0.5)).Next(cut);
                var last = cut.Last();
                if (last.OutsAfter >= 3)
                {
                    Assert.That(segment, Is.Null, $"{s} {outcome}: 3아웃");
                    continue;
                }
                bool expected = last.RunnersAfter.Third != null && last.OutsAfter < 2;
                Assert.That(segment.Branch.Offers(TacticKind.SqueezeBunt), Is.EqualTo(expected), $"{s} {outcome}");
                if (expected) sawSqueeze = true; else sawNoSqueeze = true;
            }
            Assert.That(sawSqueeze && sawNoSqueeze, Is.True);
        }

        [Test]
        public void 모든_상황에서_무작위_작전으로_3아웃까지_순환하고_일관성_규칙을_지킨다()
        {
            int seed = 0;
            int maxPa = 0;
            foreach (var s in FixtureGenerator.AllSituations())
            foreach (bool mirrored in new[] { false, true })
            for (int k = 0; k < 10; k++, seed++)
            {
                var f = FixtureGenerator.CreateTactics(s, 2, 4);
                if (mirrored) f = MirrorForTest(f);
                var (path, decisions) = RunLoop(f, new SeededRandom(seed));

                var errors = PitchEventValidator.ValidateSequence(path);
                Assert.That(errors, Is.Empty, $"[{s} 뒤집기 {mirrored} seed {seed}]\n" + string.Join("\n", errors));

                var ends = PlateAppearanceEnds(path).ToList();
                Assert.That(ends.Count, Is.EqualTo(decisions), "타석마다 작전 한 번");
                bool capped = PlayByPlayGenerator.PlateAppearancesInHalf(path) >= PlayByPlayGenerator.MaxPlateAppearancesPerHalf;
                Assert.That(path.Last().OutsAfter == 3 || capped, Is.True, $"[{s} seed {seed}] 3아웃 또는 안전 한도로 끝나야 함");
                Assert.That(path.Count(e => e.OutsAfter == 3), Is.LessThanOrEqualTo(1), "3아웃 뒤 투구 없음");

                // 타순은 3번부터 한 칸씩 (9번 다음 1번)
                for (int i = 0; i < ends.Count; i++)
                    Assert.That(BattingOrder.OrderOf(ends[i].BatterId), Is.EqualTo((2 + i) % 9 + 1));
                // 같은 반 이닝 그대로 (8회말, 뒤집으면 9회초)
                Assert.That(path.Select(e => (e.Inning, e.HalfInning)).Distinct().Count(), Is.EqualTo(1));
                maxPa = System.Math.Max(maxPa, ends.Count);
            }
            Assert.That(maxPa, Is.GreaterThan(3), "주자가 나가 타순이 여러 번 도는 경우도 있어야 함");
        }

        [Test]
        public void 같은_Seed면_같은_순환_결과다()
        {
            var f = FixtureGenerator.CreateTactics(new ClutchSituation(0, FixtureGenerator.RunnersFor(7)), 1, 1);
            var a = RunLoop(f, new SeededRandom(42)).path;
            var b = RunLoop(f, new SeededRandom(42)).path;
            Assert.That(a.Select(Describe), Is.EqualTo(b.Select(Describe)));
        }

        [Test]
        public void 강공이_계속_실패하면_삼진으로_3아웃까지_가서_멈춘다()
        {
            // 0.99: 강공 실패 → 실패 결과표 끝(삼진)
            var f = FixtureGenerator.CreateTactics(new ClutchSituation(0, BaseState.Empty), 0, 0);
            var (path, decisions) = RunLoop(f, new ScriptedRandom(0.99), _ => TacticKind.SwingAway);
            Assert.That(decisions, Is.EqualTo(3), "무사 주자 없음: 땅볼 → 삼진 → 삼진");
            Assert.That(path.Last().OutsAfter, Is.EqualTo(3));
            Assert.That(path.Last().Outcome, Is.EqualTo(PlateAppearanceOutcome.Strikeout));
            Assert.That(new PlayByPlayGenerator(null, new ScriptedRandom(0.5)).Next(path), Is.Null, "3아웃이면 다음 타자 없음");
        }

        [Test]
        public void 아웃이_나오지_않아도_반_이닝_타석_수_한도에서_순환을_멈춘다()
        {
            // 0.0: 기본 진행은 항상 출루 → 결과표 첫 칸(볼넷). 밀어내기만 계속되어 3아웃이 오지 않는다.
            var f = FixtureGenerator.CreateTactics(new ClutchSituation(0, BaseState.Empty), 0, 0);
            var (path, decisions) = RunLoop(f, new ScriptedRandom(0.0), _ => TacticKind.Default);
            Assert.That(decisions, Is.EqualTo(PlayByPlayGenerator.MaxPlateAppearancesPerHalf));
            Assert.That(path.Last().OutsAfter, Is.Zero);
            Assert.That(PlayByPlayGenerator.HalfInningContinues(path), Is.False);
            Assert.That(PitchEventValidator.ValidateSequence(path), Is.Empty);
        }

        [Test]
        public void 타석이_끝나지_않았으면_다음_타자를_만들지_않는다()
        {
            var f = FixtureGenerator.CreateTactics(new ClutchSituation(1, BaseState.Empty), 0, 0);
            Assert.That(PlayByPlayGenerator.HalfInningContinues(f.Events), Is.False, "1-1 진행 중");
            Assert.That(new PlayByPlayGenerator(null, new SeededRandom(1)).Next(f.Events), Is.Null);
        }

        static string Describe(PitchEvent e) =>
            $"{e.BatterId}|{e.Result}|{e.PlayType}|{e.OutsAfter}|{e.RunnersAfter}|{e.RunsScored}";

        /// <summary>UI의 ScenarioMirror와 같은 규칙(8회말 → 9회초)으로 뒤집는다. Sim 테스트는 UI 어셈블리를 참조하지 않는다.</summary>
        static ScenarioFixture MirrorForTest(ScenarioFixture f)
        {
            PitchEvent M(PitchEvent e) => new PitchEvent
            {
                Inning = e.Inning + 1, HalfInning = HalfInning.Top, OutsBefore = e.OutsBefore, CountBefore = e.CountBefore,
                PitcherId = e.PitcherId, BatterId = e.BatterId, PitchType = e.PitchType, VelocityKmh = e.VelocityKmh,
                LocationX = e.LocationX, LocationY = e.LocationY, Zone = e.Zone, Result = e.Result,
                IsStrikeByRule = e.IsStrikeByRule, CountAfter = e.CountAfter, Outcome = e.Outcome, PlayResult = e.PlayResult,
                PlayType = e.PlayType, SprayAngleDeg = e.SprayAngleDeg, RunnersBefore = e.RunnersBefore,
                RunnersAfter = e.RunnersAfter, RunsScored = e.RunsScored, OutsAfter = e.OutsAfter,
            };
            return new ScenarioFixture
            {
                Id = f.Id, Events = f.Events.Select(M).ToList(),
                Branch = new BranchPoint
                {
                    AfterIndex = f.Branch.AfterIndex,
                    Options = f.Branch.Options.ToDictionary(kv => kv.Key, kv => new TacticBranch
                    {
                        SuccessRate = kv.Value.SuccessRate,
                        Success = () => kv.Value.Success().Select(M).ToList(),
                        Failure = kv.Value.Failure == null ? null
                            : (System.Func<IReadOnlyList<PitchEvent>>)(() => kv.Value.Failure().Select(M).ToList()),
                    }),
                },
            };
        }
    }
}
