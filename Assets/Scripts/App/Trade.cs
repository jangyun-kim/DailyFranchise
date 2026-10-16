using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>
    /// CPU 프런트의 트레이드 가치 평가 (Phase 34). 순수 C#.
    /// - 생산력 = WAR + 폼 × <see cref="WarPerForm"/> (폼 +1%p ≈ WAR +0.1 — 폼은 안타 확률 상대 보정이라 WAR와 단위를 맞춘다).
    /// - 기본 가치 = 생산력 × 적정 연봉 ÷ 실제 연봉. 적정 연봉은 생산력으로 매긴 연봉(<see cref="FrontOfficeTrust.SalaryOf"/>).
    ///   WAR 연봉을 받는 선수는 가치 = 생산력이고, 계약 연봉이 실력보다 비싸면 깎이고 싸면 오른다.
    ///   ("WAR ÷ 연봉" 그대로면 최저 연봉 벤치 선수가 연봉 높은 주전보다 비싸져, 비율에 생산력을 곱한다.) 생산력이 0 이하면 0.
    /// - 나이 가중치: 24세 이하 × 1.5 (잠재력), 32세 이상 × 0.8. 나이를 모르면(0) 1.
    /// - 1:1 수락: 내 선수 가치 ≥ 상대 선수 가치 × 1.05 (CPU 프리미엄 5%).
    /// </summary>
    public static class TradeEvaluator
    {
        public const int YoungMaxAge = 24;
        public const double YoungMultiplier = 1.5;
        public const int VeteranMinAge = 32;
        public const double VeteranMultiplier = 0.8;
        /// <summary>폼 1.0(=100%)당 WAR. 폼 +5%면 WAR +0.5.</summary>
        public const double WarPerForm = 10;
        /// <summary>CPU가 요구하는 가치 배수 (5% 프리미엄).</summary>
        public const double CpuPremium = 1.05;

        const double Epsilon = 1e-9;

        public static double Production(PlayerData p)
        {
            if (p == null) return 0;
            double war = double.IsNaN(p.War) ? 0 : p.War;
            return war + p.Form * WarPerForm;
        }

        /// <summary>생산력 × 적정 연봉 ÷ 실제 연봉 (생산력 0 이하 또는 연봉 0 이하면 0).</summary>
        public static double BaseValue(PlayerData p, long salary)
        {
            double production = Production(p);
            if (production <= 0 || salary <= 0) return 0;
            return production * FrontOfficeTrust.SalaryOf(production) / salary;
        }

        public static double AgeMultiplier(int age) =>
            age > 0 && age <= YoungMaxAge ? YoungMultiplier : age >= VeteranMinAge ? VeteranMultiplier : 1.0;

        public static double Value(PlayerData p, long salary) => BaseValue(p, salary) * AgeMultiplier(p?.Age ?? 0);

        /// <summary>상대 선수를 받으려면 내 선수에게 필요한 가치.</summary>
        public static double RequiredValue(double theirValue) => theirValue * CpuPremium;

        /// <summary>CPU가 수락하는지: 내 가치 ≥ 상대 가치 × 1.05.</summary>
        public static bool Accepts(double myValue, double theirValue) => myValue + Epsilon >= RequiredValue(theirValue);

        public static string ValueText(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>트레이드를 막는 이유 (Phase 34).</summary>
    public enum TradeBlock
    {
        None,
        InGame,
        /// <summary>보낼 선수가 내 구단 1군·퓨처스 선수가 아니다 (대체 선수 포함).</summary>
        NotMine,
        /// <summary>받을 선수가 타 구단(내 구단·자유계약 제외) 선수가 아니다.</summary>
        NotAvailable,
        /// <summary>교환 뒤 어느 구단이 1군 정원(26)·벤치(5)·불펜(7)을 넘는다.</summary>
        RosterFull,
        /// <summary>교환 뒤 페이롤이 시즌 캡을 넘는다 (페이롤이 줄어드는 교환은 막지 않는다).</summary>
        SalaryCap,
    }

    /// <summary>받은 선수가 들어갈 자리.</summary>
    public enum TradeArrival
    {
        /// <summary>내준 선발 타자 자리에 바로 선발 (같은 수비 위치·수비군).</summary>
        Lineup,
        /// <summary>내준 로테이션 투수 순번을 잇는다.</summary>
        Rotation,
        Bench,
        Bullpen,
        /// <summary>1군 빈자리가 없어 퓨처스로 (페이롤에 잡히지 않는다).</summary>
        Futures,
    }

    /// <summary>타 구단 명단의 선수 한 명 (트레이드 대상).</summary>
    public sealed class TradeTarget
    {
        public TradeTarget(PlayerData player, string team, bool futures, long salary, double value)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Team = team;
            Futures = futures;
            Salary = salary;
            Value = value;
        }

        public PlayerData Player { get; }
        public string Team { get; }
        /// <summary>퓨처스(2군) 선수인지.</summary>
        public bool Futures { get; }
        /// <summary>지금 연봉 (<see cref="StoveLeague.SalaryOf"/>).</summary>
        public long Salary { get; }
        /// <summary>트레이드 가치 (<see cref="TradeEvaluator.Value"/>).</summary>
        public double Value { get; }
        public string Id => Player.Id;
    }

    /// <summary>1:1 트레이드 한 건의 사전 평가 (아무것도 바꾸지 않는다).</summary>
    public sealed class TradeQuote
    {
        public TradeBlock Block;
        /// <summary>내가 보낼 선수 (지금 데이터). 못 찾으면 null.</summary>
        public PlayerData Mine;
        /// <summary>받을 선수 (지금 데이터). 못 찾으면 null.</summary>
        public PlayerData Theirs;
        /// <summary>상대 구단.</summary>
        public string Partner;
        public long MySalary;
        public long TheirSalary;
        public double MyValue;
        public double TheirValue;
        /// <summary>CPU가 요구하는 가치 = 상대 가치 × 1.05.</summary>
        public double RequiredValue;
        /// <summary>막히지 않았다면 CPU가 수락할지.</summary>
        public bool WouldAccept;
        public long PayrollBefore;
        public long PayrollAfter;
        public TradeArrival Arrival;
        /// <summary>교환 뒤 장부 (막히지 않았을 때만).</summary>
        public LeagueRosterBook After;

        public long PayrollDelta => PayrollAfter - PayrollBefore;
    }

    /// <summary>[트레이드 제안] 한 번의 결과.</summary>
    public sealed class TradeOutcome
    {
        public TradeQuote Quote;
        public bool Accepted;
        public TradeBlock Block => Quote?.Block ?? TradeBlock.InGame;
        public bool Rejected => Block == TradeBlock.None && !Accepted;
    }

    /// <summary>
    /// 1:1 트레이드 규칙 (Phase 34). 순수 C#. 다대다 교환은 지원하지 않는다.
    /// - 대상: 리그 선수단 장부에서 내 구단·자유계약이 아닌 구단의 1군·퓨처스 선수 (대체 선수 제외). 리그 전체 구단을 순회한다.
    /// - 보낼 선수: 내 구단 1군(선발 타순·벤치·로테이션·불펜)·퓨처스 선수 (대체 선수 제외).
    /// - 실행: 트레이드 표시가 붙은 이적 기록 한 쌍 (내 선수 → 상대, 상대 선수 → 내 구단). 연봉은 두 선수의 지금 연봉이 따라간다.
    ///   장부가 한 쌍을 함께 적용한다 (<see cref="LeagueRosterBook"/>): 내준 선발 타자 자리는 김헌곤 룰(<see cref="PositionMatcher"/>)로
    ///   메우되 받은 선수를 맨 앞 후보로 보고, 내준 로테이션 투수 자리는 받은 투수가 잇는다. 그 밖에는 받은 선수가
    ///   1군 빈자리(타자 벤치·투수 불펜), 없으면 퓨처스로 들어간다. 상대 구단도 같은 규칙.
    /// - 사전 검사: 경기 중 · 선수 소속 · 1군 정원(26·벤치 5·불펜 7) · 교환 뒤 페이롤 &gt; 시즌 캡(페이롤이 늘 때만).
    /// - CPU 판단: <see cref="TradeEvaluator.Accepts"/>. 거절은 아무것도 바꾸지 않는다 (패널티 없음).
    /// - 성사: [오피셜] 트레이드 뉴스. 페이롤은 장부를 다시 짜며 바뀐다 (보낸 선수 연봉이 빠지고 받은 선수 연봉이 더해진다).
    /// </summary>
    public static class TradeDesk
    {
        public const string RejectedText = "[거절] 상대 구단이 제안을 매몰차게 거절했습니다.";

        static bool IsCpuTeam(string team, string myTeam) => team != myTeam && team != LeagueRosterBook.FreeAgentTeam;

        static IEnumerable<(PlayerData p, bool futures)> Everyone(TeamRoster r) =>
            LeagueRosterBook.AllOf(r).Select(p => (p, false)).Concat(r.Reserves.Select(p => (p, true)))
                .Where(x => !LeagueRosterBook.IsReplacement(x.p));

        /// <summary>타 구단 명단: 구단 이름 순, 구단 안에서는 1군 먼저 · 가치 높은 순.</summary>
        public static IReadOnlyList<TradeTarget> Targets(LeagueRosterBook book, string myTeam, IEnumerable<SigningRecord> signings)
        {
            if (book == null) return Array.Empty<TradeTarget>();
            var list = signings?.ToList();
            return book.Rosters.Where(r => IsCpuTeam(r.Team, myTeam))
                .OrderBy(r => r.Team, StringComparer.Ordinal)
                .SelectMany(r => Everyone(r)
                    .Select(x =>
                    {
                        long salary = StoveLeague.SalaryOf(x.p, r.Team, list);
                        return new TradeTarget(x.p, r.Team, x.futures, salary, TradeEvaluator.Value(x.p, salary));
                    })
                    .OrderBy(t => t.Futures).ThenByDescending(t => t.Value).ThenBy(t => t.Id, StringComparer.Ordinal))
                .ToList();
        }

        /// <summary>내가 보낼 수 있는 선수 (1군 먼저, 그다음 퓨처스).</summary>
        public static IReadOnlyList<TradeTarget> Offers(LeagueRosterBook book, string myTeam, IEnumerable<SigningRecord> signings)
        {
            var mine = book?.Get(myTeam);
            if (mine == null) return Array.Empty<TradeTarget>();
            var list = signings?.ToList();
            return Everyone(mine).Select(x =>
            {
                long salary = StoveLeague.SalaryOf(x.p, myTeam, list);
                return new TradeTarget(x.p, myTeam, x.futures, salary, TradeEvaluator.Value(x.p, salary));
            }).ToList();
        }

        /// <summary>트레이드 사전 평가: 막히는 이유, 두 선수 가치, CPU 수락 여부, 페이롤 변화, 받은 선수 자리.</summary>
        public static TradeQuote Quote(SeasonState season, LeagueRosterBook book, string myPlayerId, string theirPlayerId)
        {
            if (season == null) throw new ArgumentNullException(nameof(season));
            var q = new TradeQuote();
            if (season.InProgress != null) return Blocked(q, TradeBlock.InGame);
            var mine = book?.Get(season.MyTeam);
            q.Mine = mine == null ? null : Everyone(mine).Select(x => x.p).FirstOrDefault(p => p.Id == myPlayerId);
            if (q.Mine == null) return Blocked(q, TradeBlock.NotMine);
            foreach (var r in book.Rosters.Where(r => IsCpuTeam(r.Team, season.MyTeam)))
            {
                var p = Everyone(r).Select(x => x.p).FirstOrDefault(x => x.Id == theirPlayerId);
                if (p == null) continue;
                (q.Theirs, q.Partner) = (p, r.Team);
                break;
            }
            if (q.Theirs == null) return Blocked(q, TradeBlock.NotAvailable);

            q.MySalary = StoveLeague.SalaryOf(q.Mine, season.MyTeam, season.Signings);
            q.TheirSalary = StoveLeague.SalaryOf(q.Theirs, q.Partner, season.Signings);
            q.MyValue = TradeEvaluator.Value(q.Mine, q.MySalary);
            q.TheirValue = TradeEvaluator.Value(q.Theirs, q.TheirSalary);
            q.RequiredValue = TradeEvaluator.RequiredValue(q.TheirValue);
            q.WouldAccept = TradeEvaluator.Accepts(q.MyValue, q.TheirValue);

            var records = Records(season.MyTeam, q);
            var after = book.With(records);
            if (after.Applied.Count != book.Applied.Count + records.Length) return Blocked(q, TradeBlock.NotAvailable);
            var myAfter = after.Get(season.MyTeam);
            q.Arrival = myAfter.Lineup.Any(p => p.Id == theirPlayerId) ? TradeArrival.Lineup
                : myAfter.Starters.Any(p => p.Id == theirPlayerId) ? TradeArrival.Rotation
                : myAfter.Reserves.Any(p => p.Id == theirPlayerId) ? TradeArrival.Futures
                : q.Theirs.IsPitcher ? TradeArrival.Bullpen : TradeArrival.Bench;
            q.PayrollBefore = StoveLeague.Payroll(mine, season.Signings);
            q.PayrollAfter = StoveLeague.Payroll(myAfter, season.Signings.Concat(records));
            // 이미 넘어 있던 구단(이전 저장 파일)은 교환 때문에 막지 않는다.
            if (RosterLimits.IsOverLimit(myAfter) && !RosterLimits.IsOverLimit(mine)
                || RosterLimits.IsOverLimit(after.Get(q.Partner)) && !RosterLimits.IsOverLimit(book.Get(q.Partner)))
                return Blocked(q, TradeBlock.RosterFull);
            if (q.PayrollAfter > season.SalaryCap && q.PayrollAfter > q.PayrollBefore) return Blocked(q, TradeBlock.SalaryCap);
            q.After = after;
            return q;
        }

        /// <summary>
        /// [트레이드 제안]. 막히거나 거절되면 아무것도 바꾸지 않는다. 수락되면 이적 기록 두 건과 [오피셜] 뉴스를 남긴다
        /// (선수단 장부·페이롤·라인업 카드는 부른 쪽이 다시 짠다 — <see cref="SeasonManager.ProposeTrade"/>).
        /// </summary>
        public static TradeOutcome Propose(SeasonState season, LeagueRosterBook book, string myPlayerId, string theirPlayerId)
        {
            var q = Quote(season, book, myPlayerId, theirPlayerId);
            var outcome = new TradeOutcome { Quote = q };
            if (q.Block != TradeBlock.None || !q.WouldAccept) return outcome;
            var records = Records(season.MyTeam, q);
            season.AddSigning(records[0], isBatter: !q.Mine.IsPitcher);
            season.AddSigning(records[1], isBatter: !q.Theirs.IsPitcher);
            season.AddNews(NewsWriter.Traded(season.MyTeam, q.Partner, q.Mine.Name, q.Theirs.Name, season.Year, season.NewsDate));
            outcome.Accepted = true;
            return outcome;
        }

        /// <summary>내 선수 → 상대, 상대 선수 → 내 구단 (트레이드 한 쌍 — 장부가 함께 적용한다).</summary>
        static SigningRecord[] Records(string myTeam, TradeQuote q) => new[]
        {
            new SigningRecord(q.Mine.Id, myTeam, q.Partner, q.MySalary, trade: true),
            new SigningRecord(q.Theirs.Id, q.Partner, myTeam, q.TheirSalary, trade: true),
        };

        static TradeQuote Blocked(TradeQuote q, TradeBlock block)
        {
            q.Block = block;
            return q;
        }

        public static string ArrivalText(TradeArrival arrival)
        {
            switch (arrival)
            {
                case TradeArrival.Lineup: return "선발 라인업";
                case TradeArrival.Rotation: return "선발 로테이션";
                case TradeArrival.Bullpen: return "불펜";
                case TradeArrival.Futures: return "퓨처스";
                default: return "벤치";
            }
        }

        public static string BlockText(TradeBlock block)
        {
            switch (block)
            {
                case TradeBlock.None: return "";
                case TradeBlock.InGame: return "경기 중에는 트레이드할 수 없습니다.";
                case TradeBlock.NotMine: return "보낼 선수는 내 구단 1군·퓨처스 선수여야 합니다.";
                case TradeBlock.NotAvailable: return "타 구단 소속 선수가 아닙니다.";
                case TradeBlock.RosterFull:
                    return $"교환 뒤 1군 정원({RosterLimits.MaxRoster}명 · 벤치 {RosterLimits.MaxBench} · 불펜 {RosterLimits.MaxBullpen})을 넘습니다.";
                case TradeBlock.SalaryCap: return "교환 뒤 페이롤이 샐러리캡을 넘어 제안할 수 없습니다.";
                default: throw new ArgumentOutOfRangeException(nameof(block), block, null);
            }
        }
    }
}
