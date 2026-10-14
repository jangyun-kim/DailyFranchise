using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>시즌 Save/Load: JsonUtility 왕복, 파일 저장·불러오기, 손상 파일 복구.</summary>
    public class SaveManagerTests
    {
        string _dir;
        string _path;
        List<string> _warnings;
        SaveManager _saves;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "DailyFranchiseSaveTests_" + Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_dir, SaveManager.FileName);
            _warnings = new List<string>();
            _saves = new SaveManager(_path, _warnings.Add);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        /// <summary>두 경기를 치른 시즌 (홈 패 3:2, 원정 승 4:1). 타 구단 결과는 고정 Seed.</summary>
        static SeasonState PlayedTwoGames()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(7);
            s.StartNextGame();
            s.FinishCurrentGame(new GameScore(3, 2), random);
            s.StartNextGame();
            s.FinishCurrentGame(new GameScore(4, 1), random);
            return s;
        }

        static void AssertSameSeason(SeasonState actual, SeasonState expected)
        {
            Assert.That(actual.SeasonTitle, Is.EqualTo(expected.SeasonTitle));
            Assert.That(actual.MyTeam, Is.EqualTo(expected.MyTeam));
            Assert.That(actual.GamesPlayed, Is.EqualTo(expected.GamesPlayed));
            Assert.That(actual.NextGame?.Number, Is.EqualTo(expected.NextGame?.Number));
            Assert.That(actual.InProgress, Is.Null);
            Assert.That(actual.Schedule.Select(g => (g.Number, g.Date, g.AwayTeam, g.HomeTeam, g.Stadium, g.Status, g.Score)),
                Is.EqualTo(expected.Schedule.Select(g => (g.Number, g.Date, g.AwayTeam, g.HomeTeam, g.Stadium, g.Status, g.Score))));
            Assert.That(actual.Standings.Select(r => (r.Rank, r.Team, r.Wins, r.Losses, r.Draws, r.GamesBehind)),
                Is.EqualTo(expected.Standings.Select(r => (r.Rank, r.Team, r.Wins, r.Losses, r.Draws, r.GamesBehind))));
        }

        [Test]
        public void 진행한_시즌을_JSON으로_바꿨다_되살리면_일정_순위_진행_상황이_같다()
        {
            var season = PlayedTwoGames();
            string json = SaveManager.ToJson(season);
            Assert.That(json, Does.Contain("\"NextIndex\": 2"));
            Assert.That(json, Does.Contain("부산 웨이브스"));

            var loaded = SaveManager.FromJson(json);
            AssertSameSeason(loaded, season);
            Assert.That(loaded.Schedule[0].Score, Is.EqualTo(new GameScore(3, 2)));
            Assert.That(loaded.MyOutcome(loaded.Schedule[1]), Is.EqualTo(GameOutcome.Win), "2경기 원정 4:1 승");
            Assert.That(loaded.Schedule[2].Score, Is.Null);

            // 되살린 시즌도 그대로 이어서 진행할 수 있다.
            loaded.StartNextGame();
            loaded.FinishCurrentGame(new GameScore(0, 0), new SeededRandom(1));
            Assert.That(loaded.GamesPlayed, Is.EqualTo(3));
        }

        [Test]
        public void 새_시즌도_JSON_왕복이_된다()
        {
            var fresh = SeasonState.CreateDummy();
            AssertSameSeason(SaveManager.FromJson(SaveManager.ToJson(fresh)), fresh);
        }

        [Test]
        public void 저장하면_파일이_생기고_불러오면_이어서_진행한다()
        {
            var season = PlayedTwoGames();
            Assert.That(_saves.HasSave, Is.False);
            Assert.That(_saves.Save(season), Is.True);
            Assert.That(File.Exists(_path), Is.True);
            Assert.That(File.Exists(_path + ".tmp"), Is.False, "임시 파일을 남기지 않는다");

            var loaded = _saves.LoadOrCreate(SeasonState.CreateDummy, out var status);
            Assert.That(status, Is.EqualTo(SeasonLoadStatus.Loaded));
            AssertSameSeason(loaded, season);
            Assert.That(loaded.NextGame.Number, Is.EqualTo(3));
            Assert.That(_warnings, Is.Empty);
        }

        [Test]
        public void 다시_저장하면_이전_저장을_덮어쓴다()
        {
            var season = SeasonState.CreateDummy();
            _saves.Save(season);
            season.StartNextGame();
            season.FinishCurrentGame(new GameScore(1, 0), new SeededRandom(3));
            _saves.Save(season);

            var loaded = _saves.LoadOrCreate(SeasonState.CreateDummy, out _);
            Assert.That(loaded.GamesPlayed, Is.EqualTo(1));
        }

        [Test]
        public void 저장_파일이_없으면_새_시즌을_시작하고_파일을_만들지_않는다()
        {
            var season = _saves.LoadOrCreate(SeasonState.CreateDummy, out var status);
            Assert.That(status, Is.EqualTo(SeasonLoadStatus.NewSeason));
            Assert.That(season.GamesPlayed, Is.EqualTo(0));
            Assert.That(File.Exists(_path), Is.False);
            Assert.That(_warnings, Is.Empty);
        }

        [Test]
        public void 경기_중에는_저장하지_않는다()
        {
            var season = SeasonState.CreateDummy();
            season.StartNextGame();
            Assert.Throws<InvalidOperationException>(() => _saves.Save(season));
            Assert.That(File.Exists(_path), Is.False);
        }

        static IEnumerable<TestCaseData> CorruptFiles()
        {
            string valid = SaveManager.ToJson(PlayedTwoGames());
            yield return new TestCaseData("").SetName("손상_빈_파일");
            yield return new TestCaseData("{ \"Version\": 1, \"Schedule\": [ {").SetName("손상_잘린_JSON");
            yield return new TestCaseData("garbage \u0000\u0001 not json").SetName("손상_JSON_아님");
            yield return new TestCaseData("{}").SetName("손상_빈_객체");
            yield return new TestCaseData(valid.Replace("\"Version\": 1", "\"Version\": 99")).SetName("손상_버전_불일치");
            yield return new TestCaseData(valid.Replace("\"NextIndex\": 2", "\"NextIndex\": 42")).SetName("손상_진행_위치_범위_밖");
            yield return new TestCaseData(valid.Replace("\"NextIndex\": 2", "\"NextIndex\": 3")).SetName("손상_진행_위치와_종료_경기_불일치");
            yield return new TestCaseData(new Regex("\"AwayTeam\": \"[^\"]+\"").Replace(valid, "\"AwayTeam\": \"없는 구단\"", 1))
                .SetName("손상_순위표에_없는_팀");
            yield return new TestCaseData(new Regex(@"""Wins"": \d+").Replace(valid, "\"Wins\": -1", 1)).SetName("손상_음수_성적");
        }

        [TestCaseSource(nameof(CorruptFiles))]
        public void 손상된_JSON은_FormatException(string json)
        {
            Assert.Throws<FormatException>(() => SaveManager.FromJson(json));
        }

        [TestCaseSource(nameof(CorruptFiles))]
        public void 손상된_저장_파일은_경고를_남기고_새_시즌으로_덮어쓴다(string json)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, json);

            SeasonState season = null;
            SeasonLoadStatus status = default;
            Assert.DoesNotThrow(() => season = _saves.LoadOrCreate(SeasonState.CreateDummy, out status));

            Assert.That(status, Is.EqualTo(SeasonLoadStatus.RecoveredFromCorrupt));
            Assert.That(season.GamesPlayed, Is.EqualTo(0), "새 시즌");
            Assert.That(_warnings, Has.Count.EqualTo(1));
            Assert.That(_warnings[0], Does.Contain("손상"));

            // 손상 파일은 새 시즌으로 덮어써져, 다음 실행부터는 정상으로 불러온다.
            var again = new SaveManager(_path, _warnings.Add).LoadOrCreate(SeasonState.CreateDummy, out var nextStatus);
            Assert.That(nextStatus, Is.EqualTo(SeasonLoadStatus.Loaded));
            AssertSameSeason(again, SeasonState.CreateDummy());
            Assert.That(_warnings, Has.Count.EqualTo(1));
        }
    }
}
