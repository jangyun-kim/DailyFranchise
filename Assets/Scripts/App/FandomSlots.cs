using System;

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
        /// <summary>유니폼 — 시즌 10승에 해금. 승리 시 굿즈 수입 +₩5,000.</summary>
        Uniform = 1,
        /// <summary>세리머니 — 팬 만족도 70 첫 도달에 해금. 승리 시 팬 만족도 +1%p.</summary>
        Ceremony = 2,
        /// <summary>치어리더 — 팬 만족도 80 첫 도달에 해금. 패배 시 팬 만족도 하락 1%p 완화.</summary>
        Cheerleader = 4,
        All = Uniform | Ceremony | Cheerleader,
    }

    /// <summary>팬덤 슬롯 해금 조건 (순수 C#).</summary>
    public static class FandomRules
    {
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
                case FandomSlot.Uniform: return $"시즌 {UniformWins}승에 해금 · 승리 시 굿즈 수입 +₩5,000";
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
