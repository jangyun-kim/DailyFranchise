using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Data
{
    /// <summary>
    /// 선수 컨디션 1~5단계 (Phase 22). 경기를 치를수록 떨어지고 쉬면 회복한다 (<see cref="ConditionRules"/>).
    /// 5 최상 · 4 좋음 · 3 보통 · 2 지침 · 1 위험. 기록이 없는 선수는 보통(3).
    /// </summary>
    public static class PlayerCondition
    {
        public const int Exhausted = 1;
        public const int Tired = 2;
        public const int Normal = 3;
        public const int Good = 4;
        public const int Excellent = 5;
        public const int Min = Exhausted;
        public const int Max = Excellent;
        public const int Default = Normal;

        public static int Clamp(int value) => Math.Max(Min, Math.Min(Max, value));

        public static string Label(int value)
        {
            switch (Clamp(value))
            {
                case Excellent: return "최상";
                case Good: return "좋음";
                case Normal: return "보통";
                case Tired: return "지침";
                default: return "위험";
            }
        }
    }

    /// <summary>선수 ID(선수 DB ID) → 컨디션. 모르는 선수는 <see cref="PlayerCondition.Default"/>.</summary>
    public interface IConditionSource
    {
        int ConditionOf(string playerId);
    }

    /// <summary>
    /// 한 경기의 출장 기록 (선수 DB ID 기준). 경기 화면이 만들어 경기 종료 처리(<see cref="ConditionRules"/>)에 넘긴다.
    /// </summary>
    public sealed class GameParticipation
    {
        readonly HashSet<string> _roster = new HashSet<string>();
        readonly Dictionary<string, string> _startingPitchers = new Dictionary<string, string>();
        readonly HashSet<string> _batters = new HashSet<string>();
        readonly Dictionary<string, int> _pitches = new Dictionary<string, int>();

        /// <summary>컨디션을 갱신할 선수 전원 (경기를 치른 두 구단의 선수단). 출장하지 않은 선수는 휴식으로 회복한다.</summary>
        public IReadOnlyCollection<string> Roster => _roster;
        /// <summary>타석에 1번 이상 선 선수.</summary>
        public IReadOnlyCollection<string> Batters => _batters;
        /// <summary>투수별 투구 수 (1구 이상 던진 투수만).</summary>
        public IReadOnlyDictionary<string, int> PitchesThrown => _pitches;

        public void AddRosterPlayer(string playerId)
        {
            if (!string.IsNullOrEmpty(playerId)) _roster.Add(playerId);
        }

        public void AddBatter(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            _roster.Add(playerId);
            _batters.Add(playerId);
        }

        public void AddPitches(string playerId, int pitches)
        {
            if (string.IsNullOrEmpty(playerId) || pitches <= 0) return;
            _roster.Add(playerId);
            _pitches[playerId] = (_pitches.TryGetValue(playerId, out int n) ? n : 0) + pitches;
        }

        /// <summary>구단 이름 → 이 경기 선발 투수 ID (로테이션 다음 순번 계산용).</summary>
        public IReadOnlyDictionary<string, string> StartingPitchers => _startingPitchers;

        public void SetStartingPitcher(string team, string playerId)
        {
            if (string.IsNullOrEmpty(team) || string.IsNullOrEmpty(playerId)) return;
            _startingPitchers[team] = playerId;
        }

        public bool Played(string playerId) => _batters.Contains(playerId) || _pitches.ContainsKey(playerId);
    }

    /// <summary>컨디션 변화 한 건 (로그·테스트용).</summary>
    public readonly struct ConditionChange
    {
        public ConditionChange(string playerId, int before, int after)
        {
            PlayerId = playerId;
            Before = before;
            After = after;
        }

        public string PlayerId { get; }
        public int Before { get; }
        public int After { get; }
        public int Delta => After - Before;
        public override string ToString() => $"{PlayerId} {Before}→{After}";
    }

    /// <summary>
    /// 경기 뒤 컨디션 변화 규칙 (Phase 22). 결과는 항상 1~5.
    /// - 투수: 30구 이상 −1, 60구 이상 −2 (확정). 1~29구는 변화 없음.
    /// - 야수: 타석에 섰으면 <see cref="BatterDeclineChance"/>(25%) 확률로 −1.
    /// - 출장하지 않은 선수(휴식): +1.
    /// 난수는 야수 하락 판정에만, 선수 ID 순서(ordinal)대로 하나씩 쓴다 → 같은 난수·같은 출장이면 같은 결과.
    /// 경기 결과 처리용 난수를 따로 받는다 (인게임 작전 난수·타 구단 경기 난수와 섞지 않는다).
    /// </summary>
    public static class ConditionRules
    {
        public const double BatterDeclineChance = 0.25;
        public const int PitcherOneStep = 30;
        public const int PitcherTwoSteps = 60;

        /// <summary>한 선수의 변화량. 야수가 출장했으면 난수를 하나 쓴다.</summary>
        public static int DeltaFor(string playerId, GameParticipation game, IRandomSource random)
        {
            if (game.PitchesThrown.TryGetValue(playerId, out int pitches))
                return pitches >= PitcherTwoSteps ? -2 : pitches >= PitcherOneStep ? -1 : 0;
            if (game.Batters.Contains(playerId))
                return random.NextDouble() < BatterDeclineChance ? -1 : 0;
            return +1;
        }

        /// <summary>
        /// 출장 기록으로 컨디션을 바꾼다. <paramref name="conditions"/>에 없는 선수는 보통(3)에서 시작한다.
        /// (선발 순번 기록은 호출하는 쪽이 <see cref="GameParticipation.StartingPitchers"/>로 따로 남긴다.)
        /// </summary>
        /// <returns>대상 선수 전원의 변화 (ID 순).</returns>
        public static List<ConditionChange> Apply(IDictionary<string, int> conditions, GameParticipation game, IRandomSource random)
        {
            if (conditions == null) throw new ArgumentNullException(nameof(conditions));
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (random == null) throw new ArgumentNullException(nameof(random));
            var changes = new List<ConditionChange>();
            foreach (var id in game.Roster.OrderBy(x => x, StringComparer.Ordinal))
            {
                int before = conditions.TryGetValue(id, out int c) ? PlayerCondition.Clamp(c) : PlayerCondition.Default;
                int after = PlayerCondition.Clamp(before + DeltaFor(id, game, random));
                conditions[id] = after;
                changes.Add(new ConditionChange(id, before, after));
            }
            return changes;
        }
    }

    /// <summary>
    /// 선발 로테이션 (Phase 22). 직전 선발 다음 순번(Order)부터 돌며 컨디션이 보통(3) 이상인 첫 선발이 오늘 선발이다.
    /// 모두 보통 미만이면 컨디션이 가장 좋은 선발 (같으면 다음 순번에 가까운 쪽). 직전 선발을 모르면 1번부터.
    /// 컨디션만으로 고르면 쉬어서 최상(5)에 닿은 선발끼리 순번이 빠른 쪽만 계속 나오므로, 순번은 직전 선발로 잇는다.
    /// 원본 선수단은 바꾸지 않고, 오늘 선발을 Order 1로 올린 사본을 돌려준다.
    /// </summary>
    public static class RotationPicker
    {
        /// <summary>선발 자격 컨디션 (이 값 미만이면 순번을 건너뛴다).</summary>
        public const int MinStartCondition = PlayerCondition.Normal;

        /// <summary>오늘 선발. 선발이 없으면 null.</summary>
        /// <param name="lastStarterId">직전 경기 선발. 모르거나 선수단에 없으면 1번부터.</param>
        public static PlayerData Pick(TeamRoster roster, IConditionSource conditions, string lastStarterId = null)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            var ordered = roster.Starters.OrderBy(p => p.Order).ToList();
            if (ordered.Count == 0) return null;
            int last = ordered.FindIndex(p => p.Id == lastStarterId);
            var cycle = Enumerable.Range(1, ordered.Count).Select(i => ordered[(last + i) % ordered.Count]).ToList();
            int Condition(PlayerData p) => conditions?.ConditionOf(p.Id) ?? PlayerCondition.Default;
            return cycle.FirstOrDefault(p => Condition(p) >= MinStartCondition)
                   ?? cycle.OrderByDescending(Condition).First(); // OrderBy는 안정 정렬 → 같으면 순번 순
        }

        /// <summary>오늘 선발을 Order 1로 둔 선수단 사본. 이미 1번이면 원본 그대로.</summary>
        public static TeamRoster Apply(TeamRoster roster, IConditionSource conditions, string lastStarterId = null)
        {
            if (roster == null) return null;
            var today = Pick(roster, conditions, lastStarterId);
            if (today == null || ReferenceEquals(today, roster.StartingPitcher)) return roster;
            var starters = new List<PlayerData> { today.CloneAs(RosterRole.Starter, 1, today.Position) };
            int order = 2;
            foreach (var p in roster.Starters.Where(p => !ReferenceEquals(p, today)))
                starters.Add(p.CloneAs(RosterRole.Starter, order++, p.Position));
            return new TeamRoster(roster.Team, roster.Lineup, roster.Bench, starters, roster.Bullpen);
        }
    }
}
