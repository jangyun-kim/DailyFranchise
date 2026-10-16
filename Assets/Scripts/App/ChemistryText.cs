using System;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>로비 라인업의 팀 케미스트리 문구와 선수 성향 배지 (Phase 24). 순수 C#.</summary>
    public static class ChemistryText
    {
        public const string None = "활성화된 케미스트리 없음";

        /// <summary>"활성화됨: 발야구 · 거포 클린업". 없거나 모르면 <see cref="None"/>.</summary>
        public static string Summary(ActiveChemistry active) =>
            active == null || active.Kinds.Count == 0
                ? None
                : "활성화됨: " + string.Join(" · ", active.Kinds.Select(ChemistryRules.Label));

        /// <summary>바뀌었는지 비교할 열쇠 (발동 종류 + 멤버). 같으면 다시 그리지 않는다.</summary>
        public static string Key(ActiveChemistry active) =>
            active == null ? "" : string.Join("|", active.Kinds.Select(k =>
                k + ":" + string.Join(",", active.Members(k).OrderBy(id => id, StringComparer.Ordinal))));

        /// <summary>라인업 행 배지 글자: 주(주루)·수(수비)·장(장타). 태그가 없으면 빈 문자열.</summary>
        public static string Badge(ChemistryTag tag)
        {
            switch (tag)
            {
                case ChemistryTag.Speed: return "주";
                case ChemistryTag.Glove: return "수";
                case ChemistryTag.Power: return "장";
                default: return "";
            }
        }

        /// <summary>배지 USS 클래스 (chem-badge--speed 등). 태그가 없으면 chem-badge--none.</summary>
        public static string BadgeClass(ChemistryTag tag)
        {
            switch (tag)
            {
                case ChemistryTag.Speed: return "chem-badge--speed";
                case ChemistryTag.Glove: return "chem-badge--glove";
                case ChemistryTag.Power: return "chem-badge--power";
                default: return "chem-badge--none";
            }
        }

        /// <summary>배지 툴팁: "주루 · 도루 시도율 리그 상위 20%".</summary>
        public static string BadgeTooltip(ChemistryTag tag)
        {
            int top = (int)Math.Round(ChemistryTagger.TopPercent * 100);
            switch (tag)
            {
                case ChemistryTag.Speed: return $"주루 · 도루 시도율 리그 상위 {top}%";
                case ChemistryTag.Glove: return $"수비 · 600타석 dWAR 리그 상위 {top}%";
                case ChemistryTag.Power: return $"장타 · ISO 리그 상위 {top}%";
                default: return "";
            }
        }
    }
}
