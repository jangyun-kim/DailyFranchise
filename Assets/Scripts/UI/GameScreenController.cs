using System.Collections.Generic;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 1920×1080 경기 화면. Fixture8thBottom을 읽기 전용으로 재생하며,
    /// 이름·기록은 IRosterSource / IScoreboardSource에서 가져온다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameScreenController : MonoBehaviour
    {
        const int MinInnings = 9;
        const int MaxLogEntries = 5;
        const string Unknown = "–";

        [SerializeField] bool autoPlayOnStart = true;

        ScenarioFixture _fixture;
        IRosterSource _roster;
        IScoreboardSource _scoreboard;
        PitchPlayback _playback;

        VisualElement _root;
        AbsZoneView _abs;
        Label _awayName, _homeName, _awayBadge, _homeBadge, _awayScore, _homeScore, _statusInning, _statusCount;
        VisualElement _lineScore;
        Label _headlineMain, _headlineSub;
        Label _runner1, _runner2, _runner3, _playResult, _playRuns, _playOuts;
        VisualElement _base1, _base2, _base3;
        Label _pitcherName, _pitcherTeam, _pitcherSeason, _pitcherLine1, _pitcherLine2, _pitcherFitness;
        Label _batterName, _batterTeam, _batterSeason, _batterLine1, _batterLine2, _batterFitness;
        VisualElement _lineup, _bench, _bullpen;
        VisualElement _log;
        Label _featuredName, _featuredLine, _position;
        Button _speed1, _speed2, _speed4, _pauseChip;
        Button _restart, _nextPitch, _nextPa, _inningEnd, _gameEnd, _auto;

        public PitchPlayback Playback => _playback;
        public AbsZoneView AbsZone => _abs;

        /// <summary>배속별 판정 점등 시간 (초). x4는 생략.</summary>
        public static float FlashSecondsFor(int speed) => speed == 1 ? 0.40f : speed == 2 ? 0.25f : 0f;

        void OnEnable()
        {
            _fixture = Fixture8thBottom.Create();
            var data = new DummyScenarioData();
            _roster = data;
            _scoreboard = data;

            _playback = new PitchPlayback(_fixture.Events);
            _playback.IndexChanged += OnIndexChanged;
            _playback.StateChanged += OnPlaybackStateChanged;

            Bind(GetComponent<UIDocument>().rootVisualElement);
            RenderRoster();
            Render();
            RenderControls();

            if (autoPlayOnStart) _playback.Play();
        }

        void OnDisable()
        {
            if (_playback == null) return;
            _playback.IndexChanged -= OnIndexChanged;
            _playback.StateChanged -= OnPlaybackStateChanged;
        }

        void Update() => _playback?.Tick(Time.deltaTime);

        void OnIndexChanged(int index)
        {
            Render();
            RenderControls();
            _abs.Flash(index >= 0 ? _fixture.Events[index] : null, FlashSecondsFor(_playback.Speed));
        }

        void OnPlaybackStateChanged()
        {
            Render();
            RenderControls();
        }

        void Bind(VisualElement root)
        {
            _root = root;
            _abs = new AbsZoneView(root);

            _awayName = root.Q<Label>("away-name");
            _homeName = root.Q<Label>("home-name");
            _awayBadge = root.Query<Label>(className: "team-badge").AtIndex(0);
            _homeBadge = root.Query<Label>(className: "team-badge").AtIndex(1);
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
            _pitcherSeason = root.Q<Label>("pitcher-season");
            _pitcherLine1 = root.Q<Label>("pitcher-line1");
            _pitcherLine2 = root.Q<Label>("pitcher-line2");
            _pitcherFitness = root.Q<Label>("pitcher-fitness");
            _batterName = root.Q<Label>("batter-name");
            _batterTeam = root.Q<Label>("batter-team");
            _batterSeason = root.Q<Label>("batter-season");
            _batterLine1 = root.Q<Label>("batter-line1");
            _batterLine2 = root.Q<Label>("batter-line2");
            _batterFitness = root.Q<Label>("batter-fitness");

            _lineup = root.Q("lineup");
            _bench = root.Q("bench");
            _bullpen = root.Q("bullpen");
            _log = root.Q("log");
            _featuredName = root.Q<Label>("featured-name");
            _featuredLine = root.Q<Label>("featured-line");
            _position = root.Q<Label>("playback-position");

            // 작전 버튼은 아직 연결 전이므로 비활성.
            root.Query<Button>(className: "btn--inactive").ForEach(b => b.SetEnabled(false));

            _speed1 = root.Q<Button>("speed-1");
            _speed2 = root.Q<Button>("speed-2");
            _speed4 = root.Q<Button>("speed-4");
            _pauseChip = root.Q<Button>("speed-pause");
            _speed1.clicked += () => _playback.SetSpeed(1);
            _speed2.clicked += () => _playback.SetSpeed(2);
            _speed4.clicked += () => _playback.SetSpeed(4);
            _pauseChip.clicked += _playback.TogglePlay;

            _restart = root.Q<Button>("ctl-restart");
            _nextPitch = root.Q<Button>("ctl-next-pitch");
            _nextPa = root.Q<Button>("ctl-next-pa");
            _inningEnd = root.Q<Button>("ctl-inning-end");
            _gameEnd = root.Q<Button>("ctl-game-end");
            _auto = root.Q<Button>("ctl-auto");
            _restart.clicked += _playback.Restart;
            _nextPitch.clicked += _playback.StepPitch;
            _nextPa.clicked += _playback.SkipToPlateAppearanceEnd;
            _inningEnd.clicked += _playback.SkipToHalfInningEnd;
            _gameEnd.clicked += _playback.SkipToEnd;
            _auto.clicked += _playback.TogglePlay;
        }

        string NameOf(string playerId) => _roster.GetPlayer(playerId).Name;

        void RenderControls()
        {
            _speed1.EnableInClassList("btn--active", _playback.Speed == 1);
            _speed2.EnableInClassList("btn--active", _playback.Speed == 2);
            _speed4.EnableInClassList("btn--active", _playback.Speed == 4);
            _pauseChip.text = _playback.IsPlaying ? "일시정지" : "재생";
            _auto.text = _playback.IsPlaying ? "자동 ■" : "자동 ▶";
            _auto.EnableInClassList("btn--active", _playback.IsPlaying);

            bool atEnd = _playback.IsAtEnd;
            _pauseChip.SetEnabled(!atEnd || _playback.IsPlaying);
            _auto.SetEnabled(!atEnd || _playback.IsPlaying);
            _nextPitch.SetEnabled(!atEnd);
            _nextPa.SetEnabled(!atEnd);
            _inningEnd.SetEnabled(!atEnd);
            _gameEnd.SetEnabled(!atEnd);
            _restart.SetEnabled(_playback.Index >= 0);

            _position.text = _playback.Index < 0
                ? $"투구 전 · 0 / {_playback.Count}"
                : $"투구 {_playback.Index + 1} / {_playback.Count}";
        }

        void RenderRoster()
        {
            var my = _roster.MyTeam;
            _root.Q<Label>("roster-title").text = $"내 선수단 · {_roster.GetTeam(my).ShortName}";
            FillRoster(_lineup, _roster.GetLineup(my));
            FillRoster(_bench, _roster.GetBench(my));
            FillRoster(_bullpen, _roster.GetBullpen(my));
            _root.Q<Label>("roster-source").text = "표시 데이터: 가상 더미 (DummyScenarioData)";

            var featured = _roster.GetPlayer(_roster.FeaturedPlayerId);
            _featuredName.text = $"{featured.Name} · {featured.Position}";
            _featuredLine.text = $"{featured.SeasonLine} / {featured.TodayLine}";

            _awayName.text = _roster.GetTeam(TeamSide.Away).Name;
            _homeName.text = _roster.GetTeam(TeamSide.Home).Name;
            _awayBadge.text = _roster.GetTeam(TeamSide.Away).ShortName;
            _homeBadge.text = _roster.GetTeam(TeamSide.Home).ShortName;
        }

        void FillRoster(VisualElement container, IReadOnlyList<RosterEntry> entries)
        {
            container.Clear();
            foreach (var entry in entries)
            {
                var row = new VisualElement { userData = entry.PlayerId };
                row.AddToClassList("roster-row");
                row.Add(Cell(entry.Order?.ToString() ?? "", "roster-order"));
                row.Add(Cell(entry.Role, "roster-pos"));
                row.Add(Cell(NameOf(entry.PlayerId), "roster-name"));
                row.Add(Cell(entry.Note, "roster-state"));
                container.Add(row);
            }

            Label Cell(string text, string cls)
            {
                var l = new Label(text);
                l.AddToClassList(cls);
                return l;
            }
        }

        void Render()
        {
            var events = _fixture.Events;
            var s = ScreenState.Build(events, _fixture.AwayScoreBefore, _fixture.HomeScoreBefore, _playback.Index);
            var e = s.Pitch;
            var away = _roster.GetTeam(TeamSide.Away);
            var home = _roster.GetTeam(TeamSide.Home);
            var battingTeam = s.HomeBatting ? home : away;
            string half = $"{s.Inning}회{(s.HomeBatting ? "말" : "초")}";

            // 헤더 (현재 = 투구 후 상태)
            _awayScore.text = s.AwayScore.ToString();
            _homeScore.text = s.HomeScore.ToString();
            _statusInning.text = $"{half} · {PitchPresentation.OutsText(s.Outs)} · {s.Runners}";
            _statusCount.text = s.PlateAppearanceEnded
                ? $"타석 종료 · {PitchPresentation.ResultText(e)}  ·  O {s.Outs}"
                : $"B {s.Count.Balls}  ·  S {s.Count.Strikes}  ·  O {s.Outs}";
            RenderLineScore(s);

            // 중앙 헤드라인 (투구 직전 상황)
            _headlineMain.text = $"{half} · {battingTeam.ShortName} 공격 · " +
                                 $"{PitchPresentation.OutsText(s.OutsBefore)} {s.RunnersBefore}";
            _headlineSub.text = $"{away.ShortName} {s.AwayScore} : {home.ShortName} {s.HomeScore} · " +
                                ScoreSituation(away.ShortName, s.AwayScore, home.ShortName, s.HomeScore);

            // 필드 (현재 주자)
            _base1.EnableInClassList("base--occupied", s.Runners.First != null);
            _base2.EnableInClassList("base--occupied", s.Runners.Second != null);
            _base3.EnableInClassList("base--occupied", s.Runners.Third != null);
            _runner3.text = $"3루  {RunnerName(s.Runners.Third)}";
            _runner2.text = $"2루  {RunnerName(s.Runners.Second)}";
            _runner1.text = $"1루  {RunnerName(s.Runners.First)}";
            _playResult.text = e == null ? "투구 대기" : e.PlayResult ?? PitchPresentation.ResultText(e);
            SetOptional(_playRuns, e != null && e.RunsScored > 0 ? $"득점 +{e.RunsScored}" : null);
            SetOptional(_playOuts, e != null && e.OutsAfter != e.OutsBefore ? $"아웃 {e.OutsBefore} → {e.OutsAfter}" : null);

            // 투수·타자
            var pitcher = _roster.GetPlayer(s.PitcherId);
            var batter = _roster.GetPlayer(s.BatterId);
            _pitcherName.text = pitcher.Name;
            _pitcherTeam.text = $"{_roster.GetTeam(pitcher.Side).ShortName} · {pitcher.Handedness} · {pitcher.Position}";
            _pitcherSeason.text = pitcher.SeasonLine;
            _pitcherLine1.text = $"{pitcher.TodayLine} · {pitcher.PitchesBeforeScene + s.PitcherPitchesInScene}구";
            _pitcherLine2.text = e == null ? "직전 구종 —" : $"직전 구종 {e.PitchType} · {e.VelocityKmh:0} km/h";
            _pitcherFitness.text = pitcher.Fitness.HasValue ? $"체력 {pitcher.Fitness}%" : "체력 —";
            _batterName.text = batter.Name;
            _batterTeam.text = $"{_roster.GetTeam(batter.Side).ShortName} · {batter.Handedness} · {batter.Position}";
            _batterSeason.text = batter.SeasonLine;
            _batterLine1.text = s.PitchNumberInPlateAppearance == 0
                ? $"{batter.TodayLine} · 타석 대기"
                : $"{batter.TodayLine} · 이번 타석 {s.PitchNumberInPlateAppearance}구째";
            _batterLine2.text = $"존 높이 {s.Zone.BottomCm:0.0}–{s.Zone.TopCm:0.0} cm";
            _batterFitness.text = batter.Fitness.HasValue ? $"컨디션 {batter.Fitness}%" : "컨디션 —";

            // 라인업에서 현재 타자·주자 표시
            foreach (var row in _lineup.Children())
            {
                var id = row.userData as string;
                row.EnableInClassList("roster-row--current", id == s.BatterId);
                row.EnableInClassList("roster-row--onbase",
                    id != null && (id == s.Runners.First || id == s.Runners.Second || id == s.Runners.Third));
            }

            bool showHistory = !(_playback.IsPlaying && _playback.Speed >= 4);
            _abs.Render(events, s, NameOf, showHistory);
            RenderLog(events);
        }

        string RunnerName(string id) => id == null ? "—" : NameOf(id);

        void RenderLineScore(ScreenState s)
        {
            _lineScore.Clear();
            int innings = Mathf.Max(MinInnings, s.Inning);
            var events = _fixture.Events;

            var head = Row();
            head.Add(Cell("", "ls-cell--team"));
            for (int i = 1; i <= innings; i++) head.Add(Cell(i.ToString(), "ls-cell--head"));
            head.Add(Gap());
            foreach (var h in new[] { "R", "H", "E", "B" }) head.Add(Cell(h, "ls-cell--head"));
            _lineScore.Add(head);

            _lineScore.Add(TeamRow(TeamSide.Away, HalfInning.Top, s.AwayScore));
            _lineScore.Add(TeamRow(TeamSide.Home, HalfInning.Bottom, s.HomeScore));

            VisualElement TeamRow(TeamSide side, HalfInning half, int total)
            {
                var seed = _scoreboard.GetBoxSeed(side);
                var row = Row();
                row.Add(Cell(_roster.GetTeam(side).ShortName, "ls-cell--team"));
                for (int i = 1; i <= innings; i++)
                {
                    int cmp = CompareHalf(i, half, s.Inning, s.Half);
                    int seedRuns = i <= seed.RunsByInning.Length ? seed.RunsByInning[i - 1] : 0;
                    if (cmp == 0)
                    {
                        int runs = seedRuns + (s.Index < 0 ? 0 : ScenarioReadModel.RunsInHalfThrough(events, s.Index, i, half));
                        row.Add(Cell(runs.ToString(), "ls-cell--current"));
                    }
                    else if (cmp < 0)
                    {
                        row.Add(Cell(i <= seed.RunsByInning.Length ? seedRuns.ToString() : Unknown, null));
                    }
                    else
                    {
                        row.Add(Cell("", null));
                    }
                }
                row.Add(Gap());
                row.Add(Cell(total.ToString(), "ls-cell--total"));
                row.Add(Cell(seed.Hits.ToString(), null));
                row.Add(Cell(seed.Errors.ToString(), null));
                row.Add(Cell((seed.Walks + WalksInScene(half)).ToString(), null));
                return row;
            }

            int WalksInScene(HalfInning half)
            {
                int n = 0;
                for (int i = 0; i <= s.Index; i++)
                    if (events[i].HalfInning == half && events[i].Outcome == PlateAppearanceOutcome.Walk) n++;
                return n;
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
            int index = _playback.Index;
            if (index < 0)
            {
                var waiting = new Label("경기 재개 대기 중 · 자동 또는 다음 투구를 누르세요.");
                waiting.AddToClassList("log-entry__situation");
                _log.Add(waiting);
                return;
            }

            int last = Mathf.Max(0, index - MaxLogEntries + 1);
            for (int i = index; i >= last; i--)
            {
                var e = events[i];
                int pitchNo = i - ScenarioReadModel.PlateAppearanceStart(events, i) + 1;

                var entry = new VisualElement();
                entry.AddToClassList("log-entry");
                if (i == index) entry.AddToClassList("log-entry--latest");

                var situation = new Label(
                    $"{PitchPresentation.HalfText(e)} {PitchPresentation.OutsText(e.OutsBefore)} {e.RunnersBefore} · {NameOf(e.BatterId)} {pitchNo}구째");
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

        static string ScoreSituation(string awayName, int away, string homeName, int home)
        {
            if (away == home) return "동점";
            int diff = Mathf.Abs(away - home);
            return away > home ? $"{awayName} {diff}점 차 리드" : $"{homeName} {diff}점 차 리드";
        }
    }
}
