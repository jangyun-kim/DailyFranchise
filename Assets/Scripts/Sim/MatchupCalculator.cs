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
        /// <summary>투수의 지금 체력. 모르면 null (체력 보정 없음).</summary>
        public PitcherState PitcherState;
        /// <summary>타자·투수의 오늘 컨디션 (1~5). 0이면 모름 (컨디션 보정 없음).</summary>
        public int BatterCondition;
        public int PitcherCondition;
    }

    /// <summary>컨디션 보정이 보는 쪽.</summary>
    public enum ConditionSide
    {
        Batter,
        Pitcher,
    }

    /// <summary>
    /// 컨디션 보정 (Phase 22). 한쪽 선수의 오늘 컨디션(<see cref="PlayerCondition"/>)으로 안타(출루) 확률을 상대 보정한다.
    /// - 타자: 최상(5) +4%, 위험(1) −8%.
    /// - 투수: 최상(5)이면 타자 안타 확률 −4%, 위험(1)이면 +8% (투수가 강하면 타자에게 불리).
    /// 2~4단계와 모르는 컨디션(0)은 보정 없음. 툴팁 예: "타자 컨디션 최상 +4%", "투수 컨디션 위험 +8%".
    /// </summary>
    public sealed class ConditionModifier : IDescribedMatchupModifier
    {
        public const double ExcellentBonus = 0.04;
        public const double ExhaustedPenalty = 0.08;

        public ConditionModifier(ConditionSide side)
        {
            Side = side;
        }

        public ConditionSide Side { get; }

        int ConditionIn(MatchupContext context) =>
            context == null ? 0 : Side == ConditionSide.Batter ? context.BatterCondition : context.PitcherCondition;

        /// <summary>타자 안타 확률 기준 상대 보정.</summary>
        public double RelativeBonus(MatchupContext context)
        {
            double playerEffect = ConditionIn(context) == PlayerCondition.Excellent ? ExcellentBonus
                : ConditionIn(context) == PlayerCondition.Exhausted ? -ExhaustedPenalty : 0;
            return Side == ConditionSide.Batter ? playerEffect : -playerEffect;
        }

        public double Adjust(double probability, MatchupContext context) => probability * (1 + RelativeBonus(context));

        public string Label(MatchupContext context)
        {
            int c = ConditionIn(context);
            if (c != PlayerCondition.Excellent && c != PlayerCondition.Exhausted) return null;
            return $"{(Side == ConditionSide.Batter ? "타자" : "투수")} 컨디션 {PlayerCondition.Label(c)}";
        }
    }

    /// <summary>
    /// 에이징 커브 보정 (Phase 29). 선수의 폼(<see cref="PlayerData.Form"/>, 오프시즌 성장·노쇠화 누적)으로 안타(출루) 확률을 상대 보정한다.
    /// - 타자: 폼 +3%면 안타 확률 ×1.03.
    /// - 투수: 폼 +3%면 타자 안타 확률 ×0.97 (투수가 좋아지면 타자에게 불리).
    /// 폼은 ±<see cref="MaxForm"/>(10%)로 자른다 — 20세가 5년 뒤 괴물이 되거나 35세가 확률 0이 되지 않게.
    /// 툴팁 예: "타자 성장세 +3%", "투수 노쇠화 +4%".
    /// </summary>
    public sealed class ProgressionModifier : IDescribedMatchupModifier
    {
        /// <summary>폼 한계 (±10%).</summary>
        public const double MaxForm = 0.10;

        public static double ClampForm(double form) =>
            double.IsNaN(form) ? 0 : Math.Max(-MaxForm, Math.Min(MaxForm, form));

        public ProgressionModifier(ConditionSide side)
        {
            Side = side;
        }

        public ConditionSide Side { get; }

        double FormIn(MatchupContext context)
        {
            var p = context == null ? null : Side == ConditionSide.Batter ? context.Batter : context.Pitcher;
            return p == null || MatchupCalculator.IsLeagueAverage(p) ? 0 : ClampForm(p.Form);
        }

        /// <summary>타자 안타 확률 기준 상대 보정.</summary>
        public double RelativeBonus(MatchupContext context)
        {
            double form = FormIn(context);
            return Side == ConditionSide.Batter ? form : -form;
        }

        public double Adjust(double probability, MatchupContext context) => probability * (1 + RelativeBonus(context));

        public string Label(MatchupContext context)
        {
            double form = FormIn(context);
            if (form == 0) return null;
            return $"{(Side == ConditionSide.Batter ? "타자" : "투수")} {(form > 0 ? "성장세" : "노쇠화")}";
        }
    }

    /// <summary>
    /// 상황 보정. 안타(출루) 확률을 받아 보정한 값을 돌려준다. 계산기가 결과를 [0, 1]로 다시 자른다.
    /// 새 보정(예: 2사 보정, 좌우 놀이)은 이 인터페이스로 계산기에 끼운다.
    /// </summary>
    public interface IMatchupModifier
    {
        double Adjust(double probability, MatchupContext context);
    }

    /// <summary>
    /// 툴팁에 내역을 보이는 보정. 이 맥락에서 적용되지 않으면 <see cref="Label"/>이 null.
    /// 보정 크기는 계산기가 적용 전·후 확률로 잰다 (<see cref="MatchupCalculator.Adjustments"/>).
    /// </summary>
    public interface IDescribedMatchupModifier : IMatchupModifier
    {
        /// <summary>내역 이름 (예: "플래툰 열세(좌투 vs 좌타)"). 적용되지 않으면 null.</summary>
        string Label(MatchupContext context);
    }

    /// <summary>보정 하나의 실제 효과 (안타 확률 기준 상대 변화).</summary>
    public readonly struct MatchupAdjustment
    {
        public MatchupAdjustment(string label, double before, double after)
        {
            Label = label;
            Before = before;
            After = after;
        }

        public string Label { get; }
        public double Before { get; }
        public double After { get; }

        /// <summary>상대 변화 (After / Before − 1). Before가 0이면 0.</summary>
        public double RelativeChange => Before > 0 ? After / Before - 1 : 0;

        /// <summary>반올림한 % (예: +3, −5).</summary>
        public int Percent => (int)Math.Round(RelativeChange * 100, MidpointRounding.AwayFromZero);

        /// <summary>"플래툰 우위(우투 vs 좌타) +3%".</summary>
        public string Text => $"{Label} {(Percent > 0 ? "+" : Percent < 0 ? "-" : "±")}{Math.Abs(Percent)}%";

        public override string ToString() => Text;
    }

    /// <summary>
    /// 득점권(2·3루 주자)에서 안타 확률을 일괄로 올린다 (기본 +5%). Phase 17부터 기본 보정은 선수별로 달라지는
    /// <see cref="ClutchModifier"/>이며, 이 클래스는 단순 일괄 보정이 필요할 때 쓴다.
    /// </summary>
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
    /// 플래툰(좌우 상성). 타자 타석 방향 vs 투수 투구 방향:
    /// - 반대 손(우투 vs 좌타, 좌투 vs 우타)·스위치 타자: 타자 우위 <see cref="Advantage"/> (기본 +3%)
    /// - 우투 vs 우타: <see cref="SameHandPenalty"/> (기본 −3%)
    /// - 좌투 vs 좌타: <see cref="LeftOnLeftPenalty"/> (기본 −5%, 좌타자는 좌완 공을 볼 기회가 적어 더 약하다)
    /// 리그 평균 가상 선수(데이터 없음)는 손을 모르므로 보정하지 않는다.
    /// </summary>
    public sealed class PlatoonModifier : IDescribedMatchupModifier
    {
        public readonly double Advantage;
        public readonly double SameHandPenalty;
        public readonly double LeftOnLeftPenalty;

        public PlatoonModifier(double advantage = 0.03, double sameHandPenalty = -0.03, double leftOnLeftPenalty = -0.05)
        {
            foreach (var v in new[] { advantage, sameHandPenalty, leftOnLeftPenalty })
                if (double.IsNaN(v) || v <= -1) throw new ArgumentOutOfRangeException(nameof(advantage), v, "보정은 −100% 초과");
            Advantage = advantage;
            SameHandPenalty = sameHandPenalty;
            LeftOnLeftPenalty = leftOnLeftPenalty;
        }

        /// <summary>이 대결의 상대 보정. 손을 모르면 0.</summary>
        public double RelativeBonus(MatchupContext context)
        {
            if (!Applies(context)) return 0;
            var bats = context.Batter.Bats;
            var throws = context.Pitcher.Throws;
            if (bats == Hand.Switch || bats != throws) return Advantage;
            return throws == Hand.Left ? LeftOnLeftPenalty : SameHandPenalty;
        }

        public double Adjust(double probability, MatchupContext context) => probability * (1 + RelativeBonus(context));

        public string Label(MatchupContext context)
        {
            if (!Applies(context)) return null;
            string bats = context.Batter.Bats == Hand.Switch ? "양" : HandText(context.Batter.Bats);
            string matchup = $"{HandText(context.Pitcher.Throws)}투 vs {bats}타";
            return RelativeBonus(context) >= 0 ? $"플래툰 우위({matchup})" : $"플래툰 열세({matchup})";
        }

        static bool Applies(MatchupContext context) =>
            context != null && !MatchupCalculator.IsLeagueAverage(context.Batter) && !MatchupCalculator.IsLeagueAverage(context.Pitcher)
            && context.Pitcher.Throws != Hand.Switch;

        static string HandText(Hand h) => h == Hand.Left ? "좌" : "우";
    }

    /// <summary>
    /// 득점권 클러치. 2·3루 주자가 있으면 타자의 (득점권 타율 / 시즌 타율 − 1)만큼 안타 확률을 상대 보정한다
    /// ([<see cref="MinBonus"/>, <see cref="MaxBonus"/>]로 자름). 득점권 기록이 없는 타자(리그 평균 포함)는
    /// 리그 기본 <see cref="DefaultBonus"/>(+5%, 이전 일괄 보정과 같다).
    /// </summary>
    public sealed class ClutchModifier : IDescribedMatchupModifier
    {
        public const double DefaultBonus = 0.05;
        public const double MinBonus = -0.15;
        public const double MaxBonus = 0.15;

        /// <summary>득점권이면 적용할 상대 보정, 아니면 0.</summary>
        public static double RelativeBonus(MatchupContext context)
        {
            if (context == null || !context.Situation.RunnersInScoringPosition) return 0;
            return HasClutchRecord(context.Batter)
                ? Math.Max(MinBonus, Math.Min(MaxBonus, context.Batter.ScoringPositionAverage / context.Batter.BattingAverage - 1))
                : DefaultBonus;
        }

        public static bool HasClutchRecord(PlayerData batter) =>
            batter != null && IsRate(batter.ScoringPositionAverage) && IsRate(batter.BattingAverage);

        static bool IsRate(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0 && v <= 1;

        public double Adjust(double probability, MatchupContext context) => probability * (1 + RelativeBonus(context));

        public string Label(MatchupContext context)
        {
            if (context == null || !context.Situation.RunnersInScoringPosition) return null;
            return HasClutchRecord(context.Batter) ? "득점권 클러치" : "득점권";
        }
    }

    /// <summary>
    /// 타자·투수 시즌 기록으로 타석 안타(출루) 확률과 작전 성공률을 계산한다. 순수 C#이며 판정(난수)은 하지 않는다.
    /// 공식은 이 클래스 안에만 있어 나중에 세이버메트릭스 공식으로 바꿀 수 있다.
    ///
    /// 1) 타자 안타력  B = (2 × 타율 + 출루율) / 3
    /// 2) 투수 허용률  P = (피안타율 + 리그 타율 × 평균자책 / 리그 평균자책) / 2
    /// 3) 타석 확률    H = 상황 보정( (B + P) / 2 ), [0, 1]. 기본 보정은 플래툰(<see cref="PlatoonModifier"/>) →
    ///    득점권 클러치(<see cref="ClutchModifier"/>) → 투수 체력(<see cref="StaminaModifier"/>) →
    ///    타자·투수 컨디션(<see cref="ConditionModifier"/>) → 타자·투수 에이징 폼(<see cref="ProgressionModifier"/>, Phase 29) 순으로 곱한다.
    ///    체력 보정은 투수 상태 공급자(<see cref="IPitcherStateSource"/>)를 넘겼을 때만 작동한다 (없으면 확률 그대로).
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
        readonly IPitcherStateSource _pitcherStates;
        readonly IConditionSource _conditions;
        readonly List<IMatchupModifier> _modifiers;

        /// <param name="players">선수 ID → 데이터. null이면 모든 선수가 데이터 없음.</param>
        /// <param name="modifiers">상황 보정 목록. null이면 <see cref="DefaultModifiers"/>, 빈 목록이면 보정 없음.</param>
        /// <param name="pitcherStates">투수 ID → 지금 체력. null이면 체력 보정 없음 (이전과 같은 확률).</param>
        /// <param name="conditions">선수 DB ID → 오늘 컨디션. null이면 컨디션 보정 없음.</param>
        public MatchupCalculator(IPlayerLookup players, IEnumerable<IMatchupModifier> modifiers = null,
            IPitcherStateSource pitcherStates = null, IConditionSource conditions = null)
        {
            _players = players;
            _pitcherStates = pitcherStates;
            _conditions = conditions;
            _modifiers = (modifiers ?? DefaultModifiers()).ToList();
            if (_modifiers.Any(m => m == null)) throw new ArgumentException("보정에 null이 있다.", nameof(modifiers));
        }

        /// <summary>
        /// 기본 보정: 플래툰 → 득점권 클러치 → 투수 체력 → 타자 컨디션 → 투수 컨디션 → 타자 폼 → 투수 폼
        /// (체력·컨디션은 공급자를 넘겨 상태를 알 때만 작동, 폼은 선수 데이터에 덧씌운 값이 있을 때만).
        /// </summary>
        public static IEnumerable<IMatchupModifier> DefaultModifiers() =>
            new IMatchupModifier[]
            {
                new PlatoonModifier(), new ClutchModifier(), new StaminaModifier(),
                new ConditionModifier(ConditionSide.Batter), new ConditionModifier(ConditionSide.Pitcher),
                new ProgressionModifier(ConditionSide.Batter), new ProgressionModifier(ConditionSide.Pitcher),
            };

        public const string AverageBatterId = "league-batter";
        public const string AveragePitcherId = "league-pitcher";

        /// <summary>데이터가 없어 대신 쓰는 리그 평균 가상 선수인가.</summary>
        public static bool IsLeagueAverage(PlayerData player) =>
            player == null || player.Id == AverageBatterId || player.Id == AveragePitcherId;

        public IReadOnlyList<IMatchupModifier> Modifiers => _modifiers;

        /// <summary>리그 평균 타자 (타율·출루율 리그 평균).</summary>
        public static PlayerData AverageBatter => new PlayerData
        {
            Id = AverageBatterId, Name = "리그 평균 타자", Role = RosterRole.Lineup,
            BattingAverage = LeagueAverage, OnBasePercentage = LeagueOnBase,
        };

        /// <summary>리그 평균 투수 (피안타율 = 리그 타율, 평균자책 리그 평균).</summary>
        public static PlayerData AveragePitcher => new PlayerData
        {
            Id = AveragePitcherId, Name = "리그 평균 투수", Role = RosterRole.Bullpen,
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
                PitcherState = _pitcherStates?.StateOf(pitcherId),
                BatterCondition = ConditionOf(batter),
                PitcherCondition = ConditionOf(pitcher),
            };
        }

        /// <summary>선수 DB ID 기준 컨디션. 공급자가 없거나 데이터 없는 선수(리그 평균)면 0.</summary>
        int ConditionOf(PlayerData player) =>
            _conditions == null || player == null || IsLeagueAverage(player) ? 0 : PlayerCondition.Clamp(_conditions.ConditionOf(player.Id));

        /// <summary>투수의 지금 체력 (공급자가 없거나 모르면 null).</summary>
        public PitcherState PitcherStateOf(string pitcherId) => _pitcherStates?.StateOf(pitcherId);

        /// <summary>타석 안타(출루) 확률 H. 둘 다 데이터가 없으면 리그 평균 대결로 계산한다.</summary>
        public double HitProbability(string batterId, string pitcherId, MatchupSituation situation) =>
            HitProbability(ContextFor(batterId, pitcherId, situation)
                           ?? new MatchupContext
                           {
                               Batter = AverageBatter, Pitcher = AveragePitcher, Situation = situation,
                               PitcherState = _pitcherStates?.StateOf(pitcherId),
                           });

        public double HitProbability(MatchupContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            double p = Combine(context.Batter, context.Pitcher);
            foreach (var m in _modifiers) p = Clamp01(m.Adjust(p, context));
            return p;
        }

        /// <summary>
        /// 보정 내역 (툴팁용). <see cref="IDescribedMatchupModifier"/> 중 이 맥락에 적용되고 확률을 실제로 바꾼 것만,
        /// 적용 순서대로. 각 항목은 그 보정 직전·직후 확률을 담는다.
        /// </summary>
        public IReadOnlyList<MatchupAdjustment> Adjustments(MatchupContext context)
        {
            var list = new List<MatchupAdjustment>();
            if (context == null) return list;
            double p = Combine(context.Batter, context.Pitcher);
            foreach (var m in _modifiers)
            {
                double next = Clamp01(m.Adjust(p, context));
                string label = (m as IDescribedMatchupModifier)?.Label(context);
                if (label != null && next != p) list.Add(new MatchupAdjustment(label, p, next));
                p = next;
            }
            return list;
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
