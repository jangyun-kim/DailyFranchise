using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    /// <summary>
    /// Phase 38: 퀵 시뮬레이션 투고타저 밸런스 — 잔루 안타 채우기(득점은 그대로)와 퀵 시뮬 구간 자동 투수 교체(가상 투구 수).
    /// </summary>
    public class QuickSimBalanceTests
    {
        const int Seed = 20261017;

        /// <summary>리그 평균 타선 · 선발 1명 · 불펜 <paramref name="relievers"/>명(마지막은 마무리)인 선수단.</summary>
        static TeamRoster Roster(string team, int relievers = 5)
        {
            var lineup = Enumerable.Range(1, 9).Select(i => new PlayerData
            {
                Id = $"{team}-b{i}", Name = $"{team} {i}", Role = RosterRole.Lineup, Order = i,
                Ops = QuickSimEngine.LeagueOps, OnBasePercentage = QuickSimEngine.LeagueOnBase, BattingAverage = 0.270,
            }).ToList();
            var starter = new PlayerData { Id = $"{team}-sp", Name = $"{team} 선발", Role = RosterRole.Starter, Order = 1, Era = MatchupCalculator.LeagueEra };
            var pen = Enumerable.Range(1, relievers).Select(i => new PlayerData
            {
                Id = $"{team}-rp{i}", Name = $"{team} 불펜 {i}", Role = RosterRole.Bullpen, Order = i,
                Position = i == relievers ? CpuRosterManager.CloserPosition : "셋업",
            }).ToList();
            return new TeamRoster(team, lineup, null, new[] { starter }, pen);
        }

        // ---------- 3-1. 안타 볼륨 ----------

        [Test]
        public void 잔루_안타는_난수_칸으로_정해진다_득점_이닝_0에서_3개_무득점_이닝_0에서_2개()
        {
            // 득점 이닝: [0, .10) 3개 · [.10, .35) 2개 · [.35, .70) 1개 · 나머지 0개
            Assert.That(QuickSimEngine.StrandedHits(1, 1.0, 0.05), Is.EqualTo(3));
            Assert.That(QuickSimEngine.StrandedHits(2, 1.0, 0.20), Is.EqualTo(2));
            Assert.That(QuickSimEngine.StrandedHits(1, 1.0, 0.50), Is.EqualTo(1));
            Assert.That(QuickSimEngine.StrandedHits(3, 1.0, 0.80), Is.EqualTo(0));
            // 무득점 이닝: [0, .09) 2개 · [.09, .35) 1개 · 나머지 0개 — 독립 확률로 35% 이닝에 안타가 나온다
            Assert.That(QuickSimEngine.StrandedHits(0, 1.0, 0.05), Is.EqualTo(2));
            Assert.That(QuickSimEngine.StrandedHits(0, 1.0, 0.20), Is.EqualTo(1));
            Assert.That(QuickSimEngine.StrandedHits(0, 1.0, 0.40), Is.EqualTo(0));
            // 강타선은 칸이 넓어지지만 95%를 넘지 않는다
            Assert.That(QuickSimEngine.StrandedHits(0, 1.2, 0.40), Is.EqualTo(1), "OPS 1.2배면 무득점 이닝 1안타 칸이 .42까지");
            Assert.That(QuickSimEngine.StrandedHits(1, 3.0, 0.96), Is.EqualTo(0));
            Assert.That(QuickSimEngine.StrandedHits(0, double.NaN, 0.0), Is.EqualTo(0), "비정상 OPS는 0배");
        }

        [Test]
        public void Mock_난수로_9이닝을_돌리면_팀당_안타가_8에서_10개_사이다()
        {
            // A = 1점 이닝: 득점(0.2) → 추가 득점 실패(0.9) → 잔루 안타 1개(0.5) → 볼넷·실책 실패 = 2안타
            // B = 무득점 안타 이닝: 득점 실패(0.9) → 잔루 안타 1개(0.2) → 볼넷·실책 실패 = 1안타
            // C = 삼자범퇴: 4번 모두 실패
            // 반 이닝 순서 A A B B C C … → 원정(1·3·5번째 …)과 홈이 모두 A, B, C를 차례로 받는다.
            double[] a = { 0.2, 0.9, 0.5, 0.9, 0.9 }, b = { 0.9, 0.2, 0.9, 0.9 }, c = { 0.9, 0.9, 0.9, 0.9 };
            var random = new ScriptedRandom(a.Concat(a).Concat(b).Concat(b).Concat(c).Concat(c).ToArray());
            var r = new QuickSimEngine().Simulate(null, null, random, 9);
            Assert.That(r.AwayHits, Is.EqualTo(9), "A·B·C × 3 = (2 + 1 + 0) × 3");
            Assert.That(r.HomeHits, Is.EqualTo(9));
            Assert.That(new[] { r.AwayHits, r.HomeHits }, Has.All.InRange(8, 10));
            Assert.That((r.AwayTotal, r.HomeTotal), Is.EqualTo((3, 3)), "득점은 A 이닝 1점씩");
            Assert.That(r.AwayHits, Is.GreaterThan(r.AwayTotal * 2), "안타가 득점보다 넉넉하다 (잔루)");
        }

        [Test]
        public void 실제_난수로_많이_돌리면_9이닝_팀당_안타는_평균_8에서_10개이고_무득점_이닝의_약_30퍼센트에_안타가_나온다()
        {
            var engine = new QuickSimEngine();
            var random = new SeededRandom(Seed);
            int games = 400, hits = 0, runs = 0, scoreless = 0, scorelessWithHit = 0;
            for (int g = 0; g < games; g++)
            {
                var r = engine.Simulate(null, null, random, 9);
                hits += r.AwayHits + r.HomeHits;
                runs += r.AwayTotal + r.HomeTotal;
                foreach (var h in r.AwayHalves.Concat(r.HomeHalves))
                {
                    Assert.That(h.Hits, Is.GreaterThanOrEqualTo(h.Runs), "안타는 득점 이상");
                    Assert.That(h.Hits + h.Walks + h.Errors - h.Runs, Is.LessThanOrEqualTo(QuickSimEngine.MaxLeftOnBase), "잔루 3명 이하");
                    if (h.Runs > 0) continue;
                    scoreless++;
                    if (h.Hits > 0) scorelessWithHit++;
                }
            }
            double perTeam = hits / (2.0 * games);
            double share = (double)scorelessWithHit / scoreless;
            Assert.That(perTeam, Is.InRange(8.0, 10.0), $"팀당 안타 {perTeam:0.00}");
            Assert.That(hits / (double)games, Is.InRange(16.0, 20.0), "양 팀 합산");
            Assert.That(share, Is.InRange(0.25, 0.42), $"무득점 이닝 안타 비율 {share:0.00}");
            Assert.That(runs / (2.0 * games), Is.InRange(3.5, 5.0), "팀당 득점은 Phase 37 수준 그대로");
        }

        [Test]
        public void 안타를_채워도_반_이닝당_난수_사용_횟수는_그대로라_득점_결과가_바뀌지_않는다()
        {
            // 득점 실패 이닝 = 득점 1 + 안타 1 + 볼넷 1 + 실책 1 = 4번, 1점 이닝 = 5번 (Phase 37과 같다)
            var random = new ScriptedRandom(0.99, 0.99, 0.99, 0.99, 0.0, 0.99, 0.99, 0.99, 0.99);
            var r = new QuickSimEngine().Simulate(null, null, random, 3);
            Assert.That(random.Draws, Is.EqualTo(27));
            Assert.That(r.AwayRuns, Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(r.HomeRuns, Is.EqualTo(new[] { 1, 1, 1 }));
        }

        // ---------- 3-2. 퀵 시뮬 자동 투수 교체 ----------

        static IEnumerable<HalfInningLine> Halves(int count, int hits = 1, int walks = 0) =>
            Enumerable.Repeat(new HalfInningLine(0, hits, walks, 0), count);

        [Test]
        public void 가상_투구_수는_이닝당_11구에_출루_하나마다_4구씩_더한다()
        {
            Assert.That(QuickSimStaff.PitchesFor(new HalfInningLine(0, 0, 0, 0)), Is.EqualTo(11), "삼자범퇴");
            Assert.That(QuickSimStaff.PitchesFor(new HalfInningLine(1, 2, 1, 0)), Is.EqualTo(23));
            Assert.That(QuickSimStaff.PitchesFor(new HalfInningLine(0, 1, 0, 0)), Is.InRange(15, 18), "리그 평균 이닝 ≈ 15~18구");
        }

        [Test]
        public void 투구_수가_적어도_선발은_6이닝을_채우면_내려가고_구원은_한_이닝씩_던진다()
        {
            var staff = QuickSimStaff.Pitch("away_sp", new[] { "away_rp1", "away_rp2", "away_rp3" }, Halves(8, hits: 0));
            Assert.That(staff.PitcherByHalf.Take(6), Has.All.EqualTo("away_sp"));
            Assert.That(staff.PitcherByHalf.Skip(6), Is.EqualTo(new[] { "away_rp1", "away_rp2" }));
            Assert.That(staff.Find("away_sp").Innings, Is.EqualTo(6));
            Assert.That(staff.Find("away_sp").Pitches, Is.EqualTo(66));
            Assert.That(staff.CurrentPitcherId, Is.EqualTo("away_rp3"), "마지막 구원도 한 이닝을 채워 다음 투수가 장면 첫 이닝 마운드");
        }

        [Test]
        public void 투구_수가_95구를_넘기면_6이닝_전이라도_선발이_내려간다()
        {
            // 출루 3명 이닝 = 23구 → 4이닝 92구, 5이닝 115구
            var staff = QuickSimStaff.Pitch("home_sp", new[] { "home_rp1", "home_rp2", "home_rp3", "home_rp4" }, Halves(8, hits: 2, walks: 1));
            Assert.That(staff.Find("home_sp").Innings, Is.EqualTo(5));
            Assert.That(staff.Find("home_sp").Pitches, Is.EqualTo(115));
            Assert.That(staff.Appearances.Select(a => a.PitcherId), Is.EqualTo(new[] { "home_sp", "home_rp1", "home_rp2", "home_rp3", "home_rp4" }));
        }

        [Test]
        public void 불펜이_바닥나면_마지막_투수가_한계를_넘겨도_끝까지_던진다()
        {
            var none = QuickSimStaff.Pitch("away_sp", null, Halves(8));
            Assert.That(none.PitcherByHalf, Has.All.EqualTo("away_sp"), "구원이 없으면 선발 완투");
            Assert.That(none.PastLimit, Is.True);
            Assert.That(none.Find("away_sp").Outs, Is.EqualTo(24));

            var one = QuickSimStaff.Pitch("away_sp", new[] { "away_rp1" }, Halves(8, hits: 0));
            Assert.That(one.PitcherByHalf.Skip(6), Has.All.EqualTo("away_rp1"), "하나뿐인 구원이 7·8회를 이어 던진다");
            Assert.That(one.CurrentPitcherId, Is.EqualTo("away_rp1"));
            Assert.That(one.PastLimit, Is.True);
            Assert.DoesNotThrow(() => QuickSimStaff.Pitch("away_sp", new string[0], Enumerable.Empty<HalfInningLine>()));
        }

        [Test]
        public void 퀵_시뮬_구원_순서는_불펜_대기_순서이고_마무리는_아껴_둔다()
        {
            var order = QuickSimStaff.ReliefOrder(Roster("한화", 4), "away_");
            Assert.That(order, Is.EqualTo(new[] { "away_rp1", "away_rp2", "away_rp3" }), "4번째(마무리) 제외");
            Assert.That(QuickSimStaff.ReliefOrder(null, "away_"), Is.Empty);

            // DB 보직이 모두 "P"면 시즌 세이브가 가장 많은 투수를 마무리로 본다
            var pen = Enumerable.Range(1, 4).Select(i => new PlayerData
            {
                Id = $"p{i}", Role = RosterRole.Bullpen, Order = i, Position = "P", Saves = i == 2 ? 30 : i == 3 ? 5 : 0,
            }).ToList();
            var db = new TeamRoster("삼성", null, null, null, pen);
            Assert.That(QuickSimStaff.CloserOf(db).Id, Is.EqualTo("p2"));
            Assert.That(QuickSimStaff.ReliefOrder(db, "home_"), Is.EqualTo(new[] { "home_rp1", "home_rp3", "home_rp4" }));
        }

        [Test]
        public void 생성_경기는_양_팀_모두_선발이_5에서_6이닝에서_내려가고_8회말_장면은_상대_구원이_던진다()
        {
            for (int i = 0; i < 40; i++)
            {
                var g = FixtureGenerator.Generate(Roster("원정"), Roster("홈"), new SeededRandom(Seed + i));
                foreach (var staff in new[] { g.OpponentStaff, g.MyStaff })
                {
                    Assert.That(staff.Appearances.Count, Is.GreaterThanOrEqualTo(2), $"경기 {i}: 투수 2명 이상 등판");
                    Assert.That(staff.Appearances[0].Innings, Is.LessThanOrEqualTo(6), "선발은 6이닝까지");
                    Assert.That(staff.Appearances[0].Innings, Is.GreaterThanOrEqualTo(3), "두 이닝 최대 86구라 3이닝은 던진다");
                    Assert.That(staff.Appearances.Sum(a => a.Outs), Is.EqualTo(3 * staff.PitcherByHalf.Count));
                }
                Assert.That(g.OpponentStaff.PitcherByHalf.Count, Is.EqualTo(FixtureGenerator.SimulatedInnings));
                Assert.That(g.MyStaff.PitcherByHalf.Count, Is.EqualTo(FixtureGenerator.SimulatedInnings + 1), "1~8회초");
                Assert.That(g.DecisionPitcher, Is.Not.EqualTo(FixtureGenerator.Pitcher), "8회말 장면 투수는 선발이 아니다");
                Assert.That(g.DecisionPitcher, Does.StartWith("away_rp"));
                Assert.That(g.Fixture.Events.Select(e => e.PitcherId), Has.All.EqualTo(g.DecisionPitcher));
                foreach (var kind in g.Fixture.Branch.Options.Keys)
                    Assert.That(g.Fixture.Branch.PathFor(g.Fixture.Events, kind).Select(e => e.PitcherId), Has.All.EqualTo(g.DecisionPitcher));
                Assert.That(g.MyStaff.CurrentPitcherId, Is.Not.EqualTo(FixtureGenerator.MyStarter), "9회초 마운드도 구원");
            }
        }

        [Test]
        public void 생성_경기의_득점은_투수_운용과_상관없이_선수단_불펜_유무에_관계없이_같다()
        {
            for (int i = 0; i < 20; i++)
            {
                var withPen = FixtureGenerator.Generate(Roster("원정"), Roster("홈"), new SeededRandom(Seed + i));
                var noPen = FixtureGenerator.Generate(Roster("원정", 0), Roster("홈", 0), new SeededRandom(Seed + i));
                Assert.That(withPen.AwayRunsByInning, Is.EqualTo(noPen.AwayRunsByInning));
                Assert.That(withPen.HomeRunsByInning, Is.EqualTo(noPen.HomeRunsByInning));
                Assert.That(withPen.Situation, Is.EqualTo(noPen.Situation));
                Assert.That(noPen.DecisionPitcher, Is.EqualTo(FixtureGenerator.Pitcher), "불펜이 없으면 선발이 끝까지 (Fallback)");
            }
        }

        [Test]
        public void 박스스코어는_퀵_시뮬_구간_투수_운용대로_아웃과_실점을_나누고_투수_등판_기록이_2명_이상_남는다()
        {
            for (int i = 0; i < 20; i++)
            {
                var g = FixtureGenerator.Generate(Roster("원정"), Roster("홈"), new SeededRandom(Seed + i));
                var box = BoxScoreBuilder.Build(g, null, null, new SeededRandom(BoxScoreBuilder.SeedFor(g)));
                var mine = box.Pitching.Where(kv => kv.Key.StartsWith("home_")).ToList();
                var theirs = box.Pitching.Where(kv => kv.Key.StartsWith("away_")).ToList();
                Assert.That(mine.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(theirs.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(mine.Sum(kv => kv.Value.Outs), Is.EqualTo(3 * (FixtureGenerator.SimulatedInnings + 1)));
                Assert.That(theirs.Sum(kv => kv.Value.Outs), Is.EqualTo(3 * FixtureGenerator.SimulatedInnings));
                Assert.That(mine.Sum(kv => kv.Value.RunsAllowed), Is.EqualTo(g.Fixture.AwayScoreBefore));
                foreach (var a in g.MyStaff.Appearances.Concat(g.OpponentStaff.Appearances).Where(x => x.Outs > 0))
                {
                    Assert.That(box.Pitching[a.PitcherId].Outs, Is.EqualTo(a.Outs), a.ToString());
                    Assert.That(box.Pitching[a.PitcherId].RunsAllowed, Is.EqualTo(a.RunsAllowed), a.ToString());
                }
                Assert.That(box.Pitching["home_sp"].GamesStarted, Is.EqualTo(1));
                Assert.That(box.Pitching["home_sp"].Outs, Is.LessThanOrEqualTo(QuickSimStaff.StarterMaxOuts));
            }
        }

        [Test]
        public void 퀵_시뮬_구간_박스스코어_팀_타율은_260에서_280_근처다()
        {
            int atBats = 0, hits = 0;
            for (int i = 0; i < 200; i++)
            {
                var g = FixtureGenerator.Generate(Roster("원정"), Roster("홈"), new SeededRandom(Seed + i));
                var box = BoxScoreBuilder.Build(g, null, null, new SeededRandom(BoxScoreBuilder.SeedFor(g)));
                atBats += box.Batting.Values.Sum(b => b.AtBats);
                hits += box.Batting.Values.Sum(b => b.Hits);
            }
            double avg = (double)hits / atBats;
            Assert.That(avg, Is.InRange(0.250, 0.285), $"팀 타율 {avg:0.000}");
        }
    }
}
