using DailyFranchise.UI.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.App
{
    /// <summary>
    /// 1920×1080 로비. SeasonManager의 시즌 상태를 읽어 일정·순위·라인업을 표시하고,
    /// [다음 경기 시작]으로 경기 화면 진입을 요청한다. 경기 화면을 직접 참조하지 않는다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyController : MonoBehaviour
    {
        VisualElement _root;
        Button _start;

        void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _start = _root.Q<Button>("start-game");
            _start.clicked += OnStartClicked;
            Render(SeasonManager.EnsureExists().Season);
        }

        void OnDisable()
        {
            if (_start != null) _start.clicked -= OnStartClicked;
            _start = null;
        }

        void OnStartClicked()
        {
            var manager = SeasonManager.Instance;
            if (manager == null) return;
            // 씬 로드 전에 두 번 눌리지 않도록 즉시 잠근다.
            if (manager.StartNextGame()) _start.SetEnabled(false);
        }

        void Render(SeasonState season)
        {
            Q<Label>("season-title").text = season.SeasonTitle;
            Q<Label>("my-team").text = season.MyTeam;
            var me = season.MyStanding;
            Q<Label>("my-record").text = me == null
                ? "—"
                : $"{me.Rank}위 · {me.Wins}승 {me.Losses}패 {me.Draws}무 · 승률 {me.WinPctText} · 진행 {season.GamesPlayed} / {season.Schedule.Count}경기";

            RenderRoster();
            RenderSchedule(season);
            RenderStandings(season);

            var next = season.NextGame;
            Q<Label>("next-game").text = next == null
                ? "시즌 일정이 모두 끝났습니다."
                : $"경기 {next.Number} · {next.Date} · {next.MatchupText} · {next.Stadium}";
            _start.text = next == null ? "시즌 종료" : "다음 경기 시작";
            _start.SetEnabled(next != null && season.InProgress == null);
        }

        void RenderRoster()
        {
            // 경기 화면과 같은 더미 선수 데이터를 쓴다 (내 구단 = 홈 부산 웨이브스).
            var roster = new DummyScenarioData();
            var container = Q("roster");
            container.Clear();
            foreach (var entry in roster.GetLineup(roster.MyTeam))
            {
                var p = roster.GetPlayer(entry.PlayerId);
                container.Add(Row("lobby-row",
                    Cell(entry.Order?.ToString() ?? "", "col-order"),
                    Cell(entry.Role, "col-pos"),
                    Cell(p.Name, "col-name"),
                    Cell(p.Handedness, "col-hand")));
            }
            Q<Label>("roster-source").text = "표시 데이터: 가상 더미";
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
                var row = Row("lobby-row",
                    Cell(s.Rank.ToString(), "col-rank"),
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
