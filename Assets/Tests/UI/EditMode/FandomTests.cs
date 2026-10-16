using System;
using System.Linq;
using System.Text.RegularExpressions;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 23: 팬덤 슬롯 해금, 관중 배수·슬롯 효과의 스노우볼 재정 공식, 저장 하위 호환, 확률 엔진 격리.</summary>
    public class FandomTests
    {
        const int Seed = 20261016;

        /// <summary>내 구단이 이기는 점수 (홈/원정에 맞춰).</summary>
        static GameScore MyWin(SeasonState s) => s.NextGame.HomeTeam == s.MyTeam ? new GameScore(2, 5) : new GameScore(5, 2);
        static GameScore MyLoss(SeasonState s) => s.NextGame.HomeTeam == s.MyTeam ? new GameScore(5, 2) : new GameScore(2, 5);

        static PostGameReport Play(SeasonState s, Func<SeasonState, GameScore> score)
        {
            var final = score(s);
            s.StartNextGame();
            return PostGameProcessor.Process(s, final, new SeededRandom(Seed), null);
        }

        [Test]
        public void Normal_구간_슬롯_없음이면_기존_공식과_같다()
        {
            foreach (GameOutcome o in Enum.GetValues(typeof(GameOutcome)))
                Assert.That(FranchiseFinance.For(o, FanMood.Normal, FandomSlot.None), Is.EqualTo(FranchiseFinance.For(o)), o.ToString());
        }

        [TestCase(GameOutcome.Win, FanMood.Happy, 60_000_000L, 3)]
        [TestCase(GameOutcome.Win, FanMood.Angry, 40_000_000L, 3)]
        [TestCase(GameOutcome.Draw, FanMood.Happy, 36_000_000L, 0)]
        [TestCase(GameOutcome.Draw, FanMood.Angry, 24_000_000L, 0)]
        [TestCase(GameOutcome.Loss, FanMood.Happy, 24_000_000L, -4)]
        [TestCase(GameOutcome.Loss, FanMood.Angry, 16_000_000L, -2)]
        public void 관중_배수와_Happy_패배_브레이크(GameOutcome outcome, FanMood mood, long budget, int fan)
        {
            Assert.That(FranchiseFinance.For(outcome, mood, FandomSlot.None), Is.EqualTo(new FinanceChange(budget, fan)));
        }

        [Test]
        public void 슬롯_효과는_결과별로_하나씩_붙는다()
        {
            Assert.That(FranchiseFinance.For(GameOutcome.Win, FanMood.Normal, FandomSlot.Uniform), Is.EqualTo(new FinanceChange(55_000_000, 3)));
            Assert.That(FranchiseFinance.For(GameOutcome.Win, FanMood.Normal, FandomSlot.Ceremony), Is.EqualTo(new FinanceChange(50_000_000, 4)));
            Assert.That(FranchiseFinance.For(GameOutcome.Win, FanMood.Happy, FandomSlot.All), Is.EqualTo(new FinanceChange(65_000_000, 4)), "치어리더는 승리에 영향 없음");
            Assert.That(FranchiseFinance.For(GameOutcome.Loss, FanMood.Normal, FandomSlot.Cheerleader), Is.EqualTo(new FinanceChange(20_000_000, -1)));
            Assert.That(FranchiseFinance.For(GameOutcome.Loss, FanMood.Happy, FandomSlot.All), Is.EqualTo(new FinanceChange(24_000_000, -3)), "Happy 패배 −4 → 치어리더 −3");
            Assert.That(FranchiseFinance.For(GameOutcome.Draw, FanMood.Normal, FandomSlot.All), Is.EqualTo(new FinanceChange(30_000_000, 0)), "무승부엔 슬롯 효과 없음");
        }

        [Test]
        public void 해금_조건은_10승_팬70_팬80이고_한번_열리면_잃지_않는다()
        {
            Assert.That(FandomRules.Unlocked(FandomSlot.None, 9, 69), Is.EqualTo(FandomSlot.None));
            Assert.That(FandomRules.Unlocked(FandomSlot.None, 10, 69), Is.EqualTo(FandomSlot.Uniform));
            Assert.That(FandomRules.Unlocked(FandomSlot.None, 0, 70), Is.EqualTo(FandomSlot.Ceremony));
            Assert.That(FandomRules.Unlocked(FandomSlot.None, 0, 80), Is.EqualTo(FandomSlot.Ceremony | FandomSlot.Cheerleader));
            Assert.That(FandomRules.Unlocked(FandomSlot.All, 0, 0), Is.EqualTo(FandomSlot.All), "팬 만족도가 떨어져도 유지");
            Assert.That(FandomRules.Unlocked((FandomSlot)64, 0, 0), Is.EqualTo(FandomSlot.None), "모르는 비트는 버린다");
        }

        [Test]
        public void 연승하면_슬롯이_차례로_해금되고_다음_경기부터_효과가_붙는다()
        {
            var s = SeasonState.CreateDummy();
            int fan = 50;
            for (int game = 1; game <= 10; game++)
            {
                var r = Play(s, MyWin);
                Assert.That(r.MyOutcome, Is.EqualTo(GameOutcome.Win));
                Assert.That(r.FanApprovalBefore, Is.EqualTo(fan));
                var expected = game == 1 ? FandomSlot.None
                    : game <= 7 ? FandomSlot.Uniform : FandomSlot.Uniform | FandomSlot.Ceremony;
                Assert.That(r.SlotsApplied, Is.EqualTo(expected), $"{game}경기 적용 슬롯");
                fan = r.FanApprovalAfter;
            }
            // 더미 시즌은 23승에서 시작 → 1경기 뒤 유니폼(10승 이상) 해금.
            // 팬 50 → (+3 × 7) 71: 7경기 뒤 세리머니 해금 → 8~10경기 +4 → 83: 치어리더.
            Assert.That(s.FanApproval, Is.EqualTo(83));
            Assert.That(s.UnlockedSlots, Is.EqualTo(FandomSlot.All));
            // 예산: 경기 전 팬은 끝까지 79 이하(Normal) → 기본 5만, 2경기부터 유니폼 굿즈 +5천.
            Assert.That(s.Budget, Is.EqualTo(5_000_000_000L + 50_000_000 + 55_000_000L * 9));
        }

        [Test]
        public void 리포트는_새로_해금된_슬롯만_알린다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(5_000_000_000, 68);
            var first = Play(s, MyWin);
            Assert.That(first.NewlyUnlocked, Is.EqualTo(FandomSlot.Uniform | FandomSlot.Ceremony), "24승 · 68 + 3 = 71");
            var second = Play(s, MyWin);
            Assert.That(second.NewlyUnlocked, Is.EqualTo(FandomSlot.None));
            Assert.That(second.Finance, Is.EqualTo(new FinanceChange(55_000_000, 4)));
        }

        [Test]
        public void Happy_구간에서_지면_수입은_늘지만_팬_만족도는_두배로_떨어진다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(5_000_000_000, 85);
            var r = Play(s, MyLoss);
            Assert.That(r.MoodBefore, Is.EqualTo(FanMood.Happy));
            Assert.That(r.Finance, Is.EqualTo(new FinanceChange(24_000_000, -4)));
            Assert.That(s.UnlockedSlots, Is.EqualTo(FandomSlot.All), "23승 · 경기 뒤 81 → 전부 해금");

            var next = Play(s, MyLoss);
            Assert.That(next.Finance, Is.EqualTo(new FinanceChange(24_000_000, -3)), "치어리더가 하락 완화");
            Assert.That(s.FanApproval, Is.EqualTo(78));
        }

        [Test]
        public void 슬롯은_저장했다_불러와도_같고_없는_이전_파일은_해금_없음이다()
        {
            var s = SeasonState.CreateDummy();
            s.SetUnlockedSlots(FandomSlot.Uniform | FandomSlot.Cheerleader);
            string json = SaveManager.ToJson(s);
            Assert.That(SaveManager.FromJson(json).UnlockedSlots, Is.EqualTo(FandomSlot.Uniform | FandomSlot.Cheerleader));

            string legacy = Regex.Replace(json, @",\s*""(HasFandom|FandomSlots)"":\s*[^,\r\n}]+", "");
            Assert.That(legacy, Does.Not.Contain("Fandom"));
            var loaded = SaveManager.FromJson(legacy);
            Assert.That(loaded.UnlockedSlots, Is.EqualTo(FandomSlot.None));
            Assert.That(loaded.Budget, Is.EqualTo(s.Budget), "나머지는 그대로");

            var data = s.ToSaveData();
            data.FandomSlots = 0xFF;
            Assert.That(SeasonState.FromSaveData(data).UnlockedSlots, Is.EqualTo(FandomSlot.All), "모르는 비트는 버린다");
        }

        [Test]
        public void 확률_엔진은_팬덤_재정_계층을_참조하지_않는다()
        {
            var sim = typeof(MatchupCalculator).Assembly;
            Assert.That(sim.GetReferencedAssemblies().Select(a => a.Name), Has.None.EqualTo(typeof(FandomSlot).Assembly.GetName().Name));
            Assert.That(sim.GetTypes().Select(t => t.Name), Has.None.Contains("Fandom"));
        }

        [Test]
        public void 슬롯_라벨()
        {
            Assert.That(new[] { FandomSlot.Uniform, FandomSlot.Ceremony, FandomSlot.Cheerleader }.Select(FandomRules.Label),
                Is.EqualTo(new[] { "유니폼", "세리머니", "치어리더" }));
            Assert.Throws<ArgumentOutOfRangeException>(() => FandomRules.Label(FandomSlot.All));
            Assert.That(FandomRules.Slots, Is.EqualTo(new[] { FandomSlot.Uniform, FandomSlot.Ceremony, FandomSlot.Cheerleader }));
            Assert.That(FandomRules.Hint(FandomSlot.Uniform), Is.EqualTo("시즌 10승에 해금 · 승리 시 굿즈 수입 +₩500만"));
            Assert.That(FandomRules.Hint(FandomSlot.Cheerleader), Does.StartWith("팬 만족도 80% 도달 시 해금"));
            Assert.Throws<ArgumentOutOfRangeException>(() => FandomRules.Hint(FandomSlot.None));
        }
    }
}
