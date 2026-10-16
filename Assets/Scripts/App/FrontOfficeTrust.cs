using System;
using DailyFranchise.Sim;

namespace DailyFranchise.App
{
    /// <summary>선수 영입 협상 한 번의 결과 (<see cref="SeasonState.Negotiate"/>).</summary>
    public readonly struct NegotiationResult
    {
        public NegotiationResult(bool attempted, bool signed, bool ceilingUsed, double successRate, int fee, int trustDelta)
        {
            Attempted = attempted;
            Signed = signed;
            CeilingUsed = ceilingUsed;
            SuccessRate = successRate;
            Fee = fee;
            TrustDelta = trustDelta;
        }

        /// <summary>예산이 수수료보다 적으면 false (아무것도 바뀌지 않는다).</summary>
        public bool Attempted { get; }
        public bool Signed { get; }
        /// <summary>하드 천장(TP ≥ <see cref="FrontOfficeTrust.Ceiling"/>)으로 확정 영입했는지.</summary>
        public bool CeilingUsed { get; }
        /// <summary>판정에 쓴 성공률 (소프트 천장 포함).</summary>
        public double SuccessRate { get; }
        /// <summary>예산에서 나간 협상 수수료.</summary>
        public int Fee { get; }
        /// <summary>신뢰도 변화: 결렬 환급(+) 또는 성공 시 천장 초기화(−).</summary>
        public int TrustDelta { get; }
    }

    /// <summary>
    /// 프런트 신뢰도(TP)·천장·샐러리캡 유동성 규칙 (Phase 23). 순수 C# — 상태는 <see cref="SeasonState"/>가 들고 있다.
    ///
    /// 연봉·협상:
    /// - 연봉 = ₩30,000 + max(0, WAR) × ₩150,000, 기본 샐러리캡 C = ₩6,000,000 (평균 팀 연봉 ≈ C)
    /// - 협상 수수료 = 요구액 × 5% (시도마다 예산에서 나간다)
    /// - 기본 성공률 = clamp(0.5 + 2.5 × (제시/요구 − 1), 5%, 95%)
    /// - 소프트 천장: + 0.5 × min(TP, 100) / 100 (일반 협상)
    /// - 하드 천장 (Phase 25): TP ≥ 100이면 [확정 영입]을 따로 고를 수 있다 — TP 100을 쓰고 요구액 그대로 100% 영입
    /// - 결렬 환급: 제시/요구 ≥ 0.9(성의 있는 제시)일 때만 TP += floor(수수료 / ₩1,000)
    /// - 일반 협상이 성공하면 천장 카운터가 초기화된다: TP −= min(TP, 100)
    /// - TP 상한 200, 한 시즌 적립 상한 150, 시즌이 끝나면 100까지만 이월
    ///
    /// 신뢰 대출 (캡 유동성):
    /// - Flex = min(10% × C, 쓴 TP × ₩2,000) — 이번 시즌 캡을 늘린다
    /// - 다음 시즌 상환 Repay = 50% × Flex — 다음 시즌 캡에서 뺀다
    /// - 시즌 캡 = C − Repay + Flex
    /// ₩1,000 수수료 → 1 TP → 캡 ₩2,000 − 상환 ₩1,000 = 순증 ₩1,000이라 예산과 캡이 1:1로만 바뀐다 (무한 증식 없음).
    /// </summary>
    public static class FrontOfficeTrust
    {
        public const int Ceiling = 100;
        public const int MaxTrust = 200;
        public const int MaxSeasonEarn = 150;
        public const int CarryOver = 100;
        public const int WonPerTrust = 1_000;
        public const int CapPerTrust = 2_000;
        public const double SoftPityWeight = 0.5;
        public const double FeeRate = 0.05;
        public const double MinSuccessRate = 0.05;
        public const double MaxSuccessRate = 0.95;
        public const double MaxFlexRate = 0.10;
        public const double RepayRate = 0.5;

