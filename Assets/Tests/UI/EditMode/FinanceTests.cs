using System;
using System.Text.RegularExpressions;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 21: 구단 재정(예산·팬 만족도)의 경기 후 증감, 범위 제한, 저장 하위 호환, 로비 표기.</summary>
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
        public void 새_시즌은_예산_500만원_팬_만족도_50퍼센트로_시작한다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((5_000_000, 50)));
        }

        [TestCase(GameOutcome.Win, 50_000, 3)]
        [TestCase(GameOutcome.Draw, 30_000, 0)]
        [TestCase(GameOutcome.Loss, 20_000, -2)]
        public void 경기_결과별_재정_변화_공식(GameOutcome outcome, int budget, int fan)
        {
            Assert.That(FranchiseFinance.For(outcome), Is.EqualTo(new FinanceChange(budget, fan)));
        }

        [Test]
        public void 승리하면_관중_수입_5만원과_팬_만족도_3퍼센트포인트가_오른다()
        {
            var s = SeasonState.CreateDummy();
            var r = Play(s, HomeWin);
            Assert.That(r.MyOutcome, Is.EqualTo(GameOutcome.Win));
            Assert.That((r.BudgetBefore, r.BudgetAfter, s.Budget), Is.EqualTo((5_000_000, 5_050_000, 5_050_000)));
            Assert.That((r.FanApprovalBefore, r.FanApprovalAfter, s.FanApproval), Is.EqualTo((50, 53, 53)));
            Assert.That(r.Finance, Is.EqualTo(new FinanceChange(50_000, 3)));
        }

        [Test]
        public void 패배하면_수입이_줄고_팬_만족도가_2퍼센트포인트_내려가며_무승부는_만족도_그대로다()
        {
            var s = SeasonState.CreateDummy();
            var loss = Play(s, HomeLoss);
            Assert.That(loss.MyOutcome, Is.EqualTo(GameOutcome.Loss));
            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((5_020_000, 48)));

            // 2번째 경기: 내 구단 원정 (부산 @ 인천). 원정이어도 결과는 내 구단 기준.
            var draw = Play(s, Tie);
            Assert.That(draw.MyOutcome, Is.EqualTo(GameOutcome.Draw));
            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((5_050_000, 48)));
            Assert.That(draw.Finance, Is.EqualTo(new FinanceChange(30_000, 0)));
        }

        [Test]
        public void 팬_만족도는_0에서_100_사이로_자르고_실제_변화량을_보고한다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(1_000, 99);
            var r = Play(s, HomeWin);
            Assert.That(s.FanApproval, Is.EqualTo(100), "99 + 3 → 100");
            Assert.That(r.Finance.FanDelta, Is.EqualTo(1), "잘린 뒤 실제로 오른 양");

            s.SetFinance(1_000, 1);
            Assert.That(s.ApplyFinance(FranchiseFinance.For(GameOutcome.Loss)), Is.EqualTo(new FinanceChange(20_000, -1)));
            Assert.That(s.FanApproval, Is.Zero, "1 − 2 → 0");

            s.SetFinance(-500, 150);
            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((0, 100)), "범위 밖 값은 맞춘다");
            s.SetFinance(10, -7);
            Assert.That(s.FanApproval, Is.Zero);
        }

        [Test]
        public void 예산은_음수가_되지_않고_넘치지도_않는다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(30_000, 50);
            Assert.That(s.ApplyFinance(new FinanceChange(-100_000, 0)), Is.EqualTo(new FinanceChange(-30_000, 0)));
            Assert.That(s.Budget, Is.Zero, "0 아래로 내려가지 않는다");
            Assert.That(s.ApplyFinance(new FinanceChange(int.MinValue, 0)).BudgetDelta, Is.Zero);

            s.SetFinance(int.MaxValue - 10, 50);
            s.ApplyFinance(FranchiseFinance.For(GameOutcome.Win));
            Assert.That(s.Budget, Is.EqualTo(int.MaxValue), "int 범위를 넘지 않는다");
            Assert.That(FranchiseFinance.ClampBudget(long.MaxValue), Is.EqualTo(int.MaxValue));
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
            // Phase 23: 더미 시즌은 23승에서 시작해 첫 승 뒤 유니폼 슬롯(10승)이 열려, 두 번째 승리에 굿즈 수입 +₩5,000.
            Assert.That((loaded.Budget, loaded.FanApproval), Is.EqualTo((5_105_000, 56)));
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
            Assert.That((zero.Budget, zero.FanApproval), Is.EqualTo((0, 0)));
        }

        [Test]
        public void 범위를_벗어난_저장값은_손상이_아니라_범위_안으로_맞춘다()
        {
            var data = SeasonState.CreateDummy().ToSaveData();
            data.Budget = -1;
            data.FanApproval = 250;
            var loaded = SeasonState.FromSaveData(data);
            Assert.That((loaded.Budget, loaded.FanApproval), Is.EqualTo((0, 100)));
        }

        [Test]
        public void 로비_표기_예산_변화_날짜_팬_구간()
        {
            Assert.That(FranchiseFinance.BudgetText(5_000_000), Is.EqualTo("₩ 5,000,000"));
            Assert.That(FranchiseFinance.BudgetText(0), Is.EqualTo("₩ 0"));
            Assert.That(FranchiseFinance.BudgetDeltaText(50_000), Is.EqualTo("+₩50,000"));
            Assert.That(FranchiseFinance.BudgetDeltaText(-1_200), Is.EqualTo("−₩1,200"));
            Assert.That(FranchiseFinance.BudgetDeltaText(0), Is.EqualTo("±₩0"));
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
