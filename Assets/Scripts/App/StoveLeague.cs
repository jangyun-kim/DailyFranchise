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
    /// </summary>
    public sealed class SigningRecord
    {
        public SigningRecord(string playerId, string fromTeam, string toTeam, long salary)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("선수 ID가 비어 있다.", nameof(playerId));
            if (string.IsNullOrEmpty(fromTeam) || string.IsNullOrEmpty(toTeam)) throw new ArgumentException("구단이 비어 있다.");
            PlayerId = playerId;
            FromTeam = fromTeam;
            ToTeam = toTeam;
            Salary = Math.Max(0, salary);
        }

        public string PlayerId { get; }
        public string FromTeam { get; }
        public string ToTeam { get; }
        /// <summary>계약 연봉 (영입 때 합의한 제시액). 페이롤에 이 값이 잡힌다. 방출 기록은 0.</summary>
        public long Salary { get; }

        public bool IsRelease => ToTeam == LeagueRosterBook.FreeAgentTeam;
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

        LeagueRosterBook(Dictionary<string, TeamRoster> rosters, IReadOnlyList<SigningRecord> applied)
        {
            _rosters = rosters;
            Applied = applied;
        }

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
            var book = new LeagueRosterBook(rosters, new List<SigningRecord>());
            var applied = new List<SigningRecord>();
            foreach (var s in signings ?? Enumerable.Empty<SigningRecord>())
            {
                if (s == null || s.FromTeam == s.ToTeam) continue;
                var from = book.Get(s.FromTeam);
                var to = book.Get(s.ToTeam);
                var player = from == null ? null : AllOf(from).FirstOrDefault(p => p.Id == s.PlayerId);
                if (to == null || player == null || IsReplacement(player)) continue;
                rosters[s.FromTeam] = Remove(from, player);
                rosters[s.ToTeam] = Add(to, player);
                applied.Add(s);
            }
            if (progression != null && progression.Count > 0) ApplyProgression(rosters, progression);
            return new LeagueRosterBook(rosters, applied);
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
        static void ApplyProgression(Dictionary<string, TeamRoster> rosters, IReadOnlyDictionary<string, PlayerProgression> progression)
        {
            foreach (var team in rosters.Keys.ToList())
            {
                var r = rosters[team];
                // 퓨처스 은퇴자를 먼저 뺀다 — 1군 빈자리를 퓨처스에서 메울 때(Phase 30) 은퇴 선수가 올라오지 않게.
                if (r.Reserves.Any(p => progression.TryGetValue(p.Id, out var d) && d.Retired))
                    r = new TeamRoster(r.Team, r.Lineup, r.Bench, r.Starters, r.Bullpen,
                        r.Reserves.Where(p => !(progression.TryGetValue(p.Id, out var d) && d.Retired)).ToList());
                foreach (var gone in AllOf(r).Where(p => progression.TryGetValue(p.Id, out var d) && d.Retired).ToList())
                    r = Remove(r, AllOf(r).First(p => p.Id == gone.Id));
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

        /// <summary>새 구단 소속 사본을 타자는 벤치 끝, 투수는 불펜 끝에 넣는다.</summary>
        static TeamRoster Add(TeamRoster to, PlayerData player)
        {
            var moved = player.CloneAs(player.IsPitcher ? RosterRole.Bullpen : RosterRole.Bench,
                (player.IsPitcher ? to.Bullpen.Count : to.Bench.Count) + 1, player.Position);
            moved.Team = to.Team;
            return player.IsPitcher
                ? new TeamRoster(to.Team, to.Lineup, to.Bench, to.Starters, to.Bullpen.Append(moved).ToList(), to.Reserves)
                : new TeamRoster(to.Team, to.Lineup, to.Bench.Append(moved).ToList(), to.Starters, to.Bullpen, to.Reserves);
        }

        static TeamRoster Remove(TeamRoster from, PlayerData player)
        {
            var lineup = from.Lineup.ToList();
            var bench = from.Bench.Where(p => p.Id != player.Id).ToList();
            var starters = from.Starters.Where(p => p.Id != player.Id).ToList();
            var bullpen = from.Bullpen.Where(p => p.Id != player.Id).ToList();
            var reserves = from.Reserves.ToList();

            int hole = lineup.FindIndex(p => p.Id == player.Id);
            if (hole >= 0)
            {
                var gone = lineup[hole];
                var pick = PositionMatcher.Pick(gone.Position, bench, reserves);
                if (pick != null)
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
        /// <summary>이미 내 구단 선수 (또는 시장에 없는 선수).</summary>
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
    /// - 시장: 리그 선수단 장부에서 내 구단이 아닌 선수 전원 (자유계약 포함, 대체 선수 제외), WAR 높은 순.
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
            return book.Rosters.Where(r => r.Team != myTeam)
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
                if (s.PlayerId == p.Id && s.ToTeam == team) contract = s.Salary;
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
        public static SigningBlock Check(SeasonState season, LeagueRosterBook book, FreeAgent agent, long offer, bool ceiling = false)
        {
            if (season.InProgress != null) return SigningBlock.InGame;
            var (owner, _) = book.Find(agent.Id);
            if (owner == null || owner.Team == season.MyTeam) return SigningBlock.NotAvailable;
            var roster = RosterBlock(book.Get(season.MyTeam), agent.Player);
            if (roster != SigningBlock.None) return roster;
            if (ceiling && !FrontOfficeTrust.CanUseCeiling(season.TrustPoints)) return SigningBlock.Trust;
            if (offer <= 0) return SigningBlock.InvalidOffer;
            if (FrontOfficeTrust.FeeOf(agent.Ask) > season.Budget) return SigningBlock.Budget;
            if (offer > CapRoom(season, book)) return SigningBlock.SalaryCap;
            return SigningBlock.None;
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
