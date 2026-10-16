using System;
using System.Globalization;

namespace DailyFranchise.App
{
    /// <summary>재정 변화 한 번 (예산 증감, 팬 만족도 %p 증감). 예산은 long (Phase 26 화폐 현실화).</summary>
    public readonly struct FinanceChange : IEquatable<FinanceChange>
    {
        public FinanceChange(long budgetDelta, int fanDelta)
        {
            BudgetDelta = budgetDelta;
            FanDelta = fanDelta;
        }

        public long BudgetDelta { get; }
        public int FanDelta { get; }

        public bool Equals(FinanceChange other) => BudgetDelta == other.BudgetDelta && FanDelta == other.FanDelta;
        public override bool Equals(object obj) => obj is FinanceChange other && Equals(other);
        public override int GetHashCode() => BudgetDelta.GetHashCode() * 397 ^ FanDelta;
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
    /// 구단 재정 규칙 (Phase 21, Phase 26 화폐 현실화). 순수 C# — 경기 결과에 따른 예산·팬 만족도 변화와 범위 제한, 로비 표기.
    /// 예산 단위는 원(₩, long)이며 음수가 될 수 없다. 팬 만족도는 0~100%.
    ///
    /// 경기 결과별 관중 수입·팬 만족도 (내 구단 기준):
    /// - 승: +₩5,000만, +3%p
    /// - 무: +₩3,000만, ±0
    /// - 패: +₩2,000만 (관중이 줄어 수입 감소), −2%p
    /// 지출(Phase 26): 경기마다 선수단 유지비 = 페이롤 ÷ 144 (<see cref="PayrollPerGame"/>, <see cref="PostGameProcessor"/>).
    /// </summary>
    public static class FranchiseFinance
    {
        /// <summary>Phase 25 이전 화폐 단위 → 지금 단위 배수 (이전 저장 파일 변환용).</summary>
        public const long LegacyMoneyScale = 1_000;

        public const long DefaultBudget = 5_000_000_000;
        public const int DefaultFanApproval = 50;
        public const int MinFanApproval = 0;
        public const int MaxFanApproval = 100;

        public const long WinIncome = 50_000_000;
        public const long DrawIncome = 30_000_000;
        public const long LossIncome = 20_000_000;
        public const int WinFan = 3;
        public const int DrawFan = 0;
        public const int LossFan = -2;

        /// <summary>정규시즌 경기 수 — 페이롤을 경기마다 나눠 낸다.</summary>
        public const int GamesPerSeason = 144;

        /// <summary>이 값 이상이면 <see cref="FanMood.Happy"/>.</summary>
        public const int HappyFrom = 80;
        /// <summary>이 값 미만이면 <see cref="FanMood.Angry"/>.</summary>
        public const int AngryBelow = 30;

        static readonly string[] Weekdays = { "일", "월", "화", "수", "목", "금", "토" };

        /// <summary>내 구단 경기 결과에 따른 재정 변화 (관중 수입·팬 만족도).</summary>
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
        public const long UniformMerchIncome = 5_000_000;
        /// <summary>Happy 구간 패배는 팬 기대치 때문에 하락 폭이 이 배수가 된다 (스노우볼 브레이크).</summary>
        public const int HappyLossMultiplier = 2;

        public static int AttendancePercent(FanMood mood) =>
            mood == FanMood.Happy ? HappyAttendancePercent : mood == FanMood.Angry ? AngryAttendancePercent : 100;

        /// <summary>
        /// 팬덤 스노우볼 재정 변화 (Phase 23). 경기 전 팬 만족도 구간(<paramref name="mood"/>)과 해금 슬롯을 반영한다.
        /// - 예산: 기본 수입 × 관중 배수(0.8/1.0/1.2) + (승리 &amp; 유니폼 ? ₩500만 : 0)
        /// - 팬: 승 +3 (+1 세리머니), 무 0, 패 −2 (Happy면 ×2) (+1 치어리더)
        /// Normal 구간·슬롯 없음이면 <see cref="For(GameOutcome)"/>와 같다.
        /// </summary>
        public static FinanceChange For(GameOutcome outcome, FanMood mood, FandomSlot slots)
        {
            var basic = For(outcome);
            long budget = basic.BudgetDelta * AttendancePercent(mood) / 100;
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

        /// <summary>경기당 선수단 유지비 = 페이롤 ÷ 144 (내림). 음수 페이롤은 0.</summary>
        public static long PayrollPerGame(long payroll) => Math.Max(0, payroll) / GamesPerSeason;

        /// <summary>예산 범위 [0, long.MaxValue].</summary>
        public static long ClampBudget(long budget) => Math.Max(0, budget);

        /// <summary>넘침 없이 더한다: 결과는 [0, long.MaxValue].</summary>
        public static long AddBudget(long budget, long delta)
        {
            if (delta > 0 && budget > long.MaxValue - delta) return long.MaxValue;
            if (delta < 0 && budget < long.MinValue - delta) return 0;
            return ClampBudget(budget + delta);
        }

        /// <summary>팬 만족도 범위 [0, 100].</summary>
        public static int ClampFanApproval(long approval) => (int)Math.Max(MinFanApproval, Math.Min(MaxFanApproval, approval));

        public static FanMood MoodOf(int approval) =>
            approval >= HappyFrom ? FanMood.Happy : approval < AngryBelow ? FanMood.Angry : FanMood.Normal;

        /// <summary>
        /// 읽기 쉬운 금액 (부호 없이 절댓값): 1억 이상 "50억" / "46억 499만", 1만 이상 "5,000만", 그 아래 "9,999".
        /// 만 원 아래는 버린다 (표기용 — 정확한 값은 <see cref="ExactText"/>).
        /// </summary>
        public static string Won(long amount)
        {
            ulong v = amount < 0 ? (ulong)(-(amount + 1)) + 1 : (ulong)amount;
            ulong eok = v / 100_000_000, man = v % 100_000_000 / 10_000;
            var inv = CultureInfo.InvariantCulture;
            if (eok > 0) return man > 0 ? $"{eok.ToString("N0", inv)}억 {man.ToString("N0", inv)}만" : $"{eok.ToString("N0", inv)}억";
            if (man > 0) return $"{man.ToString("N0", inv)}만";
            return v.ToString("N0", inv);
        }

        /// <summary>"₩5,000,000,000" (툴팁용 정확한 값).</summary>
        public static string ExactText(long amount) =>
            (amount < 0 ? "−₩" : "₩") + (amount < 0 ? (ulong)(-(amount + 1)) + 1 : (ulong)amount).ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>"₩ 50억".</summary>
        public static string BudgetText(long budget) => "₩ " + Won(budget);

        /// <summary>"+₩5,000만" / "−₩2,791만" / "±₩0".</summary>
        public static string BudgetDeltaText(long delta) =>
            (delta > 0 ? "+" : delta < 0 ? "−" : "±") + "₩" + Won(delta);

        /// <summary>"+3%p" / "−2%p" / "±0%p".</summary>
        public static string FanDeltaText(int delta) =>
            (delta > 0 ? "+" : delta < 0 ? "−" : "±") + Math.Abs(delta).ToString(CultureInfo.InvariantCulture) + "%p";

        /// <summary>현실 날짜 "2026-10-16 (금)". 요일은 문화권과 무관하게 한글로.</summary>
        public static string TodayText(DateTime date) =>
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + $" ({Weekdays[(int)date.DayOfWeek]})";
    }
}
