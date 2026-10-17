using System.Linq;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>퀵 시뮬레이션 경기의 스코어보드·장면 주입 검증.</summary>
    public class GeneratedScenarioTests
    {
        const int Seed = 20261014;
        static readonly MasterDatabase Db = MasterDatabase.LoadFromResources();
        static TeamRoster Busan => Db.GetRoster("삼성 라이온즈");
        static TeamRoster Seoul => Db.GetRoster("LG 트윈스");

        static GeneratedGame Generate(int seed = Seed) => FixtureGenerator.Generate(Seoul, Busan, new SeededRandom(seed));

        [Test]
        public void 스코어보드_기초값은_생성된_이닝별_득점과_H_E_B다()
        {
            var game = Generate();
            var data = new DummyScenarioData(game, Matchup.FromNames("LG 트윈스", "삼성 라이온즈", TeamSide.Home));
            var away = data.GetBoxSeed(TeamSide.Away);
            var home = data.GetBoxSeed(TeamSide.Home);

            Assert.That(away.RunsByInning, Is.EqualTo(game.AwayRunsByInning));
            Assert.That(home.RunsByInning, Is.EqualTo(game.HomeRunsByInning));
            Assert.That((away.Hits, away.Errors, away.Walks), Is.EqualTo((game.AwayHits, game.AwayErrors, game.AwayWalks)));
            Assert.That((home.Hits, home.Errors, home.Walks), Is.EqualTo((game.HomeHits, game.HomeErrors, game.HomeWalks)));
            Assert.That(away.RunsByInning.Sum(), Is.EqualTo(game.Fixture.AwayScoreBefore));
            Assert.That(home.RunsByInning.Sum(), Is.EqualTo(game.Fixture.HomeScoreBefore));
            // Phase 40.5: 상대(LG)도 실제 불펜이 있어 선발은 퀵 시뮬 구간에서 강판한다 (Phase 38 투수 운용)
            var starter = game.OpponentStaff.Appearances[0];
            Assert.That(data.GetPlayer(Fixture8thBottom.Pitcher).TodayLine,
                Is.EqualTo($"오늘 {starter.Innings}이닝 · {starter.RunsAllowed}실점"), "상대 선발의 퀵 시뮬 구간 기록");
            Assert.That(game.OpponentStaff.Appearances.Sum(a => a.RunsAllowed), Is.EqualTo(game.Fixture.HomeScoreBefore),
                "상대 투수진 실점 합 = 내 구단 득점");
        }

        [Test]
        public void 원정_경기는_스코어보드와_장면을_뒤집어도_시작_점수가_맞는다()
        {
            var game = Generate();
            var data = new DummyScenarioData(game, Matchup.FromNames("삼성 라이온즈", "SSG 랜더스", TeamSide.Away));
            Assert.That(data.Mirrored, Is.True);
            Assert.That(data.GetBoxSeed(TeamSide.Away).RunsByInning, Is.EqualTo(game.HomeRunsByInning), "내 구단은 원정 쪽");
            Assert.That(data.GetBoxSeed(TeamSide.Home).RunsByInning, Is.EqualTo(game.AwayRunsByInning));

            var mirrored = ScenarioMirror.Mirror(game.Fixture);
            Assert.That(data.GetBoxSeed(TeamSide.Away).RunsByInning.Sum(), Is.EqualTo(mirrored.AwayScoreBefore));
            Assert.That(data.GetBoxSeed(TeamSide.Home).RunsByInning.Sum(), Is.EqualTo(mirrored.HomeScoreBefore));
            Assert.That(mirrored.Events[0].HalfInning, Is.EqualTo(HalfInning.Top), "9회초 원정(내 구단) 공격");
            Assert.That(mirrored.Events[0].Inning, Is.EqualTo(9));
        }

        [Test]
        public void 뒤집은_생성_장면도_모든_상황_모든_경로가_일관성_검사를_통과한다()
        {
            foreach (var s in FixtureGenerator.AllSituations())
            {
                var f = ScenarioMirror.Mirror(FixtureGenerator.CreateTactics(s, 2, 4));
                foreach (var tactic in f.Branch.Tactics)
                foreach (var outcome in new[] { TacticOutcome.Success, TacticOutcome.Failure })
                {
                    if (outcome == TacticOutcome.Failure && !f.Branch[tactic].IsGamble) continue;
                    var errors = PitchEventValidator.ValidateSequence(f.Branch.PathFor(f.Events, tactic, outcome));
                    Assert.That(errors, Is.Empty, $"{s} {tactic} {outcome}\n" + string.Join("\n", errors));
                }
            }
        }

        [Test]
        public void 선수단을_입혀도_생성된_스코어보드를_그대로_쓰고_주자는_DB_타자로_표시된다()
        {
            var game = Generate();
            var dummy = new DummyScenarioData(game, Matchup.FromNames("LG 트윈스", "삼성 라이온즈", TeamSide.Home, Seoul, Busan));
            var source = new ScenarioRosterSource(dummy, "SampleRoster.csv");
            Assert.That(source.GetBoxSeed(TeamSide.Home), Is.SameAs(dummy.GetBoxSeed(TeamSide.Home)));
            Assert.That(source.GetBoxSeed(TeamSide.Away), Is.SameAs(dummy.GetBoxSeed(TeamSide.Away)));

            // 만루라면 2번 → 1루, 1번 → 2루, 9번 → 3루
            var full = FixtureGenerator.RunnersFor(7);
            Assert.That(source.GetPlayer(full.First).Name, Is.EqualTo(Busan.AtOrder(2).Name));
            Assert.That(source.GetPlayer(full.Second).Name, Is.EqualTo(Busan.AtOrder(1).Name));
            Assert.That(source.GetPlayer(full.Third).Name, Is.EqualTo(Busan.AtOrder(9).Name));
        }

        [Test]
        public void DB_선수단으로_만든_경기도_같은_Seed면_같고_득점은_상한_안이다()
        {
            for (int i = 0; i < 50; i++)
            {
                var a = Generate(Seed + i);
                var b = Generate(Seed + i);
                Assert.That(a.AwayRunsByInning, Is.EqualTo(b.AwayRunsByInning));
                Assert.That(a.HomeRunsByInning, Is.EqualTo(b.HomeRunsByInning));
                Assert.That(a.Situation, Is.EqualTo(b.Situation));
                Assert.That(a.AwayRunsByInning.Concat(a.HomeRunsByInning), Has.All.InRange(0, QuickSimEngine.DefaultMaxRunsPerInning));
            }
        }
    }
}
