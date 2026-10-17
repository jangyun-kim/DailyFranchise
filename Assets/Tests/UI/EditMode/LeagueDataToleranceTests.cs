using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 40.5: 10개 구단 실데이터(SampleRoster_Export.csv) 내성 검증. 삼성 외 9개 구단은 KBO 기록실 Basic2·Detail 화면이
    /// 빠져 볼넷·삼진·피안타율·GS 등이 추정값이고 RISP·완투 등은 빈 칸이다. 그래도 로딩·확률·퀵 시뮬·박스스코어가
    /// NaN·무한대·0으로 나누기 없이, 비정상(무조건 안타/무안타) 없이 돌아가야 한다.
    /// </summary>
    public class LeagueDataToleranceTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;

        static IEnumerable<TeamRoster> Rosters => Real.Teams.Select(t => Real.GetRoster(t.Name)).Where(r => r != null);

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        [Test]
        public void 열개_구단_모두_1군_라인업_9명과_선발_로테이션과_퓨처스가_나뉘어_적재된다()
        {
            var rosters = Rosters.ToList();
            Assert.That(rosters.Count, Is.EqualTo(10), "KBO 10개 구단");
            int players = rosters.Sum(r => LeagueRosterBook.AllOf(r).Count() + r.Reserves.Count);
            Assert.That(players, Is.InRange(550, 700), "약 600명");
            foreach (var r in rosters)
            {
                Assert.That(r.Lineup.Count, Is.EqualTo(9), r.Team);
                Assert.That(r.Lineup.Select(p => p.Order).OrderBy(o => o), Is.EqualTo(Enumerable.Range(1, 9)), r.Team + " 타순");
                Assert.That(r.Starters.Count, Is.InRange(1, 5), r.Team + " 선발");
                Assert.That(r.Bench.Count, Is.LessThanOrEqualTo(RosterLimits.MaxBench), r.Team + " 1군 벤치");
                Assert.That(r.Reserves, Is.Not.Empty, r.Team + " 퓨처스로 분리");
                Assert.That(LeagueRosterBook.AllOf(r).Concat(r.Reserves).Select(p => p.Id), Is.Unique, r.Team);
            }
        }

        [Test]
        public void 약_600명_CSV도_동기_로딩이_1초_안에_끝나_로비가_멈추지_않는다()
        {
            MasterDatabase.LoadFromResources(); // 첫 Resources 로드(에셋 임포트 캐시)는 빼고 잰다
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var db = MasterDatabase.LoadFromResources();
            watch.Stop();
            Assert.That(db.Players.Count, Is.GreaterThan(550));
            UnityEngine.Debug.Log($"[Phase40.5] MasterDatabase {db.Players.Count}명 로딩 {watch.ElapsedMilliseconds} ms");
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000), "비동기 로딩이 필요 없는 수준");
        }

        [Test]
        public void 모든_선수의_기록_값은_NaN이나_무한대가_아니고_비율은_0에서_1이다()
        {
            foreach (var p in Rosters.SelectMany(r => LeagueRosterBook.AllOf(r).Concat(r.Reserves)))
            {
                foreach (var (name, v) in new[]
                         {
                             ("AVG", p.BattingAverage), ("OBP", p.OnBasePercentage), ("OPS", p.Ops), ("ERA", p.Era),
                             ("BAA", p.OpponentAverage), ("WAR", p.War), ("WHIP", p.Whip), ("FIP", p.Fip),
                         })
                    Assert.That(Finite(v), Is.True, $"{p.Id} {name}={v}");
                Assert.That(p.BattingAverage, Is.InRange(0, 1), p.Id);
                Assert.That(p.OnBasePercentage, Is.InRange(0, 1), p.Id);
                Assert.That(p.OpponentAverage, Is.InRange(0, 1), p.Id);
                Assert.That(p.Era, Is.GreaterThanOrEqualTo(0), p.Id);
            }
        }

        [Test]
        public void 추정값이_들어간_타_구단_타자도_볼넷과_출루율이_있어_0으로_계산되지_않는다()
        {
            foreach (var r in Rosters.Where(r => r.Team != TestLeague.Samsung))
            foreach (var b in r.Lineup)
            {
                Assert.That(b.OnBasePercentage, Is.GreaterThan(b.BattingAverage), $"{r.Team} {b.Name}: 출루율 ≥ 타율 (볼넷 반영)");
                Assert.That(b.Ops, Is.GreaterThan(0.4), $"{r.Team} {b.Name}");
                Assert.That(b.Walks, Is.GreaterThan(0), $"{r.Team} {b.Name}: 볼넷 0이 아니다");
            }
        }

        [Test]
        public void 모든_선발_타자와_모든_구단_선발_투수의_대결_확률이_정상_범위다()
        {
            var batters = Rosters.SelectMany(r => r.Lineup).ToList();
            var pitchers = Rosters.SelectMany(r => r.Starters.Concat(r.Bullpen)).ToList();
            Assert.That(batters.Count, Is.EqualTo(90));
            foreach (var b in batters)
                Assert.That(MatchupCalculator.BatterHitRate(b), Is.InRange(0.15, 0.50), b.Id);
            foreach (var p in pitchers)
                Assert.That(MatchupCalculator.PitcherAllowedRate(p), Is.InRange(0.0, 0.60), p.Id);
            foreach (var b in batters)
            foreach (var p in pitchers)
            {
                double h = MatchupCalculator.Combine(b, p);
                Assert.That(Finite(h) && h > 0.05 && h < 0.6, Is.True, $"{b.Id} vs {p.Id}: {h}");
            }
        }

        [Test]
        public void 모든_구단_맞대결_퀵_시뮬과_박스스코어가_정상_범위로_나온다()
        {
            var rosters = Rosters.ToList();
            var runs = new Dictionary<string, List<int>>();
            var hits = new Dictionary<string, List<int>>();
            int seed = 4000;
            foreach (var away in rosters)
            foreach (var home in rosters)
            {
                if (away == home) continue;
                for (int g = 0; g < 4; g++)
                {
                    var game = FixtureGenerator.Generate(away, home, new SeededRandom(seed++));
                    Assert.That(game.Fixture.AwayScoreBefore, Is.InRange(0, 40));
                    PlayerData Resolve(string id) =>
                        id.StartsWith("home_b") ? home.AtOrder(int.Parse(id.Substring(6)))
                        : id.StartsWith("away_b") ? away.AtOrder(int.Parse(id.Substring(6)))
                        : id == "home_sp" ? home.StartingPitcher : id == "away_sp" ? away.StartingPitcher : null;
                    var box = BoxScoreBuilder.Build(game, null, Resolve, new SeededRandom(BoxScoreBuilder.SeedFor(game)));
                    Assert.That(box.Batting.Values.Sum(b => b.Hits), Is.EqualTo(game.AwayHits + game.HomeHits));
                    Assert.That(box.Pitching.Values.All(p => p.Outs >= 0 && p.EarnedRuns >= 0), Is.True);
                    Add(runs, away.Team, game.Early.AwayTotal);
                    Add(runs, home.Team, game.Early.HomeTotal);
                    Add(hits, away.Team, game.Early.AwayHits);
                    Add(hits, home.Team, game.Early.HomeHits);
                }
            }
            foreach (var team in runs.Keys)
            {
                double r = runs[team].Average(), h = hits[team].Average();
                // 7이닝 퀵 시뮬 구간 기준 (9이닝 환산 약 4~5점 · 9~10안타)
                Assert.That(r, Is.InRange(1.5, 6.0), $"{team} 7이닝 평균 득점 {r:0.00}");
                Assert.That(h, Is.InRange(4.5, 10.5), $"{team} 7이닝 평균 안타 {h:0.00}");
            }
        }

        static void Add(Dictionary<string, List<int>> d, string k, int v)
        {
            if (!d.TryGetValue(k, out var list)) d[k] = list = new List<int>();
            list.Add(v);
        }

        [Test]
        public void 시즌_성적_누적은_타_구단_박스스코어로도_0으로_나누지_않는다()
        {
            var stats = new SeasonStatsTracker();
            var rosters = Rosters.ToList();
            var away = rosters.First(r => r.Team != TestLeague.Samsung);
            var home = rosters.First(r => r.Team == TestLeague.Samsung);
            for (int i = 0; i < 20; i++)
            {
                var game = FixtureGenerator.Generate(away, home, new SeededRandom(77 + i));
                PlayerData Resolve(string id) => id.StartsWith("away_b") ? away.AtOrder(int.Parse(id.Substring(6)))
                    : id.StartsWith("home_b") ? home.AtOrder(int.Parse(id.Substring(6))) : null;
                var box = BoxScoreBuilder.Build(game, null, Resolve, new SeededRandom(i)).Remap(id => Resolve(id)?.Id);
                stats.Add(box, "5." + i, away.Team);
            }
            foreach (var b in stats.Batters.Values)
                Assert.That(Finite(b.Average) && Finite(b.Ops) && b.Average <= 1, Is.True, b.PlayerId);
            Assert.That(stats.Batters.Keys.Count(id => away.Lineup.Any(p => p.Id == id)), Is.EqualTo(9), "타 구단 타자도 누적");
        }
    }
}
