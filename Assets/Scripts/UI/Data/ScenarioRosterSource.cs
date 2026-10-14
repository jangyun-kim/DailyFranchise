using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.UI.Data
{
    /// <summary>
    /// 장면 더미 데이터(<see cref="DummyScenarioData"/>) 위에 외부 선수 데이터(<see cref="PlayerData"/>)의 이름표를 씌운다.
    /// 장면 투구 이벤트는 고정 선수 ID(home_b3, away_sp …)를 쓰므로, ID를 선수단 자리로 바꿔 그 자리의 선수로 표시한다.
    /// - 선수 데이터에서: 이름, 수비 위치·보직, 투타, 시즌 기록 문구, 벤치 메모
    /// - 장면 데이터 그대로: 소속 쪽(원정/홈), 오늘 기록·투구 수·체력, 스코어보드 기초값, 대진 팀명
    /// 선수단이 없거나 그 자리에 선수가 없으면 더미 정보를 그대로 쓴다.
    ///
    /// 자리 규칙 (home_ = 내 구단, away_ = 상대. 장면이 뒤집혀도 같다):
    ///   bN → 타순 N, phN → 벤치 순서 N(대타), prN → 벤치 순서 2+N(대주자), rpN → 불펜 순서 N, sp → 선발 순서 1
    /// </summary>
    public sealed class ScenarioRosterSource : IRosterSource, IScoreboardSource, IPlayerLookup
    {
        /// <summary>장면 벤치의 대타 자리 수. 대주자는 그 뒤 순서를 쓴다.</summary>
        const int PinchHitterSlots = 2;

        readonly DummyScenarioData _scene;
        readonly TeamRoster _mine;
        readonly TeamRoster _opponent;
        readonly Dictionary<string, PlayerInfo> _cache = new Dictionary<string, PlayerInfo>();

        /// <param name="scene">장면 기록·대진을 가진 더미 데이터 (대진의 선수단을 함께 쓴다).</param>
        /// <param name="sourceLabel">화면 하단 출처 표기 (예: "SampleRoster.csv").</param>
        public ScenarioRosterSource(DummyScenarioData scene, string sourceLabel)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            var m = scene.Matchup;
            _mine = m.RosterOf(m.MySide);
            _opponent = m.RosterOf(m.MySide == TeamSide.Home ? TeamSide.Away : TeamSide.Home);
            SourceLabel = sourceLabel;
        }

        public string SourceLabel { get; }
        public TeamSide MyTeam => _scene.MyTeam;
        public string FeaturedPlayerId => _scene.FeaturedPlayerId;
        public bool Mirrored => _scene.Mirrored;
        public TeamInfo GetTeam(TeamSide side) => _scene.GetTeam(side);
        public TeamBoxSeed GetBoxSeed(TeamSide side) => _scene.GetBoxSeed(side);

        /// <summary>장면 선수 ID에 대응하는 외부 선수. 자리가 비었거나 선수단이 없으면 null.</summary>
        public PlayerData Resolve(string playerId)
        {
            if (playerId == null) return null;
            TeamRoster roster;
            string slot;
            if (playerId.StartsWith("home_", StringComparison.Ordinal)) (roster, slot) = (_mine, playerId.Substring(5));
            else if (playerId.StartsWith("away_", StringComparison.Ordinal)) (roster, slot) = (_opponent, playerId.Substring(5));
            else return null;
            if (roster == null) return null;

            if (slot == "sp") return At(roster.Starters, 1);
            if (TryIndex(slot, "b", out int n)) return roster.AtOrder(n);
            if (TryIndex(slot, "ph", out n)) return At(roster.Bench, n);
            if (TryIndex(slot, "pr", out n)) return At(roster.Bench, PinchHitterSlots + n);
            if (TryIndex(slot, "rp", out n)) return At(roster.Bullpen, n);
            return null;
        }

        /// <summary>확률 계산기용: 장면 선수 ID의 외부 선수 데이터 (= <see cref="Resolve"/>).</summary>
        public PlayerData FindPlayer(string playerId) => Resolve(playerId);

        public PlayerInfo GetPlayer(string playerId)
        {
            var key = playerId ?? "";
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var scene = _scene.GetPlayer(playerId);
            var data = Resolve(playerId);
            var info = data == null ? scene : new PlayerInfo
            {
                Id = scene.Id,
                Name = data.Name,
                Side = scene.Side,
                Position = data.Position,
                Handedness = HandednessText(data),
                SeasonLine = SeasonLineText(data),
                TodayLine = scene.TodayLine,
                TodayAtBats = scene.TodayAtBats,
                TodayHits = scene.TodayHits,
                TodayExtra = scene.TodayExtra,
                PitchesBeforeScene = scene.PitchesBeforeScene,
                Fitness = scene.Fitness,
            };
            _cache[key] = info;
            return info;
        }

        public IReadOnlyList<RosterEntry> GetLineup(TeamSide side) =>
            _scene.GetLineup(side).Select(e => new RosterEntry
            {
                Order = e.Order, Role = GetPlayer(e.PlayerId).Position, PlayerId = e.PlayerId, Note = e.Note,
            }).ToList();

        public IReadOnlyList<RosterEntry> GetBench(TeamSide side) =>
            _scene.GetBench(side).Select(e =>
            {
                var data = Resolve(e.PlayerId);
                return new RosterEntry
                {
                    Order = e.Order, Role = data?.Position ?? e.Role, PlayerId = e.PlayerId,
                    Note = data == null ? e.Note : BenchNote(data),
                };
            }).ToList();

        /// <summary>불펜 칸의 메모는 장면의 체력·준비 상태라 그대로 둔다.</summary>
        public IReadOnlyList<RosterEntry> GetBullpen(TeamSide side) =>
            _scene.GetBullpen(side).Select(e => new RosterEntry
            {
                Order = e.Order, Role = Resolve(e.PlayerId)?.Position ?? e.Role, PlayerId = e.PlayerId, Note = e.Note,
            }).ToList();

        /// <summary>투수는 "우투", 타자는 "우투좌타" 꼴.</summary>
        public static string HandednessText(PlayerData p) =>
            p.IsPitcher ? $"{HandText(p.Throws)}투" : $"{HandText(p.Throws)}투{HandText(p.Bats)}타";

        /// <summary>
        /// 시즌 기록 한 줄. 타자: "타율 .318 · 19홈런 · OPS .889" (20도루 이상이면 도루 추가).
        /// 투수: 선발은 승패 "시즌 9승 7패 · 평균자책 3.55", 구원은 세이브·홀드 중 많은 쪽 (둘 다 없으면 승패).
        /// </summary>
        public static string SeasonLineText(PlayerData p)
        {
            var inv = CultureInfo.InvariantCulture;
            if (p.IsPitcher)
            {
                bool reliever = p.Role != RosterRole.Starter;
                string record = reliever && p.Saves > 0 && p.Saves >= p.Holds ? $"시즌 {p.Saves}세이브"
                    : reliever && p.Holds > 0 ? $"시즌 {p.Holds}홀드"
                    : $"시즌 {p.Wins}승 {p.Losses}패";
                return $"{record} · 평균자책 {p.Era.ToString("0.00", inv)}";
            }

            var parts = new List<string> { $"타율 {Rate(p.BattingAverage)}" };
            if (p.HomeRuns > 0) parts.Add($"{p.HomeRuns}홈런");
            if (p.Ops > 0) parts.Add($"OPS {Rate(p.Ops)}");
            if (p.StolenBases >= 20) parts.Add($"{p.StolenBases}도루");
            return string.Join(" · ", parts);

            string Rate(double v) => v.ToString(v < 1 ? ".000" : "0.000", inv);
        }

        /// <summary>
        /// 벤치 메모: 대타는 "좌/장타", 그 외는 메모 그대로. 메모가 없으면(예: 라인업에서 빠진 선발 타자) "좌타"처럼 타석만.
        /// </summary>
        static string BenchNote(PlayerData p)
        {
            if (p.Memo == null) return $"{HandText(p.Bats)}타";
            return p.Position == "대타" ? $"{HandText(p.Bats)}/{p.Memo}" : p.Memo;
        }

        static string HandText(Hand h) => h == Hand.Left ? "좌" : h == Hand.Switch ? "양" : "우";

        static PlayerData At(IReadOnlyList<PlayerData> list, int order) => list.FirstOrDefault(p => p.Order == order);

        static bool TryIndex(string slot, string prefix, out int n)
        {
            n = 0;
            return slot.StartsWith(prefix, StringComparison.Ordinal)
                   && int.TryParse(slot.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out n);
        }
    }
}
