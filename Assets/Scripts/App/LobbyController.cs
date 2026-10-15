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
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyController : MonoBehaviour
    {
        VisualElement _root;
        Button _start;
        Button _mode;
        SeasonManager _manager;
        readonly LineupSelection _selection = new LineupSelection();
        /// <summary>지금 그린 라인업 카드. 선수 DB가 없으면 null (교체 불가).</summary>
        LineupCard _card;

        /// <summary>현재 라인업 선택 상태 (테스트·진단용).</summary>
        public LineupSelection Selection => _selection;

        void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _start = _root.Q<Button>("start-game");
            _start.clicked += OnStartClicked;
            _mode = _root.Q<Button>("game-mode");
            _mode.clicked += OnModeClicked;
            _manager = SeasonManager.EnsureExists();
            _manager.ModeChanged += Render;
            _manager.DatabaseReady += OnDatabaseReady;
            _manager.LineupChanged += OnLineupChanged;
            _selection.Clear();
            Render();
        }

        void OnDisable()
        {
            if (_start != null) _start.clicked -= OnStartClicked;
            if (_mode != null) _mode.clicked -= OnModeClicked;
            if (_manager != null)
            {
                _manager.ModeChanged -= Render;
                _manager.DatabaseReady -= OnDatabaseReady;
                _manager.LineupChanged -= OnLineupChanged;
            }
            _start = null;
            _mode = null;
            _manager = null;
        }

        void OnDatabaseReady() => Render();

        void OnModeClicked() => _manager?.ToggleFullInningMode();

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
            var me = season.MyStanding;
            Q<Label>("my-record").text = me == null
                ? "—"
                : $"{me.Rank}위 · {me.Wins}승 {me.Losses}패 {me.Draws}무 · 승률 {me.WinPctText} · 진행 {season.GamesPlayed} / {season.Schedule.Count}경기";

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
            _mode.text = _manager.FullInningMode ? "진행: 풀 이닝 (1회부터)" : "진행: 퀵 시뮬 (8회부터)";
            _mode.EnableInClassList("btn--active", _manager.FullInningMode);
        }

        void RenderRoster(SeasonState season)
        {
            var db = _manager.Database;
            var mine = _manager.MyRoster;
            _card = _manager.CurrentLineup;
            if (mine != null && _card != null)
            {
                RenderDatabaseRoster(mine);
                Q<Label>("roster-source").text = $"표시 데이터: {MasterDatabase.RosterFileName} ({db.Season})";
                RenderSelection();
                return;
            }

            // 선수 DB가 없거나 아직 읽는 중이면 경기 화면과 같은 더미 선수 데이터를 쓴다 (교체 불가).
            _card = null;
            _selection.Clear();
            var roster = new DummyScenarioData();
            FillDummy(Q("roster"), roster, roster.GetLineup(roster.MyTeam));
            FillDummy(Q("bench"), roster, roster.GetBench(roster.MyTeam));
            Q<Label>("roster-source").text = "표시 데이터: 가상 더미";
            Q<Label>("lineup-hint").text = _manager.IsDatabaseReady ? "라인업 교체는 선수 데이터가 있을 때만 할 수 있습니다." : "";
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
                    Cell(p.Handedness, "col-hand")));
            }
        }

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
                Cell(ScenarioRosterSource.HandednessText(p), "col-hand"));
            row.name = spot.IsBench ? $"bench-{spot.Index + 1}" : $"lineup-{spot.Index + 1}";
            row.userData = spot;
            row.AddToClassList("lobby-row--selectable");
            row.RegisterCallback<ClickEvent>(_ => ClickLineupSpot(spot));
            return row;
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
