using System;
using System.Text.RegularExpressions;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 21: 구단 재정(예산·팬 만족도)의 경기 후 증감, 범위 제한, 저장 하위 호환, 로비 표기.
    /// Phase 26: 화폐 현실화(×1,000, long), 경기당 선수단 유지비(페이롤 ÷ 144), 이전 단위 저장 파일 변환, "₩ 50억" 표기.
    /// </summary>
    public class FinanceTests
    {
        const int Seed = 20261016;

        /// <summary>더미 시즌 첫 경기는 서울(원정) vs 부산(홈, 내 구단).</summary>
        static PostGameReport Play(SeasonState s, GameScore score)
        {
            s.StartNextGame();
            return PostGameProcessor.Process(s, score, new SeededRandom(Seed), null);
        }

        static readonly GameScore HomeWin = new GameScore(2, 5);
        static readonly GameScore HomeLoss = new GameScore(6, 1);
        static readonly GameScore Tie = new GameScore(3, 3);

        [Test]
        public void 새_시즌은_예산_50억원_팬_만족도_50퍼센트로_시작한다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((5_000_000_000L, 50)));
            Assert.That(s.Payroll, Is.Zero, "선수 DB를 모르면 페이롤 0 (유지비 없음)");
        }

        [TestCase(GameOutcome.Win, 50_000_000L, 3)]
        [TestCase(GameOutcome.Draw, 30_000_000L, 0)]
        [TestCase(GameOutcome.Loss, 20_000_000L, -2)]
        public void 경기_결과별_재정_변화_공식(GameOutcome outcome, long budget, int fan)
        {
            Assert.That(FranchiseFinance.For(outcome), Is.EqualTo(new FinanceChange(budget, fan)));
        }

        [Test]
        public void 승리하면_관중_수입_5천만원과_팬_만족도_3퍼센트포인트가_오른다()
        {
            var s = SeasonState.CreateDummy();
            var r = Play(s, HomeWin);
            Assert.That(r.MyOutcome, Is.EqualTo(GameOutcome.Win));
            Assert.That((r.BudgetBefore, r.BudgetAfter, s.Budget), Is.EqualTo((5_000_000_000L, 5_050_000_000L, 5_050_000_000L)));
            Assert.That((r.FanApprovalBefore, r.FanApprovalAfter, s.FanApproval), Is.EqualTo((50, 53, 53)));
            Assert.That(r.Finance, Is.EqualTo(new FinanceChange(50_000_000, 3)));
            Assert.That((r.Income, r.PayrollExpense, r.NetIncome), Is.EqualTo((50_000_000L, 0L, 50_000_000L)));
        }

        [Test]
        public void 패배하면_수입이_줄고_팬_만족도가_2퍼센트포인트_내려가며_무승부는_만족도_그대로다()
        {
            var s = SeasonState.CreateDummy();
            var loss = Play(s, HomeLoss);
            Assert.That(loss.MyOutcome, Is.EqualTo(GameOutcome.Loss));
            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((5_020_000_000L, 48)));

            // 2번째 경기: 내 구단 원정 (부산 @ 인천). 원정이어도 결과는 내 구단 기준.
            var draw = Play(s, Tie);
            Assert.That(draw.MyOutcome, Is.EqualTo(GameOutcome.Draw));
            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((5_050_000_000L, 48)));
            Assert.That(draw.Finance, Is.EqualTo(new FinanceChange(30_000_000, 0)));
        }

        [Test]
        public void 경기마다_관중_수입에서_페이롤_나누기_144의_유지비를_뺀다()
        {
            var s = SeasonState.CreateDummy();
            s.SetPayroll(4_020_000_000); // 샘플 부산 1군 연봉 합계
            var win = Play(s, HomeWin);
            Assert.That(FranchiseFinance.PayrollPerGame(4_020_000_000), Is.EqualTo(27_916_666L), "÷144 내림");
            Assert.That((win.Income, win.PayrollExpense, win.NetIncome), Is.EqualTo((50_000_000L, 27_916_666L, 22_083_334L)));
            Assert.That(win.Finance, Is.EqualTo(new FinanceChange(22_083_334, 3)));
            Assert.That(s.Budget, Is.EqualTo(5_022_083_334L));
            Assert.That(win.FinanceText, Is.EqualTo("관중 수입 +₩5,000만 · 유지비 −₩2,791만 · 순이익 +₩2,208만"));

            // 2번째 경기(원정 6 : 홈 1 → 원정인 내 구단 승) 다음 3번째: 패배는 적자
            Play(s, HomeLoss);
            var loss = Play(s, HomeLoss); // 3번째는 홈 → 패
            Assert.That(loss.MyOutcome, Is.EqualTo(GameOutcome.Loss));
            Assert.That(loss.NetIncome, Is.EqualTo(20_000_000 - 27_916_666), "패배하면 적자");
            Assert.That(loss.Finance.BudgetDelta, Is.EqualTo(loss.NetIncome));
            Assert.That(loss.FinanceText, Does.EndWith("순이익 −₩791만"));
        }

        [Test]
        public void 유지비로_예산이_바닥나면_0에서_멈추고_실제_변화만_보고한다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(1_000_000, 50);
            s.SetPayroll(144_000_000_000); // 경기당 10억
            var r = Play(s, HomeLoss);
            Assert.That(r.NetIncome, Is.EqualTo(20_000_000 - 1_000_000_000));
            Assert.That((s.Budget, r.Finance.BudgetDelta), Is.EqualTo((0L, -1_000_000L)));
            s.SetPayroll(-5);
            Assert.That(s.Payroll, Is.Zero);
        }

        [Test]
        public void 팬_만족도는_0에서_100_사이로_자르고_실제_변화량을_보고한다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(1_000_000, 99);
            var r = Play(s, HomeWin);
            Assert.That(s.FanApproval, Is.EqualTo(100), "99 + 3 → 100");
            Assert.That(r.Finance.FanDelta, Is.EqualTo(1), "잘린 뒤 실제로 오른 양");

            s.SetFinance(1_000_000, 1);
            Assert.That(s.ApplyFinance(FranchiseFinance.For(GameOutcome.Loss)), Is.EqualTo(new FinanceChange(20_000_000, -1)));
            Assert.That(s.FanApproval, Is.Zero, "1 − 2 → 0");

            s.SetFinance(-500, 150);
            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((0L, 100)), "범위 밖 값은 맞춘다");
            s.SetFinance(10, -7);
            Assert.That(s.FanApproval, Is.Zero);
        }

        [Test]
        public void 예산은_long이라_int를_넘어도_계산되고_음수나_넘침이_없다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(30_000_000, 50);
            Assert.That(s.ApplyFinance(new FinanceChange(-100_000_000, 0)), Is.EqualTo(new FinanceChange(-30_000_000, 0)));
            Assert.That(s.Budget, Is.Zero, "0 아래로 내려가지 않는다");
            Assert.That(s.ApplyFinance(new FinanceChange(long.MinValue, 0)).BudgetDelta, Is.Zero);

            // int 범위(약 21억)를 넘는 예산도 그대로 계산된다.
            s.SetFinance(int.MaxValue, 50);
            s.ApplyFinance(FranchiseFinance.For(GameOutcome.Win));
            Assert.That(s.Budget, Is.EqualTo((long)int.MaxValue + 50_000_000), "int 넘침 없음");

            s.SetFinance(long.MaxValue - 10, 50);
            s.ApplyFinance(FranchiseFinance.For(GameOutcome.Win));
            Assert.That(s.Budget, Is.EqualTo(long.MaxValue), "long 범위를 넘지 않는다");
            Assert.That(FranchiseFinance.AddBudget(long.MaxValue, long.MaxValue), Is.EqualTo(long.MaxValue));
            Assert.That(FranchiseFinance.AddBudget(5, long.MinValue), Is.Zero);
            Assert.That(FranchiseFinance.ClampBudget(-1), Is.Zero);
        }

        [Test]
        public void 재정은_저장했다_불러와도_같다()
        {
            var s = SeasonState.CreateDummy();
            Play(s, HomeWin);
            Play(s, HomeLoss); // 원정 경기: 원정 6 : 홈 1 → 내 구단(원정) 승
            var loaded = SaveManager.FromJson(SaveManager.ToJson(s));
            Assert.That((loaded.Budget, loaded.FanApproval), Is.EqualTo((s.Budget, s.FanApproval)));
            // Phase 23: 더미 시즌은 23승에서 시작해 첫 승 뒤 유니폼 슬롯(10승)이 열려, 두 번째 승리에 굿즈 수입 +₩500만.
            Assert.That((loaded.Budget, loaded.FanApproval), Is.EqualTo((5_105_000_000L, 56)));

            s.SetFinance(9_000_000_000_000, 50); // int를 한참 넘는 값
            Assert.That(SaveManager.FromJson(SaveManager.ToJson(s)).Budget, Is.EqualTo(9_000_000_000_000L));
        }

        [Test]
        public void 재정_필드가_없는_이전_저장_파일은_기본값으로_읽힌다()
        {
            var s = SeasonState.CreateDummy();
            Play(s, HomeWin);
            string json = SaveManager.ToJson(s);
            string legacy = Regex.Replace(json, @",\s*""(HasFinance|Budget|FanApproval)"":\s*[^,\r\n}]+", "");
            Assert.That(legacy, Does.Not.Contain("Budget").And.Not.Contain("FanApproval").And.Not.Contain("HasFinance"));

            var loaded = SaveManager.FromJson(legacy);
            Assert.That((loaded.Budget, loaded.FanApproval), Is.EqualTo((FranchiseFinance.DefaultBudget, FranchiseFinance.DefaultFanApproval)));
            Assert.That(loaded.NextGame.Number, Is.EqualTo(2), "나머지 진행 상황은 그대로");

            // 저장된 0은 "없음"과 구분된다.
            s.SetFinance(0, 0);
            var zero = SaveManager.FromJson(SaveManager.ToJson(s));
            Assert.That((zero.Budget, zero.FanApproval), Is.EqualTo((0L, 0)));
        }

        [Test]
        public void 화폐_단위_버전이_없는_Phase_25_이전_저장_파일은_금액을_1000배로_바꿔_읽는다()
        {
            var s = SeasonState.CreateDummy();
            s.SetTrust(30, 10, 0, 0);
            string json = SaveManager.ToJson(s);
            Assert.That(json, Does.Contain("\"MoneyUnitVersion\": 1").Or.Contain("\"MoneyUnitVersion\":1"));

            // 이전 형식: 단위 버전 없음, 예산 ₩5,050,000 (int), 대출 ₩200,000, 영입 연봉 ₩376,200
            var data = s.ToSaveData();
            data.MoneyUnitVersion = 0;
            data.Budget = 5_050_000;
            data.CapFlex = 200_000;
            data.CapRepay = 100_000;
            data.Signings.Add(new SigningData { PlayerId = "BW004", FromTeam = "LG 트윈스", ToTeam = "삼성 라이온즈", Salary = 376_200 });
            var loaded = SeasonState.FromSaveData(data);
            Assert.That(loaded.Budget, Is.EqualTo(5_050_000_000L));
            Assert.That((loaded.CapFlex, loaded.CapRepay), Is.EqualTo((200_000_000L, 100_000_000L)));
            Assert.That(loaded.Signings[0].Salary, Is.EqualTo(376_200_000L));
            Assert.That(loaded.TrustPoints, Is.EqualTo(30), "TP는 단위가 아니다");

            string legacyJson = Regex.Replace(SaveManager.ToJson(s), @"""MoneyUnitVersion"":\s*\d+,?\s*", "");
            Assert.That(legacyJson, Does.Not.Contain("MoneyUnitVersion"));
            Assert.That(SaveManager.FromJson(legacyJson).Budget, Is.EqualTo(5_000_000_000_000L), "필드가 없으면 이전 단위로 본다");
        }

        [Test]
        public void 범위를_벗어난_저장값은_손상이_아니라_범위_안으로_맞춘다()
        {
            var data = SeasonState.CreateDummy().ToSaveData();
            data.Budget = -1;
            data.FanApproval = 250;
            var loaded = SeasonState.FromSaveData(data);
            Assert.That((loaded.Budget, loaded.FanApproval), Is.EqualTo((0L, 100)));

            data.MoneyUnitVersion = 0;
            data.Budget = long.MaxValue / 10;
            Assert.That(SeasonState.FromSaveData(data).Budget, Is.EqualTo(long.MaxValue), "변환하다 넘치면 끝값");
        }

        [Test]
        public void 로비_표기_억_만_단위와_날짜_팬_구간()
        {
            Assert.That(FranchiseFinance.BudgetText(5_000_000_000), Is.EqualTo("₩ 50억"));
            Assert.That(FranchiseFinance.BudgetText(4_604_990_000), Is.EqualTo("₩ 46억 499만"));
            Assert.That(FranchiseFinance.BudgetText(123_456_789_000_000), Is.EqualTo("₩ 1,234,567억 8,900만"));
            Assert.That(FranchiseFinance.BudgetText(50_000_000), Is.EqualTo("₩ 5,000만"));
            Assert.That(FranchiseFinance.BudgetText(9_999), Is.EqualTo("₩ 9,999"));
            Assert.That(FranchiseFinance.BudgetText(0), Is.EqualTo("₩ 0"));
            Assert.That(FranchiseFinance.BudgetDeltaText(50_000_000), Is.EqualTo("+₩5,000만"));
            Assert.That(FranchiseFinance.BudgetDeltaText(-27_916_666), Is.EqualTo("−₩2,791만"), "만 원 아래는 버린다");
            Assert.That(FranchiseFinance.BudgetDeltaText(0), Is.EqualTo("±₩0"));
            Assert.That(FranchiseFinance.BudgetDeltaText(long.MinValue), Does.StartWith("−₩"));
            Assert.That(FranchiseFinance.ExactText(5_000_000_000), Is.EqualTo("₩5,000,000,000"));
            Assert.That(FranchiseFinance.ExactText(-1_200), Is.EqualTo("−₩1,200"));
            Assert.That(FranchiseFinance.FanDeltaText(3), Is.EqualTo("+3%p"));
            Assert.That(FranchiseFinance.FanDeltaText(-2), Is.EqualTo("−2%p"));
            Assert.That(FranchiseFinance.TodayText(new DateTime(2026, 10, 16)), Is.EqualTo("2026-10-16 (금)"));
            Assert.That(FranchiseFinance.TodayText(new DateTime(2026, 10, 18, 23, 59, 0)), Is.EqualTo("2026-10-18 (일)"));

            Assert.That(FranchiseFinance.MoodOf(80), Is.EqualTo(FanMood.Happy));
            Assert.That(FranchiseFinance.MoodOf(79), Is.EqualTo(FanMood.Normal));
            Assert.That(FranchiseFinance.MoodOf(30), Is.EqualTo(FanMood.Normal));
            Assert.That(FranchiseFinance.MoodOf(29), Is.EqualTo(FanMood.Angry));
        }
    }
}
