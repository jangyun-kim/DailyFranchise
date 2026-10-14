using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;

namespace DailyFranchise.Sim
{
    /// <summary>확률 보정에 쓰는 타석 상황 (투구 직후 기준: 아웃 수, 루상 주자).</summary>
    public readonly struct MatchupSituation
    {
        public readonly int Outs;
        public readonly BaseState Runners;

        public MatchupSituation(int outs, BaseState runners)
        {
            if (outs < 0 || outs > 2) throw new ArgumentOutOfRangeException(nameof(outs), outs, "타석이 진행 중인 아웃 수는 0~2");
            Outs = outs;
            Runners = runners;
        }

        /// <summary>2루 또는 3루에 주자가 있다.</summary>
        public bool RunnersInScoringPosition => Runners.Second != null || Runners.Third != null;

        /// <summary>이 투구를 마친 뒤의 상황.</summary>
        public static MatchupSituation After(PitchEvent e) => new MatchupSituation(e.OutsAfter, e.RunnersAfter);

        public override string ToString() => $"{Outs}사 {Runners}";
    }

    /// <summary>계산에 쓴 타자·투수와 상황.</summary>
    public sealed class MatchupContext
    {
        /// <summary>데이터가 없으면 리그 평균 가상 선수.</summary>
        public PlayerData Batter;
        public PlayerData Pitcher;
        public MatchupSituation Situation;
    }

    /// <summary>
    /// 상황 보정. 안타(출루) 확률을 받아 보정한 값을 돌려준다. 계산기가 결과를 [0, 1]로 다시 자른다.
    /// 새 보정(예: 2사 보정, 좌우 놀이)은 이 인터페이스로 계산기에 끼운다.
    /// </summary>
    public interface IMatchupModifier
    {
        double Adjust(double probability, MatchupContext context);
    }

    /// <summary>득점권(2·3루 주자)에서 안타 확률을 상대적으로 올린다 (기본 +5%).</summary>
    public sealed class ScoringPositionModifier : IMatchupModifier
    {
        public readonly double RelativeBonus;

        public ScoringPositionModifier(double relativeBonus = 0.05)
        {
            if (double.IsNaN(relativeBonus) || relativeBonus <= -1) throw new ArgumentOutOfRangeException(nameof(relativeBonus));
            RelativeBonus = relativeBonus;
        }

        public double Adjust(double probability, MatchupContext context) =>
            context.Situation.RunnersInScoringPosition ? probability * (1 + RelativeBonus) : probability;
    }

    /// <summary>
    /// 타자·투수 시즌 기록으로 타석 안타(출루) 확률과 작전 성공률을 계산한다. 순수 C#이며 판정(난수)은 하지 않는다.
    /// 공식은 이 클래스 안에만 있어 나중에 세이버메트릭스 공식으로 바꿀 수 있다.
    ///
    /// 1) 타자 안타력  B = (2 × 타율 + 출루율) / 3
    /// 2) 투수 허용률  P = (피안타율 + 리그 타율 × 평균자책 / 리그 평균자책) / 2
    /// 3) 타석 확률    H = 상황 보정( (B + P) / 2 ), [0, 1]
    /// 4) 작전 성공률: 리그 평균 대결(상황 보정 없음)의 확률 H₀ 대비 비율 r = H / H₀ 로 장면 기본 성공률을 늘리고 줄인다.
    ///    강공 = 기본 × r,  스퀴즈(컨택 위주라 덜 민감) = 기본 × (1 + 0.5 × (r − 1)),  [5%, 95%]로 자른다.
    /// 타자·투수 둘 다 데이터가 없으면 계산하지 않고 장면 기본 성공률을 그대로 쓴다. 한쪽만 없으면 그쪽은 리그 평균.
    /// </summary>
    public sealed class MatchupCalculator
    {
        public const double LeagueAverage = 0.270;
        public const double LeagueOnBase = 0.345;
        public const double LeagueEra = 4.30;
        /// <summary>강공 성공률이 타석 확률 비율에 반응하는 정도.</summary>
        public const double SwingSensitivity = 1.0;
        /// <summary>스퀴즈 성공률이 타석 확률 비율에 반응하는 정도.</summary>
        public const double SqueezeSensitivity = 0.5;
        /// <summary>확률 작전은 늘 운이 섞이도록 성공률을 이 범위로 자른다.</summary>
        public const double MinTacticRate = 0.05;
        public const double MaxTacticRate = 0.95;
        /// <summary>계산에 쓰는 평균자책 상한 (기록이 비정상이어도 확률이 무너지지 않게).</summary>
        const double MaxEra = 27.0;

