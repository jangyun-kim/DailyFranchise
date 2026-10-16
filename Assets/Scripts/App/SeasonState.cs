using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    public enum GameStatus
    {
        Scheduled,
        InProgress,
        Final,
    }

    public enum GameOutcome
    {
        Win,
        Loss,
        Draw,
    }

    /// <summary>하루 경기 결과 한 건 (내 경기 또는 타 구단 경기).</summary>
    public sealed class GameRecord
    {
        public string AwayTeam;
        public string HomeTeam;
        public GameScore Score;

        public GameOutcome OutcomeFor(string team)
        {
            if (team != AwayTeam && team != HomeTeam) throw new ArgumentException($"이 경기 팀이 아니다: {team}", nameof(team));
            if (Score.Away == Score.Home) return GameOutcome.Draw;
            bool awayWon = Score.Away > Score.Home;
            return (team == AwayTeam) == awayWon ? GameOutcome.Win : GameOutcome.Loss;
        }

        /// <summary>이 팀의 득점.</summary>
        public int RunsFor(string team)
        {
            if (team != AwayTeam && team != HomeTeam) throw new ArgumentException($"이 경기 팀이 아니다: {team}", nameof(team));
            return team == AwayTeam ? Score.Away : Score.Home;
        }

        /// <summary>이 팀의 실점 (상대 득점).</summary>
        public int RunsAgainst(string team)
        {
            if (team != AwayTeam && team != HomeTeam) throw new ArgumentException($"이 경기 팀이 아니다: {team}", nameof(team));
            return team == AwayTeam ? Score.Home : Score.Away;
        }
    }

    /// <summary>최근 N경기 전적 (예: L10 "6승 4패").</summary>
    public readonly struct RecentForm
    {
        public RecentForm(int wins, int losses, int draws)
        {
            Wins = wins;
            Losses = losses;
            Draws = draws;
        }

        public int Wins { get; }
        public int Losses { get; }
        public int Draws { get; }
        public int Games => Wins + Losses + Draws;

        /// <summary>"6승 4패" (무승부가 있으면 "5승 4패 1무"). 경기가 없으면 "—".</summary>
        public string Text => Games == 0 ? "—" : Draws == 0 ? $"{Wins}승 {Losses}패" : $"{Wins}승 {Losses}패 {Draws}무";
    }

    public sealed class ScheduledGame
    {
        public int Number;
        public string Date;
        public string AwayTeam;
        public string HomeTeam;
        public string Stadium;
        public GameStatus Status;
        /// <summary>종료된 경기의 최종 점수. 끝나지 않았으면 null.</summary>
        public GameScore? Score;

        public string MatchupText => $"{AwayTeam} vs {HomeTeam}";
    }

    public sealed class StandingRow
    {
        public int Rank;
        public string Team;
        public int Wins;
        public int Losses;
        public int Draws;
        /// <summary>승차 표기 (1위는 "—").</summary>
        public string GamesBehind;
        /// <summary>가장 최근 경기일 직전 순위. 0이면 아직 없다 (시즌 첫 경기 전·이전 저장 파일).</summary>
        public int PreviousRank;
        /// <summary>시즌 누적 득점(RS)·실점(RA). 이 값이 없던 이전 저장 파일은 0에서 시작한다.</summary>
        public int RunsScored;
        public int RunsAllowed;

        /// <summary>득실차 (RS - RA).</summary>
        public int RunDifferential => RunsScored - RunsAllowed;

        /// <summary>득실차 표기 (예: "+12", "-3", "0").</summary>
        public string RunDifferentialText => RunDifferential > 0 ? "+" + RunDifferential : RunDifferential.ToString(CultureInfo.InvariantCulture);

        public int GamesPlayed => Wins + Losses + Draws;

        /// <summary>직전 순위 대비 변동 (+면 상승). 직전 순위가 없으면 0.</summary>
        public int RankChange => PreviousRank == 0 ? 0 : PreviousRank - Rank;

        /// <summary>순위 변동 표기 (예: "+2", "-1"). 변동이 없거나 직전 순위가 없으면 "".</summary>
        public string RankChangeText => RankChange > 0 ? "+" + RankChange : RankChange < 0 ? RankChange.ToString(CultureInfo.InvariantCulture) : "";

        /// <summary>KBO 승률 = 승 / (승 + 패). 무승부는 제외한다.</summary>
        public double WinPct => Wins + Losses == 0 ? 0 : (double)Wins / (Wins + Losses);

        /// <summary>승률 표기 (예: 0.561).</summary>
        public string WinPctText => WinPct.ToString("0.000", CultureInfo.InvariantCulture);

        /// <summary>경기 한 건을 반영한다: 승/패/무와 득실점.</summary>
        public void Apply(GameRecord game)
        {
            Apply(game.OutcomeFor(Team));
            RunsScored += game.RunsFor(Team);
            RunsAllowed += game.RunsAgainst(Team);
        }

        public void Apply(GameOutcome outcome)
        {
            switch (outcome)
            {
                case GameOutcome.Win: Wins++; break;
                case GameOutcome.Loss: Losses++; break;
                default: Draws++; break;
            }
        }
    }

    /// <summary>
    /// 시즌 진행 상태. 씬과 무관한 순수 C#이며 SeasonManager가 씬 전환 동안 들고 있는다.
    /// 경기가 끝나면 내 경기 점수와 같은 날 타 구단 경기 결과(난수)를 성적에 반영하고 순위를 다시 매긴다.
    /// </summary>
    public sealed class SeasonState : IConditionSource
    {
        readonly List<ScheduledGame> _schedule;
        readonly List<StandingRow> _standings;

        /// <summary>타 구단 경기 무승부 확률 (KBO는 연장 12회 무승부가 있다).</summary>
        public const double OtherGameDrawRate = 0.03;

        /// <summary>KBO 팀당 정규시즌 경기 수. 순위 동률 판정의 잔여 경기 계산에 쓴다.</summary>
        public const int GamesPerTeam = 144;

        /// <summary>순위표 정렬 규칙 (승률 → 다승 → 무승부 → 잔여 경기).</summary>
        public static readonly StandingComparer StandingOrder = new StandingComparer(GamesPerTeam);

        readonly List<GameRecord> _lastDay = new List<GameRecord>();

        /// <summary>새 시즌 기본 연도 (Phase 27).</summary>
        public const int DefaultYear = 2026;

        /// <param name="seasonTitle">연도를 읽을 제목 (예: "2026 시즌 · 5월"). 앞 네 자리가 연도가 아니면 <see cref="DefaultYear"/>.</param>
        public SeasonState(string seasonTitle, string myTeam, List<ScheduledGame> schedule, List<StandingRow> standings)
        {
            if (schedule == null || schedule.Count == 0) throw new ArgumentException("일정이 비어 있다.", nameof(schedule));
            Year = YearOf(seasonTitle) ?? DefaultYear;
            MyTeam = myTeam;
            _schedule = schedule;
            _standings = standings;
            Rerank();
        }

        /// <summary>시즌 연도 (Phase 27). 오프시즌 결산(<see cref="TransitionToNextSeason"/>)마다 1씩 오른다. 시즌과 함께 저장된다.</summary>
        public int Year { get; private set; }

        /// <summary>
        /// 로비 상단 시즌 제목: "2026 시즌 · 5월" (다음 경기 날짜의 달), 일정을 모두 치르면 "2026 시즌 · 정규시즌 종료".
        /// </summary>
        public string SeasonTitle
        {
            get
            {
                if (Phase == SeasonPhase.PostSeason) return $"{Year} 시즌 · {PostseasonSeries.Title}";
                if (Phase == SeasonPhase.OffSeason) return $"{Year} 시즌 · 시즌 종료";
                if (IsSeasonFinished) return $"{Year} 시즌 · 정규시즌 종료";
                int? month = MonthDayOf(NextGame?.Date)?.Month;
                return month.HasValue ? $"{Year} 시즌 · {month}월" : $"{Year} 시즌";
            }
        }

        /// <summary>제목 앞 네 자리 연도 (예: "2026 시즌" → 2026). 없으면 null.</summary>
        public static int? YearOf(string title)
        {
            if (title == null || title.Length < 4) return null;
            if (!int.TryParse(title.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year)) return null;
            return year >= 1000 && (title.Length == 4 || !char.IsDigit(title[4])) ? year : (int?)null;
        }

        /// <summary>일정 날짜 표기 "5.15 (금)"의 월·일. 읽을 수 없으면 null.</summary>
        static (int Month, int Day)? MonthDayOf(string date)
        {
            if (string.IsNullOrEmpty(date)) return null;
            int dot = date.IndexOf('.');
            if (dot <= 0) return null;
            int end = dot + 1;
            while (end < date.Length && char.IsDigit(date[end])) end++;
            if (!int.TryParse(date.Substring(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out int m)
                || !int.TryParse(date.Substring(dot + 1, end - dot - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int d))
                return null;
            return m >= 1 && m <= 12 && d >= 1 && d <= 31 ? (m, d) : ((int, int)?)null;
        }

        static readonly string[] KoreanWeekdays = { "일", "월", "화", "수", "목", "금", "토" };

        /// <summary>
        /// 다음 해 같은 월·일의 날짜 표기 (Phase 27), 요일은 그해 달력으로 다시 붙인다. 예: 2026 "5.15 (금)" → 2027 "5.15 (토)".
        /// (같은 요일로 맞추면 해마다 하루씩 당겨져 일정이 흘러가므로 월·일을 고정한다.) 2.29는 평년이면 2.28.
        /// 읽을 수 없거나 그해에 없는 날짜 표기는 그대로.
        /// </summary>
        /// <param name="year">지금 날짜의 연도.</param>
        public static string NextYearDate(string date, int year)
        {
            var md = MonthDayOf(date);
            if (md == null) return date;
            var (month, day) = md.Value;
            int next = year + 1;
            if (month == 2 && day == 29 && !DateTime.IsLeapYear(next)) day = 28;
            if (day > DateTime.DaysInMonth(next, month)) return date;
            var to = new DateTime(next, month, day);
            return $"{to.Month}.{to.Day} ({KoreanWeekdays[(int)to.DayOfWeek]})";
        }
        /// <summary>일정 날짜 표기("5.26 (화)")를 그해 날짜로. 읽을 수 없거나 그해에 없는 날짜면 null.</summary>
        public static DateTime? DateOf(string date, int year)
        {
            var md = MonthDayOf(date);
            if (md == null || year < 1 || year > 9999) return null;
            var (month, day) = md.Value;
            return day > DateTime.DaysInMonth(year, month) ? (DateTime?)null : new DateTime(year, month, day);
        }

        /// <summary>날짜 표기 "5.28 (목)".</summary>
        public static string DateText(DateTime day) => $"{day.Month}.{day.Day} ({KoreanWeekdays[(int)day.DayOfWeek]})";

        public string MyTeam { get; }
        public IReadOnlyList<ScheduledGame> Schedule => _schedule;
        public IReadOnlyList<StandingRow> Standings => _standings;

        /// <summary>다음에 치를 경기의 일정 인덱스. 시즌이 끝나면 Schedule.Count.</summary>
        public int NextIndex { get; private set; }
        /// <summary>진행 중인 경기. 로비에 있으면 null.</summary>
        public ScheduledGame InProgress { get; private set; }

        /// <summary>
        /// 사용자가 바꾼 내 구단 타자 라인업. null이면 선수 DB 기본 라인업을 쓴다.
        /// 시즌과 함께 저장된다.
        /// </summary>
        public LineupCard MyLineup { get; set; }

        /// <summary>구단 예산 (원, long — Phase 26). 0 이상 (<see cref="FranchiseFinance.ClampBudget"/>). 시즌과 함께 저장된다.</summary>
        public long Budget { get; private set; } = FranchiseFinance.DefaultBudget;

        /// <summary>팬 만족도 (%). 0~100 (<see cref="FranchiseFinance.ClampFanApproval"/>). 시즌과 함께 저장된다.</summary>
        public int FanApproval { get; private set; } = FranchiseFinance.DefaultFanApproval;

        /// <summary>
        /// 재정 변화를 적용한다. 예산은 0 아래로, 팬 만족도는 0~100 밖으로 나가지 않는다.
        /// </summary>
        /// <returns>범위 제한 뒤 실제로 바뀐 양.</returns>
        public FinanceChange ApplyFinance(FinanceChange change)
        {
            long budget = Budget;
            int fan = FanApproval;
            SetFinance(FranchiseFinance.AddBudget(Budget, change.BudgetDelta), (long)FanApproval + change.FanDelta);
            return new FinanceChange(Budget - budget, FanApproval - fan);
        }

        /// <summary>해금한 팬덤 슬롯 (Phase 23, <see cref="FandomRules"/>). 한 번 해금되면 잃지 않는다. 시즌과 함께 저장된다.</summary>
        public FandomSlot UnlockedSlots { get; private set; }

        /// <summary>슬롯을 정한다 (불러오기·테스트용). 모르는 비트는 버린다.</summary>
        public void SetUnlockedSlots(FandomSlot slots) => UnlockedSlots = slots & FandomSlot.All;

        /// <summary>지금 성적·팬 만족도로 슬롯 해금을 갱신한다.</summary>
        /// <returns>이번에 새로 해금된 슬롯.</returns>
        public FandomSlot UpdateFandomUnlocks()
        {
            var before = UnlockedSlots;
            UnlockedSlots = FandomRules.Unlocked(before, MyStanding?.Wins ?? 0, FanApproval);
            return UnlockedSlots & ~before;
        }

        /// <summary>프런트 신뢰도 TP (Phase 23, <see cref="FrontOfficeTrust"/>). 0~200. 시즌과 함께 저장된다.</summary>
        public int TrustPoints { get; private set; }
        /// <summary>이번 시즌에 적립한 TP (상한 <see cref="FrontOfficeTrust.MaxSeasonEarn"/>).</summary>
        public int TrustEarnedThisSeason { get; private set; }
        /// <summary>이번 시즌 캡에서 빼는 상환액 (직전 시즌 신뢰 대출의 50%).</summary>
        public long CapRepay { get; private set; }
        /// <summary>이번 시즌 신뢰 대출로 늘린 캡 (최대 캡 기준의 10%).</summary>
        public long CapFlex { get; private set; }

        /// <summary>
        /// 샐러리캡 기준 (Phase 26): 리그 개막 평균 페이롤 × 1.25 (<see cref="StoveLeague.OpeningCap"/>).
        /// 선수 DB에서 다시 계산하는 값이라 저장하지 않는다. DB를 모르면 <see cref="FrontOfficeTrust.DefaultSalaryCap"/>.
        /// </summary>
        public long SalaryCapBase { get; private set; } = FrontOfficeTrust.DefaultSalaryCap;

        /// <summary>캡 기준을 정한다 (0 이하는 무시). 대출은 새 기준의 10% 안으로 맞춘다.</summary>
        public void SetSalaryCapBase(long baseCap)
        {
            if (baseCap <= 0) return;
            SalaryCapBase = baseCap;
            CapFlex = Math.Min(CapFlex, FrontOfficeTrust.MaxFlexOf(baseCap));
        }

        /// <summary>이번 시즌 샐러리캡 = 캡 기준 − 상환 + 대출.</summary>
        public long SalaryCap => FrontOfficeTrust.EffectiveCap(SalaryCapBase, CapRepay, CapFlex);

        /// <summary>
        /// 내 구단 1군 총연봉 (Phase 26, <see cref="StoveLeague.Payroll"/>). 선수단 장부가 바뀔 때 SeasonManager가 맞춰 둔다
        /// (선수 DB에서 다시 계산하는 값이라 저장하지 않는다). 경기마다 이 값 ÷ 144가 유지비로 나간다.
        /// </summary>
        public long Payroll { get; private set; }

        public void SetPayroll(long payroll) => Payroll = Math.Max(0, payroll);

        /// <summary>신뢰도 상태를 범위 안으로 맞춰 정한다 (불러오기·테스트용).</summary>
        public void SetTrust(int trust, int earnedThisSeason, long capRepay, long capFlex)
        {
            TrustPoints = Math.Max(0, Math.Min(FrontOfficeTrust.MaxTrust, trust));
            TrustEarnedThisSeason = Math.Max(0, Math.Min(FrontOfficeTrust.MaxSeasonEarn, earnedThisSeason));
            CapRepay = Math.Max(0, Math.Min(SalaryCapBase, capRepay));
            CapFlex = Math.Max(0, Math.Min(FrontOfficeTrust.MaxFlexOf(SalaryCapBase), capFlex));
        }

        /// <summary>TP를 적립한다. TP 상한·시즌 적립 상한을 넘는 부분은 버린다.</summary>
        /// <returns>실제로 적립한 양.</returns>
        public int EarnTrust(int amount)
        {
            int room = Math.Min(FrontOfficeTrust.MaxTrust - TrustPoints, FrontOfficeTrust.MaxSeasonEarn - TrustEarnedThisSeason);
            int earned = Math.Max(0, Math.Min(amount, room));
            TrustPoints += earned;
            TrustEarnedThisSeason += earned;
            return earned;
        }

        /// <summary>
        /// 영입 협상 한 번 (수수료만 다룬다 — 연봉은 경기마다 페이롤로 나간다). 수수료를 예산에서 내고
        /// 소프트 천장 성공률로 판정한다. 성공하면 천장 카운터(TP 최대 100)를 초기화하고, 결렬이면 성의 있는 제시일 때
        /// 수수료를 TP로 환급한다. 예산이 수수료보다 적으면 시도하지 않는다.
        /// </summary>
        public NegotiationResult Negotiate(long offer, long ask, IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            double rate = FrontOfficeTrust.SuccessRate(offer, ask, TrustPoints);
            long fee = FrontOfficeTrust.FeeOf(ask);
            if (Budget < fee) return new NegotiationResult(false, false, false, rate, 0, 0);

            ApplyFinance(new FinanceChange(-fee, 0));
            bool signed = random.NextDouble() < rate;
            int delta;
            if (signed)
            {
                delta = -Math.Min(TrustPoints, FrontOfficeTrust.Ceiling);
                TrustPoints += delta;
            }
            else
            {
                delta = EarnTrust(FrontOfficeTrust.RefundOnFailure(offer, ask));
            }
            return new NegotiationResult(true, signed, false, rate, fee, delta);
        }

        /// <summary>
        /// 하드 천장 [확정 영입] (Phase 25): TP 100을 쓰고 난수 없이 영입한다. 수수료는 낸다 (연봉은 경기마다 페이롤로).
        /// TP가 100 미만이거나 예산이 수수료보다 적으면 시도하지 않는다.
        /// </summary>
        public NegotiationResult SignWithCeiling(long ask)
        {
            long fee = FrontOfficeTrust.FeeOf(ask);
            if (!FrontOfficeTrust.CanUseCeiling(TrustPoints) || Budget < fee) return new NegotiationResult(false, false, false, 1, 0, 0);
            ApplyFinance(new FinanceChange(-fee, 0));
            TrustPoints -= FrontOfficeTrust.Ceiling;
            return new NegotiationResult(true, true, true, 1, fee, -FrontOfficeTrust.Ceiling);
        }

        /// <summary>
        /// TP를 써서 이번 시즌 캡을 늘린다 (신뢰 대출). 시즌 대출 한도(캡 기준의 10%)와 가진 TP 안에서만 쓰고,
        /// 한도를 채우는 데 필요한 만큼만 TP를 깎는다.
        /// </summary>
        /// <returns>이번에 늘어난 캡.</returns>
        public long BorrowCap(int trust)
        {
            long room = Math.Max(0, FrontOfficeTrust.MaxFlexOf(SalaryCapBase) - CapFlex);
            int usable = Math.Min(Math.Max(0, trust), TrustPoints);
            usable = (int)Math.Min(usable, (room + FrontOfficeTrust.CapPerTrust - 1) / FrontOfficeTrust.CapPerTrust);
            long flex = Math.Min(room, FrontOfficeTrust.FlexFor(usable, SalaryCapBase));
            TrustPoints -= usable;
            CapFlex += flex;
            return flex;
        }

        /// <summary>
        /// 시즌 마감: 이번 대출의 50%를 다음 시즌 상환으로 넘기고 대출·시즌 적립을 비우며, TP는 100까지만 이월한다.
        /// </summary>
        public void CloseTrustSeason()
        {
            CapRepay = FrontOfficeTrust.NextRepay(CapFlex);
            CapFlex = 0;
            TrustEarnedThisSeason = 0;
            TrustPoints = Math.Min(TrustPoints, FrontOfficeTrust.CarryOver);
        }

        readonly List<SigningRecord> _signings = new List<SigningRecord>();

        /// <summary>
        /// 이적 기록 (Phase 25 영입, Phase 26 방출 — 받는 구단이 <see cref="LeagueRosterBook.FreeAgentTeam"/>이면 방출).
        /// 선수단은 "선수 DB + 이 기록"으로 다시 짠다 (<see cref="LeagueRosterBook"/>). 시즌과 함께 저장된다.
        /// </summary>
        public IReadOnlyList<SigningRecord> Signings => _signings;

        /// <summary>
        /// 영입 기록을 남긴다. 타자이고 사용자 라인업 카드가 있으면 그 카드 벤치 끝에도 넣는다
        /// (카드가 새 선수단과 계속 맞도록 — 안 넣으면 기본 라인업으로 돌아간다).
        /// </summary>
        public void AddSigning(SigningRecord record, bool isBatter)
        {
            _signings.Add(record ?? throw new ArgumentNullException(nameof(record)));
            if (!isBatter || MyLineup == null) return;
            if (record.ToTeam == MyTeam && !MyLineup.Bench.Contains(record.PlayerId)
                && MyLineup.Batting.All(s => s.PlayerId != record.PlayerId))
                MyLineup = new LineupCard(MyLineup.Batting, MyLineup.Bench.Append(record.PlayerId));
            else if (record.FromTeam == MyTeam && MyLineup.Bench.Contains(record.PlayerId))
                MyLineup = new LineupCard(MyLineup.Batting, MyLineup.Bench.Where(id => id != record.PlayerId));
        }

        readonly Dictionary<string, int> _conditions = new Dictionary<string, int>();

        /// <summary>
        /// 선수 컨디션 (선수 DB ID → 1~5, Phase 22). 기록이 없는 선수는 보통(3). 시즌과 함께 저장된다.
        /// </summary>
        public IReadOnlyDictionary<string, int> Conditions => _conditions;

        /// <summary>선수 컨디션. 기록이 없으면 <see cref="PlayerCondition.Default"/>(보통).</summary>
        public int ConditionOf(string playerId) =>
            playerId != null && _conditions.TryGetValue(playerId, out int c) ? c : PlayerCondition.Default;

        /// <summary>선수 컨디션을 1~5로 맞춰 정한다 (불러오기·테스트용).</summary>
        public void SetCondition(string playerId, int condition)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("선수 ID가 비어 있다.", nameof(playerId));
            _conditions[playerId] = PlayerCondition.Clamp(condition);
        }

        readonly Dictionary<string, string> _lastStarters = new Dictionary<string, string>();

        /// <summary>구단별 직전 경기 선발 투수 ID (선발 로테이션 순번, <see cref="RotationPicker"/>). 시즌과 함께 저장된다.</summary>
        public IReadOnlyDictionary<string, string> LastStarters => _lastStarters;

        /// <summary>구단의 직전 선발. 모르면 null (로테이션 1번부터).</summary>
        public string LastStarterOf(string team) =>
            team != null && _lastStarters.TryGetValue(team, out var id) ? id : null;

        public void SetLastStarter(string team, string playerId)
        {
            if (string.IsNullOrEmpty(team) || string.IsNullOrEmpty(playerId)) return;
            _lastStarters[team] = playerId;
        }

        /// <summary>경기 출장 기록으로 컨디션을 바꾸고(<see cref="ConditionRules"/>) 두 구단의 선발 순번을 남긴다.</summary>
        public List<ConditionChange> ApplyConditions(GameParticipation game, IRandomSource random)
        {
            var changes = ConditionRules.Apply(_conditions, game, random);
            foreach (var kv in game.StartingPitchers) SetLastStarter(kv.Key, kv.Value);
            return changes;
        }

        readonly Dictionary<string, PlayerProgression> _progression = new Dictionary<string, PlayerProgression>();

        /// <summary>
        /// 선수 변동치 (Phase 29, 선수 DB ID → 나이 증가·폼·은퇴). 마스터 CSV는 그대로 두고 이 값만 저장한다.
        /// 선수단 장부가 덧씌운다 (<see cref="LeagueRosterBook.Build"/>).
        /// </summary>
        public IReadOnlyDictionary<string, PlayerProgression> Progression => _progression;

        /// <summary>변동치가 바뀔 때마다 오른다 (선수단 장부를 다시 짤지 판단용, 저장하지 않는다).</summary>
        public int ProgressionVersion { get; private set; }

        /// <summary>선수 변동치. 없으면 null (원본 그대로).</summary>
        public PlayerProgression ProgressionOf(string playerId) =>
            playerId != null && _progression.TryGetValue(playerId, out var p) ? p : null;

        public void SetProgression(PlayerProgression progression)
        {
            if (progression == null) throw new ArgumentNullException(nameof(progression));
            _progression[progression.PlayerId] = progression;
            ProgressionVersion++;
        }

        readonly List<PlayerData> _rookies = new List<PlayerData>();

        /// <summary>
        /// 드래프트로 생긴 가상 신인 (Phase 30, 지명 순). 마스터 CSV에 없는 선수라 기록 전체를 <see cref="PlayerData"/> 그대로
        /// 시즌과 함께 저장한다. <see cref="PlayerData.Team"/>이 지명 구단이고, 선수단 장부가 그 구단 퓨처스 끝에 넣는다
        /// (<see cref="LeagueRosterBook.Build"/>). 나이·폼·은퇴는 다른 선수처럼 <see cref="Progression"/>으로 바뀐다.
        /// </summary>
        public IReadOnlyList<PlayerData> Rookies => _rookies;

        /// <summary>신인을 더한다. ID가 비었거나 이미 있으면 무시하고 false.</summary>
        public bool AddRookie(PlayerData rookie)
        {
            if (rookie == null || string.IsNullOrEmpty(rookie.Id) || string.IsNullOrEmpty(rookie.Team)) return false;
            if (_rookies.Any(r => r.Id == rookie.Id)) return false;
            _rookies.Add(rookie);
            return true;
        }

        /// <summary>리그 뉴스 보관 개수 (Phase 28). 넘치면 오래된 뉴스부터 버린다 — 저장 파일이 커지지 않게.</summary>
        public const int MaxNews = 15;

        readonly Queue<NewsItem> _news = new Queue<NewsItem>();

        /// <summary>최근 리그 뉴스 (오래된 것부터, 최대 <see cref="MaxNews"/>건). 시즌과 함께 저장된다.</summary>
        public IReadOnlyCollection<NewsItem> RecentNews => _news;

        /// <summary>최신 뉴스부터 최대 <paramref name="count"/>건 (로비 뉴스 패널).</summary>
        public IReadOnlyList<NewsItem> LatestNews(int count) =>
            _news.Reverse().Take(Math.Max(0, count)).ToList();

        /// <summary>뉴스를 발행한다. <see cref="MaxNews"/>를 넘으면 가장 오래된 뉴스를 꺼내 버린다.</summary>
        public void AddNews(NewsItem item)
        {
            _news.Enqueue(item ?? throw new ArgumentNullException(nameof(item)));
            while (_news.Count > MaxNews) _news.Dequeue();
        }

        /// <summary>로비에서 일어난 일(영입·방출 등)의 뉴스 날짜: 다음 경기일, 시즌이 끝났으면 마지막 경기일.</summary>
        public string NewsDate => NextGame?.Date ?? Postseason?.LastPlayed?.Date ?? LastFinished?.Date ?? "";

        /// <summary>내 구단 현재 연승(+)·연패(−). 무승부나 치른 경기가 없으면 0.</summary>
        public int MyStreak()
        {
            int streak = 0;
            for (int i = NextIndex - 1; i >= 0; i--)
            {
                var o = MyOutcome(_schedule[i]);
                if (o == GameOutcome.Win && streak >= 0) streak++;
                else if (o == GameOutcome.Loss && streak <= 0) streak--;
                else break;
            }
            return streak;
        }

        /// <summary>예산·팬 만족도를 범위 안으로 맞춰 정한다 (불러오기·테스트용).</summary>
        public void SetFinance(long budget, long fanApproval)
        {
            Budget = FranchiseFinance.ClampBudget(budget);
            FanApproval = FranchiseFinance.ClampFanApproval(fanApproval);
        }

        /// <summary>가장 최근에 끝난 날의 경기 결과 (첫 항목이 내 경기). 아직 없으면 비어 있다.</summary>
        public IReadOnlyList<GameRecord> LastDayResults => _lastDay;

        /// <summary>가장 최근에 끝난 내 경기. 없으면 null.</summary>
        public ScheduledGame LastFinished => NextIndex > 0 ? _schedule[NextIndex - 1] : null;

        /// <summary>
        /// 다음에 치를 내 경기: 정규시즌이면 일정의 다음 경기, 포스트시즌이면 내 구단이 나가는 한국시리즈의 다음 경기
        /// (Phase 32 — 탈락했거나 시리즈가 끝났으면 null), 오프시즌이면 null.
        /// </summary>
        public ScheduledGame NextGame =>
            Phase == SeasonPhase.RegularSeason ? (NextIndex < _schedule.Count ? _schedule[NextIndex] : null)
            : Phase == SeasonPhase.PostSeason && Postseason != null && Postseason.Involves(MyTeam) ? Postseason.NextGame
            : null;

        /// <summary>정규시즌 일정을 모두 치렀다 (다음 경기 인덱스 ≥ 일정 수). 포스트시즌·오프시즌에도 true.</summary>
        public bool IsSeasonFinished => NextIndex >= _schedule.Count;

        /// <summary>정규시즌 일정을 모두 치렀다 (<see cref="IsSeasonFinished"/>와 같다 — Phase 32에서 뜻을 분명히 한 이름).</summary>
        public bool IsRegularSeasonFinished => IsSeasonFinished;

        /// <summary>시즌 진행 단계 (Phase 32). 시즌과 함께 저장된다 (없는 이전 파일은 정규시즌).</summary>
        public SeasonPhase Phase { get; private set; } = SeasonPhase.RegularSeason;

        /// <summary>이번 시즌 한국시리즈. 정규시즌 중이면 null. 결산하면 비운다.</summary>
        public PostseasonSeries Postseason { get; private set; }

        /// <summary>내 구단이 한국시리즈에 나가는지 (포스트시즌·오프시즌).</summary>
        public bool InPostseason => Postseason != null && Postseason.Involves(MyTeam);

        /// <summary>한국시리즈 우승 상금 (Phase 32, 내 구단이 우승하면 예산에 바로 더한다).</summary>
        public const long ChampionPrize = 2_000_000_000;

        /// <summary>이번 실행에서 내 구단이 받은 우승 보상 (예산·팬 만족도 실제 변화). 없으면 null.</summary>
        public FinanceChange? ChampionAward { get; private set; }

        /// <summary>가장 최근 시리즈 경기 처리로 시즌이 발행한 뉴스 (경기 결과·우승). PostGameProcessor가 보고서에 싣는다.</summary>
        public IReadOnlyList<NewsItem> LastPostseasonNews => _lastPostseasonNews;
        readonly List<NewsItem> _lastPostseasonNews = new List<NewsItem>();

        /// <summary>
        /// 정규시즌 일정을 다 치렀으면 한국시리즈를 연다 (Phase 32): 순위표 1·2위의 7전 4선승제, [가을야구] 개막 뉴스.
        /// 경기 중이거나 이미 열었으면 아무것도 하지 않고 false. 순위표 팀이 둘 미만이면 바로 오프시즌.
        /// 마지막 정규시즌 경기 처리(<see cref="FinishCurrentGame"/>)와 이전 저장 파일 불러오기 뒤(SeasonManager)에 부른다.
        /// </summary>
        public bool BeginPostseasonIfDue()
        {
            if (Phase != SeasonPhase.RegularSeason || !IsRegularSeasonFinished || InProgress != null) return false;
            if (_standings.Count < 2)
            {
                Phase = SeasonPhase.OffSeason;
                return true;
            }
            Postseason = PostseasonSeries.Create(_standings[0].Team, _standings[1].Team, Year, _schedule[_schedule.Count - 1].Date, KboStadiums);
            Phase = SeasonPhase.PostSeason;
            AddNews(NewsWriter.PostseasonBegins(Postseason, MyTeam, Year));
            return true;
        }

        /// <summary>
        /// 남은 한국시리즈를 결과만 빠르게 정한다 (Phase 32 — 내 구단이 탈락했을 때 로비 [가을야구 결과 시뮬레이션]).
        /// 경기마다 무승부 없는 점수(승 1~10점 차)를 반반 확률로 정하고, 우승이 정해지면 오프시즌으로 넘긴다.
        /// 포스트시즌이 아니거나 경기 중이면 false.
        /// </summary>
        public bool SimulatePostseason(IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (Phase != SeasonPhase.PostSeason || Postseason == null || InProgress != null) return false;
            while (!Postseason.IsOver)
            {
                int loser = random.Next(7);
                int winner = loser + 1 + random.Next(5);
                Postseason.Record(random.NextDouble() < 0.5 ? new GameScore(winner, loser) : new GameScore(loser, winner));
            }
            ConcludePostseason();
            return true;
        }

        /// <summary>우승 팀 결정: [챔피언] 뉴스, 내 구단 우승이면 상금(₩20억)과 팬 만족도 100%, 오프시즌으로.</summary>
        void ConcludePostseason()
        {
            Phase = SeasonPhase.OffSeason;
            string champion = Postseason.Champion;
            var news = NewsWriter.Champion(champion, Postseason, Year);
            AddNews(news);
            _lastPostseasonNews.Add(news);
            if (champion == MyTeam)
                ChampionAward = ApplyFinance(new FinanceChange(ChampionPrize, FranchiseFinance.MaxFanApproval - FanApproval));
        }

        /// <summary>진행 중인 한국시리즈 경기를 끝낸다 (정규시즌 순위표·타 구단 경기는 건드리지 않는다).</summary>
        void FinishPostseasonGame(GameScore score, int finalInning, bool walkOff)
        {
            var game = InProgress;
            _lastDay.Clear();
            _lastDay.Add(new GameRecord { AwayTeam = game.AwayTeam, HomeTeam = game.HomeTeam, Score = score });
            _lastPostseasonNews.Clear();
            Postseason.Record(score);
            InProgress = null;
            var news = NewsWriter.SeriesGame(MyTeam, game, Postseason, finalInning, walkOff, Year);
            AddNews(news);
            _lastPostseasonNews.Add(news);
            if (Postseason.IsOver) ConcludePostseason();
        }
        public int GamesPlayed => NextIndex;

        public StandingRow MyStanding => StandingOf(MyTeam);

        public StandingRow StandingOf(string team)
        {
            foreach (var row in _standings)
                if (row.Team == team) return row;
            return null;
        }

        /// <summary>내 구단 기준 경기 결과. 끝나지 않은 경기면 null.</summary>
        public GameOutcome? MyOutcome(ScheduledGame game) =>
            game.Score.HasValue
                ? new GameRecord { AwayTeam = game.AwayTeam, HomeTeam = game.HomeTeam, Score = game.Score.Value }.OutcomeFor(MyTeam)
                : (GameOutcome?)null;

        /// <summary>
        /// 내 구단 최근 경기 전적 (일정에서 끝난 경기 중 마지막 <paramref name="count"/>경기).
        /// 저장된 일정만 보므로 이 세이브에서 치른 경기만 센다.
        /// </summary>
        public RecentForm MyRecentForm(int count = 10)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "1 이상이어야 한다.");
            int w = 0, l = 0, d = 0;
            for (int i = NextIndex - 1; i >= 0 && i >= NextIndex - count; i--)
            {
                switch (MyOutcome(_schedule[i]))
                {
                    case GameOutcome.Win: w++; break;
                    case GameOutcome.Loss: l++; break;
                    case GameOutcome.Draw: d++; break;
                }
            }
            return new RecentForm(w, l, d);
        }

        public ScheduledGame StartNextGame()
        {
            if (InProgress != null) throw new InvalidOperationException($"이미 진행 중인 경기가 있다: {InProgress.Number}");
            var game = NextGame ?? throw new InvalidOperationException("남은 경기가 없다.");
            game.Status = GameStatus.InProgress;
            InProgress = game;
            return game;
        }

        /// <summary>내 구단·상대 득점을 진행 중인 경기 일정의 원정 : 홈 점수로 바꾼다.</summary>
        public GameScore ScheduleScore(int myRuns, int opponentRuns)
        {
            var game = InProgress ?? throw new InvalidOperationException("진행 중인 경기가 없다.");
            return game.HomeTeam == MyTeam ? new GameScore(opponentRuns, myRuns) : new GameScore(myRuns, opponentRuns);
        }

        /// <summary>
        /// 진행 중인 경기를 최종 점수로 끝낸다. 내 경기와, 남은 구단끼리 짝지은 같은 날 타 구단 경기(난수)를
        /// 성적에 반영한 뒤 순위를 다시 매기고 일정을 다음 경기로 전진한다. 반영 직전 순위는 각 팀의
        /// <see cref="StandingRow.PreviousRank"/>에 남는다 (로비 순위 변동 표시용).
        /// </summary>
        /// <param name="score">내 경기 최종 점수 (일정의 원정 : 홈 기준).</param>
        /// <param name="random">타 구단 대진·결과용 난수. 테스트는 고정 Seed를 넣는다.</param>
        /// <param name="finalInning">경기가 끝난 이닝 (한국시리즈 뉴스용, 모르면 0).</param>
        /// <param name="walkOff">끝내기로 끝났는지 (한국시리즈 뉴스용).</param>
        public void FinishCurrentGame(GameScore score, IRandomSource random, int finalInning = 0, bool walkOff = false)
        {
            if (InProgress == null) throw new InvalidOperationException("진행 중인 경기가 없다.");
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (Phase == SeasonPhase.PostSeason && Postseason != null && ReferenceEquals(InProgress, Postseason.NextGame))
            {
                FinishPostseasonGame(score, finalInning, walkOff);
                return;
            }

            var game = InProgress;
            _lastDay.Clear();
            _lastDay.Add(new GameRecord { AwayTeam = game.AwayTeam, HomeTeam = game.HomeTeam, Score = score });
            _lastDay.AddRange(SimulateOtherGames(game, random));
            foreach (var row in _standings) row.PreviousRank = row.Rank;
            foreach (var r in _lastDay)
            {
                StandingOf(r.AwayTeam)?.Apply(r);
                StandingOf(r.HomeTeam)?.Apply(r);
            }
            Rerank();

            game.Score = score;
            game.Status = GameStatus.Final;
            InProgress = null;
            NextIndex++;
            BeginPostseasonIfDue(); // 마지막 정규시즌 경기면 한국시리즈 개막 (Phase 32)
        }

        /// <summary>내 경기 두 팀을 뺀 구단을 무작위로 짝지어 경기 결과를 만든다. 홀수면 한 팀은 쉰다.</summary>
        List<GameRecord> SimulateOtherGames(ScheduledGame mine, IRandomSource random)
        {
            var teams = _standings.Select(r => r.Team)
                .Where(t => t != mine.AwayTeam && t != mine.HomeTeam)
                .ToList();
            // Fisher-Yates 섞기
            for (int i = teams.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var tmp = teams[i];
                teams[i] = teams[j];
                teams[j] = tmp;
            }

            var games = new List<GameRecord>();
            for (int i = 0; i + 1 < teams.Count; i += 2)
                games.Add(new GameRecord { AwayTeam = teams[i], HomeTeam = teams[i + 1], Score = RandomScore(random) });
            return games;
        }

        static GameScore RandomScore(IRandomSource random)
        {
            if (random.NextDouble() < OtherGameDrawRate)
            {
                int tie = random.Next(8);
                return new GameScore(tie, tie);
            }
            int loser = random.Next(8);
            int winner = loser + 1 + random.Next(6);
            return random.NextDouble() < 0.5 ? new GameScore(winner, loser) : new GameScore(loser, winner);
        }

        /// <summary>
        /// <see cref="StandingOrder"/>(승률 → 다승 → 무승부 → 잔여 경기)로 정렬한다. 모두 같으면 같은 순위를 주고
        /// 기존 순서를 유지한다 (안정 정렬). 승차는 1위 기준 ((1위 승 - 승) + (패 - 1위 패)) / 2.
        /// </summary>
        public void Rerank()
        {
            var ordered = _standings.OrderBy(r => r, StandingOrder).ToList();
            _standings.Clear();
            _standings.AddRange(ordered);
            if (_standings.Count == 0) return;

            var leader = _standings[0];
            for (int i = 0; i < _standings.Count; i++)
            {
                var row = _standings[i];
                var prev = i > 0 ? _standings[i - 1] : null;
                row.Rank = prev != null && StandingOrder.Compare(prev, row) == 0 ? prev.Rank : i + 1;
                double gb = ((leader.Wins - row.Wins) + (row.Losses - leader.Losses)) / 2.0;
                row.GamesBehind = i == 0 ? "—" : gb.ToString("0.0", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 오프시즌 결산 (Phase 27): 정규시즌을 모두 치렀을 때만. 연도 +1, 순위표 전적·득실점·순위 변동 초기화,
        /// 지난 시즌 일정의 대진을 다음 해 같은 월·일로 옮긴 새 일정, 최근 경기·L10 비움, 컨디션 보통(3) 미만은 보통으로,
        /// 선발 로테이션은 1번부터, 신뢰도 시즌 마감(<see cref="CloseTrustSeason"/> — 신뢰 대출의 50%를 새 시즌 캡 상환으로,
        /// TP는 이월 상한까지). 예산·팬 만족도·팬덤 슬롯·이적 기록(영입·방출 = 선수단과 FA 시장)·라인업은 그대로 넘어간다.
        /// 저장은 하지 않는다 (SeasonManager가 바로 저장한다).
        /// </summary>
        /// <param name="league">
        /// 지금 리그 선수단 장부 (Phase 29). 난수와 함께 넘기면 모든 선수를 한 살 먹이고 에이징 커브·은퇴를 처리한다
        /// (<see cref="OffseasonAging"/>). 은퇴 선수가 사용자 라인업 카드에 있으면 벤치에서 빼고, 타순이면 카드를 버린다(기본 라인업).
        /// 그 뒤 신인 드래프트 (Phase 30, <see cref="RookieDraft"/>): 선수단이 있는 구단마다 신인 3~5명을 퓨처스에 넣는다.
        /// </param>
        /// <returns>끝난 시즌 요약.</returns>
        /// <param name="postseasonRandom">
        /// 한국시리즈가 아직 끝나지 않았을 때 남은 경기를 정할 난수 (Phase 32). 정상 흐름(로비)은 오프시즌에서만 결산하므로 쓰지 않는다.
        /// 정규시즌만 치르고 바로 결산하는 경우(이전 저장 파일·테스트)를 위한 것으로, null이면 연도로 시드를 정한 난수를 쓴다
        /// (에이징 난수와 분리 — 에이징·드래프트 결과가 흔들리지 않게).
        /// </param>
        public SeasonRollover TransitionToNextSeason(LeagueRosterBook league = null, IRandomSource agingRandom = null,
            IRandomSource postseasonRandom = null)
        {
            if (InProgress != null) throw new InvalidOperationException($"경기 중에는 시즌을 넘기지 않는다: {InProgress.Number}");
            if (!IsSeasonFinished) throw new InvalidOperationException($"정규시즌이 끝나지 않았다: {NextIndex} / {_schedule.Count}");
            BeginPostseasonIfDue();
            if (Phase == SeasonPhase.PostSeason) SimulatePostseason(postseasonRandom ?? new SeededRandom(Year * 7919 + 32));

            var me = MyStanding;
            var summary = new SeasonRollover(Year, me?.Rank ?? 0, me?.Wins ?? 0, me?.Losses ?? 0, me?.Draws ?? 0, CapFlex)
            {
                Champion = Postseason?.Champion,
                InChampionship = InPostseason,
                WonChampionship = Postseason?.Champion == MyTeam,
            };

            var schedule = _schedule.Select(g => new ScheduledGame
            {
                Number = g.Number,
                Date = NextYearDate(g.Date, Year),
                AwayTeam = g.AwayTeam,
                HomeTeam = g.HomeTeam,
                Stadium = g.Stadium,
                Status = GameStatus.Scheduled,
            }).ToList();
            _schedule.Clear();
            _schedule.AddRange(schedule);
            NextIndex = 0;
            _lastDay.Clear();

            foreach (var row in _standings)
            {
                row.Wins = row.Losses = row.Draws = 0;
                row.RunsScored = row.RunsAllowed = 0;
                row.PreviousRank = 0;
            }
            Rerank(); // 전원 동률 — 지난 시즌 최종 순서를 유지한 채 공동 1위

            foreach (var id in _conditions.Keys.ToList())
                _conditions[id] = Math.Max(_conditions[id], PlayerCondition.Default);
            _lastStarters.Clear();

            CloseTrustSeason();
            Phase = SeasonPhase.RegularSeason;
            Postseason = null;
            ChampionAward = null;
            Year++;
            AddNews(NewsWriter.SeasonClosed(MyTeam, summary)); // 지난 시즌 뉴스는 지우지 않는다 (보관 개수로만 밀려난다)
            if (league != null && agingRandom != null)
            {
                summary.Aging = OffseasonAging.Process(this, league, agingRandom);
                DropRetiredFromLineup();
                summary.Draft = RookieDraft.Process(this, league, agingRandom);
            }
            return summary;
        }

        void DropRetiredFromLineup()
        {
            if (MyLineup == null) return;
            bool Retired(string id) => ProgressionOf(id)?.Retired == true;
            if (MyLineup.Batting.Any(s => Retired(s.PlayerId))) MyLineup = null;
            else if (MyLineup.Bench.Any(Retired)) MyLineup = new LineupCard(MyLineup.Batting, MyLineup.Bench.Where(id => !Retired(id)));
        }

        /// <summary>저장용 형태로 바꾼다. 경기 중에는 저장할 수 없다 (로비에서만 저장한다).</summary>
        public SeasonSaveData ToSaveData()
        {
            if (InProgress != null) throw new InvalidOperationException($"경기 중에는 저장하지 않는다: {InProgress.Number}");
            return new SeasonSaveData
            {
                Version = SeasonSaveData.CurrentVersion,
                SeasonTitle = SeasonTitle,
                Year = Year,
                MyTeam = MyTeam,
                NextIndex = NextIndex,
                Schedule = _schedule.Select(g => new ScheduledGameData
                {
                    Number = g.Number,
                    Date = g.Date,
                    AwayTeam = g.AwayTeam,
                    HomeTeam = g.HomeTeam,
                    Stadium = g.Stadium,
                    Final = g.Status == GameStatus.Final,
                    AwayScore = g.Score?.Away ?? 0,
                    HomeScore = g.Score?.Home ?? 0,
                }).ToList(),
                Standings = _standings.Select(r => new StandingData
                {
                    Team = r.Team, Wins = r.Wins, Losses = r.Losses, Draws = r.Draws, PreviousRank = r.PreviousRank,
                    RunsScored = r.RunsScored, RunsAllowed = r.RunsAllowed,
                }).ToList(),
                Lineup = MyLineup == null ? new LineupSaveData() : new LineupSaveData
                {
                    Batting = MyLineup.Batting.Select(s => s.PlayerId).ToList(),
                    Positions = MyLineup.Batting.Select(s => s.Position).ToList(),
                    Bench = MyLineup.Bench.ToList(),
                },
                MoneyUnitVersion = SeasonSaveData.CurrentMoneyUnitVersion,
                HasFinance = true,
                Budget = Budget,
                FanApproval = FanApproval,
                HasFandom = true,
                FandomSlots = (int)UnlockedSlots,
                Signings = _signings.Select(s => new SigningData
                {
                    PlayerId = s.PlayerId, FromTeam = s.FromTeam, ToTeam = s.ToTeam, Salary = s.Salary,
                }).ToList(),
                HasTrust = true,
                TrustPoints = TrustPoints,
                TrustEarnedThisSeason = TrustEarnedThisSeason,
                CapRepay = CapRepay,
                CapFlex = CapFlex,
                Conditions = _conditions.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new ConditionData { Id = kv.Key, Value = kv.Value }).ToList(),
                LastStarters = _lastStarters.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new LastStarterData { Team = kv.Key, PlayerId = kv.Value }).ToList(),
                Progression = _progression.Values.OrderBy(p => p.PlayerId, StringComparer.Ordinal).Select(p => new ProgressionData
                {
                    Id = p.PlayerId, AgeDelta = p.AgeDelta, Form = p.Form, Retired = p.Retired, RetiredYear = p.RetiredYear,
                }).ToList(),
                Rookies = _rookies.ToList(),
                Phase = (int)Phase,
                Postseason = Postseason == null ? new PostseasonData() : new PostseasonData
                {
                    TopSeed = Postseason.TopSeed,
                    SecondSeed = Postseason.SecondSeed,
                    NextIndex = Postseason.NextIndex,
                    Games = Postseason.Games.Select(g => new ScheduledGameData
                    {
                        Number = g.Number, Date = g.Date, AwayTeam = g.AwayTeam, HomeTeam = g.HomeTeam, Stadium = g.Stadium,
                        Final = g.Status == GameStatus.Final, AwayScore = g.Score?.Away ?? 0, HomeScore = g.Score?.Home ?? 0,
                    }).ToList(),
                },
                News = _news.Select(n => new NewsData
                {
                    Kind = (int)n.Kind, Tag = n.Tag, Team = n.Team, Headline = n.Headline, Year = n.Year, Date = n.Date,
                }).ToList(),
            };
        }

        /// <summary>
        /// 저장 형태에서 시즌을 되살린다. 내용이 앞뒤가 맞지 않으면(버전·빈 일정·모르는 팀·진행 상황 불일치 등)
        /// <see cref="FormatException"/>을 던진다. 순위·승차는 성적으로 다시 계산한다.
        /// </summary>
        public static SeasonState FromSaveData(SeasonSaveData data)
        {
            if (data == null) throw new FormatException("저장 데이터가 없다.");
            if (data.Version != SeasonSaveData.CurrentVersion)
                throw new FormatException($"저장 형식 버전 불일치: {data.Version} (현재 {SeasonSaveData.CurrentVersion})");
            if (string.IsNullOrEmpty(data.MyTeam)) throw new FormatException("내 구단이 비어 있다.");
            if (data.Schedule == null || data.Schedule.Count == 0) throw new FormatException("일정이 비어 있다.");
            if (data.Standings == null || data.Standings.Count == 0) throw new FormatException("순위표가 비어 있다.");
            if (data.NextIndex < 0 || data.NextIndex > data.Schedule.Count)
                throw new FormatException($"진행 위치가 일정 범위를 벗어났다: {data.NextIndex} / {data.Schedule.Count}");

            var teams = new HashSet<string>();
            var standings = new List<StandingRow>();
            foreach (var s in data.Standings)
            {
                if (s == null || string.IsNullOrEmpty(s.Team) || !teams.Add(s.Team))
                    throw new FormatException($"순위표 팀이 비었거나 중복: {s?.Team}");
                if (s.Wins < 0 || s.Losses < 0 || s.Draws < 0) throw new FormatException($"음수 성적: {s.Team}");
                if (s.PreviousRank < 0 || s.PreviousRank > data.Standings.Count)
                    throw new FormatException($"직전 순위가 범위를 벗어났다: {s.Team} {s.PreviousRank}");
                if (s.RunsScored < 0 || s.RunsAllowed < 0) throw new FormatException($"음수 득실점: {s.Team}");
                standings.Add(new StandingRow
                {
                    Team = s.Team, Wins = s.Wins, Losses = s.Losses, Draws = s.Draws, PreviousRank = s.PreviousRank,
                    RunsScored = s.RunsScored, RunsAllowed = s.RunsAllowed,
                });
            }
            if (!teams.Contains(data.MyTeam)) throw new FormatException($"내 구단이 순위표에 없다: {data.MyTeam}");

            var schedule = new List<ScheduledGame>();
            for (int i = 0; i < data.Schedule.Count; i++)
            {
                var g = data.Schedule[i] ?? throw new FormatException($"일정 {i + 1}번째 항목이 비어 있다.");
                if (!teams.Contains(g.AwayTeam) || !teams.Contains(g.HomeTeam) || g.AwayTeam == g.HomeTeam)
                    throw new FormatException($"경기 {g.Number}: 팀이 잘못됐다 ({g.AwayTeam} vs {g.HomeTeam})");
                if (g.AwayTeam != data.MyTeam && g.HomeTeam != data.MyTeam)
                    throw new FormatException($"경기 {g.Number}: 내 구단 경기가 아니다.");
                // 치른 경기는 앞에서부터 이어진다: 진행 위치 앞은 모두 종료, 뒤는 모두 예정.
                if (g.Final != (i < data.NextIndex))
                    throw new FormatException($"경기 {g.Number}: 종료 여부가 진행 위치({data.NextIndex})와 맞지 않는다.");
                if (g.Final && (g.AwayScore < 0 || g.HomeScore < 0)) throw new FormatException($"경기 {g.Number}: 음수 점수");
                schedule.Add(new ScheduledGame
                {
                    Number = g.Number,
                    Date = g.Date,
                    AwayTeam = g.AwayTeam,
                    HomeTeam = g.HomeTeam,
                    Stadium = g.Stadium,
                    Status = g.Final ? GameStatus.Final : GameStatus.Scheduled,
                    Score = g.Final ? new GameScore(g.AwayScore, g.HomeScore) : (GameScore?)null,
                });
            }

            var season = new SeasonState(data.SeasonTitle, data.MyTeam, schedule, standings)
            {
                NextIndex = data.NextIndex,
                MyLineup = LineupFrom(data.Lineup),
            };
            // 연도가 없는 이전 저장 파일(Phase 27 이전)은 생성자가 제목 앞 네 자리에서 읽은 연도(없으면 2026)를 쓴다.
            if (data.Year > 0) season.Year = data.Year;
            // 재정 필드가 없는 이전 저장 파일(Phase 20 이전)은 기본값(₩5,000,000 · 50%)으로 시작한다.
            // 범위를 벗어난 값은 손상으로 보지 않고 범위 안으로 맞춘다.
            // Phase 26 이전 저장 파일(화폐 단위 버전 0)은 금액이 1/1,000 단위라 ×1,000으로 바꿔 읽는다.
            long money = data.MoneyUnitVersion >= SeasonSaveData.CurrentMoneyUnitVersion ? 1 : FranchiseFinance.LegacyMoneyScale;
            if (data.HasFinance) season.SetFinance(Scaled(data.Budget, money), data.FanApproval);
            // 팬덤 슬롯이 없는 이전 저장 파일(Phase 22 이전)은 해금 없음에서 시작한다. 모르는 비트는 버린다.
            if (data.HasFandom) season.SetUnlockedSlots((FandomSlot)data.FandomSlots);
            // 신뢰도 필드가 없는 이전 저장 파일은 TP 0·대출 없음에서 시작한다. 범위 밖 값은 범위 안으로 맞춘다.
            if (data.HasTrust) season.SetTrust(data.TrustPoints, data.TrustEarnedThisSeason, Scaled(data.CapRepay, money), Scaled(data.CapFlex, money));
            // 영입 기록이 없는 이전 저장 파일은 빈 목록. 빈 칸이 있는 항목은 건너뛴다 (선수 DB와 맞지 않는 기록은 장부가 건너뛴다).
            if (data.Signings != null)
                foreach (var s in data.Signings)
                    if (s != null && !string.IsNullOrEmpty(s.PlayerId) && !string.IsNullOrEmpty(s.FromTeam) && !string.IsNullOrEmpty(s.ToTeam))
                        season._signings.Add(new SigningRecord(s.PlayerId, s.FromTeam, s.ToTeam, Scaled(s.Salary, money)));
            // 컨디션 목록이 없는 이전 저장 파일(Phase 21 이전)은 전원 보통(3)에서 시작한다 (기록 없음 = 보통).
            // ID가 빈 항목은 건너뛰고, 범위 밖 값은 1~5로 맞춘다.
            if (data.Conditions != null)
                foreach (var c in data.Conditions)
                    if (c != null && !string.IsNullOrEmpty(c.Id)) season.SetCondition(c.Id, c.Value);
            // 선발 순번이 없는 이전 저장 파일은 로테이션 1번부터.
            if (data.LastStarters != null)
                foreach (var l in data.LastStarters)
                    if (l != null) season.SetLastStarter(l.Team, l.PlayerId);
            // 선수 변동치가 없는 이전 저장 파일(Phase 29 이전)은 모두 원본 그대로. ID가 빈 항목은 건너뛰고 범위 밖 값은 맞춘다.
            if (data.Progression != null)
                foreach (var p in data.Progression)
                    if (p != null && !string.IsNullOrEmpty(p.Id))
                        season.SetProgression(new PlayerProgression(p.Id, p.AgeDelta, p.Form, p.Retired, p.RetiredYear));
            // 시즌 단계가 없는 이전 저장 파일(Phase 32 이전)은 정규시즌. 정규시즌을 다 치른 파일은 SeasonManager가 불러온 뒤
            // 한국시리즈를 연다 (BeginPostseasonIfDue). 단계와 맞지 않는 시리즈 기록(팀이 순위표에 없음 등)은 버리고 정규시즌 끝에서 다시 연다.
            season.RestorePhase(data);
            // 신인이 없는 이전 저장 파일(Phase 30 이전)은 빈 목록. ID·구단이 빈 항목과 중복 ID는 건너뛴다.
            if (data.Rookies != null)
                foreach (var r in data.Rookies)
                    season.AddRookie(r);
            // 뉴스가 없는 이전 저장 파일(Phase 28 이전)은 빈 피드. 본문이 빈 항목은 건너뛰고, 모르는 종류는 General,
            // 보관 개수를 넘는 파일은 최근 것만 남는다 (AddNews).
            if (data.News != null)
                foreach (var n in data.News)
                    if (n != null && !string.IsNullOrEmpty(n.Headline))
                        season.AddNews(new NewsItem(Enum.IsDefined(typeof(NewsKind), n.Kind) ? (NewsKind)n.Kind : NewsKind.General,
                            n.Tag, n.Team, n.Headline, n.Year, n.Date));
            return season;
        }

        void RestorePhase(SeasonSaveData data)
        {
            var phase = Enum.IsDefined(typeof(SeasonPhase), data.Phase) ? (SeasonPhase)data.Phase : SeasonPhase.RegularSeason;
            if (phase == SeasonPhase.RegularSeason || !IsRegularSeasonFinished) return;
            var ps = data.Postseason;
            bool valid = ps != null && !string.IsNullOrEmpty(ps.TopSeed) && !string.IsNullOrEmpty(ps.SecondSeed) && ps.TopSeed != ps.SecondSeed
                && StandingOf(ps.TopSeed) != null && StandingOf(ps.SecondSeed) != null && ps.Games != null && ps.Games.Count > 0
                && ps.Games.All(g => g != null && (g.AwayTeam == ps.TopSeed || g.AwayTeam == ps.SecondSeed)
                                     && (g.HomeTeam == ps.TopSeed || g.HomeTeam == ps.SecondSeed) && g.AwayTeam != g.HomeTeam);
            if (!valid) return; // 정규시즌 끝 상태로 두고 다시 연다
            var games = ps.Games.Select(g => new ScheduledGame
            {
                Number = g.Number, Date = g.Date, AwayTeam = g.AwayTeam, HomeTeam = g.HomeTeam, Stadium = g.Stadium,
                Status = g.Final ? GameStatus.Final : GameStatus.Scheduled,
                Score = g.Final ? new GameScore(Math.Max(0, g.AwayScore), Math.Max(0, g.HomeScore)) : (GameScore?)null,
            }).ToList();
            int next = games.TakeWhile(g => g.Status == GameStatus.Final).Count();
            Postseason = new PostseasonSeries(Year, ps.TopSeed, ps.SecondSeed, games, next);
            // 저장된 단계보다 시리즈 기록을 믿는다: 우승 팀이 있으면 오프시즌, 없으면 포스트시즌
            Phase = Postseason.IsOver ? SeasonPhase.OffSeason : SeasonPhase.PostSeason;
        }

        /// <summary>이전 단위 금액 변환. 넘치면 long 끝값으로 맞춘다.</summary>
        static long Scaled(long amount, long scale) =>
            amount > 0 && amount > long.MaxValue / scale ? long.MaxValue
            : amount < 0 && amount < long.MinValue / scale ? long.MinValue
            : amount * scale;

        /// <summary>저장된 라인업. 비어 있으면 null(기본 라인업). 모양이 깨졌으면 FormatException.</summary>
        static LineupCard LineupFrom(LineupSaveData data)
        {
            if (data == null || data.IsEmpty) return null;
            var batting = data.Batting ?? new List<string>();
            var positions = data.Positions ?? new List<string>();
            if (positions.Count != batting.Count)
                throw new FormatException($"라인업 수비 위치 {positions.Count}개가 타순 {batting.Count}명과 다르다.");
            try
            {
                return new LineupCard(batting.Select((id, i) => new LineupSlot(id, positions[i])), data.Bench ?? new List<string>());
            }
            catch (ArgumentException e)
            {
                throw new FormatException($"저장된 라인업이 잘못됐다: {e.Message}", e);
            }
        }

        /// <summary>KBO 10개 구단 홈구장 (Resources/Database/Teams.csv와 같다 — 테스트가 맞는지 확인한다).</summary>
        public static readonly IReadOnlyDictionary<string, string> KboStadiums = new Dictionary<string, string>
        {
            ["KIA 타이거즈"] = "광주 챔피언스필드",
            ["삼성 라이온즈"] = "대구 라이온즈파크",
            ["LG 트윈스"] = "잠실야구장",
            ["두산 베어스"] = "잠실야구장",
            ["KT 위즈"] = "수원 위즈파크",
            ["SSG 랜더스"] = "인천 랜더스필드",
            ["롯데 자이언츠"] = "사직야구장",
            ["한화 이글스"] = "대전 이글스파크",
            ["NC 다이노스"] = "창원NC파크",
            ["키움 히어로즈"] = "고척스카이돔",
        };

        /// <summary>
        /// KBO 10개 구단, 내 구단 삼성 라이온즈(실제 선수 데이터가 있는 구단), 10경기 일정의 새 시즌 (Phase 26.5).
        /// 순위표 성적은 시즌 중반 시작용 게임 값이다 (실제 기록 아님).
        /// </summary>
        public static SeasonState CreateDummy()
        {
            const string my = "삼성 라이온즈";
            var opponents = new[]
            {
                "LG 트윈스", "SSG 랜더스", "KIA 타이거즈", "두산 베어스", "한화 이글스",
                "KT 위즈", "롯데 자이언츠", "NC 다이노스", "키움 히어로즈", "LG 트윈스",
            };
            var dates = new[]
            {
                "5.15 (금)", "5.16 (토)", "5.17 (일)", "5.19 (화)", "5.20 (수)",
                "5.21 (목)", "5.22 (금)", "5.23 (토)", "5.24 (일)", "5.26 (화)",
            };

            var schedule = new List<ScheduledGame>();
            for (int i = 0; i < opponents.Length; i++)
            {
                bool home = i % 3 != 1; // 첫 경기는 LG(원정) vs 삼성(홈)
                string away = home ? opponents[i] : my;
                string homeTeam = home ? my : opponents[i];
                schedule.Add(new ScheduledGame
                {
                    Number = i + 1,
                    Date = dates[i],
                    AwayTeam = away,
                    HomeTeam = homeTeam,
                    Stadium = KboStadiums[homeTeam],
                });
            }

            var standings = new List<StandingRow>
            {
                new StandingRow { Team = "KIA 타이거즈", Wins = 26, Losses = 15, Draws = 1 },
                new StandingRow { Team = "LG 트윈스", Wins = 24, Losses = 16, Draws = 2 },
                new StandingRow { Team = "삼성 라이온즈", Wins = 23, Losses = 18, Draws = 0 },
                new StandingRow { Team = "두산 베어스", Wins = 22, Losses = 19, Draws = 1 },
                new StandingRow { Team = "KT 위즈", Wins = 21, Losses = 20, Draws = 0 },
                new StandingRow { Team = "SSG 랜더스", Wins = 20, Losses = 21, Draws = 1 },
                new StandingRow { Team = "롯데 자이언츠", Wins = 19, Losses = 22, Draws = 0 },
                new StandingRow { Team = "한화 이글스", Wins = 18, Losses = 23, Draws = 1 },
                new StandingRow { Team = "NC 다이노스", Wins = 17, Losses = 24, Draws = 0 },
                new StandingRow { Team = "키움 히어로즈", Wins = 15, Losses = 27, Draws = 0 },
            };
            // 개막 후 득실점: 경기당 4.6점 안팎에 승패 마진만큼 벌린다 (리그 득점 합 = 실점 합).
            foreach (var row in standings)
            {
                int margin = (int)Math.Round((row.Wins - row.Losses) * 2.5, MidpointRounding.AwayFromZero);
                int baseline = (int)Math.Round(row.GamesPlayed * 4.6);
                row.RunsScored = baseline + margin;
                row.RunsAllowed = baseline - margin;
            }
            return new SeasonState($"{DefaultYear} 시즌", my, schedule, standings);
        }
    }

    /// <summary>오프시즌 결산으로 끝난 시즌 요약 (Phase 27, <see cref="SeasonState.TransitionToNextSeason"/>).</summary>
    public sealed class SeasonRollover
    {
        public SeasonRollover(int year, int rank, int wins, int losses, int draws, long capFlex)
        {
            Year = year;
            Rank = rank;
            Wins = wins;
            Losses = losses;
            Draws = draws;
            CapFlex = capFlex;
        }

        /// <summary>끝난 시즌 연도.</summary>
        public int Year { get; }
        /// <summary>내 구단 최종 순위 (순위표에 없으면 0).</summary>
        public int Rank { get; }
        public int Wins { get; }
        public int Losses { get; }
        public int Draws { get; }
        /// <summary>끝난 시즌에 받은 신뢰 대출 — 50%가 새 시즌 캡에서 빠진다.</summary>
        public long CapFlex { get; }
        /// <summary>오프시즌 에이징 결과 (Phase 29). 장부 없이 결산했으면 null.</summary>
        public AgingReport Aging { get; internal set; }
        /// <summary>신인 드래프트 결과 (Phase 30). 장부 없이 결산했으면 null.</summary>
        public DraftReport Draft { get; internal set; }
        /// <summary>한국시리즈 우승 팀 (Phase 32). 시리즈가 없었으면 null.</summary>
        public string Champion { get; internal set; }
        /// <summary>내 구단이 한국시리즈에 나갔는지.</summary>
        public bool InChampionship { get; internal set; }
        /// <summary>내 구단이 한국시리즈에서 우승했는지.</summary>
        public bool WonChampionship { get; internal set; }

        /// <summary>"2026 시즌 최종 3위 · 78승 64패 2무 · 은퇴 2명 · 신인 4명" (무승부·은퇴·신인이 없으면 생략, 신인은 내 구단 지명 수).</summary>
        public string Text
        {
            get
            {
                string record = Draws == 0 ? $"{Wins}승 {Losses}패" : $"{Wins}승 {Losses}패 {Draws}무";
                string text = Rank > 0 ? $"{Year} 시즌 최종 {Rank}위 · {record}" : $"{Year} 시즌 · {record}";
                if (InChampionship) text += WonChampionship ? " · 한국시리즈 우승" : " · 한국시리즈 준우승";
                if (Aging != null && Aging.Retired.Count > 0) text += $" · 은퇴 {Aging.Retired.Count}명";
                int mine = Draft?.MyPicks.Count ?? 0;
                return mine > 0 ? $"{text} · 신인 {mine}명" : text;
            }
        }
    }
}
