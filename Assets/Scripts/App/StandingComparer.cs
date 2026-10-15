using System;
using System.Collections.Generic;

namespace DailyFranchise.App
{
    /// <summary>
    /// 순위표 정렬 규칙 (앞에 올수록 작다). KBO 순위 규정을 단순화해 승률 → 다승 → 무승부 → 잔여 경기 순으로 비교한다.
    /// - 승률: 높을수록 위 (무승부 제외 승 / (승 + 패))
    /// - 다승: 많을수록 위
    /// - 무승부: 많을수록 위 (같은 승·패면 지지 않은 경기가 더 많다)
    /// - 잔여 경기: 많을수록 위 (뒤집을 기회가 더 남았다)
    /// 모두 같으면 0 (같은 순위). 상대 전적·득실은 아직 기록하지 않으므로 쓰지 않는다.
    /// </summary>
    public sealed class StandingComparer : IComparer<StandingRow>
    {
        public StandingComparer(int gamesPerTeam)
        {
            if (gamesPerTeam <= 0) throw new ArgumentOutOfRangeException(nameof(gamesPerTeam), gamesPerTeam, "팀당 경기 수는 1 이상이어야 한다.");
            GamesPerTeam = gamesPerTeam;
        }

        /// <summary>팀당 시즌 경기 수. 잔여 경기 = 이 값 - 치른 경기.</summary>
        public int GamesPerTeam { get; }

        public int RemainingGames(StandingRow row) => Math.Max(0, GamesPerTeam - row.GamesPlayed);

        public int Compare(StandingRow x, StandingRow y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return 1;
            if (y == null) return -1;
            int c = y.WinPct.CompareTo(x.WinPct);
            if (c != 0) return c;
            c = y.Wins.CompareTo(x.Wins);
            if (c != 0) return c;
            c = y.Draws.CompareTo(x.Draws);
            if (c != 0) return c;
            return RemainingGames(y).CompareTo(RemainingGames(x));
        }
    }
}
