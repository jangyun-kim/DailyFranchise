using System;
using System.Linq;
using DailyFranchise.App.Data;
using DailyFranchise.Sim.Data;
using DailyFranchise.UI.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 18: 파이썬 마스터 CSV 형식(확장 세이버메트릭스 열) 파싱과 안전 폴백, 카드 표시 문구.</summary>
    public class MasterCsvTests
    {
        const string Teams = "name,short_name,stadium\n가 팀,가,가 구장\n";

        /// <summary>kbo_stats_injector.py가 만드는 마스터 CSV 헤더 그대로 (56열).</summary>
        const string MasterHeader =
            "Year,Team,Name,Age,Pos.,Back Num.,G,oWAR,dWAR,PA,ePA,AB,R,H,2B,3B,HR,TB,RBI,SB,CS,BB,HP,IB,SO,GDP,SH,SF," +
            "AVG,OBP,SLG,OPS,R/ePA,wRC+,WAR,GS,GR,GF,CG,SHO,W,L,S,HD,IP,ER,rRA,TBF,ROE,BK,WP,ERA,RA9,rRA9,FIP,WHIP\n";

        const string Hitter =
            "2026,가 팀,타자일,29,외야수,24,133,4.1,0.3,571.0,560.5,480,80,163,37,3,30,296,118,4,3,76,5,15,88,8,0,10," +
            "0.34,0.427,0.617,1.044,0.181,172.4,4.45,0,0,0,0,0,0,0,0,0,0.0,0,0.0,0,0,0,0,0.0,0.0,0.0,0.0,0.0\n";

        const string Pitcher =
            "2026,가 팀,투수일,31,투수,47,27,0.0,0.0,0.0,0.0,0,61,158,30,2,14,0,0,0,0,45,6,2,141,0,0,0," +
            "0.0,0.0,0.0,0.0,0.0,0.0,3.87,27,0,0,1,1,12,8,0,0,165.2,57,25.3,690,3,1,5,3.10,3.31,1.62,3.48,1.23\n";

        const string Closer =
            "2026,가 팀,투수이,27,투수,1,61,0.0,0.0,0.0,0.0,0,15,48,0,0,3,0,0,0,0,18,1,0,70,0,0,0," +
            "0.0,0.0,0.0,0.0,0.0,0.0,2.10,0,61,52,0,0,3,2,33,4,63.1,13,0,250,0,0,2,1.85,2.13,0,2.61,1.04\n";

        [Test]
        public void 마스터_CSV_헤더의_56개_열을_모두_PlayerData에_파싱한다()
        {
            var db = MasterDatabase.FromCsv(Teams, MasterHeader + Hitter + Pitcher);
            Assert.That(CsvReader.Parse(MasterHeader + Hitter)[0].Column("WHIP"), Is.EqualTo("WHIP"));
            Assert.That(db.Season, Is.EqualTo(2026), "Year 열 = 시즌");

            var h = db.Players.Single(p => p.Name == "타자일");
            Assert.That((h.Year, h.Age, h.Number, h.Position), Is.EqualTo((2026, 29, 24, "외야수")), "Pos.·Back Num. 별칭");
            Assert.That((h.Games, h.PlateAppearances, h.AtBats, h.Runs, h.Hits, h.Doubles, h.Triples, h.HomeRuns, h.TotalBases),
                Is.EqualTo((133, 571, 480, 80, 163, 37, 3, 30, 296)), "PA 571.0 같은 소수 표기 정수도 받는다");
            Assert.That((h.RunsBattedIn, h.StolenBases, h.CaughtStealing, h.Walks, h.HitByPitch, h.IntentionalWalks, h.Strikeouts),
                Is.EqualTo((118, 4, 3, 76, 5, 15, 88)));
            Assert.That((h.GroundIntoDoublePlays, h.SacrificeHits, h.SacrificeFlies), Is.EqualTo((8, 0, 10)));
            Assert.That(h.BattingAverage, Is.EqualTo(0.34));
            Assert.That(h.OnBasePercentage, Is.EqualTo(0.427));
            Assert.That(h.RecordedSlugging, Is.EqualTo(0.617));
            Assert.That(h.Ops, Is.EqualTo(1.044));
            Assert.That(h.SluggingPercentage, Is.EqualTo(1.044 - 0.427).Within(1e-9), "확률 계산용 장타율 공식은 그대로");
            Assert.That(h.EffectivePlateAppearances, Is.EqualTo(560.5));
            Assert.That(h.RunsPerEffectivePa, Is.EqualTo(0.181));
            Assert.That(h.WrcPlus, Is.EqualTo(172.4));
            Assert.That((h.OffensiveWar, h.DefensiveWar, h.War), Is.EqualTo((4.1, 0.3, 4.45)));
            Assert.That((h.HitsAllowed, h.RunsAllowed, h.PitcherStrikeouts), Is.EqualTo((0, 0, 0)), "타자 행 공용 열은 타격 기록");

            var p = db.Players.Single(x => x.Name == "투수일");
            Assert.That((p.Games, p.GamesStarted, p.ReliefGames, p.GamesFinished, p.CompleteGames, p.Shutouts),
                Is.EqualTo((27, 27, 0, 0, 1, 1)));
            Assert.That((p.Wins, p.Losses, p.Saves, p.Holds), Is.EqualTo((12, 8, 0, 0)), "S·HD 별칭");
            Assert.That(p.InningsPitched, Is.EqualTo(165.2));
            Assert.That((p.EarnedRuns, p.RunsAllowed, p.HitsAllowed, p.HomeRunsAllowed, p.WalksAllowed, p.PitcherStrikeouts),
                Is.EqualTo((57, 61, 158, 14, 45, 141)), "투수 행 R·H·HR·BB·SO = 허용 기록");
            Assert.That((p.HomeRuns, p.Hits, p.Strikeouts), Is.EqualTo((0, 0, 0)), "투수 허용 기록이 타격 기록(팀 내 홈런 1위 등)에 섞이지 않는다");
            Assert.That((p.BattersFaced, p.ReachedOnError, p.Balks, p.WildPitches), Is.EqualTo((690, 3, 1, 5)));
            Assert.That((p.Era, p.RunsPer9, p.ReplacementRunsAllowed, p.ReplacementRunsPer9), Is.EqualTo((3.10, 3.31, 25.3, 1.62)));
            Assert.That((p.Fip, p.Whip, p.War), Is.EqualTo((3.48, 1.23, 3.87)));
        }

        [Test]
        public void 게임_전용_열이_없으면_ID_투타_역할_순서를_추정한다()
        {
            var db = MasterDatabase.FromCsv(Teams, MasterHeader + Hitter + Pitcher + Closer);
            var h = db.Players.Single(p => p.Name == "타자일");
            Assert.That(h.Id, Is.EqualTo("가 팀-2026-타자일"));
            Assert.That((h.Throws, h.Bats), Is.EqualTo((Hand.Right, Hand.Right)), "투타 정보 없음 → 우");
            Assert.That((h.Role, h.Order), Is.EqualTo((RosterRole.Bench, 1)), "타순 미정 야수 → 벤치");

            var r = db.GetRoster("가 팀");
            Assert.That(r.StartingPitcher.Name, Is.EqualTo("투수일"), "선발 등판 절반 이상 → 선발");
            Assert.That(r.Bullpen.Select(p => (p.Name, p.Order)), Is.EqualTo(new[] { ("투수이", 1) }));
            Assert.That(r.Lineup, Is.Empty);
        }

        [Test]
        public void 확장_기록의_빈칸_하이픈_깨진_값_누락_열은_0으로_폴백한다()
        {
            const string header = "Year,Team,Name,Pos.,PA,AB,wRC+,WAR,FIP,WHIP,IP,Back Num.,Age\n";
            var db = MasterDatabase.FromCsv(Teams, header +
                "2026,가 팀,빈칸,1B,,,,,,,,,\n" +
                "2026,가 팀,하이픈,2B,-,-,-,-,-,-,-,-,-\n" +
                "2026,가 팀,깨짐,SS,abc,1e999,NaN,x,∞,?,1/3,No.7,스물\n");
            foreach (var p in db.Players)
            {
                Assert.That((p.PlateAppearances, p.AtBats, p.Number, p.Age), Is.EqualTo((0, 0, 0, 0)), p.Name);
                Assert.That((p.WrcPlus, p.War, p.Fip, p.Whip, p.InningsPitched), Is.EqualTo((0.0, 0.0, 0.0, 0.0, 0.0)), p.Name);
                Assert.That((p.GamesStarted, p.Doubles, p.RunsBattedIn, p.EarnedRuns), Is.EqualTo((0, 0, 0, 0)), $"{p.Name}: 없는 열");
                Assert.That(p.Role, Is.EqualTo(RosterRole.Bench));
            }
            Assert.That(db.Players.Select(p => p.Order), Is.EqualTo(new[] { 1, 2, 3 }), "역할 안 순서는 등장 순");
        }

        [Test]
        public void SafeInt_SafeDouble은_첫_후보_열을_읽고_실패하면_0()
        {
            var row = CsvReader.Parse("Back Num.,ip,bad,big,neg\n7,152.2,-,3e10,-1.6").Single();
            Assert.That(row.Column("number", "Back Num."), Is.EqualTo("Back Num."));
            Assert.That(row.Column("없음1", "없음2"), Is.Null);
            Assert.That(row.SafeInt("number", "Back Num."), Is.EqualTo(7));
            Assert.That(row.SafeDouble("IP"), Is.EqualTo(152.2), "대소문자 무시");
            Assert.That(row.SafeInt("ip"), Is.EqualTo(152));
            Assert.That(row.SafeInt("bad"), Is.EqualTo(0));
            Assert.That(row.SafeInt("big"), Is.EqualTo(int.MaxValue), "범위 초과는 잘라낸다");
            Assert.That(row.SafeInt("neg"), Is.EqualTo(-2));
            Assert.That(row.SafeDouble("없음"), Is.EqualTo(0));
            Assert.Throws<FormatException>(() => row.Double("bad"), "기존 엄격 읽기는 그대로");
        }

        [Test]
        public void 확률_계산용_기존_열은_마스터_형식에서도_엄격하다()
        {
            var bad = Hitter.Replace("0.34,0.427", "abc,0.427");
            var e = Assert.Throws<FormatException>(() => MasterDatabase.FromCsv(Teams, MasterHeader + bad));
            Assert.That(e.Message, Does.Contain("숫자가 아니다").And.Contain("2행"));
        }

        [Test]
        public void Resources_샘플은_마스터_형식이며_확장_기록을_담는다()
        {
            var text = UnityEngine.Resources.Load<UnityEngine.TextAsset>(MasterDatabase.RosterResource).text;
            var header = CsvReader.Parse(text)[0];
            foreach (var column in MasterHeader.Trim().Split(','))
                Assert.That(header.Has(column), column);

            var db = TestLeague.TwoTeams;
            var batter = db.FindPlayer("WV003");
            Assert.That((batter.Name, batter.Number, batter.Age), Is.EqualTo(("성지환", 84, 34)));
            Assert.That((batter.WrcPlus, batter.War, batter.Doubles, batter.RunsBattedIn), Is.EqualTo((140.0, 1.66, 26, 90)));
            Assert.That((batter.BattingAverage, batter.HomeRuns, batter.Ops), Is.EqualTo((0.318, 19, 0.889)), "기존 값 보존");

            var pitcher = db.FindPlayer("WV013");
            Assert.That((pitcher.Whip, pitcher.Fip, pitcher.InningsPitched, pitcher.Era), Is.EqualTo((1.38, 3.94, 150.1, 3.55)));
            Assert.That((pitcher.HomeRunsAllowed, pitcher.HomeRuns, pitcher.HomeRunsPer9), Is.EqualTo((15, 0, 0.92)));

            foreach (var p in db.Players)
            {
                Assert.That(p.Number, Is.InRange(1, 99), p.Name);
                Assert.That(p.Age, Is.InRange(18, 45), p.Name);
                if (p.IsPitcher) Assert.That((p.Whip > 0, p.Fip > 0), Is.EqualTo((true, true)), p.Name);
                else Assert.That((p.WrcPlus > 0, p.PlateAppearances > 0), Is.EqualTo((true, true)), p.Name);
            }
        }

        [Test]
        public void 라인업_카드를_적용해도_확장_기록이_유지된다()
        {
            var roster = TestLeague.TwoTeams.GetRoster("삼성 라이온즈");
            var card = LineupCard.FromRoster(roster);
            Assert.That(card.TrySwap(LineupSpot.Batting(3), LineupSpot.Bench(0)), Is.True);
            var applied = card.ApplyTo(roster);

            var moved = applied.Bench.Single(p => p.Id == "WV003");
            Assert.That((moved.Role, moved.WrcPlus, moved.War, moved.Number, moved.Age), Is.EqualTo((RosterRole.Bench, 140.0, 1.66, 84, 34)));
            Assert.That(applied.AtOrder(3).Id, Is.EqualTo("WV010"));
            Assert.That(roster.AtOrder(3).Role, Is.EqualTo(RosterRole.Lineup), "원본은 그대로");
        }

        [Test]
        public void 카드_문구_등번호_나이_핵심_지표_승패세홀()
        {
            var batter = new PlayerData { Role = RosterRole.Lineup, Number = 24, Age = 29, WrcPlus = 142.6, War = 4.3 };
            Assert.That(ScenarioRosterSource.ProfileText(batter), Is.EqualTo("No.24 · 29세"));
            Assert.That(ScenarioRosterSource.KeyStatsOf(batter).Select(s => s.ToString()), Is.EqualTo(new[] { "wRC+ 143", "WAR 4.30" }));

            var pitcher = new PlayerData { Role = RosterRole.Starter, Age = 31, Whip = 1.234, Fip = 3.5, Wins = 9, Losses = 7, Saves = 1, Holds = 2 };
            Assert.That(ScenarioRosterSource.ProfileText(pitcher), Is.EqualTo("31세"), "등번호 0 = 기록 없음");
            Assert.That(ScenarioRosterSource.KeyStatsOf(pitcher).Select(s => s.ToString()), Is.EqualTo(new[] { "WHIP 1.23", "FIP 3.50" }));
            Assert.That(ScenarioRosterSource.RecordLineText(pitcher), Is.EqualTo("9승 7패 · 1세이브 · 2홀드"));

            var empty = new PlayerData { Role = RosterRole.Bullpen };
            Assert.That(ScenarioRosterSource.ProfileText(empty), Is.Null);
            Assert.That(ScenarioRosterSource.KeyStatsOf(empty).Select(s => s.Value), Is.EqualTo(new[] { "—", "—" }), "폴백 0 = 기록 없음");
        }

        [Test]
        public void 경기_화면_선수_정보에_확장_지표가_실린다()
        {
            var db = TestLeague.TwoTeams;
            var source = new ScenarioRosterSource(new DummyScenarioData(DailyFranchise.Sim.Fixtures.ScenarioCatalog.EighthBottomTactics,
                Matchup.FromNames("LG 트윈스", "삼성 라이온즈", TeamSide.Home, db.GetRoster("LG 트윈스"), db.GetRoster("삼성 라이온즈"))),
                MasterDatabase.RosterFileName);

            var batter = source.GetPlayer("home_b3");
            Assert.That(batter.Profile, Is.EqualTo("No.84 · 34세"));
            Assert.That(batter.KeyStats.Select(s => s.ToString()), Is.EqualTo(new[] { "wRC+ 140", "WAR 1.66" }));
            Assert.That(batter.RecordLine, Is.Null);

            var pitcher = source.GetPlayer("home_sp");
            Assert.That(pitcher.KeyStats.Select(s => s.ToString()), Is.EqualTo(new[] { "WHIP 1.38", "FIP 3.94" }));
            Assert.That(pitcher.RecordLine, Is.EqualTo("9승 7패 · 0세이브 · 0홀드"));

            var dummy = new DummyScenarioData(DailyFranchise.Sim.Fixtures.ScenarioCatalog.EighthBottomTactics).GetPlayer("home_b3");
            Assert.That(dummy.Profile, Is.Null, "더미 선수는 확장 지표 없음");
            Assert.That(dummy.KeyStats, Is.Null);
            Assert.That(dummy.RecordLine, Is.Null);
        }
    }
}