        readonly IPlayerLookup _players;
        readonly List<IMatchupModifier> _modifiers;

        /// <param name="players">선수 ID → 데이터. null이면 모든 선수가 데이터 없음.</param>
        /// <param name="modifiers">상황 보정 목록. null이면 <see cref="DefaultModifiers"/>, 빈 목록이면 보정 없음.</param>
        public MatchupCalculator(IPlayerLookup players, IEnumerable<IMatchupModifier> modifiers = null)
        {
            _players = players;
            _modifiers = (modifiers ?? DefaultModifiers()).ToList();
            if (_modifiers.Any(m => m == null)) throw new ArgumentException("보정에 null이 있다.", nameof(modifiers));
        }

        public static IEnumerable<IMatchupModifier> DefaultModifiers() => new IMatchupModifier[] { new ScoringPositionModifier() };

        public IReadOnlyList<IMatchupModifier> Modifiers => _modifiers;

        /// <summary>리그 평균 타자 (타율·출루율 리그 평균).</summary>
        public static PlayerData AverageBatter => new PlayerData
        {
            Id = "league-batter", Name = "리그 평균 타자", Role = RosterRole.Lineup,
            BattingAverage = LeagueAverage, OnBasePercentage = LeagueOnBase,
        };

        /// <summary>리그 평균 투수 (피안타율 = 리그 타율, 평균자책 리그 평균).</summary>
        public static PlayerData AveragePitcher => new PlayerData
        {
            Id = "league-pitcher", Name = "리그 평균 투수", Role = RosterRole.Bullpen,
            Era = LeagueEra, OpponentAverage = LeagueAverage,
        };

        /// <summary>리그 평균 대결, 상황 보정 없는 타석 확률 H₀.</summary>
        public static double NeutralHitProbability => Combine(AverageBatter, AveragePitcher);

        /// <summary>타자 안타력 B = (2 × 타율 + 출루율) / 3. 출루율이 없으면 타율 + 리그 (출루율 − 타율).</summary>
        public static double BatterHitRate(PlayerData batter)
        {
            double avg = Rate(batter.BattingAverage, LeagueAverage);
            double obp = batter.OnBasePercentage > 0
                ? Rate(batter.OnBasePercentage, LeagueOnBase)
                : Clamp01(avg + (LeagueOnBase - LeagueAverage));
            return Clamp01((2 * avg + obp) / 3);
        }

        /// <summary>투수 허용률 P = (피안타율 + 리그 타율 × 평균자책 / 리그 평균자책) / 2. 피안타율이 없으면 평균자책 환산값만.</summary>
        public static double PitcherAllowedRate(PlayerData pitcher)
        {
            double era = double.IsNaN(pitcher.Era) || pitcher.Era < 0 ? LeagueEra : Math.Min(pitcher.Era, MaxEra);
            double fromEra = Clamp01(LeagueAverage * era / LeagueEra);
            double baa = pitcher.OpponentAverage > 0 ? Rate(pitcher.OpponentAverage, LeagueAverage) : fromEra;
            return Clamp01((baa + fromEra) / 2);
        }

        /// <summary>상황 보정 전 타석 확률 (B + P) / 2.</summary>
        public static double Combine(PlayerData batter, PlayerData pitcher) =>
            Clamp01((BatterHitRate(batter) + PitcherAllowedRate(pitcher)) / 2);

