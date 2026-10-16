using System;
using System.Collections.Generic;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.Sim
{
    /// <summary>
    /// 팀 케미스트리 보정 (Phase 23, <see cref="ChemistryRules"/>). 세 케미스트리를 한 보정에서 합산해 ±<see cref="ChemistryRules.Cap"/>로 자른다.
    /// 공격팀 = 타자 소속, 수비팀 = 투수 소속. 리그 평균 가상 선수(소속 없음)는 보정 없음.
    /// 기본 보정 목록(<see cref="MatchupCalculator.DefaultModifiers"/>)에는 없고, 경기 화면이 케미스트리를 알 때만 덧붙인다.
    /// </summary>
    public sealed class ChemistryModifier : IDescribedMatchupModifier
    {
        readonly IChemistrySource _source;
        readonly IPlayerLookup _runners;

        /// <param name="runners">
        /// 주자 ID(장면 ID일 수 있다) → 선수 DB 데이터. null이면 주자 ID를 그대로 선수 DB ID로 본다.
        /// </param>
        public ChemistryModifier(IChemistrySource source, IPlayerLookup runners = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _runners = runners;
        }

        public double RelativeBonus(MatchupContext context) => Sum(Parts(context));

        public double Adjust(double probability, MatchupContext context) => probability * (1 + RelativeBonus(context));

        public string Label(MatchupContext context)
        {
            var parts = Parts(context);
            if (parts.Count == 0 || Sum(parts) == 0) return null;
            var names = new List<string>();
            foreach (var (kind, _) in parts) names.Add(ChemistryRules.Label(kind));
            return "케미 " + string.Join("·", names);
        }

        static double Sum(List<(ChemistryKind kind, double bonus)> parts)
        {
            double sum = 0;
            foreach (var (_, bonus) in parts) sum += bonus;
            return Math.Max(-ChemistryRules.Cap, Math.Min(ChemistryRules.Cap, sum));
        }

        /// <summary>이 타석에 적용되는 케미스트리별 보정 (발동 순서: 발야구 → 철벽 센터라인 → 거포 클린업).</summary>
        List<(ChemistryKind kind, double bonus)> Parts(MatchupContext context)
        {
            var parts = new List<(ChemistryKind, double)>();
            if (context?.Batter == null || context.Pitcher == null) return parts;
            if (MatchupCalculator.IsLeagueAverage(context.Batter) || MatchupCalculator.IsLeagueAverage(context.Pitcher)) return parts;
            var offense = _source.ActiveFor(context.Batter.Team);
            var defense = _source.ActiveFor(context.Pitcher.Team);
            if (offense == null && defense == null) return parts;
            var runners = context.Situation.Runners;

            if (offense != null && offense.Has(ChemistryKind.SpeedBall) &&
                (IsSpeedRunner(offense, runners.First) || IsSpeedRunner(offense, runners.Second)))
            {
                bool countered = defense != null && defense.Has(ChemistryKind.CenterLineGlove);
                parts.Add((ChemistryKind.SpeedBall, ChemistryRules.SpeedBonus * (countered ? ChemistryRules.CounterFactor : 1)));
            }

            if (defense != null && defense.Has(ChemistryKind.CenterLineGlove))
            {
                bool countered = offense != null && offense.IsMember(ChemistryKind.PowerCleanup, context.Batter.Id);
                parts.Add((ChemistryKind.CenterLineGlove, -ChemistryRules.GloveBonus * (countered ? ChemistryRules.CounterFactor : 1)));
            }

            if (offense != null && offense.IsMember(ChemistryKind.PowerCleanup, context.Batter.Id) && runners.RunnerCount > 0)
            {
                bool countered = _source.TagOf(context.Pitcher.Id) == ChemistryTag.Ace;
                parts.Add((ChemistryKind.PowerCleanup, ChemistryRules.PowerBonus * (countered ? ChemistryRules.CounterFactor : 1)));
            }
            return parts;
        }

        bool IsSpeedRunner(ActiveChemistry offense, string runnerId)
        {
            if (runnerId == null) return false;
            string id = _runners == null ? runnerId : _runners.FindPlayer(runnerId)?.Id;
            return offense.IsMember(ChemistryKind.SpeedBall, id);
        }
    }
}
