using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>FA 영입 한 건 (Phase 25). 시즌과 함께 저장되고, 선수단은 "선수 DB + 영입 기록"으로 다시 짠다.</summary>
    public sealed class SigningRecord
    {
        public SigningRecord(string playerId, string fromTeam, string toTeam, int salary)
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
        /// <summary>계약 연봉 (영입 때 예산에서 낸 금액). 페이롤(샐러리캡)에 이 값이 잡힌다.</summary>
        public int Salary { get; }
    }

    /// <summary>1군 정원 (Phase 25). 선발 9 + 벤치 5 + 선발 로테이션 5 + 불펜 7 = 26.</summary>
    public static class RosterLimits
    {
        public const int MaxRoster = 26;
        public const int MaxBench = 5;
        public const int MaxBullpen = 7;

        public static int CountOf(TeamRoster r) =>
            r == null ? 0 : r.Lineup.Count + r.Bench.Count + r.Starters.Count + r.Bullpen.Count;
    }

    /// <summary>
    /// 리그 선수단 장부: 선수 DB 선수단에 영입 기록을 차례로 적용한 결과. 기록이 없는 구단은 DB 선수단 그대로(같은 참조).
    /// 영입된 선수는 새 구단 소속 사본으로 타자는 벤치 끝, 투수(선발 포함)는 불펜 끝에 들어간다.
    /// 원 소속 구단의 빈자리:
    /// - 선발 타자: 벤치 첫 타자가 그 타순·수비 위치로 올라온다. 벤치가 없으면 리그 평균 "대체 선수"가 채운다.
    /// - 벤치·불펜: 빼고 순번을 다시 매긴다.
    /// - 선발 투수: 빼고 순번을 다시 매긴다. 선발이 하나도 안 남으면 불펜 첫 투수(없으면 대체 투수)가 선발로 간다.
    /// 장부와 맞지 않는 기록(선수 없음·이미 다른 구단)은 건너뛴다 (DB가 바뀐 이전 저장 파일 대비).
    /// </summary>
    public sealed class LeagueRosterBook
    {
        public const string ReplacementPrefix = "REPL-";

        readonly Dictionary<string, TeamRoster> _rosters;

        LeagueRosterBook(Dictionary<string, TeamRoster> rosters, IReadOnlyList<SigningRecord> applied)
        {
            _rosters = rosters;
            Applied = applied;
        }

        /// <summary>실제로 적용된 영입 기록 (건너뛴 것 제외).</summary>
        public IReadOnlyList<SigningRecord> Applied { get; }

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

        public static LeagueRosterBook Build(MasterDatabase db, IEnumerable<SigningRecord> signings)
        {
            var rosters = new Dictionary<string, TeamRoster>();
            if (db != null)
                foreach (var team in db.Players.Select(p => p.Team).Distinct())
                {
                    var r = db.GetRoster(team);
                    if (r != null) rosters[team] = r;
                }
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
            return new LeagueRosterBook(rosters, applied);
        }

        /// <summary>새 구단 소속 사본을 타자는 벤치 끝, 투수는 불펜 끝에 넣는다.</summary>
        static TeamRoster Add(TeamRoster to, PlayerData player)
        {
            var moved = player.CloneAs(player.IsPitcher ? RosterRole.Bullpen : RosterRole.Bench,
                (player.IsPitcher ? to.Bullpen.Count : to.Bench.Count) + 1, player.Position);
            moved.Team = to.Team;
            return player.IsPitcher
                ? new TeamRoster(to.Team, to.Lineup, to.Bench, to.Starters, to.Bullpen.Append(moved).ToList())
                : new TeamRoster(to.Team, to.Lineup, to.Bench.Append(moved).ToList(), to.Starters, to.Bullpen);
        }

        static TeamRoster Remove(TeamRoster from, PlayerData player)
        {
            var lineup = from.Lineup.ToList();
            var bench = from.Bench.Where(p => p.Id != player.Id).ToList();
            var starters = from.Starters.Where(p => p.Id != player.Id).ToList();
            var bullpen = from.Bullpen.Where(p => p.Id != player.Id).ToList();

            int hole = lineup.FindIndex(p => p.Id == player.Id);
            if (hole >= 0)
            {
                var gone = lineup[hole];
                PlayerData up;
                if (bench.Count > 0)
                {
                    up = bench[0].CloneAs(RosterRole.Lineup, gone.Order, gone.Position);
                    bench.RemoveAt(0);
                }
                else up = ReplacementBatter(from.Team, gone);
                lineup[hole] = up;
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
                Renumber(bullpen, RosterRole.Bullpen));
        }

        static List<PlayerData> Renumber(List<PlayerData> players, RosterRole role) =>
            players.Select((p, i) => p.Order == i + 1 && p.Role == role ? p : p.CloneAs(role, i + 1, p.Position)).ToList();

        /// <summary>리그 평균 기록의 대체 타자 (빠진 선수의 타순·수비 위치).</summary>
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
        public FreeAgent(PlayerData player, string team, int salary, int ask)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Team = team;
            Salary = salary;
            Ask = ask;
        }

        /// <summary>지금 소속 구단 기준 데이터.</summary>
        public PlayerData Player { get; }
        /// <summary>지금 소속 구단.</summary>
        public string Team { get; }
        /// <summary>WAR 기준 연봉 (<see cref="FrontOfficeTrust.SalaryOf"/>).</summary>
        public int Salary { get; }
        /// <summary>요구액 = 연봉 × (1 + 인기 보정).</summary>
        public int Ask { get; }
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
        /// <summary>예산 &lt; 제시액 + 수수료.</summary>
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
        /// <summary>계약금 (성공했을 때만, 수수료 별도).</summary>
        public int Paid;
        public FreeAgent Agent;
    }

    /// <summary>
    /// 스토브리그 FA 시장 규칙 (Phase 25). 순수 C#.
    /// - 시장: 리그 선수단 장부에서 내 구단이 아닌 선수 전원 (대체 선수 제외), WAR 높은 순.
    /// - 요구액 = 연봉 × (1 + 인기 보정): 경기 전 팬 만족도 구간 Happy 0% · Normal 10% · Angry 20% (인기 없는 구단엔 더 비싸게).
    /// - 협상: 수수료(요구액 5%)는 시도마다, 계약금(제시액)은 성공했을 때만 예산에서 나간다. 성공률은 <see cref="FrontOfficeTrust.SuccessRate"/>.
    /// - [확정 영입]: TP 100을 쓰고 요구액 그대로 100% 영입.
    /// - 막는 조건: 정원(26·벤치 5·불펜 7), 예산 &lt; 제시액 + 수수료, 페이롤 + 제시액 &gt; 시즌 캡.
    /// - 페이롤: 영입 선수는 계약 연봉, 나머지는 WAR 연봉, 대체 선수는 최저 연봉.
    /// </summary>
    public static class StoveLeague
    {
        public const int NormalPremiumPercent = 10;
        public const int AngryPremiumPercent = 20;

        public static int PremiumPercent(FanMood mood) =>
            mood == FanMood.Happy ? 0 : mood == FanMood.Angry ? AngryPremiumPercent : NormalPremiumPercent;

        public static int AskOf(int salary, FanMood mood) =>
            (int)Math.Round(salary * (100 + PremiumPercent(mood)) / 100.0, MidpointRounding.AwayFromZero);

        /// <summary>요구액의 <paramref name="percent"/>% 제시액 (반올림). 100%면 요구액과 같다.</summary>
        public static int OfferAt(int ask, int percent) =>
            (int)Math.Round(ask * Math.Max(0, percent) / 100.0, MidpointRounding.AwayFromZero);

        public static IReadOnlyList<FreeAgent> Market(LeagueRosterBook book, string myTeam, FanMood mood)
        {
            if (book == null) return Array.Empty<FreeAgent>();
            return book.Rosters.Where(r => r.Team != myTeam)
                .SelectMany(r => LeagueRosterBook.AllOf(r).Select(p => (r.Team, p)))
                .Where(x => !LeagueRosterBook.IsReplacement(x.p))
                .Select(x =>
                {
                    int salary = FrontOfficeTrust.SalaryOf(x.p.War);
                    return new FreeAgent(x.p, x.Team, salary, AskOf(salary, mood));
                })
                .OrderByDescending(a => a.Player.War).ThenBy(a => a.Id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>선수단 연봉 합계. 영입 선수는 마지막 계약 연봉.</summary>
        public static long Payroll(TeamRoster roster, IEnumerable<SigningRecord> signings)
        {
            if (roster == null) return 0;
            var contracts = new Dictionary<string, int>();
            foreach (var s in signings ?? Enumerable.Empty<SigningRecord>())
                if (s.ToTeam == roster.Team) contracts[s.PlayerId] = s.Salary;
            long sum = 0;
            foreach (var p in LeagueRosterBook.AllOf(roster))
                sum += contracts.TryGetValue(p.Id, out int c) ? c
                    : LeagueRosterBook.IsReplacement(p) ? FrontOfficeTrust.MinSalary
                    : FrontOfficeTrust.SalaryOf(p.War);
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
        public static SigningBlock Check(SeasonState season, LeagueRosterBook book, FreeAgent agent, int offer, bool ceiling = false)
        {
            if (season.InProgress != null) return SigningBlock.InGame;
            var (owner, _) = book.Find(agent.Id);
            if (owner == null || owner.Team == season.MyTeam) return SigningBlock.NotAvailable;
            var roster = RosterBlock(book.Get(season.MyTeam), agent.Player);
            if (roster != SigningBlock.None) return roster;
            if (ceiling && !FrontOfficeTrust.CanUseCeiling(season.TrustPoints)) return SigningBlock.Trust;
            if (offer <= 0) return SigningBlock.InvalidOffer;
            if ((long)offer + FrontOfficeTrust.FeeOf(agent.Ask) > season.Budget) return SigningBlock.Budget;
            if (offer > CapRoom(season, book)) return SigningBlock.SalaryCap;
            return SigningBlock.None;
        }

        /// <summary>
        /// 일반 협상. 막히면 아무것도 바꾸지 않는다. 시도하면 수수료를 내고(<see cref="SeasonState.Negotiate"/>),
        /// 성공하면 제시액을 계약금으로 내고 영입 기록을 남긴다 (선수단 장부는 호출한 쪽이 다시 짠다).
        /// </summary>
        public static SigningOutcome Negotiate(SeasonState season, LeagueRosterBook book, FreeAgent agent, int offer, IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var outcome = new SigningOutcome { Agent = agent, Block = Check(season, book, agent, offer) };
            if (outcome.Block != SigningBlock.None) return outcome;
            outcome.Negotiation = season.Negotiate(offer, agent.Ask, random);
            if (outcome.Negotiation.Signed) Sign(season, agent, offer, outcome);
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

        static void Sign(SeasonState season, FreeAgent agent, int salary, SigningOutcome outcome)
        {
            season.ApplyFinance(new FinanceChange(-salary, 0));
            season.AddSigning(new SigningRecord(agent.Id, agent.Team, season.MyTeam, salary), isBatter: !agent.Player.IsPitcher);
            outcome.Paid = salary;
        }

        public static string BlockText(SigningBlock block)
        {
            switch (block)
            {
                case SigningBlock.None: return "";
                case SigningBlock.NotAvailable: return "시장에 없는 선수입니다.";
                case SigningBlock.RosterFull: return $"1군 정원 {RosterLimits.MaxRoster}명이 찼습니다. (방출 기능 준비 중)";
                case SigningBlock.BenchFull: return $"벤치 정원 {RosterLimits.MaxBench}명이 찼습니다. 야수는 더 영입할 수 없습니다.";
                case SigningBlock.BullpenFull: return $"불펜 정원 {RosterLimits.MaxBullpen}명이 찼습니다. 투수는 더 영입할 수 없습니다.";
                case SigningBlock.InvalidOffer: return "제시액이 없습니다.";
                case SigningBlock.Budget: return "예산이 제시액 + 수수료보다 적습니다.";
                case SigningBlock.SalaryCap: return "샐러리캡 여유가 제시액보다 적습니다.";
                case SigningBlock.Trust: return $"확정 영입은 TP {FrontOfficeTrust.Ceiling} 이상일 때만 쓸 수 있습니다.";
                case SigningBlock.InGame: return "경기 중에는 협상할 수 없습니다.";
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
