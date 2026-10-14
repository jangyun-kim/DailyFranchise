using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>일정표 대진 주입과 원정 경기용 장면 뒤집기 검증.</summary>
    public class MatchupMirrorTests
    {
        static readonly string[] Ids = ScenarioCatalog.All.Select(e => e.Id).ToArray();

        /// <summary>일정 2경기와 같은 원정 경기: 부산(내 구단, 원정) vs 인천(홈).</summary>
        static Matchup AwayGame => Matchup.FromNames("부산 웨이브스", "인천 하버스", TeamSide.Away);

        /// <summary>장면의 모든 재생 경로 (일직선 장면은 1개, 분기 장면은 작전 × 성공/실패).</summary>
        static IEnumerable<(string name, IReadOnlyList<PitchEvent> events)> Paths(ScenarioFixture f)
        {
            if (f.Branch == null)
            {
                yield return ("일직선", f.Events);
                yield break;
            }
            foreach (var tactic in f.Branch.Tactics)
            {
                yield return ($"{tactic} 성공", f.Branch.PathFor(f.Events, tactic, TacticOutcome.Success));
                if (f.Branch[tactic].IsGamble)
                    yield return ($"{tactic} 실패", f.Branch.PathFor(f.Events, tactic, TacticOutcome.Failure));
            }
        }

        [Test]
        public void 대진_약칭은_연고지이고_기본_대진은_서울_원정_부산_홈()
        {
            var m = Matchup.FromNames("대구 썬더스", "부산 웨이브스", TeamSide.Home);
            Assert.That(m.Away.ShortName, Is.EqualTo("대구"));
            Assert.That(m.Home.Name, Is.EqualTo("부산 웨이브스"));
            Assert.That(Matchup.Default.Away.Name, Is.EqualTo("서울 블루윙스"));
            Assert.That(Matchup.Default.Home.Name, Is.EqualTo("부산 웨이브스"));
            Assert.That(Matchup.Default.MySide, Is.EqualTo(TeamSide.Home));
            Assert.Throws<System.ArgumentException>(() => Matchup.FromNames("", "부산 웨이브스", TeamSide.Home));
        }

        [Test]
        public void 홈_경기_대진은_팀명만_바꾸고_뒤집지_않는다()
        {
            var data = new DummyScenarioData(ScenarioCatalog.EighthBottomTactics,
                Matchup.FromNames("대구 썬더스", "부산 웨이브스", TeamSide.Home));
            Assert.That(data.Mirrored, Is.False);
            Assert.That(data.MyTeam, Is.EqualTo(TeamSide.Home));
            Assert.That(data.GetTeam(TeamSide.Away).Name, Is.EqualTo("대구 썬더스"));
            Assert.That(data.GetPlayer(Fixture8thBottom.Pitcher).Side, Is.EqualTo(TeamSide.Away));
            Assert.That(data.GetLineup(TeamSide.Home).Count, Is.EqualTo(9));
        }

        [Test]
        public void 원정_경기_대진은_선수_소속_라인업_스코어보드를_뒤집는다()
        {
            var native = new DummyScenarioData(ScenarioCatalog.EighthBottomTactics);
            var data = new DummyScenarioData(ScenarioCatalog.EighthBottomTactics, AwayGame);

            Assert.That(data.Mirrored, Is.True);
            Assert.That(data.MyTeam, Is.EqualTo(TeamSide.Away));
            Assert.That(data.GetTeam(TeamSide.Away).Name, Is.EqualTo("부산 웨이브스"));
            Assert.That(data.GetTeam(TeamSide.Home).Name, Is.EqualTo("인천 하버스"));

            Assert.That(data.GetLineup(TeamSide.Away).Select(r => r.PlayerId),
                Is.EqualTo(native.GetLineup(TeamSide.Home).Select(r => r.PlayerId)), "내 선수단은 원정 쪽");
            Assert.That(data.GetLineup(TeamSide.Home), Is.Empty);
            Assert.That(data.GetBench(TeamSide.Away).Count, Is.EqualTo(3));
            Assert.That(data.GetBullpen(TeamSide.Away).Count, Is.EqualTo(3));

            Assert.That(data.GetPlayer(Fixture8thBottom.Batter1).Side, Is.EqualTo(TeamSide.Away));
            Assert.That(data.GetPlayer(Fixture8thBottom.Pitcher).Side, Is.EqualTo(TeamSide.Home));

            Assert.That(data.GetBoxSeed(TeamSide.Away), Is.SameAs(native.GetBoxSeed(TeamSide.Home)));
            Assert.That(data.GetBoxSeed(TeamSide.Home), Is.SameAs(native.GetBoxSeed(TeamSide.Away)));
        }

        [Test]
        public void 뒤집은_투구는_반_이닝과_원정_홈만_바뀌고_나머지는_같다()
        {
            var original = Fixture8thBottomTactics.Create();
            var mirrored = ScenarioMirror.Mirror(original);

            Assert.That(mirrored.AwayScoreBefore, Is.EqualTo(original.HomeScoreBefore));
            Assert.That(mirrored.HomeScoreBefore, Is.EqualTo(original.AwayScoreBefore));
            Assert.That(mirrored.Events.All(e => e.Inning == 9 && e.HalfInning == HalfInning.Top), Is.True, "8회말 → 9회초");

            var fields = typeof(PitchEvent).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.Name != nameof(PitchEvent.Inning) && f.Name != nameof(PitchEvent.HalfInning))
                .ToArray();
            Assert.That(fields.Length, Is.GreaterThan(15));
            for (int i = 0; i < original.Events.Count; i++)
                foreach (var f in fields)
                    Assert.That(f.GetValue(mirrored.Events[i]), Is.EqualTo(f.GetValue(original.Events[i])), $"{i}구 {f.Name}");

            var top = ScenarioMirror.Mirror(new PitchEvent { Inning = 3, HalfInning = HalfInning.Top });
            Assert.That((top.Inning, top.HalfInning), Is.EqualTo((3, HalfInning.Bottom)), "N회초 → N회말");
        }

        [TestCaseSource(nameof(Ids))]
        public void 뒤집은_장면의_모든_경로가_투구_검증을_통과하고_원정이_같은_점수를_낸다(string id)
        {
            var original = ScenarioCatalog.Find(id).Create();
            var mirrored = ScenarioMirror.Mirror(original);
            var originalPaths = Paths(original).ToList();
            var mirroredPaths = Paths(mirrored).ToList();
            Assert.That(mirroredPaths.Select(p => p.name), Is.EqualTo(originalPaths.Select(p => p.name)));

            for (int p = 0; p < mirroredPaths.Count; p++)
            {
                var (name, events) = mirroredPaths[p];
                var errors = events.SelectMany(PitchEventValidator.Validate)
                    .Concat(PitchEventValidator.ValidateSequence(events)).ToList();
                Assert.That(errors, Is.Empty, $"{id} {name}: {string.Join(", ", errors)}");

                var a = ScreenState.Build(originalPaths[p].events, original.AwayScoreBefore, original.HomeScoreBefore, events.Count - 1);
                var b = ScreenState.Build(events, mirrored.AwayScoreBefore, mirrored.HomeScoreBefore, events.Count - 1);
                Assert.That((b.AwayScore, b.HomeScore), Is.EqualTo((a.HomeScore, a.AwayScore)), $"{id} {name}: 최종 점수 원정 ↔ 홈");
            }
        }

        [TestCaseSource(nameof(Ids))]
        public void 원정_경기에서도_선수_소속이_공수와_맞고_득점_기초값이_시작_점수와_같다(string id)
        {
            var f = ScenarioMirror.Mirror(ScenarioCatalog.Find(id).Create());
            var data = new DummyScenarioData(id, AwayGame);
            foreach (var e in f.Events)
            {
                var battingSide = e.HalfInning == HalfInning.Top ? TeamSide.Away : TeamSide.Home;
                Assert.That(data.GetPlayer(e.BatterId).Side, Is.EqualTo(battingSide), $"{id}: 타자 {e.BatterId}");
                Assert.That(data.GetPlayer(e.PitcherId).Side, Is.Not.EqualTo(battingSide), $"{id}: 투수 {e.PitcherId}");
            }
            Assert.That(data.GetBoxSeed(TeamSide.Away).RunsByInning.Sum(), Is.EqualTo(f.AwayScoreBefore), $"{id} 원정");
            Assert.That(data.GetBoxSeed(TeamSide.Home).RunsByInning.Sum(), Is.EqualTo(f.HomeScoreBefore), $"{id} 홈");
        }
    }
}
