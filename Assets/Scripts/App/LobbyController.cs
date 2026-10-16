using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.App.Data;
using DailyFranchise.Sim.Data;
using DailyFranchise.UI.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.App
{
    /// <summary>
    /// 1920×1080 로비. SeasonManager의 시즌 상태를 읽어 일정·순위·라인업을 표시하고,
    /// [다음 경기 시작]으로 경기 화면 진입을 요청한다. 경기 화면을 직접 참조하지 않는다.
    /// 선수 DB를 읽는 동안에는 시작 버튼을 잠그고, 다 읽으면 다시 그린다.
    /// 좌측 라인업은 Click-to-Swap으로 바꾼다: 첫 클릭으로 선수를 고르고(강조), 두 번째 클릭한 선수와 맞바꾼다.
    /// 바꾼 라인업은 SeasonManager가 시즌에 반영·저장하고, 여기서는 다시 그리기만 한다.
    /// 선택 상태 표시는 USS 클래스(lobby-row--selected / --swappable)로만 바꾼다.
    /// 중앙 상단 대시보드: 내 구단 시즌 득점·실점·득실차·최근 10경기(SeasonState, 경기마다 PostGameProcessor가 갱신)와
    /// 팀 내 기록 1위(RosterAnalyzer, 라인업을 적용한 선수단의 DB 기록 기준 — 라인업을 바꾸면 다시 뽑는다).
    /// 순위표는 SeasonState가 정렬한 순서(승률 → 다승 → 무승부 → 잔여 경기)대로 그리고, 직전 경기일 대비 순위 변동을 함께 보인다.
    /// 헤더 프런트 오피스(Phase 21): 구단 예산·팬 만족도 게이지(80% 이상 초록, 30% 미만 빨강)·시즌 진행도와
    /// 직전 경기 재정 변화, 왼쪽 위에 현실 날짜(<see cref="Clock"/>).
    /// 선수 컨디션(Phase 22): 라인업·벤치 행마다 컨디션 점·글자(5 파랑 · 4/3 초록 · 2 노랑 · 1 빨강), 그 아래 선발 로테이션
    /// (직전 선발 다음 순번 중 컨디션 보통 이상인 투수가 "오늘 선발", <see cref="RotationPicker"/>).
    /// 흥행 시스템(Phase 24): 프런트 오피스 넷째 칸에 신뢰도 TP 게이지와 팬덤 특전 슬롯(해금/잠김),
    /// 라인업 패널 위에 팀 케미스트리(<see cref="ChemistryRules.Evaluate"/>)와 행마다 성향 배지(주·수·장).
    /// 케미스트리는 라인업을 그릴 때(진입·DB 로드·교체 이벤트)만 판정하고, 결과가 바뀐 경우에만 문구를 바꾼다.
    /// 스토브리그(Phase 25): 하단 [스카우트 / FA]로 FA 시장 모달(<see cref="FaMarketView"/>)을 연다 (선수 DB가 있고 경기 중이 아닐 때).
    /// 벤치가 5명까지 늘 수 있어 투수진(선발 로테이션·불펜)은 순위표 아래에 그린다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyController : MonoBehaviour
    {
        VisualElement _root;
        Button _start;
        Button _mode;
        Button _scout;
        FaMarketView _fa;
        SeasonManager _manager;
        readonly LineupSelection _selection = new LineupSelection();

        /// <summary>오늘 날짜 (현실 시간). 테스트는 고정 날짜를 넣는다.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.Now;
        /// <summary>지금 그린 라인업 카드. 선수 DB가 없으면 null (교체 불가).</summary>
        LineupCard _card;

        /// <summary>FA 시장 모달 (로비가 켜져 있을 때만).</summary>
        public FaMarketView FaMarket => _fa;

        /// <summary>현재 라인업 선택 상태 (테스트·진단용).</summary>
        public LineupSelection Selection => _selection;

        /// <summary>마지막으로 그린 케미스트리 열쇠 (<see cref="ChemistryText.Key"/>). null이면 아직 안 그림.</summary>
        string _chemistryKey;

        /// <summary>케미스트리 문구를 실제로 바꾼 횟수 (결과가 같으면 늘지 않는다 — 테스트·진단용).</summary>
        public int ChemistryRenderCount { get; private set; }

        /// <summary>지금 표시 중인 내 구단 케미스트리. 선수 DB가 없으면 null.</summary>
        public ActiveChemistry Chemistry { get; private set; }

        void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _start = _root.Q<Button>("start-game");
            _start.clicked += OnStartClicked;
            _mode = _root.Q<Button>("game-mode");
            _mode.clicked += OnModeClicked;
            _scout = _root.Q<Button>("scout-fa");
            _scout.clicked += OnScoutClicked;
            _manager = SeasonManager.EnsureExists();
            _fa = new FaMarketView(_root, _manager);
            _manager.ModeChanged += Render;
            _manager.DatabaseReady += OnDatabaseReady;
            _manager.LineupChanged += OnLineupChanged;
            _manager.StoveChanged += OnStoveChanged;
            _selection.Clear();
            _chemistryKey = null;
            Render();
        }

        void OnDisable()
        {
            if (_start != null) _start.clicked -= OnStartClicked;
            if (_mode != null) _mode.clicked -= OnModeClicked;
            if (_scout != null) _scout.clicked -= OnScoutClicked;
            if (_manager != null)
            {
                _manager.ModeChanged -= Render;
                _manager.DatabaseReady -= OnDatabaseReady;
                _manager.LineupChanged -= OnLineupChanged;
                _manager.StoveChanged -= OnStoveChanged;
            }
            _fa?.Detach();
            _fa = null;
            _start = null;
            _mode = null;
            _scout = null;
            _manager = null;
        }

        void OnDatabaseReady() => Render();

        void OnModeClicked() => _manager?.ToggleFullInningMode();

        /// <summary>[스카우트 / FA]: FA 시장 모달을 연다. 선수 DB가 없거나 경기 중이면 무시한다.</summary>
        public void OpenFaMarket()
        {
            if (_manager == null || !_manager.IsDatabaseReady || _manager.Season.InProgress != null) return;
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
            RenderRoster(_manager.Season);
            RenderLeaders();
        }

        /// <summary>
        /// 라인업 자리 클릭. 선수 DB 라인업이 없거나 경기 중이면 무시한다.
        /// 두 번째 클릭이 맞바꿀 수 있는 자리면 SeasonManager에 교체를 요청한다 (성공하면 LineupChanged로 다시 그림).
        /// </summary>
        public void ClickLineupSpot(LineupSpot spot)
        {
            if (_card == null || !_card.Contains(spot) || _manager.Season.InProgress != null) return;
            var result = _selection.Click(spot, _card.CanSwap);
            if (result == LineupClick.Swap)
            {
                var (first, second) = _selection.PendingSwap.Value;
                if (_manager.SwapLineup(first, second)) return;
            }
            RenderSelection();
        }

        void OnStartClicked()
        {
            var manager = SeasonManager.Instance;
            if (manager == null) return;
            // 씬 로드 전에 두 번 눌리지 않도록 즉시 잠근다.
            if (manager.StartNextGame()) _start.SetEnabled(false);
        }

        void Render()
        {
            var season = _manager.Season;
            Q<Label>("season-title").text = season.SeasonTitle;
            Q<Label>("my-team").text = season.MyTeam;
            Q<Label>("my-record").text = RecordText(season);

            RenderFrontOffice(season);
            RenderRoster(season);
            RenderTeamStats(season);
            RenderLeaders();
            RenderSchedule(season);
            RenderStandings(season);

            Q<Label>("last-result").text = LastResultText(season);

            var next = season.NextGame;
            Q<Label>("next-game").text = next == null
                ? "시즌 일정이 모두 끝났습니다."
                : $"경기 {next.Number} · {next.Date} · {next.MatchupText} · {next.Stadium}";
            _start.text = next == null ? "시즌 종료" : !_manager.IsDatabaseReady ? "선수 데이터 불러오는 중…" : "다음 경기 시작";
            _start.SetEnabled(next != null && season.InProgress == null && _manager.IsDatabaseReady);
            _scout.SetEnabled(_manager.IsDatabaseReady && season.InProgress == null);
            _mode.text = _manager.FullInningMode ? "진행: 풀 이닝 (1회부터)" : "진행: 퀵 시뮬 (8회부터)";
            _mode.EnableInClassList("btn--active", _manager.FullInningMode);
        }

        static string RecordText(SeasonState season)
        {
            var me = season.MyStanding;
            return me == null
                ? "—"
                : $"{me.Rank}위 · {me.Wins}승 {me.Losses}패 {me.Draws}무 · 승률 {me.WinPctText} · 진행 {season.GamesPlayed} / {season.Schedule.Count}경기";
        }

        void RenderRoster(SeasonState season)
        {
            var db = _manager.Database;
            var mine = _manager.MyRoster;
            _card = _manager.CurrentLineup;
            if (mine != null && _card != null)
            {
                RenderChemistry(ChemistryRules.Evaluate(mine, _manager.ChemistryTags), _manager.ChemistryTags != null);
                RenderDatabaseRoster(mine);
                RenderRotation(mine, season);
                Q<Label>("roster-source").text = $"표시 데이터: {MasterDatabase.RosterFileName} ({db.Season})";
                RenderSelection();
                return;
            }

            // 선수 DB가 없거나 아직 읽는 중이면 경기 화면과 같은 더미 선수 데이터를 쓴다 (교체 불가).
            RenderChemistry(null, false);
            _card = null;
            _selection.Clear();
            Q("rotation").Clear();
            SetVisible(Q("rotation-title"), false);
            SetVisible(Q("bullpen-title"), false);
            Q<Label>("bullpen").text = "";
            var roster = new DummyScenarioData();
            FillDummy(Q("roster"), roster, roster.GetLineup(roster.MyTeam));
            FillDummy(Q("bench"), roster, roster.GetBench(roster.MyTeam));
            Q<Label>("roster-source").text = "표시 데이터: 가상 더미";
            Q<Label>("lineup-hint").text = _manager.IsDatabaseReady ? "라인업 교체는 선수 데이터가 있을 때만 할 수 있습니다." : "";
        }

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

        static void FillDummy(VisualElement container, DummyScenarioData roster, IEnumerable<RosterEntry> entries)
        {
            container.Clear();
            foreach (var entry in entries)
            {
                var p = roster.GetPlayer(entry.PlayerId);
                container.Add(Row("lobby-row",
                    Cell(entry.Order?.ToString() ?? "", "col-order"),
                    Cell(entry.Role, "col-pos"),
                    Cell(p.Name, "col-name"),
                    Cell("", "col-cond"),
                    Cell(p.Handedness, "col-hand")));
            }
        }

        /// <summary>선발 로테이션: 로테이션 순서대로, 컨디션과 오늘 선발 표시.</summary>
        void RenderRotation(TeamRoster roster, SeasonState season)
        {
            var container = Q("rotation");
            container.Clear();
            SetVisible(Q("rotation-title"), roster.Starters.Count > 0);
            SetVisible(Q("bullpen-title"), true);
            Q<Label>("bullpen-title").text = $"불펜 {roster.Bullpen.Count}명";
            Q<Label>("bullpen").text = roster.Bullpen.Count == 0 ? "없음"
                : string.Join(" · ", roster.Bullpen.Select(p => $"{p.Name}({p.Position})"));
            var today = RotationPicker.Pick(roster, season, season.LastStarterOf(roster.Team));
            foreach (var p in roster.Starters)
            {
                bool isToday = ReferenceEquals(p, today);
                var row = Row("lobby-row",
                    Cell(p.Order.ToString(CultureInfo.InvariantCulture), "col-order"),
                    Cell(p.Name, "col-name"),
                    ConditionCell(season.ConditionOf(p.Id)),
                    Cell(isToday ? "오늘 선발" : "ERA " + p.Era.ToString("0.00", CultureInfo.InvariantCulture), "col-hand"));
                row.name = $"rotation-{p.Order}";
                row.userData = p.Id;
                row.AddToClassList("lobby-row--compact");
                row.EnableInClassList("lobby-row--today", isToday);
                container.Add(row);
            }
        }

        /// <summary>컨디션 점 + 글자 ("최상"). 클래스 cond--1 ~ cond--5로 색을 바꾼다.</summary>
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

        VisualElement SpotRow(LineupSpot spot, string order, PlayerData p)
        {
            var row = Row("lobby-row",
                Cell(order, "col-order"),
                Cell(p.Position, "col-pos"),
                Cell(p.Name, "col-name"),
                BadgeCell(TagOf(p)),
                ConditionCell(_manager.Season.ConditionOf(p.Id)),
                Cell(ScenarioRosterSource.HandednessText(p), "col-hand"));
            row.name = spot.IsBench ? $"bench-{spot.Index + 1}" : $"lineup-{spot.Index + 1}";
            row.userData = spot;
            row.AddToClassList("lobby-row--selectable");
            row.RegisterCallback<ClickEvent>(_ => ClickLineupSpot(spot));
            return row;
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
            Q<Label>("lineup-hint").text = _manager.Season.InProgress != null ? ""
                : name == null ? "선수를 누른 뒤 바꿀 타순 또는 벤치 선수를 누르면 자리를 맞바꿉니다."
                : $"{name} 선택됨 · 바꿀 선수를 누르세요 (다시 누르면 취소).";
        }

        string NameAt(LineupSpot spot)
        {
            var id = _card.PlayerAt(spot);
            return id == null ? null : _manager.Database.FindPlayer(id)?.Name ?? id;
        }

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

        /// <summary>팀 내 기록 1위. 선수 DB가 없으면(읽는 중 포함) 안내 문구만 보인다.</summary>
        void RenderLeaders()
        {
            var container = Q("team-leaders");
            container.Clear();
            var roster = _manager.MyRoster;
            if (roster == null)
            {
                Q<Label>("leaders-source").text = _manager.IsDatabaseReady ? "내 구단 선수 데이터가 없습니다." : "선수 데이터 불러오는 중…";
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
                container.Add(row);
            }
            Q<Label>("leaders-source").text = $"기준: {MasterDatabase.RosterFileName} 시즌 기록 · 타자는 선발 라인업만";
        }

        void RenderSchedule(SeasonState season)
        {
            var container = Q("schedule");
            container.Clear();
            var next = season.NextGame;
            foreach (var g in season.Schedule)
            {
                string status = g.Status == GameStatus.Final ? FinalText(season, g)
                    : g.Status == GameStatus.InProgress ? "진행 중"
                    : g == next ? "다음 경기"
                    : "예정";
                var row = Row("lobby-row",
                    Cell(g.Number.ToString(), "col-no"),
                    Cell(g.Date, "col-date"),
                    Cell(g.AwayTeam, "col-team"),
                    Cell("vs", "col-vs"),
                    Cell(g.HomeTeam, "col-team"),
                    Cell(g.Stadium, "col-stadium"),
                    Cell(status, "col-status"));
                row.name = "game-" + g.Number;
                row.EnableInClassList("lobby-row--final", g.Status == GameStatus.Final);
                row.EnableInClassList("lobby-row--next", g == next);
                container.Add(row);
            }
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

        /// <summary>헤더: 오늘 날짜와 프런트 오피스 (예산·팬 만족도·시즌 진행). 직전 경기 변화는 이번 실행에서 치른 경기만.</summary>
        void RenderFrontOffice(SeasonState season)
        {
            Q<Label>("today").text = "오늘 " + FranchiseFinance.TodayText(Clock());

            var report = _manager.LastReport;
            Q<Label>("fo-budget").text = FranchiseFinance.BudgetText(season.Budget);
            Q<Label>("fo-budget-delta").text = report == null
                ? $"승 {FranchiseFinance.BudgetDeltaText(FranchiseFinance.WinIncome)} · 패 {FranchiseFinance.BudgetDeltaText(FranchiseFinance.LossIncome)}"
                : $"직전 경기({OutcomeText(report.MyOutcome)}) 관중 수입 {FranchiseFinance.BudgetDeltaText(report.Finance.BudgetDelta)}";

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
            Q<Label>("fo-season-next").text = next == null ? "시즌 종료" : $"다음 경기일 {next.Date} · 남은 {total - played}경기";
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
