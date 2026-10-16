using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>
    /// 선수 한 명의 시즌 간 변동치 (Phase 29). 마스터 CSV는 바꾸지 않고 이 값만 시즌과 함께 저장한다.
    /// 선수단 장부(<see cref="LeagueRosterBook.Build"/>)가 원본 선수의 사본에 나이(원본 + <see cref="AgeDelta"/>)와
    /// 폼(<see cref="PlayerData.Form"/>)을 덧씌우고, 은퇴한 선수는 빼고 짠다.
    /// </summary>
    public sealed class PlayerProgression
    {
        public PlayerProgression(string playerId, int ageDelta, double form, bool retired, int retiredYear)
        {
            if (string.IsNullOrEmpty(playerId)) throw new ArgumentException("선수 ID가 비어 있다.", nameof(playerId));
            PlayerId = playerId;
            AgeDelta = Math.Max(0, ageDelta);
            Form = ProgressionModifier.ClampForm(form);
            Retired = retired;
            RetiredYear = retired ? Math.Max(0, retiredYear) : 0;
        }

        public string PlayerId { get; }
        /// <summary>원본 나이에 더할 햇수 (오프시즌마다 +1).</summary>
        public int AgeDelta { get; }
        /// <summary>누적 폼 (안타 확률 상대 보정, ±10%).</summary>
        public double Form { get; }
        public bool Retired { get; }
        /// <summary>은퇴를 선언한 오프시즌의 새 시즌 연도 (은퇴가 아니면 0).</summary>
        public int RetiredYear { get; }
    }

    /// <summary>나이 구간 (에이징 커브).</summary>
    public enum AgeStage
    {
        /// <summary>나이 모름 (CSV 0) — 변동 없음.</summary>
        Unknown,
        /// <summary>24세 이하 유망주: 높은 확률로 성장.</summary>
        Prospect,
        /// <summary>25~31세 전성기: 유지 또는 소폭 변동.</summary>
        Peak,
        /// <summary>32세 이상: 해마다 하락 누적.</summary>
        Decline,
    }

    /// <summary>
    /// 에이징 커브와 은퇴 규칙 (Phase 29). 순수 C#. 나이는 오프시즌에 한 살 먹은 뒤의 나이로 판정한다.
    /// - 유망주(≤24): <see cref="ProspectGrowthChance"/>(75%) 확률로 폼 +0.5~3.0%p, 아니면 그대로.
    /// - 전성기(25~31): 폼 −1.0~+1.0%p.
    /// - 노쇠화(≥32): 폼 −(1.0% + 0.5% × (나이 − 32)), 최대 −3.0%p, 여기에 ±0.5%p 흔들림.
    /// - 폼은 누적되며 ±10%로 자른다 (<see cref="ProgressionModifier.MaxForm"/>).
    /// - 은퇴: 40세 이상 무조건, 37~39세는 (나이 − 36) × 20% 확률, 34세 이상이면서 폼 −8% 이하로 무너지면 50% 확률.
    /// - 폭풍 성장: 한 해 폼 상승 +2.5%p 이상.
    /// </summary>
    public static class AgingCurve
    {
        public const int ProspectMaxAge = 24;
        public const int PeakMaxAge = 31;
        public const double ProspectGrowthChance = 0.75;
        public const double ProspectMinGrowth = 0.005;
        public const double ProspectMaxGrowth = 0.03;
        public const double PeakSwing = 0.01;
        public const double DeclineBase = 0.01;
        public const double DeclinePerYear = 0.005;
        public const double DeclineMax = 0.03;
        public const double DeclineJitter = 0.005;
        public const double BreakoutGrowth = 0.025;

        public const int ForcedRetirementAge = 40;
        public const int RetirementRiskAge = 37;
        public const double RetirementRiskPerYear = 0.2;
        public const int CollapseAge = 34;
        public const double CollapseForm = -0.08;
        public const double CollapseRetirementChance = 0.5;

        public static AgeStage StageOf(int age) =>
            age <= 0 ? AgeStage.Unknown : age <= ProspectMaxAge ? AgeStage.Prospect : age <= PeakMaxAge ? AgeStage.Peak : AgeStage.Decline;

        /// <summary>한 해 폼 변화 (클램프 전). 난수는 나이를 아는 선수만 쓴다.</summary>
        public static double FormDelta(int age, IRandomSource random)
        {
            switch (StageOf(age))
            {
                case AgeStage.Prospect:
                    return random.NextDouble() < ProspectGrowthChance
                        ? ProspectMinGrowth + random.NextDouble() * (ProspectMaxGrowth - ProspectMinGrowth)
                        : 0;
                case AgeStage.Peak:
                    return (random.NextDouble() * 2 - 1) * PeakSwing;
                case AgeStage.Decline:
                    double decline = Math.Min(DeclineMax, DeclineBase + DeclinePerYear * (age - (PeakMaxAge + 1)));
                    return -decline + (random.NextDouble() * 2 - 1) * DeclineJitter;
                default:
                    return 0;
            }
        }

        /// <summary>은퇴 확률 (한 살 먹은 뒤 나이·폼 기준).</summary>
        public static double RetirementChance(int age, double form)
        {
            if (age <= 0) return 0;
            if (age >= ForcedRetirementAge) return 1;
            double byAge = age >= RetirementRiskAge ? (age - RetirementRiskAge + 1) * RetirementRiskPerYear : 0;
            double byCollapse = age >= CollapseAge && form <= CollapseForm ? CollapseRetirementChance : 0;
            return Math.Max(byAge, byCollapse);
        }

        /// <summary>"+3%" / "−4%" (폼 표기).</summary>
        public static string FormText(double form)
        {
            int pct = (int)Math.Round(form * 100, MidpointRounding.AwayFromZero);
            return pct > 0 ? $"+{pct}%" : pct < 0 ? $"−{-pct}%" : "±0%";
        }
    }

    /// <summary>오프시즌 에이징 한 번의 결과.</summary>
    public sealed class AgingReport
    {
        /// <summary>한 살 먹은 선수 수.</summary>
        public int Aged;
        /// <summary>은퇴한 선수 (구단·선수 사본은 은퇴 직전 장부 기준, 새 나이).</summary>
        public readonly List<(string Team, PlayerData Player)> Retired = new List<(string, PlayerData)>();
        /// <summary>폭풍 성장 (구단, 선수, 이번 상승폭).</summary>
        public readonly List<(string Team, PlayerData Player, double Gain)> Breakouts = new List<(string, PlayerData, double)>();
        /// <summary>은퇴하지 않은 선수 전원의 이번 폼 변화 (구단, 새 나이·폼 사본, 변화량) — 스프링캠프 리포트용 (Phase 33).</summary>
        public readonly List<(string Team, PlayerData Player, double Delta)> FormChanges = new List<(string, PlayerData, double)>();
    }

    /// <summary>
    /// 오프시즌 에이징 처리 (Phase 29, <see cref="SeasonState.TransitionToNextSeason(LeagueRosterBook, IRandomSource)"/>).
    /// 장부의 모든 선수(1군·퓨처스·자유계약, 대체 선수 제외)를 한 살 먹이고 폼을 바꾼 뒤 은퇴를 판정해
    /// 시즌의 선수 변동치에 남기고 뉴스를 낸다. 은퇴 선수는 다음 장부부터 빠진다 (정원·페이롤·시장에서도).
    /// 난수는 선수 ID 순으로 뽑아 장부 순서와 무관하게 결정적이다.
    /// </summary>
    public static class OffseasonAging
    {
        /// <summary>오프시즌 성장 뉴스 최대 건수 (상승폭 큰 순, 내 구단 먼저).</summary>
        public const int MaxBreakoutNews = 3;
        /// <summary>은퇴 뉴스 최대 건수 (내 구단 먼저, 나이 많은 순).</summary>
        public const int MaxRetirementNews = 8;

        public static AgingReport Process(SeasonState season, LeagueRosterBook book, IRandomSource random)
        {
            if (season == null) throw new ArgumentNullException(nameof(season));
            if (random == null) throw new ArgumentNullException(nameof(random));
            var report = new AgingReport();
            if (book == null) return report;

            var players = book.Rosters
                .SelectMany(r => LeagueRosterBook.AllOf(r).Concat(r.Reserves).Select(p => (Team: r.Team, Player: p)))
                .Where(x => !LeagueRosterBook.IsReplacement(x.Player) && season.ProgressionOf(x.Player.Id)?.Retired != true)
                .GroupBy(x => x.Player.Id).Select(g => g.First())
                .OrderBy(x => x.Player.Id, StringComparer.Ordinal)
                .ToList();

            foreach (var (team, p) in players)
            {
                var before = season.ProgressionOf(p.Id);
                int ageDelta = (before?.AgeDelta ?? 0) + 1;
                int age = p.Age > 0 ? p.Age + 1 : 0; // 장부 사본의 나이는 이미 덧씌운 나이
                double oldForm = before?.Form ?? 0;
                double form = ProgressionModifier.ClampForm(oldForm + AgingCurve.FormDelta(age, random));
                bool retired = age > 0 && random.NextDouble() < AgingCurve.RetirementChance(age, form);
                season.SetProgression(new PlayerProgression(p.Id, ageDelta, form, retired, retired ? season.Year : 0));
                report.Aged++;
                var aged = p.WithProgression(age, form);
                if (!retired) report.FormChanges.Add((team, aged, form - oldForm));
                if (retired) report.Retired.Add((team, aged));
                else if (form - oldForm >= AgingCurve.BreakoutGrowth) report.Breakouts.Add((team, aged, form - oldForm));
            }

            string my = season.MyTeam;
            foreach (var b in report.Breakouts.OrderByDescending(b => b.Team == my).ThenByDescending(b => b.Gain).Take(MaxBreakoutNews))
                season.AddNews(NewsWriter.Breakout(b.Team, b.Player.Name, b.Gain, season.Year));
            foreach (var r in report.Retired.OrderByDescending(r => r.Team == my).ThenByDescending(r => r.Player.Age).Take(MaxRetirementNews))
                season.AddNews(NewsWriter.Retired(r.Team, r.Player.Name, r.Player.Age, season.Year));
            return report;
        }
    }
}
