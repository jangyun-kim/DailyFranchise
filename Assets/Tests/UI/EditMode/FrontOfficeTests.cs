using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 42: 수뇌부 신뢰도 TP 적립(3연승·한국시리즈 진출·우승)·[수뇌부 설득] 캡 초과 1회 영입, 팬덤 레벨(100% 도달 → 보상·게이지 60%),
    /// 라이벌 승리 팬 보너스, 저장.
    /// </summary>
    public class FrontOfficeTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;
        const string Samsung = TestLeague.Samsung;
        const int Seed = 4242;

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Real, s.Signings, s.Progression, s.Rookies);

        static PostGameReport Play(SeasonState s, int my, int opp)
        {
            s.StartNextGame();
            return PostGameProcessor.Process(s, s.ScheduleScore(my, opp), new SeededRandom(Seed), null);
        }

        /// <summary>내 구단 벤치 한 자리를 비우고, 비싼 타 구단 타자 한 명을 자유계약으로 보낸 시즌. 캡 여유는 0.</summary>
        static (SeasonState s, FreeAgent star) CapBoundMarket()
        {
            var s = SeasonState.CreateDummy();
            var book = Book(s);
            var bench = book.Get(Samsung).Bench.Last();
            s.AddSigning(new SigningRecord(bench.Id, Samsung, LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            var star = book.Get("KIA 타이거즈").Lineup.OrderByDescending(p => p.War).First(); // 김도영
            s.AddSigning(new SigningRecord(star.Id, "KIA 타이거즈", LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            book = Book(s);
            s.SetPayroll(StoveLeague.Payroll(book.Get(Samsung), s.Signings));
            s.SetSalaryCapBase(s.Payroll); // 캡 여유 0
            var agent = StoveLeague.Market(book, Samsung, FanMood.Normal).Single(a => a.Id == star.Id);
            return (s, agent);
        }

        // ---------- 수뇌부 설득 ----------

        [Test]
        public void 캡에만_막힌_FA는_TP_10으로_수뇌부를_설득해_캡을_넘겨_요구액에_영입한다()
        {
            var (s, star) = CapBoundMarket();
            s.SetTrust(25, 0, 0, 0);
            var book = Book(s);
            Assert.That(StoveLeague.Check(s, book, star, star.Ask), Is.EqualTo(SigningBlock.SalaryCap));
            Assert.That(StoveLeague.CheckPersuasion(s, book, star), Is.EqualTo(SigningBlock.None));
            long budget = s.Budget, capBase = s.SalaryCapBase, cap = s.SalaryCap;

            var o = StoveLeague.SignWithPersuasion(s, book, star);
            Assert.That((o.Signed, o.Persuaded, o.Salary), Is.EqualTo((true, true, star.Ask)));
            Assert.That(s.TrustPoints, Is.EqualTo(25 - FrontOfficeTrust.PersuadeCost));
            Assert.That(s.Budget, Is.EqualTo(budget - FrontOfficeTrust.FeeOf(star.Ask)), "수수료는 낸다");
            Assert.That((s.SalaryCapBase, s.SalaryCap), Is.EqualTo((capBase, cap)), "캡 자체는 늘지 않는다 (이번 한 건만 예외)");
            var after = Book(s);
            Assert.That(after.Get(Samsung).Bench.Any(p => p.Id == star.Id), Is.True);
            long payroll = StoveLeague.Payroll(after.Get(Samsung), s.Signings);
            Assert.That(payroll, Is.EqualTo(capBase + star.Ask), "페이롤 = 기존 + 계약 연봉 (계산 방식 그대로)");
            Assert.That(payroll, Is.GreaterThan(s.SalaryCap), "캡 초과 상태가 된다");
            Assert.That(s.LatestNews(1).Single().Headline, Does.StartWith($"{Samsung}, 수뇌부 설득 끝에 캡을 넘겨 {star.Player.Name} 영입!"));
            Assert.That(s.Signings.Last().Salary, Is.EqualTo(star.Ask));
        }

        [Test]
        public void 설득은_캡에만_막힐_때_TP_10_이상일_때만_되고_다음_영입은_다시_캡에_막힌다()
        {
            var (s, star) = CapBoundMarket();
            s.SetTrust(FrontOfficeTrust.PersuadeCost - 1, 0, 0, 0);
            Assert.That(StoveLeague.CheckPersuasion(s, Book(s), star), Is.EqualTo(SigningBlock.Trust));
            var failed = StoveLeague.SignWithPersuasion(s, Book(s), star);
            Assert.That(failed.Signed, Is.False);
            Assert.That(s.TrustPoints, Is.EqualTo(FrontOfficeTrust.PersuadeCost - 1), "막히면 아무것도 바뀌지 않는다");

            // 캡 여유가 있으면 설득이 필요 없다
            s.SetTrust(50, 0, 0, 0);
            s.SetSalaryCapBase(s.Payroll + star.Ask * 2);
            Assert.That(StoveLeague.CheckPersuasion(s, Book(s), star), Is.EqualTo(SigningBlock.CapRoomAvailable));

            // 설득으로 한 명 영입 → 페이롤이 캡을 넘어 다음 FA는 다시 캡에 막힌다 (또 설득하려면 TP 10)
            s.SetSalaryCapBase(s.Payroll);
            Assert.That(StoveLeague.SignWithPersuasion(s, Book(s), star).Signed, Is.True);
            var book = Book(s);
            var other = book.Get("LG 트윈스").Lineup.OrderByDescending(p => p.War).First();
            var mine = book.Get(Samsung).Bench.First(p => p.Id != star.Id);
            s.AddSigning(new SigningRecord(other.Id, "LG 트윈스", LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            s.AddSigning(new SigningRecord(mine.Id, Samsung, LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            book = Book(s);
            s.SetPayroll(StoveLeague.Payroll(book.Get(Samsung), s.Signings));
            var next = StoveLeague.Market(book, Samsung, FanMood.Normal).Single(a => a.Id == other.Id);
            Assert.That(StoveLeague.Check(s, book, next, next.Ask), Is.EqualTo(SigningBlock.SalaryCap));
            Assert.That(StoveLeague.CheckPersuasion(s, book, next), Is.EqualTo(SigningBlock.None), "TP 40 남아 또 설득 가능");
        }

        [Test]
        public void 정원이_차_있으면_설득으로도_영입할_수_없다()
        {
            var s = SeasonState.CreateDummy();
            var book = Book(s);
            var star = book.Get("KIA 타이거즈").Lineup.OrderByDescending(p => p.War).First();
            s.AddSigning(new SigningRecord(star.Id, "KIA 타이거즈", LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            book = Book(s);
            s.SetTrust(100, 0, 0, 0);
            s.SetSalaryCapBase(1);
            var agent = StoveLeague.Market(book, Samsung, FanMood.Normal).Single(a => a.Id == star.Id);
            Assert.That(StoveLeague.CheckPersuasion(s, book, agent), Is.EqualTo(SigningBlock.RosterFull), "캡 말고 다른 이유(1군 26명)는 설득으로 못 넘는다");
        }

        // ---------- TP 적립 ----------

        [Test]
        public void 정규시즌_3연승마다_TP_1을_적립한다()
        {
            var s = SeasonState.CreateDummy();
            var tp = Enumerable.Range(0, 7).Select(_ => { var r = Play(s, 5, 1); return (s.TrustPoints, r.TrustEarned); }).ToList();
            Assert.That(tp.Select(x => x.TrustPoints), Is.EqualTo(new[] { 0, 0, 1, 1, 1, 2, 2 }));
            Assert.That(tp.Select(x => x.TrustEarned), Is.EqualTo(new[] { 0, 0, 1, 0, 0, 1, 0 }));
            Play(s, 1, 5); // 연승 끊김
            var again = Play(s, 5, 1);
            Assert.That((s.TrustPoints, again.TrustEarned), Is.EqualTo((2, 0)), "다시 1연승 (더미 시즌 10경기 중 9경기째)");
        }

        [Test]
        public void 한국시리즈_진출에_TP_10_우승에_TP_20을_적립한다()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(7);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(9, 0), random);
            }
            Assert.That(s.Postseason.Involves(s.MyTeam), Is.True);
            Assert.That(s.TrustPoints, Is.EqualTo(FrontOfficeTrust.PostseasonTrust), "진출");
            while (s.Phase == SeasonPhase.PostSeason)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(9, 0), random);
            }
            Assert.That(s.Postseason.Champion, Is.EqualTo(s.MyTeam));
            Assert.That(s.TrustPoints, Is.EqualTo(FrontOfficeTrust.PostseasonTrust + FrontOfficeTrust.ChampionTrust), "우승");
        }

        // ---------- 팬덤 ----------

        [Test]
        public void 라이벌_1_2위를_이기면_팬_만족도가_1퍼센트포인트_더_오른다()
        {
            var s = SeasonState.CreateDummy();
            var first = s.NextGame;
            string opponent = first.AwayTeam == s.MyTeam ? first.HomeTeam : first.AwayTeam;
            Assert.That(s.StandingOf(opponent).Rank, Is.LessThanOrEqualTo(FandomRules.RivalMaxRank), "개막전 상대 LG는 2위");
            int fan = s.FanApproval;
            var r = Play(s, 5, 1);
            Assert.That(r.RivalWin, Is.True);
            Assert.That(s.FanApproval - fan, Is.EqualTo(FranchiseFinance.WinFan + FandomRules.RivalWinFan));

            // 라이벌이 아닌 상대 승리는 그대로
            while (true)
            {
                var g = s.NextGame;
                string opp = g.AwayTeam == s.MyTeam ? g.HomeTeam : g.AwayTeam;
                if (s.StandingOf(opp).Rank > FandomRules.RivalMaxRank) break;
                Play(s, 1, 5);
            }
            fan = s.FanApproval;
            var plain = Play(s, 5, 1);
            Assert.That(plain.RivalWin, Is.False);
            Assert.That(s.FanApproval - fan, Is.EqualTo(FranchiseFinance.WinFan));
        }

        [Test]
        public void 팬_만족도가_100에_닿으면_팬덤_레벨업_보상을_받고_게이지는_60으로_돌아간다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(s.Budget, 98);
            long budget = s.Budget;
            var r = Play(s, 5, 1);
            Assert.That(r.FandomLevelUp, Is.Not.Null);
            Assert.That(s.FandomLevel, Is.EqualTo(1));
            Assert.That(s.FanApproval, Is.EqualTo(FandomRules.LevelResetApproval), "게이지 초기화");
            Assert.That(r.FandomLevelUp.Theme, Is.EqualTo(FandomRules.ClassicTheme), "Lv.1 클래식 올드 유니폼 테마");
            Assert.That(s.Budget - budget, Is.EqualTo(r.Income - r.PayrollExpense + FandomRules.LevelBudgetBonus), "경기 수입 + 특별 예산");
            Assert.That(s.RecentNews.Any(n => n.Tag == "[특전]" && n.Headline == $"{Samsung} 팬덤 폭발! 구단 굿즈 완판으로 특별 예산 확보!"), Is.True);
            Assert.That(FandomRules.HasClassicTheme(s.FandomLevel), Is.True);

            // Lv.2: CP +2, Lv.3: TP +5
            int cp = s.CampPoints;
            s.SetFinance(s.Budget, 100);
            var l2 = s.CheckFandomLevelUp();
            Assert.That((l2.Level, s.CampPoints - cp), Is.EqualTo((2, FandomRules.Level2CampPoints)));
            int tp = s.TrustPoints;
            s.SetFinance(s.Budget, 100);
            var l3 = s.CheckFandomLevelUp();
            Assert.That((l3.Level, s.TrustPoints - tp), Is.EqualTo((3, FandomRules.Level3Trust)));
            Assert.That(s.CheckFandomLevelUp(), Is.Null, "60%면 레벨업 없음");
            Assert.That(FandomRules.RewardsUpTo(3).Select(x => x.Level), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(l3.Text, Is.EqualTo("특별 예산 ₩ 3억 · 신뢰도 +5 TP"));
        }

        [Test]
        public void 한국시리즈_우승은_팬_만족도_100을_찍고_그대로_팬덤_레벨업으로_이어진다()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(7);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(9, 0), random);
            }
            int before = s.FandomLevel;
            while (s.Phase == SeasonPhase.PostSeason)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(9, 0), random);
            }
            Assert.That(s.ChampionAward.Value.FanDelta, Is.GreaterThanOrEqualTo(0), "우승 보상은 팬 100%까지");
            Assert.That(s.FandomLevel, Is.EqualTo(before + 1));
            Assert.That(s.LastFandomLevelUp.Level, Is.EqualTo(s.FandomLevel));
            Assert.That(s.FanApproval, Is.EqualTo(FandomRules.LevelResetApproval));
        }

        [Test]
        public void 팬덤_레벨은_저장되고_이전_파일은_0이다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFandomLevel(2);
            string json = JsonUtility.ToJson(s.ToSaveData());
            Assert.That(SeasonState.FromSaveData(JsonUtility.FromJson<SeasonSaveData>(json)).FandomLevel, Is.EqualTo(2));
            var old = JsonUtility.FromJson<SeasonSaveData>(json.Replace("\"FandomLevel\"", "\"FandomLevel_old\""));
            Assert.That(SeasonState.FromSaveData(old).FandomLevel, Is.Zero);
        }
    }
}
