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

        public SeasonState(string seasonTitle, string myTeam, List<ScheduledGame> schedule, List<StandingRow> standings)
        {
            if (schedule == null || schedule.Count == 0) throw new ArgumentException("일정이 비어 있다.", nameof(schedule));
            SeasonTitle = seasonTitle;
            MyTeam = myTeam;
            _schedule = schedule;
            _standings = standings;
            Rerank();
        }

        public string SeasonTitle { get; }
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

        public ScheduledGame NextGame => NextIndex < _schedule.Count ? _schedule[NextIndex] : null;
        public bool IsSeasonOver => NextIndex >= _schedule.Count && InProgress == null;
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
        public void FinishCurrentGame(GameScore score, IRandomSource random)
        {
            if (InProgress == null) throw new InvalidOperationException("진행 중인 경기가 없다.");
            if (random == null) throw new ArgumentNullException(nameof(random));

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

        /// <summary>저장용 형태로 바꾼다. 경기 중에는 저장할 수 없다 (로비에서만 저장한다).</summary>
        public SeasonSaveData ToSaveData()
        {
            if (InProgress != null) throw new InvalidOperationException($"경기 중에는 저장하지 않는다: {InProgress.Number}");
            return new SeasonSaveData
            {
                Version = SeasonSaveData.CurrentVersion,
                SeasonTitle = SeasonTitle,
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
            return season;
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

        /// <summary>가상 구단 10개, 내 구단 부산 웨이브스, 10경기 일정의 더미 시즌.</summary>
        public static SeasonState CreateDummy()
        {
            const string my = "부산 웨이브스";
            var opponents = new[]
            {
                "서울 블루윙스", "인천 하버스", "대구 썬더스", "광주 레드폭스", "대전 코메츠",
                "수원 나이츠", "창원 마리너스", "서울 그리폰스", "고양 스타즈", "서울 블루윙스",
            };
            var dates = new[]
            {
                "5.15 (금)", "5.16 (토)", "5.17 (일)", "5.19 (화)", "5.20 (수)",
                "5.21 (목)", "5.22 (금)", "5.23 (토)", "5.24 (일)", "5.26 (화)",
            };

            var schedule = new List<ScheduledGame>();
            for (int i = 0; i < opponents.Length; i++)
            {
                bool home = i % 3 != 1; // 첫 경기는 경기 화면 더미와 같은 서울(원정) vs 부산(홈)
                string away = home ? opponents[i] : my;
                string homeTeam = home ? my : opponents[i];
                schedule.Add(new ScheduledGame
                {
                    Number = i + 1,
                    Date = dates[i],
                    AwayTeam = away,
                    HomeTeam = homeTeam,
                    Stadium = homeTeam.Split(' ')[0] + " 구장",
                });
            }

            var standings = new List<StandingRow>
            {
                new StandingRow { Team = "대구 썬더스", Wins = 26, Losses = 15, Draws = 1 },
                new StandingRow { Team = "서울 블루윙스", Wins = 24, Losses = 16, Draws = 2 },
                new StandingRow { Team = "부산 웨이브스", Wins = 23, Losses = 18, Draws = 0 },
                new StandingRow { Team = "광주 레드폭스", Wins = 22, Losses = 19, Draws = 1 },
                new StandingRow { Team = "수원 나이츠", Wins = 21, Losses = 20, Draws = 0 },
                new StandingRow { Team = "인천 하버스", Wins = 20, Losses = 21, Draws = 1 },
                new StandingRow { Team = "창원 마리너스", Wins = 19, Losses = 22, Draws = 0 },
                new StandingRow { Team = "대전 코메츠", Wins = 18, Losses = 23, Draws = 1 },
                new StandingRow { Team = "서울 그리폰스", Wins = 17, Losses = 24, Draws = 0 },
                new StandingRow { Team = "고양 스타즈", Wins = 15, Losses = 27, Draws = 0 },
            };
            // 개막 후 득실점: 경기당 4.6점 안팎에 승패 마진만큼 벌린다 (리그 득점 합 = 실점 합).
            foreach (var row in standings)
            {
                int margin = (int)Math.Round((row.Wins - row.Losses) * 2.5, MidpointRounding.AwayFromZero);
                int baseline = (int)Math.Round(row.GamesPlayed * 4.6);
                row.RunsScored = baseline + margin;
                row.RunsAllowed = baseline - margin;
            }
            return new SeasonState("2026 시즌 · 5월", my, schedule, standings);
        }
    }
}
