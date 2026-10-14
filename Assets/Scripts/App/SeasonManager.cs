using DailyFranchise.Sim;
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
    /// - 경기 화면: 일정표의 대진으로 시즌 경기 장면(작전 승부처)을 불러오고, GameScreenController.ExitRequested(최종 점수)를
    ///   구독해 결과를 시즌 성적·순위에 반영·저장한 뒤 로비로 복귀
    /// - 저장: 처음 만들어질 때(로비 최초 진입) 저장 파일이 있으면 이어서, 없으면 새 시즌. 매 경기 결과 반영 직후 저장.
    /// </summary>
    public sealed class SeasonManager : MonoBehaviour
    {
        public const string LobbyScene = "LobbyScreen";
        public const string GameScene = "GameScreen";
        /// <summary>시즌 경기로 재생할 장면. 고정 시나리오 단계라 모든 경기가 같은 승부처 장면이다.</summary>
        public const string SeasonGameScenario = ScenarioCatalog.EighthBottomTactics;

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

        /// <returns>경기 화면으로 넘어가면 true. 이미 전환 중이거나 남은 경기가 없으면 false.</returns>
        public bool StartNextGame()
        {
            if (_loading || Season.InProgress != null || Season.NextGame == null) return false;
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
            if (Season.InProgress != null) _game.LoadScenario(SeasonGameScenario, MatchupOf(Season.InProgress));
        }

        /// <summary>일정표 경기의 대진. 원정·홈과 팀명은 일정 그대로, 내 구단 쪽을 함께 넘긴다.</summary>
        public Matchup MatchupOf(ScheduledGame game) =>
            Matchup.FromNames(game.AwayTeam, game.HomeTeam,
                game.HomeTeam == Season.MyTeam ? TeamSide.Home : TeamSide.Away);

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
