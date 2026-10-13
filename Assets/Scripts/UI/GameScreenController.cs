using System.Collections.Generic;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 1920×1080 경기 화면. ScenarioCatalog의 고정 장면을 읽기 전용으로 재생하며,
    /// 이름·기록은 IRosterSource / IScoreboardSource에서 가져온다.
    /// 점수·H·아웃·주자·타자 기록은 필드 연출이 끝나는 순간에 반영한다 (StatsSync).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameScreenController : MonoBehaviour
    {
        const int MinInnings = 9;
        const int MaxLogEntries = 5;
        const string Unknown = "–";

        [SerializeField] bool autoPlayOnStart = true;

        ScenarioFixture _fixture;
        /// <summary>현재 재생 경로. 일직선 장면은 Fixture 이벤트, 분기 장면은 작전 선택 후 기본 구간 + 후속 구간.</summary>
        IReadOnlyList<PitchEvent> _events;
        TacticKind? _chosenTactic;
        TacticKind? _previewTactic;
        IRosterSource _roster;
        IScoreboardSource _scoreboard;
        PitchPlayback _playback;
        readonly StatsSync _sync = new StatsSync();
        string _scenarioId = ScenarioCatalog.EighthBottom;

        VisualElement _root;
        AbsZoneView _abs;
        FieldView _field;
        Label _awayName, _homeName, _awayBadge, _homeBadge, _awayScore, _homeScore, _statusInning, _statusCount;
        VisualElement _lineScore;
        Label _headlineMain, _headlineSub;
        Label _runner1, _runner2, _runner3, _playResult, _playType, _playRuns, _playOuts;
        Label _pitcherName, _pitcherTeam, _pitcherSeason, _pitcherLine1, _pitcherLine2, _pitcherFitness;
        Label _batterName, _batterTeam, _batterSeason, _batterLine1, _batterLine2, _batterFitness;
        VisualElement _lineup, _bench, _bullpen;
        VisualElement _log;
        Label _featuredName, _featuredLine, _position;
        VisualElement _scenarioTabs;
        VisualElement _tactics;
        Label _tacticsTitle, _tacticDesc;
        readonly Dictionary<TacticKind, Button> _tacticButtons = new Dictionary<TacticKind, Button>();
        Button _speed1, _speed2, _speed4, _pauseChip;
        Button _restart, _nextPitch, _nextPa, _inningEnd, _gameEnd, _auto;

        public PitchPlayback Playback => _playback;
        public AbsZoneView AbsZone => _abs;
        public FieldView Field => _field;
        public StatsSync Sync => _sync;
        public string ScenarioId => _scenarioId;
        public TacticKind? ChosenTactic => _chosenTactic;
        public IReadOnlyList<PitchEvent> Events => _events;

        /// <summary>배속별 판정 점등 시간 (초). x4는 생략.</summary>
        public static float FlashSecondsFor(int speed) => speed == 1 ? 0.40f : speed == 2 ? 0.25f : 0f;

        void OnEnable()
        {
            Bind(GetComponent<UIDocument>().rootVisualElement);
            LoadScenario(_scenarioId);
        }

        void OnDisable() => DetachPlayback();

        /// <summary>
        /// 시나리오를 교체한다. 이전 재생기 구독, 필드 연출·토큰, ABS 점등·마커, 로그, 기록 동기화를 모두 초기화한다.
        /// 배속 설정은 유지한다.
        /// </summary>
        public void LoadScenario(string scenarioId)
        {
            var entry = ScenarioCatalog.Find(scenarioId);
            int speed = _playback?.Speed ?? 1;
            DetachPlayback();

            _scenarioId = entry.Id;
            _fixture = entry.Create();
            _events = _fixture.Events;
            _chosenTactic = null;
            _previewTactic = null;
            var data = new DummyScenarioData(entry.Id);
            _roster = data;
            _scoreboard = data;

            _playback = new PitchPlayback(_events);
            _playback.SetSpeed(speed);
            if (_fixture.Branch != null) _playback.SetDecisionPoint(_fixture.Branch.AfterIndex);
            _playback.IndexChanged += OnIndexChanged;
            _playback.StateChanged += OnPlaybackStateChanged;

            _sync.Reset();
            _field.Reset();
            _abs.CancelFlash();

            RenderRoster();
            RenderScenarioTabs();
            Render();
            RenderControls();
            _field.Show(CurrentState(), animate: false);

            if (autoPlayOnStart) _playback.Play();
        }

        void DetachPlayback()
        {
            if (_playback == null) return;
            _playback.IndexChanged -= OnIndexChanged;
            _playback.StateChanged -= OnPlaybackStateChanged;
            _playback.Pause();
        }

        /// <summary>
        /// 승부처에서 작전을 고른다. 이미 재생한 투구·기록·필드 상태는 그대로 두고
        /// 기본 구간 뒤에 작전 후속 구간을 이어 붙여 재생을 재개한다.
        /// </summary>
        /// <returns>분기했으면 true. 작전 대기 중이 아니거나 이 분기점에 없는 작전이면 false.</returns>
        public bool ChooseTactic(TacticKind tactic)
        {
            var branch = _fixture.Branch;
            if (branch == null || !_playback.AwaitingDecision || !branch.Continuations.ContainsKey(tactic)) return false;

            var path = branch.PathFor(_events, tactic);
            _events = path;
            _chosenTactic = tactic;
            _previewTactic = null;
            _playback.ContinueWith(path); // StateChanged → Render
            _playback.Play();
            return true;
        }

        /// <summary>작전 버튼에 포인터를 올렸을 때 설명 미리보기. null이면 기본 안내.</summary>
        public void PreviewTactic(TacticKind? tactic)
        {
            _previewTactic = tactic;
            RenderTactics();
        }

        /// <summary>처음으로: 분기한 장면은 작전 선택 전 기본 구간으로 되돌린다.</summary>
        void RestartScenario()
        {
            if (_chosenTactic.HasValue)
            {
                LoadScenario(_scenarioId);
                _playback.Pause();
            }
            else
            {
                _playback.Restart();
            }
        }

        void Update()
        {
            if (_playback == null) return;
            _playback.Tick(Time.deltaTime);
            _field.Tick(Time.deltaTime * _playback.Speed);
        }

        ScreenState CurrentState() => StateAt(_playback.Index);

        /// <summary>기록 표시용 상태: 필드 연출이 끝나기 전에는 직전 투구까지.</summary>
        ScreenState StatsState() => StateAt(_sync.StatsIndex);

        ScreenState StateAt(int index) =>
            ScreenState.Build(_events, _fixture.AwayScoreBefore, _fixture.HomeScoreBefore, index);

        void OnIndexChanged(int index)
        {
            bool animating = _field.Show(CurrentState(), animate: true);
            _sync.OnIndexChanged(index, animating);
            Render();
            RenderControls();
            _abs.Flash(index >= 0 ? _events[index] : null, FlashSecondsFor(_playback.Speed));
        }

        void OnFieldAnimationCompleted()
        {
            if (_sync.OnAnimationCompleted()) Render();
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
            _field = new FieldView(root);
            _field.AnimationCompleted += OnFieldAnimationCompleted;

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
            _playResult = root.Q<Label>("play-result");
            _playType = root.Q<Label>("play-type");
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
            _scenarioTabs = root.Q("scenario-tabs");

            // 작전 버튼은 아직 연결 전이므로 비활성.
            root.Query<Button>(className: "btn--inactive").ForEach(b => b.SetEnabled(false));

            _speed1 = root.Q<Button>("speed-1");
            _speed2 = root.Q<Button>("speed-2");
            _speed4 = root.Q<Button>("speed-4");
            _pauseChip = root.Q<Button>("speed-pause");
            _speed1.clicked += () => _playback.SetSpeed(1);
            _speed2.clicked += () => _playback.SetSpeed(2);
            _speed4.clicked += () => _playback.SetSpeed(4);
            // 재생기는 시나리오마다 새로 만들므로 메서드 그룹이 아니라 람다로 현재 인스턴스를 참조한다.
            _pauseChip.clicked += () => _playback.TogglePlay();

            _restart = root.Q<Button>("ctl-restart");
            _nextPitch = root.Q<Button>("ctl-next-pitch");
            _nextPa = root.Q<Button>("ctl-next-pa");
            _inningEnd = root.Q<Button>("ctl-inning-end");
            _gameEnd = root.Q<Button>("ctl-game-end");
            _auto = root.Q<Button>("ctl-auto");
            _restart.clicked += RestartScenario;
            _nextPitch.clicked += () => _playback.StepPitch();
            _nextPa.clicked += () => _playback.SkipToPlateAppearanceEnd();
            _inningEnd.clicked += () => _playback.SkipToHalfInningEnd();
            _gameEnd.clicked += () => _playback.SkipToEnd();
            _auto.clicked += () => _playback.TogglePlay();

            // 감독 개입 버튼: 승부처에서만 켜진다. 대타·투수 교체는 이번 범위 밖이라 계속 비활성.
            _tactics = root.Q("tactics");
            _tacticsTitle = root.Q<Label>("tactics-title");
            _tacticDesc = root.Q<Label>("tactic-desc");
            _tacticButtons.Clear();
            BindTactic("tactic-default", TacticKind.Default);
            BindTactic("tactic-swing", TacticKind.SwingAway);
            BindTactic("tactic-squeeze", TacticKind.SqueezeBunt);

            // 디버그 전용 시나리오 탭
            _scenarioTabs.Clear();
            foreach (var entry in ScenarioCatalog.All)
            {
                string id = entry.Id;
                var tab = new Button(() => LoadScenario(id)) { text = entry.Label, name = "scenario-" + id, userData = id };
                tab.AddToClassList("debug-strip__btn");
                _scenarioTabs.Add(tab);
            }
        }

        void BindTactic(string buttonName, TacticKind kind)
        {
            var button = _root.Q<Button>(buttonName);
            button.text = TacticText.Label(kind);
            button.clicked += () => ChooseTactic(kind);
            button.RegisterCallback<PointerEnterEvent>(_ => PreviewTactic(kind));
            button.RegisterCallback<PointerLeaveEvent>(_ => PreviewTactic(null));
            _tacticButtons[kind] = button;
        }

        void RenderTactics()
        {
            var branch = _fixture.Branch;
            bool open = branch != null && _playback.AwaitingDecision;
            _tactics.EnableInClassList("tactics--open", open);
            _tacticsTitle.text = open ? "감독 개입 · 작전을 선택하세요" : "감독 개입";

            foreach (var pair in _tacticButtons)
            {
                bool available = open && branch.Continuations.ContainsKey(pair.Key);
                pair.Value.SetEnabled(available);
                pair.Value.EnableInClassList("btn--tactic-available", available);
                pair.Value.EnableInClassList("btn--active", !open && _chosenTactic == pair.Key);
            }

            if (open && _previewTactic.HasValue)
                _tacticDesc.text = TacticText.Detail(_previewTactic.Value);
            else if (open)
                _tacticDesc.text = "승부처입니다. 버튼에 포인터를 올리면 실행 조건, 성공 이득, 실패 위험을 볼 수 있습니다.";
            else if (_chosenTactic.HasValue)
                _tacticDesc.text = "선택한 작전 · " + TacticText.Detail(_chosenTactic.Value);
            else
                _tacticDesc.text = "승부처가 오면 작전을 고를 수 있습니다. 대타·투수 교체는 아직 지원하지 않습니다.";
        }

        void RenderScenarioTabs()
        {
            foreach (var tab in _scenarioTabs.Children())
                tab.EnableInClassList("debug-strip__btn--active", (string)tab.userData == _scenarioId);
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

            _position.text = _playback.AwaitingDecision ? $"투구 {_playback.Index + 1} · 작전 대기"
                : _playback.Index < 0 ? $"투구 전 · 0 / {_playback.Count}"
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
                row.Add(Cell(entry.Note ?? "", "roster-state"));
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
            var events = _events;
            // s: 지금 보여주는 투구 (ABS·중계·결과 문구는 즉시)
            // st: 기록 반영 기준 (점수·H·아웃·주자·타자 기록은 필드 연출이 끝난 뒤)
            var s = CurrentState();
            var st = StatsState();
            bool committed = st.Index == s.Index;
            var e = s.Pitch;
            var away = _roster.GetTeam(TeamSide.Away);
            var home = _roster.GetTeam(TeamSide.Home);
            var battingTeam = s.HomeBatting ? home : away;
            string half = $"{s.Inning}회{(s.HomeBatting ? "말" : "초")}";
            bool inningOver = st.Outs >= 3;

            // 헤더
            _awayScore.text = st.AwayScore.ToString();
            _homeScore.text = st.HomeScore.ToString();
            _statusInning.text = inningOver
                ? $"{half} · 3아웃 · 이닝 종료"
                : $"{half} · {PitchPresentation.OutsText(st.Outs)} · {st.Runners}";
            _statusCount.text = inningOver ? $"공수교대 · 다음 {NextHalfText(s)}"
                : s.PlateAppearanceEnded ? $"타석 종료 · {PitchPresentation.ResultText(e)}  ·  O {st.Outs}"
                : $"B {s.Count.Balls}  ·  S {s.Count.Strikes}  ·  O {st.Outs}";
            RenderLineScore(s, st);

            // 중앙 헤드라인 (투구 직전 상황 / 작전 대기 / 이닝 종료 시 공수교대)
            _headlineMain.text = _playback.AwaitingDecision
                ? $"{half} · 승부처 · {PitchPresentation.OutsText(st.Outs)} {st.Runners} · 작전 선택 대기"
                : inningOver
                ? $"{half} 종료 · 공수교대"
                : $"{half} · {battingTeam.ShortName} 공격 · {PitchPresentation.OutsText(s.OutsBefore)} {s.RunnersBefore}";
            _headlineSub.text = $"{away.ShortName} {st.AwayScore} : {home.ShortName} {st.HomeScore} · " +
                                ScoreSituation(away.ShortName, st.AwayScore, home.ShortName, st.HomeScore);

            // 필드. 루 점유·주자 토큰·타구선은 FieldView가 그린다.
            _runner3.text = $"3루  {RunnerName(st.Runners.Third)}";
            _runner2.text = $"2루  {RunnerName(st.Runners.Second)}";
            _runner1.text = $"1루  {RunnerName(st.Runners.First)}";
            _playResult.text = e == null ? "투구 대기" : e.PlayResult ?? PitchPresentation.ResultText(e);
            SetOptional(_playType, e != null && e.PlayType != PlayType.None && e.SprayAngleDeg.HasValue
                ? $"{PlayTypeLabel(e)} · {PitchPresentation.DirectionText(e.SprayAngleDeg.Value)}"
                : null);
            SetOptional(_playRuns, committed && e != null && e.RunsScored > 0 ? $"득점 +{e.RunsScored}" : null);
            SetOptional(_playOuts, committed && e != null && e.OutsAfter != e.OutsBefore
                ? $"아웃 {e.OutsBefore} → {e.OutsAfter}{(e.OutsAfter >= 3 ? " · 이닝 종료" : "")}"
                : null);

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
            string batterToday = TodayBatting(batter, st.Index);
            _batterLine1.text = s.PitchNumberInPlateAppearance == 0
                ? $"{batterToday} · 타석 대기"
                : $"{batterToday} · 이번 타석 {s.PitchNumberInPlateAppearance}구째";
            _batterLine2.text = $"존 높이 {s.Zone.BottomCm:0.0}–{s.Zone.TopCm:0.0} cm";
            _batterFitness.text = batter.Fitness.HasValue ? $"컨디션 {batter.Fitness}%" : "컨디션 —";

            // 라인업: 오늘 안타-타수 갱신, 현재 타자·주자 표시
            foreach (var row in _lineup.Children())
            {
                var id = row.userData as string;
                var player = _roster.GetPlayer(id);
                if (player.TodayAtBats.HasValue)
                {
                    var (ab, h) = TodayTotals(player, st.Index);
                    row.Q<Label>(className: "roster-state").text = $"{h}-{ab}";
                }
                row.EnableInClassList("roster-row--current", id == s.BatterId);
                row.EnableInClassList("roster-row--onbase",
                    id != null && (id == st.Runners.First || id == st.Runners.Second || id == st.Runners.Third));
            }

            var featured = _roster.GetPlayer(_roster.FeaturedPlayerId);
            _featuredLine.text = $"{featured.SeasonLine} / {TodayBatting(featured, st.Index)}";

            bool showHistory = !(_playback.IsPlaying && _playback.Speed >= 4);
            _abs.Render(events, s, NameOf, showHistory);
            RenderLog(events);
            RenderTactics();
        }

        string RunnerName(string id) => id == null ? "—" : NameOf(id);

        string NextHalfText(ScreenState s) => s.HomeBatting
            ? $"{s.Inning + 1}회초 {_roster.GetTeam(TeamSide.Away).ShortName} 공격"
            : $"{s.Inning}회말 {_roster.GetTeam(TeamSide.Home).ShortName} 공격";

        /// <summary>타구 종류 표기. 아웃이 2개 늘어난 땅볼은 병살, 득점이 난 희생 번트는 스퀴즈로 표시한다.</summary>
        static string PlayTypeLabel(PitchEvent e)
        {
            if (e.PlayType == PlayType.GroundOut && e.OutsAfter - e.OutsBefore >= 2) return "땅볼 병살";
            if (e.PlayType == PlayType.SacrificeBunt && e.RunsScored > 0) return "스퀴즈 번트";
            return PitchPresentation.PlayTypeText(e.PlayType);
        }

        /// <param name="s">현재 투구 (어느 반 이닝을 강조할지).</param>
        /// <param name="st">기록 반영 기준 (득점·H·B를 어디까지 셀지).</param>
        void RenderLineScore(ScreenState s, ScreenState st)
        {
            _lineScore.Clear();
            int innings = Mathf.Max(MinInnings, s.Inning);
            var events = _events;

            var head = Row();
            head.Add(Cell("", "ls-cell--team"));
            for (int i = 1; i <= innings; i++) head.Add(Cell(i.ToString(), "ls-cell--head"));
            head.Add(Gap());
            foreach (var h in new[] { "R", "H", "E", "B" }) head.Add(Cell(h, "ls-cell--head"));
            _lineScore.Add(head);

            _lineScore.Add(TeamRow(TeamSide.Away, HalfInning.Top, st.AwayScore));
            _lineScore.Add(TeamRow(TeamSide.Home, HalfInning.Bottom, st.HomeScore));

            VisualElement TeamRow(TeamSide side, HalfInning half, int total)
            {
                var seed = _scoreboard.GetBoxSeed(side);
                var row = Row();
                row.Add(Cell(_roster.GetTeam(side).ShortName, "ls-cell--team"));
                for (int i = 1; i <= innings; i++)
                {
                    int cmp = CompareHalf(i, half, s.Inning, s.Half);
                    bool seeded = i <= seed.RunsByInning.Length;
                    int runs = (seeded ? seed.RunsByInning[i - 1] : 0) +
                               (st.Index < 0 ? 0 : ScenarioReadModel.RunsInHalfThrough(events, st.Index, i, half));
                    if (cmp == 0)
                        row.Add(Cell(runs.ToString(), "ls-cell--current"));
                    else if (cmp < 0) // 장면 안에서 끝난 반 이닝의 득점도 포함
                        row.Add(Cell(seeded || HalfInScene(i, half) ? runs.ToString() : Unknown, null));
                    else
                        row.Add(Cell("", null));
                }
                row.Add(Gap());
                row.Add(Cell(total.ToString(), "ls-cell--total"));
                int hits = seed.Hits + (st.Index < 0 ? 0 : ScenarioReadModel.HitsThrough(events, st.Index, half));
                row.Add(Named(Cell(hits.ToString(), null), side, "h"));
                row.Add(Named(Cell(seed.Errors.ToString(), null), side, "e"));
                row.Add(Named(Cell((seed.Walks + WalksInScene(half)).ToString(), null), side, "b"));
                return row;
            }

            Label Named(Label l, TeamSide side, string column)
            {
                l.name = $"ls-{(side == TeamSide.Away ? "away" : "home")}-{column}";
                return l;
            }

            bool HalfInScene(int inning, HalfInning half)
            {
                foreach (var ev in events)
                    if (ev.Inning == inning && ev.HalfInning == half) return true;
                return false;
            }

            int WalksInScene(HalfInning half)
            {
                int n = 0;
                for (int i = 0; i <= st.Index; i++)
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

            // 작전 선택 기록은 분기점 투구 바로 위(시간상 직후)에 끼우고, 투구와 같이 개수 제한에 포함한다.
            int? tacticAfter = _chosenTactic.HasValue ? _fixture.Branch.AfterIndex : (int?)null;
            int budget = MaxLogEntries;
            for (int i = index; i >= 0 && budget > 0; i--)
            {
                if (tacticAfter == i && index > i)
                {
                    var note = new Label($"감독 작전 · {TacticText.Label(_chosenTactic.Value)}");
                    note.AddToClassList("log-entry");
                    note.AddToClassList("log-entry--tactic");
                    _log.Add(note);
                    if (--budget == 0) break;
                }

                budget--;
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

        /// <summary>장면 시작 전 오늘 기록 + 장면 안에서 index까지의 타석 결과.</summary>
        (int atBats, int hits) TodayTotals(PlayerInfo p, int index)
        {
            var scene = index < 0 ? (0, 0) : ScenarioReadModel.BatterLineThrough(_events, index, p.Id);
            return ((p.TodayAtBats ?? 0) + scene.Item1, p.TodayHits + scene.Item2);
        }

        string TodayBatting(PlayerInfo p, int index)
        {
            if (!p.TodayAtBats.HasValue) return p.TodayLine ?? "오늘 기록 —";
            var (ab, h) = TodayTotals(p, index);
            return PitchPresentation.TodayBattingText(ab, h, p.TodayExtra);
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
