using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>
    /// 이적 한 건 (Phase 25 영입, Phase 26 방출). 시즌과 함께 저장되고, 선수단은 "선수 DB + 이적 기록"으로 다시 짠다.
    /// 받는 구단이 <see cref="LeagueRosterBook.FreeAgentTeam"/>이면 방출이다.
    /// Phase 35: 같은 구단 안의 1군 ↔ 퓨처스 맞바꾸기(<see cref="Move"/>)도 같은 목록에 차례대로 남긴다 — 트레이드·방출과 순서가 섞여도
    /// 다시 짤 때 그때와 같은 선수단이 나오도록.
    /// </summary>
    public sealed class SigningRecord
    {
        /// <param name="trade">1:1 트레이드의 한쪽 (Phase 34). 연달아 놓인 두 기록(서로 맞바꾼 구단)을 장부가 함께 적용한다.</param>
        public SigningRecord(string playerId, string fromTeam, string toTeam, long salary, bool trade = false)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("선수 ID가 비어 있다.", nameof(playerId));
            if (string.IsNullOrEmpty(fromTeam) || string.IsNullOrEmpty(toTeam)) throw new ArgumentException("구단이 비어 있다.");
            PlayerId = playerId;
            FromTeam = fromTeam;
            ToTeam = toTeam;
            Salary = Math.Max(0, salary);
            IsTrade = trade;
        }

        /// <summary>
        /// 1군 말소·퓨처스 등록 맞바꾸기 (Phase 35): <paramref name="sendDownId"/>(1군 벤치·불펜)가 퓨처스로, <paramref name="callUpId"/>(퓨처스)가
        /// 그 1군 자리로. 같은 구단 안이라 연봉 기록이 아니다 (<see cref="Salary"/> 0, 계약 연봉을 바꾸지 않는다).
        /// </summary>
        public static SigningRecord Move(string team, string sendDownId, string callUpId)
        {
            if (string.IsNullOrEmpty(callUpId)) throw new ArgumentException("올릴 선수 ID가 비어 있다.", nameof(callUpId));
            return new SigningRecord(sendDownId, team, team, 0) { CallUpId = callUpId };
        }

        /// <summary>
        /// 부상 (Phase 36): <paramref name="playerId"/>가 부상으로 1군에서 퓨처스로 내려간다 (이미 퓨처스면 자리는 그대로). 빈자리는 장부가
        /// 메운다 (<see cref="LeagueRosterBook"/>). 회복 기록(<see cref="Recovery"/>)이 나올 때까지 그 선수는 1군에 올라오지 않는다.
        /// 같은 구단 안이라 연봉 기록이 아니다.
        /// </summary>
        public static SigningRecord Injury(string team, string playerId) =>
            new SigningRecord(playerId, team, team, 0) { Mark = InjuryMark.Injured };

        /// <summary>부상 회복 (Phase 36): 퓨처스에 그대로 있고, 다시 1군 등록·빈자리 콜업 대상이 된다.</summary>
        public static SigningRecord Recovery(string team, string playerId) =>
            new SigningRecord(playerId, team, team, 0) { Mark = InjuryMark.Recovered };

        /// <summary>
        /// CPU 구단 스토브리그 AI의 영입·방출 (Phase 41, <see cref="CpuStoveLeague"/>). 장부는 CPU 영입 선수를 1군 정원이 차 있으면
        /// 퓨처스 끝에 넣는다 (사용자 영입은 정원 검사를 협상 단계에서 하므로 그대로 1군).
        /// </summary>
        public bool ByCpu { get; private set; }

        /// <summary>CPU 구단의 FA 영입 (자유계약 → 구단).</summary>
        public static SigningRecord CpuSigning(string playerId, string team, long salary) =>
            new SigningRecord(playerId, LeagueRosterBook.FreeAgentTeam, team, salary) { ByCpu = true };

        /// <summary>CPU 구단의 방출 (구단 → 자유계약).</summary>
        public static SigningRecord CpuRelease(string playerId, string team) =>
            new SigningRecord(playerId, team, LeagueRosterBook.FreeAgentTeam, 0) { ByCpu = true };

        /// <summary>저장 파일에서 되살린 기록에 CPU 표시를 붙인다 (이적 기록만).</summary>
        public SigningRecord AsCpu()
        {
            if (IsTransfer && !IsTrade) ByCpu = true;
            return this;
        }

        /// <summary>부상·회복 기록이면 그 종류 (Phase 36). 이적·맞바꾸기 기록이면 None.</summary>
        public InjuryMark Mark { get; private set; }

        /// <summary>부상·회복 기록인지 (Phase 36).</summary>
        public bool IsInjuryMark => Mark != InjuryMark.None;

        /// <summary>계약이 따라오는 이적 기록인지 (영입·방출·트레이드). 맞바꾸기·부상·회복 기록은 아니다.</summary>
        public bool IsTransfer => !IsRosterMove && !IsInjuryMark;

        /// <summary>1군 ↔ 퓨처스 맞바꾸기에서 1군으로 올라온 선수 (Phase 35). 이적 기록이면 null.</summary>
        public string CallUpId { get; private set; }

        /// <summary>1군 ↔ 퓨처스 맞바꾸기 기록인지 (Phase 35). <see cref="PlayerId"/>가 말소된 선수.</summary>
        public bool IsRosterMove => CallUpId != null;

        /// <summary>1:1 트레이드의 한쪽인지 (Phase 34).</summary>
        public bool IsTrade { get; }

        public string PlayerId { get; }
        public string FromTeam { get; }
        public string ToTeam { get; }
        /// <summary>계약 연봉 (영입 때 합의한 제시액). 페이롤에 이 값이 잡힌다. 방출 기록은 0.</summary>
        public long Salary { get; }

        public bool IsRelease => ToTeam == LeagueRosterBook.FreeAgentTeam;
    }

    /// <summary>부상 기록 종류 (Phase 36, <see cref="SigningRecord.Mark"/>).</summary>
    public enum InjuryMark
    {
        None = 0,
        Injured = 1,
        Recovered = 2,
    }

    /// <summary>1군 정원 (Phase 25). 선발 9 + 벤치 5 + 선발 로테이션 5 + 불펜 7 = 26.</summary>
    public static class RosterLimits
    {
        public const int MaxRoster = 26;
        public const int MaxBench = 5;
        public const int MaxBullpen = 7;
        /// <summary>경기를 치르려면 필요한 선발 타순.</summary>
        public const int LineupSize = LineupCard.BattingSlots;

        public static int CountOf(TeamRoster r) =>
            r == null ? 0 : r.Lineup.Count + r.Bench.Count + r.Starters.Count + r.Bullpen.Count;

        /// <summary>1군에 이 선수(투수면 불펜, 타자면 벤치)가 들어갈 빈자리가 있는지.</summary>
        public static bool HasFirstTeamRoom(TeamRoster r, bool pitcher) =>
            r != null && CountOf(r) < MaxRoster && (pitcher ? r.Bullpen.Count < MaxBullpen : r.Bench.Count < MaxBench);

        /// <summary>1군 정원·벤치·불펜 한도를 넘었는지.</summary>
        public static bool IsOverLimit(TeamRoster r) =>
            r != null && (CountOf(r) > MaxRoster || r.Bench.Count > MaxBench || r.Bullpen.Count > MaxBullpen);

        /// <summary>
        /// 경기를 치를 수 없는 선수단이면 이유 (선발 타순 9명이 아님 · 선발 투수 없음), 괜찮으면 null.
        /// 로비가 [다음 경기 시작]을 잠그고 경고한다 (Phase 26 — 방출 등으로 빈자리가 생겨도 경기가 깨지지 않게).
        /// </summary>
        public static string GameReadinessProblem(TeamRoster r)
        {
            if (r == null) return null;
            var orders = r.Lineup.Select(p => p.Order).ToList();
            if (r.Lineup.Count != LineupSize || orders.Distinct().Count() != LineupSize || orders.Any(o => o < 1 || o > LineupSize))
                return $"선발 타순이 {r.Lineup.Count}명입니다. {LineupSize}명을 채워야 경기를 시작할 수 있습니다.";
            if (r.Starters.Count == 0) return "선발 투수가 없습니다. 로테이션을 채워야 경기를 시작할 수 있습니다.";
            return null;
        }
    }

    /// <summary>
    /// 리그 선수단 장부: 선수 DB 선수단에 이적 기록을 차례로 적용한 결과. 기록이 없는 구단은 DB 선수단 그대로(같은 참조).
    /// 방출된 선수는 소속 없는 자유계약 명단(<see cref="FreeAgentTeam"/>)으로 가고 FA 시장에 다시 나온다.
    /// 옮긴 선수는 새 구단 소속 사본으로 타자는 벤치 끝, 투수(선발 포함)는 불펜 끝에 들어간다.
    /// 원 소속 구단의 빈자리:
    /// - 선발 타자 (Phase 30 "김헌곤 룰", <see cref="PositionMatcher"/>): 빠진 선수의 수비 위치에 맞는 선수가 그 타순·위치로 올라온다 —
    ///   벤치 같은 위치 → 퓨처스 같은 위치 → 벤치 같은 수비군 → 퓨처스 같은 수비군 (포수는 포수만). 구단에 맞는 선수가 없으면
    ///   그 위치의 리그 평균 "대체 선수"가 채운다 (포수 자리에 외야수를 세우지 않는다).
    /// - 벤치·불펜: 빼고 순번을 다시 매긴다.
    /// - 선발 투수: 빼고 순번을 다시 매긴다. 선발이 하나도 안 남으면 불펜 첫 투수(없으면 대체 투수)가 선발로 간다.
    /// 장부와 맞지 않는 기록(선수 없음·이미 다른 구단)은 건너뛴다 (DB가 바뀐 이전 저장 파일 대비).
    /// 드래프트 신인 (Phase 30, <see cref="SeasonState.Rookies"/>): 이적 기록보다 먼저 지명 구단의 퓨처스 끝에 들어간다.
    /// </summary>
    public sealed class LeagueRosterBook
    {
        public const string ReplacementPrefix = "REPL-";
        /// <summary>방출된 선수의 소속 (자유계약). 경기를 치르지 않는다.</summary>
        public const string FreeAgentTeam = "자유계약";

        readonly Dictionary<string, TeamRoster> _rosters;
        readonly HashSet<string> _injured;

        LeagueRosterBook(Dictionary<string, TeamRoster> rosters, IReadOnlyList<SigningRecord> applied, HashSet<string> injured)
        {
            _rosters = rosters;
            Applied = applied;
            _injured = injured;
        }

        /// <summary>
        /// 기록상 부상 중인 선수 (Phase 36 — 부상 기록 뒤 회복 기록이 아직 없는 선수). 퓨처스에 있고, 빈자리 콜업·맞바꾸기로 올라오지 않는다.
        /// 남은 경기 수는 시즌이 들고 있다 (<see cref="SeasonState.Injuries"/>).
        /// </summary>
        public IReadOnlyCollection<string> Injured => _injured;

        /// <summary>실제로 적용된 이적 기록 (건너뛴 것 제외).</summary>
        public IReadOnlyList<SigningRecord> Applied { get; }

        /// <summary>자유계약 명단을 포함한 모든 선수단.</summary>
        public IEnumerable<TeamRoster> Rosters => _rosters.Values;

        public TeamRoster Get(string team) => team != null && _rosters.TryGetValue(team, out var r) ? r : null;

        /// <summary>선수의 지금 소속과 데이터. 없으면 (null, null).</summary>
        public (TeamRoster roster, PlayerData player) Find(string playerId)
        {
            foreach (var r in _rosters.Values)
            {
                var p = AllOf(r).FirstOrDefault(x => x.Id == playerId);
                if (p != null) return (r, p);
            }
            return (null, null);
        }

        public static bool IsReplacement(PlayerData p) => p?.Id != null && p.Id.StartsWith(ReplacementPrefix, StringComparison.Ordinal);

        public static IEnumerable<PlayerData> AllOf(TeamRoster r) => r.Lineup.Concat(r.Bench).Concat(r.Starters).Concat(r.Bullpen);

        /// <param name="progression">
        /// 선수 변동치 (Phase 29, <see cref="SeasonState.Progression"/>). 은퇴 선수는 이적 기록 적용 뒤 빼고(빈자리는 이적과 같은 규칙,
        /// 퓨처스에서도 뺀다), 변동치가 있는 선수는 나이·폼을 덧씌운 사본으로 바꾼다. 변동치가 없는 구단은 그대로(같은 참조).
        /// </param>
        /// <param name="rookies">
        /// 드래프트 신인 (Phase 30, <see cref="SeasonState.Rookies"/>). 선수단이 있는 지명 구단의 퓨처스 끝에 넣는다
        /// (선수단이 없는 구단·이미 있는 ID는 건너뛴다). 신인이 없는 구단은 그대로(같은 참조).
        /// </param>
        public static LeagueRosterBook Build(MasterDatabase db, IEnumerable<SigningRecord> signings,
            IReadOnlyDictionary<string, PlayerProgression> progression = null, IEnumerable<PlayerData> rookies = null)
        {
            var rosters = new Dictionary<string, TeamRoster>();
            if (db != null)
                foreach (var team in db.Players.Select(p => p.Team).Distinct())
                {
                    var r = db.GetRoster(team);
                    if (r != null) rosters[team] = r;
                }
            if (rookies != null) AddRookies(rosters, rookies);
            rosters[FreeAgentTeam] = new TeamRoster(FreeAgentTeam, null, null, null, null);
            var applied = new List<SigningRecord>();
            var injured = new HashSet<string>();
            ApplySignings(rosters, signings, applied, injured);
            if (progression != null && progression.Count > 0) ApplyProgression(rosters, progression, injured);
            return new LeagueRosterBook(rosters, applied, injured);
        }

        /// <summary>
        /// 지금 장부에 이적 기록을 더 적용한 새 장부 (Phase 34 — 트레이드 사전 검사용 가정 계산). 이 장부는 바꾸지 않는다.
        /// 맞지 않는 기록은 <see cref="Build"/>처럼 건너뛰므로, 적용됐는지는 <see cref="Applied"/> 수로 본다.
        /// </summary>
        public LeagueRosterBook With(IEnumerable<SigningRecord> extra)
        {
            var rosters = new Dictionary<string, TeamRoster>(_rosters);
            var applied = Applied.ToList();
            var injured = new HashSet<string>(_injured);
            ApplySignings(rosters, extra, applied, injured);
            return new LeagueRosterBook(rosters, applied, injured);
        }

        /// <summary>
        /// 이적 기록을 차례로 적용한다. 1군뿐 아니라 퓨처스 선수도 옮길 수 있다 (Phase 34 트레이드).
        /// 트레이드 한 쌍(연달아 놓인, 서로 구단을 맞바꾼 두 기록)은 함께 적용하고, 한쪽이라도 맞지 않으면 둘 다 건너뛴다.
        /// 부상 기록(Phase 36)은 그 선수를 퓨처스로 내리고 <paramref name="injured"/>에 넣는다 (<see cref="InjureOut"/>), 회복 기록은 뺀다.
        /// 그 사이의 빈자리 콜업(김헌곤 룰 포함)·맞바꾸기는 그때 부상 중인 선수를 올리지 않는다 — 다시 짜도 그때와 같은 선수단.
        /// </summary>
        static void ApplySignings(Dictionary<string, TeamRoster> rosters, IEnumerable<SigningRecord> signings, List<SigningRecord> applied,
            HashSet<string> injured)
        {
            var list = (signings ?? Enumerable.Empty<SigningRecord>()).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s != null && s.IsInjuryMark)
                {
                    if (!rosters.TryGetValue(s.FromTeam, out var club)) continue;
                    var found = AllOf(club).Concat(club.Reserves).FirstOrDefault(p => p.Id == s.PlayerId);
                    if (found == null || IsReplacement(found)) continue;
                    if (s.Mark == InjuryMark.Injured)
                    {
                        injured.Add(s.PlayerId);
                        rosters[s.FromTeam] = InjureOut(club, found, injured);
                    }
                    else injured.Remove(s.PlayerId);
                    applied.Add(s);
                    continue;
                }
                if (s != null && s.IsRosterMove)
                {
                    if (rosters.TryGetValue(s.FromTeam, out var team) && TryMove(team, s.PlayerId, s.CallUpId, out var moved, injured))
                    {
                        rosters[s.FromTeam] = moved;
                        applied.Add(s);
                    }
                    continue;
                }
                if (s == null || s.FromTeam == s.ToTeam) continue;
                if (s.IsTrade && i + 1 < list.Count && IsTradePair(s, list[i + 1]))
                {
                    if (ApplyTrade(rosters, s, list[i + 1], injured))
                    {
                        applied.Add(s);
                        applied.Add(list[i + 1]);
                    }
                    i++;
                    continue;
                }
                if (!rosters.TryGetValue(s.FromTeam, out var from) || !rosters.TryGetValue(s.ToTeam, out var to)) continue;
                var player = Movable(from, s.PlayerId);
                if (player == null) continue;
                rosters[s.FromTeam] = Remove(from, player, injured);
                // CPU 영입만 정원이 차면 퓨처스로 (자유계약 명단으로 가는 방출은 늘 시장에 보이게 1군 쪽에 둔다)
                rosters[s.ToTeam] = Add(to, player, futuresWhenFull: s.ByCpu && s.ToTeam != FreeAgentTeam);
                applied.Add(s);
            }
        }

        /// <summary>
        /// 1군 ↔ 퓨처스 맞바꾸기 (Phase 35): 1군 타자(선발 타순·벤치)나 불펜 투수 <paramref name="downId"/>와 같은 종류(타자·투수)의
        /// 퓨처스 선수 <paramref name="upId"/>가 자리를 바꾼다. 올라온 선수는 내려간 선수의 그 자리·순번을, 내려간 선수는 올라온 선수의
        /// 퓨처스 순번을 잇는다 — 1군 정원·벤치·불펜 수가 그대로다. 선발 로테이션 투수·대체 선수·종류가 다르면 false (바꾸지 않는다).
        /// 사용자 라인업 카드는 따로 고친다 (<see cref="SeasonState.AddRosterMove"/>) — 카드의 선발 타자는 막는 쪽이 호출한 곳이다.
        /// </summary>
        /// <param name="injured">부상 중인 선수 (Phase 36). 올릴 선수가 여기 있으면 false.</param>
        public static bool TryMove(TeamRoster r, string downId, string upId, out TeamRoster moved, IReadOnlyCollection<string> injured = null)
        {
            moved = r;
            if (r == null || injured != null && injured.Contains(upId)) return false;
            var up = r.Reserves.FirstOrDefault(p => p.Id == upId);
            var down = r.Lineup.Concat(r.Bench).Concat(r.Bullpen).FirstOrDefault(p => p.Id == downId);
            if (up == null || down == null || IsReplacement(up) || IsReplacement(down) || up.IsPitcher != down.IsPitcher) return false;
            var raised = up.CloneAs(down.Role, down.Order, down.Role == RosterRole.Lineup ? down.Position : up.Position);
            raised.Team = r.Team;
            var lowered = down.CloneAs(down.IsPitcher ? RosterRole.Bullpen : RosterRole.Bench, up.Order, down.Position);
            lowered.Team = r.Team;
            List<PlayerData> Replace(IEnumerable<PlayerData> list, PlayerData gone, PlayerData with) =>
                list.Select(p => p.Id == gone.Id ? with : p).ToList();
            moved = new TeamRoster(r.Team, Replace(r.Lineup, down, raised), Replace(r.Bench, down, raised), r.Starters,
                Replace(r.Bullpen, down, raised), Replace(r.Reserves, up, lowered));
            return true;
        }

        static bool IsTradePair(SigningRecord a, SigningRecord b) =>
            b != null && b.IsTrade && a.FromTeam == b.ToTeam && a.ToTeam == b.FromTeam && a.PlayerId != b.PlayerId;

        /// <summary>옮길 수 있는 선수 (1군·퓨처스, 대체 선수 제외). 없으면 null.</summary>
        static PlayerData Movable(TeamRoster from, string playerId)
        {
            var p = AllOf(from).Concat(from.Reserves).FirstOrDefault(x => x.Id == playerId);
            return p == null || IsReplacement(p) ? null : p;
        }

        static bool ApplyTrade(Dictionary<string, TeamRoster> rosters, SigningRecord a, SigningRecord b, ISet<string> injured)
        {
            if (!rosters.TryGetValue(a.FromTeam, out var x) || !rosters.TryGetValue(b.FromTeam, out var y)) return false;
            var pa = Movable(x, a.PlayerId);
            var pb = Movable(y, b.PlayerId);
            if (pa == null || pb == null) return false;
            rosters[a.FromTeam] = Swap(x, pa, pb, injured);
            rosters[b.FromTeam] = Swap(y, pb, pa, injured);
            return true;
        }

        /// <summary>
        /// 트레이드 한쪽 (Phase 34): <paramref name="outgoing"/>을 내주고 <paramref name="incoming"/>을 받는다.
        /// - 내준 선수가 선발 타자: 빈 타순을 김헌곤 룰로 메우되 받은 선수를 맨 앞 후보로 본다 (같은 위치면 받은 선수가 바로 선발,
        ///   구단에 같은 위치가 없으면 같은 수비군인 받은 선수가 먼저).
        /// - 내준 선수가 선발 로테이션 투수이고 받은 선수가 투수: 받은 투수가 그 순번을 잇는다.
        /// - 그 밖에는 내준 뒤의 1군 빈자리(타자 벤치·투수 불펜), 없으면 퓨처스.
        /// </summary>
        static TeamRoster Swap(TeamRoster from, PlayerData outgoing, PlayerData incoming, ISet<string> injured)
        {
            var arriving = incoming.CloneAs(incoming.IsPitcher ? RosterRole.Bullpen : RosterRole.Bench, 0, incoming.Position);
            arriving.Team = from.Team;
            int rotation = from.Starters.ToList().FindIndex(p => p.Id == outgoing.Id);
            if (rotation >= 0 && incoming.IsPitcher)
            {
                var starters = from.Starters.ToList();
                starters[rotation] = arriving.CloneAs(RosterRole.Starter, outgoing.Order, outgoing.Position);
                return new TeamRoster(from.Team, from.Lineup, from.Bench, starters, from.Bullpen, from.Reserves);
            }
            var removed = Remove(from, outgoing, arriving, injured, out bool started);
            return started ? removed : Add(removed, arriving, futuresWhenFull: true);
        }

        static void AddRookies(Dictionary<string, TeamRoster> rosters, IEnumerable<PlayerData> rookies)
        {
            var known = new HashSet<string>(rosters.Values.SelectMany(r => AllOf(r).Concat(r.Reserves)).Select(p => p.Id));
            foreach (var group in rookies.Where(p => p?.Id != null && p.Team != null).GroupBy(p => p.Team))
            {
                if (!rosters.TryGetValue(group.Key, out var r)) continue;
                var fresh = group.Where(p => known.Add(p.Id)).ToList();
                if (fresh.Count > 0)
                    rosters[group.Key] = new TeamRoster(r.Team, r.Lineup, r.Bench, r.Starters, r.Bullpen, r.Reserves.Concat(fresh).ToList());
            }
        }

        /// <summary>은퇴 선수를 빼고, 변동치가 있는 선수에 나이·폼을 덧씌운다 (Phase 29).</summary>
        static void ApplyProgression(Dictionary<string, TeamRoster> rosters, IReadOnlyDictionary<string, PlayerProgression> progression,
            ISet<string> injured)
        {
            foreach (var team in rosters.Keys.ToList())
            {
                var r = rosters[team];
                // 퓨처스 은퇴자를 먼저 뺀다 — 1군 빈자리를 퓨처스에서 메울 때(Phase 30) 은퇴 선수가 올라오지 않게.
                if (r.Reserves.Any(p => progression.TryGetValue(p.Id, out var d) && d.Retired))
                    r = new TeamRoster(r.Team, r.Lineup, r.Bench, r.Starters, r.Bullpen,
                        r.Reserves.Where(p => !(progression.TryGetValue(p.Id, out var d) && d.Retired)).ToList());
                foreach (var gone in AllOf(r).Where(p => progression.TryGetValue(p.Id, out var d) && d.Retired).ToList())
                    r = Remove(r, AllOf(r).First(p => p.Id == gone.Id), injured);
                if (AllOf(r).Concat(r.Reserves).Any(p => progression.ContainsKey(p.Id)))
                    r = new TeamRoster(r.Team, Overlay(r.Lineup, progression), Overlay(r.Bench, progression),
                        Overlay(r.Starters, progression), Overlay(r.Bullpen, progression), Overlay(r.Reserves, progression));
                rosters[team] = r;
            }
        }

        static List<PlayerData> Overlay(IEnumerable<PlayerData> players, IReadOnlyDictionary<string, PlayerProgression> progression) =>
            players.Select(p => progression.TryGetValue(p.Id, out var d)
                ? p.WithProgression(p.Age > 0 ? p.Age + d.AgeDelta : 0, d.Form)
                : p).ToList();

        /// <summary>
        /// 새 구단 소속 사본을 타자는 벤치 끝, 투수는 불펜 끝에 넣는다. 영입·방출 기록은 정원을 보지 않는다 (정원은 협상 단계에서).
        /// <paramref name="futuresWhenFull"/>(Phase 34 트레이드)이면 1군 정원·벤치·불펜이 찼을 때 퓨처스 끝으로 간다.
        /// </summary>
        static TeamRoster Add(TeamRoster to, PlayerData player, bool futuresWhenFull = false)
        {
            if (futuresWhenFull && !RosterLimits.HasFirstTeamRoom(to, player.IsPitcher))
            {
                var reserve = player.CloneAs(player.IsPitcher ? RosterRole.Bullpen : RosterRole.Bench, to.Reserves.Count + 1, player.Position);
                reserve.Team = to.Team;
                return new TeamRoster(to.Team, to.Lineup, to.Bench, to.Starters, to.Bullpen, to.Reserves.Append(reserve).ToList());
            }
            var moved = player.CloneAs(player.IsPitcher ? RosterRole.Bullpen : RosterRole.Bench,
                (player.IsPitcher ? to.Bullpen.Count : to.Bench.Count) + 1, player.Position);
            moved.Team = to.Team;
            return player.IsPitcher
                ? new TeamRoster(to.Team, to.Lineup, to.Bench, to.Starters, to.Bullpen.Append(moved).ToList(), to.Reserves)
                : new TeamRoster(to.Team, to.Lineup, to.Bench.Append(moved).ToList(), to.Starters, to.Bullpen, to.Reserves);
        }

        static TeamRoster Remove(TeamRoster from, PlayerData player, ISet<string> injured) => Remove(from, player, null, injured, out _);

        /// <param name="preferred">빈 타순의 맨 앞 후보 (트레이드로 받은 선수, 아직 선수단 밖). 그 선수가 메웠으면 <paramref name="preferredStarted"/>.</param>
        /// <param name="injured">부상 중인 선수 (Phase 36) — 퓨처스에서 빈 타순을 메울 때 건너뛴다.</param>
        static TeamRoster Remove(TeamRoster from, PlayerData player, PlayerData preferred, ISet<string> injured, out bool preferredStarted)
        {
            preferredStarted = false;
            var lineup = from.Lineup.ToList();
            var bench = from.Bench.Where(p => p.Id != player.Id).ToList();
            var starters = from.Starters.Where(p => p.Id != player.Id).ToList();
            var bullpen = from.Bullpen.Where(p => p.Id != player.Id).ToList();
            var reserves = from.Reserves.Where(p => p.Id != player.Id).ToList();

            int hole = lineup.FindIndex(p => p.Id == player.Id);
            if (hole >= 0)
            {
                var gone = lineup[hole];
                var pick = PositionMatcher.Pick(gone.Position, preferred == null ? null : new[] { preferred }, bench, Healthy(reserves, injured));
                if (pick != null && pick == preferred) preferredStarted = true;
                else if (pick != null)
                {
                    bench.Remove(pick);
                    reserves.Remove(pick);
                }
                lineup[hole] = pick != null ? pick.CloneAs(RosterRole.Lineup, gone.Order, gone.Position) : ReplacementBatter(from.Team, gone);
            }
            if (starters.Count == 0 && from.Starters.Count > 0)
            {
                if (bullpen.Count > 0)
                {
                    starters.Add(bullpen[0].CloneAs(RosterRole.Starter, 1, bullpen[0].Position));
                    bullpen.RemoveAt(0);
                }
                else starters.Add(ReplacementPitcher(from.Team, player));
            }
            return new TeamRoster(from.Team, lineup, Renumber(bench, RosterRole.Bench), Renumber(starters, RosterRole.Starter),
                Renumber(bullpen, RosterRole.Bullpen), reserves.Count == from.Reserves.Count ? from.Reserves : reserves);
        }

        static IEnumerable<PlayerData> Healthy(IEnumerable<PlayerData> reserves, ICollection<string> injured) =>
            injured == null || injured.Count == 0 ? reserves : reserves.Where(p => !injured.Contains(p.Id));

        /// <summary>
        /// 부상 강등 (Phase 36): 1군 선수를 퓨처스 끝으로 내리고 빈자리를 메운다 (이미 퓨처스면 그대로). <paramref name="injured"/>(이 선수 포함)는 올리지 않는다.
        /// - 선발 타자: 김헌곤 룰 (<see cref="PositionMatcher"/>: 벤치 같은 위치 → 퓨처스 같은 위치 → 벤치 같은 수비군 → 퓨처스 같은 수비군).
        ///   벤치에서 올라왔으면 벤치 빈자리에 퓨처스 타자를 올린다. 맞는 선수가 없으면 대체 선수.
        /// - 벤치: 퓨처스 타자 (같은 수비 위치·수비군 먼저, 없으면 첫 타자)를 벤치 끝에 올린다.
        /// - 선발 로테이션: 퓨처스 첫 투수가 그 순번을 잇는다. 없으면 빼고 순번을 다시 매긴다 (선발이 없으면 불펜 첫 투수 → 대체 투수).
        /// - 불펜: 퓨처스 첫 투수를 불펜 끝에 올린다.
        /// 퓨처스에 올릴 선수가 없으면 그 자리는 빈 채로 둔다 (1군 정원이 하나 준다).
        /// </summary>
        static TeamRoster InjureOut(TeamRoster from, PlayerData player, ISet<string> injured)
        {
            if (from.Reserves.Any(p => p.Id == player.Id)) return from;
            var lineup = from.Lineup.ToList();
            var bench = from.Bench.ToList();
            var starters = from.Starters.ToList();
            var bullpen = from.Bullpen.ToList();
            var reserves = from.Reserves.ToList();
            PlayerData NextBatter(string position) =>
                PositionMatcher.Pick(position, Healthy(reserves, injured)) ?? Healthy(reserves, injured).FirstOrDefault(p => !p.IsPitcher);
            PlayerData NextPitcher() => Healthy(reserves, injured).FirstOrDefault(p => p.IsPitcher);

            int hole = lineup.FindIndex(p => p.Id == player.Id);
            int benchAt = bench.FindIndex(p => p.Id == player.Id);
            int rotation = starters.FindIndex(p => p.Id == player.Id);
            if (hole >= 0)
            {
                var pick = PositionMatcher.Pick(player.Position, bench, Healthy(reserves, injured));
                if (pick != null && bench.Remove(pick))
                {
                    var up = NextBatter(pick.Position);
                    if (up != null)
                    {
                        reserves.Remove(up);
                        bench.Add(up);
                    }
                }
                else if (pick != null) reserves.Remove(pick);
                lineup[hole] = pick != null ? pick.CloneAs(RosterRole.Lineup, player.Order, player.Position) : ReplacementBatter(from.Team, player);
            }
            else if (benchAt >= 0)
            {
                bench.RemoveAt(benchAt);
                var up = NextBatter(player.Position);
                if (up != null)
                {
                    reserves.Remove(up);
                    bench.Add(up);
                }
            }
            else if (rotation >= 0)
            {
                var up = NextPitcher();
                if (up != null)
                {
                    reserves.Remove(up);
                    starters[rotation] = up.CloneAs(RosterRole.Starter, player.Order, up.Position);
                }
                else
                {
                    starters.RemoveAt(rotation);
                    if (starters.Count == 0)
                    {
                        if (bullpen.Count > 0)
                        {
                            starters.Add(bullpen[0].CloneAs(RosterRole.Starter, 1, bullpen[0].Position));
                            bullpen.RemoveAt(0);
                        }
                        else starters.Add(ReplacementPitcher(from.Team, player));
                    }
                }
            }
            else
            {
                bullpen.RemoveAll(p => p.Id == player.Id);
                var up = NextPitcher();
                if (up != null)
                {
                    reserves.Remove(up);
                    bullpen.Add(up);
                }
            }
            var lowered = player.CloneAs(player.IsPitcher ? RosterRole.Bullpen : RosterRole.Bench,
                reserves.Count == 0 ? 1 : reserves.Max(p => p.Order) + 1, player.Position);
            lowered.Team = from.Team;
            reserves.Add(lowered);
            return new TeamRoster(from.Team, lineup, Renumber(bench, RosterRole.Bench), Renumber(starters, RosterRole.Starter),
                Renumber(bullpen, RosterRole.Bullpen), reserves);
        }

        static List<PlayerData> Renumber(List<PlayerData> players, RosterRole role) =>
            players.Select((p, i) => p.Order == i + 1 && p.Role == role ? p : p.CloneAs(role, i + 1, p.Position)).ToList();

        /// <summary>리그 평균 기록의 대체 타자 (빠진 선수의 타순·수비 위치 — 구단에 그 위치를 맡을 선수가 없을 때).</summary>
        static PlayerData ReplacementBatter(string team, PlayerData gone) => new PlayerData
        {
            Id = ReplacementPrefix + gone.Id, Name = "대체 선수", Team = team, Role = RosterRole.Lineup,
            Order = gone.Order, Position = gone.Position, Year = gone.Year,
            BattingAverage = MatchupCalculator.LeagueAverage, OnBasePercentage = MatchupCalculator.LeagueOnBase,
        };

        static PlayerData ReplacementPitcher(string team, PlayerData gone) => new PlayerData
        {
            Id = ReplacementPrefix + gone.Id, Name = "대체 투수", Team = team, Role = RosterRole.Starter,
            Order = 1, Position = "선발", Year = gone.Year,
            Era = MatchupCalculator.LeagueEra, OpponentAverage = MatchupCalculator.LeagueAverage,
        };
    }

    /// <summary>FA 시장의 선수 한 명.</summary>
    public sealed class FreeAgent
    {
        public FreeAgent(PlayerData player, string team, long salary, long ask)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Team = team;
            Salary = salary;
            Ask = ask;
        }

        /// <summary>지금 소속 구단 기준 데이터.</summary>
        public PlayerData Player { get; }
        /// <summary>지금 소속 구단 (방출된 선수는 <see cref="LeagueRosterBook.FreeAgentTeam"/>).</summary>
        public string Team { get; }
        /// <summary>WAR 기준 연봉 (<see cref="FrontOfficeTrust.SalaryOf"/>).</summary>
        public long Salary { get; }
        /// <summary>요구액 = 연봉 × (1 + 인기 보정).</summary>
        public long Ask { get; }
        public string Id => Player.Id;
    }

    /// <summary>영입을 막는 이유.</summary>
    public enum SigningBlock
    {
        None,
        /// <summary>자유계약 선수가 아님 (내 구단·타 구단 선수 — Phase 35부터 타 구단 선수는 트레이드로만).</summary>
        NotAvailable,
        RosterFull,
        BenchFull,
        BullpenFull,
        /// <summary>제시액이 0 이하.</summary>
        InvalidOffer,
        /// <summary>예산 &lt; 수수료.</summary>
        Budget,
        /// <summary>페이롤 + 제시액 &gt; 이번 시즌 샐러리캡.</summary>
        SalaryCap,
        /// <summary>[확정 영입]인데 TP &lt; 100.</summary>
        Trust,
        /// <summary>경기 중.</summary>
        InGame,
        /// <summary>[수뇌부 설득]인데 캡에 막히지 않았다 (설득이 필요 없다, Phase 42).</summary>
        CapRoomAvailable,
    }

    /// <summary>협상 버튼 한 번의 결과.</summary>
    public sealed class SigningOutcome
    {
        public SigningBlock Block;
        public NegotiationResult Negotiation;
        public bool Signed => Block == SigningBlock.None && Negotiation.Signed;
        /// <summary>계약 연봉 (성공했을 때만). 영입 때 내지 않고 경기마다 페이롤 유지비로 나간다.</summary>
        public long Salary;
        public FreeAgent Agent;
        /// <summary>[수뇌부 설득]으로 캡을 넘겨 영입했는지 (Phase 42).</summary>
        public bool Persuaded;
    }

    /// <summary>방출을 막는 이유 (Phase 26).</summary>
    public enum ReleaseBlock
    {
        None,
        /// <summary>내 구단 선수가 아니다.</summary>
        NotMine,
        /// <summary>선발 로테이션 투수 — 먼저 불펜으로 내려야 한다 (Phase 30부터 선발 타자는 방출할 수 있다).</summary>
        Starter,
        /// <summary>예산 &lt; 위약금.</summary>
        Budget,
        InGame,
        /// <summary>부상 중인 선수 (Phase 36).</summary>
        Injured,
    }

    /// <summary>방출 한 번의 결과.</summary>
    public sealed class ReleaseOutcome
    {
        public ReleaseBlock Block;
        /// <summary>낸 위약금 (방출했을 때만).</summary>
        public long Penalty;
        public PlayerData Player;
        public bool Released => Block == ReleaseBlock.None;
    }

    /// <summary>
    /// 스토브리그 FA 시장 규칙 (Phase 25, Phase 26 재정 현실화). 순수 C#.
    /// - 시장 (Phase 35): 리그 선수단 장부의 자유계약 명단만 (방출·은퇴 전 선수 — 타 구단 선수는 트레이드 탭), 대체 선수 제외, WAR 높은 순.
    /// - 요구액 = 연봉 × (1 + 인기 보정): 경기 전 팬 만족도 구간 Happy 0% · Normal 10% · Angry 20% (인기 없는 구단엔 더 비싸게).
    /// - 협상: 수수료(요구액 5%)는 시도마다 예산에서. 연봉(제시액)은 영입 때 내지 않고 페이롤에 잡혀 경기마다 ÷144씩 나간다.
    ///   성공률은 <see cref="FrontOfficeTrust.SuccessRate"/>.
    /// - [확정 영입]: TP 100을 쓰고 요구액 그대로 100% 영입.
    /// - 막는 조건: 정원(26·벤치 5·불펜 7), 예산 &lt; 수수료, 페이롤 + 제시액 &gt; 시즌 캡.
    /// - 페이롤: 영입 선수는 계약 연봉, 나머지는 WAR 연봉, 대체 선수는 최저 연봉.
    /// - 방출 (Phase 26): 위약금 = 연봉의 50% (예산에서 즉시), 선수는 자유계약으로 시장에 돌아간다. 선발 로테이션 투수는 막는다.
    ///   선발 타자 (Phase 30): 빈자리를 같은 수비 위치 선수로 메우게 된 뒤로(<see cref="PositionMatcher"/>) 방출할 수 있다.
    /// - 캡 기준 (Phase 26): 리그 개막 평균 페이롤 × 1.25 (<see cref="OpeningCap"/>).
    /// - 리그 뉴스 (Phase 28): 영입 [오피셜] · 협상 결렬 [속보] · 방출 [단독]을 시즌 뉴스에 발행한다.
    /// </summary>
    public static class StoveLeague
    {
        public const int NormalPremiumPercent = 10;
        public const int AngryPremiumPercent = 20;
        /// <summary>방출 위약금 = 연봉의 50%.</summary>
        public const int ReleasePenaltyPercent = 50;
        /// <summary>캡 기준 = 리그 개막 평균 페이롤 × 125%.</summary>
        public const int CapPercentOfAveragePayroll = 125;

        public static int PremiumPercent(FanMood mood) =>
            mood == FanMood.Happy ? 0 : mood == FanMood.Angry ? AngryPremiumPercent : NormalPremiumPercent;

        public static long AskOf(long salary, FanMood mood) =>
            (long)Math.Round(salary * (100 + PremiumPercent(mood)) / 100.0, MidpointRounding.AwayFromZero);

        /// <summary>요구액의 <paramref name="percent"/>% 제시액 (반올림). 100%면 요구액과 같다.</summary>
        public static long OfferAt(long ask, int percent) =>
            (long)Math.Round(ask * Math.Max(0, percent) / 100.0, MidpointRounding.AwayFromZero);

        /// <summary>
        /// 리그 개막 샐러리캡 기준 = 선수 DB 구단(선수단이 있는 구단) 평균 페이롤 × 1.25.
        /// 내 구단의 지금 페이롤에 비례시키면 영입할수록 캡도 커져 제약이 사라지므로, 개막 시점 리그 평균에 묶는다.
        /// DB가 없으면 <see cref="FrontOfficeTrust.DefaultSalaryCap"/>.
        /// </summary>
        public static long OpeningCap(MasterDatabase db)
        {
            var teams = db?.Players.Select(p => p.Team).Distinct().Select(db.GetRoster).Where(r => r != null).ToList();
            if (teams == null || teams.Count == 0) return FrontOfficeTrust.DefaultSalaryCap;
            decimal average = teams.Sum(r => (decimal)Payroll(r, null)) / teams.Count;
            return (long)(average * CapPercentOfAveragePayroll / 100m);
        }

        public static IReadOnlyList<FreeAgent> Market(LeagueRosterBook book, string myTeam, FanMood mood)
        {
            if (book == null) return Array.Empty<FreeAgent>();
            return book.Rosters.Where(r => r.Team == LeagueRosterBook.FreeAgentTeam && r.Team != myTeam)
                .SelectMany(r => LeagueRosterBook.AllOf(r).Select(p => (r.Team, p)))
                .Where(x => !LeagueRosterBook.IsReplacement(x.p))
                .Select(x =>
                {
                    long salary = FrontOfficeTrust.SalaryOf(x.p.War);
                    return new FreeAgent(x.p, x.Team, salary, AskOf(salary, mood));
                })
                .OrderByDescending(a => a.Player.War).ThenBy(a => a.Id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>선수 한 명의 지금 연봉: 이 구단으로 온 마지막 계약 연봉, 없으면 WAR 연봉 (대체 선수는 최저 연봉).</summary>
        public static long SalaryOf(PlayerData p, string team, IEnumerable<SigningRecord> signings)
        {
            long? contract = null;
            foreach (var s in signings ?? Enumerable.Empty<SigningRecord>())
                if (s.IsTransfer && s.PlayerId == p.Id && s.ToTeam == team) contract = s.Salary;
            return contract ?? (LeagueRosterBook.IsReplacement(p) ? FrontOfficeTrust.MinSalary : FrontOfficeTrust.SalaryOf(p.War));
        }

        /// <summary>선수단 연봉 합계 (1군 전원).</summary>
        public static long Payroll(TeamRoster roster, IEnumerable<SigningRecord> signings)
        {
            if (roster == null) return 0;
            var list = signings?.ToList();
            long sum = 0;
            foreach (var p in LeagueRosterBook.AllOf(roster)) sum += SalaryOf(p, roster.Team, list);
            return sum;
        }

        /// <summary>이번 시즌 캡 여유 = 시즌 캡 − 페이롤 (음수 가능).</summary>
        public static long CapRoom(SeasonState season, LeagueRosterBook book) =>
            season.SalaryCap - Payroll(book?.Get(season.MyTeam), season.Signings);

        /// <summary>정원 검사만 (선수 역할별).</summary>
        public static SigningBlock RosterBlock(TeamRoster mine, PlayerData player)
        {
            if (mine == null) return SigningBlock.NotAvailable;
            if (RosterLimits.CountOf(mine) >= RosterLimits.MaxRoster) return SigningBlock.RosterFull;
            if (player.IsPitcher) return mine.Bullpen.Count >= RosterLimits.MaxBullpen ? SigningBlock.BullpenFull : SigningBlock.None;
            return mine.Bench.Count >= RosterLimits.MaxBench ? SigningBlock.BenchFull : SigningBlock.None;
        }

        /// <summary>이 제시액으로 협상(또는 확정 영입)할 수 있는지. 막히면 첫 이유.</summary>
        /// <param name="bypassCap">[수뇌부 설득] (Phase 42): 이번 한 건만 샐러리캡 검사를 건너뛴다 (TP 10 필요).</param>
        public static SigningBlock Check(SeasonState season, LeagueRosterBook book, FreeAgent agent, long offer, bool ceiling = false,
            bool bypassCap = false)
        {
            if (season.InProgress != null) return SigningBlock.InGame;
            var (owner, _) = book.Find(agent.Id);
            if (owner == null || owner.Team != LeagueRosterBook.FreeAgentTeam) return SigningBlock.NotAvailable; // Phase 35: 자유계약만 (타 구단 선수는 트레이드로)
            var roster = RosterBlock(book.Get(season.MyTeam), agent.Player);
            if (roster != SigningBlock.None) return roster;
            if (ceiling && !FrontOfficeTrust.CanUseCeiling(season.TrustPoints)) return SigningBlock.Trust;
            if (bypassCap && !FrontOfficeTrust.CanPersuade(season.TrustPoints)) return SigningBlock.Trust;
            if (offer <= 0) return SigningBlock.InvalidOffer;
            if (FrontOfficeTrust.FeeOf(agent.Ask) > season.Budget) return SigningBlock.Budget;
            if (!bypassCap && offer > CapRoom(season, book)) return SigningBlock.SalaryCap;
            return SigningBlock.None;
        }

        /// <summary>
        /// [수뇌부 설득]을 쓸 수 있는지 (Phase 42): 요구액 그대로의 일반 영입이 샐러리캡에만 막혀야 한다 — 캡 여유가 있으면
        /// <see cref="SigningBlock.CapRoomAvailable"/>, 다른 이유로 막히면 그 이유, TP가 모자라면 <see cref="SigningBlock.Trust"/>.
        /// </summary>
        public static SigningBlock CheckPersuasion(SeasonState season, LeagueRosterBook book, FreeAgent agent)
        {
            var normal = Check(season, book, agent, agent.Ask);
            if (normal == SigningBlock.None) return SigningBlock.CapRoomAvailable;
            if (normal != SigningBlock.SalaryCap) return normal;
            return Check(season, book, agent, agent.Ask, bypassCap: true);
        }

        /// <summary>
        /// [수뇌부 설득 (TP 10 소모)] (Phase 42): 캡만 막을 때 이번 한 건만 캡을 넘겨 요구액 그대로 영입한다.
        /// 캡 기준·Flex·페이롤 계산은 바꾸지 않는다 (영입 기록의 연봉이 페이롤에 그대로 더해진다).
        /// </summary>
        public static SigningOutcome SignWithPersuasion(SeasonState season, LeagueRosterBook book, FreeAgent agent)
        {
            var outcome = new SigningOutcome { Agent = agent, Block = CheckPersuasion(season, book, agent) };
            if (outcome.Block != SigningBlock.None) return outcome;
            outcome.Negotiation = season.PersuadeFrontOffice(agent.Ask);
            if (!outcome.Negotiation.Signed) return outcome;
            outcome.Persuaded = true;
            season.AddSigning(new SigningRecord(agent.Id, agent.Team, season.MyTeam, agent.Ask), isBatter: !agent.Player.IsPitcher);
            outcome.Salary = agent.Ask;
            season.AddNews(NewsWriter.Persuaded(season.MyTeam, agent.Player.Name, agent.Ask, season.Year, season.NewsDate));
            return outcome;
        }

        /// <summary>
        /// 일반 협상. 막히면 아무것도 바꾸지 않는다. 시도하면 수수료를 내고(<see cref="SeasonState.Negotiate"/>),
        /// 성공하면 제시액을 계약 연봉으로 영입 기록을 남긴다 (선수단 장부·페이롤은 호출한 쪽이 다시 짠다).
        /// </summary>
        public static SigningOutcome Negotiate(SeasonState season, LeagueRosterBook book, FreeAgent agent, long offer, IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var outcome = new SigningOutcome { Agent = agent, Block = Check(season, book, agent, offer) };
            if (outcome.Block != SigningBlock.None) return outcome;
            outcome.Negotiation = season.Negotiate(offer, agent.Ask, random);
            if (outcome.Negotiation.Signed) Sign(season, agent, offer, outcome);
            else if (outcome.Negotiation.Attempted)
                season.AddNews(NewsWriter.NegotiationFailed(season.MyTeam, agent.Player.Name, season.Year, season.NewsDate));
            return outcome;
        }

        /// <summary>[확정 영입]: TP 100과 수수료를 쓰고 요구액 그대로 영입한다.</summary>
        public static SigningOutcome SignWithCeiling(SeasonState season, LeagueRosterBook book, FreeAgent agent)
        {
            var outcome = new SigningOutcome { Agent = agent, Block = Check(season, book, agent, agent.Ask, ceiling: true) };
            if (outcome.Block != SigningBlock.None) return outcome;
            outcome.Negotiation = season.SignWithCeiling(agent.Ask);
            if (outcome.Negotiation.Signed) Sign(season, agent, agent.Ask, outcome);
            return outcome;
        }

        static void Sign(SeasonState season, FreeAgent agent, long salary, SigningOutcome outcome)
        {
            season.AddSigning(new SigningRecord(agent.Id, agent.Team, season.MyTeam, salary), isBatter: !agent.Player.IsPitcher);
            outcome.Salary = salary;
            season.AddNews(NewsWriter.Signed(season.MyTeam, agent.Player.Name, salary, outcome.Negotiation.CeilingUsed, season.Year, season.NewsDate));
        }

        /// <summary>방출 위약금 = 지금 연봉의 50% (내림).</summary>
        public static long ReleasePenaltyOf(long salary) => Math.Max(0, salary) * ReleasePenaltyPercent / 100;

        /// <summary>내 구단 선수의 방출 위약금. 내 선수가 아니면 0.</summary>
        public static long ReleasePenalty(SeasonState season, LeagueRosterBook book, string playerId)
        {
            var mine = book?.Get(season.MyTeam);
            var p = mine == null ? null : LeagueRosterBook.AllOf(mine).FirstOrDefault(x => x.Id == playerId);
            return p == null ? 0 : ReleasePenaltyOf(SalaryOf(p, season.MyTeam, season.Signings));
        }

        /// <summary>방출할 수 있는지. 선발 로테이션이 아닌 1군 선수, 위약금을 낼 예산이 있어야 한다.</summary>
        public static ReleaseBlock CheckRelease(SeasonState season, LeagueRosterBook book, string playerId)
        {
            if (season.InProgress != null) return ReleaseBlock.InGame;
            var mine = book?.Get(season.MyTeam);
            if (mine == null || playerId == null) return ReleaseBlock.NotMine;
            if (season.IsInjured(playerId) && LeagueRosterBook.AllOf(mine).Concat(mine.Reserves).Any(p => p.Id == playerId)) return ReleaseBlock.Injured;
            if (mine.Starters.Any(p => p.Id == playerId)) return ReleaseBlock.Starter;
            if (LeagueRosterBook.IsReplacement(mine.Lineup.FirstOrDefault(p => p.Id == playerId))) return ReleaseBlock.NotMine;
            if (!mine.Lineup.Concat(mine.Bench).Concat(mine.Bullpen).Any(p => p.Id == playerId)) return ReleaseBlock.NotMine;
            if (ReleasePenalty(season, book, playerId) > season.Budget) return ReleaseBlock.Budget;
            return ReleaseBlock.None;
        }

        /// <summary>
        /// 방출. 막히면 아무것도 바꾸지 않는다. 위약금을 예산에서 내고 자유계약으로 보내는 이적 기록을 남긴다
        /// (사용자 라인업 카드 벤치에서도 뺀다 — <see cref="SeasonState.AddSigning"/>).
        /// </summary>
        public static ReleaseOutcome Release(SeasonState season, LeagueRosterBook book, string playerId)
        {
            var outcome = new ReleaseOutcome { Block = CheckRelease(season, book, playerId) };
            if (outcome.Block != ReleaseBlock.None) return outcome;
            var p = LeagueRosterBook.AllOf(book.Get(season.MyTeam)).First(x => x.Id == playerId);
            outcome.Player = p;
            outcome.Penalty = ReleasePenalty(season, book, playerId);
            season.ApplyFinance(new FinanceChange(-outcome.Penalty, 0));
            season.AddSigning(new SigningRecord(playerId, season.MyTeam, LeagueRosterBook.FreeAgentTeam, 0), isBatter: !p.IsPitcher);
            season.AddNews(NewsWriter.Released(season.MyTeam, p.Name, season.Year, season.NewsDate));
            return outcome;
        }

        public static string BlockText(SigningBlock block)
        {
            switch (block)
            {
                case SigningBlock.None: return "";
                case SigningBlock.NotAvailable: return "시장에 없는 선수입니다.";
                case SigningBlock.RosterFull: return $"1군 정원 {RosterLimits.MaxRoster}명이 찼습니다. 로비에서 선수를 방출하세요.";
                case SigningBlock.BenchFull: return $"벤치 정원 {RosterLimits.MaxBench}명이 찼습니다. 야수를 방출해야 영입할 수 있습니다.";
                case SigningBlock.BullpenFull: return $"불펜 정원 {RosterLimits.MaxBullpen}명이 찼습니다. 투수를 방출해야 영입할 수 있습니다.";
                case SigningBlock.InvalidOffer: return "제시액이 없습니다.";
                case SigningBlock.Budget: return "예산이 협상 수수료보다 적습니다.";
                case SigningBlock.SalaryCap: return "샐러리캡 여유가 제시액보다 적습니다.";
                case SigningBlock.Trust: return $"확정 영입은 TP {FrontOfficeTrust.Ceiling} 이상일 때만 쓸 수 있습니다.";
                case SigningBlock.InGame: return "경기 중에는 협상할 수 없습니다.";
                case SigningBlock.CapRoomAvailable: return "샐러리캡 여유가 있어 수뇌부 설득이 필요 없습니다.";
                default: throw new ArgumentOutOfRangeException(nameof(block), block, null);
            }
        }

        public static string ReleaseBlockText(ReleaseBlock block)
        {
            switch (block)
            {
                case ReleaseBlock.None: return "";
                case ReleaseBlock.NotMine: return "내 구단 선수가 아닙니다.";
                case ReleaseBlock.Starter: return "선발 로테이션 투수는 방출할 수 없습니다.";
                case ReleaseBlock.Budget: return "예산이 위약금보다 적어 방출할 수 없습니다.";
                case ReleaseBlock.InGame: return "경기 중에는 방출할 수 없습니다.";
                case ReleaseBlock.Injured: return "부상 중인 선수는 방출할 수 없습니다.";
                default: throw new ArgumentOutOfRangeException(nameof(block), block, null);
            }
        }

        /// <summary>시장 목록 핵심 기록: 타자 "wRC+ 140", 투수 "FIP 3.21" (없으면 "OPS .850" / "ERA 3.55").</summary>
        public static string KeyStat(PlayerData p)
        {
            var inv = CultureInfo.InvariantCulture;
            if (p.IsPitcher) return p.Fip > 0 ? "FIP " + p.Fip.ToString("0.00", inv) : "ERA " + p.Era.ToString("0.00", inv);
            if (p.WrcPlus > 0) return "wRC+ " + p.WrcPlus.ToString("0", inv);
            return "OPS " + p.Ops.ToString(".000", inv);
        }
    }
}
