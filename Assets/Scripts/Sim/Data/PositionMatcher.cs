using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Data
{
    /// <summary>한 선수가 어떤 수비 자리에 얼마나 맞는지.</summary>
    public enum PositionFit
    {
        /// <summary>맞지 않는다 (예: 포수 자리에 외야수).</summary>
        None = 0,
        /// <summary>같은 수비군 (내야 ↔ 내야, 외야 ↔ 외야, 지명타자 자리는 어느 타자나).</summary>
        Compatible = 1,
        /// <summary>같은 수비 위치.</summary>
        Exact = 2,
    }

    /// <summary>
    /// 수비 위치 매칭 (Phase 30, "김헌곤 룰"). 선발 타순에 빈자리가 났을 때 아무 벤치 선수나 올리지 않고
    /// 그 자리에 맞는 선수를 고른다. 순수 C#.
    /// - 같은 위치(<see cref="PositionFit.Exact"/>)가 먼저, 없으면 같은 수비군(<see cref="PositionFit.Compatible"/>).
    /// - 포수(C)는 전문 수비라 같은 수비군이 없다 — 포수 자리는 포수만, 포수는 다른 자리에 대신 서지 않는다.
    /// - 내야(1B·2B·3B·SS), 외야(LF·CF·RF)는 서로 메운다. 지명타자(DH) 자리는 포수가 아닌 어느 타자나 메운다.
    /// - 모르는 표기(예: "대타")의 자리는 포수가 아닌 어느 타자나 메운다.
    /// </summary>
    public static class PositionMatcher
    {
        public const string Catcher = "C";
        public const string DesignatedHitter = "DH";

        static readonly string[] Infield = { "1B", "2B", "3B", "SS" };
        static readonly string[] Outfield = { "LF", "CF", "RF" };

        /// <summary>신인 드래프트·부족 포지션 계산이 쓰는 야수 수비 위치 8개.</summary>
        public static readonly IReadOnlyList<string> FieldPositions = new[] { "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF" };

        static string Normalize(string position) => position?.Trim().ToUpperInvariant() ?? "";

        /// <summary>수비군: "C", "IF", "OF", "DH". 모르는 표기면 "".</summary>
        public static string GroupOf(string position)
        {
            string p = Normalize(position);
            if (p == Catcher) return Catcher;
            if (Infield.Contains(p)) return "IF";
            if (Outfield.Contains(p)) return "OF";
            if (p == DesignatedHitter) return DesignatedHitter;
            return "";
        }

        /// <summary><paramref name="playerPosition"/>의 선수가 <paramref name="slotPosition"/> 자리에 얼마나 맞는지.</summary>
        public static PositionFit FitOf(string slotPosition, string playerPosition)
        {
            string slot = Normalize(slotPosition), player = Normalize(playerPosition);
            if (slot.Length > 0 && slot == player) return PositionFit.Exact;
            if (slot == Catcher || player == Catcher) return PositionFit.None;
            string group = GroupOf(slot);
            if (group == DesignatedHitter || group.Length == 0) return PositionFit.Compatible;
            return group == GroupOf(player) ? PositionFit.Compatible : PositionFit.None;
        }

        /// <summary>
        /// 후보 목록(여러 묶음이면 앞 묶음이 우선)에서 자리에 가장 잘 맞는 선수. 같은 위치가 어느 묶음에든 있으면 그 선수가
        /// 같은 수비군보다 먼저다 (벤치 같은 위치 → 퓨처스 같은 위치 → 벤치 같은 수비군 → 퓨처스 같은 수비군).
        /// 맞는 선수가 없으면 null.
        /// </summary>
        public static PlayerData Pick(string slotPosition, params IEnumerable<PlayerData>[] pools)
        {
            foreach (var fit in new[] { PositionFit.Exact, PositionFit.Compatible })
                foreach (var pool in pools)
                {
                    var p = pool?.FirstOrDefault(x => x != null && !x.IsPitcher && FitOf(slotPosition, x.Position) == fit);
                    if (p != null) return p;
                }
            return null;
        }

        /// <summary>표시용 이름: "포수", "1루수" … 모르면 원래 표기.</summary>
        public static string Label(string position)
        {
            switch (Normalize(position))
            {
                case "C": return "포수";
                case "1B": return "1루수";
                case "2B": return "2루수";
                case "3B": return "3루수";
                case "SS": return "유격수";
                case "LF": return "좌익수";
                case "CF": return "중견수";
                case "RF": return "우익수";
                case "DH": return "지명타자";
                case "P": return "투수";
                default: return position ?? "";
            }
        }
    }
}
