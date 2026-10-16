using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>라인업 카드 교체 규칙과 선수단 적용.</summary>
    public class LineupCardTests
    {
        static readonly MasterDatabase Db = TestLeague.TwoTeams;
        static TeamRoster Busan => Db.GetRoster("삼성 라이온즈");

        static string[] BattingIds(LineupCard c) => c.Batting.Select(s => s.PlayerId).ToArray();

        [Test]
        public void 기본_라인업은_선수_DB_타순과_벤치_순서()
        {
            var card = LineupCard.FromRoster(Busan);
            Assert.That(BattingIds(card), Is.EqualTo(Enumerable.Range(1, 9).Select(i => $"WV00{i}")));
            Assert.That(card.Batting[2].Position, Is.EqualTo("RF"));
            Assert.That(card.Bench, Is.EqualTo(new[] { "WV010", "WV011", "WV012" }));
            Assert.That(card.Matches(Busan), Is.True);
            Assert.That(LineupCard.FromRoster(null), Is.Null);
        }

        [Test]
        public void 선발과_벤치를_바꾸면_벤치_선수가_그_타순과_수비_위치로_들어간다()
        {
            var card = LineupCard.FromRoster(Busan);
            Assert.That(card.TrySwap(LineupSpot.Batting(3), LineupSpot.Bench(0)), Is.True);
            Assert.That(card.Batting[2], Is.EqualTo(new LineupSlot("WV010", "RF")));
            Assert.That(card.Bench[0], Is.EqualTo("WV003"));

            var roster = card.ApplyTo(Busan);
            var third = roster.AtOrder(3);
            Assert.That((third.Name, third.Position, third.Role, third.Order), Is.EqualTo(("소윤호", "RF", RosterRole.Lineup, 3)));
            Assert.That(third.BattingAverage, Is.EqualTo(0.296), "기록은 그대로");
            var benched = roster.Bench[0];
            Assert.That((benched.Name, benched.Position, benched.Role, benched.Order), Is.EqualTo(("성지환", "대타", RosterRole.Bench, 1)));
            Assert.That(roster.Lineup.Select(p => p.Order), Is.EqualTo(Enumerable.Range(1, 9)));
            Assert.That(roster.Starters, Is.SameAs(Busan.Starters));
            Assert.That(roster.Bullpen, Is.SameAs(Busan.Bullpen));

            Assert.That(Busan.AtOrder(3).Name, Is.EqualTo("성지환"), "원본 선수단은 그대로");
            Assert.That(Busan.AtOrder(3).Role, Is.EqualTo(RosterRole.Lineup));
        }

        [Test]
        public void 벤치를_먼저_골라도_결과가_같다()
        {
            var a = LineupCard.FromRoster(Busan);
            var b = LineupCard.FromRoster(Busan);
            a.TrySwap(LineupSpot.Batting(5), LineupSpot.Bench(2));
            b.TrySwap(LineupSpot.Bench(2), LineupSpot.Batting(5));
            Assert.That(a.Batting, Is.EqualTo(b.Batting));
            Assert.That(a.Bench, Is.EqualTo(b.Bench));
        }

        [Test]
        public void 선발끼리_바꾸면_선수가_수비_위치를_갖고_타순만_바뀐다()
        {
            var card = LineupCard.FromRoster(Busan);
            Assert.That(card.TrySwap(LineupSpot.Batting(1), LineupSpot.Batting(9)), Is.True);
            Assert.That(card.Batting[0], Is.EqualTo(new LineupSlot("WV009", "DH")));
            Assert.That(card.Batting[8], Is.EqualTo(new LineupSlot("WV001", "CF")));
            var roster = card.ApplyTo(Busan);
            Assert.That((roster.AtOrder(1).Name, roster.AtOrder(1).Position), Is.EqualTo(("탁현서", "DH")));
            Assert.That((roster.AtOrder(9).Name, roster.AtOrder(9).Position), Is.EqualTo(("하윤재", "CF")));
        }

        [Test]
        public void 같은_두_자리를_두_번_바꾸면_원래대로()
        {
            var card = LineupCard.FromRoster(Busan);
            var original = LineupCard.FromRoster(Busan);
            card.TrySwap(LineupSpot.Batting(4), LineupSpot.Bench(1));
            card.TrySwap(LineupSpot.Batting(4), LineupSpot.Bench(1));
            Assert.That(card.Batting, Is.EqualTo(original.Batting));
            Assert.That(card.Bench, Is.EqualTo(original.Bench));
        }

        static IEnumerable<TestCaseData> Invalid()
        {
            yield return new TestCaseData(LineupSpot.Batting(0), LineupSpot.Batting(1)).SetName("타순_0은_범위_밖");
            yield return new TestCaseData(LineupSpot.Batting(10), LineupSpot.Batting(1)).SetName("타순_10은_범위_밖");
            yield return new TestCaseData(LineupSpot.Batting(3), LineupSpot.Bench(3)).SetName("벤치_4번은_범위_밖");
            yield return new TestCaseData(LineupSpot.Bench(-1), LineupSpot.Batting(3)).SetName("벤치_음수는_범위_밖");
            yield return new TestCaseData(LineupSpot.Batting(int.MinValue + 1), LineupSpot.Bench(int.MaxValue)).SetName("극단값");
            yield return new TestCaseData(LineupSpot.Batting(3), LineupSpot.Batting(3)).SetName("같은_자리");
            yield return new TestCaseData(LineupSpot.Bench(0), LineupSpot.Bench(1)).SetName("벤치끼리");
        }

        [TestCaseSource(nameof(Invalid))]
        public void 바꿀_수_없는_조합은_예외_없이_false이고_아무것도_바뀌지_않는다(LineupSpot a, LineupSpot b)
        {
            var card = LineupCard.FromRoster(Busan);
            var before = (card.Batting.ToArray(), card.Bench.ToArray());
            Assert.That(card.CanSwap(a, b), Is.False);
            Assert.DoesNotThrow(() => Assert.That(card.TrySwap(a, b), Is.False));
            Assert.That(card.Batting, Is.EqualTo(before.Item1));
            Assert.That(card.Bench, Is.EqualTo(before.Item2));
        }

        [Test]
        public void 범위_밖_자리의_선수는_null()
        {
            var card = LineupCard.FromRoster(Busan);
            Assert.That(card.PlayerAt(LineupSpot.Batting(10)), Is.Null);
            Assert.That(card.PlayerAt(LineupSpot.Bench(5)), Is.Null);
            Assert.That(card.PlayerAt(LineupSpot.Bench(0)), Is.EqualTo("WV010"));
            Assert.That(card.Contains(LineupSpot.Batting(9)), Is.True);
        }

        [Test]
        public void 카드_모양이_잘못되면_만들_수_없다()
        {
            var slots = Enumerable.Range(1, 9).Select(i => new LineupSlot("p" + i, "CF")).ToList();
            Assert.DoesNotThrow(() => new LineupCard(slots, new[] { "b1" }));
            Assert.Throws<ArgumentException>(() => new LineupCard(slots.Take(8), new string[0]), "8명");
            Assert.Throws<ArgumentException>(() => new LineupCard(slots, new[] { "p1" }), "중복");
            Assert.Throws<ArgumentException>(() => new LineupCard(slots, new[] { "" }), "빈 ID");
            var noPos = slots.ToList();
            noPos[0] = new LineupSlot("p1", "");
            Assert.Throws<ArgumentException>(() => new LineupCard(noPos, new string[0]), "빈 수비 위치");
        }

        [Test]
        public void 선수단과_맞지_않는_카드는_적용하지_않는다()
        {
            var slots = Enumerable.Range(1, 9).Select(i => new LineupSlot("p" + i, "CF")).ToList();
            var foreign = new LineupCard(slots, new[] { "b1", "b2", "b3" });
            Assert.That(foreign.Matches(Busan), Is.False);
            Assert.Throws<ArgumentException>(() => foreign.ApplyTo(Busan));
        }

        [Test]
        public void 선수단_적용은_역할_순서_위치_외의_모든_선수_필드를_복사한다()
        {
            var card = LineupCard.FromRoster(Busan);
            card.TrySwap(LineupSpot.Batting(3), LineupSpot.Bench(0));
            var applied = card.ApplyTo(Busan);
            var skip = new[] { nameof(PlayerData.Role), nameof(PlayerData.Order), nameof(PlayerData.Position) };
            var fields = typeof(PlayerData).GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !skip.Contains(f.Name)).ToList();
            foreach (var p in applied.Lineup.Concat(applied.Bench))
            {
                var original = Db.FindPlayer(p.Id);
                Assert.That(p, Is.Not.SameAs(original));
                foreach (var f in fields)
                    Assert.That(f.GetValue(p), Is.EqualTo(f.GetValue(original)), $"{p.Id}.{f.Name}");
            }
        }

        [Test]
        public void 바꾼_선수단을_경기_화면에_넣으면_3번_자리와_승부처_타자가_바뀐다()
        {
            var card = LineupCard.FromRoster(Busan);
            card.TrySwap(LineupSpot.Batting(3), LineupSpot.Bench(0));
            var mine = card.ApplyTo(Busan);
            var source = new ScenarioRosterSource(new DummyScenarioData(ScenarioCatalog.EighthBottomTactics,
                Matchup.FromNames("LG 트윈스", "삼성 라이온즈", TeamSide.Home, Db.GetRoster("LG 트윈스"), mine)), "SampleRoster.csv");

            Assert.That(source.GetLineup(TeamSide.Home).Select(e => source.GetPlayer(e.PlayerId).Name).ElementAt(2), Is.EqualTo("소윤호"));
            Assert.That(source.GetLineup(TeamSide.Home).ElementAt(2).Role, Is.EqualTo("RF"));
            var bench = source.GetBench(TeamSide.Home);
            Assert.That(source.GetPlayer(bench[0].PlayerId).Name, Is.EqualTo("성지환"));
            Assert.That(bench[0].Note, Is.EqualTo("좌타"), "메모 없는 선발 출신 벤치 선수");

            var f = Fixture8thBottomTactics.Create();
            var calc = new MatchupCalculator(source);
            Assert.That(calc.DecisionContext(f.Branch, f.Events).Batter.Name, Is.EqualTo("소윤호"), "승부처 3번 타자");
        }
    }

    public class LineupSelectionTests
    {
        static bool AnyButBenchPair(LineupSpot a, LineupSpot b) => !(a.IsBench && b.IsBench);

        [Test]
        public void 첫_클릭은_선택_같은_자리는_취소()
        {
            var s = new LineupSelection();
            Assert.That(s.Click(LineupSpot.Batting(3), AnyButBenchPair), Is.EqualTo(LineupClick.Selected));
            Assert.That(s.Selected, Is.EqualTo(LineupSpot.Batting(3)));
            Assert.That(s.Click(LineupSpot.Batting(3), AnyButBenchPair), Is.EqualTo(LineupClick.Deselected));
            Assert.That(s.Selected, Is.Null);
        }

        [Test]
        public void 두_번째_클릭이_바꿀_수_있는_자리면_교체_요청하고_선택을_푼다()
        {
            var s = new LineupSelection();
            s.Click(LineupSpot.Batting(3), AnyButBenchPair);
            Assert.That(s.Click(LineupSpot.Bench(0), AnyButBenchPair), Is.EqualTo(LineupClick.Swap));
            Assert.That(s.PendingSwap, Is.EqualTo((LineupSpot.Batting(3), LineupSpot.Bench(0))));
            Assert.That(s.Selected, Is.Null);

            Assert.That(s.Click(LineupSpot.Batting(1), AnyButBenchPair), Is.EqualTo(LineupClick.Selected));
            Assert.That(s.PendingSwap, Is.Null, "다음 클릭에서 지난 요청은 지운다");
        }

        [Test]
        public void 바꿀_수_없는_자리를_누르면_선택이_옮겨간다()
        {
            var s = new LineupSelection();
            s.Click(LineupSpot.Bench(0), AnyButBenchPair);
            Assert.That(s.Click(LineupSpot.Bench(2), AnyButBenchPair), Is.EqualTo(LineupClick.Reselected));
            Assert.That(s.Selected, Is.EqualTo(LineupSpot.Bench(2)));
            Assert.That(s.PendingSwap, Is.Null);
            s.Clear();
            Assert.That(s.Selected, Is.Null);
            Assert.Throws<ArgumentNullException>(() => s.Click(LineupSpot.Batting(1), null));
        }
    }

    /// <summary>라인업 변경의 시즌 저장 반영.</summary>
    public class LineupSaveTests
    {
        string _dir;
        string _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "DailyFranchiseLineupTests_" + Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_dir, SaveManager.FileName);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        static SeasonState WithSwappedLineup()
        {
            var season = SeasonState.CreateDummy();
            var card = LineupCard.FromRoster(TestLeague.TwoTeams.GetRoster(season.MyTeam));
            card.TrySwap(LineupSpot.Batting(3), LineupSpot.Bench(0));
            card.TrySwap(LineupSpot.Batting(1), LineupSpot.Batting(2));
            season.MyLineup = card;
            return season;
        }

        [Test]
        public void 바꾼_라인업이_JSON_왕복으로_유지된다()
        {
            var season = WithSwappedLineup();
            var loaded = SaveManager.FromJson(SaveManager.ToJson(season));
            Assert.That(loaded.MyLineup.Batting, Is.EqualTo(season.MyLineup.Batting));
            Assert.That(loaded.MyLineup.Bench, Is.EqualTo(season.MyLineup.Bench));
            Assert.That(loaded.MyLineup.Batting[2], Is.EqualTo(new LineupSlot("WV010", "RF")));
        }

        [Test]
        public void 바꾼_라인업이_세이브_파일에_덮어써지고_다시_불러와진다()
        {
            var saves = new SaveManager(_path);
            var season = SeasonState.CreateDummy();
            saves.Save(season);
            Assert.That(saves.LoadOrCreate(SeasonState.CreateDummy, out _).MyLineup, Is.Null, "바꾸기 전에는 기본 라인업");

            season.MyLineup = WithSwappedLineup().MyLineup;
            saves.Save(season);
            Assert.That(File.ReadAllText(_path), Does.Contain("WV010"));
            var loaded = saves.LoadOrCreate(SeasonState.CreateDummy, out var status);
            Assert.That(status, Is.EqualTo(SeasonLoadStatus.Loaded));
            Assert.That(loaded.MyLineup.PlayerAt(LineupSpot.Batting(3)), Is.EqualTo("WV010"));
            Assert.That(loaded.MyLineup.PlayerAt(LineupSpot.Bench(0)), Is.EqualTo("WV003"));
        }

        [Test]
        public void 라인업_항목이_없는_이전_저장_파일은_기본_라인업으로_읽힌다()
        {
            var json = SaveManager.ToJson(SeasonState.CreateDummy());
            var phase9 = System.Text.RegularExpressions.Regex.Replace(json, @",\s*""Lineup""\s*:\s*\{[^{}]*\}", "");
            Assert.That(phase9, Is.Not.EqualTo(json), "Lineup 항목을 지웠다");
            Assert.That(phase9, Does.Not.Contain("Lineup"));
            Assert.That(SaveManager.FromJson(phase9).MyLineup, Is.Null);
        }

        static IEnumerable<TestCaseData> BrokenLineups()
        {
            string Json(Action<SeasonSaveData> edit)
            {
                var data = WithSwappedLineup().ToSaveData();
                edit(data);
                return UnityEngine.JsonUtility.ToJson(data);
            }
            yield return new TestCaseData(Json(d => d.Lineup.Batting.RemoveAt(0))).SetName("타순_8명");
            yield return new TestCaseData(Json(d => d.Lineup.Positions.RemoveAt(0))).SetName("수비_위치_개수_불일치");
            yield return new TestCaseData(Json(d => d.Lineup.Bench[0] = d.Lineup.Batting[0])).SetName("중복_선수");
            yield return new TestCaseData(Json(d => d.Lineup.Batting[4] = "")).SetName("빈_선수_ID");
        }

        [TestCaseSource(nameof(BrokenLineups))]
        public void 모양이_깨진_라인업은_손상으로_본다(string json)
        {
            Assert.Throws<FormatException>(() => SaveManager.FromJson(json));
        }
    }
}
