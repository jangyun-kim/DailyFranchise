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
    /// <summary>Resources 실제 KBO 로스터(Phase 26.5) 로딩과 MasterDatabase 검증.</summary>
    public class MasterDatabaseTests
    {
        const string Teams = "name,short_name,stadium\n가 팀,가,가 구장\n나 팀,나,나 구장\n";
        const string Header = "season,team,id,name,position,throws,bats,role,order,memo,avg,hr,ops,sb,era,w,l,sv,hld\n";

        [Test]
        public void Resources_실제_KBO_선수_데이터를_동기로_읽는다()
        {
            var warnings = new List<string>();
            var db = MasterDatabase.LoadFromResources(warnings.Add);
            Assert.That(warnings, Is.Empty);
            Assert.That(MasterDatabase.RosterFileName, Is.EqualTo("SampleRoster_Export.csv"));
            Assert.That(db.IsEmpty, Is.False);
            Assert.That(db.Season, Is.EqualTo(2024));
            Assert.That(db.Teams.Select(t => t.Name), Is.EqualTo(new[]
            {
                "KIA 타이거즈", "삼성 라이온즈", "LG 트윈스", "두산 베어스", "KT 위즈",
                "SSG 랜더스", "롯데 자이언츠", "한화 이글스", "NC 다이노스", "키움 히어로즈",
            }));
            Assert.That(db.Players.Count, Is.EqualTo(61), "파이썬 파이프라인 내보내기: 삼성 2024 전체 선수단");
            Assert.That(db.Players.Select(p => p.Team).Distinct(), Is.EqualTo(new[] { "삼성 라이온즈" }), "약칭 '삼성' → 정식 이름");
            Assert.That(db.Players.Select(p => p.Id), Is.Unique);

            var samsung = db.GetRoster("삼성 라이온즈");
            Assert.That(samsung.Lineup.Select(p => p.Name), Is.EqualTo(new[]
                { "구자욱", "김지찬", "김영웅", "이재현", "강민호", "박병호", "이성규", "류지혁", "맥키넌" }));
            Assert.That(samsung.Starters.Select(p => p.Name), Is.EqualTo(new[] { "코너", "원태인", "레예스", "이승현", "백정현" }));
            var lead = samsung.AtOrder(1);
            Assert.That((lead.Name, lead.Position, lead.Throws, lead.Bats), Is.EqualTo(("구자욱", "LF", Hand.Right, Hand.Left)));
            Assert.That((lead.BattingAverage, lead.HomeRuns, lead.Ops, lead.War), Is.EqualTo((0.343, 33, 1.044, 4.39)));
            Assert.That(samsung.StartingPitcher.Era, Is.EqualTo(3.43));

            Assert.That(db.GetRoster("LG 트윈스"), Is.Null, "선수 데이터가 아직 없는 구단");
            Assert.That(db.FindTeam("삼성 라이온즈").ShortName, Is.EqualTo("삼성"));
            Assert.That(db.FindPlayer("p_삼성_2024_구자욱").Name, Is.EqualTo("구자욱"));
            Assert.That(db.FindPlayer("WV003"), Is.Null, "가상 선수 데이터는 런타임에서 읽지 않는다");
            Assert.That(UnityEngine.Resources.Load<UnityEngine.TextAsset>("Database/SampleRoster_Master"), Is.Null,
                "가상 데이터 파일은 Resources에서 뺐다");
        }

        [Test]
        public void 실제_데이터는_1군_엔트리와_퓨처스로_나눈다()
        {
            var samsung = TestLeague.Real.GetRoster("삼성 라이온즈");
            // 1군: 선발 타순 9 · 벤치 앞 5 · 선발 5 · 불펜 앞 7 = 26, 나머지 35명은 퓨처스
            Assert.That((samsung.Lineup.Count, samsung.Bench.Count, samsung.Starters.Count, samsung.Bullpen.Count, samsung.Reserves.Count),
                Is.EqualTo((9, 5, 5, 7, 35)));
            Assert.That(RosterLimits.CountOf(samsung), Is.EqualTo(RosterLimits.MaxRoster));
            Assert.That(samsung.Bench.Select(p => p.Name), Is.EqualTo(new[] { "김헌곤", "김현준", "윤정빈", "이병헌", "안주형" }),
                "데이터 순서(출장 많은 순) 앞 5명");
            Assert.That(samsung.Bullpen.Select(p => p.Name), Is.EqualTo(new[] { "김재윤", "이승현", "임창민", "오승환", "김태훈", "이상민", "최지광" }));
            Assert.That(samsung.Reserves.Take(2).Select(p => p.Name), Is.EqualTo(new[] { "전병우", "디아즈" }), "벤치 6번째부터");
            Assert.That(samsung.Reserves.Count(p => p.IsPitcher), Is.EqualTo(16));
            Assert.That(RosterLimits.GameReadinessProblem(samsung), Is.Null);

            // 퓨처스는 라인업 카드·정원·페이롤에 들어가지 않는다
            Assert.That(LineupCard.FromRoster(samsung).Bench.Count, Is.EqualTo(5));
            Assert.That(StoveLeague.Payroll(samsung, null),
                Is.EqualTo(LeagueRosterBook.AllOf(samsung).Sum(p => FrontOfficeTrust.SalaryOf(p.War))));
            Assert.That(LeagueRosterBook.AllOf(samsung).Count(), Is.EqualTo(26));
        }

        [Test]
        public void 다구단_검증용_테스트_선수단은_2개_구단이다()
        {
            var db = TestLeague.TwoTeams;
            Assert.That(db.Players.Count, Is.EqualTo(35));
            Assert.That(db.Players.Select(p => p.Team).Distinct(), Is.EquivalentTo(new[] { "삼성 라이온즈", "LG 트윈스" }));
            Assert.That(db.GetRoster("삼성 라이온즈").Reserves, Is.Empty, "벤치 3 · 불펜 3이라 퓨처스 없음");
            Assert.That(db.GetRoster("SSG 랜더스"), Is.Null);
        }

        [Test]
        public void 선수_행의_구단은_정식_이름이나_약칭으로_적는다()
        {
            const string teams = "name,short_name,stadium\n가 구단,가,가 구장\n나 구단,나,나 구장\n다 구단,다,다 구장\n라 구단,다,라 구장\n";
            var db = MasterDatabase.FromCsv(teams, Header +
                "2026,가,a1,타자1,CF,R,L,lineup,1,,,,,,,,,,\n" +
                "2026,나 구단,b1,타자2,SS,R,R,lineup,1,,,,,,,,,,\n");
            Assert.That(db.FindPlayer("a1").Team, Is.EqualTo("가 구단"));
            Assert.That(db.GetRoster("가 구단").Lineup.Single().Id, Is.EqualTo("a1"));
            Assert.That(db.FindPlayer("b1").Team, Is.EqualTo("나 구단"));

            var e = Assert.Throws<FormatException>(() => MasterDatabase.FromCsv(teams, Header + "2026,다,x,이름,CF,R,R,lineup,1,,,,,,,,,,\n"));
            Assert.That(e.Message, Does.Contain("여러 구단에 겹친다"));
        }

        [Test]
        public void 시즌_구장은_구단_데이터와_같다()
        {
            foreach (var team in TestLeague.Real.Teams)
                Assert.That(SeasonState.KboStadiums[team.Name], Is.EqualTo(team.Stadium), team.Name);
            Assert.That(SeasonState.KboStadiums.Count, Is.EqualTo(TestLeague.Real.Teams.Count));
            var season = SeasonState.CreateDummy();
            Assert.That(season.Schedule[0].Stadium, Is.EqualTo("대구 라이온즈파크"), "1경기 LG @ 삼성");
            Assert.That(season.Schedule.All(g => g.Stadium == SeasonState.KboStadiums[g.HomeTeam]), Is.True);
        }

        [Test]
        public void 시즌_일정의_모든_구단이_구단_데이터에_있다()
        {
            var db = TestLeague.Real;
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
            Assert.That(db.GetRoster("삼성 라이온즈"), Is.Null);
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
        static readonly MasterDatabase Db = TestLeague.TwoTeams;
        static readonly string[] Ids = ScenarioCatalog.All.Select(e => e.Id).ToArray();

        static TeamRoster Busan => Db.GetRoster("삼성 라이온즈");
        static TeamRoster Seoul => Db.GetRoster("LG 트윈스");

        /// <summary>1경기: 서울(원정) vs 부산(홈, 내 구단). 두 구단 모두 선수단이 있다.</summary>
        static ScenarioRosterSource HomeGame(string scenario = ScenarioCatalog.EighthBottomTactics) =>
            new ScenarioRosterSource(new DummyScenarioData(scenario,
                Matchup.FromNames("LG 트윈스", "삼성 라이온즈", TeamSide.Home, Seoul, Busan)), "SampleRoster.csv");

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
        public void 원정_경기에서도_내_선수는_DB_선수이고_선수단이_없는_상대는_이름을_지어내지_않는다()
        {
            // 2경기: 부산(원정, 내 구단) vs 인천(홈, 샘플 선수단 없음)
            var source = new ScenarioRosterSource(new DummyScenarioData(ScenarioCatalog.EighthBottomTactics,
                Matchup.FromNames("삼성 라이온즈", "SSG 랜더스", TeamSide.Away, Busan, Db.GetRoster("SSG 랜더스"))), "SampleRoster.csv");

            Assert.That(source.Mirrored, Is.True);
            var batter = source.GetPlayer(Fixture8thBottom.Batter1);
            Assert.That(batter.Name, Is.EqualTo("성지환"));
            Assert.That(batter.Side, Is.EqualTo(TeamSide.Away));
            Assert.That(source.GetLineup(TeamSide.Away).Count, Is.EqualTo(9));
            Assert.That(source.GetLineup(TeamSide.Home).Count, Is.EqualTo(0));

            var pitcher = source.GetPlayer(Fixture8thBottom.Pitcher);
            Assert.That(source.Resolve(Fixture8thBottom.Pitcher), Is.Null);
            // Phase 26.5: 상대 선수단이 없으면 가상 이름 대신 구단 약칭 + 자리, 기록은 리그 평균 안내
            Assert.That(pitcher.Name, Is.EqualTo("SSG 선발 투수"));
            Assert.That(pitcher.SeasonLine, Is.EqualTo(ScenarioRosterSource.NoDataLine));
            Assert.That(pitcher.Handedness, Is.EqualTo("—"));
            Assert.That(pitcher.Side, Is.EqualTo(TeamSide.Home));
            Assert.That(pitcher.PitchesBeforeScene, Is.EqualTo(new DummyScenarioData(ScenarioCatalog.EighthBottomTactics)
                .GetPlayer(Fixture8thBottom.Pitcher).PitchesBeforeScene), "장면의 투구 수는 그대로");
            Assert.That(source.GetPlayer("away_b4").Name, Is.EqualTo("SSG 4번 타자"));
            Assert.That(source.GetPlayer("away_rp2").Name, Is.EqualTo("SSG 불펜 2"));
            Assert.That(ScenarioRosterSource.SlotLabel("home_ph1"), Is.EqualTo("대타 1"));
            Assert.That(ScenarioRosterSource.SlotLabel("home_pr1"), Is.EqualTo("대주자 1"));
            Assert.That(ScenarioRosterSource.SlotLabel("ghost"), Is.Null);
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
