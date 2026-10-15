using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DailyFranchise.Sim.Data
{
    /// <summary>팀 내 기록 1위 부문.</summary>
    public enum LeaderCategory
    {
        /// <summary>타율 (높을수록 위).</summary>
        BattingAverage,
        /// <summary>홈런 (많을수록 위).</summary>
        HomeRuns,
        /// <summary>평균자책 (낮을수록 위). 선발 투수만.</summary>
        Era,
        /// <summary>세이브 (많을수록 위). 불펜 투수만.</summary>
        Saves,
    }

    /// <summary>한 부문의 팀 내 1위 선수.</summary>
    public sealed class TeamLeader
    {
        public LeaderCategory Category;
        public PlayerData Player;

        /// <summary>부문 이름 (예: "타율").</summary>
        public string CategoryText => RosterAnalyzer.CategoryName(Category);
        /// <summary>기록 표기 (예: ".318", "19개", "3.55").</summary>
        public string ValueText => RosterAnalyzer.ValueText(Category, Player);
    }

    /// <summary>
    /// 선수단에서 부문별 팀 내 1위를 뽑는다. 기록은 선수 DB(마스터 파일)의 시즌 기록 그대로다 (경기별 누적 없음).
    /// 자격:
    /// - 타자 부문: 선발 타순(라인업) 야수 중 타격 기록이 있는 선수. 벤치·투수·기록 없는 선수(타율·OPS·홈런 모두 0)는 뺀다.
    ///   DB에 타석 수가 없으므로 "라인업에 들어 있고 기록이 있다"를 규정 타석 대신 쓴다.
    /// - 평균자책: 평균자책 기록(0 초과)이 있는 선발 투수. 짧게 던진 불펜 기록이 1위를 가로채지 않게 한다.
    /// - 세이브: 세이브가 1개 이상인 불펜 투수.
    /// 동률이면 선수단 순서(타순·표시 순서)가 앞선 선수.
    /// </summary>
    public static class RosterAnalyzer
    {
        /// <summary>로비에 보이는 순서.</summary>
        public static readonly IReadOnlyList<LeaderCategory> DashboardCategories = new[]
        {
            LeaderCategory.BattingAverage, LeaderCategory.HomeRuns, LeaderCategory.Era, LeaderCategory.Saves,
        };

        public static bool HasBattingRecord(PlayerData p) => p.BattingAverage > 0 || p.Ops > 0 || p.HomeRuns > 0;

        /// <summary>타자 부문 자격 선수 (라인업 야수 중 타격 기록이 있는 선수).</summary>
        public static IEnumerable<PlayerData> QualifiedHitters(TeamRoster roster) =>
            roster.Lineup.Where(p => !p.IsPitcher && HasBattingRecord(p));

        /// <summary>부문 1위. 자격 선수가 없으면 null.</summary>
        public static TeamLeader Leader(TeamRoster roster, LeaderCategory category)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            PlayerData best;
            switch (category)
            {
                case LeaderCategory.BattingAverage:
                    best = QualifiedHitters(roster).OrderByDescending(p => p.BattingAverage).FirstOrDefault();
                    break;
                case LeaderCategory.HomeRuns:
                    best = QualifiedHitters(roster).OrderByDescending(p => p.HomeRuns).FirstOrDefault();
                    break;
                case LeaderCategory.Era:
                    best = roster.Starters.Where(p => p.Era > 0).OrderBy(p => p.Era).FirstOrDefault();
                    break;
                case LeaderCategory.Saves:
                    best = roster.Bullpen.Where(p => p.Saves > 0).OrderByDescending(p => p.Saves).FirstOrDefault();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
            return best == null ? null : new TeamLeader { Category = category, Player = best };
        }

        /// <summary>대시보드 부문 순서대로 1위. 자격 선수가 없는 부문은 빠진다.</summary>
        public static List<TeamLeader> Leaders(TeamRoster roster) =>
            DashboardCategories.Select(c => Leader(roster, c)).Where(l => l != null).ToList();

        public static string CategoryName(LeaderCategory category)
        {
            switch (category)
            {
                case LeaderCategory.BattingAverage: return "타율";
                case LeaderCategory.HomeRuns: return "홈런";
                case LeaderCategory.Era: return "평균자책";
                case LeaderCategory.Saves: return "세이브";
                default: throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
        }

        public static string ValueText(LeaderCategory category, PlayerData p)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (category)
            {
                case LeaderCategory.BattingAverage: return p.BattingAverage.ToString(p.BattingAverage < 1 ? ".000" : "0.000", inv);
                case LeaderCategory.HomeRuns: return p.HomeRuns + "개";
                case LeaderCategory.Era: return p.Era.ToString("0.00", inv);
                case LeaderCategory.Saves: return p.Saves + "개";
                default: throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
        }
    }
}
