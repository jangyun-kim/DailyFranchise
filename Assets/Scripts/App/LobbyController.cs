using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.UI.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.App
{
    /// <summary>
    /// 1920×1080 로비 (Phase 31 결정 중심 대시보드). SeasonManager의 시즌 상태를 읽어 그리기만 하고,
    /// [다음 경기 시작]으로 경기 화면 진입을 요청한다. 경기 화면을 직접 참조하지 않는다.
    /// - 헤더: 팀 컬러 엠블럼·구단·성적 / 프런트 오피스(예산·팬 만족도·시즌 진행·신뢰도와 팬덤 특전) / 게임 내 시즌·다음 경기일.
    ///   현실 날짜와 원천 데이터 연도는 보이지 않는다. 화면 루트에 구단 테마 클래스(<see cref="LobbyBriefing.TeamThemeClass"/>)를 붙여
    ///   헤더 띠·엠블럼·프리뷰 카드·주요 버튼을 팀 컬러로 칠한다.
    /// - 왼쪽 라인업·벤치: 행을 누르면 선수 상세 모달(<see cref="PlayerDetailView"/> — 기록·컨디션·교체·방출).
    ///   상세의 [직접 고르기] 뒤(선택 중)에는 기존 Click-to-Swap대로 두 번째 행과 맞바꾼다 (<see cref="ClickLineupSpot"/>).
    ///   컨디션은 위험(빨강)·지침(주황)·최상(파랑)만 보이고 보통·좋음은 숨긴다. 방출 버튼은 목록에 없다 (상세 모달 안에서만, 두 번 눌러 확정).
    /// - 가운데 1단: 다음 경기 프리뷰(원정·홈 순위·전적, 오늘 선발 비교, 시즌 상대 전적) / 오늘의 결정(<see cref="LobbyBriefing.Decisions"/> —
    ///   지친 선발 타자·경기 불가 선수단·결산 필요, 선수 항목은 누르면 상세).
    ///   2단: 리그 뉴스 최신 <see cref="NewsShown"/>건 / 투수진(선발 로테이션·불펜 칩, 누르면 상세). 3단: 팀 주요 통계 / 팀 내 기록 1위.
    /// - 오른쪽: 리그 순위, 그 아래 작은 일정 목록 (구장은 툴팁).
    /// - 하단: 지난 경기·다음 경기 요약과 [스카우트 / FA] · 진행 방식 · [다음 경기 시작] (정규시즌 뒤에는 [시즌 결산]).
    /// 경기 뒤 로비 진입, FA 협상·방출·라인업 교체(LineupChanged·NewsChanged), 시즌 결산 때 다시 그린다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyController : MonoBehaviour
    {
        VisualElement _root;
        Button _start;
        Button _mode;
        Button _scout;
        Button _campClose;
        FaMarketView _fa;
        PlayerDetailView _detail;
        SeasonManager _manager;
        readonly LineupSelection _selection = new LineupSelection();

        /// <summary>지금 그린 라인업 카드. 선수 DB가 없으면 null (교체 불가).</summary>
        LineupCard _card;

        /// <summary>FA 시장 모달 (로비가 켜져 있을 때만).</summary>
        public FaMarketView FaMarket => _fa;

        /// <summary>선수 상세 모달 (로비가 켜져 있을 때만).</summary>
        public PlayerDetailView PlayerDetail => _detail;

        /// <summary>현재 라인업 선택 상태 (테스트·진단용).</summary>
        public LineupSelection Selection => _selection;

        /// <summary>방출 결과 등 라인업 안내 줄에 잠시 보일 문구. 다음 클릭 때 지운다.</summary>
        string _notice;

        /// <summary>마지막으로 그린 케미스트리 열쇠 (<see cref="ChemistryText.Key"/>). null이면 아직 안 그림.</summary>
        string _chemistryKey;

        /// <summary>케미스트리 문구를 실제로 바꾼 횟수 (결과가 같으면 늘지 않는다 — 테스트·진단용).</summary>
        public int ChemistryRenderCount { get; private set; }

        /// <summary>지금 표시 중인 내 구단 케미스트리. 선수 DB가 없으면 null.</summary>
        public ActiveChemistry Chemistry { get; private set; }

        /// <summary>오늘의 결정 카드에 보이는 최대 항목 수 (넘치면 머리에 "외 N건").</summary>
        public const int DecisionsShown = 3;

        /// <summary>로비 뉴스 패널에 보이는 최신 뉴스 수.</summary>
        public const int NewsShown = 5;

        void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _start = _root.Q<Button>("start-game");
            _start.clicked += OnStartClicked;
            _mode = _root.Q<Button>("game-mode");
            _mode.clicked += OnModeClicked;
            _scout = _root.Q<Button>("scout-fa");
            _scout.clicked += OnScoutClicked;
            _campClose = _root.Q<Button>("camp-close");
            _campClose.clicked += CloseCampReport;
            _manager = SeasonManager.EnsureExists();
            _fa = new FaMarketView(_root, _manager, OpenPlayer);
            _detail = new PlayerDetailView(_root, _manager, ReleasePlayer, BeginSwapFrom, TradePlayer, MovePlayer);
            _manager.ModeChanged += Render;
            _manager.DatabaseReady += OnDatabaseReady;
            _manager.LineupChanged += OnLineupChanged;
            _manager.StoveChanged += OnStoveChanged;
            _manager.SeasonRolledOver += OnSeasonRolledOver;
            _manager.NewsChanged += OnNewsChanged;
            _manager.PostseasonAdvanced += Render;
            _manager.CampChanged += OnCampChanged;
            _selection.Clear();
            _chemistryKey = null;
            Render();
        }

        void OnDisable()
        {
            if (_start != null) _start.clicked -= OnStartClicked;
            if (_mode != null) _mode.clicked -= OnModeClicked;
            if (_scout != null) _scout.clicked -= OnScoutClicked;
            if (_campClose != null) _campClose.clicked -= CloseCampReport;
            if (_manager != null)
            {
                _manager.ModeChanged -= Render;
                _manager.DatabaseReady -= OnDatabaseReady;
                _manager.LineupChanged -= OnLineupChanged;
                _manager.StoveChanged -= OnStoveChanged;
                _manager.SeasonRolledOver -= OnSeasonRolledOver;
                _manager.NewsChanged -= OnNewsChanged;
                _manager.PostseasonAdvanced -= Render;
                _manager.CampChanged -= OnCampChanged;
            }
            _fa?.Detach();
            _fa = null;
            _detail?.Detach();
            _detail = null;
            _start = null;
            _mode = null;
            _scout = null;
            _manager = null;
        }

        void OnDatabaseReady() => Render();

        void OnCampChanged() => RenderCampPoints(_manager.Season);

        /// <summary>헤더 캠프 포인트 (Phase 33). 0이면 숨긴다.</summary>
        void RenderCampPoints(SeasonState season)
        {
            var label = Q<Label>("camp-points");
            label.text = $"캠프 포인트 {season.CampPoints} CP";
            label.tooltip = $"결산마다 {SpringCamp.PointsPerSeason} CP · 24세 이하 선수 특별 훈련에 1 CP (폼 +1%p)";
            SetVisible(label, season.CampPoints > 0);
        }

        /// <summary>스프링캠프 리포트가 떠 있는지 (Phase 33).</summary>
        public bool CampReportOpen => Q("camp-modal").ClassListContains("camp-modal--open");

        /// <summary>[캠프 시작]: 리포트를 닫는다 (다시 뜨지 않는다).</summary>
        public void CloseCampReport()
        {
            _manager?.DismissCampReport();
            Q("camp-modal").RemoveFromClassList("camp-modal--open");
        }

        /// <summary>
        /// 결산 직후 한 번 뜨는 스프링캠프 리포트 (Phase 33, <see cref="SeasonManager.PendingCampReport"/>):
        /// 받은 CP, 폭풍 성장(+1.5%p 이상), 노쇠화 주의(32세 이상 하락)·은퇴, 루키 합류. 목록은 칸마다 최대 <see cref="CampRowsShown"/>줄.
        /// </summary>
        void RenderCampReport()
        {
            var report = _manager.PendingCampReport;
            var modal = Q("camp-modal");
            modal.EnableInClassList("camp-modal--open", report != null);
            if (report == null) return;
            _detail?.Close();
            Q<Label>("camp-title").text = $"{report.Year} 스프링캠프 리포트";
            Q<Label>("camp-cp").text = $"캠프 포인트 +{report.PointsGranted} CP 지급 · 보유 {report.PointsTotal} CP";
            FillCamp("camp-growth", report.Growth.Select(g =>
                $"{g.Player.Name} · {g.Player.Position} · {g.Player.Age}세 · {SpringCampReport.DeltaText(g.Delta)} (폼 {AgingCurve.FormText(g.Player.Form)})"),
                "크게 성장한 선수가 없습니다.");
            FillCamp("camp-decline", report.Decline.Select(d =>
                    $"{d.Player.Name} · {d.Player.Position} · {d.Player.Age}세 · {SpringCampReport.DeltaText(d.Delta)} (폼 {AgingCurve.FormText(d.Player.Form)})")
                .Concat(report.Retired.Select(r => $"{r.Name} · {r.Age}세 · 은퇴")),
                "눈에 띄게 하락한 베테랑이 없습니다.");
            FillCamp("camp-rookies", report.Rookies.Select(r =>
                $"{r.Round}R {r.Player.Name} · {PositionMatcher.Label(r.Player.Position)} · {r.Player.Age}세"),
                "이번 드래프트 지명이 없습니다.");
            Q<Label>("camp-hint").text = $"24세 이하 선수를 눌러 [스프링캠프 특별 훈련]에 CP를 쓰세요 (1 CP = 폼 +1%p, 상한 +10%).";
        }

        public const int CampRowsShown = 6;

        void FillCamp(string container, IEnumerable<string> rows, string empty)
        {
            var box = Q(container);
            box.Clear();
            var list = rows.ToList();
            if (list.Count == 0)
            {
                var none = Cell(empty, "camp-row");
                none.AddToClassList("camp-row--empty");
                box.Add(none);
                return;
            }
            foreach (var r in list.Take(CampRowsShown)) box.Add(Cell(r, "camp-row"));
            if (list.Count > CampRowsShown)
            {
                var more = Cell($"외 {list.Count - CampRowsShown}명", "camp-row");
                more.AddToClassList("camp-row--empty");
                box.Add(more);
            }
        }

        void OnNewsChanged() => RenderNews(_manager.Season);

        /// <summary>최신 뉴스부터 <see cref="NewsShown"/>건 (말머리 · 본문 · 날짜). 없으면 안내 한 줄.</summary>
        void RenderNews(SeasonState season)
        {
            var container = Q("news");
            container.Clear();
            var latest = season.LatestNews(NewsShown);
            if (latest.Count == 0)
            {
                container.Add(Cell("아직 리그 뉴스가 없습니다. 경기를 치르거나 FA 시장을 열어 보세요.", "news-empty"));
                return;
            }
            for (int i = 0; i < latest.Count; i++)
            {
                var n = latest[i];
                var row = new VisualElement { name = "news-" + i, userData = n };
                row.AddToClassList("news-row");
                row.EnableInClassList("news-row--latest", i == 0);
                var tag = Cell(n.Tag, "news-tag");
                string tagClass = NewsTagClass(n.Tag);
                if (tagClass != null) tag.AddToClassList(tagClass);
                row.Add(tag);
                var text = Cell(NewsWriter.DisplayText(n.Headline), "news-text"); // Phase 35: 묶은 덩어리는 줄바꿈 없이
                text.tooltip = n.Text;
                row.Add(text);
                row.Add(Cell(n.Date.Length == 0 ? $"{n.Year}" : $"{n.Year} · {n.Date}", "news-date"));
                container.Add(row);
            }
        }

        static string NewsTagClass(string tag)
        {
            switch (tag)
            {
                case NewsWriter.OfficialTag: return "news-tag--official";
                case NewsWriter.ExclusiveTag: return "news-tag--exclusive";
                case NewsWriter.BreakingTag: return "news-tag--breaking";
                case NewsWriter.WalkOffTag: return "news-tag--walkoff";
                case NewsWriter.PostseasonTag: return "news-tag--postseason";
                case NewsWriter.ChampionTag: return "news-tag--champion";
                case NewsWriter.InjuryTag: return "news-tag--injury";
                case NewsWriter.RecoveryTag: return "news-tag--recovery";
                default: return null;
            }
        }

        void OnSeasonRolledOver()
        {
            _selection.Clear();
            _notice = null;
            _detail?.Close();
            Render();
        }

        void OnModeClicked() => _manager?.ToggleFullInningMode();

        /// <summary>[스카우트 / FA]: FA 시장 모달을 연다. 선수 DB가 없거나 경기 중이면 무시한다.</summary>
        public void OpenFaMarket()
        {
            if (_manager == null || !_manager.IsDatabaseReady || _manager.Season.InProgress != null) return;
            _detail.Close();
            _fa.Open();
        }

        void OnScoutClicked() => OpenFaMarket();

        /// <summary>협상 뒤: 예산·TP가 바뀌었을 수 있어 헤더를 다시 그린다 (선수단은 영입 시 LineupChanged로).</summary>
        void OnStoveChanged()
        {
            var season = _manager.Season;
            RenderFrontOffice(season);
            Q<Label>("my-record").text = RecordText(season);
        }

        void OnLineupChanged()
        {
            _selection.Clear();
            var season = _manager.Season;
            RenderRoster(season);
            RenderLeaders();
            RenderPreview(season);
            RenderDecisions(season);
        }

        /// <summary>
        /// 라인업 자리 선택 (Click-to-Swap). 선수 DB 라인업이 없거나 경기 중이면 무시한다.
        /// 두 번째 클릭이 맞바꿀 수 있는 자리면 SeasonManager에 교체를 요청한다 (성공하면 LineupChanged로 다시 그림).
        /// 행을 누르는 기본 동작은 선수 상세(<see cref="OnRowClicked"/>)이고, 선택 중일 때만 이 경로로 온다.
        /// </summary>
        public void ClickLineupSpot(LineupSpot spot)
        {
            if (_card == null || !_card.Contains(spot) || _manager.Season.InProgress != null) return;
            _notice = null;
            var result = _selection.Click(spot, _card.CanSwap);
            if (result == LineupClick.Swap)
            {
                var (first, second) = _selection.PendingSwap.Value;
                if (_manager.SwapLineup(first, second)) return;
            }
            RenderSelection();
        }

        /// <summary>라인업·벤치 행 클릭: 교체 대상을 고르는 중이면 그 자리로 교체, 아니면 선수 상세를 연다.</summary>
        void OnRowClicked(LineupSpot spot)
        {
            if (_selection.Selected.HasValue)
            {
                ClickLineupSpot(spot);
                return;
            }
            OpenPlayer(_card?.PlayerAt(spot));
        }

        /// <summary>선수 상세 모달을 연다 (내 구단 1군·퓨처스 선수, 또는 FA 모달 타 구단 명단의 선수). 열었으면 true.</summary>
        public bool OpenPlayer(string playerId)
        {
            if (_detail == null || playerId == null) return false;
            _notice = null;
            return _detail.Open(playerId);
        }

        /// <summary>상세 모달의 [직접 고르기]: 이 자리를 선택한 상태로 두고 두 번째 행을 기다린다.</summary>
        void BeginSwapFrom(LineupSpot spot)
        {
            _selection.Clear();
            ClickLineupSpot(spot);
        }

        void OnStartClicked()
        {
            var manager = SeasonManager.Instance;
            if (manager == null) return;
            // 우승 팀이 정해졌으면 같은 버튼이 시즌 결산 (결산 뒤 SeasonRolledOver로 다시 그린다).
            if (manager.Season.Phase == SeasonPhase.OffSeason)
            {
                manager.TransitionToNextSeason();
                return;
            }
            // 내 구단이 탈락한 한국시리즈는 결과만 시뮬레이션 (Phase 32, PostseasonAdvanced로 다시 그린다).
            if (manager.Season.Phase == SeasonPhase.PostSeason && !manager.Season.InPostseason)
            {
                manager.SimulatePostseason();
                return;
            }
            // 씬 로드 전에 두 번 눌리지 않도록 즉시 잠근다.
            if (manager.StartNextGame()) _start.SetEnabled(false);
        }

        void Render()
        {
            _ = _manager.Rosters; // 선수단 장부·페이롤·캡 기준을 먼저 맞춘다 (헤더 유지비 표기가 페이롤을 쓴다)
            var season = _manager.Season;
            ApplyTeamTheme(season.MyTeam);
            // 가을야구 테마 (Phase 32): 한국시리즈 중, 그리고 내 구단이 우승한 오프시즌에 금색 강조
            Q("screen").EnableInClassList("ps-theme", season.Phase == SeasonPhase.PostSeason
                || (season.Phase == SeasonPhase.OffSeason && season.Postseason?.Champion == season.MyTeam));
            Q<Label>("season-title").text = season.SeasonTitle;
            Q<Label>("game-day").text = LobbyBriefing.GameDayText(season);
            Q<Label>("my-team").text = season.MyTeam;
            Q<Label>("team-mark").text = LobbyBriefing.TeamMark(season.MyTeam);
            Q<Label>("my-record").text = RecordText(season);

            RenderFrontOffice(season);
            RenderCampPoints(season);
            RenderRoster(season);
            RenderPreview(season);
            RenderDecisions(season);
            RenderTeamStats(season);
            RenderLeaders();
            RenderSchedule(season);
            RenderStandings(season);
            RenderNews(season);

            Q<Label>("last-result").text = season.LastFinished == null && _manager.LastRollover != null
                ? _manager.LastRollover.Text
                : LastResultText(season);
            Q<Label>("last-finance").text = _manager.LastReport?.FinanceText ?? "";

            var next = season.NextGame;
            bool offseason = season.Phase == SeasonPhase.OffSeason;
            bool skipPostseason = season.Phase == SeasonPhase.PostSeason && !season.InPostseason;
            Q<Label>("next-game").text = NextGameText(season);
            string problem = _manager.IsDatabaseReady && next != null ? _manager.RosterProblem : null;
            _start.text = offseason ? OffseasonButtonText(season)
                : skipPostseason ? PostseasonSkipButtonText
                : !_manager.IsDatabaseReady ? "선수 데이터 불러오는 중…"
                : problem != null ? "선수단 확인 필요"
                : next == null ? "다음 경기 없음"
                : season.Phase == SeasonPhase.PostSeason ? $"한국시리즈 {next.Number}차전 시작"
                : "다음 경기 시작";
            _start.EnableInClassList("btn--offseason", offseason || skipPostseason);
            // 결산·가을야구 시뮬레이션은 선수 DB 없이도 할 수 있다.
            _start.SetEnabled(season.InProgress == null
                && (offseason || skipPostseason || (_manager.IsDatabaseReady && problem == null && next != null)));
            _start.tooltip = offseason
                ? "전적·일정을 새로 짜고 예산·팬 만족도·신뢰도·선수단은 그대로 넘깁니다. 신뢰 대출의 50%는 새 시즌 캡에서 빠집니다."
                : skipPostseason ? "내 구단이 탈락한 한국시리즈의 결과를 바로 정하고 결산 단계로 넘어갑니다."
                : problem ?? "";
            _scout.SetEnabled(_manager.IsDatabaseReady && season.InProgress == null);
            _mode.text = _manager.FullInningMode ? "진행: 풀 이닝 (1회부터)" : "진행: 퀵 시뮬 (8회부터)";
            _mode.EnableInClassList("btn--active", _manager.FullInningMode);
            RenderCampReport();
        }

        /// <summary>화면 루트에 구단 테마 클래스 하나만 남긴다.</summary>
        void ApplyTeamTheme(string team)
        {
            var screen = Q("screen");
            foreach (var c in screen.GetClasses().Where(c => c.StartsWith("team--", StringComparison.Ordinal)).ToList())
                screen.RemoveFromClassList(c);
            screen.AddToClassList(LobbyBriefing.TeamThemeClass(team));
        }

        /// <summary>내 구단이 탈락한 포스트시즌의 시작 버튼 문구 (Phase 32).</summary>
        public const string PostseasonSkipButtonText = "가을야구 결과 시뮬레이션";

        /// <summary>
        /// 하단 다음 경기 줄: 정규시즌 "경기 3 · 5.17 (일) · KIA vs 삼성 · 구장", 한국시리즈 "한국시리즈 2차전 · 날짜 · 대진 · 구장",
        /// 탈락 "2026 한국시리즈 KIA 타이거즈 vs LG 트윈스 · 삼성 라이온즈 가을야구 탈락", 오프시즌 "… 우승. 결산 후 2027 시즌".
        /// </summary>
        public static string NextGameText(SeasonState season)
        {
            var next = season.NextGame;
            var ps = season.Postseason;
            switch (season.Phase)
            {
                case SeasonPhase.PostSeason when next != null:
                    return $"한국시리즈 {next.Number}차전 · {next.Date} · {next.MatchupText}"; // 구장은 프리뷰에
                case SeasonPhase.PostSeason:
                    return ps == null ? $"{season.Year} 한국시리즈 진행 중"
                        : $"{season.Year} 한국시리즈 {ps.TopSeed} vs {ps.SecondSeed} · {season.MyTeam} 가을야구 탈락";
                case SeasonPhase.OffSeason:
                    return ps?.Champion != null
                        ? $"{season.Year} 한국시리즈 {ps.Champion} 우승. 결산 후 {season.Year + 1} 시즌으로 넘어갑니다."
                        : $"{season.Year} 시즌이 끝났습니다. 결산 후 {season.Year + 1} 시즌으로 넘어갑니다.";
                default:
                    return next == null
                        ? $"{season.Year} 정규시즌 일정이 모두 끝났습니다."
                        : $"경기 {next.Number} · {next.Date} · {next.MatchupText} · {next.Stadium}";
            }
        }

        /// <summary>정규시즌 종료 뒤 시작 버튼 문구: "시즌 결산 및 2027 시즌 준비".</summary>
        public static string OffseasonButtonText(SeasonState season) => $"시즌 결산 및 {season.Year + 1} 시즌 준비";

        /// <summary>
        /// 헤더 성적 줄. 정규시즌 "3위 · 23승 18패 0무 · 승률 · 진행", 한국시리즈 "한국시리즈 삼성 2승 - 1승 LG · 정규시즌 1위",
        /// 우승 "한국시리즈 우승 · 삼성 4승 - 1승 LG", 탈락 "정규시즌 3위 · 23승 18패 0무 · 가을야구 탈락".
        /// </summary>
        public static string RecordText(SeasonState season)
        {
            var me = season.MyStanding;
            if (me == null) return "—";
            var ps = season.Postseason;
            if (season.Phase != SeasonPhase.RegularSeason && ps != null)
            {
                if (!ps.Involves(season.MyTeam)) return $"정규시즌 {me.Rank}위 · {me.Wins}승 {me.Losses}패 {me.Draws}무 · 가을야구 탈락";
                if (ps.Champion == season.MyTeam) return $"한국시리즈 우승 · {ps.RecordText(season.MyTeam)}";
                if (ps.IsOver) return $"한국시리즈 준우승 · {ps.RecordText(season.MyTeam)}";
                return $"한국시리즈 {ps.RecordText(season.MyTeam)} · 정규시즌 {me.Rank}위";
            }
            return $"{me.Rank}위 · {me.Wins}승 {me.Losses}패 {me.Draws}무 · 승률 {me.WinPctText} · 진행 {season.GamesPlayed} / {season.Schedule.Count}경기";
        }

        // ---------- 다음 경기 프리뷰 / 오늘의 결정 ----------

        /// <summary>
        /// 다음 경기 프리뷰: 원정·홈 구단과 순위·전적, 오늘 선발(로테이션 규칙 그대로 — 상대 선수 데이터가 없으면 "선발 정보 없음"),
        /// 내 구단 쪽 강조, 시즌 상대 전적·최근 10경기. 정규시즌이 끝났으면 결산 안내.
        /// </summary>
        void RenderPreview(SeasonState season)
        {
            var game = season.NextGame;
            var away = Q("pv-away");
            var home = Q("pv-home");
            var ps = season.Postseason;
            Q<Label>("pv-title").text = season.Phase == SeasonPhase.RegularSeason ? "다음 경기"
                : season.Phase == SeasonPhase.PostSeason ? "KOREAN SERIES · 한국시리즈" : "한국시리즈 결과";
            if (season.Phase != SeasonPhase.RegularSeason && ps != null)
            {
                RenderSeriesPreview(season, ps, game);
                return;
            }
            if (game == null)
            {
                Q<Label>("pv-meta").text = "정규시즌 종료";
                SetSide("away", season.MyTeam, season, null, false);
                SetSide("home", "—", season, null, false);
                away.EnableInClassList("pv-side--mine", true);
                home.EnableInClassList("pv-side--mine", false);
                Q<Label>("pv-note").text = $"결산하면 {season.Year + 1} 시즌이 시작됩니다.";
                return;
            }
            Q<Label>("pv-meta").text = $"경기 {game.Number} · {game.Date} · {game.Stadium}";
            bool ready = _manager.IsDatabaseReady;
            SetSide("away", game.AwayTeam, season, ready ? _manager.GameRosterOf(game.AwayTeam)?.StartingPitcher : null, ready);
            SetSide("home", game.HomeTeam, season, ready ? _manager.GameRosterOf(game.HomeTeam)?.StartingPitcher : null, ready);
            away.EnableInClassList("pv-side--mine", game.AwayTeam == season.MyTeam);
            home.EnableInClassList("pv-side--mine", game.HomeTeam == season.MyTeam);
            string opponent = game.AwayTeam == season.MyTeam ? game.HomeTeam : game.AwayTeam;
            var l10 = season.MyRecentForm();
            Q<Label>("pv-note").text = LobbyBriefing.HeadToHeadText(season, opponent)
                + (l10.Games > 0 ? $" · 최근 {l10.Games}경기 {l10.Text}" : "");
        }

        /// <summary>
        /// 한국시리즈 프리뷰 (Phase 32): 두 팀의 시리즈 승수와 정규시즌 순위, 다음 경기 선발(내 구단이 나갈 때), 시리즈 전적 줄.
        /// 다음 경기가 없으면(탈락·종료) 1위 팀을 원정 칸이 아니라 왼쪽에, 2위 팀을 오른쪽에 둔다.
        /// </summary>
        void RenderSeriesPreview(SeasonState season, PostseasonSeries ps, ScheduledGame game)
        {
            string left = game?.AwayTeam ?? ps.TopSeed, right = game?.HomeTeam ?? ps.SecondSeed;
            bool ready = _manager.IsDatabaseReady && game != null;
            Q<Label>("pv-meta").text = game != null ? $"{game.Number}차전 · {game.Date} · {game.Stadium}"
                : ps.IsOver ? $"{ps.Champion} 우승" : "내 구단 탈락 · 결과 시뮬레이션 대기";
            SetSide("away", left, season, ready ? _manager.GameRosterOf(left)?.StartingPitcher : null, ready);
            SetSide("home", right, season, ready ? _manager.GameRosterOf(right)?.StartingPitcher : null, ready);
            Q<Label>("pv-away-standing").text = $"시리즈 {ps.WinsOf(left)}승 · 정규시즌 {season.StandingOf(left)?.Rank ?? 0}위";
            Q<Label>("pv-home-standing").text = $"시리즈 {ps.WinsOf(right)}승 · 정규시즌 {season.StandingOf(right)?.Rank ?? 0}위";
            Q("pv-away").EnableInClassList("pv-side--mine", left == season.MyTeam);
            Q("pv-home").EnableInClassList("pv-side--mine", right == season.MyTeam);
            Q<Label>("pv-note").text = ps.IsOver
                ? $"{ps.RecordText(season.MyTeam)} · {ps.Champion} 우승"
                : $"{ps.RecordText(season.MyTeam)} · {PostseasonSeries.WinsToClinch}승 선착 팀 우승";
        }

        void SetSide(string side, string team, SeasonState season, PlayerData starter, bool ready)
        {
            Q<Label>($"pv-{side}-team").text = team;
            Q<Label>($"pv-{side}-standing").text = team == "—" ? "" : LobbyBriefing.StandingText(season.StandingOf(team));
            var sp = Q<Label>($"pv-{side}-sp");
            sp.text = starter?.Name ?? (ready ? "선발 미정" : "—");
            string line = !ready ? "" : LobbyBriefing.StarterLine(starter);
            if (starter != null && team == season.MyTeam)
            {
                int c = season.ConditionOf(starter.Id);
                if (c != PlayerCondition.Normal) line += " · 컨디션 " + PlayerCondition.Label(c);
            }
            Q<Label>($"pv-{side}-sp-line").text = line;
        }

        /// <summary>오늘의 결정: 경고 항목 최대 <see cref="DecisionsShown"/>건 (선수 항목은 누르면 상세). 없으면 "바로 시작해도 좋다".</summary>
        void RenderDecisions(SeasonState season)
        {
            var container = Q("decisions");
            container.Clear();
            var roster = _manager.IsDatabaseReady ? _manager.MyRoster : null;
            var today = roster == null ? null : _manager.GameRosterOf(season.MyTeam)?.StartingPitcher;
            string problem = _manager.IsDatabaseReady && season.NextGame != null ? _manager.RosterProblem : null;
            var items = LobbyBriefing.Decisions(season, roster, today, problem);
            Q<Label>("decisions-count").text = items.Count > DecisionsShown ? $"{items.Count}건 중 {DecisionsShown}건 · 외 {items.Count - DecisionsShown}건"
                : items.Count > 0 ? $"{items.Count}건" : "";
            if (items.Count == 0)
            {
                container.Add(DecisionRow(new BriefingItem("ok", BriefingSeverity.Info,
                    _manager.IsDatabaseReady ? "라인업 컨디션 이상 없음" : "선수 데이터 불러오는 중…",
                    _manager.IsDatabaseReady ? "바로 경기를 시작해도 좋습니다." : ""), ok: true));
                return;
            }
            foreach (var item in items.Take(DecisionsShown)) container.Add(DecisionRow(item, ok: false));
        }

        VisualElement DecisionRow(BriefingItem item, bool ok)
        {
            var row = new VisualElement { name = "decision-" + item.Key, userData = item };
            row.AddToClassList("decision");
            row.EnableInClassList("decision--danger", item.Severity == BriefingSeverity.Danger);
            row.EnableInClassList("decision--caution", item.Severity == BriefingSeverity.Caution);
            row.EnableInClassList("decision--ok", ok);
            row.Add(Cell(item.Title, "decision__title"));
            row.Add(Cell(item.Detail, "decision__detail"));
            if (item.PlayerId != null)
            {
                row.AddToClassList("decision--player");
                row.tooltip = "누르면 선수 상세 (교체·휴식)";
                string id = item.PlayerId;
                row.RegisterCallback<ClickEvent>(_ => OpenPlayer(id));
            }
            return row;
        }

        // ---------- 라인업 ----------

        void RenderRoster(SeasonState season)
        {
            var mine = _manager.MyRoster;
            _card = _manager.CurrentLineup;
            if (mine != null && _card != null)
            {
                RenderChemistry(ChemistryRules.Evaluate(mine, _manager.ChemistryTags), _manager.ChemistryTags != null);
                RenderDatabaseRoster(mine);
                RenderRotation(mine, season);
                // 1군 엔트리 밖 퓨처스 선수 수 (Phase 26.5 — 실제 데이터는 구단 전체 선수단이다)
                Q<Label>("bench-title").text = mine.Reserves.Count > 0 ? $"벤치 · 퓨처스 {mine.Reserves.Count}명 대기" : "벤치";
                RenderFuturesRookies(mine);
                RenderSelection();
                return;
            }

            // 선수 DB를 읽는 중이거나 내 구단 데이터가 없으면 선수 이름을 지어내지 않고 비워 둔다 (Phase 26.5).
            RenderChemistry(null, false);
            _card = null;
            _selection.Clear();
            Q("rotation").Clear();
            SetVisible(Q("rotation-title"), false);
            SetVisible(Q("bullpen-title"), false);
            Q("bullpen").Clear();
            Q("roster").Clear();
            Q("bench").Clear();
            RenderFuturesRookies(null);
            Q<Label>("lineup-hint").text = _manager.IsDatabaseReady
                ? $"{season.MyTeam} 선수 데이터가 없습니다. 선수 CSV를 확인하세요."
                : "선수 데이터 불러오는 중…";
        }

        /// <summary>
        /// 퓨처스 신인 한 줄 (Phase 30): "퓨처스 신인 4명 · 김민준 C 19세 외 3명" (첫 지명), 툴팁은 전원 (라운드·위치·나이·주요 기록).
        /// 1군 선수단(벤치 포함)의 신인은 벤치 행에 이미 보이므로 퓨처스에 있는 신인만. 없으면 숨긴다.
        /// </summary>
        void RenderFuturesRookies(TeamRoster roster)
        {
            var label = Q<Label>("futures-rookies");
            var season = _manager.Season;
            // Phase 36: 부상자 명단 (퓨처스에 내려간 부상 선수, 남은 경기 많은 순) — 신인 줄 앞에, 칩도 먼저
            var injured = roster?.Reserves.Where(p => season.IsInjured(p.Id))
                .OrderByDescending(p => season.InjuryGamesOf(p.Id)).ThenBy(p => p.Id, StringComparer.Ordinal).ToList() ?? new List<PlayerData>();
            var rookies = roster?.Reserves.Where(p => RookieGenerator.IsRookie(p) && !season.IsInjured(p.Id)).ToList() ?? new List<PlayerData>();
            SetVisible(label, rookies.Count > 0 || injured.Count > 0);
            string rookieText = rookies.Count == 0 ? ""
                : $"퓨처스 신인 {rookies.Count}명 · {rookies[0].Name} {rookies[0].Position} {rookies[0].Age}세"
                  + (rookies.Count > 1 ? $" 외 {rookies.Count - 1}명" : "");
            label.text = injured.Count == 0 ? rookieText
                : $"{InjuryRules.Icon} 부상자 명단 {injured.Count}명" + (rookies.Count > 0 ? $" · 퓨처스 신인 {rookies.Count}명" : "");
            label.EnableInClassList("futures-rookies--injured", injured.Count > 0);
            label.tooltip = string.Join("\n", injured.Select(p => $"{p.Name} · {PositionMatcher.Label(p.Position)} · {InjuryRules.BadgeText(season.InjuryGamesOf(p.Id))}")
                .Concat(rookies.Select(p => $"{p.Name} ({p.Memo}) · {PositionMatcher.Label(p.Position)} · {p.Age}세 · {StoveLeague.KeyStat(p)}")));

            var chips = Q("futures-chips");
            chips.Clear();
            SetVisible(chips, rookies.Count > 0 || injured.Count > 0);
            foreach (var p in injured.Take(FuturesChipsShown))
            {
                string id = p.Id;
                var chip = Cell($"{p.Name} {InjuryRules.ShortBadge(season.InjuryGamesOf(id))}", "futures-chip");
                chip.AddToClassList("futures-chip--injured");
                chip.name = "inj-" + id;
                chip.tooltip = $"{InjuryRules.LockedText(season.InjuryGamesOf(id))} · 누르면 선수 상세";
                chip.RegisterCallback<ClickEvent>(_ => OpenPlayer(id));
                chips.Add(chip);
            }
            // Phase 33: 신인 칩 (최근 지명 먼저, 부상 칩과 합쳐 최대 FuturesChipsShown개) — 누르면 선수 상세 (특별 훈련)
            foreach (var p in rookies.AsEnumerable().Reverse().Take(Math.Max(0, FuturesChipsShown - injured.Count)))
            {
                string id = p.Id;
                var chip = Cell($"{p.Name} {p.Position} {p.Age}", "futures-chip");
                chip.name = "fut-" + id;
                chip.tooltip = $"{p.Memo} · 에이징 폼 {AgingCurve.FormText(p.Form)} · 누르면 선수 상세";
                chip.RegisterCallback<ClickEvent>(_ => OpenPlayer(id));
                chips.Add(chip);
            }
        }

        /// <summary>퓨처스 신인 칩 최대 개수 (두 줄).</summary>
        public const int FuturesChipsShown = 6;

        /// <summary>
        /// 팀 케미스트리 문구. 발동 종류·멤버가 직전과 같으면 아무것도 바꾸지 않는다 (교체해도 결과가 같으면 다시 그리지 않음).
        /// </summary>
        /// <param name="known">태그를 알고 있는지. false면 "선수 데이터 필요" 안내.</param>
        void RenderChemistry(ActiveChemistry active, bool known)
        {
            Chemistry = active;
            string key = known ? ChemistryText.Key(active) : "unknown";
            if (key == _chemistryKey) return;
            _chemistryKey = key;
            ChemistryRenderCount++;
            var label = Q<Label>("chem-active");
            label.text = known ? ChemistryText.Summary(active) : "선수 데이터를 읽으면 판정합니다";
            label.EnableInClassList("chem-active--on", active != null && active.Kinds.Count > 0);
        }

        /// <summary>선발 로테이션(순서대로, 컨디션·오늘 선발)과 불펜 칩. 모두 누르면 선수 상세.</summary>
        void RenderRotation(TeamRoster roster, SeasonState season)
        {
            var container = Q("rotation");
            container.Clear();
            SetVisible(Q("rotation-title"), roster.Starters.Count > 0);
            SetVisible(Q("bullpen-title"), true);
            Q<Label>("bullpen-title").text = $"불펜 {roster.Bullpen.Count}명";
            var bullpen = Q("bullpen");
            bullpen.Clear();
            if (roster.Bullpen.Count == 0) bullpen.Add(Cell("없음", "bullpen-empty"));
            foreach (var p in roster.Bullpen)
            {
                string id = p.Id;
                int c = season.ConditionOf(id);
                var chip = new VisualElement { name = "bullpen-" + id, userData = id };
                chip.AddToClassList("bullpen-chip");
                chip.EnableInClassList("bullpen-chip--tired", c == PlayerCondition.Tired);
                chip.EnableInClassList("bullpen-chip--exhausted", c <= PlayerCondition.Exhausted);
                chip.Add(Cell($"{p.Name}({p.Position})", "bullpen-chip__name"));
                chip.tooltip = $"컨디션 {PlayerCondition.Label(c)} · 누르면 선수 상세";
                chip.RegisterCallback<ClickEvent>(_ => OpenPlayer(id));
                bullpen.Add(chip);
            }
            var today = RotationPicker.Pick(roster, season, season.LastStarterOf(roster.Team));
            foreach (var p in roster.Starters)
            {
                bool isToday = ReferenceEquals(p, today);
                string id = p.Id;
                var row = Row("lobby-row",
                    Cell(p.Order.ToString(CultureInfo.InvariantCulture), "col-order"),
                    Cell(p.Name, "col-name"),
                    ConditionCell(season.ConditionOf(p.Id)),
                    Cell(isToday ? "오늘 선발" : "ERA " + p.Era.ToString("0.00", CultureInfo.InvariantCulture), "col-hand"));
                row.name = $"rotation-{p.Order}";
                row.userData = p.Id;
                row.AddToClassList("lobby-row--compact");
                row.AddToClassList("lobby-row--clickable");
                row.EnableInClassList("lobby-row--today", isToday);
                row.RegisterCallback<ClickEvent>(_ => OpenPlayer(id));
                container.Add(row);
            }
        }

        /// <summary>컨디션 점 + 글자. 클래스 cond--1 ~ cond--5 (USS가 보통·좋음은 숨기고 위험·지침·최상만 보인다).</summary>
        static VisualElement ConditionCell(int condition)
        {
            int c = PlayerCondition.Clamp(condition);
            var cell = new VisualElement { userData = c };
            cell.AddToClassList("col-cond");
            cell.AddToClassList("cond--" + c);
            var dot = new VisualElement();
            dot.AddToClassList("cond-dot");
            var text = new Label(PlayerCondition.Label(c));
            text.AddToClassList("cond-text");
            cell.Add(dot);
            cell.Add(text);
            cell.tooltip = "컨디션 " + PlayerCondition.Label(c);
            return cell;
        }

        static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        void RenderDatabaseRoster(TeamRoster roster)
        {
            var lineup = Q("roster");
            lineup.Clear();
            foreach (var p in roster.Lineup)
                lineup.Add(SpotRow(LineupSpot.Batting(p.Order), p.Order.ToString(), p));

            var bench = Q("bench");
            bench.Clear();
            for (int i = 0; i < roster.Bench.Count; i++)
                bench.Add(SpotRow(LineupSpot.Bench(i), "", roster.Bench[i]));
        }

        /// <summary>
        /// 라인업 행: 순번 · 위치 · 이름 · 배지 · 컨디션 · 나이. 벤치 행은 순번 없이 (투타는 이름 툴팁).
        /// 누르면 선수 상세 (교체 대상을 고르는 중이면 교체). Phase 31부터 방출 버튼은 행에 없다.
        /// </summary>
        VisualElement SpotRow(LineupSpot spot, string order, PlayerData p)
        {
            var name = Cell(p.Name, "col-name");
            var row = spot.IsBench
                ? Row("lobby-row",
                    Cell(p.Position, "col-pos"),
                    name,
                    BadgeCell(TagOf(p)),
                    ConditionCell(_manager.Season.ConditionOf(p.Id)),
                    AgeCell(p))
                : Row("lobby-row",
                    Cell(order, "col-order"),
                    Cell(p.Position, "col-pos"),
                    name,
                    BadgeCell(TagOf(p)),
                    ConditionCell(_manager.Season.ConditionOf(p.Id)),
                    AgeCell(p));
            name.tooltip = ScenarioRosterSource.HandednessText(p);
            row.name = spot.IsBench ? $"bench-{spot.Index + 1}" : $"lineup-{spot.Index + 1}";
            row.userData = spot;
            row.AddToClassList("lobby-row--selectable");
            row.RegisterCallback<ClickEvent>(_ => OnRowClicked(spot));
            return row;
        }

        /// <summary>폼 표시 문턱 (±0.5%p 미만은 변동 없음으로 본다).</summary>
        const double FormShown = 0.005;

        /// <summary>
        /// 나이 칸 (Phase 29): 장부가 덧씌운 나이 "31세". 폼이 오르면 col-age--up, 내리면 col-age--down,
        /// 툴팁 "31세 · 에이징 폼 +3% (안타 확률)".
        /// </summary>
        public static Label AgeCell(PlayerData p)
        {
            var cell = Cell(p.Age > 0 ? p.Age + "세" : "—", "col-age");
            cell.name = "age-" + p.Id;
            cell.EnableInClassList("col-age--up", p.Form >= FormShown);
            cell.EnableInClassList("col-age--down", p.Form <= -FormShown);
            cell.tooltip = (p.Age > 0 ? $"{p.Age}세 · " : "") + $"에이징 폼 {AgingCurve.FormText(p.Form)} (안타 확률)";
            return cell;
        }

        /// <summary>
        /// 선수 방출 (선수 상세 모달의 [방출 확정]). 결과는 라인업 안내 줄에 보인다. 선발 타자였으면 그 타순을 누가 메웠는지도
        /// ("포수 자리: 이병헌").
        /// </summary>
        public ReleaseOutcome ReleasePlayer(string playerId)
        {
            if (_manager == null) return null;
            string name = _manager.Rosters?.Find(playerId).player?.Name ?? playerId;
            int slot = _manager.CurrentLineup?.Batting.Select(s => s.PlayerId).ToList().IndexOf(playerId) ?? -1;
            _selection.Clear();
            var outcome = _manager.Release(playerId);
            var filler = outcome.Released && slot >= 0 ? _manager.MyRoster?.AtOrder(slot + 1) : null;
            _notice = outcome.Released
                ? $"방출 · {name} → FA 시장 (위약금 {FranchiseFinance.BudgetDeltaText(-outcome.Penalty)})"
                  + (filler != null ? $" · {PositionMatcher.Label(filler.Position)} 자리: {filler.Name}" : "")
                : StoveLeague.ReleaseBlockText(outcome.Block);
            RenderSelection();
            return outcome;
        }

        /// <summary>
        /// 1:1 트레이드 제안 (선수 상세 모달에서 보낼 선수를 골랐을 때, Phase 34). 결과는 FA 모달의 트레이드 데스크에 남고,
        /// 성사되면 라인업 안내 줄에도 보인다. 선발 타자를 내줬으면 그 타순을 누가 메웠는지도 ("유격수 자리: 이재현").
        /// </summary>
        public TradeOutcome TradePlayer(string myPlayerId, string theirPlayerId)
        {
            if (_manager == null) return null;
            int slot = _manager.CurrentLineup?.Batting.Select(s => s.PlayerId).ToList().IndexOf(myPlayerId) ?? -1;
            _selection.Clear();
            var outcome = _manager.ProposeTrade(myPlayerId, theirPlayerId);
            if (outcome.Accepted)
            {
                var q = outcome.Quote;
                var filler = slot >= 0 ? _manager.MyRoster?.AtOrder(slot + 1) : null;
                _notice = $"트레이드 성사 · {q.Mine.Name} 내주고 {q.Theirs.Name} 영입 ({q.Partner}) · {q.Theirs.Name} {TradeDesk.ArrivalText(q.Arrival)} 합류"
                          + (filler != null ? $" · {PositionMatcher.Label(filler.Position)} 자리: {filler.Name}" : "");
            }
            _fa?.ReportTrade(outcome);
            RenderSelection();
            return outcome;
        }

        /// <summary>
        /// 1군 등록·말소 (선수 상세 모달에서 맞바꿀 선수를 골랐을 때, Phase 35). 되면 라인업 안내 줄에
        /// "1군 등록 · 김민준 (벤치) · 퓨처스 말소 · 소윤호"를 보인다. 막힌 이유는 모달이 보인다.
        /// </summary>
        public RosterMoveOutcome MovePlayer(string a, string b)
        {
            if (_manager == null) return null;
            _selection.Clear();
            var outcome = _manager.MoveRoster(a, b);
            if (outcome.Moved)
                _notice = $"1군 등록 · {outcome.Up.Name} ({(outcome.Up.IsPitcher ? "불펜" : "벤치")}) · 퓨처스 말소 · {outcome.Down.Name}";
            RenderSelection();
            return outcome;
        }

        ChemistryTag TagOf(PlayerData p) =>
            _manager.ChemistryTags != null && p?.Id != null && _manager.ChemistryTags.TryGetValue(p.Id, out var t) ? t : ChemistryTag.None;

        /// <summary>성향 배지 (주·수·장). 태그가 없으면 빈 자리만 차지한다 (열 맞춤).</summary>
        static Label BadgeCell(ChemistryTag tag)
        {
            var badge = Cell(ChemistryText.Badge(tag), "chem-badge");
            badge.AddToClassList(ChemistryText.BadgeClass(tag));
            badge.tooltip = ChemistryText.BadgeTooltip(tag);
            badge.userData = tag;
            return badge;
        }

        /// <summary>선택·교체 가능 표시를 USS 클래스로만 갱신하고 안내 문구를 바꾼다.</summary>
        void RenderSelection()
        {
            var selected = _selection.Selected;
            foreach (var row in Q("roster").Children().Concat(Q("bench").Children()))
            {
                if (!(row.userData is LineupSpot spot)) continue;
                row.EnableInClassList("lobby-row--selected", selected == spot);
                row.EnableInClassList("lobby-row--swappable", selected.HasValue && _card.CanSwap(selected.Value, spot));
            }

            string name = selected.HasValue ? NameAt(selected.Value) : null;
            string problem = _manager.RosterProblem;
            Q<Label>("lineup-hint").text = _manager.Season.InProgress != null ? ""
                : _notice != null ? _notice
                : problem != null ? problem
                : name == null ? "선수를 누르면 기록·컨디션을 보고 교체할 수 있습니다."
                : $"{name} 선택됨 · 바꿀 선수를 누르세요 (다시 누르면 취소).";
        }

        string NameAt(LineupSpot spot)
        {
            var id = _card.PlayerAt(spot);
            return id == null ? null : _manager.Rosters?.Find(id).player?.Name ?? _manager.Database?.FindPlayer(id)?.Name ?? id;
        }

        // ---------- 팀 통계 · 기록 1위 · 일정 · 순위 ----------

        void RenderTeamStats(SeasonState season)
        {
            var me = season.MyStanding;
            var l10 = season.MyRecentForm();
            Q<Label>("stat-rs").text = me?.RunsScored.ToString() ?? "—";
            Q<Label>("stat-ra").text = me?.RunsAllowed.ToString() ?? "—";
            var diff = Q<Label>("stat-diff");
            diff.text = me?.RunDifferentialText ?? "—";
            diff.EnableInClassList("stat-tile__value--plus", me != null && me.RunDifferential > 0);
            diff.EnableInClassList("stat-tile__value--minus", me != null && me.RunDifferential < 0);
            Q<Label>("stat-l10").text = l10.Text;
            Q<Label>("stat-note").text = StatNoteText(me, l10);
        }

        /// <summary>
        /// 팀 통계 보조 줄: 경기당 득·실점과 최근 전적 집계 경기 수
        /// (예: "41경기 · 경기당 득점 4.9 / 실점 4.3 · 최근 전적 3경기 기준").
        /// </summary>
        public static string StatNoteText(StandingRow me, RecentForm recent)
        {
            if (me == null || me.GamesPlayed == 0) return "";
            var inv = CultureInfo.InvariantCulture;
            string rs = ((double)me.RunsScored / me.GamesPlayed).ToString("0.0", inv);
            string ra = ((double)me.RunsAllowed / me.GamesPlayed).ToString("0.0", inv);
            string text = $"{me.GamesPlayed}경기 · 경기당 득점 {rs} / 실점 {ra}";
            return recent.Games < 10 ? $"{text} · 최근 전적 {recent.Games}경기 기준" : text;
        }

        /// <summary>팀 내 기록 1위 (지난 시즌 기록, 라인업 적용 선수단). 선수 DB가 없으면 비운다.</summary>
        void RenderLeaders()
        {
            var container = Q("team-leaders");
            container.Clear();
            var roster = _manager.MyRoster;
            if (roster == null)
            {
                container.Add(Cell(_manager.IsDatabaseReady ? "내 구단 선수 데이터가 없습니다." : "선수 데이터 불러오는 중…", "news-empty"));
                return;
            }
            foreach (var leader in RosterAnalyzer.Leaders(roster))
            {
                var row = Row("lobby-row",
                    Cell(leader.CategoryText, "col-leader-cat"),
                    Cell(leader.Player.Name, "col-leader-name"),
                    Cell(leader.Player.Position, "col-leader-role"),
                    Cell(leader.ValueText, "col-leader-value"));
                row.name = "leader-" + leader.Category;
                row.AddToClassList("lobby-row--leader");
                container.Add(row);
            }
        }

        /// <summary>작은 일정 목록: 번호 · 날짜 · 상대(홈 "vs", 원정 "@") · 상태. 구장은 툴팁.</summary>
        void RenderSchedule(SeasonState season)
        {
            var container = Q("schedule");
            container.Clear();
            var next = season.NextGame;
            var ps = season.Postseason;
            if (season.Phase != SeasonPhase.RegularSeason && ps != null)
            {
                Q<Label>("schedule-title").text = $"{PostseasonSeries.Title} 일정 · {ps.RecordText(season.MyTeam)}";
                RenderSeriesSchedule(season, ps, next);
                return;
            }
            Q<Label>("schedule-title").text = "일정";
            foreach (var g in season.Schedule)
            {
                string status = g.Status == GameStatus.Final ? FinalText(season, g)
                    : g.Status == GameStatus.InProgress ? "진행 중"
                    : g == next ? "다음 경기"
                    : "예정";
                bool home = g.HomeTeam == season.MyTeam;
                string opponent = home ? g.AwayTeam : g.HomeTeam;
                var row = Row("lobby-row",
                    Cell(g.Number.ToString(), "col-no"),
                    Cell(g.Date, "col-date"),
                    Cell((home ? "vs " : "@ ") + opponent, "col-opp"),
                    Cell(status, "col-status"));
                row.name = "game-" + g.Number;
                row.tooltip = $"{g.MatchupText} · {g.Stadium}";
                row.AddToClassList("lobby-row--schedule");
                row.EnableInClassList("lobby-row--final", g.Status == GameStatus.Final);
                row.EnableInClassList("lobby-row--next", g == next);
                container.Add(row);
            }
        }

        /// <summary>한국시리즈 일정 행 (ks-1 …): 차전 · 날짜 · 대진(내 구단 기준 vs/@, 아니면 원정 @ 홈) · 결과(승리 팀 약칭 + 점수).</summary>
        void RenderSeriesSchedule(SeasonState season, PostseasonSeries ps, ScheduledGame next)
        {
            var container = Q("schedule");
            foreach (var g in ps.Games)
            {
                bool mine = ps.Involves(season.MyTeam);
                bool home = g.HomeTeam == season.MyTeam;
                string matchup = mine ? (home ? "vs " : "@ ") + (home ? g.AwayTeam : g.HomeTeam)
                    : $"{Short(g.AwayTeam)} @ {Short(g.HomeTeam)}";
                string status = g.Status == GameStatus.Final
                    ? (mine ? FinalText(season, g) : WinnerText(g))
                    : g.Status == GameStatus.InProgress ? "진행 중" : g == next ? "다음 경기" : "예정";
                var row = Row("lobby-row",
                    Cell(g.Number + "차", "col-no"),
                    Cell(g.Date, "col-date"),
                    Cell(matchup, "col-opp"),
                    Cell(status, "col-status"));
                row.name = "ks-" + g.Number;
                row.tooltip = $"한국시리즈 {g.Number}차전 · {g.MatchupText} · {g.Stadium}";
                row.AddToClassList("lobby-row--schedule");
                row.EnableInClassList("lobby-row--final", g.Status == GameStatus.Final);
                row.EnableInClassList("lobby-row--next", g == next);
                container.Add(row);
            }
        }

        static string Short(string team) => LobbyBriefing.TeamMark(team);

        /// <summary>내 구단 경기가 아닌 시리즈 경기 결과: "KIA 5:3" (무승부는 "무 3:3").</summary>
        static string WinnerText(ScheduledGame g)
        {
            var s = g.Score.Value;
            if (s.Away == s.Home) return $"무 {s}";
            return $"{Short(s.Away > s.Home ? g.AwayTeam : g.HomeTeam)} {Math.Max(s.Away, s.Home)}:{Math.Min(s.Away, s.Home)}";
        }

        void RenderStandings(SeasonState season)
        {
            var container = Q("standings");
            container.Clear();
            foreach (var s in season.Standings)
            {
                var move = Cell(s.RankChangeText, "col-move");
                move.EnableInClassList("col-move--up", s.RankChange > 0);
                move.EnableInClassList("col-move--down", s.RankChange < 0);
                var row = Row("lobby-row",
                    Cell(s.Rank.ToString(), "col-rank"),
                    move,
                    Cell(s.Team, "col-standing-team"),
                    Cell(s.Wins.ToString(), "col-num"),
                    Cell(s.Losses.ToString(), "col-num"),
                    Cell(s.Draws.ToString(), "col-num"),
                    Cell(s.WinPctText, "col-pct"),
                    Cell(s.GamesBehind, "col-gb"));
                row.AddToClassList("lobby-row--standing");
                row.EnableInClassList("lobby-row--mine", s.Team == season.MyTeam);
                container.Add(row);
            }
        }

        /// <summary>
        /// 하단 지난 경기 요약: 경기 번호·대진·내 구단 기준 결과와 그 경기로 바뀐 내 순위
        /// (예: "경기 1 · 서울 블루윙스 vs 부산 웨이브스 · 승 2:3 · 3위 → 2위"). 아직 치른 경기가 없으면 "—".
        /// </summary>
        public static string LastResultText(SeasonState season)
        {
            // 한국시리즈 (Phase 32): 내 구단이 나갔으면 "한국시리즈 3차전 · 승 2:5 · 삼성 2승 - 1승 LG", 아니면 "한국시리즈 · KIA 4승 - 2승 LG".
            var ps = season.Postseason;
            var lastKs = ps?.LastPlayed;
            if (lastKs != null)
            {
                string series = ps.Involves(season.MyTeam)
                    ? $"한국시리즈 {lastKs.Number}차전 · {FinalText(season, lastKs)} · {ps.RecordText(season.MyTeam)}"
                    : $"한국시리즈 · {ps.RecordText()}";
                return !ps.IsOver ? series
                    : ps.Champion == season.MyTeam ? series + " · 우승!"
                    : ps.Involves(season.MyTeam) ? series + " · 준우승"
                    : $"{series} · {Short(ps.Champion)} 우승";
            }
            var last = season.LastFinished;
            if (last == null) return "—";
            string text = $"경기 {last.Number} · {last.MatchupText} · {FinalText(season, last)}";
            var me = season.MyStanding;
            if (me == null || me.PreviousRank == 0) return text;
            return me.PreviousRank == me.Rank ? $"{text} · {me.Rank}위 유지" : $"{text} · {me.PreviousRank}위 → {me.Rank}위";
        }

        /// <summary>종료 경기 상태 칸: 내 구단 기준 승/패/무 + 원정:홈 점수 (예: "패 3:2").</summary>
        static string FinalText(SeasonState season, ScheduledGame g)
        {
            var outcome = season.MyOutcome(g);
            if (!outcome.HasValue) return "종료";
            string word = outcome == GameOutcome.Win ? "승" : outcome == GameOutcome.Loss ? "패" : "무";
            return $"{word} {g.Score.Value}";
        }

        T Q<T>(string name) where T : VisualElement => _root.Q<T>(name);

        /// <summary>헤더 프런트 오피스 (예산·팬 만족도·시즌 진행·신뢰도). 직전 경기 변화는 이번 실행에서 치른 경기만.</summary>
        void RenderFrontOffice(SeasonState season)
        {
            var report = _manager.LastReport;
            var budget = Q<Label>("fo-budget");
            budget.text = FranchiseFinance.BudgetText(season.Budget);
            budget.tooltip = FranchiseFinance.ExactText(season.Budget);
            var delta = Q<Label>("fo-budget-delta");
            delta.text = report == null
                ? $"승 {FranchiseFinance.BudgetDeltaText(FranchiseFinance.WinIncome)} · 유지비 {FranchiseFinance.BudgetDeltaText(-FranchiseFinance.PayrollPerGame(season.Payroll))}"
                : $"직전 경기({OutcomeText(report.MyOutcome)}) 순이익 {FranchiseFinance.BudgetDeltaText(report.NetIncome)}";
            delta.tooltip = report == null
                ? $"경기마다 관중 수입이 들어오고 선수단 유지비(페이롤 {FranchiseFinance.BudgetText(season.Payroll)} ÷ {FranchiseFinance.GamesPerSeason})가 나갑니다."
                : report.FinanceText;

            int fan = season.FanApproval;
            var mood = FranchiseFinance.MoodOf(fan);
            var fill = Q<VisualElement>("fo-fan-fill");
            fill.style.width = Length.Percent(fan);
            fill.EnableInClassList("fo-fill--happy", mood == FanMood.Happy);
            fill.EnableInClassList("fo-fill--angry", mood == FanMood.Angry);
            var fanLabel = Q<Label>("fo-fan");
            fanLabel.text = fan + "%";
            fanLabel.EnableInClassList("fo-percent--happy", mood == FanMood.Happy);
            fanLabel.EnableInClassList("fo-percent--angry", mood == FanMood.Angry);
            Q<Label>("fo-fan-delta").text = report == null
                ? $"승 {FranchiseFinance.FanDeltaText(FranchiseFinance.WinFan)} · 패 {FranchiseFinance.FanDeltaText(FranchiseFinance.LossFan)}"
                : $"직전 경기({OutcomeText(report.MyOutcome)}) {FranchiseFinance.FanDeltaText(report.Finance.FanDelta)}";

            RenderTrustAndSlots(season);

            int played = season.GamesPlayed, total = season.Schedule.Count;
            Q<VisualElement>("fo-season-fill").style.width = Length.Percent(total == 0 ? 0 : 100f * played / total);
            Q<Label>("fo-season").text = $"{played} / {total}경기";
            var next = season.NextGame;
            Q<Label>("fo-season-next").text = season.Phase == SeasonPhase.PostSeason ? $"한국시리즈 {season.Postseason?.RecordText(season.MyTeam)}"
                : next == null ? "시즌 종료" : $"다음 경기일 {next.Date} · 남은 {total - played}경기";
        }

        /// <summary>프런트 신뢰도 TP 게이지(0~200)와 팬덤 특전 슬롯 해금/잠김.</summary>
        void RenderTrustAndSlots(SeasonState season)
        {
            int trust = season.TrustPoints;
            Q<VisualElement>("fo-trust-fill").style.width = Length.Percent(100f * trust / FrontOfficeTrust.MaxTrust);
            Q<Label>("fo-trust").text = FrontOfficeTrust.TrustText(trust);
            Q("fo-trust-gauge").tooltip = FrontOfficeTrust.CeilingText(trust);
            foreach (var slot in FandomRules.Slots)
            {
                bool unlocked = (season.UnlockedSlots & slot) != 0;
                var chip = Q<Label>("fo-slot-" + slot.ToString().ToLowerInvariant());
                chip.EnableInClassList("fo-slot--unlocked", unlocked);
                chip.EnableInClassList("fo-slot--locked", !unlocked);
                chip.tooltip = (unlocked ? "해금됨 · " : "잠김 · ") + FandomRules.Hint(slot);
            }
        }

        static string OutcomeText(GameOutcome outcome) =>
            outcome == GameOutcome.Win ? "승" : outcome == GameOutcome.Loss ? "패" : "무";
        VisualElement Q(string name) => _root.Q(name);

        static VisualElement Row(string cls, params VisualElement[] cells)
        {
            var row = new VisualElement();
            row.AddToClassList(cls);
            foreach (var c in cells) row.Add(c);
            return row;
        }

        static Label Cell(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }
    }
}