        /// <summary>계산에 쓸 타자·투수. 데이터가 없는 쪽은 리그 평균. 둘 다 없으면 null.</summary>
        public MatchupContext ContextFor(string batterId, string pitcherId, MatchupSituation situation)
        {
            var batter = _players?.FindPlayer(batterId);
            var pitcher = _players?.FindPlayer(pitcherId);
            if (batter == null && pitcher == null) return null;
            return new MatchupContext
            {
                Batter = batter ?? AverageBatter,
                Pitcher = pitcher ?? AveragePitcher,
                Situation = situation,
            };
        }

        /// <summary>타석 안타(출루) 확률 H. 둘 다 데이터가 없으면 리그 평균 대결로 계산한다.</summary>
        public double HitProbability(string batterId, string pitcherId, MatchupSituation situation) =>
            HitProbability(ContextFor(batterId, pitcherId, situation)
                           ?? new MatchupContext { Batter = AverageBatter, Pitcher = AveragePitcher, Situation = situation });

        public double HitProbability(MatchupContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            double p = Combine(context.Batter, context.Pitcher);
            foreach (var m in _modifiers) p = Clamp01(m.Adjust(p, context));
            return p;
        }

        /// <summary>
        /// 작전 성공률. 확률 판정이 없는 작전(기본 진행)은 1. 타자·투수 둘 다 데이터가 없으면 <paramref name="baseRate"/> 그대로.
        /// </summary>
        /// <param name="baseRate">장면이 정한 기본 성공률 (리그 평균 대결 기준).</param>
        public double TacticSuccessRate(TacticKind tactic, double baseRate, string batterId, string pitcherId, MatchupSituation situation)
        {
            if (double.IsNaN(baseRate) || baseRate < 0 || baseRate > 1)
                throw new ArgumentOutOfRangeException(nameof(baseRate), baseRate, "기본 성공률은 0~1");
            if (tactic == TacticKind.Default) return 1;

            var context = ContextFor(batterId, pitcherId, situation);
            if (context == null) return baseRate;

            double ratio = HitProbability(context) / NeutralHitProbability;
            double sensitivity = tactic == TacticKind.SqueezeBunt ? SqueezeSensitivity : SwingSensitivity;
            double rate = baseRate * (1 + sensitivity * (ratio - 1));
            return double.IsNaN(rate) ? baseRate : Math.Max(MinTacticRate, Math.Min(MaxTacticRate, rate));
        }

        /// <summary>
        /// 승부처 작전 성공률. 분기점 투구(기본 구간 마지막)의 타자·투수가 이어서 승부하고, 그 투구 직후 상황으로 보정한다.
        /// 확률 판정이 없는 작전이면 1.
        /// </summary>
        public double TacticSuccessRate(BranchPoint branch, IReadOnlyList<PitchEvent> baseEvents, TacticKind tactic)
        {
            var option = branch[tactic];
            if (!option.IsGamble) return 1;
            var decision = DecisionPitch(branch, baseEvents);
            return TacticSuccessRate(tactic, option.SuccessRate, decision.BatterId, decision.PitcherId, MatchupSituation.After(decision));
        }

        /// <summary>승부처 대결의 타자·투수 맥락. 둘 다 데이터가 없으면 null (장면 기본 확률).</summary>
        public MatchupContext DecisionContext(BranchPoint branch, IReadOnlyList<PitchEvent> baseEvents)
        {
            var decision = DecisionPitch(branch, baseEvents);
            return ContextFor(decision.BatterId, decision.PitcherId, MatchupSituation.After(decision));
        }

        static PitchEvent DecisionPitch(BranchPoint branch, IReadOnlyList<PitchEvent> baseEvents)
        {
            if (branch == null) throw new ArgumentNullException(nameof(branch));
            if (baseEvents == null || baseEvents.Count <= branch.AfterIndex)
                throw new ArgumentException($"분기점 {branch.AfterIndex}번 투구가 기본 구간에 없다.", nameof(baseEvents));
            return baseEvents[branch.AfterIndex];
        }

        /// <summary>비율 기록(타율 등): 숫자가 아니거나 음수면 기본값, 1 초과는 1.</summary>
        static double Rate(double value, double fallback) =>
            double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? fallback : Math.Min(value, 1);

        static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v));
    }
}
