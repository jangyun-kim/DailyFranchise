using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    public class DecisionPlaybackTests
    {
        ScenarioFixture _f;
        PitchPlayback _p;
        List<int> _decisions;

        [SetUp]
        public void SetUp()
        {
            _f = Fixture8thBottomTactics.Create();
            _p = new PitchPlayback(_f.Events);
            _p.SetDecisionPoint(_f.Branch.AfterIndex);
            _decisions = new List<int>();
            _p.DecisionReached += _decisions.Add;
        }

        [Test]
        public void 자동_재생은_분기점_투구를_보여준_뒤_멈춘다()
        {
            _p.Play();
            _p.Tick(100);
            Assert.That(_p.Index, Is.EqualTo(_f.Branch.AfterIndex));
            Assert.That(_p.AwaitingDecision, Is.True);
            Assert.That(_p.IsPlaying, Is.False);
            Assert.That(_decisions, Is.EqualTo(new[] { _f.Branch.AfterIndex }), "한 번만 알림");
        }

        [Test]
        public void 작전_대기_중에는_재생과_앞으로_이동이_막힌다()
        {
            _p.SkipToEnd();
            Assert.That(_p.AwaitingDecision, Is.True);
            _p.Play();
            Assert.That(_p.IsPlaying, Is.False);
            _p.StepPitch();
            Assert.That(_p.Index, Is.EqualTo(_f.Branch.AfterIndex));
        }

        [Test]
        public void 작전을_고르면_같은_위치에서_후속_구간으로_이어진다()
        {
            _p.SkipToEnd();
            var path = _f.Branch.PathFor(_f.Events, TacticKind.SqueezeBunt);
            _p.ContinueWith(path);
            Assert.That(_p.AwaitingDecision, Is.False);
            Assert.That(_p.DecisionIndex, Is.Null);
            Assert.That(_p.Index, Is.EqualTo(_f.Branch.AfterIndex), "재생 위치 유지");
            Assert.That(_p.Count, Is.EqualTo(path.Count));

            var seen = new List<int>();
            _p.IndexChanged += seen.Add;
            _p.Play();
            _p.Tick(1000);
            Assert.That(seen, Is.EqualTo(Enumerable.Range(_f.Branch.AfterIndex + 1, path.Count - _f.Branch.AfterIndex - 1)));
            Assert.That(_p.IsAtEnd, Is.True);
        }

        [Test]
        public void 이음새가_다른_경로는_거부한다()
        {
            _p.SkipToEnd();
            var foreign = Fixture8thBottom.Create().Events; // 내용은 같아도 다른 객체
            Assert.Throws<ArgumentException>(() => _p.ContinueWith(foreign));
            Assert.That(_p.AwaitingDecision, Is.True, "거부되면 대기 상태 유지");
        }

        [Test]
        public void 대기_중이_아니면_이어_붙일_수_없다()
        {
            var path = _f.Branch.PathFor(_f.Events, TacticKind.Default);
            Assert.Throws<InvalidOperationException>(() => _p.ContinueWith(path));
        }

        [Test]
        public void 분기점_앞으로_되돌아가면_대기가_풀리고_다시_오면_다시_멈춘다()
        {
            _p.SkipToEnd();
            _p.Restart();
            Assert.That(_p.AwaitingDecision, Is.False);
            Assert.That(_p.Index, Is.EqualTo(-1));
            _p.SkipToEnd();
            Assert.That(_p.AwaitingDecision, Is.True);
            Assert.That(_decisions, Has.Count.EqualTo(2));
        }

        [Test]
        public void 분기점은_이벤트_범위_안이어야_한다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _p.SetDecisionPoint(_f.Events.Count));
        }

        [Test]
        public void 작전_문구는_모든_작전에_있다()
        {
            foreach (TacticKind t in Enum.GetValues(typeof(TacticKind)))
            {
                Assert.That(TacticText.Label(t), Is.Not.EqualTo(t.ToString()), $"{t} 이름");
                Assert.That(TacticText.Detail(t), Does.Contain("이득").And.Contain("위험"), $"{t} 설명");
            }
        }

        [Test]
        public void 분기_경로의_선수와_스코어보드_더미_데이터가_있다()
        {
            var data = new DummyScenarioData(_f.Id);
            foreach (var tactic in _f.Branch.Tactics)
            foreach (var e in _f.Branch.PathFor(_f.Events, tactic))
            {
                Assert.That(data.GetPlayer(e.BatterId).Name, Is.Not.EqualTo(e.BatterId));
                Assert.That(data.GetPlayer(e.PitcherId).Name, Is.Not.EqualTo(e.PitcherId));
            }
            Assert.That(data.GetBoxSeed(TeamSide.Home).RunsByInning.Sum(), Is.EqualTo(_f.HomeScoreBefore));
        }

        [Test]
        public void 스퀴즈_경로에서_타자는_타수가_늘지_않는다()
        {
            var path = _f.Branch.PathFor(_f.Events, TacticKind.SqueezeBunt);
            Assert.That(ScenarioReadModel.BatterLineThrough(path, 2, Fixture8thBottom.Batter1), Is.EqualTo((0, 0)));
        }
    }
}
