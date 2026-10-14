using System;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI;
using DailyFranchise.UI.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DailyFranchise.App
{
    /// <summary>
    /// 씬을 넘어 시즌 상태를 유지하고 로비 ↔ 경기 화면 전환을 중계한다.
    /// 로비와 경기 화면은 서로를 모르며, 이 객체만 두 쪽을 안다.
    /// - 로비: StartNextGame() 호출
    /// - 경기 화면: 일정표의 대진으로 시즌 경기 장면을 불러오고 (기본: 두 선수단 기록으로 1~7회를 퀵 시뮬레이션하고
    ///   8회말 승부처의 아웃·주자를 무작위로 정한 장면), GameScreenController.ExitRequested(최종 점수)를
    ///   구독해 결과를 시즌 성적·순위에 반영·저장한 뒤 로비로 복귀
    /// - 저장: 처음 만들어질 때(로비 최초 진입) 저장 파일이 있으면 이어서, 없으면 새 시즌. 매 경기 결과 반영 직후 저장.
    /// - 선수 DB: 처음 만들어질 때 <see cref="MasterDatabase"/>를 읽는다 (기본 비동기). 다 읽기 전에는 경기를 시작하지 않는다.
    ///   경기 시작 시 일정의 두 구단 선수단을 대진에 실어 경기 화면에 넘긴다.
    /// - 라인업: 로비에서 바꾼 내 구단 타자 라인업(<see cref="SeasonState.MyLineup"/>)을 선수단에 적용하고, 바꿀 때마다 저장한다.
    /// </summary>
    public sealed class SeasonManager : MonoBehaviour
    {
        public const string LobbyScene = "LobbyScreen";
        public const string GameScene = "GameScreen";
        /// <summary><see cref="UseGeneratedScenario"/>가 false일 때 시즌 경기로 재생할 고정 장면.</summary>
        public const string SeasonGameScenario = ScenarioCatalog.EighthBottomTactics;

        /// <summary>
        /// true(기본)면 시즌 경기마다 퀵 시뮬레이션으로 스코어보드와 승부처 상황을 새로 만든다
        /// (<see cref="GameScreenController.LoadGeneratedScenario"/>). false면 고정 장면(<see cref="SeasonGameScenario"/>).
        /// </summary>
        public bool UseGeneratedScenario { get; set; } = true;

        /// <summary>
        /// 퀵 시뮬레이션·승부처 상황 생성 난수. 시즌 동안 이어 쓰므로 경기마다 다른 경기가 나온다. 테스트는 고정 Seed를 넣는다.
        /// </summary>
        public IRandomSource ScenarioRandom { get; set; } = SeededRandom.FromClock();

        public static SeasonManager Instance { get; private set; }

        public SeasonState Season { get; private set; }
        /// <summary>처리한 경기 종료 요청 수 (중복 구독 진단용).</summary>
        public int ExitsHandled { get; private set; }
        /// <summary>마지막으로 반영한 경기 최종 점수 (일정의 원정 : 홈 기준).</summary>
        public GameScore? LastScore { get; private set; }

        /// <summary>
        /// 타 구단 경기 결과용 난수. 경기 화면의 작전 판정 난수(<see cref="GameScreenController.TacticRandom"/>)와 별개다.
        /// 테스트는 고정 Seed를 넣는다.
        /// </summary>
        public IRandomSource LeagueRandom { get; set; } = SeededRandom.FromClock();

        /// <summary>
        /// 저장 파일 경로 재지정. null이면 <see cref="SaveManager.DefaultPath"/>.
        /// SeasonManager가 만들어지기 전에 정해야 한다 (테스트가 실제 저장 파일을 건드리지 않게 할 때 쓴다).
        /// </summary>
        public static string SavePathOverride { get; set; }

        /// <summary>
        /// true면 선수 DB를 Awake에서 동기로 읽는다 (테스트용). false(기본)면 코루틴으로 비동기로 읽는다.
        /// SeasonManager가 만들어지기 전에 정해야 한다.
        /// </summary>
        public static bool LoadDatabaseSynchronously { get; set; }

        /// <summary>선수·구단 마스터 데이터. 아직 읽는 중이면 null.</summary>
        public MasterDatabase Database { get; private set; }
        public bool IsDatabaseReady => Database != null;
        /// <summary>선수 DB를 다 읽었을 때 (비동기 로딩 완료 시점). 동기 로딩이면 Awake 안에서 이미 끝나 있다.</summary>
        public event Action DatabaseReady;

        /// <summary>내 구단 라인업이 바뀌었을 때 (로비 다시 그리기용).</summary>
        public event Action LineupChanged;

        public SaveManager Saves { get; private set; }
        /// <summary>시작할 때 시즌을 어떻게 얻었는지 (불러옴 / 새 시즌 / 손상 복구).</summary>
        public SeasonLoadStatus LoadStatus { get; private set; }

        GameScreenController _game;
        bool _loading;

        /// <summary>없으면 만든다. 씬에 미리 배치하지 않고 첫 진입 화면(로비)에서 호출한다.</summary>
        public static SeasonManager EnsureExists()
        {
            if (Instance != null) return Instance;
            return new GameObject(nameof(SeasonManager)).AddComponent<SeasonManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Saves = new SaveManager(SavePathOverride ?? SaveManager.DefaultPath);
            Season = Saves.LoadOrCreate(SeasonState.CreateDummy, out var status);
            LoadStatus = status;
            if (LoadDatabaseSynchronously) SetDatabase(MasterDatabase.LoadFromResources());
            else StartCoroutine(MasterDatabase.LoadFromResourcesAsync(SetDatabase));
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            DetachGame();
            Instance = null;
        }

        void SetDatabase(MasterDatabase database)
        {
            Database = database;
            DatabaseReady?.Invoke();
        }

        /// <summary>선수 DB의 내 구단 기본 선수단 (라인업 변경 전). DB가 없거나 내 구단 데이터가 없으면 null.</summary>
        public TeamRoster DefaultMyRoster => Database?.GetRoster(Season.MyTeam);

        /// <summary>
        /// 지금 쓰는 라인업 카드: 저장된 사용자 라인업이 DB 선수단과 맞으면 그것, 아니면 DB 기본 라인업.
        /// 선수단이 없으면 null. 돌려준 카드를 고쳐도 시즌에는 반영되지 않는다 (<see cref="SwapLineup"/>을 쓴다).
        /// </summary>
        public LineupCard CurrentLineup
        {
            get
            {
                var roster = DefaultMyRoster;
                if (roster == null) return null;
                var saved = Season.MyLineup;
                return saved != null && saved.Matches(roster) ? saved.Clone() : LineupCard.FromRoster(roster);
            }
        }

        /// <summary>라인업을 적용한 내 구단 선수단. 경기 화면과 로비가 이걸 그린다. 없으면 null.</summary>
        public TeamRoster MyRoster
        {
            get
            {
                var roster = DefaultMyRoster;
                var card = CurrentLineup;
                return roster == null || card == null ? roster : card.ApplyTo(roster);
            }
        }

        /// <summary>
        /// 라인업 두 자리를 맞바꾸고 바로 저장한다. 경기 중·전환 중이거나, 선수단이 없거나,
        /// 바꿀 수 없는 조합(범위 밖·같은 자리·벤치끼리)이면 아무것도 바꾸지 않고 false.
        /// </summary>
        public bool SwapLineup(LineupSpot a, LineupSpot b)
        {
            if (_loading || Season.InProgress != null) return false;
            var card = CurrentLineup;
            if (card == null || !card.TrySwap(a, b)) return false;
            Season.MyLineup = card;
            Saves.Save(Season);
            LineupChanged?.Invoke();
            return true;
        }

        /// <returns>경기 화면으로 넘어가면 true. 이미 전환 중이거나, 선수 DB를 읽는 중이거나, 남은 경기가 없으면 false.</returns>
        public bool StartNextGame()
        {
            if (_loading || !IsDatabaseReady || Season.InProgress != null || Season.NextGame == null) return false;
            Season.StartNextGame();
            Load(GameScene);
            return true;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _loading = false;
            if (scene.name != GameScene) return;
            DetachGame();
            _game = FindAnyObjectByType<GameScreenController>();
            if (_game == null) return;
            _game.ExitRequested += OnGameExitRequested;
            if (Season.InProgress == null) return;
            if (Database != null && !Database.IsEmpty)
                _game.RosterSourceLabel = $"{MasterDatabase.RosterFileName} ({Database.Season})";
            var matchup = MatchupOf(Season.InProgress);
            if (UseGeneratedScenario) _game.LoadGeneratedScenario(matchup, ScenarioRandom);
            else _game.LoadScenario(SeasonGameScenario, matchup);
        }

        /// <summary>
        /// 일정표 경기의 대진. 원정·홈과 팀명은 일정 그대로, 내 구단 쪽을 함께 넘긴다.
        /// 선수 DB에 구단 정보·선수단이 있으면 약칭과 선수단(<see cref="TeamRoster"/>)도 싣는다.
        /// </summary>
        public Matchup MatchupOf(ScheduledGame game)
        {
            var mySide = game.HomeTeam == Season.MyTeam ? TeamSide.Home : TeamSide.Away;
            return new Matchup(TeamInfoOf(game.AwayTeam), TeamInfoOf(game.HomeTeam), mySide,
                RosterOf(game.AwayTeam), RosterOf(game.HomeTeam));
        }

        /// <summary>내 구단은 라인업을 적용한 선수단, 상대는 DB 선수단.</summary>
        TeamRoster RosterOf(string team) => team == Season.MyTeam ? MyRoster : Database?.GetRoster(team);

        TeamInfo TeamInfoOf(string team)
        {
            var data = Database?.FindTeam(team);
            return data != null
                ? new TeamInfo { Name = data.Name, ShortName = data.ShortName }
                : Matchup.TeamFromName(team);
        }

        void OnSceneUnloaded(Scene scene)
        {
            if (scene.name == GameScene) DetachGame();
        }

        void OnGameExitRequested(GameResult result)
        {
            if (_loading) return;
            ExitsHandled++;
            DetachGame();
            if (Season.InProgress != null)
            {
                // 경기 화면은 일정의 원정·홈대로 그리지만, 내 구단 득점 기준으로 다시 맞춰 반영한다.
                var score = Season.ScheduleScore(result.MyRuns, result.OpponentRuns);
                LastScore = score;
                Season.FinishCurrentGame(score, LeagueRandom);
                Saves.Save(Season);
            }
            Load(LobbyScene);
        }

        void DetachGame()
        {
            if (_game != null) _game.ExitRequested -= OnGameExitRequested;
            _game = null;
        }

        void Load(string sceneName)
        {
            _loading = true;
            SceneManager.LoadScene(sceneName);
        }
    }
}
