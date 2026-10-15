using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    /// <summary>Phase 20: 정규 이닝 루프의 경기 끝 규칙(9회말·끝내기·연장·무승부)과 CPU 투수 교체 AI.</summary>
    public class GameFlowTests
    {
        static PitchEvent End(int inning, HalfInning half, int outsAfter) =>
            new PitchEvent { Inning = inning, HalfInning = half, OutsAfter = outsAfter, BatterId = "away_b1", Outcome = PlateAppearanceOutcome.InPlay };

        // (회, 초/말, 아웃, 원정, 홈, 이어 갈 수 있나) → 흐름
        [TestCase(9, HalfInning.Top, 3, 2, 3, true, GameFlowStep.GameOver, TestName = "9회초 종료 홈 리드 → 9회말 없이 종료")]
        [TestCase(9, HalfInning.Top, 3, 3, 2, true, GameFlowStep.NextHalf, TestName = "9회초 종료 홈 열세 → 9회말 진행")]
        [TestCase(9, HalfInning.Top, 3, 3, 3, true, GameFlowStep.NextHalf, TestName = "9회초 종료 동점 → 9회말 진행")]
        [TestCase(9, HalfInning.Bottom, 1, 3, 4, true, GameFlowStep.GameOver, TestName = "9회말 1사 역전 → 끝내기 즉시 종료")]
        [TestCase(9, HalfInning.Bottom, 2, 3, 3, true, GameFlowStep.ContinueHalf, TestName = "9회말 동점 2사 → 계속")]
        [TestCase(9, HalfInning.Bottom, 3, 4, 3, true, GameFlowStep.GameOver, TestName = "9회말 종료 원정 리드 → 종료")]
        [TestCase(9, HalfInning.Bottom, 3, 3, 3, true, GameFlowStep.NextHalf, TestName = "9회말 종료 동점 → 10회초 연장")]
        [TestCase(10, HalfInning.Top, 3, 4, 3, true, GameFlowStep.NextHalf, TestName = "10회초 원정 득점 → 10회말 진행")]
        [TestCase(11, HalfInning.Bottom, 0, 2, 3, true, GameFlowStep.GameOver, TestName = "11회말 끝내기")]
        [TestCase(12, HalfInning.Top, 3, 2, 2, true, GameFlowStep.NextHalf, TestName = "12회초 동점 → 12회말")]
        [TestCase(12, HalfInning.Bottom, 3, 2, 2, true, GameFlowStep.GameOver, TestName = "12회말 종료 동점 → 무승부")]
        [TestCase(8, HalfInning.Bottom, 3, 1, 5, true, GameFlowStep.NextHalf, TestName = "8회말 종료는 홈 리드여도 9회초 진행")]
        [TestCase(8, HalfInning.Bottom, 2, 1, 5, true, GameFlowStep.ContinueHalf, TestName = "8회말 홈 리드는 끝내기가 아니다")]
        [TestCase(1, HalfInning.Top, 1, 0, 0, false, GameFlowStep.NextHalf, TestName = "타석 수 한도로 못 이으면 반 이닝 종료 처리")]
        [TestCase(9, HalfInning.Top, 1, 1, 2, false, GameFlowStep.GameOver, TestName = "9회초 한도 종료도 홈 리드면 종료")]
        public void 타석이_끝난_뒤_경기_흐름은_야구_규칙을_따른다(int inning, HalfInning half, int outs, int away, int home,
            bool canContinue, GameFlowStep expected)
        {
            Assert.That(PlayByPlayGenerator.StepAfter(End(inning, half, outs), away, home, canContinue), Is.EqualTo(expected));
        }

        [Test]
        public void 반_이닝_순서는_초_말_다음_회로_이어지고_12회를_넘지_않는다()
        {
            var halves = new List<(int, HalfInning)>();
            var at = (inning: 1, half: HalfInning.Top);
            int guard = 0;
            while (true)
            {
                halves.Add(at);
                // 0 : 0 동점이 이어지는 경기
                var step = PlayByPlayGenerator.StepAfter(End(at.inning, at.half, 3), 0, 0, true);
                if (step == GameFlowStep.GameOver) break;
                Assert.That(step, Is.EqualTo(GameFlowStep.NextHalf));
                at = PlayByPlayGenerator.NextHalf(at.inning, at.half);
                Assert.That(++guard, Is.LessThan(100), "무한 루프");
            }
            Assert.That(halves, Has.Count.EqualTo(24), "1회초 ~ 12회말");
            Assert.That(halves.Last(), Is.EqualTo((12, HalfInning.Bottom)));
            Assert.That(halves.Distinct().Count(), Is.EqualTo(24));
        }

        [Test]
        public void 실점은_그_투수가_마운드에_있을_때만_센다()
        {
            var events = new List<PitchEvent>
            {
                new PitchEvent { PitcherId = "away_sp", RunsScored = 2 },
                new PitchEvent { PitcherId = "away_rp1", RunsScored = 1 },
                new PitchEvent { PitcherId = "away_sp", RunsScored = 1 },
            };
            Assert.That(PitcherState.RunsAllowedBy(events, "away_sp", 2), Is.EqualTo(3));
            Assert.That(PitcherState.RunsAllowedBy(events, "away_sp", 1), Is.EqualTo(2));
            Assert.That(PitcherState.RunsAllowedBy(events, "away_rp1", 2), Is.EqualTo(1));
            Assert.That(PitcherState.RunsAllowedBy(events, "away_sp", -1), Is.Zero);
        }

        static CpuRosterManager Cpu(params (string id, string pos)[] bullpen) =>
            new CpuRosterManager(new InGameRoster("away_sp", bullpen.Select((p, i) =>
                new PlayerData { Id = p.id, Name = p.id, Role = RosterRole.Bullpen, Order = i + 1, Position = p.pos })));

        static CpuRosterManager Standard() =>
            Cpu(("away_rp1", "마무리"), ("away_rp2", "셋업"), ("away_rp3", "롱릴리프"));

        [Test]
        public void CPU_선발이_체력_고갈이면_이닝_교대_때_불펜으로_바꾼다()
        {
            var cpu = Standard();
            Assert.That(cpu.OnHalfInningStart(7, new PitcherState("away_sp", true, 99), 0), Is.Null, "99구는 그대로");

            var change = cpu.OnHalfInningStart(7, new PitcherState("away_sp", true, 100), 0);
            Assert.That(change, Is.Not.Null);
            Assert.That((change.From, change.To, change.Reason), Is.EqualTo(("away_sp", "away_rp2", "체력 고갈")), "9회 전에는 마무리를 아낀다");
            Assert.That(cpu.Roster.CurrentPitcherId, Is.EqualTo("away_rp2"));
            Assert.That(cpu.Roster.UsedPitchers, Is.EqualTo(new[] { "away_sp" }));

            // 구원도 30구면 고갈 → 다음 투수
            change = cpu.OnHalfInningStart(8, new PitcherState("away_rp2", false, 30), 0);
            Assert.That(change.To, Is.EqualTo("away_rp3"));
            Assert.That(cpu.Roster.CanBringIn("away_sp"), Is.False, "강판된 선발은 다시 안 나온다");
        }

        [Test]
        public void 구회_이후에는_마무리를_올린다()
        {
            var cpu = Standard();
            var change = cpu.OnHalfInningStart(9, new PitcherState("away_sp", true, 104), 0);
            Assert.That(change.To, Is.EqualTo("away_rp1"));
        }

        [TestCase(6, 4, true, TestName = "6회 4실점 선발 → 교체")]
        [TestCase(6, 3, false, TestName = "6회 3실점 선발 → 유지")]
        [TestCase(5, 7, false, TestName = "5회 이전은 실점으로 바꾸지 않는다")]
        [TestCase(8, 5, true, TestName = "8회 5실점 → 교체")]
        public void 육회_이후_선발이_4실점_이상이면_바꾼다(int inning, int runs, bool replaced)
        {
            var cpu = Standard();
            var change = cpu.OnHalfInningStart(inning, new PitcherState("away_sp", true, 60), runs);
            Assert.That(change != null, Is.EqualTo(replaced));
            if (replaced) Assert.That(change.Reason, Is.EqualTo($"6회 이후 {runs}실점"));
        }

        [Test]
        public void 구원은_3실점_기준이고_남은_투수가_없으면_그대로_던진다()
        {
            var cpu = Cpu(("away_rp1", "셋업"));
            Assert.That(cpu.OnHalfInningStart(7, new PitcherState("away_sp", true, 100), 0).To, Is.EqualTo("away_rp1"));
            Assert.That(CpuRosterManager.ShouldReplace(new PitcherState("away_rp1", false, 10), 3, 8), Is.True);
            Assert.That(CpuRosterManager.ShouldReplace(new PitcherState("away_rp1", false, 10), 2, 8), Is.False);
            Assert.That(cpu.OnHalfInningStart(8, new PitcherState("away_rp1", false, 35), 3), Is.Null, "올릴 투수가 없다");
            Assert.That(cpu.Roster.CurrentPitcherId, Is.EqualTo("away_rp1"));
        }

        [Test]
        public void CPU_판단은_결정적이고_지금_투수가_아닌_상태는_거부한다()
        {
            var a = Standard();
            var b = Standard();
            for (int inning = 1; inning <= 12; inning++)
            {
                var sa = new PitcherState(a.Roster.CurrentPitcherId, a.Roster.CurrentPitcherId == "away_sp", 35 * inning);
                var sb = new PitcherState(b.Roster.CurrentPitcherId, b.Roster.CurrentPitcherId == "away_sp", 35 * inning);
                Assert.That(a.OnHalfInningStart(inning, sa, inning)?.ToString(), Is.EqualTo(b.OnHalfInningStart(inning, sb, inning)?.ToString()));
            }
            Assert.Throws<ArgumentException>(() => Standard().OnHalfInningStart(7, new PitcherState("away_rp1", false, 40), 0));
            Assert.Throws<ArgumentNullException>(() => new CpuRosterManager(null));
        }
    }
}
