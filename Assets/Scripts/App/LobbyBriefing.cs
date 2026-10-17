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

    /// <summary>
    /// 로비 "오늘의 결정" 한 줄. 선수 항목은 누르면 선수 상세가 열린다.
    /// Phase 39: 해결 동선 — <see cref="MarketPosition"/>이 있으면 [시장에서 찾기](그 포지션으로 거른 FA·트레이드 시장),
    /// <see cref="FuturesId"/>가 있으면 [퓨처스 후보](그 선수 상세 → 1군 등록) 버튼이 붙는다.
    /// </summary>
    public sealed class BriefingItem
    {
        public BriefingItem(string key, BriefingSeverity severity, string title, string detail, string playerId = null,
            string marketPosition = null, string futuresId = null)
        {
            Key = key;
            Severity = severity;
            Title = title;
            Detail = detail ?? "";
            PlayerId = playerId;
            MarketPosition = marketPosition;
            FuturesId = futuresId;
        }

        /// <summary>시장에서 찾을 포지션 (예: "C"). 없으면 null.</summary>
        public string MarketPosition { get; }
        /// <summary>그 자리를 메울 퓨처스 후보 선수 ID. 없으면 null.</summary>
        public string FuturesId { get; }

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
    ///   부상자 명단의 1군 선수(Phase 39), 벤치에 대신 설 선수가 없는 수비 위치(뎁스 부족, Phase 39 — 사실상 포수),
    ///   오늘 선발이 지친 상태, 정규시즌 종료 → 결산. 지친 타자에게는 컨디션이 보통 이상인 벤치 교체 후보를 붙인다.
    ///   선수·포지션 문제에는 해결 동선(시장 포지션·퓨처스 후보)을 붙인다.
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
            var series = season.Postseason;
            if (season.Phase == SeasonPhase.PostSeason && series != null)
                items.Add(season.InPostseason
                    ? new BriefingItem("postseason", BriefingSeverity.Info, $"한국시리즈 · {series.RecordText(season.MyTeam)}",
                        $"{PostseasonSeries.WinsToClinch}승 선착 팀이 우승합니다. 지친 선수를 쉬게 하세요.")
                    : new BriefingItem("postseason", BriefingSeverity.Info, "가을야구 탈락",
                        "[가을야구 결과 시뮬레이션]으로 한국시리즈 결과를 보고 결산하세요"));
            if (season.Phase == SeasonPhase.OffSeason)
                items.Add(new BriefingItem("offseason", BriefingSeverity.Info,
                    series?.Champion != null ? $"{season.Year} 시즌 종료 · {series.Champion} 우승" : $"{season.Year} 시즌 종료",
                    $"결산하고 {season.Year + 1} 시즌을 준비하세요 (에이징·은퇴·드래프트)"));
            if (rosterProblem != null)
                items.Add(new BriefingItem("roster", BriefingSeverity.Danger, "경기를 시작할 수 없습니다", rosterProblem));
            // 오프시즌에는 치를 경기가 없어 컨디션 경고를 내지 않는다 (결산 때 컨디션은 보통 이상으로 돌아간다).
            if (lineup == null || season.Phase == SeasonPhase.OffSeason) return items;

            var fresh = lineup.Bench.Where(p => season.ConditionOf(p.Id) > WarnCondition)
                .OrderByDescending(p => season.ConditionOf(p.Id)).ThenBy(p => p.Order).ToList();
            var futures = lineup.Reserves.Where(p => !p.IsPitcher && !season.IsInjured(p.Id))
                .OrderByDescending(p => p.War).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            foreach (var p in lineup.Lineup.Where(p => season.ConditionOf(p.Id) <= WarnCondition)
                         .OrderBy(p => season.ConditionOf(p.Id)).ThenBy(p => p.Order))
            {
                int c = season.ConditionOf(p.Id);
                var sub = fresh.FirstOrDefault(b => PositionMatcher.FitOf(p.Position, b.Position) != PositionFit.None);
                string detail = sub != null
                    ? $"추천 교체: 벤치 {sub.Name} ({sub.Position} · {PlayerCondition.Label(season.ConditionOf(sub.Id))})"
                    : $"{PositionMatcher.Label(p.Position)} 자리를 대신할 벤치 선수 없음 · 시장이나 퓨처스에서 찾아보세요";
                items.Add(new BriefingItem("cond-" + p.Id, c <= PlayerCondition.Exhausted ? BriefingSeverity.Danger : BriefingSeverity.Caution,
                    $"{p.Order}번 {p.Name} ({p.Position}) · 컨디션 {PlayerCondition.Label(c)}", detail, p.Id,
                    MarketPositionOf(p), sub == null ? FuturesFor(p.Position, futures)?.Id : null));
            }
            // 부상자 명단 (Phase 36 — 1군에서 다친 선수만 퓨처스 부상자 명단으로 간다)
            foreach (var p in lineup.Reserves.Where(p => season.IsInjured(p.Id))
                         .OrderByDescending(p => season.InjuryGamesOf(p.Id)).ThenBy(p => p.Id, StringComparer.Ordinal))
            {
                int games = season.InjuryGamesOf(p.Id);
                var cover = p.IsPitcher ? null : FuturesFor(p.Position, futures);
                items.Add(new BriefingItem("injury-" + p.Id, BriefingSeverity.Caution,
                    $"부상 · {p.Name} ({PositionMatcher.Label(p.Position)}) · {games}경기 결장",
                    p.IsPitcher ? "불펜·로테이션 공백 · 시장에서 투수를 찾아보세요"
                        : $"그동안 {PositionMatcher.Label(p.Position)} 자리를 누가 맡을지 정하세요" + (cover != null ? $" · 퓨처스 후보 {cover.Name}" : ""),
                    p.Id, MarketPositionOf(p), cover?.Id));
            }
            // 뎁스 부족: 선발 타자의 수비 위치를 벤치(건강한 야수)에서 아무도 대신할 수 없다
            foreach (var p in lineup.Lineup.Where(p => PositionMatcher.FieldPositions.Contains(p.Position)).GroupBy(p => p.Position).Select(g => g.First()))
            {
                bool covered = lineup.Bench.Any(b => !b.IsPitcher && !season.IsInjured(b.Id) && PositionMatcher.FitOf(p.Position, b.Position) != PositionFit.None);
                if (covered) continue;
                var cover = FuturesFor(p.Position, futures);
                string label = PositionMatcher.Label(p.Position);
                items.Add(new BriefingItem("depth-" + p.Position, BriefingSeverity.Caution,
                    $"{label} 뎁스 부족 · 백업 {label} 없음",
                    $"주전 {p.Name}이(가) 지치거나 다치면 대신 설 벤치 선수가 없습니다" + (cover != null ? $" · 퓨처스 후보 {cover.Name}" : ""),
                    null, p.Position, cover?.Id));
            }
            if (todayStarter != null && season.ConditionOf(todayStarter.Id) < RotationPicker.MinStartCondition)
                items.Add(new BriefingItem("starter-" + todayStarter.Id, BriefingSeverity.Caution,
                    $"오늘 선발 {todayStarter.Name} · 컨디션 {PlayerCondition.Label(season.ConditionOf(todayStarter.Id))}",
                    "로테이션 전원이 지쳐 가장 나은 투수가 나갑니다", todayStarter.Id));
            return items;
        }

        /// <summary>시장에서 찾을 포지션: 야수는 수비 위치, 투수는 "P".</summary>
        static string MarketPositionOf(PlayerData p) => p.IsPitcher ? "P" : p.Position;

        /// <summary>그 자리를 메울 퓨처스 야수 (같은 위치 → 같은 수비군, WAR 높은 순). 없으면 null.</summary>
        static PlayerData FuturesFor(string position, IReadOnlyList<PlayerData> futures) => PositionMatcher.Pick(position, futures);

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

        /// <summary>헤더 게임 내 날짜 줄: "다음 경기 5.15 (토)" / "한국시리즈 3차전 5.30 (토)" / "시즌 종료 · 스토브리그".</summary>
        public static string GameDayText(SeasonState season)
        {
            var next = season.NextGame;
            switch (season.Phase)
            {
                case SeasonPhase.PostSeason:
                    return next != null ? $"한국시리즈 {next.Number}차전 {next.Date}" : "한국시리즈 진행 중";
                case SeasonPhase.OffSeason:
                    return "시즌 종료 · 스토브리그";
                default:
                    return next == null ? "정규시즌 종료" : $"다음 경기 {next.Date}";
            }
        }

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
