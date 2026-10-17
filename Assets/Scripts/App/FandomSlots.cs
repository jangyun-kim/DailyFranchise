using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.App
{
    /// <summary>
    /// 팬덤 슬롯 (Phase 23). 효과는 플레이로 해금한 슬롯에 붙고, 유료 아이템은 슬롯을 채우는 외형(스킨)만 바꾼다.
    /// 슬롯마다 같은 효과를 내는 무료 아이템이 있으므로 결제 여부는 재정에 영향을 주지 않는다 (P2W 0).
    /// 이 타입은 App 어셈블리에만 있어 확률 엔진(Sim)이 참조할 수 없다.
    /// </summary>
    [Flags]
    public enum FandomSlot
    {
        None = 0,
        /// <summary>유니폼 — 시즌 10승에 해금. 승리 시 굿즈 수입 +₩500만.</summary>
        Uniform = 1,
        /// <summary>세리머니 — 팬 만족도 70 첫 도달에 해금. 승리 시 팬 만족도 +1%p.</summary>
        Ceremony = 2,
        /// <summary>치어리더 — 팬 만족도 80 첫 도달에 해금. 패배 시 팬 만족도 하락 1%p 완화.</summary>
        Cheerleader = 4,
        All = Uniform | Ceremony | Cheerleader,
    }

    /// <summary>팬덤 레벨업 한 번의 보상 (Phase 42, <see cref="SeasonState.CheckFandomLevelUp"/>).</summary>
    public sealed class FandomLevelUp
    {
        public int Level;
        public long Budget;
        public int CampPoints;
        public int Trust;
        /// <summary>이번 레벨에 해금된 로비 테마 (없으면 null).</summary>
        public string Theme;
        /// <summary>보상 문구 ("특별 예산 ₩ 3억 · 클래식 올드 유니폼 테마").</summary>
        public string Text => string.Join(" · ", new[]
        {
            Budget > 0 ? "특별 예산 " + FranchiseFinance.BudgetText(Budget) : null,
            CampPoints > 0 ? $"캠프 포인트 +{CampPoints} CP" : null,
            Trust > 0 ? $"신뢰도 +{Trust} TP" : null,
            Theme != null ? $"'{Theme}' 테마" : null,
        }.Where(t => t != null));
    }

    /// <summary>팬덤 슬롯 해금 조건 (순수 C#).</summary>
    public static class FandomRules
    {
        // ---------- Phase 42: 팬덤 레벨 ----------

        /// <summary>팬 만족도가 이 값에 닿으면 팬덤 레벨이 오르고 게이지가 <see cref="LevelResetApproval"/>로 돌아간다.</summary>
        public const int LevelUpApproval = 100;
        public const int LevelResetApproval = 60;
        /// <summary>레벨업마다: 굿즈 완판 특별 예산.</summary>
        public const long LevelBudgetBonus = 300_000_000;
        /// <summary>Lv.1 클래식 올드 유니폼 테마 · Lv.2 캠프 포인트 · Lv.3 신뢰도 (그 뒤는 예산만).</summary>
        public const string ClassicTheme = "클래식 올드 유니폼";
        public const int ClassicThemeLevel = 1;
        public const int Level2CampPoints = 2;
        public const int Level3Trust = 5;
        /// <summary>1·2위 구단(라이벌)을 이기면 팬 만족도 추가 +1%p.</summary>
        public const int RivalWinFan = 1;
        public const int RivalMaxRank = 2;

        /// <summary>그 레벨에 오를 때 받는 보상.</summary>
        public static FandomLevelUp RewardFor(int level) => new FandomLevelUp
        {
            Level = level,
            Budget = LevelBudgetBonus,
            CampPoints = level == 2 ? Level2CampPoints : 0,
            Trust = level == 3 ? Level3Trust : 0,
            Theme = level == ClassicThemeLevel ? ClassicTheme : null,
        };

        /// <summary>그 레벨까지 해금한 특전 목록 (Front Office 모달). 레벨 0이면 빈 목록.</summary>
        public static IReadOnlyList<FandomLevelUp> RewardsUpTo(int level) =>
            Enumerable.Range(1, Math.Max(0, level)).Select(RewardFor).ToList();

        /// <summary>클래식 테마를 해금했는지.</summary>
        public static bool HasClassicTheme(int level) => level >= ClassicThemeLevel;

        public const int UniformWins = 10;
        public const int CeremonyFan = 70;
        public const int CheerleaderFan = 80;

        /// <summary>지금 성적·팬 만족도로 해금되는 슬롯 전체 (이미 해금된 것 포함). 한 번 해금되면 잃지 않는다.</summary>
        public static FandomSlot Unlocked(FandomSlot current, int seasonWins, int fanApproval)
        {
            var slots = current & FandomSlot.All;
            if (seasonWins >= UniformWins) slots |= FandomSlot.Uniform;
            if (fanApproval >= CeremonyFan) slots |= FandomSlot.Ceremony;
            if (fanApproval >= CheerleaderFan) slots |= FandomSlot.Cheerleader;
            return slots;
        }

        /// <summary>로비 표시 순서.</summary>
        public static readonly FandomSlot[] Slots = { FandomSlot.Uniform, FandomSlot.Ceremony, FandomSlot.Cheerleader };

        /// <summary>해금 조건과 효과 (로비 툴팁).</summary>
        public static string Hint(FandomSlot slot)
        {
            switch (slot)
            {
                case FandomSlot.Uniform: return $"시즌 {UniformWins}승에 해금 · 승리 시 굿즈 수입 {FranchiseFinance.BudgetDeltaText(FranchiseFinance.UniformMerchIncome)}";
                case FandomSlot.Ceremony: return $"팬 만족도 {CeremonyFan}% 도달 시 해금 · 승리 시 팬 만족도 +1%p";
                case FandomSlot.Cheerleader: return $"팬 만족도 {CheerleaderFan}% 도달 시 해금 · 패배 시 팬 하락 1%p 완화";
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "슬롯 하나만");
            }
        }

        public static string Label(FandomSlot slot)
        {
            switch (slot)
            {
                case FandomSlot.Uniform: return "유니폼";
                case FandomSlot.Ceremony: return "세리머니";
                case FandomSlot.Cheerleader: return "치어리더";
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "슬롯 하나만");
            }
        }
    }
}
