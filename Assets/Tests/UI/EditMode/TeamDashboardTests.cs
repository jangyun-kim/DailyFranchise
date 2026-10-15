using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// 프런트 오피스 대시보드 (Phase 16): 팀 득실점(RS/RA) 누적·최근 10경기 전적·세이브 호환,
    /// 팀 내 기록 1위 추출(RosterAnalyzer). 타 구단 결과 난수는 고정 Seed만 쓴다.
    /// </summary>
    public class TeamDashboardTests
    {
        const int Seed = 20261016;

        static PostGameReport Play(SeasonState s, GameScore score, int seed = Seed, SaveManager saves = null)
        {
            s.StartNextGame();
            return PostGameProcessor.Process(s, score, new SeededRandom(seed), saves);
        }

        /// <summary>내 구단 득점 기준으로 일정의 원정 : 홈 점수를 만든다.</summary>
        static GameScore Mine(SeasonState s, int myRuns, int oppRuns)
        {
            var g = s.NextGame;
            return g.HomeTeam == s.MyTeam ? new GameScore(oppRuns, myRuns) : new GameScore(myRuns, oppRuns);
        }

        // ───────── 득실점 누적 ─────────

        [Test]
        public void 홈_경기_득실점은_내_구단과_상대에_반대로_누적된다()
        {
            var s = SeasonState.CreateDummy(); // 1경기: 서울 블루윙스(원정) vs 부산 웨이브스(홈)
            var me = s.MyStanding;
            var opp = s.StandingOf("서울 블루윙스");
            var (rs, ra, oRs, oRa) = (me.RunsScored, me.RunsAllowed, opp.RunsScored, opp.RunsAllowed);

            var report = Play(s, new GameScore(2, 7));

            Assert.That((me.RunsScored, me.RunsAllowed), Is.EqualTo((rs + 7, ra + 2)));
            Assert.That((opp.RunsScored, opp.RunsAllowed), Is.EqualTo((oRs + 2, oRa + 7)));
            Assert.That((report.MyRunsScored, report.MyRunsAllowed, report.MyOutcome), Is.EqualTo((7, 2, GameOutcome.Win)));
            Assert.That(me.RunDifferential, Is.EqualTo(me.RunsScored - me.RunsAllowed));
        }

        [Test]
        public void 원정_경기_득실점은_일정의_원정_쪽으로_누적된다()
        {
            var s = SeasonState.CreateDummy();
            Play(s, new GameScore(0, 1));
            Assert.That(s.NextGame.AwayTeam, Is.EqualTo(s.MyTeam), "2경기는 부산 원정");
            var me = s.MyStanding;
            var (rs, ra) = (me.RunsScored, me.RunsAllowed);

            var report = Play(s, new GameScore(5, 3));

            Assert.That((me.RunsScored, me.RunsAllowed), Is.EqualTo((rs + 5, ra + 3)));
            Assert.That((report.MyRunsScored, report.MyRunsAllowed), Is.EqualTo((5, 3)));
        }

        [Test]
        public void 타_구단_경기_득실점도_누적되고_리그_득점_합은_실점_합과_같다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.Standings.Sum(r => r.RunsScored), Is.EqualTo(s.Standings.Sum(r => r.RunsAllowed)), "개막 더미도 합이 맞는다");
            var before = s.Standings.ToDictionary(r => r.Team, r => (r.RunsScored, r.RunsAllowed));

            var report = Play(s, new GameScore(3, 4));

            foreach (var game in report.DayResults)
                foreach (var team in new[] { game.AwayTeam, game.HomeTeam })
                {
                    var row = s.StandingOf(team);
                    Assert.That((row.RunsScored, row.RunsAllowed),
                        Is.EqualTo((before[team].RunsScored + game.RunsFor(team), before[team].RunsAllowed + game.RunsAgainst(team))), team);
                }
            Assert.That(s.Standings.Sum(r => r.RunsScored), Is.EqualTo(s.Standings.Sum(r => r.RunsAllowed)));
        }

        [Test]
        public void 득실차_표기는_부호를_붙인다()
        {
            Assert.That(new StandingRow { RunsScored = 210, RunsAllowed = 198 }.RunDifferentialText, Is.EqualTo("+12"));
            Assert.That(new StandingRow { RunsScored = 190, RunsAllowed = 193 }.RunDifferentialText, Is.EqualTo("-3"));
            Assert.That(new StandingRow { RunsScored = 5, RunsAllowed = 5 }.RunDifferentialText, Is.EqualTo("0"));
        }

        [Test]
        public void 득점_실점_표기와_RunsFor는_경기_팀이_아니면_예외()
        {
            var g = new GameRecord { AwayTeam = "A", HomeTeam = "B", Score = new GameScore(3, 1) };
            Assert.That((g.RunsFor("A"), g.RunsAgainst("A"), g.RunsFor("B"), g.RunsAgainst("B")), Is.EqualTo((3, 1, 1, 3)));
            Assert.Throws<ArgumentException>(() => g.RunsFor("C"));
            Assert.Throws<ArgumentException>(() => g.RunsAgainst("C"));
        }

        // ───────── 최근 10경기 ─────────

        [Test]
        public void 최근_10경기는_끝난_내_경기_중_마지막_10경기만_센다()
        {
            var standings = new List<StandingRow> { new StandingRow { Team = "나" }, new StandingRow { Team = "너" } };
            var schedule = Enumerable.Range(1, 12)
                .Select(n => new ScheduledGame { Number = n, AwayTeam = "너", HomeTeam = "나" }).ToList();
            var s = new SeasonState("t", "나", schedule, standings);
            Assert.That(s.MyRecentForm().Text, Is.EqualTo("—"));

            // 2패 → 이후 7승 2패 1무. 처음 두 패는 10경기 밖으로 밀려난다.
            var results = new[] { (0, 1), (0, 1), (2, 1), (2, 1), (0, 1), (2, 1), (2, 1), (3, 3), (2, 1), (2, 1), (0, 1), (2, 1) };
            foreach (var (mine, theirs) in results) Play(s, new GameScore(theirs, mine));

            var l10 = s.MyRecentForm();
            Assert.That((l10.Wins, l10.Losses, l10.Draws, l10.Games), Is.EqualTo((7, 2, 1, 10)));
            Assert.That(l10.Text, Is.EqualTo("7승 2패 1무"));
            Assert.That(s.MyRecentForm(3).Text, Is.EqualTo("2승 1패"));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.MyRecentForm(0));
        }

        [Test]
        public void 경기가_10개_미만이면_치른_경기만_세고_보조_줄에_기준을_밝힌다()
        {
            var s = SeasonState.CreateDummy();
            Play(s, Mine(s, 6, 2));
            Play(s, Mine(s, 1, 4), Seed + 1);

            var l10 = s.MyRecentForm();
            Assert.That(l10.Text, Is.EqualTo("1승 1패"));
            var note = LobbyController.StatNoteText(s.MyStanding, l10);
            Assert.That(note, Does.StartWith($"{s.MyStanding.GamesPlayed}경기 · 경기당 득점 "));
            Assert.That(note, Does.EndWith("최근 전적 2경기 기준"));
            Assert.That(LobbyController.StatNoteText(new StandingRow(), l10), Is.Empty, "경기 0이면 빈 줄");
        }

        // ───────── 저장 ─────────

        [Test]
        public void 득실점은_저장했다_불러와도_그대로다()
        {
            string dir = Path.Combine(Path.GetTempPath(), "DailyFranchiseDashboardTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var saves = new SaveManager(Path.Combine(dir, SaveManager.FileName), _ => Assert.Fail("경고가 나면 안 된다"));
                var s = SeasonState.CreateDummy();
                var report = Play(s, Mine(s, 8, 3), Seed, saves);
                Assert.That(report.Saved, Is.True);

                var loaded = saves.LoadOrCreate(() => throw new AssertionException("새 시즌을 만들면 안 된다"), out _);
                Assert.That(loaded.Standings.Select(r => (r.Team, r.RunsScored, r.RunsAllowed)),
                    Is.EqualTo(s.Standings.Select(r => (r.Team, r.RunsScored, r.RunsAllowed))));
                Assert.That(loaded.MyRecentForm().Text, Is.EqualTo("1승 0패"), "최근 전적은 저장된 일정에서 다시 센다");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void 득실점이_없는_이전_저장_파일은_0에서_시작해_이어서_누적된다()
        {
            var s = SeasonState.CreateDummy();
            string legacy = Regex.Replace(SaveManager.ToJson(s), @",\s*""Runs(Scored|Allowed)"":\s*\d+", "");
            Assert.That(legacy, Does.Not.Contain("Runs"));

            var loaded = SaveManager.FromJson(legacy);
            Assert.That(loaded.Standings.All(r => r.RunsScored == 0 && r.RunsAllowed == 0), Is.True);
            Play(loaded, Mine(loaded, 4, 2));
            Assert.That((loaded.MyStanding.RunsScored, loaded.MyStanding.RunsAllowed), Is.EqualTo((4, 2)));
        }

        [Test]
        public void 음수_득실점_저장_파일은_손상으로_본다()
        {
            var data = SeasonState.CreateDummy().ToSaveData();
            data.Standings[0].RunsAllowed = -1;
            Assert.Throws<FormatException>(() => SeasonState.FromSaveData(data));
        }

        // ───────── 팀 내 기록 1위 ─────────

        static PlayerData Hitter(string id, int order, double avg, int hr, RosterRole role = RosterRole.Lineup) =>
            new PlayerData { Id = id, Name = id, Role = role, Order = order, Position = "RF", BattingAverage = avg, HomeRuns = hr, Ops = avg > 0 ? avg * 2.6 : 0 };

        static PlayerData Pitcher(string id, RosterRole role, int order, double era, int saves = 0) =>
            new PlayerData { Id = id, Name = id, Role = role, Order = order, Position = role == RosterRole.Starter ? "선발" : "불펜", Era = era, Saves = saves };

        static TeamRoster SampleRoster() => new TeamRoster("나",
            new[]
            {
                Hitter("타율왕", 1, .331, 8),
                Hitter("거포", 2, .262, 31),
                Hitter("무기록", 3, 0, 0),               // 타격 기록 없음 → 제외
                Hitter("동률거포", 4, .250, 31),          // 거포와 홈런 동률 → 타순 뒤라 밀림
            },
            new[] { Hitter("벤치강타", 1, .412, 40, RosterRole.Bench) }, // 벤치 → 제외
            new[] { Pitcher("에이스", RosterRole.Starter, 1, 2.91), Pitcher("5선발", RosterRole.Starter, 2, 4.80), Pitcher("미등판", RosterRole.Starter, 3, 0) },
            new[] { Pitcher("셋업", RosterRole.Bullpen, 1, 1.20), Pitcher("마무리", RosterRole.Bullpen, 2, 2.40, 25) });

        [Test]
        public void 타자_부문은_라인업_야수_중_기록이_있는_선수만_대상이다()
        {
            var roster = SampleRoster();
            Assert.That(RosterAnalyzer.QualifiedHitters(roster).Select(p => p.Id), Is.EqualTo(new[] { "타율왕", "거포", "동률거포" }));
            Assert.That(RosterAnalyzer.Leader(roster, LeaderCategory.BattingAverage).Player.Id, Is.EqualTo("타율왕"), "벤치 .412는 제외");
            Assert.That(RosterAnalyzer.Leader(roster, LeaderCategory.HomeRuns).Player.Id, Is.EqualTo("거포"), "벤치 40홈런 제외, 동률은 타순 앞");
        }

        [Test]
        public void 평균자책은_기록_있는_선발만_세이브는_불펜만_대상이다()
        {
            var roster = SampleRoster();
            Assert.That(RosterAnalyzer.Leader(roster, LeaderCategory.Era).Player.Id, Is.EqualTo("에이스"), "불펜 1.20·미등판 0.00 제외");
            Assert.That(RosterAnalyzer.Leader(roster, LeaderCategory.Saves).Player.Id, Is.EqualTo("마무리"));
        }

        [Test]
        public void 리더는_대시보드_순서로_나오고_표기는_부문별_형식을_따른다()
        {
            var leaders = RosterAnalyzer.Leaders(SampleRoster());
            Assert.That(leaders.Select(l => (l.CategoryText, l.Player.Name, l.ValueText)), Is.EqualTo(new[]
            {
                ("타율", "타율왕", ".331"), ("홈런", "거포", "31개"), ("평균자책", "에이스", "2.91"), ("세이브", "마무리", "25개"),
            }));
        }

        [Test]
        public void 자격_선수가_없는_부문은_빠진다()
        {
            var roster = new TeamRoster("나", new[] { Hitter("무기록", 1, 0, 0) }, new[] { Hitter("벤치", 1, .300, 5, RosterRole.Bench) },
                null, new[] { Pitcher("셋업", RosterRole.Bullpen, 1, 2.0) });
            Assert.That(RosterAnalyzer.Leader(roster, LeaderCategory.BattingAverage), Is.Null);
            Assert.That(RosterAnalyzer.Leaders(roster), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => RosterAnalyzer.Leader(null, LeaderCategory.HomeRuns));
        }

        [Test]
        public void 샘플_DB_내_구단_리더는_마스터_파일_기록과_같고_라인업을_바꾸면_다시_뽑힌다()
        {
            var db = MasterDatabase.LoadFromResources();
            var roster = db.GetRoster("부산 웨이브스");
            Assert.That(roster, Is.Not.Null);
            var leaders = RosterAnalyzer.Leaders(roster);
            Assert.That(leaders.Select(l => (l.CategoryText, l.Player.Name, l.ValueText)), Is.EqualTo(new[]
            {
                ("타율", "성지환", ".318"), ("홈런", "표준혁", "25개"), ("평균자책", "차은호", "3.55"), ("세이브", "석진영", "27개"),
            }));

            // 성지환(3번)을 벤치 소윤호(.296)와 바꾸면 타율 1위는 라인업에 남은 하윤재(.301).
            var card = LineupCard.FromRoster(roster);
            Assert.That(card.TrySwap(LineupSpot.Batting(3), LineupSpot.Bench(0)), Is.True);
            var swapped = card.ApplyTo(roster);
            Assert.That(RosterAnalyzer.Leader(swapped, LeaderCategory.BattingAverage).Player.Name, Is.EqualTo("하윤재"));
        }
    }
}
