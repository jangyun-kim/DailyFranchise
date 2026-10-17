using System;

namespace DailyFranchise.Sim.Data
{
    /// <summary>
    /// 라인업 맞바꾸기 수비 위치 규칙 (Phase 40). 선발 타순 자리(<paramref name="slotPosition"/>)에 들어갈 선수를 거른다.
    /// - 투수는 타순 자리에 서지 않는다 (투수끼리는 로테이션·불펜으로 따로 운용).
    /// - 그 밖은 <see cref="PositionMatcher.FitOf"/>: 포수 자리는 포수만, 포수는 다른 자리에 서지 않는다, 내야 ↔ 내야, 외야 ↔ 외야,
    ///   지명타자·모르는 표기 자리는 포수가 아닌 어느 타자나.
    /// </summary>
    public static class LineupFit
    {
        public static bool Allows(string slotPosition, PlayerData player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            return !player.IsPitcher && PositionMatcher.FitOf(slotPosition, player.Position) != PositionFit.None;
        }
    }
}
