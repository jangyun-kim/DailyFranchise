using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Resources 샘플 로스터 로딩과 MasterDatabase 검증.</summary>
    public class MasterDatabaseTests
    {
        const string Teams = "name,short_name,stadium\n가 팀,가,가 구장\n나 팀,나,나 구장\n";
        const string Header = "season,team,id,name,position,throws,bats,role,order,memo,avg,hr,ops,sb,era,w,l,sv,hld\n";

        [Test]
        public void Resources_샘플_로스터를_동기로_읽는다()
        {
            var warnings = new List<string>();
            var db = MasterDatabase.LoadFromResources(warnings.Add);
            Assert.That(warnings, Is.Empty);
            Assert.That(db.IsEmpty, Is.False);
            Assert.That(db.Season, Is.EqualTo(2026));
            Assert.That(db.Teams, Has.Count.EqualTo(10));
            Assert.That(db.Players.Count, Is.InRange(20, 30), "샘플 20~30행");
            Assert.That(db.Players.Select(p => p.Team).Distinct().Count(), Is.EqualTo(2), "2개 구단 분량");
            Assert.That(db.Players.Select(p => p.Id), Is.Unique);

            var busan = db.GetRoster("부산 웨이브스");
            Assert.That(busan.Lineup.Select(p => p.Order), Is.EqualTo(Enumerable.Range(1, 9)));
            Assert.That(busan.Bench, Has.Count.EqualTo(3));
            Assert.That(busan.Bullpen, Has.Count.EqualTo(3));
            Assert.That(busan.StartingPitcher.Name, Is.EqualTo("차은호"));
            var third = busan.AtOrder(3);
            Assert.That((third.Name, third.Position, third.Throws, third.Bats), Is.EqualTo(("성지환", "RF", Hand.Left, Hand.Left)));
            Assert.That((third.BattingAverage, third.HomeRuns, third.Ops), Is.EqualTo((0.318, 19, 0.889)));

            var seoul = db.GetRoster("서울 블루윙스");
            Assert.That(seoul.StartingPitcher.Era, Is.EqualTo(3.21));
            Assert.That(seoul.Lineup, Has.Count.EqualTo(9));

            Assert.That(db.GetRoster("인천 하버스"), Is.Null, "선수 데이터가 없는 구단");
            Assert.That(db.FindTeam("인천 하버스").ShortName, Is.EqualTo("인천"));
            Assert.That(db.FindPlayer("WV003").Name, Is.EqualTo("성지환"));
            Assert.That(db.FindPlayer("없음"), Is.Null);
        }

        [Test]
        public void 시즌_일정의_모든_구단이_구단_데이터에_있다()
        {
            var db = MasterDatabase.LoadFromResources();
            var season = SeasonState.CreateDummy();
            foreach (var team in season.Standings.Select(r => r.Team))
                Assert.That(db.FindTeam(team), Is.Not.Null, team);
            Assert.That(db.GetRoster(season.MyTeam), Is.Not.Null, "내 구단 선수단");
        }

        [Test]
        public void 빈_DB는_비어_있고_선수단이_없다()
        {
            var db = MasterDatabase.Empty;
            Assert.That(db.IsEmpty, Is.True);
            Assert.That(db.GetRoster("부산 웨이브스"), Is.Null);
        }

        [Test]
        public void 문자열_CSV에서_역할별로_순서대로_나눈다()
        {
            var db = MasterDatabase.FromCsv(Teams, Header +
                "2026,가 팀,p2,투수2,마무리,우,우,bullpen,2,,,,,,2.5,1,1,10,0\n" +
                "2026,가 팀,b2,타자2,SS,R,S,lineup,2,,.250,1,.700,0,,,,,\n" +
                "2026,가 팀,p1,투수1,셋업,L,L,BULLPEN,1,,,,,,3.0,0,0,0,5\n" +
                "2026,가 팀,b1,타자1,CF,R,L,lineup,1,빠름,.300,0,,30,,,,,\n");
            var r = db.GetRoster("가 팀");
            Assert.That(r.Lineup.Select(p => p.Id), Is.EqualTo(new[] { "b1", "b2" }));
            Assert.That(r.Bullpen.Select(p => p.Id), Is.EqualTo(new[] { "p1", "p2" }));
            Assert.That(r.Lineup[1].Bats, Is.EqualTo(Hand.Switch));
            Assert.That(r.Bullpen[1].Throws, Is.EqualTo(Hand.Right), "한글 우/좌 표기");
            Assert.That(r.Lineup[0].Memo, Is.EqualTo("빠름"));
            Assert.That(r.Starters, Is.Empty);
            Assert.That(r.StartingPitcher, Is.Null);
            Assert.That(db.GetRoster("나 팀"), Is.Null);
        }

        [TestCase("2026,없는 팀,x,이름,CF,R,R,lineup,1,,,,,,,,,,\n", "모르는 구단")]
        [TestCase("2026,가 팀,x,이름,CF,R,R,lineup,1,,,,,,,,,,\n2026,가 팀,x,이름2,SS,R,R,lineup,2,,,,,,,,,,\n", "ID 중복")]
        [TestCase("2026,가 팀,x,이름,CF,R,R,lineup,1,,,,,,,,,,\n2026,가 팀,y,이름2,SS,R,R,lineup,1,,,,,,,,,,\n", "타순 1 중복")]
        [TestCase("2026,가 팀,x,이름,CF,R,R,lineup,10,,,,,,,,,,\n", "타순은 1~9")]
        [TestCase("2026,가 팀,x,이름,CF,X,R,lineup,1,,,,,,,,,,\n", "R/L/S")]
        [TestCase("2026,가 팀,x,이름,CF,R,R,closer,1,,,,,,,,,,\n", "role")]
        [TestCase("2026,가 팀,x,,CF,R,R,lineup,1,,,,,,,,,,\n", "'name' 값이 비어")]
        [TestCase("2026,가 팀,x,이름,CF,R,R,lineup,1,,abc,,,,,,,,\n", "숫자가 아니다")]
        [TestCase("2026,가 팀,x,이름,CF,R,R,lineup,1,,,,,,,,,,\n2025,가 팀,y,이름2,SS,R,R,lineup,2,,,,,,,,,,\n", "시즌")]
        public void 내용이_잘못된_로스터는_행_번호가_담긴_FormatException(string rows, string message)
        {
            var e = Assert.Throws<FormatException>(() => MasterDatabase.FromCsv(Teams, Header + rows));
            Assert.That(e.Message, Does.Contain(message));
            Assert.That(e.Message, Does.Match(@"\d+행"));
        }
    }

    /// <summary>선수단 주입 시 장면 더미 이름표가 DB 선수로 바뀌는지 검증.</summary>
    public class ScenarioRosterSourceTests
    {
        static readonly MasterDatabase Db = MasterDatabase.LoadFromResources();
        static readonly string[] Ids = ScenarioCatalog.All.Select(e => e.Id).ToArray();

        static TeamRoster Busan => Db.GetRoster("부산 웨이브스");
        static TeamRoster Seoul => Db.GetRoster("서울 블루윙스");

        /// <summary>1경기: 서울(원정) vs 부산(홈, 내 구단). 두 구단 모두 선수단이 있다.</summary>
        static ScenarioRosterSource HomeGame(string scenario = ScenarioCatalog.EighthBottomTactics) =>
            new ScenarioRosterSource(new DummyScenarioData(scenario,
                Matchup.FromNames("서울 블루윙스", "부산 웨이브스", TeamSide.Home, Seoul, Busan)), "SampleRoster.csv");

        [Test]
        public void 더미_이름표가_DB_선수로_바뀌고_장면_기록은_그대로다()
        {
            var dummy = new DummyScenarioData(ScenarioCatalog.EighthBottomTactics);
            var source = HomeGame();

            var batter = source.GetPlayer(Fixture8thBottom.Batter1);
            var dummyBatter = dummy.GetPlayer(Fixture8thBottom.Batter1);
            Assert.That(dummyBatter.Name, Is.EqualTo("최도윤"));
            Assert.That(batter.Name, Is.EqualTo("성지환"), "최도윤 → 타순 3번 DB 선수");
            Assert.That(batter.Position, Is.EqualTo("RF"));
            Assert.That(batter.Handedness, Is.EqualTo("좌투좌타"));
            Assert.That(batter.SeasonLine, Is.EqualTo("타율 .318 · 19홈런 · OPS .889"));
            Assert.That(batter.Id, Is.EqualTo(Fixture8thBottom.Batter1), "장면 이벤트 ID는 유지");
            Assert.That((batter.Side, batter.TodayAtBats, batter.TodayHits, batter.TodayExtra, batter.Fitness),
                Is.EqualTo((dummyBatter.Side, dummyBatter.TodayAtBats, dummyBatter.TodayHits, dummyBatter.TodayExtra, dummyBatter.Fitness)));

            var pitcher = source.GetPlayer(Fixture8thBottom.Pitcher);
            var dummyPitcher = dummy.GetPlayer(Fixture8thBottom.Pitcher);
            Assert.That(pitcher.Name, Is.EqualTo("염태준"), "강민혁 → 서울 선발");
            Assert.That(pitcher.Handedness, Is.EqualTo("좌투"));
            Assert.That(pitcher.SeasonLine, Is.EqualTo("시즌 11승 6패 · 평균자책 3.21"));
            Assert.That((pitcher.TodayLine, pitcher.PitchesBeforeScene, pitcher.Fitness),
                Is.EqualTo((dummyPitcher.TodayLine, dummyPitcher.PitchesBeforeScene, dummyPitcher.Fitness)));

            Assert.That(source.SourceLabel, Is.EqualTo("SampleRoster.csv"));
            Assert.That(source.GetBoxSeed(TeamSide.Away), Is.SameAs(dummy.GetBoxSeed(TeamSide.Away)));
        }

        [Test]
        public void 좌측_선수단은_DB_타순_벤치_불펜_순서로_표시된다()
        {
            var source = HomeGame();
            var lineup = source.GetLineup(TeamSide.Home);
            Assert.That(lineup.Select(e => source.GetPlayer(e.PlayerId).Name), Is.EqualTo(Busan.Lineup.Select(p => p.Name)));
            Assert.That(lineup.Select(e => e.Role), Is.EqualTo(Busan.Lineup.Select(p => p.Position)));
            Assert.That(lineup.Select(e => e.Order), Is.EqualTo(Enumerable.Range(1, 9).Select(i => (int?)i)));

            var bench = source.GetBench(TeamSide.Home);
            Assert.That(bench.Select(e => source.GetPlayer(e.PlayerId).Name), Is.EqualTo(new[] { "소윤호", "피준서", "국태민" }));
            Assert.That(bench.Select(e => e.Note), Is.EqualTo(new[] { "좌/장타", "우/정교", "주력 상" }));

            var bullpen = source.GetBullpen(TeamSide.Home);
            Assert.That(bullpen.Select(e => source.GetPlayer(e.PlayerId).Name), Is.EqualTo(new[] { "석진영", "왕도현", "옥승민" }));
            Assert.That(bullpen.Select(e => e.Role), Is.EqualTo(new[] { "마무리", "좌완", "롱릴리프" }));
            Assert.That(source.GetPlayer("home_rp1").SeasonLine, Is.EqualTo("시즌 27세이브 · 평균자책 2.36"));
            Assert.That(source.GetPlayer("away_rp1").SeasonLine, Is.EqualTo("시즌 21홀드 · 평균자책 2.88"));

            Assert.That(source.GetPlayer(source.FeaturedPlayerId).Name, Is.EqualTo("성지환"));
        }

        [TestCaseSource(nameof(Ids))]
        public void 두_구단_선수단이_있으면_장면의_모든_선수가_DB_선수로_바뀐다(string id)
        {
            var source = HomeGame(id);
            var dummy = new DummyScenarioData(id);
            var f = ScenarioCatalog.Find(id).Create();
            var events = f.Branch == null ? f.Events
                : f.Branch.Tactics.SelectMany(t => f.Branch.PathFor(f.Events, t, TacticOutcome.Success)
                    .Concat(f.Branch[t].IsGamble ? f.Branch.PathFor(f.Events, t, TacticOutcome.Failure) : new List<PitchEvent>())).ToList();
            var ids = events.SelectMany(e => new[]
                {
                    e.PitcherId, e.BatterId,
                    e.RunnersBefore.First, e.RunnersBefore.Second, e.RunnersBefore.Third,
                    e.RunnersAfter.First, e.RunnersAfter.Second, e.RunnersAfter.Third,
                })
                .Concat(new[] { TeamSide.Home }.SelectMany(s => source.GetLineup(s).Concat(source.GetBench(s)).Concat(source.GetBullpen(s)))
                    .Select(e => e.PlayerId))
                .Where(x => x != null).Distinct();

            var dbNames = new HashSet<string>(Db.Players.Select(p => p.Name));
            foreach (var pid in ids)
            {
                Assert.That(source.Resolve(pid), Is.Not.Null, $"{id}: {pid} 자리에 DB 선수가 없음");
                Assert.That(dbNames, Does.Contain(source.GetPlayer(pid).Name), $"{id}: {pid}");
                Assert.That(source.GetPlayer(pid).Name, Is.Not.EqualTo(dummy.GetPlayer(pid).Name), $"{id}: {pid} 더미 이름 남음");
            }
        }

        [Test]
        public void 원정_경기에서도_내_선수는_DB_선수이고_선수단이_없는_상대는_더미_이름을_쓴다()
        {
            // 2경기: 부산(원정, 내 구단) vs 인천(홈, 샘플 선수단 없음)
            var source = new ScenarioRosterSource(new DummyScenarioData(ScenarioCatalog.EighthBottomTactics,
                Matchup.FromNames("부산 웨이브스", "인천 하버스", TeamSide.Away, Busan, Db.GetRoster("인천 하버스"))), "SampleRoster.csv");

            Assert.That(source.Mirrored, Is.True);
            var batter = source.GetPlayer(Fixture8thBottom.Batter1);
            Assert.That(batter.Name, Is.EqualTo("성지환"));
            Assert.That(batter.Side, Is.EqualTo(TeamSide.Away));
            Assert.That(source.GetLineup(TeamSide.Away).Count, Is.EqualTo(9));
            Assert.That(source.GetLineup(TeamSide.Home).Count, Is.EqualTo(0));

            var pitcher = source.GetPlayer(Fixture8thBottom.Pitcher);
            Assert.That(source.Resolve(Fixture8thBottom.Pitcher), Is.Null);
            Assert.That(pitcher.Name, Is.EqualTo("강민혁"), "상대 선수단이 없으면 더미");
            Assert.That(pitcher.Side, Is.EqualTo(TeamSide.Home));
        }

        [Test]
        public void 모르는_ID는_더미와_같이_ID를_이름으로_쓴다()
        {
            var source = HomeGame();
            Assert.That(source.Resolve("ghost"), Is.Null);
            Assert.That(source.GetPlayer("ghost").Name, Is.EqualTo("ghost"));
            Assert.That(source.Resolve("home_b99"), Is.Null);
            Assert.That(source.GetPlayer(null).Name, Is.EqualTo("—"));
        }

        [Test]
        public void 투타_표기와_시즌_기록_문구()
        {
            Assert.That(ScenarioRosterSource.HandednessText(new PlayerData { Role = RosterRole.Lineup, Throws = Hand.Right, Bats = Hand.Switch }),
                Is.EqualTo("우투양타"));
            Assert.That(ScenarioRosterSource.HandednessText(new PlayerData { Role = RosterRole.Bullpen, Throws = Hand.Left, Bats = Hand.Left }),
                Is.EqualTo("좌투"));
            Assert.That(ScenarioRosterSource.SeasonLineText(new PlayerData { Role = RosterRole.Lineup, BattingAverage = 0.301, StolenBases = 28 }),
                Is.EqualTo("타율 .301 · 28도루"), "0홈런·OPS 없음은 생략, 20도루 이상 표기");
            Assert.That(ScenarioRosterSource.SeasonLineText(new PlayerData { Role = RosterRole.Starter, Wins = 9, Losses = 7, Era = 3.5, Saves = 1 }),
                Is.EqualTo("시즌 9승 7패 · 평균자책 3.50"), "선발은 세이브가 있어도 승패");
        }
    }
}
