using System.Collections.Generic;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>경기 결과 반영과 순위 재정렬. 타 구단 결과 난수는 고정 Seed만 쓴다.</summary>
    public class SeasonStandingsTests
    {
        const int Seed = 20261014;

        static int Total(SeasonState s, System.Func<StandingRow, int> f) => s.Standings.Sum(f);

        [Test]
        public void 홈_승리는_내_구단_승_원정팀_패로_반영된다()
        {
            var s = SeasonState.CreateDummy(); // 1경기: 서울 블루윙스(원정) vs 부산 웨이브스(홈)
            var me = s.MyStanding;
            var opp = s.StandingOf("서울 블루윙스");
            var (w, l, oppW, oppL) = (me.Wins, me.Losses, opp.Wins, opp.Losses);

            s.StartNextGame();
            s.FinishCurrentGame(new GameScore(2, 3), new SeededRandom(Seed));

            Assert.That((me.Wins, me.Losses), Is.EqualTo((w + 1, l)));
            Assert.That((opp.Wins, opp.Losses), Is.EqualTo((oppW, oppL + 1)));
            Assert.That(s.Schedule[0].Score, Is.EqualTo(new GameScore(2, 3)));
            Assert.That(s.MyOutcome(s.Schedule[0]), Is.EqualTo(GameOutcome.Win));
            Assert.That(s.LastFinished, Is.SameAs(s.Schedule[0]));
        }

        [Test]
        public void 원정_경기_점수는_일정의_원정_홈_기준으로_판정한다()
        {
            var s = SeasonState.CreateDummy();
            s.StartNextGame();
            s.FinishCurrentGame(new GameScore(0, 1), new SeededRandom(Seed));
            var g2 = s.StartNextGame(); // 2경기: 부산(원정) vs 인천(홈)
            Assert.That(g2.AwayTeam, Is.EqualTo(s.MyTeam));
            int losses = s.MyStanding.Losses;
            s.FinishCurrentGame(new GameScore(2, 3), new SeededRandom(Seed));
            Assert.That(s.MyOutcome(g2), Is.EqualTo(GameOutcome.Loss));
            Assert.That(s.MyStanding.Losses, Is.EqualTo(losses + 1));
        }

        [Test]
        public void 내_구단_기준_점수는_일정의_원정_홈_순서로_바뀐다()
        {
            var s = SeasonState.CreateDummy();
            s.StartNextGame(); // 부산 홈
            Assert.That(s.ScheduleScore(5, 1), Is.EqualTo(new GameScore(1, 5)));
            s.FinishCurrentGame(new GameScore(1, 5), new SeededRandom(Seed));
            s.StartNextGame(); // 부산 원정
            Assert.That(s.ScheduleScore(5, 1), Is.EqualTo(new GameScore(5, 1)));
        }

        [Test]
        public void 무승부는_승률에서_빠진다()
        {
            var s = SeasonState.CreateDummy();
            var me = s.MyStanding;
            double pct = me.WinPct;
            int draws = me.Draws;
            s.StartNextGame();
            s.FinishCurrentGame(new GameScore(3, 3), new SeededRandom(Seed));
            Assert.That(me.Draws, Is.EqualTo(draws + 1));
            Assert.That(me.WinPct, Is.EqualTo(pct));
            Assert.That(s.MyOutcome(s.Schedule[0]), Is.EqualTo(GameOutcome.Draw));
            Assert.That(new StandingRow().WinPct, Is.Zero, "경기 수 0이면 0");
            Assert.That(new StandingRow { Wins = 23, Losses = 18 }.WinPctText, Is.EqualTo("0.561"));
        }

        [Test]
        public void 하루에_모든_구단이_한_경기씩_치르고_승패_합이_맞는다()
        {
            var s = SeasonState.CreateDummy();
            int games = Total(s, r => r.Wins + r.Losses + r.Draws);
            int wins = Total(s, r => r.Wins), losses = Total(s, r => r.Losses);

            s.StartNextGame();
            s.FinishCurrentGame(new GameScore(5, 1), new SeededRandom(Seed));

            Assert.That(s.LastDayResults, Has.Count.EqualTo(5), "10개 구단 → 5경기");
            var teams = s.LastDayResults.SelectMany(r => new[] { r.AwayTeam, r.HomeTeam }).ToList();
            Assert.That(teams, Is.Unique);
            Assert.That(teams, Is.EquivalentTo(s.Standings.Select(r => r.Team)));
            Assert.That(Total(s, r => r.Wins + r.Losses + r.Draws), Is.EqualTo(games + 10));
            Assert.That(Total(s, r => r.Wins) - wins, Is.EqualTo(Total(s, r => r.Losses) - losses), "승 합 = 패 합");
            Assert.That(s.LastDayResults[0].HomeTeam, Is.EqualTo(s.MyTeam), "첫 항목은 내 경기");
        }

        [Test]
        public void 같은_Seed면_타_구단_결과와_순위가_같다()
        {
            List<(string, int, int, int)> Run(int seed)
            {
                var s = SeasonState.CreateDummy();
                for (int i = 0; i < 5; i++)
                {
                    s.StartNextGame();
                    s.FinishCurrentGame(new GameScore(3, 2), new SeededRandom(seed + i));
                }
                return s.Standings.Select(r => (r.Team, r.Rank, r.Wins, r.Losses)).ToList();
            }

            Assert.That(Run(Seed), Is.EqualTo(Run(Seed)));
            Assert.That(Run(Seed), Is.Not.EqualTo(Run(Seed + 100)), "다른 Seed면 결과가 달라진다 (이 Seed 조합에서 확인)");
        }

        [Test]
        public void 순위는_승률_다승_무승부_순으로_정렬하고_모두_같으면_공동_순위()
        {
            var standings = new List<StandingRow>
            {
                new StandingRow { Team = "A", Wins = 10, Losses = 10 },          // .500, 10승 0무
                new StandingRow { Team = "B", Wins = 12, Losses = 8 },           // .600
                new StandingRow { Team = "C", Wins = 15, Losses = 15, Draws = 2 }, // .500, 15승 → A보다 위
                new StandingRow { Team = "D", Wins = 10, Losses = 10, Draws = 1 }, // A와 승률·다승 같고 무승부 많음 → A보다 위
                new StandingRow { Team = "F", Wins = 10, Losses = 10 },          // A와 완전히 같음 → 공동
                new StandingRow { Team = "E", Wins = 0, Losses = 0 },
            };
            var s = new SeasonState("t", "A", new List<ScheduledGame> { new ScheduledGame { Number = 1, AwayTeam = "A", HomeTeam = "B" } }, standings);

            Assert.That(s.Standings.Select(r => r.Team), Is.EqualTo(new[] { "B", "C", "D", "A", "F", "E" }));
            Assert.That(s.Standings.Select(r => r.Rank), Is.EqualTo(new[] { 1, 2, 3, 4, 4, 6 }));
            // 승차: B 12-8 기준, C 15-15 → ((12-15) + (15-8)) / 2 = 2.0
            Assert.That(s.Standings[1].GamesBehind, Is.EqualTo("2.0"));
        }

        [Test]
        public void 패배로_승률이_떨어지면_순위가_내려간다()
        {
            var standings = new List<StandingRow>
            {
                new StandingRow { Team = "나", Wins = 11, Losses = 9 },  // .550
                new StandingRow { Team = "상대", Wins = 10, Losses = 9 }, // .526
                new StandingRow { Team = "쉬는팀", Wins = 9, Losses = 10 },
            };
            var schedule = new List<ScheduledGame> { new ScheduledGame { Number = 1, AwayTeam = "상대", HomeTeam = "나" } };
            var s = new SeasonState("t", "나", schedule, standings);
            Assert.That(s.MyStanding.Rank, Is.EqualTo(1));

            s.StartNextGame();
            s.FinishCurrentGame(new GameScore(4, 1), new ScriptedRandom(0.5));

            // 나 11-10 .524, 상대 11-9 .550 → 역전
            Assert.That(s.Standings.Select(r => r.Team).Take(2), Is.EqualTo(new[] { "상대", "나" }));
            Assert.That(s.MyStanding.Rank, Is.EqualTo(2));
            Assert.That(s.Standings[1].GamesBehind, Is.EqualTo("0.5"));
            Assert.That(s.LastDayResults, Has.Count.EqualTo(1), "남은 팀이 한 팀이면 그 팀은 쉰다");
            Assert.That(s.StandingOf("쉬는팀").Wins + s.StandingOf("쉬는팀").Losses, Is.EqualTo(19));
        }
    }
}
