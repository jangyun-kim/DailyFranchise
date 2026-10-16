using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>특별 훈련을 막는 이유 (Phase 33).</summary>
    public enum TrainingBlock
    {
        None,
        /// <summary>내 구단(1군·퓨처스) 선수가 아니다.</summary>
        NotMine,
        /// <summary>24세 초과 (나이 모름 포함).</summary>
        TooOld,
        /// <summary>캠프 포인트 부족.</summary>
        NoPoints,
        /// <summary>폼이 이미 상한(+10%).</summary>
        MaxForm,
        InGame,
    }

    /// <summary>특별 훈련 한 번의 결과.</summary>
    public sealed class TrainingOutcome
    {
        public TrainingBlock Block;
        public bool Trained => Block == TrainingBlock.None;
        public string PlayerId;
        public double FormBefore;
        public double FormAfter;
        public int PointsLeft;
    }

    /// <summary>
    /// 스프링캠프 (Phase 33). 순수 C#. 마스터 CSV·확률 공식은 그대로 두고, 시즌의 선수 변동치(<see cref="PlayerProgression"/>) 폼만 바꾼다
    /// (선수단 장부가 <see cref="PlayerData.WithProgression"/>으로 덧씌운다 — 에이징과 같은 길).
    /// - 캠프 포인트(CP): 결산마다 <see cref="PointsPerSeason"/>점 (<see cref="SeasonState.TransitionToNextSeason"/>). 쓰지 않으면 이월.
    /// - 특별 훈련: 내 구단 1군·퓨처스의 24세 이하 선수, CP <see cref="TrainingCost"/>점으로 폼 +<see cref="TrainingGain"/>(1%p),
    ///   상한 ±10%(<see cref="ProgressionModifier.MaxForm"/>)를 넘지 않는다. 나이·은퇴 기록은 그대로 둔다.
    /// </summary>
    public static class SpringCamp
    {
        public const int PointsPerSeason = 5;
        public const int TrainingCost = 1;
        public const double TrainingGain = 0.01;
        public const int MaxTrainingAge = AgingCurve.ProspectMaxAge;

        /// <summary>내 구단 1군·퓨처스 선수 (장부 사본 — 덧씌운 나이·폼). 없으면 null.</summary>
        public static PlayerData FindMine(SeasonState season, LeagueRosterBook book, string playerId)
        {
            var mine = book?.Get(season.MyTeam);
            return mine == null || playerId == null ? null
                : LeagueRosterBook.AllOf(mine).Concat(mine.Reserves).FirstOrDefault(p => p.Id == playerId && !LeagueRosterBook.IsReplacement(p));
        }

        public static TrainingBlock Check(SeasonState season, LeagueRosterBook book, string playerId)
        {
            if (season.InProgress != null) return TrainingBlock.InGame;
            var p = FindMine(season, book, playerId);
            if (p == null) return TrainingBlock.NotMine;
            if (p.Age <= 0 || p.Age > MaxTrainingAge) return TrainingBlock.TooOld;
            if (CurrentForm(season, playerId) >= ProgressionModifier.MaxForm - 1e-9) return TrainingBlock.MaxForm;
            if (season.CampPoints < TrainingCost) return TrainingBlock.NoPoints;
            return TrainingBlock.None;
        }

        /// <summary>시즌에 기록된 폼 (변동치가 없으면 0).</summary>
        public static double CurrentForm(SeasonState season, string playerId) => season.ProgressionOf(playerId)?.Form ?? 0;

        /// <summary>특별 훈련. 막히면 아무것도 바꾸지 않는다. CP를 쓰고 폼을 올린 변동치를 남긴다 (저장은 부른 쪽이).</summary>
        public static TrainingOutcome Train(SeasonState season, LeagueRosterBook book, string playerId)
        {
            var outcome = new TrainingOutcome { PlayerId = playerId, Block = Check(season, book, playerId), PointsLeft = season.CampPoints };
            outcome.FormBefore = outcome.FormAfter = CurrentForm(season, playerId);
            if (!outcome.Trained) return outcome;
            var before = season.ProgressionOf(playerId);
            double form = ProgressionModifier.ClampForm(outcome.FormBefore + TrainingGain);
            season.SpendCampPoints(TrainingCost);
            season.SetProgression(new PlayerProgression(playerId, before?.AgeDelta ?? 0, form, before?.Retired ?? false, before?.RetiredYear ?? 0));
            outcome.FormAfter = form;
            outcome.PointsLeft = season.CampPoints;
            return outcome;
        }

        public static string BlockText(TrainingBlock block)
        {
            switch (block)
            {
                case TrainingBlock.None: return "";
                case TrainingBlock.NotMine: return "내 구단 선수만 훈련할 수 있습니다.";
                case TrainingBlock.TooOld: return $"특별 훈련은 {MaxTrainingAge}세 이하 유망주만 받을 수 있습니다.";
                case TrainingBlock.NoPoints: return "캠프 포인트(CP)가 없습니다. 시즌을 결산하면 지급됩니다.";
                case TrainingBlock.MaxForm: return $"폼이 이미 상한(+{(int)Math.Round(ProgressionModifier.MaxForm * 100)}%)입니다.";
                case TrainingBlock.InGame: return "경기 중에는 훈련할 수 없습니다.";
                default: throw new ArgumentOutOfRangeException(nameof(block), block, null);
            }
        }
    }

    /// <summary>
    /// 스프링캠프 리포트 (Phase 33): 결산 직후 로비가 한 번 띄운다. 내 구단 선수만.
    /// - 폭풍 성장: 이번 오프시즌 폼 상승 +<see cref="GrowthThreshold"/>(1.5%p) 이상 (큰 순).
    /// - 노쇠화 주의: 32세 이상(노쇠화 구간)에서 폼이 <see cref="DeclineThreshold"/>(0.5%p) 이상 떨어진 선수 (많이 떨어진 순).
    /// - 은퇴, 루키 합류(이번 드래프트 지명 순), 받은 캠프 포인트.
    /// </summary>
    public sealed class SpringCampReport
    {
        public const double GrowthThreshold = 0.015;
        public const double DeclineThreshold = 0.005;

        public int Year;
        public int PointsGranted;
        public int PointsTotal;
        public readonly List<(PlayerData Player, double Delta)> Growth = new List<(PlayerData, double)>();
        public readonly List<(PlayerData Player, double Delta)> Decline = new List<(PlayerData, double)>();
        public readonly List<PlayerData> Retired = new List<PlayerData>();
        public readonly List<DraftPick> Rookies = new List<DraftPick>();

        /// <summary>결산 요약에서 내 구단 항목만 뽑는다 (에이징·드래프트가 없었으면 그 목록은 비어 있다).</summary>
        public static SpringCampReport Build(string myTeam, int year, int granted, int total, AgingReport aging, DraftReport draft)
        {
            var report = new SpringCampReport { Year = year, PointsGranted = granted, PointsTotal = total };
            if (aging != null)
            {
                foreach (var c in aging.FormChanges.Where(c => c.Team == myTeam && c.Delta >= GrowthThreshold - 1e-9).OrderByDescending(c => c.Delta))
                    report.Growth.Add((c.Player, c.Delta));
                foreach (var c in aging.FormChanges.Where(c => c.Team == myTeam && c.Delta <= -DeclineThreshold + 1e-9
                                                               && AgingCurve.StageOf(c.Player.Age) == AgeStage.Decline).OrderBy(c => c.Delta))
                    report.Decline.Add((c.Player, c.Delta));
                report.Retired.AddRange(aging.Retired.Where(r => r.Team == myTeam).Select(r => r.Player));
            }
            if (draft != null) report.Rookies.AddRange(draft.MyPicks);
            return report;
        }

        /// <summary>"+2.4%p" / "−1.5%p".</summary>
        public static string DeltaText(double delta)
        {
            double pct = Math.Round(delta * 100, 1, MidpointRounding.AwayFromZero);
            var inv = CultureInfo.InvariantCulture;
            return pct >= 0 ? "+" + pct.ToString("0.0", inv) + "%p" : "−" + (-pct).ToString("0.0", inv) + "%p";
        }
    }
}
