using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    public class QuickSimEngineTests
    {
        const int Seed = 20261014;

        /// <summary>타자 9명이 모두 같은 OPS·출루율이고 선발 평균자책이 <paramref name="era"/>인 선수단.</summary>
        static TeamRoster Roster(string team, double ops, double era = MatchupCalculator.LeagueEra, double obp = 0.345)
        {
            var lineup = Enumerable.Range(1, 9).Select(i => new PlayerData
            {
                Id = $"{team}-b{i}", Name = $"{team} {i}", Role = RosterRole.Lineup, Order = i,
                Ops = ops, OnBasePercentage = obp, BattingAverage = 0.270,
            }).ToList();
            var starter = new PlayerData { Id = $"{team}-sp", Name = $"{team} 선발", Role = RosterRole.Starter, Order = 1, Era = era };
            return new TeamRoster(team, lineup, null, new[] { starter }, null);
        }

        static string Fingerprint(GeneratedGame g) =>
            $"{string.Join(",", g.AwayRunsByInning)}|{string.Join(",", g.HomeRunsByInning)}|{g.AwayHits}-{g.HomeHits}|" +
            $"{g.AwayWalks}-{g.HomeWalks}|{g.AwayErrors}-{g.HomeErrors}|{g.Situation}";

        [Test]
        public void 같은_Seed면_같은_스코어보드와_승부처_상황이_나온다()
        {
            var away = Roster("원정", 0.760, 3.80);
            var home = Roster("홈", 0.720, 4.40);

            var a = FixtureGenerator.Generate(away, home, new SeededRandom(Seed));
            var b = FixtureGenerator.Generate(away, home, new SeededRandom(Seed));

            Assert.That(a.AwayRunsByInning, Is.EqualTo(b.AwayRunsByInning));
            Assert.That(a.HomeRunsByInning, Is.EqualTo(b.HomeRunsByInning));
            Assert.That(a.Situation, Is.EqualTo(b.Situation));
            Assert.That(a.ScoreBefore, Is.EqualTo(b.ScoreBefore));
            Assert.That(Fingerprint(a), Is.EqualTo(Fingerprint(b)));
            Assert.That(a.Fixture.Events.Select(e => (e.OutsBefore, e.RunnersBefore)),
                Is.EqualTo(b.Fixture.Events.Select(e => (e.OutsBefore, e.RunnersBefore))));
        }

        [Test]
        public void Seed가_다르면_경기마다_다른_스코어보드와_상황이_나온다()
        {
            var away = Roster("원정", 0.740);
            var home = Roster("홈", 0.740);
            var games = Enumerable.Range(0, 20).Select(i => FixtureGenerator.Generate(away, home, new SeededRandom(Seed + i))).ToList();

            Assert.That(games.Select(Fingerprint).Distinct().Count(), Is.GreaterThan(10), "20경기 중 대부분이 서로 달라야 함");
            Assert.That(games.Select(g => g.Situation.Outs).Distinct().Count(), Is.EqualTo(3), "아웃 0~2가 모두 나옴");
            Assert.That(games.Select(g => g.Situation.Runners.ToString()).Distinct().Count(), Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void 이어_쓰는_난수로_연속_생성해도_결과가_재현된다()
        {
            var r1 = new SeededRandom(Seed);
            var r2 = new SeededRandom(Seed);
            for (int i = 0; i < 5; i++)
                Assert.That(Fingerprint(FixtureGenerator.Generate(null, null, r1)),
                    Is.EqualTo(Fingerprint(FixtureGenerator.Generate(null, null, r2))), $"{i + 1}번째 경기");
        }

        [Test]
        public void 타선이_강하면_득점_확률이_높고_시즌_평균_득점도_많다()
        {
            var strong = Roster("강타선", 0.880);
            var weak = Roster("약타선", 0.640);
            var pitcher = Roster("상대", 0.740);

            double pStrong = QuickSimEngine.ScoringProbability(QuickSimEngine.TeamOps(strong), QuickSimEngine.StarterEra(pitcher));
            double pWeak = QuickSimEngine.ScoringProbability(QuickSimEngine.TeamOps(weak), QuickSimEngine.StarterEra(pitcher));
            Assert.That(pStrong, Is.GreaterThan(pWeak));
            Assert.That(QuickSimEngine.ExtraRunProbability(0.880), Is.GreaterThan(QuickSimEngine.ExtraRunProbability(0.640)));

            // 같은 상대·같은 Seed로 200경기씩 돌린 7이닝 평균 득점
            var engine = new QuickSimEngine();
            double Average(TeamRoster offense)
            {
                var random = new SeededRandom(Seed);
                return Enumerable.Range(0, 200).Average(_ => engine.Simulate(offense, pitcher, random).AwayTotal);
            }
            double strongRuns = Average(strong);
            double weakRuns = Average(weak);
            Assert.That(strongRuns, Is.GreaterThan(weakRuns + 0.5), $"강타선 {strongRuns:0.00} vs 약타선 {weakRuns:0.00}");
        }

        [Test]
        public void 상대_선발_평균자책이_낮을수록_득점_확률이_낮다()
        {
            double ace = QuickSimEngine.ScoringProbability(QuickSimEngine.LeagueOps, 2.10);
            double league = QuickSimEngine.ScoringProbability(QuickSimEngine.LeagueOps, QuickSimEngine.LeagueEra);
            double bad = QuickSimEngine.ScoringProbability(QuickSimEngine.LeagueOps, 6.20);
            Assert.That(ace, Is.LessThan(league));
            Assert.That(league, Is.LessThan(bad));
            Assert.That(league, Is.EqualTo(QuickSimEngine.BaseScoringRate).Within(1e-9), "리그 평균 대결 = 기본 득점률");
        }

        [Test]
        public void 득점_확률은_상하한으로_잘린다()
        {
            Assert.That(QuickSimEngine.ScoringProbability(2.0, QuickSimEngine.MaxEra), Is.EqualTo(QuickSimEngine.MaxScoringRate));
            Assert.That(QuickSimEngine.ScoringProbability(0.2, QuickSimEngine.MinEra), Is.EqualTo(QuickSimEngine.MinScoringRate));
            Assert.That(QuickSimEngine.ScoringProbability(double.NaN, 4.0), Is.EqualTo(QuickSimEngine.MinScoringRate));
        }

        [TestCase(QuickSimEngine.DefaultMaxRunsPerInning)]
        [TestCase(3)]
        public void 매번_득점하는_난수여도_한_이닝_득점은_상한을_넘지_않는다(int max)
        {
            var engine = new QuickSimEngine(max);
            var result = engine.Simulate(Roster("원정", 1.200, obp: 0.500), Roster("홈", 1.200, 9.0), new ScriptedRandom(0.0));
            Assert.That(result.AwayRuns, Has.All.EqualTo(max));
            Assert.That(result.HomeRuns, Has.All.EqualTo(max));
            Assert.That(result.AwayTotal, Is.EqualTo(max * 7));
        }

        [Test]
        public void 실제_난수로_많이_돌려도_럭비_스코어가_나오지_않는다()
        {
            var engine = new QuickSimEngine();
            var random = new SeededRandom(Seed);
            var slugger = Roster("강타선", 1.100, 7.5);
            for (int i = 0; i < 300; i++)
            {
                var r = engine.Simulate(slugger, slugger, random);
                Assert.That(r.AwayRuns.Concat(r.HomeRuns), Has.All.InRange(0, QuickSimEngine.DefaultMaxRunsPerInning));
            }
        }

        [Test]
        public void 항상_실패하는_난수면_무득점_무안타()
        {
            var r = new QuickSimEngine().Simulate(null, null, new ScriptedRandom(0.999));
            Assert.That(r.AwayTotal + r.HomeTotal, Is.EqualTo(0));
            Assert.That(r.AwayHits + r.HomeHits + r.AwayWalks + r.HomeWalks + r.AwayErrors + r.HomeErrors, Is.EqualTo(0));
            Assert.That(r.Innings, Is.EqualTo(7));
        }

        [Test]
        public void 선수단이나_기록이_없으면_리그_평균으로_계산한다()
        {
            Assert.That(QuickSimEngine.TeamOps(null), Is.EqualTo(QuickSimEngine.LeagueOps));
            Assert.That(QuickSimEngine.StarterEra(null), Is.EqualTo(QuickSimEngine.LeagueEra));
            var noStats = new TeamRoster("빈", new[] { new PlayerData { Id = "x", Role = RosterRole.Lineup, Order = 1 } }, null, null, null);
            Assert.That(QuickSimEngine.TeamOps(noStats), Is.EqualTo(QuickSimEngine.LeagueOps));
            Assert.That(QuickSimEngine.TeamOnBase(noStats), Is.EqualTo(QuickSimEngine.LeagueOnBase));
            Assert.That(QuickSimEngine.StarterEra(noStats), Is.EqualTo(QuickSimEngine.LeagueEra));
            Assert.That(QuickSimEngine.StarterEra(Roster("a", 0.7, 0.5)), Is.EqualTo(QuickSimEngine.MinEra), "평균자책 하한");
            Assert.That(QuickSimEngine.StarterEra(Roster("a", 0.7, 40)), Is.EqualTo(QuickSimEngine.MaxEra), "평균자책 상한");
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuickSimEngine(0));
            Assert.Throws<ArgumentNullException>(() => new QuickSimEngine().SimulateHalf(null, null, null));
        }

        [Test]
        public void 반_이닝_순서는_1회초부터_원정_홈_번갈아()
        {
            // 원정 반 이닝은 5번 꺼냄(득점 실패 1 + 안타·볼넷·실책 3 = 4번), 홈은 매 이닝 1점씩 나도록 순서를 맞춘 난수.
            // 원정: 득점 실패(0.99) → 안타(0.99) → 볼넷(0.99) → 실책(0.99)
            // 홈:   득점(0.0) → 추가 득점 실패(0.99) → 안타(0.99) → 볼넷(0.99) → 실책(0.99)
            var random = new ScriptedRandom(0.99, 0.99, 0.99, 0.99, 0.0, 0.99, 0.99, 0.99, 0.99);
            var r = new QuickSimEngine().Simulate(null, null, random, 3);
            Assert.That(r.AwayRuns, Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(r.HomeRuns, Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(r.HomeHits, Is.EqualTo(3), "득점한 이닝은 득점 수만큼 안타");
            Assert.That(random.Draws, Is.EqualTo(27));
        }

        // ── 승부처 생성 ──

        [Test]
        public void 시작_점수는_이닝별_득점_합이고_8회말은_0으로_비어_있다()
        {
            for (int i = 0; i < 30; i++)
            {
                var g = FixtureGenerator.Generate(Roster("원정", 0.80), Roster("홈", 0.70), new SeededRandom(Seed + i));
                Assert.That(g.AwayRunsByInning.Length, Is.EqualTo(8), "1~7회 + 8회초");
                Assert.That(g.HomeRunsByInning.Length, Is.EqualTo(8));
                Assert.That(g.HomeRunsByInning[7], Is.EqualTo(0), "8회말은 장면에서 진행");
                Assert.That(g.Fixture.AwayScoreBefore, Is.EqualTo(g.AwayRunsByInning.Sum()));
                Assert.That(g.Fixture.HomeScoreBefore, Is.EqualTo(g.HomeRunsByInning.Sum()));
                Assert.That(g.Early.AwayRuns, Is.EqualTo(g.AwayRunsByInning.Take(7)));
                Assert.That(g.Fixture.Id, Is.EqualTo(FixtureGenerator.ScenarioId));
            }
        }

        [Test]
        public void 승부처_상황은_난수로_아웃과_주자_8가지를_고른다()
        {
            // Next(3) = floor(0.5 × 3) = 1사, Next(8) = floor(0.7 × 8) = 5 = 1·3루
            var s = FixtureGenerator.RollSituation(new ScriptedRandom(0.5, 0.7));
            Assert.That(s.Outs, Is.EqualTo(1));
            Assert.That(s.Runners.ToString(), Is.EqualTo("1·3루"));

            Assert.That(FixtureGenerator.RollSituation(new ScriptedRandom(0.0)).ToString(), Is.EqualTo("무사 주자 없음"));
            Assert.That(FixtureGenerator.RollSituation(new ScriptedRandom(0.99)).ToString(), Is.EqualTo("2사 만루"));

            var labels = Enumerable.Range(0, 8).Select(m => FixtureGenerator.RunnersFor(m).ToString()).ToArray();
            Assert.That(labels, Is.EqualTo(new[] { "주자 없음", "1루", "2루", "1·2루", "3루", "1·3루", "2·3루", "만루" }));
            Assert.That(FixtureGenerator.AllSituations().Distinct().Count(), Is.EqualTo(24));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixtureGenerator.RunnersFor(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClutchSituation(3, BaseState.Empty));
        }

        [Test]
        public void 주자는_타순상_직전_타자부터_낮은_루에_채운다()
        {
            var full = FixtureGenerator.RunnersFor(7);
            Assert.That((full.First, full.Second, full.Third), Is.EqualTo(("home_b2", "home_b1", "home_b9")));
            var corners = FixtureGenerator.RunnersFor(5);
            Assert.That((corners.First, corners.Second, corners.Third), Is.EqualTo(("home_b2", (string)null, "home_b1")));
            foreach (var s in FixtureGenerator.AllSituations())
            {
                var ids = new[] { s.Runners.First, s.Runners.Second, s.Runners.Third }.Where(id => id != null).ToList();
                Assert.That(ids, Has.None.EqualTo(FixtureGenerator.Batter1), "타자는 루상에 없다");
                Assert.That(ids, Is.Unique);
            }
        }

        static IEnumerable<TestCaseData> AllPaths()
        {
            foreach (var s in FixtureGenerator.AllSituations())
            {
                var f = FixtureGenerator.CreateTactics(s, 3, 2);
                foreach (var tactic in f.Branch.Tactics)
                {
                    yield return new TestCaseData(s.Outs, RunnerMask(s.Runners), tactic, TacticOutcome.Success)
                        .SetName($"{s} {tactic} 성공");
                    if (f.Branch[tactic].IsGamble)
                        yield return new TestCaseData(s.Outs, RunnerMask(s.Runners), tactic, TacticOutcome.Failure)
                            .SetName($"{s} {tactic} 실패");
                }
            }
        }

        static int RunnerMask(BaseState r) => (r.First != null ? 1 : 0) | (r.Second != null ? 2 : 0) | (r.Third != null ? 4 : 0);

        [TestCaseSource(nameof(AllPaths))]
        public void 모든_상황_모든_작전_경로가_일관성_검사를_통과(int outs, int mask, TacticKind tactic, TacticOutcome outcome)
        {
            var s = new ClutchSituation(outs, FixtureGenerator.RunnersFor(mask));
            var f = FixtureGenerator.CreateTactics(s, 3, 2);
            var path = f.Branch.PathFor(f.Events, tactic, outcome);
            var errors = PitchEventValidator.ValidateSequence(path);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));

            Assert.That(path[0].OutsBefore, Is.EqualTo(outs));
            Assert.That(path[0].RunnersBefore, Is.EqualTo(s.Runners));
            Assert.That(path.All(e => e.Inning == 8 && e.HalfInning == HalfInning.Bottom), Is.True, "장면은 8회말 안에서 끝난다");
            Assert.That(path.Last().OutsAfter, Is.LessThanOrEqualTo(3));
        }

        [Test]
        public void 기본_구간은_승부처_상황에서_1_1로_멈춘다()
        {
            foreach (var s in FixtureGenerator.AllSituations())
            {
                var f = FixtureGenerator.CreateTactics(s, 0, 0);
                Assert.That(f.Events.Count, Is.EqualTo(f.Branch.AfterIndex + 1));
                var last = f.Events.Last();
                Assert.That(last.CountAfter, Is.EqualTo(new Count(1, 1)));
                Assert.That(last.OutsAfter, Is.EqualTo(s.Outs));
                Assert.That(last.RunnersAfter, Is.EqualTo(s.Runners));
            }
        }

        [Test]
        public void 스퀴즈는_3루_주자가_있고_2사_전일_때만_고를_수_있다()
        {
            foreach (var s in FixtureGenerator.AllSituations())
            {
                var branch = FixtureGenerator.CreateTactics(s, 0, 0).Branch;
                bool expected = s.Runners.Third != null && s.Outs < 2;
                Assert.That(branch.Offers(TacticKind.SqueezeBunt), Is.EqualTo(expected), s.ToString());
                Assert.That(branch.Offers(TacticKind.Default) && branch.Offers(TacticKind.SwingAway), Is.True);
            }
            Assert.Throws<ArgumentException>(() =>
                FixtureGenerator.SqueezeContinuation(new ClutchSituation(2, FixtureGenerator.RunnersFor(4))));
        }

        [Test]
        public void 적시타_득점은_2루와_3루_주자_수와_같다()
        {
            foreach (var s in FixtureGenerator.AllSituations())
            {
                var hit = FixtureGenerator.HitContinuation(s).Single(e => e.PlayType == PlayType.Single);
                int expected = (s.Runners.Second != null ? 1 : 0) + (s.Runners.Third != null ? 1 : 0);
                Assert.That(hit.RunsScored, Is.EqualTo(expected), s.ToString());
            }
        }

        [Test]
        public void 생성된_1사_1_3루_장면은_기존_작전_승부처와_같은_결과를_낸다()
        {
            var s = new ClutchSituation(1, FixtureGenerator.RunnersFor(5));
            var generated = FixtureGenerator.CreateTactics(s, 3, 2);
            var fixed8th = Fixture8thBottomTactics.Create();
            foreach (var tactic in fixed8th.Branch.Tactics)
            foreach (var outcome in new[] { TacticOutcome.Success, TacticOutcome.Failure })
            {
                if (outcome == TacticOutcome.Failure && !fixed8th.Branch[tactic].IsGamble) continue;
                var a = generated.Branch.PathFor(generated.Events, tactic, outcome);
                var b = fixed8th.Branch.PathFor(fixed8th.Events, tactic, outcome);
                Assert.That(a.Sum(e => e.RunsScored), Is.EqualTo(b.Sum(e => e.RunsScored)), $"{tactic} {outcome} 득점");
                Assert.That(a.Last().OutsAfter, Is.EqualTo(b.Last().OutsAfter), $"{tactic} {outcome} 아웃");
                Assert.That(generated.Branch[tactic].SuccessRate, Is.EqualTo(fixed8th.Branch[tactic].SuccessRate));
            }
        }
    }
}
