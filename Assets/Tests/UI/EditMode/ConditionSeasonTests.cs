using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DailyFranchise.App;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 22: 시즌의 선수 컨디션 저장·하위 호환과 경기 종료 처리 연동.</summary>
    public class ConditionSeasonTests
    {
        const int LeagueSeed = 20261016;

        static GameParticipation Participation()
        {
            var g = new GameParticipation();
            foreach (var id in new[] { "WV001", "WV002", "WV003" }) g.AddBatter(id);
            g.AddPitches("WV013", 92);
            g.AddPitches("WV014", 18);
            foreach (var id in new[] { "WV010", "WV015", "WV017" }) g.AddRosterPlayer(id);
            g.SetStartingPitcher("부산 웨이브스", "WV013");
            g.SetStartingPitcher("서울 블루윙스", "BW010");
            return g;
        }

        static PostGameReport Play(SeasonState s, GameParticipation game, IRandomSource conditionRandom)
        {
            s.StartNextGame();
            return PostGameProcessor.Process(s, new GameScore(2, 5), new SeededRandom(LeagueSeed), null, game, conditionRandom);
        }

        [Test]
        public void 경기_종료_처리가_출장_기록으로_컨디션을_바꾼다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.ConditionOf("WV001"), Is.EqualTo(PlayerCondition.Normal), "기록 없음 = 보통");
            var r = Play(s, Participation(), new ScriptedRandom(0.1, 0.9, 0.9)); // WV001만 하락
            Assert.That(s.ConditionOf("WV001"), Is.EqualTo(2));
            Assert.That((s.ConditionOf("WV002"), s.ConditionOf("WV003")), Is.EqualTo((3, 3)));
            Assert.That(s.ConditionOf("WV013"), Is.EqualTo(1), "선발 92구 → −2");
            Assert.That(s.ConditionOf("WV014"), Is.EqualTo(3), "구원 18구 → 그대로");
            Assert.That(new[] { "WV010", "WV015", "WV017" }.Select(s.ConditionOf), Is.All.EqualTo(4), "결장 → +1");
            Assert.That(r.Conditions.Select(c => c.PlayerId), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(r.Conditions, Has.Count.EqualTo(8));
            Assert.That((s.LastStarterOf("부산 웨이브스"), s.LastStarterOf("서울 블루윙스"), s.LastStarterOf("인천 하버스")),
                Is.EqualTo(("WV013", "BW010", (string)null)), "선발 순번 기록");
        }

        [Test]
        public void 컨디션_난수는_타_구단_경기_결과를_바꾸지_않는다()
        {
            var plain = SeasonState.CreateDummy();
            plain.StartNextGame();
            PostGameProcessor.Process(plain, new GameScore(2, 5), new SeededRandom(LeagueSeed), null);

            var withConditions = SeasonState.CreateDummy();
            Play(withConditions, Participation(), new SeededRandom(7));

            Assert.That(withConditions.LastDayResults.Select(g => (g.AwayTeam, g.HomeTeam, g.Score)),
                Is.EqualTo(plain.LastDayResults.Select(g => (g.AwayTeam, g.HomeTeam, g.Score))), "같은 리그 난수 → 같은 결과");
            Assert.That(withConditions.Standings.Select(r => (r.Team, r.Wins, r.Losses)),
                Is.EqualTo(plain.Standings.Select(r => (r.Team, r.Wins, r.Losses))));
            Assert.That(plain.Conditions, Is.Empty, "출장 기록 없이 처리하면 컨디션은 그대로");
            Assert.Throws<ArgumentNullException>(() => Play(SeasonState.CreateDummy(), Participation(), null));
        }

        [Test]
        public void 컨디션은_저장했다_불러와도_같다()
        {
            var s = SeasonState.CreateDummy();
            Play(s, Participation(), new SeededRandom(3));
            var loaded = SaveManager.FromJson(SaveManager.ToJson(s));
            Assert.That(loaded.Conditions, Is.EquivalentTo(s.Conditions));
            Assert.That(loaded.Conditions, Is.Not.Empty);
            Assert.That(loaded.LastStarters, Is.EquivalentTo(s.LastStarters));
        }

        [Test]
        public void 컨디션_목록이_없는_이전_저장_파일은_전원_보통으로_읽힌다()
        {
            var s = SeasonState.CreateDummy();
            Play(s, Participation(), new SeededRandom(3));
            string json = SaveManager.ToJson(s);
            string legacy = Regex.Replace(json, @",\s*""(Conditions|LastStarters)"":\s*\[[^\]]*\]", "");
            Assert.That(legacy, Does.Not.Contain("Conditions").And.Not.Contain("LastStarters"));
            var loaded = SaveManager.FromJson(legacy);
            Assert.That(loaded.Conditions, Is.Empty);
            Assert.That(loaded.LastStarters, Is.Empty, "선발 순번 없음 → 로테이션 1번부터");
            Assert.That(new[] { "WV001", "WV013", "없는 선수" }.Select(loaded.ConditionOf), Is.All.EqualTo(PlayerCondition.Normal));
            Assert.That(loaded.NextGame.Number, Is.EqualTo(2), "나머지 진행은 그대로");

            var data = s.ToSaveData();
            data.Conditions = null;
            Assert.That(SeasonState.FromSaveData(data).Conditions, Is.Empty, "null 목록도 전원 보통");
        }

        [Test]
        public void 범위_밖_값은_1에서_5로_맞추고_ID_없는_항목은_건너뛴다()
        {
            var data = SeasonState.CreateDummy().ToSaveData();
            data.Conditions.Add(new ConditionData { Id = "a", Value = 9 });
            data.Conditions.Add(new ConditionData { Id = "b", Value = -3 });
            data.Conditions.Add(new ConditionData { Id = "", Value = 2 });
            data.Conditions.Add(null);
            var loaded = SeasonState.FromSaveData(data);
            Assert.That((loaded.ConditionOf("a"), loaded.ConditionOf("b")), Is.EqualTo((5, 1)));
            Assert.That(loaded.Conditions, Has.Count.EqualTo(2));
        }
    }
}
