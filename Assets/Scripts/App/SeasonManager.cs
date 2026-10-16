using System;
using System.Collections.Generic;
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
    ///   구독해 <see cref="AdvanceSeason"/>으로 결과를 시즌 성적·순위에 반영하고 일정을 전진·저장한 뒤 로비로 복귀
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
        /// true면 시즌 경기를 1회부터 Play-by-Play로 직접 진행한다 (<see cref="GameScreenController.LoadFullGame"/>, Phase 20).
        /// false(기본)면 <see cref="UseGeneratedScenario"/>를 따른다 (1~7회 퀵 시뮬레이션 뒤 8회 승부처부터).
        /// 로비의 진행 방식 버튼으로 바꾼다. 저장하지 않는다 (실행마다 기본값).
        /// </summary>
        public bool FullInningMode { get; set; }

        /// <summary>진행 방식이 바뀌었다 (로비 버튼 갱신용).</summary>
        public event Action ModeChanged;

        /// <summary>진행 방식을 바꾼다: 퀵 시뮬(8회~) ↔ 풀 이닝(1회~).</summary>
        public void ToggleFullInningMode()
        {
            FullInningMode = !FullInningMode;
            ModeChanged?.Invoke();
        }

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
        /// <summary>마지막 경기 종료 처리 결과. 이번 실행에서 아직 없으면 null.</summary>
        public PostGameReport LastReport { get; private set; }

        /// <summary>
        /// 타 구단 경기 결과용 난수. 경기 화면의 작전 판정 난수(<see cref="GameScreenController.TacticRandom"/>)와 별개다.
        /// 테스트는 고정 Seed를 넣는다.
        /// </summary>
        public IRandomSource LeagueRandom { get; set; } = SeededRandom.FromClock();

        /// <summary>
        /// 경기 뒤 선수 컨디션 하락 판정 난수 (Phase 22). 타 구단 경기 난수·경기 화면 난수와 분리해, 컨디션 판정이
        /// 순위·경기 결과의 결정론을 흔들지 않게 한다. 테스트는 고정 Seed를 넣는다.
        /// </summary>
        public IRandomSource ConditionRandom { get; set; } = SeededRandom.FromClock();

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

        /// <summary>선수 DB 전체로 매긴 성향 태그 (Phase 23, 케미스트리용). DB를 읽기 전이면 null.</summary>
        public IReadOnlyDictionary<string, ChemistryTag> ChemistryTags { get; private set; }
        public bool IsDatabaseReady => Database != null;
        /// <summary>선수 DB를 다 읽었을 때 (비동기 로딩 완료 시점). 동기 로딩이면 Awake 안에서 이미 끝나 있다.</summary>
        public event Action DatabaseReady;

        /// <summary>내 구단 라인업이 바뀌었을 때 (로비 다시 그리기용).</summary>
        public event Action LineupChanged;

        /// <summary>FA 협상을 시도했을 때 — 예산·TP·영입이 바뀌었을 수 있다 (로비·FA 시장 다시 그리기용, Phase 25).</summary>
        public event Action StoveChanged;

        /// <summary>FA 협상 성공 판정 난수 (리그·컨디션 난수와 분리). 테스트는 고정 값을 넣는다.</summary>
        public IRandomSource NegotiationRandom { get; set; } = SeededRandom.FromClock();

        LeagueRosterBook _book;
        MasterDatabase _bookDatabase;
        SeasonState _bookSeason;
        int _bookSignings = -1;
        int _bookProgression = -1;
        int _bookRookies = -1;

        /// <summary>
        /// 리그 선수단 장부 (선수 DB + 영입 기록, Phase 25). 영입 기록이 바뀌었을 때만 다시 짠다. DB를 읽기 전이면 null.
        /// </summary>
        public LeagueRosterBook Rosters
        {
            get
            {
                if (Database == null) return null;
                if (_book == null || _bookDatabase != Database || _bookSeason != Season || _bookSignings != Season.Signings.Count
                    || _bookProgression != Season.ProgressionVersion || _bookRookies != Season.Rookies.Count)
                {
                    _book = LeagueRosterBook.Build(Database, Season.Signings, Season.Progression, Season.Rookies);
                    (_bookDatabase, _bookSeason, _bookSignings, _bookProgression, _bookRookies) =
                        (Database, Season, Season.Signings.Count, Season.ProgressionVersion, Season.Rookies.Count);
                    // 재정 기준 (Phase 26): 캡 기준은 리그 개막 평균 페이롤 × 1.25, 페이롤은 지금 내 구단 1군 연봉 합계.
                    Season.SetSalaryCapBase(StoveLeague.OpeningCap(Database));
                    Season.SetPayroll(StoveLeague.Payroll(_book.Get(Season.MyTeam), Season.Signings));
                }
                return _book;
            }
        }

        /// <summary>FA 시장 (내 구단이 아닌 선수, WAR 순). 요구액은 지금 팬 만족도 구간 기준.</summary>
        public IReadOnlyList<FreeAgent> Market =>
            StoveLeague.Market(Rosters, Season.MyTeam, FranchiseFinance.MoodOf(Season.FanApproval));

        /// <summary>FA 일반 협상. 시도했으면(막히지 않았으면) 바로 저장하고 알린다.</summary>
        public SigningOutcome Negotiate(FreeAgent agent, long offer)
        {
            if (_loading || Rosters == null) return Blocked(agent);
            var chemistry = ChemistryKinds();
            return Finish(StoveLeague.Negotiate(Season, Rosters, agent, offer, NegotiationRandom), chemistry);
        }

        /// <summary>[확정 영입] (TP 100 소모). 시도했으면 바로 저장하고 알린다.</summary>
        public SigningOutcome SignWithCeiling(FreeAgent agent)
        {
            if (_loading || Rosters == null) return Blocked(agent);
            var chemistry = ChemistryKinds();
            return Finish(StoveLeague.SignWithCeiling(Season, Rosters, agent), chemistry);
        }

        static SigningOutcome Blocked(FreeAgent agent) => new SigningOutcome { Agent = agent, Block = SigningBlock.InGame };

        /// <summary>
        /// 선수 방출 (Phase 26): 선발 로테이션이 아닌 1군 선수 (Phase 30부터 선발 타자도), 위약금(연봉 50%)을 예산에서 내고
        /// 자유계약으로 시장에 돌려보낸다. 사용자 라인업 타순 선수였으면 빈 타순을 같은 수비 위치 선수로 채운 카드를 저장한다.
        /// 방출했으면 바로 저장하고 알린다.
        /// </summary>
        public ReleaseOutcome Release(string playerId)
        {
            if (_loading || Rosters == null) return new ReleaseOutcome { Block = ReleaseBlock.InGame };
            var chemistry = ChemistryKinds();
            var outcome = StoveLeague.Release(Season, Rosters, playerId); // [단독] 방출 뉴스 발행
            if (!outcome.Released) return outcome;
            _ = Rosters; // 장부·페이롤 다시 짜기
            if (Season.MyLineup != null) Season.MyLineup = CurrentLineup; // 고친 카드 (못 고치면 기본 라인업 카드)
            PublishChemistry(chemistry);
            Saves.Save(Season);
            StoveChanged?.Invoke();
            LineupChanged?.Invoke();
            NewsChanged?.Invoke();
            return outcome;
        }

        /// <summary>내 구단이 경기를 치를 수 없는 선수단이면 이유 (<see cref="RosterLimits.GameReadinessProblem"/>), 아니면 null.</summary>
        public string RosterProblem => RosterLimits.GameReadinessProblem(MyRoster);

        /// <summary>협상 뒤 (영입 [오피셜] / 결렬 [속보] 뉴스는 StoveLeague가 발행): 장부를 다시 짜고 새 케미스트리 뉴스, 저장, 알림.</summary>
        SigningOutcome Finish(SigningOutcome outcome, IReadOnlyList<ChemistryKind> chemistryBefore)
        {
            if (outcome.Block != SigningBlock.None) return outcome;
            _ = Rosters; // 영입했으면 장부·페이롤 다시 짜기
            if (outcome.Signed) PublishChemistry(chemistryBefore);
            Saves.Save(Season);
            StoveChanged?.Invoke();
            if (outcome.Signed) LineupChanged?.Invoke();
            NewsChanged?.Invoke();
            return outcome;
        }

        /// <summary>리그 뉴스가 늘었다 (로비 뉴스 패널 다시 그리기용, Phase 28).</summary>
        public event Action NewsChanged;

        /// <summary>지금 라인업으로 발동 중인 내 구단 케미스트리. 선수단·태그를 모르면 빈 목록.</summary>
        IReadOnlyList<ChemistryKind> ChemistryKinds()
        {
            var roster = MyRoster;
            return roster == null || ChemistryTags == null ? new List<ChemistryKind>() : ChemistryRules.Evaluate(roster, ChemistryTags).Kinds;
        }

        /// <summary>선수단이 바뀐 뒤 새로 발동한 케미스트리를 [라인업] 뉴스로 낸다 (저장은 부른 쪽이).</summary>
        void PublishChemistry(IReadOnlyList<ChemistryKind> before)
        {
            var roster = MyRoster;
            if (roster == null || ChemistryTags == null) return;
            foreach (var n in NewsWriter.NewChemistry(Season.MyTeam, before, ChemistryRules.Evaluate(roster, ChemistryTags), Season.Year, Season.NewsDate))
                Season.AddNews(n);
        }

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
            // 정규시즌을 다 치른 이전 저장 파일(Phase 32 이전)은 한국시리즈부터 이어간다.
            if (Season.BeginPostseasonIfDue()) Saves.Save(Season);
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
            ChemistryTags = database == null ? null : ChemistryTagger.Tag(database.Players);
            DatabaseReady?.Invoke();
        }

        /// <summary>선수 DB + 영입 기록의 내 구단 기본 선수단 (라인업 변경 전). DB가 없거나 내 구단 데이터가 없으면 null.</summary>
        public TeamRoster DefaultMyRoster => Rosters?.Get(Season.MyTeam);

        /// <summary>
        /// 지금 쓰는 라인업 카드: 저장된 사용자 라인업이 DB 선수단과 맞으면 그것, 아니면 DB 기본 라인업.
        /// Phase 30: 방출 등으로 카드 선수가 빠졌으면 먼저 카드를 고쳐 쓴다 (<see cref="LineupCard.Repair"/> — 빠진 타순을 같은 수비
        /// 위치 선수로 채우고 나머지 타순은 그대로). 고칠 수 없으면 기본 라인업.
        /// 선수단이 없으면 null. 돌려준 카드를 고쳐도 시즌에는 반영되지 않는다 (<see cref="SwapLineup"/>을 쓴다).
        /// </summary>
        public LineupCard CurrentLineup
        {
            get
            {
                var roster = DefaultMyRoster;
                if (roster == null) return null;
                var saved = Season.MyLineup;
                return saved?.Repair(roster) ?? LineupCard.FromRoster(roster);
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
            var chemistry = ChemistryKinds();
            Season.MyLineup = card;
            PublishChemistry(chemistry);
            Saves.Save(Season);
            LineupChanged?.Invoke();
            NewsChanged?.Invoke();
            return true;
        }

        /// <summary>오프시즌 에이징 커브·은퇴 판정 난수 (Phase 29, 다른 난수와 분리). 테스트는 고정 Seed를 넣는다.</summary>
        public IRandomSource AgingRandom { get; set; } = SeededRandom.FromClock();

        /// <summary>오프시즌 결산으로 새 시즌이 시작됐다 (로비 다시 그리기용, Phase 27).</summary>
        public event Action SeasonRolledOver;

        /// <summary>마지막 오프시즌 결산 요약. 이번 실행에서 아직 없으면 null.</summary>
        public SeasonRollover LastRollover { get; private set; }

        /// <summary>
        /// 결산 직후 로비가 한 번 띄울 스프링캠프 리포트 (Phase 33). 닫으면(<see cref="DismissCampReport"/>) null. 저장하지 않는다.
        /// </summary>
        public SpringCampReport PendingCampReport { get; private set; }

        public void DismissCampReport() => PendingCampReport = null;

        /// <summary>캠프 포인트·특별 훈련이 바뀌었다 (로비 헤더·선수 상세 다시 그리기용, Phase 33).</summary>
        public event Action CampChanged;

        /// <summary>
        /// 스프링캠프 특별 훈련 (Phase 33, <see cref="SpringCamp.Train"/>): CP 1로 24세 이하 내 선수의 폼 +1%p.
        /// 훈련했으면 장부를 다시 짜고 바로 저장한 뒤 알린다. 막히면 아무것도 바꾸지 않는다.
        /// </summary>
        public TrainingOutcome Train(string playerId)
        {
            if (_loading || Rosters == null) return new TrainingOutcome { PlayerId = playerId, Block = TrainingBlock.InGame, PointsLeft = Season.CampPoints };
            var outcome = SpringCamp.Train(Season, Rosters, playerId);
            if (!outcome.Trained) return outcome;
            _ = Rosters; // 폼을 덧씌운 장부로
            Saves.Save(Season);
            LineupChanged?.Invoke();
            CampChanged?.Invoke();
            return outcome;
        }

        /// <summary>CPU끼리의 한국시리즈 결과 시뮬레이션 난수 (Phase 32, 다른 난수와 분리). 테스트는 고정 Seed를 넣는다.</summary>
        public IRandomSource PostseasonRandom { get; set; } = SeededRandom.FromClock();

        /// <summary>한국시리즈가 결과만으로 진행됐다 (로비 다시 그리기용, Phase 32).</summary>
        public event Action PostseasonAdvanced;

        /// <summary>
        /// [가을야구 결과 시뮬레이션] (Phase 32): 내 구단이 탈락한 한국시리즈를 결과만으로 끝내고(<see cref="SeasonState.SimulatePostseason"/>)
        /// 오프시즌으로 넘긴 뒤 바로 저장한다. 내 구단이 나가는 시리즈는 직접 치러야 하므로 false. 포스트시즌이 아니거나 경기 중이어도 false.
        /// </summary>
        public bool SimulatePostseason()
        {
            if (_loading || Season.InProgress != null || Season.Phase != SeasonPhase.PostSeason || Season.InPostseason) return false;
            if (!Season.SimulatePostseason(PostseasonRandom)) return false;
            Saves.Save(Season);
            NewsChanged?.Invoke();
            PostseasonAdvanced?.Invoke();
            return true;
        }

        /// <summary>
        /// 시즌 결산 및 다음 시즌 준비 (Phase 27, <see cref="SeasonState.TransitionToNextSeason"/>): 연도를 넘기고 전적·일정을
        /// 새로 짠 뒤 바로 저장한다. 예산·팬 만족도·TP·팬덤 슬롯·선수단(이적 기록)은 이월된다.
        /// 정규시즌이 남았거나 경기 중·전환 중이면 아무것도 하지 않고 null.
        /// </summary>
        public SeasonRollover TransitionToNextSeason()
        {
            // Phase 32: 한국시리즈 우승 팀이 정해진 오프시즌에만 결산한다.
            if (_loading || Season.InProgress != null || Season.Phase != SeasonPhase.OffSeason) return null;
            // 선수 DB를 읽었으면 에이징 커브·은퇴까지 (Phase 29). 장부는 결산 직전 것 (나이·폼 덧씌운 상태).
            LastRollover = Season.TransitionToNextSeason(Rosters, AgingRandom);
            PendingCampReport = LastRollover.Camp;
            LastReport = null; // 직전 경기 재정 변화는 지난 시즌 것
            LastScore = null;
            _ = Rosters; // 캡 기준·페이롤 다시 맞추기 (선수단은 그대로)
            Saves.Save(Season); // 결산 뉴스는 SeasonState가 발행
            SeasonRolledOver?.Invoke();
            return LastRollover;
        }

        /// <returns>경기 화면으로 넘어가면 true. 이미 전환 중이거나, 선수 DB를 읽는 중이거나, 남은 경기가 없으면 false.</returns>
        public bool StartNextGame()
        {
            if (_loading || !IsDatabaseReady || Season.InProgress != null || Season.NextGame == null) return false;
            if (RosterProblem != null) return false; // 타순·로테이션 펑크 (Phase 26)
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
            _game.Conditions = Season;
            _game.ChemistryTags = ChemistryTags;
            if (FullInningMode) _game.LoadFullGame(matchup);
            else if (UseGeneratedScenario) _game.LoadGeneratedScenario(matchup, ScenarioRandom);
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

        /// <summary>
        /// 경기에 나서는 선수단: 내 구단은 라인업을 적용한 선수단, 상대는 DB 선수단. 두 쪽 모두 오늘 선발은
        /// 직전 선발 다음 순번 중 컨디션이 보통 이상인 투수(<see cref="RotationPicker"/>)다. 원본 선수단은 바꾸지 않는다.
        /// </summary>
        public TeamRoster GameRosterOf(string team) =>
            RotationPicker.Apply(team == Season.MyTeam ? MyRoster : Rosters?.Get(team), Season, Season.LastStarterOf(team));

        TeamRoster RosterOf(string team) => GameRosterOf(team);

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
            // 경기 화면은 일정의 원정·홈대로 그리지만, 내 구단 득점 기준으로 다시 맞춰 반영한다.
            if (Season.InProgress != null)
                AdvanceSeason(Season.ScheduleScore(result.MyRuns, result.OpponentRuns), result.Participation, result.FinalInning, result.WalkOff);
            Load(LobbyScene);
        }

        /// <summary>
        /// 진행 중인 경기를 최종 점수로 끝내고 시즌을 다음 경기로 전진한 뒤 바로 저장한다
        /// (<see cref="PostGameProcessor"/>: 승/패/무 기록 → 타 구단 경기 난수 반영 → 순위 재정렬 → 일정 전진 → 저장).
        /// 씬 전환은 하지 않는다. 진행 중인 경기가 없으면 null.
        /// </summary>
        /// <param name="finalScore">일정의 원정 : 홈 기준 최종 점수.</param>
        /// <param name="participation">출장 기록. 있으면 두 구단 선수 컨디션을 바꾼다 (<see cref="ConditionRandom"/>).</param>
        /// <param name="finalInning">경기가 끝난 이닝 (뉴스용, 모르면 0).</param>
        /// <param name="walkOff">끝내기로 끝났는지 (뉴스용).</param>
        public PostGameReport AdvanceSeason(GameScore finalScore, GameParticipation participation = null, int finalInning = 0, bool walkOff = false)
        {
            if (Season.InProgress == null) return null;
            LastScore = finalScore;
            _ = Rosters; // 정산 전에 페이롤을 지금 선수단에 맞춘다
            LastReport = PostGameProcessor.Process(Season, finalScore, LeagueRandom, Saves, participation, ConditionRandom, finalInning, walkOff);
            NewsChanged?.Invoke();
            return LastReport;
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
