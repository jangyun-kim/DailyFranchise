using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Data
{
    /// <summary>선수 성향 태그 (Phase 23). 한 선수는 태그를 하나만 갖는다.</summary>
    public enum ChemistryTag
    {
        None,
        /// <summary>주루: 도루 시도율 리그 상위 30% + 도루 성공률 65% 이상.</summary>
        Speed,
        /// <summary>수비: PA당 dWAR 리그 상위 30%.</summary>
        Glove,
        /// <summary>장타: ISO(SLG − AVG) 리그 상위 30%.</summary>
        Power,
        /// <summary>에이스 투수: FIP 리그 상위 20% (케미스트리 멤버가 아니라 거포 클린업 카운터).</summary>
        Ace,
    }

    /// <summary>라인업 조합으로 발동하는 팀 케미스트리.</summary>
    public enum ChemistryKind
    {
        /// <summary>발야구: Speed 3명 이상.</summary>
        SpeedBall,
        /// <summary>철벽 센터라인: C·2B·SS·CF 수비 위치의 Glove 3명 이상.</summary>
        CenterLineGlove,
        /// <summary>거포 클린업: 연속 타순의 Power 3명 이상.</summary>
        PowerCleanup,
    }

    /// <summary>
    /// 리그 백분위로 성향 태그를 붙인다 (DB를 읽은 뒤 한 번). 고정 기준(도루 20개 등)은 데이터 시즌·구단 규모에 따라
    /// 한 성향만 성립하게 만들므로, 리그 안 순위로 정한다.
    /// - 타자 (PA ≥ <see cref="MinPlateAppearances"/>): Speed/Glove/Power 중 자격이 있는 것들 가운데 순위 비율이 가장 높은 하나
    ///   (같으면 Speed → Glove → Power 순). 점수 0 이하는 자격 없음.
    /// - 투수 (이닝 ≥ <see cref="MinInnings"/>): FIP 상위 20%면 Ace.
    /// 순위 비율 = 등수(0부터, 동점은 같은 등수) / 대상 수. 상위 p% = 등수 &lt; ceil(대상 수 × p).
    /// </summary>
    public static class ChemistryTagger
    {
        public const int MinPlateAppearances = 100;
        public const double MinInnings = 30;
        public const double TopPercent = 0.30;
        public const double AcePercent = 0.20;
        public const double MinStealSuccess = 0.65;

        /// <summary>도루 시도율 = (SB + CS) / (단타 + 볼넷 + 사구). 1루 출루 기회당 시도.</summary>
        public static double SpeedScore(PlayerData p)
        {
            int singles = p.Hits - p.Doubles - p.Triples - p.HomeRuns;
            int chances = Math.Max(1, singles + p.Walks + p.HitByPitch);
            return (double)(p.StolenBases + p.CaughtStealing) / chances;
        }

        public static double StealSuccess(PlayerData p) =>
            p.StolenBases + p.CaughtStealing == 0 ? 0 : (double)p.StolenBases / (p.StolenBases + p.CaughtStealing);

        /// <summary>600타석 환산 dWAR.</summary>
        public static double GloveScore(PlayerData p) => p.PlateAppearances <= 0 ? 0 : p.DefensiveWar * 600 / p.PlateAppearances;

        /// <summary>ISO = SLG − AVG. 기록 장타율이 없으면 OPS − OBP.</summary>
        public static double PowerScore(PlayerData p)
        {
            double slg = p.RecordedSlugging > 0 ? p.RecordedSlugging : p.SluggingPercentage;
            return slg > 0 ? slg - p.BattingAverage : 0;
        }

        public static IReadOnlyDictionary<string, ChemistryTag> Tag(IEnumerable<PlayerData> league)
        {
            var players = (league ?? Enumerable.Empty<PlayerData>()).Where(p => p != null && !string.IsNullOrEmpty(p.Id)).ToList();
            var tags = new Dictionary<string, ChemistryTag>();

            var batters = players.Where(p => !p.IsPitcher && p.PlateAppearances >= MinPlateAppearances).ToList();
            var speed = Ranks(batters, SpeedScore, TopPercent, p => StealSuccess(p) >= MinStealSuccess);
            var glove = Ranks(batters, GloveScore, TopPercent);
            var power = Ranks(batters, PowerScore, TopPercent);
            foreach (var b in batters)
            {
                var best = ChemistryTag.None;
                double bestRank = double.MaxValue;
                foreach (var (tag, ranks) in new[] { (ChemistryTag.Speed, speed), (ChemistryTag.Glove, glove), (ChemistryTag.Power, power) })
                    if (ranks.TryGetValue(b.Id, out double r) && r < bestRank) (best, bestRank) = (tag, r);
                if (best != ChemistryTag.None) tags[b.Id] = best;
            }

            var pitchers = players.Where(p => p.IsPitcher && p.InningsPitched >= MinInnings && p.Fip > 0).ToList();
            foreach (var id in Ranks(pitchers, p => -p.Fip, AcePercent, allowNonPositive: true).Keys) tags[id] = ChemistryTag.Ace;
            return tags;
        }

        /// <summary>상위 <paramref name="top"/> 안에 든 선수 ID → 순위 비율. 동점은 같은 등수.</summary>
        static Dictionary<string, double> Ranks(List<PlayerData> pool, Func<PlayerData, double> score, double top,
            Func<PlayerData, bool> eligible = null, bool allowNonPositive = false)
        {
            var result = new Dictionary<string, double>();
            if (pool.Count == 0) return result;
            var sorted = pool.Select(p => (p, s: score(p))).Where(x => !double.IsNaN(x.s))
                .OrderByDescending(x => x.s).ThenBy(x => x.p.Id, StringComparer.Ordinal).ToList();
            int cutoff = (int)Math.Ceiling(pool.Count * top - 1e-9); // 부동소수 오차로 한 명 더 들어가지 않게
            for (int i = 0; i < sorted.Count; i++)
            {
                int rank = i > 0 && sorted[i].s == sorted[i - 1].s ? sorted.FindIndex(x => x.s == sorted[i].s) : i;
                if (rank >= cutoff) break;
                var (p, s) = sorted[i];
                if (!allowNonPositive && s <= 0) continue;
                if (eligible != null && !eligible(p)) continue;
                result[p.Id] = (double)rank / pool.Count;
            }
            return result;
        }
    }

    /// <summary>한 구단의 오늘 발동 케미스트리와 멤버.</summary>
    public sealed class ActiveChemistry
    {
        readonly Dictionary<ChemistryKind, HashSet<string>> _members;

        public ActiveChemistry(string team, IDictionary<ChemistryKind, IEnumerable<string>> members)
        {
            Team = team;
            _members = (members ?? new Dictionary<ChemistryKind, IEnumerable<string>>())
                .ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value));
        }

        public string Team { get; }

        /// <summary>발동한 케미스트리 (열거형 순서).</summary>
        public IReadOnlyList<ChemistryKind> Kinds => _members.Keys.OrderBy(k => k).ToList();

        public bool Has(ChemistryKind kind) => _members.ContainsKey(kind);

        /// <summary>멤버 선수 DB ID. 발동하지 않았으면 빈 목록.</summary>
        public IReadOnlyCollection<string> Members(ChemistryKind kind) =>
            _members.TryGetValue(kind, out var set) ? (IReadOnlyCollection<string>)set : Array.Empty<string>();

        public bool IsMember(ChemistryKind kind, string playerId) =>
            playerId != null && _members.TryGetValue(kind, out var set) && set.Contains(playerId);
    }

    /// <summary>
    /// 케미스트리 발동·효과 규칙 (Phase 23). 효과는 타석 안타(출루) 확률 H의 상대 보정이며 합계는 ±<see cref="Cap"/>로 자른다.
    /// - 발야구: Speed 멤버가 1·2루에 있으면 공격팀 타자 +4% (투수 견제 분산). 상대가 철벽 센터라인이면 절반.
    /// - 철벽 센터라인: 수비팀이면 상대 타자 −3%. 타자가 상대 거포 클린업 멤버면 절반 (홈런은 수비가 못 막는다).
    /// - 거포 클린업: 멤버가 주자 있을 때 타석이면 +4%. 상대 투수가 Ace면 절반.
    /// 한 선수는 태그가 하나라 한 라인업은 케미스트리 최대 3개(실제로는 9자리 제약으로 대개 2개)만 갖는다.
    /// </summary>
    public static class ChemistryRules
    {
        public const int MinMembers = 3;
        public const double SpeedBonus = 0.04;
        public const double GloveBonus = 0.03;
        public const double PowerBonus = 0.04;
        public const double CounterFactor = 0.5;
        public const double Cap = 0.06;

        static readonly HashSet<string> CenterLine = new HashSet<string> { "C", "2B", "SS", "CF" };

        public static bool IsCenterLine(string position) => position != null && CenterLine.Contains(position);

        /// <summary>선수단 선발 타순(Order 1~9·수비 위치)과 태그로 오늘 케미스트리를 정한다. 선수단이 없으면 null.</summary>
        public static ActiveChemistry Evaluate(TeamRoster roster, IReadOnlyDictionary<string, ChemistryTag> tags)
        {
            if (roster == null) return null;
            tags = tags ?? new Dictionary<string, ChemistryTag>();
            ChemistryTag TagOf(PlayerData p) => p?.Id != null && tags.TryGetValue(p.Id, out var t) ? t : ChemistryTag.None;

            var lineup = roster.Lineup.Where(p => p != null).OrderBy(p => p.Order).ToList();
            var members = new Dictionary<ChemistryKind, IEnumerable<string>>();

            var speed = lineup.Where(p => TagOf(p) == ChemistryTag.Speed).Select(p => p.Id).ToList();
            if (speed.Count >= MinMembers) members[ChemistryKind.SpeedBall] = speed;

            var glove = lineup.Where(p => TagOf(p) == ChemistryTag.Glove && IsCenterLine(p.Position)).Select(p => p.Id).ToList();
            if (glove.Count >= MinMembers) members[ChemistryKind.CenterLineGlove] = glove;

            var power = new List<string>();
            var run = new List<PlayerData>();
            foreach (var p in lineup.Append(null))
            {
                if (p != null && TagOf(p) == ChemistryTag.Power && (run.Count == 0 || p.Order == run[run.Count - 1].Order + 1))
                {
                    run.Add(p);
                    continue;
                }
                if (run.Count >= MinMembers) power.AddRange(run.Select(x => x.Id));
                run.Clear();
                if (p != null && TagOf(p) == ChemistryTag.Power) run.Add(p);
            }
            if (power.Count >= MinMembers) members[ChemistryKind.PowerCleanup] = power;

            return new ActiveChemistry(roster.Team, members);
        }

        public static string Label(ChemistryKind kind)
        {
            switch (kind)
            {
                case ChemistryKind.SpeedBall: return "발야구";
                case ChemistryKind.CenterLineGlove: return "철벽 센터라인";
                case ChemistryKind.PowerCleanup: return "거포 클린업";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }

    /// <summary>경기 중 구단별 케미스트리와 선수 태그를 알려준다.</summary>
    public interface IChemistrySource
    {
        /// <summary>그 구단의 오늘 케미스트리. 모르면 null.</summary>
        ActiveChemistry ActiveFor(string team);
        ChemistryTag TagOf(string playerId);
    }

    /// <summary>한 경기 두 구단의 케미스트리.</summary>
    public sealed class ChemistryBoard : IChemistrySource
    {
        readonly IReadOnlyDictionary<string, ChemistryTag> _tags;
        readonly Dictionary<string, ActiveChemistry> _teams = new Dictionary<string, ActiveChemistry>();

        public ChemistryBoard(IReadOnlyDictionary<string, ChemistryTag> tags, params TeamRoster[] rosters)
        {
            _tags = tags ?? new Dictionary<string, ChemistryTag>();
            foreach (var r in rosters ?? Array.Empty<TeamRoster>())
            {
                var active = ChemistryRules.Evaluate(r, _tags);
                if (active?.Team != null) _teams[active.Team] = active;
            }
        }

        public ActiveChemistry ActiveFor(string team) => team != null && _teams.TryGetValue(team, out var a) ? a : null;

        public ChemistryTag TagOf(string playerId) =>
            playerId != null && _tags.TryGetValue(playerId, out var t) ? t : ChemistryTag.None;
    }
}
