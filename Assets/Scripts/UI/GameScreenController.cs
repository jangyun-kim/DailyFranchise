using System.Collections.Generic;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 1920×1080 경기 화면. Fixture8thBottom의 투구를 읽기 전용으로 표시한다.
    /// Phase 2 범위: 경기 상태를 바꾸는 입력은 없고, 디버그 버튼으로 표시할 투구만 바꾼다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameScreenController : MonoBehaviour
    {
        const int MinInnings = 9;
        const int MaxLogEntries = 5;
        const string Unknown = "–";

        ScenarioFixture _fixture;
        int _index;

        AbsZoneView _abs;
        Label _awayScore, _homeScore, _statusInning, _statusCount;
        VisualElement _lineScore;
        Label _headlineMain, _headlineSub;
        Label _runner1, _runner2, _runner3, _playResult, _playRuns, _playOuts;
        VisualElement _base1, _base2, _base3;
        Label _pitcherName, _pitcherTeam, _pitcherLine1, _pitcherLine2;
        Label _batterName, _batterTeam, _batterLine1, _batterLine2;
        VisualElement _log;
        Label _debugIndex;

        public int PitchCount => _fixture?.Events.Count ?? 0;
        public int PitchIndex => _index;

        void OnEnable()
        {
            _fixture = Fixture8thBottom.Create();
            var root = GetComponent<UIDocument>().rootVisualElement;
            Bind(root);
            ShowPitch(0);
        }

        public void ShowPitch(int index)
        {
            if (_fixture == null) return;
            _index = Mathf.Clamp(index, 0, PitchCount - 1);
            Render();
        }

        void Bind(VisualElement root)
        {
            _abs = new AbsZoneView(root);

            _awayScore = root.Q<Label>("away-score");
            _homeScore = root.Q<Label>("home-score");
            _statusInning = root.Q<Label>("status-inning");
            _statusCount = root.Q<Label>("status-count");
            _lineScore = root.Q("linescore");

            _headlineMain = root.Q<Label>("headline-main");
            _headlineSub = root.Q<Label>("headline-sub");

            _runner1 = root.Q<Label>("runner-1");
            _runner2 = root.Q<Label>("runner-2");
            _runner3 = root.Q<Label>("runner-3");
            _base1 = root.Q("base-1");
            _base2 = root.Q("base-2");
            _base3 = root.Q("base-3");
            _playResult = root.Q<Label>("play-result");
            _playRuns = root.Q<Label>("play-runs");
            _playOuts = root.Q<Label>("play-outs");

            _pitcherName = root.Q<Label>("pitcher-name");
            _pitcherTeam = root.Q<Label>("pitcher-team");
            _pitcherLine1 = root.Q<Label>("pitcher-line1");
            _pitcherLine2 = root.Q<Label>("pitcher-line2");
            _batterName = root.Q<Label>("batter-name");
            _batterTeam = root.Q<Label>("batter-team");
            _batterLine1 = root.Q<Label>("batter-line1");
            _batterLine2 = root.Q<Label>("batter-line2");

            _log = root.Q("log");

            // 아직 연결되지 않은 정식 버튼은 비활성으로 둔다.
            root.Query<Button>(className: "btn--inactive").ForEach(b => b.SetEnabled(false));

            _debugIndex = root.Q<Label>("debug-index");
            root.Q<Button>("debug-prev").clicked += () => ShowPitch(_index - 1);
            root.Q<Button>("debug-next").clicked += () => ShowPitch(_index + 1);
        }

        void Render()
        {
            var events = _fixture.Events;
            var e = events[_index];
            int paStart = ScenarioReadModel.PlateAppearanceStart(events, _index);
            int awayScore = _fixture.AwayScoreBefore + ScenarioReadModel.RunsThrough(events, _index, HalfInning.Top);
            int homeScore = _fixture.HomeScoreBefore + ScenarioReadModel.RunsThrough(events, _index, HalfInning.Bottom);
            bool homeBatting = e.HalfInning == HalfInning.Bottom;

            // 헤더 (투구 후 상태)
            _awayScore.text = awayScore.ToString();
            _homeScore.text = homeScore.ToString();
            _statusInning.text =
                $"{PitchPresentation.HalfText(e)} · {PitchPresentation.OutsText(e.OutsAfter)} · {e.RunnersAfter}";
            _statusCount.text = e.Outcome == PlateAppearanceOutcome.None
                ? $"B {e.CountAfter.Balls}  ·  S {e.CountAfter.Strikes}  ·  O {e.OutsAfter}"
                : $"타석 종료 · {PitchPresentation.ResultText(e)}  ·  O {e.OutsAfter}";
            RenderLineScore(e, awayScore, homeScore);

            // 중앙 헤드라인 (투구 직전 상황)
            _headlineMain.text =
                $"{PitchPresentation.HalfText(e)} · {(homeBatting ? "홈" : "원정")} 공격 · " +
                $"{PitchPresentation.OutsText(e.OutsBefore)} {e.RunnersBefore}";
            _headlineSub.text = $"원정 {awayScore} : 홈 {homeScore} · {ScoreSituation(awayScore, homeScore)}";

            // 필드 (투구 후 주자)
            _base1.EnableInClassList("base--occupied", e.RunnersAfter.First != null);
            _base2.EnableInClassList("base--occupied", e.RunnersAfter.Second != null);
            _base3.EnableInClassList("base--occupied", e.RunnersAfter.Third != null);
            _runner3.text = $"3루  {e.RunnersAfter.Third ?? "—"}";
            _runner2.text = $"2루  {e.RunnersAfter.Second ?? "—"}";
            _runner1.text = $"1루  {e.RunnersAfter.First ?? "—"}";
            _playResult.text = e.PlayResult ?? PitchPresentation.ResultText(e);
            SetOptional(_playRuns, e.RunsScored > 0 ? $"득점 +{e.RunsScored}" : null);
            SetOptional(_playOuts, e.OutsAfter != e.OutsBefore ? $"아웃 {e.OutsBefore} → {e.OutsAfter}" : null);

            // 투수·타자
            _pitcherName.text = e.PitcherId;
            _pitcherTeam.text = $"{(homeBatting ? "원정" : "홈")} · 투수";
            _pitcherLine1.text = $"이번 장면 투구 수 {ScenarioReadModel.PitcherPitchesThrough(events, _index)}";
            _pitcherLine2.text = $"직전 구종 {e.PitchType} · {e.VelocityKmh:0} km/h";
            _batterName.text = e.BatterId;
            _batterTeam.text = $"{(homeBatting ? "홈" : "원정")} · 타자";
            _batterLine1.text = $"이번 타석 {_index - paStart + 1}구째 · 카운트 {e.CountAfter}";
            _batterLine2.text = $"존 높이 {e.Zone.BottomCm:0.0}–{e.Zone.TopCm:0.0} cm";

            _abs.Render(events, _index);
            RenderLog(events);

            _debugIndex.text = $"{_index + 1} / {PitchCount}";
        }

        void RenderLineScore(PitchEvent current, int awayScore, int homeScore)
        {
            _lineScore.Clear();
            int innings = Mathf.Max(MinInnings, current.Inning);

            var head = Row();
            head.Add(Cell("", "ls-cell--team"));
            for (int i = 1; i <= innings; i++) head.Add(Cell(i.ToString(), "ls-cell--head"));
            head.Add(Gap());
            foreach (var h in new[] { "R", "H", "E", "B" }) head.Add(Cell(h, "ls-cell--head"));
            _lineScore.Add(head);

            _lineScore.Add(TeamRow("원정", HalfInning.Top, awayScore));
            _lineScore.Add(TeamRow("홈", HalfInning.Bottom, homeScore));

            VisualElement TeamRow(string name, HalfInning half, int total)
            {
                var row = Row();
                row.Add(Cell(name, "ls-cell--team"));
                for (int i = 1; i <= innings; i++)
                {
                    int cmp = CompareHalf(i, half, current.Inning, current.HalfInning);
                    if (cmp == 0)
                    {
                        // 현재 반 이닝: 이 장면에서 올린 득점만 알 수 있다.
                        int runs = ScenarioReadModel.RunsInHalfThrough(_fixture.Events, _index, i, half);
                        row.Add(Cell(runs.ToString(), "ls-cell--current"));
                    }
                    else
                    {
                        // 지난 반 이닝은 이닝별 기록이 없어 미상, 앞으로의 반 이닝은 빈칸.
                        row.Add(Cell(cmp < 0 ? Unknown : "", null));
                    }
                }
                row.Add(Gap());
                row.Add(Cell(total.ToString(), "ls-cell--total"));
                row.Add(Cell(Unknown, null));
                row.Add(Cell(Unknown, null));
                row.Add(Cell(Unknown, null));
                return row;
            }

            VisualElement Row()
            {
                var r = new VisualElement();
                r.AddToClassList("ls-row");
                return r;
            }

            VisualElement Gap()
            {
                var g = new VisualElement();
                g.AddToClassList("ls-gap");
                return g;
            }

            Label Cell(string text, string extraClass)
            {
                var l = new Label(text);
                l.AddToClassList("ls-cell");
                if (extraClass != null) l.AddToClassList(extraClass);
                return l;
            }
        }

        static int CompareHalf(int inning, HalfInning half, int curInning, HalfInning curHalf)
        {
            int a = inning * 2 + (half == HalfInning.Bottom ? 1 : 0);
            int b = curInning * 2 + (curHalf == HalfInning.Bottom ? 1 : 0);
            return a.CompareTo(b);
        }

        void RenderLog(IReadOnlyList<PitchEvent> events)
        {
            _log.Clear();
            int last = Mathf.Max(0, _index - MaxLogEntries + 1);
            for (int i = _index; i >= last; i--)
            {
                var e = events[i];
                int pitchNo = i - ScenarioReadModel.PlateAppearanceStart(events, i) + 1;

                var entry = new VisualElement();
                entry.AddToClassList("log-entry");
                if (i == _index) entry.AddToClassList("log-entry--latest");

                var situation = new Label(
                    $"{PitchPresentation.HalfText(e)} {PitchPresentation.OutsText(e.OutsBefore)} {e.RunnersBefore} · {e.BatterId} {pitchNo}구째");
                situation.AddToClassList("log-entry__situation");
                entry.Add(situation);

                string text = $"{e.PitchType} {e.VelocityKmh:0}km/h · {PitchPresentation.ResultText(e)} ({e.CountBefore} → {e.CountAfter})";
                if (e.PlayResult != null) text += $"\n{e.PlayResult}";
                if (e.Outcome == PlateAppearanceOutcome.Strikeout) text += $"\n삼진 · {PitchPresentation.OutsText(e.OutsAfter)}";
                var result = new Label(text);
                result.AddToClassList("log-entry__result");
                entry.Add(result);

                _log.Add(entry);
            }
        }

        static void SetOptional(Label label, string text)
        {
            label.text = text ?? "";
            label.style.display = text == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        static string ScoreSituation(int away, int home)
        {
            if (away == home) return "동점";
            int diff = Mathf.Abs(away - home);
            return away > home ? $"원정 {diff}점 차 리드" : $"홈 {diff}점 차 리드";
        }
    }
}
