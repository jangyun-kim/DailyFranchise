using System;
using System.Text.RegularExpressions;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 23: 프런트 신뢰도(TP) 천장, 결렬 환급, 신뢰 대출 캡 유동성, 저장 하위 호환.</summary>
    public class FrontOfficeTrustTests
    {
        const double Eps = 1e-9;

        /// <summary>항상 같은 값을 주고 뽑은 횟수를 센다.</summary>
        sealed class FixedRandom : IRandomSource
        {
            readonly double _value;
            public FixedRandom(double value) => _value = value;
            public int Draws { get; private set; }
            public double NextDouble() { Draws++; return _value; }
            public int Next(int maxExclusive) { Draws++; return 0; }
        }

        const int Ask = 480_000; // WAR 3.0 연봉
        const int Fee = 24_000;

        [Test]
        public void 연봉과_수수료_공식()
        {
            Assert.That(FrontOfficeTrust.SalaryOf(0), Is.EqualTo(30_000));
            Assert.That(FrontOfficeTrust.SalaryOf(3.0), Is.EqualTo(Ask));
            Assert.That(FrontOfficeTrust.SalaryOf(-1.2), Is.EqualTo(30_000), "음수 WAR는 최저 연봉");
            Assert.That(FrontOfficeTrust.SalaryOf(2.345), Is.EqualTo(381_750));
            Assert.That(FrontOfficeTrust.SalaryOf(double.NaN), Is.EqualTo(30_000));
            Assert.That(FrontOfficeTrust.FeeOf(Ask), Is.EqualTo(Fee));
            Assert.That(FrontOfficeTrust.FeeOf(-5), Is.Zero);
        }

        [TestCase(480_000, 0.50)]
        [TestCase(432_000, 0.25)]
        [TestCase(566_400, 0.95)]
        [TestCase(600_000, 0.95)]
        [TestCase(240_000, 0.05)]
        [TestCase(0, 0.05)]
        public void 기본_성공률은_제시_요구_비율로_5에서_95퍼센트(int offer, double expected)
        {
            Assert.That(FrontOfficeTrust.BaseSuccessRate(offer, Ask), Is.EqualTo(expected).Within(Eps));
        }

        [Test]
        public void 소프트_천장은_TP_비례로_오르고_TP_100이면_확정()
        {
            Assert.That(FrontOfficeTrust.SuccessRate(Ask, Ask, 0), Is.EqualTo(0.5).Within(Eps));
            Assert.That(FrontOfficeTrust.SuccessRate(Ask, Ask, 40), Is.EqualTo(0.7).Within(Eps));
            Assert.That(FrontOfficeTrust.SuccessRate(Ask, Ask, 99), Is.EqualTo(0.995).Within(Eps));
            Assert.That(FrontOfficeTrust.SuccessRate(240_000, Ask, 100), Is.EqualTo(1), "헐값이어도 하드 천장");
            Assert.That(FrontOfficeTrust.SuccessRate(566_400, Ask, 60), Is.EqualTo(1), "1을 넘지 않는다");
            Assert.Throws<ArgumentOutOfRangeException>(() => FrontOfficeTrust.BaseSuccessRate(1, 0));
        }

        [Test]
        public void 결렬_환급은_요구액의_90퍼센트_이상_제시만()
        {
            Assert.That(FrontOfficeTrust.RefundOnFailure(432_000, Ask), Is.EqualTo(24), "수수료 ₩24,000 → 24 TP");
            Assert.That(FrontOfficeTrust.RefundOnFailure(431_999, Ask), Is.Zero);
            Assert.That(FrontOfficeTrust.RefundOnFailure(1, 0), Is.Zero);
        }

        [Test]
        public void 신뢰_대출_공식()
        {
            const int c = FrontOfficeTrust.BaseSalaryCap;
            Assert.That(FrontOfficeTrust.FlexFor(100, c), Is.EqualTo(200_000));
            Assert.That(FrontOfficeTrust.FlexFor(400, c), Is.EqualTo(600_000), "기본 캡의 10%까지");
            Assert.That(FrontOfficeTrust.FlexFor(-3, c), Is.Zero);
            Assert.That(FrontOfficeTrust.NextRepay(200_000), Is.EqualTo(100_000));
            Assert.That(FrontOfficeTrust.NextRepay(3), Is.EqualTo(1), "내림");
            Assert.That(FrontOfficeTrust.EffectiveCap(c, 100_000, 200_000), Is.EqualTo(6_100_000));
            Assert.That(FrontOfficeTrust.EffectiveCap(10, 100, 0), Is.Zero);
        }

        [Test]
        public void 예산을_TP로_바꿔_캡을_늘려도_두_시즌_순증은_쓴_돈을_넘지_않는다()
        {
            for (int tp = 0; tp <= 400; tp++)
            {
                int flex = FrontOfficeTrust.FlexFor(tp, FrontOfficeTrust.BaseSalaryCap);
                Assert.That(flex - FrontOfficeTrust.NextRepay(flex), Is.LessThanOrEqualTo(tp * FrontOfficeTrust.WonPerTrust), $"{tp} TP");
            }
        }

        [Test]
        public void 결렬되면_수수료가_예산에서_나가고_TP로_환급된다()
        {
            var s = SeasonState.CreateDummy();
            var random = new FixedRandom(0.99);
            var r = s.Negotiate(Ask, Ask, random);
            Assert.That((r.Attempted, r.Signed, r.CeilingUsed), Is.EqualTo((true, false, false)));
            Assert.That((r.Fee, r.TrustDelta), Is.EqualTo((Fee, 24)));
            Assert.That(r.SuccessRate, Is.EqualTo(0.5).Within(Eps));
            Assert.That((s.Budget, s.TrustPoints, s.TrustEarnedThisSeason), Is.EqualTo((5_000_000 - Fee, 24, 24)));
            Assert.That(random.Draws, Is.EqualTo(1));
        }

        [Test]
        public void 연속_결렬은_소프트_천장으로_확률을_올리고_TP_100에서_난수_없이_확정_영입한다()
        {
            var s = SeasonState.CreateDummy();
            var random = new FixedRandom(0.999);
            var rates = new double[5];
            for (int i = 0; i < 5; i++)
            {
                var r = s.Negotiate(Ask, Ask, random);
                Assert.That(r.Signed, Is.False);
                rates[i] = r.SuccessRate;
            }
            Assert.That(rates, Is.EqualTo(new[] { 0.50, 0.62, 0.74, 0.86, 0.98 }).Within(Eps));
            Assert.That(s.TrustPoints, Is.EqualTo(120));

            var ceiling = s.Negotiate(Ask, Ask, random);
            Assert.That((ceiling.Signed, ceiling.CeilingUsed, ceiling.SuccessRate), Is.EqualTo((true, true, 1.0)));
            Assert.That(ceiling.TrustDelta, Is.EqualTo(-100));
            Assert.That(s.TrustPoints, Is.EqualTo(20), "천장 카운터 100만 소모, 남는 TP는 유지");
            Assert.That(random.Draws, Is.EqualTo(5), "하드 천장은 난수를 뽑지 않는다");
            Assert.That(s.Budget, Is.EqualTo(5_000_000 - Fee * 6), "확정 영입도 수수료는 낸다");
        }

        [Test]
        public void 천장_전에_성공하면_천장_카운터가_초기화된다()
        {
            var s = SeasonState.CreateDummy();
            s.SetTrust(40, 40, 0, 0);
            var r = s.Negotiate(Ask, Ask, new FixedRandom(0.69));
            Assert.That((r.Signed, r.CeilingUsed, r.TrustDelta), Is.EqualTo((true, false, -40)));
            Assert.That(s.TrustPoints, Is.Zero);
        }

        [Test]
        public void 헐값_제시는_결렬돼도_환급이_없다()
        {
            var s = SeasonState.CreateDummy();
            var r = s.Negotiate(384_000, Ask, new FixedRandom(0.5)); // 80%
            Assert.That((r.Signed, r.TrustDelta, r.SuccessRate), Is.EqualTo((false, 0, 0.05)));
            Assert.That((s.Budget, s.TrustPoints), Is.EqualTo((5_000_000 - Fee, 0)));
        }

        [Test]
        public void 예산이_수수료보다_적으면_시도하지_않는다()
        {
            var s = SeasonState.CreateDummy();
            s.SetFinance(Fee - 1, 50);
            var random = new FixedRandom(0);
            var r = s.Negotiate(Ask, Ask, random);
            Assert.That((r.Attempted, r.Signed, r.Fee), Is.EqualTo((false, false, 0)));
            Assert.That((s.Budget, s.TrustPoints, random.Draws), Is.EqualTo((Fee - 1, 0, 0)));
            Assert.Throws<ArgumentNullException>(() => s.Negotiate(Ask, Ask, null));
        }

        [Test]
        public void TP는_200과_시즌_적립_150을_넘지_않고_시즌이_끝나면_100까지만_이월한다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.EarnTrust(160), Is.EqualTo(150));
            Assert.That(s.EarnTrust(10), Is.Zero, "시즌 적립 상한");
            Assert.That(s.EarnTrust(-5), Is.Zero);
            s.CloseTrustSeason();
            Assert.That((s.TrustPoints, s.TrustEarnedThisSeason), Is.EqualTo((100, 0)));
            Assert.That(s.EarnTrust(150), Is.EqualTo(100), "TP 상한 200");
            Assert.That(s.TrustPoints, Is.EqualTo(200));
        }

        [Test]
        public void 신뢰_대출은_TP만큼_캡을_늘리고_다음_시즌_절반을_상환한다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.SalaryCap, Is.EqualTo(6_000_000));
            s.SetTrust(150, 0, 0, 0);
            Assert.That(s.BorrowCap(100), Is.EqualTo(200_000));
            Assert.That((s.TrustPoints, s.CapFlex, s.SalaryCap), Is.EqualTo((50, 200_000, 6_200_000)));
            Assert.That(s.BorrowCap(500), Is.EqualTo(100_000), "가진 TP 50까지만");
            Assert.That(s.TrustPoints, Is.Zero);

            s.CloseTrustSeason();
            Assert.That((s.CapRepay, s.CapFlex, s.SalaryCap), Is.EqualTo((150_000, 0, 5_850_000)));
            s.CloseTrustSeason();
            Assert.That(s.SalaryCap, Is.EqualTo(6_000_000), "대출 없던 시즌 다음은 상환 없음");
        }

        [Test]
        public void 시즌_대출_한도는_기본_캡의_10퍼센트이고_필요한_TP만_쓴다()
        {
            var s = SeasonState.CreateDummy();
            s.SetTrust(200, 0, 0, 590_000);
            Assert.That(s.BorrowCap(10), Is.EqualTo(10_000), "남은 한도 ₩10,000");
            Assert.That((s.TrustPoints, s.CapFlex), Is.EqualTo((195, 600_000)), "5 TP만 소모");
            Assert.That(s.BorrowCap(10), Is.Zero);
            Assert.That(s.TrustPoints, Is.EqualTo(195), "한도가 차면 TP를 쓰지 않는다");
        }

        [Test]
        public void 신뢰도는_저장했다_불러와도_같고_없는_이전_파일은_0에서_시작한다()
        {
            var s = SeasonState.CreateDummy();
            s.SetTrust(150, 40, 100_000, 200_000);
            string json = SaveManager.ToJson(s);
            var loaded = SaveManager.FromJson(json);
            Assert.That((loaded.TrustPoints, loaded.TrustEarnedThisSeason, loaded.CapRepay, loaded.CapFlex),
                Is.EqualTo((150, 40, 100_000, 200_000)));
            Assert.That(loaded.SalaryCap, Is.EqualTo(6_100_000));

            string legacy = Regex.Replace(json, @",\s*""(HasTrust|TrustPoints|TrustEarnedThisSeason|CapRepay|CapFlex)"":\s*[^,\r\n}]+", "");
            Assert.That(legacy, Does.Not.Contain("Trust").And.Not.Contain("CapRepay").And.Not.Contain("CapFlex"));
            var old = SaveManager.FromJson(legacy);
            Assert.That((old.TrustPoints, old.TrustEarnedThisSeason, old.CapRepay, old.CapFlex), Is.EqualTo((0, 0, 0, 0)));
            Assert.That(old.Budget, Is.EqualTo(s.Budget), "나머지는 그대로");
        }

        [Test]
        public void 범위를_벗어난_신뢰도_저장값은_범위_안으로_맞춘다()
        {
            var data = SeasonState.CreateDummy().ToSaveData();
            data.TrustPoints = 999;
            data.TrustEarnedThisSeason = -4;
            data.CapRepay = -1;
            data.CapFlex = 9_999_999;
            var loaded = SeasonState.FromSaveData(data);
            Assert.That((loaded.TrustPoints, loaded.TrustEarnedThisSeason, loaded.CapRepay, loaded.CapFlex),
                Is.EqualTo((200, 0, 0, 600_000)));
        }
    }
}
