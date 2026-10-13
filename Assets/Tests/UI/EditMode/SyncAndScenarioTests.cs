using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    public class StatsSyncTests
    {
        [Test]
        public void 연출이_없으면_즉시_반영()
        {
            var sync = new StatsSync();
            sync.OnIndexChanged(2, animating: false);
            Assert.That(sync.StatsIndex, Is.EqualTo(2));
            Assert.That(sync.IsPending, Is.False);
            Assert.That(sync.OnAnimationCompleted(), Is.False);
        }

        [Test]
        public void 연출_중에는_직전_투구까지_완료되면_현재_투구까지()
        {
            var sync = new StatsSync();
            sync.OnIndexChanged(4, animating: true);
            Assert.That(sync.StatsIndex, Is.EqualTo(3));
            Assert.That(sync.IsPending, Is.True);
            Assert.That(sync.OnAnimationCompleted(), Is.True);
            Assert.That(sync.StatsIndex, Is.EqualTo(4));
            Assert.That(sync.OnAnimationCompleted(), Is.False, "두 번 반영하지 않음");
        }

        [Test]
        public void 연출_도중_다음_투구로_넘어가면_이전_투구_기록은_포함된다()
        {
            var sync = new StatsSync();
            sync.OnIndexChanged(4, animating: true);
            sync.OnIndexChanged(5, animating: false);
            Assert.That(sync.StatsIndex, Is.EqualTo(5));
            Assert.That(sync.IsPending, Is.False);
        }

        [Test]
        public void 첫_투구가_인플레이면_투구_전_상태를_유지()
        {
            var sync = new StatsSync();
            sync.OnIndexChanged(0, animating: true);
            Assert.That(sync.StatsIndex, Is.EqualTo(-1));
            sync.Reset();
            Assert.That((sync.StatsIndex, sync.IsPending), Is.EqualTo((-1, false)));
        }
    }

    /// <summary>패널 없이 FieldView를 구동한다. 시간은 Tick으로만 흐른다.</summary>
    public class FieldViewTests
    {
        VisualElement _root;
        FieldView _field;
        int _completed;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            var stage = new VisualElement { name = "diamond-stage" };
            _root.Add(stage);
            for (int i = 1; i <= 3; i++) stage.Add(new VisualElement { name = "base-" + i });
            _field = new FieldView(_root);
            _completed = 0;
            _field.AnimationCompleted += () => _completed++;
        }

        static ScreenState At(ScenarioFixture f, int index) =>
            ScreenState.Build(f.Events, f.AwayScoreBefore, f.HomeScoreBefore, index);

        bool Occupied(int b) => _root.Q("base-" + b).ClassListContains("base--occupied");

        [Test]
        public void 인플레이_연출은_전체_시간이_지난_순간_한_번만_완료를_알린다()
        {
            var f = Fixture8thBottom.Create();
            Assert.That(_field.Show(At(f, 4), animate: true), Is.True);
            _field.Tick(FieldView.TotalSeconds - 0.01f);
            Assert.That(_completed, Is.EqualTo(0));
            Assert.That(_field.IsAnimating, Is.True);
            _field.Tick(0.02f);
            Assert.That(_completed, Is.EqualTo(1));
            _field.Tick(5f);
            Assert.That(_completed, Is.EqualTo(1));
        }

        [Test]
        public void 인플레이가_아니면_연출하지_않는다()
        {
            var f = Fixture8thBottom.Create();
            Assert.That(_field.Show(At(f, 3), animate: true), Is.False);
            Assert.That(_field.IsAnimating, Is.False);
            Assert.That(_field.BattedBallVisible, Is.False);
        }

        [Test]
        public void 중단된_연출은_완료를_알리지_않는다()
        {
            var f = Fixture8thBottom.Create();
            _field.Show(At(f, 4), animate: true);
            _field.Tick(0.5f);
            _field.Show(At(f, 5), animate: true);
            _field.Tick(10f);
            Assert.That(_completed, Is.EqualTo(0));
        }

        [Test]
        public void 이동_중에는_투구_전_루_상태_끝나면_투구_후_루_상태()
        {
            var f = Fixture8thBottom.Create();
            _field.Show(At(f, 4), animate: true);
            Assert.That((Occupied(1), Occupied(2), Occupied(3)), Is.EqualTo((true, false, true)));
            _field.Tick(FieldView.TotalSeconds);
            Assert.That((Occupied(1), Occupied(2), Occupied(3)), Is.EqualTo((true, true, false)));
        }

        [Test]
        public void 홈런은_모든_토큰이_홈에_들어와_사라진다()
        {
            var f = FixtureHomeRun.Create();
            _field.Show(At(f, 2), animate: true);
            Assert.That(_field.TokenCount, Is.EqualTo(2), "1루 주자 + 타자");
            _field.Tick(FieldView.TotalSeconds);
            Assert.That(_field.TokenCount, Is.EqualTo(0));
            Assert.That((Occupied(1), Occupied(2), Occupied(3)), Is.EqualTo((false, false, false)));
        }

        [Test]
        public void 병살타의_1루_주자는_2루로_뛰다가_아웃된다()
        {
            var f = FixtureDoublePlay.Create();
            _field.Show(At(f, 2), animate: true);
            _field.Tick(FieldView.LineSeconds + FieldView.RunSeconds * 0.5f);
            var runner = _field.TokenCenters()[FixtureDoublePlay.RunnerOnFirst];
            var mid = Vector2.Lerp(FieldGeometry.First, FieldGeometry.Second, 0.5f);
            Assert.That(Vector2.Distance(runner, mid), Is.LessThan(1f));
            _field.Tick(FieldView.RunSeconds);
            Assert.That(_field.TokenCount, Is.EqualTo(0));
        }

        [Test]
        public void 이닝을_끝낸_뜬공_뒤에도_잔루_주자는_제자리()
        {
            var f = FixtureInningEnd.Create();
            _field.Show(At(f, 2), animate: true);
            _field.Tick(FieldView.TotalSeconds);
            Assert.That(_field.TokenCenters().Keys, Is.EquivalentTo(new[] { FixtureInningEnd.RunnerOnSecond }));
            _field.Show(At(f, 3), animate: true);
            Assert.That(_field.TokenCount, Is.EqualTo(0), "새 반 이닝은 주자 없음");
        }

        [Test]
        public void Reset은_토큰_타구선_루_점유를_모두_지운다()
        {
            var f = Fixture8thBottom.Create();
            _field.Show(At(f, 4), animate: true);
            _field.Tick(0.3f);
            _field.Reset();
            Assert.That(_field.IsAnimating, Is.False);
            Assert.That(_field.BattedBallVisible, Is.False);
            Assert.That(_field.TokenCount, Is.EqualTo(0));
            Assert.That((Occupied(1), Occupied(2), Occupied(3)), Is.EqualTo((false, false, false)));
            _field.Tick(10f);
            Assert.That(_completed, Is.EqualTo(0));
        }
    }

    public class ScenarioDataTests
    {
        static readonly string[] Ids = ScenarioCatalog.All.Select(e => e.Id).ToArray();

        [TestCaseSource(nameof(Ids))]
        public void 시나리오의_모든_선수에_이름이_있고_득점_기초값이_시작_점수와_같다(string id)
        {
            var f = ScenarioCatalog.Find(id).Create();
            var data = new DummyScenarioData(id);

            var ids = f.Events.SelectMany(e => new[]
            {
                e.PitcherId, e.BatterId,
                e.RunnersBefore.First, e.RunnersBefore.Second, e.RunnersBefore.Third,
            }).Where(x => x != null).Distinct();
            foreach (var pid in ids)
                Assert.That(data.GetPlayer(pid).Name, Is.Not.EqualTo(pid), $"{id}: {pid} 이름 없음");

            Assert.That(data.GetBoxSeed(TeamSide.Away).RunsByInning.Sum(), Is.EqualTo(f.AwayScoreBefore), $"{id} 원정");
            Assert.That(data.GetBoxSeed(TeamSide.Home).RunsByInning.Sum(), Is.EqualTo(f.HomeScoreBefore), $"{id} 홈");
        }

        [TestCaseSource(nameof(Ids))]
        public void 선수_소속팀이_공수와_맞는다(string id)
        {
            var f = ScenarioCatalog.Find(id).Create();
            var data = new DummyScenarioData(id);
            foreach (var e in f.Events)
            {
                var battingSide = e.HalfInning == HalfInning.Top ? TeamSide.Away : TeamSide.Home;
                Assert.That(data.GetPlayer(e.BatterId).Side, Is.EqualTo(battingSide), $"{id}: 타자 {e.BatterId}");
                Assert.That(data.GetPlayer(e.PitcherId).Side, Is.Not.EqualTo(battingSide), $"{id}: 투수 {e.PitcherId}");
            }
        }

        [Test]
        public void 모르는_시나리오는_거부()
        {
            Assert.Throws<System.ArgumentException>(() => new DummyScenarioData("nope"));
        }
    }
}
