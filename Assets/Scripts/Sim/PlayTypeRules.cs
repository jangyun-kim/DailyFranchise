namespace DailyFranchise.Sim
{
    public static class PlayTypeRules
    {
        /// <summary>페어 타구 각도 한계 (파울라인).</summary>
        public const double FairAngleLimitDeg = 45.0;
        /// <summary>아웃 타구 각도 한계 (파울 지역 뜬공 포함).</summary>
        public const double MaxAngleDeg = 90.0;

        public static bool IsHit(PlayType t) =>
            t == PlayType.Single || t == PlayType.Double || t == PlayType.Triple || t == PlayType.HomeRun;

        public static bool IsOut(PlayType t) =>
            t == PlayType.GroundOut || t == PlayType.FlyOut || t == PlayType.LineOut || t == PlayType.SacrificeBunt;

        /// <summary>안타로 타자가 진루하는 루 수 (홈런 = 4). 안타가 아니면 0.</summary>
        public static int BasesForHit(PlayType t)
        {
            switch (t)
            {
                case PlayType.Single: return 1;
                case PlayType.Double: return 2;
                case PlayType.Triple: return 3;
                case PlayType.HomeRun: return 4;
                default: return 0;
            }
        }

        /// <summary>
        /// 타석이 타수로 집계되는지. 볼넷, 희생플라이(3아웃 전 득점이 난 뜬공 아웃), 희생 번트는 제외.
        /// </summary>
        public static bool CountsAsAtBat(PitchEvent e)
        {
            switch (e.Outcome)
            {
                case PlateAppearanceOutcome.Strikeout:
                    return true;
                case PlateAppearanceOutcome.InPlay:
                    bool sacrificeFly = e.PlayType == PlayType.FlyOut && e.RunsScored > 0 && e.OutsAfter < 3;
                    return !sacrificeFly && e.PlayType != PlayType.SacrificeBunt;
                default:
                    return false;
            }
        }
    }
}
