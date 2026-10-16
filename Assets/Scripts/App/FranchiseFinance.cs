using System;
using System.Globalization;

namespace DailyFranchise.App
{
    /// <summary>재정 변화 한 번 (예산 증감, 팬 만족도 %p 증감).</summary>
    public readonly struct FinanceChange : IEquatable<FinanceChange>
    {
        public FinanceChange(int budgetDelta, int fanDelta)
        {
            BudgetDelta = budgetDelta;
            FanDelta = fanDelta;
        }

        public int BudgetDelta { get; }
        public int FanDelta { get; }

        public bool Equals(FinanceChange other) => BudgetDelta == other.BudgetDelta && FanDelta == other.FanDelta;
        public override bool Equals(object obj) => obj is FinanceChange other && Equals(other);
        public override int GetHashCode() => BudgetDelta * 397 ^ FanDelta;
        public override string ToString() => $"{BudgetDelta:+#;-#;0} / {FanDelta:+#;-#;0}%p";
    }

    /// <summary>팬 만족도 구간 (로비 게이지 색).</summary>
    public enum FanMood
    {
        /// <summary>30% 미만 (빨강).</summary>
        Angry,
        Normal,
        /// <summary>80% 이상 (초록).</summary>
        Happy,
    }

    /// <summary>
    /// 구단 재정 규칙 (Phase 21). 순수 C# — 경기 결과에 따른 예산·팬 만족도 변화와 범위 제한, 로비 표기.
    /// 예산 단위는 원(₩)이며 음수가 될 수 없다. 팬 만족도는 0~100%.
    ///
    /// 경기 결과별 변화 (내 구단 기준):
    /// - 승: 관중 수입 +₩50,000, 팬 만족도 +3%p
    /// - 무: 관중 수입 +₩30,000, 팬 만족도 ±0
    /// - 패: 관중 수입 +₩20,000 (관중이 줄어 수입 감소), 팬 만족도 −2%p
    /// 이번 Phase는 수입만 있고 지출(연봉·FA 등)은 없다.
    /// </summary>
    public static class FranchiseFinance
    {
        public const int DefaultBudget = 5_000_000;
        public const int DefaultFanApproval = 50;
        public const int MinFanApproval = 0;
        public const int MaxFanApproval = 100;

        public const int WinIncome = 50_000;
        public const int DrawIncome = 30_000;
        public const int LossIncome = 20_000;
        public const int WinFan = 3;
        public const int DrawFan = 0;
        public const int LossFan = -2;

        /// <summary>이 값 이상이면 <see cref="FanMood.Happy"/>.</summary>
        public const int HappyFrom = 80;
        /// <summary>이 값 미만이면 <see cref="FanMood.Angry"/>.</summary>
        public const int AngryBelow = 30;

        static readonly string[] Weekdays = { "일", "월", "화", "수", "목", "금", "토" };

        /// <summary>내 구단 경기 결과에 따른 재정 변화.</summary>
        public static FinanceChange For(GameOutcome outcome)
        {
            switch (outcome)
            {
                case GameOutcome.Win: return new FinanceChange(WinIncome, WinFan);
                case GameOutcome.Draw: return new FinanceChange(DrawIncome, DrawFan);
                case GameOutcome.Loss: return new FinanceChange(LossIncome, LossFan);
                default: throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
            }
        }

        /// <summary>관중 배수 (%) — 경기 전 팬 만족도 구간 기준. Angry 80 / Normal 100 / Happy 120.</summary>
        public const int AngryAttendancePercent = 80;
        public const int HappyAttendancePercent = 120;
        /// <summary>유니폼 슬롯: 승리 시 굿즈 수입.</summary>
        public const int UniformMerchIncome = 5_000;
        /// <summary>Happy 구간 패배는 팬 기대치 때문에 하락 폭이 이 배수가 된다 (스노우볼 브레이크).</summary>
        public const int HappyLossMultiplier = 2;

        public static int AttendancePercent(FanMood mood) =>
            mood == FanMood.Happy ? HappyAttendancePercent : mood == FanMood.Angry ? AngryAttendancePercent : 100;

        /// <summary>
        /// 팬덤 스노우볼 재정 변화 (Phase 23). 경기 전 팬 만족도 구간(<paramref name="mood"/>)과 해금 슬롯을 반영한다.
        /// - 예산: 기본 수입 × 관중 배수(0.8/1.0/1.2) + (승리 &amp; 유니폼 ? ₩5,000 : 0)
        /// - 팬: 승 +3 (+1 세리머니), 무 0, 패 −2 (Happy면 ×2) (+1 치어리더)
        /// Normal 구간·슬롯 없음이면 <see cref="For(GameOutcome)"/>와 같다.
        /// </summary>
        public static FinanceChange For(GameOutcome outcome, FanMood mood, FandomSlot slots)
        {
            var basic = For(outcome);
            int budget = (int)((long)basic.BudgetDelta * AttendancePercent(mood) / 100);
            int fan = basic.FanDelta;
            switch (outcome)
            {
                case GameOutcome.Win:
                    if ((slots & FandomSlot.Uniform) != 0) budget += UniformMerchIncome;
                    if ((slots & FandomSlot.Ceremony) != 0) fan += 1;
                    break;
                case GameOutcome.Loss:
                    if (mood == FanMood.Happy) fan *= HappyLossMultiplier;
                    if ((slots & FandomSlot.Cheerleader) != 0) fan += 1;
                    break;
            }
            return new FinanceChange(budget, fan);
        }

        /// <summary>예산 범위 [0, int.MaxValue]. 넘침 없이 long으로 받는다.</summary>
        public static int ClampBudget(long budget) => (int)Math.Max(0, Math.Min(int.MaxValue, budget));

        /// <summary>팬 만족도 범위 [0, 100].</summary>
        public static int ClampFanApproval(long approval) => (int)Math.Max(MinFanApproval, Math.Min(MaxFanApproval, approval));

        public static FanMood MoodOf(int approval) =>
            approval >= HappyFrom ? FanMood.Happy : approval < AngryBelow ? FanMood.Angry : FanMood.Normal;

        /// <summary>"₩ 5,000,000".</summary>
        public static string BudgetText(int budget) => "₩ " + budget.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>"+₩50,000" / "−₩10,000" / "±₩0".</summary>
        public static string BudgetDeltaText(int delta) =>
            (delta > 0 ? "+" : delta < 0 ? "−" : "±") + "₩" + Math.Abs((long)delta).ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>"+3%p" / "−2%p" / "±0%p".</summary>
        public static string FanDeltaText(int delta) =>
            (delta > 0 ? "+" : delta < 0 ? "−" : "±") + Math.Abs(delta).ToString(CultureInfo.InvariantCulture) + "%p";

        /// <summary>현실 날짜 "2026-10-16 (금)". 요일은 문화권과 무관하게 한글로.</summary>
        public static string TodayText(DateTime date) =>
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + $" ({Weekdays[(int)date.DayOfWeek]})";
    }
}
