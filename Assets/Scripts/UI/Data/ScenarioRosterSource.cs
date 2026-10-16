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
    /// 선수단이 없거나 그 자리에 선수가 없으면 이름을 지어내지 않고 "LG 4번 타자"처럼 구단 약칭 + 자리로 보인다
    /// (Phase 26.5 — 실제 데이터가 없는 구단에 가상 선수가 그려지지 않게). 확률 계산은 리그 평균 선수로 한다.
    /// 장면의 오늘 기록·투구 수·체력은 그대로 쓴다.
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
            if (data != null && !_scene.HasPlayer(playerId))
            {
                // 장면에 없는 자리(벤치 4번째·불펜 4번째부터): 쪽은 접두사로, 오늘은 대기 상태로 둔다.
                bool mine = playerId.StartsWith("home_", StringComparison.Ordinal);
                scene = new PlayerInfo
                {
                    Id = playerId, Side = mine ? MyTeam : MyTeam == TeamSide.Home ? TeamSide.Away : TeamSide.Home,
                    Position = data.Position, TodayLine = data.IsPitcher ? "불펜 대기" : null, Fitness = 100,
                };
            }
            var info = data == null ? Placeholder(scene, playerId) : new PlayerInfo
            {
                Id = scene.Id,
                Name = data.Name,
                Side = scene.Side,
                Position = data.Position,
                Handedness = HandednessText(data),
                SeasonLine = SeasonLineText(data),
                Profile = ProfileText(data),
                KeyStats = KeyStatsOf(data),
                RecordLine = data.IsPitcher ? RecordLineText(data) : null,
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

        /// <summary>
        /// 벤치. 그 쪽 선수단이 있으면 선수단 벤치 전원을 자리 ph1, ph2, pr1, pr2 … 순으로 (Phase 26.5 — 장면 자리 수(3)에
        /// 묶이지 않게: 1군 벤치 5명·FA 영입 선수까지 경기에서 쓸 수 있다). 없으면 장면 벤치 그대로.
        /// </summary>
        public IReadOnlyList<RosterEntry> GetBench(TeamSide side)
        {
            var scene = _scene.GetBench(side);
            var roster = RosterOn(side);
            if (roster == null || scene.Count == 0)
                return scene.Select(e =>
                {
                    var data = Resolve(e.PlayerId);
                    return new RosterEntry
                    {
                        Order = e.Order, Role = data?.Position ?? e.Role, PlayerId = e.PlayerId,
                        Note = data == null ? e.Note : BenchNote(data),
                    };
                }).ToList();
            string prefix = PrefixOf(side);
            return roster.Bench.Select((p, i) => new RosterEntry
            {
                Role = p.Position, Note = BenchNote(p),
                PlayerId = prefix + (i < PinchHitterSlots ? $"ph{i + 1}" : $"pr{i + 1 - PinchHitterSlots}"),
            }).ToList();
        }

        /// <summary>
        /// 불펜. 그 쪽 선수단이 있으면 선수단 불펜 전원을 자리 rp1, rp2 … 순으로 (장면에 있는 자리는 장면의 준비 상태 메모를,
        /// 그 뒤 자리는 "대기"). 없으면 장면 불펜 그대로.
        /// </summary>
        public IReadOnlyList<RosterEntry> GetBullpen(TeamSide side)
        {
            var scene = _scene.GetBullpen(side);
            var roster = RosterOn(side);
            if (roster == null || scene.Count == 0)
                return scene.Select(e => new RosterEntry
                {
                    Order = e.Order, Role = Resolve(e.PlayerId)?.Position ?? e.Role, PlayerId = e.PlayerId, Note = e.Note,
                }).ToList();
            string prefix = PrefixOf(side);
            return roster.Bullpen.Select((p, i) => new RosterEntry
            {
                Role = p.Position, PlayerId = $"{prefix}rp{i + 1}", Note = i < scene.Count ? scene[i].Note : ExtraBullpenNote,
            }).ToList();
        }

        /// <summary>장면에 없는 불펜 자리(4번째부터)의 메모·오늘 기록.</summary>
        public const string ExtraBullpenNote = "대기";

        /// <summary>화면 쪽의 선수단 (내 구단 쪽이면 내 선수단).</summary>
        TeamRoster RosterOn(TeamSide side) => side == MyTeam ? _mine : _opponent;

        /// <summary>장면 자리 ID 접두사: 내 구단 home_, 상대 away_ (장면이 뒤집혀도 같다).</summary>
        string PrefixOf(TeamSide side) => side == MyTeam ? "home_" : "away_";

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

        /// <summary>"No.24 · 29세". 0은 기록 없음으로 보고 뺀다. 둘 다 없으면 null.</summary>
        public static string ProfileText(PlayerData p)
        {
            var parts = new List<string>();
            if (p.Number > 0) parts.Add($"No.{p.Number}");
            if (p.Age > 0) parts.Add($"{p.Age}세");
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        /// <summary>
        /// 카드 핵심 지표. 타자: wRC+(정수)·WAR(소수 둘째), 투수: WHIP·FIP(소수 둘째).
        /// CSV 폴백 값 0은 기록 없음으로 보고 "—" (칩 개수는 항상 같아 카드 높이가 흔들리지 않는다).
        /// </summary>
        public static IReadOnlyList<KeyStat> KeyStatsOf(PlayerData p)
        {
            var inv = CultureInfo.InvariantCulture;
            return p.IsPitcher
                ? new[] { new KeyStat("WHIP", Fixed(p.Whip)), new KeyStat("FIP", Fixed(p.Fip)) }
                : new[] { new KeyStat("wRC+", p.WrcPlus == 0 ? "—" : Math.Round(p.WrcPlus).ToString("0", inv)), new KeyStat("WAR", Fixed(p.War)) };

            string Fixed(double v) => v == 0 ? "—" : v.ToString("0.00", inv);
        }

        /// <summary>투수 승·패·세이브·홀드 전체: "9승 7패 · 0세이브 · 0홀드".</summary>
        public static string RecordLineText(PlayerData p) =>
            $"{p.Wins}승 {p.Losses}패 · {p.Saves}세이브 · {p.Holds}홀드";

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

        /// <summary>데이터가 없는 자리: 구단 약칭 + 자리 이름, 리그 평균 안내. 장면 자리 ID가 아니면 장면 정보 그대로.</summary>
        PlayerInfo Placeholder(PlayerInfo scene, string playerId)
        {
            string label = SlotLabel(playerId);
            if (label == null) return scene;
            bool mine = playerId.StartsWith("home_", StringComparison.Ordinal);
            var team = _scene.GetTeam(mine ? MyTeam : MyTeam == TeamSide.Home ? TeamSide.Away : TeamSide.Home);
            return new PlayerInfo
            {
                Id = scene.Id,
                Name = $"{team.ShortName} {label}",
                Side = scene.Side,
                Position = scene.Position,
                Handedness = "—",
                SeasonLine = NoDataLine,
                TodayLine = scene.TodayLine,
                TodayAtBats = scene.TodayAtBats,
                TodayHits = scene.TodayHits,
                TodayExtra = scene.TodayExtra,
                PitchesBeforeScene = scene.PitchesBeforeScene,
                Fitness = scene.Fitness,
            };
        }

        public const string NoDataLine = "선수 데이터 없음 · 리그 평균 능력치";

        /// <summary>장면 자리 ID → "4번 타자" / "선발 투수" / "불펜 2" / "대타 1" / "대주자 1". 자리 ID가 아니면 null.</summary>
        public static string SlotLabel(string playerId)
        {
            if (playerId == null) return null;
            string slot = playerId.StartsWith("home_", StringComparison.Ordinal) ? playerId.Substring(5)
                : playerId.StartsWith("away_", StringComparison.Ordinal) ? playerId.Substring(5) : null;
            if (slot == null) return null;
            if (slot == "sp") return "선발 투수";
            if (TryIndex(slot, "b", out int n)) return $"{n}번 타자";
            if (TryIndex(slot, "ph", out n)) return $"대타 {n}";
            if (TryIndex(slot, "pr", out n)) return $"대주자 {n}";
            if (TryIndex(slot, "rp", out n)) return $"불펜 {n}";
            return null;
        }

        static bool TryIndex(string slot, string prefix, out int n)
        {
            n = 0;
            return slot.StartsWith(prefix, StringComparison.Ordinal)
                   && int.TryParse(slot.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out n);
        }
    }
}
