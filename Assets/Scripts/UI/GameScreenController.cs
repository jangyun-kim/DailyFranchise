using System.Collections.Generic;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 1920×1080 경기 화면. ScenarioCatalog의 고정 장면 또는 퀵 시뮬레이션으로 만든 승부처 장면을 읽기 전용으로 재생하며,
    /// 이름·기록은 IRosterSource / IScoreboardSource에서 가져온다.
    /// 점수·H·아웃·주자·타자 기록은 필드 연출이 끝나는 순간에 반영한다 (StatsSync).
    /// 퀵 시뮬레이션 경기는 Play-by-Play로 순환한다: 작전 타석이 끝나면 다음 타순 타자가 들어서 다시 작전을 기다리고, 3아웃이면 멈춘다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameScreenController : MonoBehaviour
    {
        const int MinInnings = 9;
        const int MaxLogEntries = 5;
        const string Unknown = "–";

        [SerializeField] bool autoPlayOnStart = true;

        /// <summary>작전 선택 기록 하나: 분기점 투구 인덱스, 고른 작전, 결과, 판정에 쓴 성공률.</summary>
        public readonly struct TacticDecision
        {
            public readonly int AfterIndex;
            public readonly TacticKind Tactic;
            public readonly TacticOutcome Outcome;
            public readonly double Rate;

            public TacticDecision(int afterIndex, TacticKind tactic, TacticOutcome outcome, double rate)
            {
                AfterIndex = afterIndex;
                Tactic = tactic;
                Outcome = outcome;
                Rate = rate;
            }
        }

        ScenarioFixture _fixture;
        /// <summary>
        /// 현재 재생 경로. 일직선 장면은 Fixture 이벤트, 분기 장면은 작전 선택 후 기본 구간 + 후속 구간.
        /// Play-by-Play 경기는 타석이 끝날 때마다 다음 타자의 작전 전 구간이 이어 붙는다.
        /// </summary>
        IReadOnlyList<PitchEvent> _events;
        /// <summary>지금(또는 다음에) 작전을 기다리는 분기점. 더 고를 작전이 없으면 null.</summary>
        BranchPoint _branch;
        readonly List<TacticDecision> _decisions = new List<TacticDecision>();
        TacticKind? _previewTactic;
        MatchupCalculator _calculator = new MatchupCalculator(null);
        IRosterSource _roster;
        IScoreboardSource _scoreboard;
        PitchPlayback _playback;
        readonly StatsSync _sync = new StatsSync();
        string _scenarioId = ScenarioCatalog.EighthBottom;
        Matchup _matchup = Matchup.Default;
        GeneratedGame _generated;

        VisualElement _root;
        AbsZoneView _abs;
        FieldView _field;
        Label _awayName, _homeName, _awayBadge, _homeBadge, _awayScore, _homeScore, _statusInning, _statusCount;
        VisualElement _lineScore;
        Label _headlineMain, _headlineSub;
        Label _runner1, _runner2, _runner3, _playResult, _playType, _playRuns, _playOuts;
        Label _pitcherName, _pitcherTeam, _pitcherSeason, _pitcherLine1, _pitcherLine2, _pitcherFitness;
        Label _batterName, _batterTeam, _batterSeason, _batterLine1, _batterLine2, _batterFitness;
        Label _pitcherProfile, _pitcherRecord, _batterProfile;
        VisualElement _pitcherStats, _batterStats;
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
        /// <summary>현재 장면 ID. 퀵 시뮬레이션 경기면 <see cref="FixtureGenerator.ScenarioId"/>.</summary>
        public string ScenarioId => _scenarioId;
        /// <summary>퀵 시뮬레이션으로 만든 현재 경기 (내 구단 = 홈 기준). 고정 장면이면 null.</summary>
        public GeneratedGame Generated => _generated;
        /// <summary>지금 그리는 대진 (원정·홈 = 화면의 원정·홈).</summary>
        public Matchup Matchup => _matchup;
        /// <summary>지금 이름표를 공급하는 선수 데이터 (더미 또는 선수단 주입).</summary>
        public IRosterSource Roster => _roster;
        /// <summary>대진에 선수단이 실려 있을 때 화면 하단 출처 표기. 바깥(시즌 흐름)이 정한다.</summary>
        public string RosterSourceLabel { get; set; } = "선수 데이터";
        /// <summary>현재 장면의 원정·홈 시작 점수와 이벤트 (원정 경기면 뒤집은 사본).</summary>
        public ScenarioFixture Fixture => _fixture;
        /// <summary>마지막으로 고른 작전. 작전을 고르기 전이면 null.</summary>
        public TacticKind? ChosenTactic => LastDecision?.Tactic;
        /// <summary>마지막으로 고른 작전의 성공/실패. 작전을 고르기 전이면 null.</summary>
        public TacticOutcome? TacticResult => LastDecision?.Outcome;
        /// <summary>마지막으로 고른 작전을 판정할 때 쓴 성공률. 작전 전이면 null.</summary>
        public double? ChosenTacticRate => LastDecision?.Rate;
        /// <summary>이 경기에서 고른 작전 기록 (타석 순).</summary>
        public IReadOnlyList<TacticDecision> Decisions => _decisions;
        /// <summary>지금 작전을 기다리는(또는 기다릴) 분기점. 남은 분기점이 없으면 null.</summary>
        public BranchPoint CurrentBranch => _branch;

        /// <summary>
        /// 인게임 Play-by-Play: 작전 타석이 끝나면 아웃·주자를 갱신한 상태에서 다음 타순 타자가 들어서고 다시 작전을 기다린다.
        /// 3아웃이면 순환을 멈춘다. 퀵 시뮬레이션 경기에서만 켜진다 (고정 장면은 각본 그대로).
        /// </summary>
        public bool PlayByPlay => _generated != null;

        TacticDecision? LastDecision => _decisions.Count > 0 ? _decisions[_decisions.Count - 1] : (TacticDecision?)null;
        /// <summary>
        /// 작전 성공률 계산기. 선수단이 주입되면(<see cref="ScenarioRosterSource"/>) 승부처 타자·투수 기록으로 계산하고,
        /// 더미 데이터면 장면 기본 성공률을 그대로 쓴다.
        /// </summary>
        public MatchupCalculator Calculator => _calculator;

        /// <summary>현재 장면 승부처에서 이 작전을 고르면 쓸 성공률 (확률 판정이 없는 작전은 1).</summary>
        public double TacticSuccessRate(TacticKind tactic)
        {
            var branch = _branch ?? throw new System.InvalidOperationException("작전을 기다리는 분기점이 없다");
            return _calculator.TacticSuccessRate(branch, _events, tactic);
        }

        /// <summary>
        /// 작전 성공 판정에 쓰는 난수. 기본은 실행마다 다른 Seed이고, 테스트는 고정 Seed나 고정 값을 넣는다.
        /// </summary>
        public IRandomSource TacticRandom { get; set; } = SeededRandom.FromClock();

        /// <summary>
        /// 퀵 시뮬레이션·승부처 상황 생성 난수 (<see cref="LoadGeneratedScenario"/>에 따로 넘기지 않았을 때).
        /// 작전 판정 난수(<see cref="TacticRandom"/>)와 별개다.
        /// </summary>
        public IRandomSource SimRandom { get; set; } = SeededRandom.FromClock();
        public IReadOnlyList<PitchEvent> Events => _events;

        /// <summary>배속별 판정 점등 시간 (초). x4는 생략.</summary>
        public static float FlashSecondsFor(int speed) => speed == 1 ? 0.40f : speed == 2 ? 0.25f : 0f;

        /// <summary>
        /// [경기 종료] 요청. 최종 점수(<see cref="FinalScore"/>)와 내 구단 쪽을 함께 넘긴다. 이 화면은 어디로 돌아갈지 모른다.
        /// 시즌 흐름(SeasonManager)이 구독하면 그쪽이 결과 반영과 로비 복귀를 처리하고,
        /// 구독자가 없으면(씬 단독 실행) 기존처럼 장면 끝으로 이동한다.
        /// </summary>
        public event System.Action<GameResult> ExitRequested;

        /// <summary>경기 종료를 처리할 바깥 흐름이 연결되어 있는지.</summary>
        public bool HasExitHandler => ExitRequested != null;

        public void RequestExit()
        {
            var handler = ExitRequested;
            if (handler != null) handler(new GameResult(FinalScore(), _roster.MyTeam));
            else _playback.SkipToEnd();
        }

        /// <summary>
        /// 현재 경로를 끝까지 진행했을 때의 점수. 장면 끝을 경기 끝으로 본다.
        /// 첫 작전을 고르지 않은 분기 장면은 개입 없이(기본 진행) 끝난 것으로 계산한다.
        /// Play-by-Play 중 다음 타자 작전 대기에서 끝내면 그때까지의 점수다 (남은 타석은 굴리지 않는다).
        /// </summary>
        public GameScore FinalScore()
        {
            var path = _events;
            if (_fixture.Branch != null && _decisions.Count == 0)
                path = _fixture.Branch.PathFor(_events, TacticKind.Default);
            var end = ScreenState.Build(path, _fixture.AwayScoreBefore, _fixture.HomeScoreBefore, path.Count - 1);
            return new GameScore(end.AwayScore, end.HomeScore);
        }

        void OnEnable()
        {
            Bind(GetComponent<UIDocument>().rootVisualElement);
            LoadScenario(_scenarioId);
        }

        void OnDisable() => DetachPlayback();

        void OnDestroy() => ExitRequested = null;

        /// <summary>
        /// 시나리오를 교체한다. 이전 재생기 구독, 필드 연출·토큰, ABS 점등·마커, 로그, 기록 동기화를 모두 초기화한다.
        /// 배속 설정과 현재 대진은 유지한다.
        /// </summary>
        public void LoadScenario(string scenarioId) => LoadScenario(scenarioId, _matchup);

        /// <summary>
        /// 대진을 지정해 시나리오를 불러온다. 헤더·스코어보드·좌측 선수단의 팀명은 대진을 따르고,
        /// 내 구단이 원정이면 장면을 원정·홈을 맞바꾼 사본(<see cref="ScenarioMirror"/>)으로 재생한다.
        /// 대진에 선수단이 실려 있으면 선수 이름표·시즌 기록은 그 선수단으로 그린다 (<see cref="ScenarioRosterSource"/>).
        /// </summary>
        /// <param name="matchup">null이면 <see cref="Matchup.Default"/>(서울 원정 vs 부산 홈).</param>
        public void LoadScenario(string scenarioId, Matchup matchup)
        {
            var entry = ScenarioCatalog.Find(scenarioId);
            _generated = null;
            Apply(entry.Id, entry.Create(), new DummyScenarioData(entry.Id, matchup));
        }

        /// <summary>
        /// 퀵 시뮬레이션 경기를 불러온다: 대진의 두 선수단 기록으로 1~7회·8회초 득점을 계산하고(<see cref="QuickSimEngine"/>),
        /// 8회말 승부처의 아웃 수·주자를 무작위로 정해 장면을 만든다(<see cref="FixtureGenerator"/>).
        /// 스코어보드와 시작 점수·주자·아웃은 생성 결과를 따르고, 내 구단이 원정이면 장면을 뒤집어 재생한다.
        /// </summary>
        /// <param name="matchup">null이면 <see cref="Matchup.Default"/>. 선수단이 없는 쪽은 리그 평균으로 계산한다.</param>
        /// <param name="random">생성 난수. null이면 <see cref="SimRandom"/>.</param>
        public void LoadGeneratedScenario(Matchup matchup, IRandomSource random = null)
        {
            var m = matchup ?? Matchup.Default;
            var opponent = m.RosterOf(m.MySide == TeamSide.Home ? TeamSide.Away : TeamSide.Home);
            // 생성 장면은 내 구단 = 홈(8회말 공격) 기준이다.
            var game = FixtureGenerator.Generate(opponent, m.RosterOf(m.MySide), random ?? SimRandom);
            LoadGenerated(game, m);
        }

        void LoadGenerated(GeneratedGame game, Matchup matchup)
        {
            _generated = game;
            Apply(FixtureGenerator.ScenarioId, game.Fixture, new DummyScenarioData(game, matchup));
        }

        /// <param name="native">내 구단 = 홈 기준 장면.</param>
        void Apply(string scenarioId, ScenarioFixture native, DummyScenarioData data)
        {
            int speed = _playback?.Speed ?? 1;
            DetachPlayback();

            _scenarioId = scenarioId;
            _matchup = data.Matchup;
            _fixture = data.Mirrored ? ScenarioMirror.Mirror(native) : native;
            _events = _fixture.Events;
            _branch = _fixture.Branch;
            _decisions.Clear();
            _previewTactic = null;
            if (_matchup.HasRosters)
            {
                var source = new ScenarioRosterSource(data, RosterSourceLabel);
                _roster = source;
                _scoreboard = source;
            }
            else
            {
                _roster = data;
                _scoreboard = data;
            }
            _calculator = new MatchupCalculator(_roster as IPlayerLookup);

            _playback = new PitchPlayback(_events);
            _playback.SetSpeed(speed);
            if (_branch != null) _playback.SetDecisionPoint(_branch.AfterIndex);
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
        /// 승부처에서 작전을 고른다. <see cref="TacticRandom"/>으로 성공/실패를 정한 뒤, 이미 재생한 투구·기록·필드 상태는
        /// 그대로 두고 기본 구간 뒤에 그 결과의 후속 구간을 이어 붙여 재생을 재개한다.
        /// Play-by-Play 경기(<see cref="PlayByPlay"/>)는 후속 구간을 이 타자의 타석 끝까지만 쓰고, 3아웃 전이면
        /// 다음 타순 타자의 작전 전 구간과 새 분기점을 이어 붙인다 (<see cref="PlayByPlayGenerator"/>).
        /// </summary>
        /// <returns>분기했으면 true. 작전 대기 중이 아니거나 이 분기점에 없는 작전이면 false.</returns>
        public bool ChooseTactic(TacticKind tactic)
        {
            var branch = _branch;
            if (branch == null || !_playback.AwaitingDecision || !branch.Offers(tactic)) return false;

            double rate = _calculator.TacticSuccessRate(branch, _events, tactic);
            var outcome = branch.Resolve(tactic, TacticRandom, rate);
            var path = branch.PathFor(_events, tactic, outcome);
            BranchPoint next = null;
            if (PlayByPlay)
            {
                // 각본의 후속 타자는 떼고, 갱신된 아웃·주자에서 다음 타순 타자가 들어선다. 3아웃이면 segment == null로 순환 종료.
                path = PlayByPlayGenerator.UntilPlateAppearanceEnd(path, branch.AfterIndex + 1);
                var segment = new PlayByPlayGenerator(_calculator, TacticRandom).Next(path);
                if (segment != null)
                {
                    path.AddRange(segment.Events);
                    next = segment.Branch;
                }
            }

            _events = path;
            _branch = next;
            _decisions.Add(new TacticDecision(branch.AfterIndex, tactic, outcome, rate));
            _previewTactic = null;
            _playback.ContinueWith(path); // StateChanged → Render
            if (next != null) _playback.SetDecisionPoint(next.AfterIndex);
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
            if (_decisions.Count > 0)
            {
                // 퀵 시뮬레이션 경기는 다시 굴리지 않고 같은 경기로 되돌린다.
                if (_generated != null) LoadGenerated(_generated, _matchup);
                else LoadScenario(_scenarioId);
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
            _pitcherProfile = root.Q<Label>("pitcher-profile");
            _pitcherRecord = root.Q<Label>("pitcher-record");
            _pitcherStats = root.Q("pitcher-stats");
            _batterProfile = root.Q<Label>("batter-profile");
            _batterStats = root.Q("batter-stats");

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
            _gameEnd.clicked += RequestExit;
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
            var generatedTab = new Button(() => LoadGeneratedScenario(_matchup))
            {
                text = "퀵 시뮬", name = "scenario-" + FixtureGenerator.ScenarioId, userData = FixtureGenerator.ScenarioId,
            };
            generatedTab.AddToClassList("debug-strip__btn");
            _scenarioTabs.Add(generatedTab);
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
            var branch = _branch;
            bool open = branch != null && _playback.AwaitingDecision;
            var chosen = ChosenTactic;
            _tactics.EnableInClassList("tactics--open", open);
            _tacticsTitle.text = open ? "감독 개입 · 작전을 선택하세요" : "감독 개입";

            foreach (var pair in _tacticButtons)
            {
                bool available = open && branch.Offers(pair.Key);
                pair.Value.SetEnabled(available);
                pair.Value.EnableInClassList("btn--tactic-available", available);
                pair.Value.EnableInClassList("btn--active", !open && chosen == pair.Key);
            }

            if (open && _previewTactic.HasValue && branch.Offers(_previewTactic.Value))
            {
                var t = _previewTactic.Value;
                var context = branch[t].IsGamble ? _calculator.DecisionContext(branch, _events) : null;
                var basis = TacticText.Basis(context);
                var adjustments = TacticText.Adjustments(_calculator.Adjustments(context));
                _tacticDesc.text = TacticText.Detail(t) + "\n" + (basis != null ? basis + "\n" : "") +
                                   (adjustments != null ? adjustments + "\n" : "") +
                                   TacticText.Odds(branch[t], TacticSuccessRate(t));
            }
            else if (open)
                _tacticDesc.text = "승부처입니다. 버튼에 포인터를 올리면 실행 조건, 성공 이득, 실패 위험, 성공 확률을 볼 수 있습니다.";
            else if (chosen.HasValue)
                _tacticDesc.text = "선택한 작전 · " + TacticText.Detail(chosen.Value) + "\n" +
                                   (TacticRevealed ? TacticText.OutcomeLine(chosen.Value, TacticResult.Value) : "결과 대기 중");
            else
                _tacticDesc.text = "승부처가 오면 작전을 고를 수 있습니다. 대타·투수 교체는 아직 지원하지 않습니다.";
        }

        /// <summary>Play-by-Play 순환이 3아웃으로 끝나 화면이 마지막 투구에 와 있다 (다음은 [경기 종료]).</summary>
        public bool InningEndAwaitingExit =>
            PlayByPlay && _decisions.Count > 0 && _branch == null && _playback.IsAtEnd && _events[_events.Count - 1].OutsAfter >= 3;

        /// <summary>작전 결과는 분기 직후 투구가 화면에 나온 뒤에 밝힌다.</summary>
        bool TacticRevealed => _decisions.Count > 0 && _playback.Index > LastDecision.Value.AfterIndex;

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
            _gameEnd.SetEnabled(HasExitHandler || !atEnd);
            // Play-by-Play가 3아웃으로 끝나면 [경기 종료]로 결과 반영·로비 복귀를 안내한다.
            _gameEnd.EnableInClassList("btn--active", InningEndAwaitingExit && HasExitHandler);
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
            _root.Q<Label>("roster-source").text = $"표시 데이터: {_roster.SourceLabel}";

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
                ? (InningEndAwaitingExit ? $"{half} 종료 · 공수교대 · 경기 종료 대기" : $"{half} 종료 · 공수교대")
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
            _pitcherProfile.text = pitcher.Profile ?? "";
            SetOptional(_pitcherRecord, pitcher.RecordLine);
            RenderKeyStats(_pitcherStats, pitcher.KeyStats);
            _pitcherLine1.text = $"{pitcher.TodayLine} · {pitcher.PitchesBeforeScene + s.PitcherPitchesInScene}구";
            _pitcherLine2.text = e == null ? "직전 구종 —" : $"직전 구종 {e.PitchType} · {e.VelocityKmh:0} km/h";
            _pitcherFitness.text = pitcher.Fitness.HasValue ? $"체력 {pitcher.Fitness}%" : "체력 —";
            _batterName.text = batter.Name;
            _batterTeam.text = $"{_roster.GetTeam(batter.Side).ShortName} · {batter.Handedness} · {batter.Position}";
            _batterSeason.text = batter.SeasonLine;
            _batterProfile.text = batter.Profile ?? "";
            RenderKeyStats(_batterStats, batter.KeyStats);
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
            if (e.PlayType == PlayType.PopOut && e.OutsAfter - e.OutsBefore >= 2) return "뜬공 더블 아웃";
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
            var decisionAt = new Dictionary<int, TacticDecision>();
            foreach (var d in _decisions) decisionAt[d.AfterIndex] = d;
            int budget = MaxLogEntries;
            for (int i = index; i >= 0 && budget > 0; i--)
            {
                if (index > i && decisionAt.TryGetValue(i, out var decision))
                {
                    var note = new Label($"감독 작전 · {TacticText.Label(decision.Tactic)} · {TacticText.OutcomeText(decision.Tactic, decision.Outcome)}");
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

        /// <summary>
        /// 카드 지표 칩을 그린다. 같은 목록(선수별 캐시)이면 다시 만들지 않는다. 목록이 없으면(더미 선수) 칩 줄을 숨긴다.
        /// </summary>
        static void RenderKeyStats(VisualElement container, IReadOnlyList<KeyStat> stats)
        {
            if (stats != null && ReferenceEquals(container.userData, stats)) return;
            container.userData = stats;
            container.Clear();
            container.style.display = stats == null || stats.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (stats == null) return;
            foreach (var stat in stats)
            {
                var chip = new VisualElement();
                chip.AddToClassList("stat-chip");
                var label = new Label(stat.Label);
                label.AddToClassList("stat-chip__label");
                var value = new Label(stat.Value);
                value.AddToClassList("stat-chip__value");
                chip.Add(label);
                chip.Add(value);
                container.Add(chip);
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
