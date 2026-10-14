using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DailyFranchise.App
{
    /// <summary>
    /// 씬을 넘어 시즌 상태를 유지하고 로비 ↔ 경기 화면 전환을 중계한다.
    /// 로비와 경기 화면은 서로를 모르며, 이 객체만 두 쪽을 안다.
    /// - 로비: StartNextGame() 호출
    /// - 경기 화면: 시즌 경기 장면(작전 승부처)을 불러오고, GameScreenController.ExitRequested(최종 점수)를 구독해
    ///   결과를 시즌 성적·순위에 반영한 뒤 로비로 복귀
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
            Season = SeasonState.CreateDummy();
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
            if (Season.InProgress != null) _game.LoadScenario(SeasonGameScenario);
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
                // 경기 화면은 더미 데이터라 내 구단을 늘 홈으로 그린다. 일정의 원정·홈 기준으로 바꿔 반영한다.
                var score = Season.ScheduleScore(result.MyRuns, result.OpponentRuns);
                LastScore = score;
                Season.FinishCurrentGame(score, LeagueRandom);
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
