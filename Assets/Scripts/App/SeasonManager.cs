using DailyFranchise.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DailyFranchise.App
{
    /// <summary>
    /// 씬을 넘어 시즌 상태를 유지하고 로비 ↔ 경기 화면 전환을 중계한다.
    /// 로비와 경기 화면은 서로를 모르며, 이 객체만 두 쪽을 안다.
    /// - 로비: StartNextGame() 호출
    /// - 경기 화면: GameScreenController.ExitRequested 발생 → 이 객체가 구독해 로비로 복귀
    /// </summary>
    public sealed class SeasonManager : MonoBehaviour
    {
        public const string LobbyScene = "LobbyScreen";
        public const string GameScene = "GameScreen";

        public static SeasonManager Instance { get; private set; }

        public SeasonState Season { get; private set; }
        /// <summary>처리한 경기 종료 요청 수 (중복 구독 진단용).</summary>
        public int ExitsHandled { get; private set; }

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
            if (_game != null) _game.ExitRequested += OnGameExitRequested;
        }

        void OnSceneUnloaded(Scene scene)
        {
            if (scene.name == GameScene) DetachGame();
        }

        void OnGameExitRequested()
        {
            if (_loading) return;
            ExitsHandled++;
            DetachGame();
            if (Season.InProgress != null) Season.FinishCurrentGame();
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