        public const int BaseSalaryCap = 6_000_000;
        public const int MinSalary = 30_000;
        public const int SalaryPerWar = 150_000;

        /// <summary>WAR로 정한 시즌 연봉 (음수 WAR는 최저 연봉).</summary>
        public static int SalaryOf(double war) =>
            MinSalary + (int)Math.Round(Math.Max(0, double.IsNaN(war) ? 0 : war) * SalaryPerWar, MidpointRounding.AwayFromZero);

        /// <summary>협상 수수료 = 요구액 × 5% (반올림).</summary>
        public static int FeeOf(int ask) => (int)Math.Round(Math.Max(0, ask) * FeeRate, MidpointRounding.AwayFromZero);

        /// <summary>제시액이 요구액의 90% 이상인지 (결렬 환급 대상).</summary>
        public static bool IsGoodFaith(int offer, int ask) => ask > 0 && (long)offer * 10 >= (long)ask * 9; // 정수로 90% 비교

        /// <summary>천장 보정 없는 성공률 = clamp(0.5 + 2.5 × (제시/요구 − 1), 5%, 95%).</summary>
        public static double BaseSuccessRate(int offer, int ask)
        {
            if (ask <= 0) throw new ArgumentOutOfRangeException(nameof(ask), ask, "요구액은 양수");
            double r = Math.Max(0, offer) / (double)ask;
            return Math.Max(MinSuccessRate, Math.Min(MaxSuccessRate, 0.5 + 2.5 * (r - 1)));
        }

        /// <summary>
        /// 일반 협상 성공률 (소프트 천장) = min(1, 기본 + 0.5 × min(TP, 100) / 100).
        /// 하드 천장은 확률이 아니라 별도 행동이다 (<see cref="CanUseCeiling"/>, <see cref="SeasonState.SignWithCeiling"/>).
        /// </summary>
        public static double SuccessRate(int offer, int ask, int trust)
        {
            double rate = BaseSuccessRate(offer, ask);
            return Math.Min(1, rate + SoftPityWeight * Math.Max(0, Math.Min(trust, Ceiling)) / Ceiling);
        }

        /// <summary>[확정 영입]을 쓸 수 있는지 (TP ≥ 100).</summary>
        public static bool CanUseCeiling(int trust) => trust >= Ceiling;

        /// <summary>결렬 시 환급 TP. 성의 없는 제시(90% 미만)는 0.</summary>
        public static int RefundOnFailure(int offer, int ask) => IsGoodFaith(offer, ask) ? FeeOf(ask) / WonPerTrust : 0;

        /// <summary>이번 시즌 캡 증가분 = min(10% × 기본 캡, 쓴 TP × ₩2,000).</summary>
        public static int FlexFor(int trustSpent, int baseCap) =>
            (int)Math.Min(MaxFlexOf(baseCap), (long)Math.Max(0, trustSpent) * CapPerTrust);

        public static int MaxFlexOf(int baseCap) => (int)(Math.Max(0, baseCap) * MaxFlexRate);

        /// <summary>다음 시즌 상환액 = 50% × Flex (내림).</summary>
        public static int NextRepay(int flex) => (int)(Math.Max(0, flex) * RepayRate);

        /// <summary>로비 표기 "24 / 200".</summary>
        public static string TrustText(int trust) => $"{trust} / {MaxTrust}";

        /// <summary>로비 툴팁: 천장까지 남은 TP, 천장 도달이면 "확정 영입 가능".</summary>
        public static string CeilingText(int trust) =>
            trust >= Ceiling ? "천장 도달 · 확정 영입 가능" : $"천장까지 {Ceiling - trust} TP";

        /// <summary>시즌 캡 = 기본 캡 − 상환 + Flex (0 이상).</summary>
        public static int EffectiveCap(int baseCap, int repay, int flex) => (int)Math.Max(0, (long)baseCap - repay + flex);
    }
}
