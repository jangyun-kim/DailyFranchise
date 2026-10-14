using System;
using System.Globalization;

namespace DailyFranchise.Sim
{
    /// <summary>
    /// 장면 선수 ID의 타순 순환. 장면 타자 ID는 "&lt;쪽&gt;_b&lt;타순&gt;" (예: home_b3 = 내 구단 3번)이고,
    /// 화면은 이 ID를 선수단 자리(<c>ScenarioRosterSource</c>)로 바꿔 실제 선수로 그린다.
    /// </summary>
    public static class BattingOrder
    {
        public const int Size = 9;

        /// <summary>타순 (1~9). 타순 자리 ID가 아니면 null.</summary>
        public static int? OrderOf(string batterId)
        {
            if (!TrySplit(batterId, out _, out int order)) return null;
            return order;
        }

        /// <summary>같은 팀 order번 타자 ID (예: ("home_b3", 4) → "home_b4").</summary>
        public static string IdFor(string sameTeamBatterId, int order)
        {
            if (order < 1 || order > Size) throw new ArgumentOutOfRangeException(nameof(order), order, "타순은 1~9");
            if (!TrySplit(sameTeamBatterId, out string prefix, out _))
                throw new ArgumentException($"타순 자리 ID가 아니다: {sameTeamBatterId}", nameof(sameTeamBatterId));
            return prefix + order.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>다음 타자 ID. 9번 다음은 1번.</summary>
        public static string Next(string batterId)
        {
            int order = OrderOf(batterId) ?? throw new ArgumentException($"타순 자리 ID가 아니다: {batterId}", nameof(batterId));
            return IdFor(batterId, order % Size + 1);
        }

        static bool TrySplit(string id, out string prefix, out int order)
        {
            prefix = null;
            order = 0;
            if (id == null) return false;
            int at = id.LastIndexOf("_b", StringComparison.Ordinal);
            if (at < 0) return false;
            string digits = id.Substring(at + 2);
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out order) || order < 1 || order > Size)
                return false;
            prefix = id.Substring(0, at + 2);
            return true;
        }
    }
}
