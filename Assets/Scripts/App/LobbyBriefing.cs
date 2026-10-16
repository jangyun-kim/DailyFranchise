using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>오늘의 결정 항목의 심각도 (Phase 31).</summary>
    public enum BriefingSeverity
    {
        /// <summary>알림 (예: 정규시즌 종료 → 결산).</summary>
        Info,
        /// <summary>주의 (예: 지침 선수).</summary>
        Caution,
        /// <summary>위험 (예: 위험 컨디션 선수, 경기를 시작할 수 없는 선수단).</summary>
        Danger,
    }

    /// <summary>로비 "오늘의 결정" 한 줄. 선수 항목은 누르면 선수 상세가 열린다.</summary>
    public sealed class BriefingItem
    {
        public BriefingItem(string key, BriefingSeverity severity, string title, string detail, string playerId = null)
        {
            Key = key;
            Severity = severity;
            Title = title;
            Detail = detail ?? "";
            PlayerId = playerId;
        }

        /// <summary>요소 이름용 열쇠 ("cond-p_삼성_2024_김영웅", "roster", "offseason").</summary>
        public string Key { get; }
        public BriefingSeverity Severity { get; }
        public string Title { get; }
        public string Detail { get; }
        /// <summary>관련 선수 (없으면 null).</summary>
        public string PlayerId { get; }
    }

    /// <summary>
    /// 로비 홈 대시보드 뷰모델 (Phase 31, UI 계층 — 시즌·선수단을 읽기만 한다). 순수 C#이라 EditMode에서 검증한다.
    /// - 오늘의 결정: 경기를 막는 선수단 문제, 선발 타순의 지친 선수(컨디션 <see cref="WarnCondition"/> 이하, 위험 먼저),
    ///   오늘 선발이 지친 상태, 정규시즌 종료 → 결산. 지친 타자에게는 컨디션이 보통 이상인 벤치 교체 후보를 붙인다.
    /// - 맞대결 프리뷰 문구: 순위·전적, 선발 투수 한 줄, 시즌 상대 전적.
    /// </summary>
    public static class LobbyBriefing
    {
        /// <summary>이 컨디션 이하(지침·위험)인 선발 타자를 경고한다. 쉬면(출장하지 않으면) 경기마다 +1 회복한다.</summary>
        public const int WarnCondition = PlayerCondition.Tired;

        /// <param name="lineup">라인업을 적용한 내 구단 선수단 (<see cref="SeasonManager.MyRoster"/>). 없으면 선수 항목 없음.</param>
        /// <param name="todayStarter">오늘 선발 (<see cref="RotationPicker"/>). 없으면 null.</param>
        /// <param name="rosterProblem">경기를 시작할 수 없는 이유 (<see cref="RosterLimits.GameReadinessProblem"/>), 없으면 null.</param>
        public static List<BriefingItem> Decisions(SeasonState season, TeamRoster lineup, PlayerData todayStarter, string rosterProblem)
        {
            if (season == null) throw new ArgumentNullException(nameof(season));
            var items = new List<BriefingItem>();
            if (season.IsSeasonFinished)
                items.Add(new BriefingItem("offseason", BriefingSeverity.Info, $"{season.Year} 정규시즌 종료",
                    $"결산하고 {season.Year + 1} 시즌을 준비하세요 (에이징·은퇴·드래프트)"));
            if (rosterProblem != null)
                items.Add(new BriefingItem("roster", BriefingSeverity.Danger, "경기를 시작할 수 없습니다", rosterProblem));
            if (lineup == null) return items;

            var fresh = lineup.Bench.Where(p => season.ConditionOf(p.Id) > WarnCondition)
                .OrderByDescending(p => season.ConditionOf(p.Id)).ThenBy(p => p.Order).ToList();
            foreach (var p in lineup.Lineup.Where(p => season.ConditionOf(p.Id) <= WarnCondition)
                         .OrderBy(p => season.ConditionOf(p.Id)).ThenBy(p => p.Order))
            {
                int c = season.ConditionOf(p.Id);
                var sub = fresh.FirstOrDefault(b => PositionMatcher.FitOf(p.Position, b.Position) != PositionFit.None) ?? fresh.FirstOrDefault();
                string detail = sub != null
                    ? $"추천 교체: 벤치 {sub.Name} ({sub.Position} · {PlayerCondition.Label(season.ConditionOf(sub.Id))})"
                    : "교체할 벤치 선수 없음 · 쉬면 경기마다 회복";
                items.Add(new BriefingItem("cond-" + p.Id, c <= PlayerCondition.Exhausted ? BriefingSeverity.Danger : BriefingSeverity.Caution,
                    $"{p.Order}번 {p.Name} ({p.Position}) · 컨디션 {PlayerCondition.Label(c)}", detail, p.Id));
            }
            if (todayStarter != null && season.ConditionOf(todayStarter.Id) < RotationPicker.MinStartCondition)
                items.Add(new BriefingItem("starter-" + todayStarter.Id, BriefingSeverity.Caution,
                    $"오늘 선발 {todayStarter.Name} · 컨디션 {PlayerCondition.Label(season.ConditionOf(todayStarter.Id))}",
                    "로테이션 전원이 지쳐 가장 나은 투수가 나갑니다", todayStarter.Id));
            return items;
        }

        /// <summary>"2위 · 5승 3패" (무승부가 있으면 "· 1무"). 순위표에 없으면 "순위 없음".</summary>
        public static string StandingText(StandingRow row)
        {
            if (row == null) return "순위 없음";
            string record = row.Draws == 0 ? $"{row.Wins}승 {row.Losses}패" : $"{row.Wins}승 {row.Losses}패 {row.Draws}무";
            return $"{row.Rank}위 · {record}";
        }

        /// <summary>선발 투수 한 줄: "ERA 3.66 · FIP 4.34" (기록이 없으면 "기록 없음"). 투수가 없으면 "선발 정보 없음". WHIP 등은 선수 상세에서.</summary>
        public static string StarterLine(PlayerData p)
        {
            if (p == null) return "선발 정보 없음";
            var inv = CultureInfo.InvariantCulture;
            var parts = new List<string>();
            if (p.Era > 0) parts.Add("ERA " + p.Era.ToString("0.00", inv));
            if (p.Fip > 0) parts.Add("FIP " + p.Fip.ToString("0.00", inv));
            return parts.Count == 0 ? "기록 없음" : string.Join(" · ", parts);
        }

        /// <summary>이번 시즌 이 상대와 치른 경기의 내 구단 전적: "시즌 상대 전적 1승 0패" (아직 없으면 "시즌 첫 맞대결").</summary>
        public static string HeadToHeadText(SeasonState season, string opponent)
        {
            int w = 0, l = 0, d = 0;
            foreach (var g in season.Schedule)
            {
                if (g.Status != GameStatus.Final || (g.AwayTeam != opponent && g.HomeTeam != opponent)) continue;
                var o = season.MyOutcome(g);
                if (o == GameOutcome.Win) w++;
                else if (o == GameOutcome.Loss) l++;
                else if (o == GameOutcome.Draw) d++;
            }
            if (w + l + d == 0) return "시즌 첫 맞대결";
            return d == 0 ? $"시즌 상대 전적 {w}승 {l}패" : $"시즌 상대 전적 {w}승 {l}패 {d}무";
        }

        /// <summary>헤더 게임 내 날짜 줄: "다음 경기 5.15 (토)" / "정규시즌 종료".</summary>
        public static string GameDayText(SeasonState season) =>
            season.NextGame == null ? "정규시즌 종료" : $"다음 경기 {season.NextGame.Date}";

        /// <summary>KBO 구단 테마 클래스 (팀 컬러, USS <c>team--samsung</c> 등). 모르는 구단은 기본 블루.</summary>
        public static string TeamThemeClass(string team)
        {
            switch (team)
            {
                case "삼성 라이온즈": return "team--samsung";
                case "LG 트윈스": return "team--lg";
                case "KIA 타이거즈": return "team--kia";
                case "두산 베어스": return "team--doosan";
                case "KT 위즈": return "team--kt";
                case "SSG 랜더스": return "team--ssg";
                case "롯데 자이언츠": return "team--lotte";
                case "한화 이글스": return "team--hanwha";
                case "NC 다이노스": return "team--nc";
                case "키움 히어로즈": return "team--kiwoom";
                default: return "team--default";
            }
        }

        /// <summary>엠블럼 자리 약칭: 구단 이름 첫 단어 ("삼성 라이온즈" → "삼성").</summary>
        public static string TeamMark(string team) =>
            string.IsNullOrEmpty(team) ? "—" : team.Split(' ')[0];
    }
}
