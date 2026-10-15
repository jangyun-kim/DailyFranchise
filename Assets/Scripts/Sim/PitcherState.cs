using System;
using System.Collections.Generic;

namespace DailyFranchise.Sim
{
    /// <summary>투수 체력 단계.</summary>
    public enum FatigueLevel
    {
        /// <summary>체력 30% 이상.</summary>
        Fresh,
        /// <summary>체력 30% 미만 (한계 투구 수 70% 초과).</summary>
        Tired,
        /// <summary>한계 투구 수 도달 (선발 100구, 구원 30구).</summary>
        Exhausted,
    }

    /// <summary>
    /// 인게임 투수 상태 (투구 수·체력). 경기 중에만 쓰는 값이며 선수 데이터나 시즌 기록에 저장하지 않는다.
    /// 투구 수는 난수로 더하지 않고 실제로 던진 투구 이벤트를 센다 (<see cref="PitchesBy"/>) — Seed 기반 재현성을 그대로 지킨다.
    /// 체력 = 1 − 투구 수 / 한계, [0, 1].
    /// </summary>
    public sealed class PitcherState
    {
        public const int StarterLimit = 100;
        public const int RelieverLimit = 30;
        /// <summary>체력이 이 값 미만이면 <see cref="FatigueLevel.Tired"/>.</summary>
        public const double TiredBelow = 0.30;

        public PitcherState(string pitcherId, bool isStarter, int pitchCount)
        {
            if (pitchCount < 0) throw new ArgumentOutOfRangeException(nameof(pitchCount), pitchCount, "투구 수는 0 이상");
            PitcherId = pitcherId;
            IsStarter = isStarter;
            PitchCount = pitchCount;
        }

        public string PitcherId { get; }
        public bool IsStarter { get; }
        public int PitchCount { get; }

        /// <summary>한계 투구 수: 선발 100, 구원 30.</summary>
        public int Limit => IsStarter ? StarterLimit : RelieverLimit;

        /// <summary>남은 체력 [0, 1].</summary>
        public double Stamina => Math.Max(0, 1 - (double)PitchCount / Limit);

        /// <summary>체력 % (반올림).</summary>
        public int StaminaPercent => (int)Math.Round(Stamina * 100, MidpointRounding.AwayFromZero);

        public FatigueLevel Level =>
            PitchCount >= Limit ? FatigueLevel.Exhausted : Stamina < TiredBelow ? FatigueLevel.Tired : FatigueLevel.Fresh;

        /// <summary>투구 수를 더한 새 상태.</summary>
        public PitcherState AddPitches(int pitches)
        {
            if (pitches < 0) throw new ArgumentOutOfRangeException(nameof(pitches), pitches, "더할 투구 수는 0 이상");
            return new PitcherState(PitcherId, IsStarter, PitchCount + pitches);
        }

        /// <summary>장면 선발 자리 ID인가 (예: "away_sp"). 그 외(rpN 등)는 구원.</summary>
        public static bool IsStarterSlot(string pitcherId) =>
            pitcherId != null && pitcherId.EndsWith("_sp", StringComparison.Ordinal);

        /// <summary>events[0..throughIndex]에서 해당 투수가 던진 공 수. throughIndex가 음수면 0.</summary>
        public static int PitchesBy(IReadOnlyList<PitchEvent> events, string pitcherId, int throughIndex)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            int n = 0;
            for (int i = 0; i <= throughIndex && i < events.Count; i++)
                if (events[i].PitcherId == pitcherId) n++;
            return n;
        }

        /// <summary>events[0..throughIndex]에서 해당 투수가 마운드에 있을 때 상대가 올린 득점 (자책 구분 없음).</summary>
        public static int RunsAllowedBy(IReadOnlyList<PitchEvent> events, string pitcherId, int throughIndex)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            int runs = 0;
            for (int i = 0; i <= throughIndex && i < events.Count; i++)
                if (events[i].PitcherId == pitcherId) runs += events[i].RunsScored;
            return runs;
        }

        public override string ToString() => $"{PitcherId} {PitchCount}구 체력 {StaminaPercent}% ({Level})";
    }

    /// <summary>투수 ID → 지금 체력 상태. 계산기(<see cref="MatchupCalculator"/>)가 경기 진행 방식을 모르게 하는 경계.</summary>
    public interface IPitcherStateSource
    {
        /// <summary>모르는 투수면 null (체력 보정 없음).</summary>
        PitcherState StateOf(string pitcherId);
    }

    /// <summary>
    /// 체력 저하 보정: 지친 투수는 안타(출루)를 더 허용한다. 단계별로 점진적으로 커진다.
    /// - 체력 30% 미만(<see cref="FatigueLevel.Tired"/>): <see cref="TiredPenalty"/> (기본 +5%), 툴팁 "체력 저하".
    /// - 한계 투구 수 도달(<see cref="FatigueLevel.Exhausted"/>, 선발 100구·구원 30구): <see cref="ExhaustedPenalty"/>
    ///   (기본 +10%), 툴팁 "체력 고갈: 피안타율 급증".
    /// 투수 상태를 모르면(<see cref="MatchupContext.PitcherState"/> null) 보정하지 않는다.
    /// </summary>
    public sealed class StaminaModifier : IDescribedMatchupModifier
    {
        public const string TiredLabel = "체력 저하";
        public const string ExhaustedLabel = "체력 고갈: 피안타율 급증";

        public readonly double TiredPenalty;
        public readonly double ExhaustedPenalty;

        public StaminaModifier(double tiredPenalty = 0.05, double exhaustedPenalty = 0.10)
        {
            foreach (var v in new[] { tiredPenalty, exhaustedPenalty })
                if (double.IsNaN(v) || v <= -1) throw new ArgumentOutOfRangeException(nameof(tiredPenalty), v, "보정은 −100% 초과");
            TiredPenalty = tiredPenalty;
            ExhaustedPenalty = exhaustedPenalty;
        }

        /// <summary>이 대결의 상대 보정. 체력이 충분하거나 모르면 0.</summary>
        public double RelativeBonus(MatchupContext context)
        {
            switch (context?.PitcherState?.Level)
            {
                case FatigueLevel.Exhausted: return ExhaustedPenalty;
                case FatigueLevel.Tired: return TiredPenalty;
                default: return 0;
            }
        }

        public double Adjust(double probability, MatchupContext context) => probability * (1 + RelativeBonus(context));

        public string Label(MatchupContext context)
        {
            switch (context?.PitcherState?.Level)
            {
                case FatigueLevel.Exhausted: return ExhaustedLabel;
                case FatigueLevel.Tired: return TiredLabel;
                default: return null;
            }
        }
    }
}
