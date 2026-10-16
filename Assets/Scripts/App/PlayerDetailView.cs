using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.UI.Data;
using UnityEngine.UIElements;

namespace DailyFranchise.App
{
    /// <summary>
    /// 로비 선수 상세 모달 (Phase 31). 순수 C# 뷰 — 로비(<see cref="LobbyController"/>)가 만들고, 라인업·벤치 행, 투수진,
    /// 오늘의 결정 항목을 누르면 연다. 선수단은 SeasonManager가 짠 것을 읽기만 한다.
    /// - 머리: 이름 · 등번호·나이·투타·포지션 · 지금 역할 ("선발 3번 타자", "벤치", "선발 로테이션 2번", "불펜").
    /// - 컨디션: 단계와 회복 안내 (쉬면 경기마다 +1).
    /// - 기록 타일 (지난 시즌): 타자 타율·출루율·OPS·홈런·도루·WAR, 투수 ERA·FIP·WHIP·승패·세이브/홀드·WAR.
    /// - 교체: 선발 타자는 벤치 선수, 벤치 선수는 선발 타자와 바로 맞바꾸는 버튼 (같은 수비군·컨디션 좋은 순).
    ///   [직접 고르기]는 모달을 닫고 로비의 Click-to-Swap 선택 상태로 넘긴다.
    /// - 방출 (위험 구역): 첫 누름은 확인 상태(위약금 표시)로만 바뀌고, 한 번 더 눌러야 방출한다. 막히면 잠그고 이유를 보인다.
    /// - 타 구단 선수 (Phase 34, FA 모달의 타 구단 명단에서): 기록·컨디션만 보이고 교체·훈련·방출 대신 [트레이드 제안].
    ///   누르면 보낼 내 선수 목록(1군 타자 · 투수 · 퓨처스, 가치·차단 이유)이 열리고, 고르면 즉시 평가한다.
    ///   거절·차단이면 모달을 열어 둔 채 이유를 보이고, 성사되면 닫는다 (로비가 결과를 안내).
    /// </summary>
    public sealed class PlayerDetailView
    {
        readonly VisualElement _modal;
        readonly SeasonManager _manager;
        readonly Func<string, ReleaseOutcome> _release;
        readonly Action<LineupSpot> _pick;
        readonly Button _close;
        readonly Button _pickButton;
        readonly Button _releaseButton;
        readonly Button _trainButton;
        readonly Button _tradeButton;
        readonly Button[] _offerGroups;
        readonly Button _offerPrev;
        readonly Button _offerNext;
        readonly Func<string, string, TradeOutcome> _trade;
        readonly VisualElement _root;
        readonly VisualElement _window;

        string _themeClass;
        string _tradeText = "";
        bool _tradeRejected;

        /// <param name="trade">[트레이드 제안]에서 보낼 선수를 골랐을 때 (내 선수 ID, 받을 선수 ID) — 로비가 SeasonManager에 제안하고 결과를 안내한다.</param>
        public PlayerDetailView(VisualElement root, SeasonManager manager, Func<string, ReleaseOutcome> release, Action<LineupSpot> pick,
            Func<string, string, TradeOutcome> trade)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _release = release ?? throw new ArgumentNullException(nameof(release));
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
            _trade = trade ?? throw new ArgumentNullException(nameof(trade));
            _window = root.Q("pd-window");
            _tradeButton = root.Q<Button>("pd-trade");
            _tradeButton.clicked += OnTrade;
            _offerGroups = Enumerable.Range(0, OfferGroupTitles.Length).Select(i => root.Q<Button>("pd-offer-group-" + i)).ToArray();
            for (int i = 0; i < _offerGroups.Length; i++) _offerGroups[i].userData = i;
            foreach (var b in _offerGroups) b.clicked += GroupHandler(b);
            _offerPrev = root.Q<Button>("pd-offer-prev");
            _offerNext = root.Q<Button>("pd-offer-next");
            _offerPrev.clicked += PrevOfferPage;
            _offerNext.clicked += NextOfferPage;
            _modal = root.Q("player-modal");
            _close = root.Q<Button>("pd-close");
            _pickButton = root.Q<Button>("pd-pick");
            _releaseButton = root.Q<Button>("pd-release");
            _trainButton = root.Q<Button>("pd-train");
            _close.clicked += Close;
            _pickButton.clicked += OnPick;
            _releaseButton.clicked += OnRelease;
            _trainButton.clicked += OnTrain;
            _modal.RegisterCallback<ClickEvent>(OnBackdrop);
        }

        public void Detach()
        {
            _close.clicked -= Close;
            _pickButton.clicked -= OnPick;
            _releaseButton.clicked -= OnRelease;
            _trainButton.clicked -= OnTrain;
            _tradeButton.clicked -= OnTrade;
            for (int i = 0; i < _offerGroups.Length; i++) _offerGroups[i].clicked -= _groupHandlers[i];
            _offerPrev.clicked -= PrevOfferPage;
            _offerNext.clicked -= NextOfferPage;
            _modal.UnregisterCallback<ClickEvent>(OnBackdrop);
        }

        readonly List<Action> _groupHandlers = new List<Action>();

        Action GroupHandler(Button b)
        {
            int group = (int)b.userData;
            Action handler = () => ShowOfferGroup(group);
            _groupHandlers.Add(handler);
            return handler;
        }

        /// <summary>보낼 선수 묶음 (탭 순서): 1군 타자 · 1군 투수 · 퓨처스.</summary>
        static readonly string[] OfferGroupTitles = { "1군 타자", "1군 투수", "퓨처스" };

        /// <summary>보낼 선수 한 쪽의 버튼 수 (2열 × 7줄 — 1080p 한 화면, 스크롤 없음).</summary>
        public const int OfferPageSize = 14;

        /// <summary>보낼 선수 목록의 지금 묶음 (0 1군 타자 · 1 1군 투수 · 2 퓨처스).</summary>
        public int OfferGroup { get; private set; }

        /// <summary>지금 묶음의 쪽 (0부터).</summary>
        public int OfferPage { get; private set; }

        void PrevOfferPage() => StepOfferPage(-1);
        void NextOfferPage() => StepOfferPage(1);

        /// <summary>보낼 선수 묶음을 바꾼다 (첫 쪽부터).</summary>
        public void ShowOfferGroup(int group)
        {
            OfferGroup = Math.Max(0, Math.Min(OfferGroupTitles.Length - 1, group));
            OfferPage = 0;
            if (IsOpen) Render();
        }

        /// <summary>보낼 선수 쪽을 넘긴다 (처음·끝에서 멈춘다 — 그리면서 맞춘다).</summary>
        public void StepOfferPage(int delta)
        {
            OfferPage = Math.Max(0, OfferPage + delta);
            if (IsOpen) Render();
        }

        static bool InGroup(TradeTarget o, int group) =>
            group == 2 ? o.Futures : !o.Futures && o.Player.IsPitcher == (group == 1);

        public bool IsOpen => _modal.ClassListContains("pd-modal--open");

        /// <summary>보고 있는 선수 ID (닫혀 있으면 null).</summary>
        public string PlayerId { get; private set; }

        /// <summary>[방출]을 한 번 눌러 확인 상태인지.</summary>
        public bool ReleaseArmed { get; private set; }

        /// <summary>타 구단 선수를 보고 있는지 (Phase 34 — 트레이드 대상).</summary>
        public bool IsOtherTeam => Team != null;

        /// <summary>보고 있는 타 구단 선수의 소속. 내 선수거나 닫혀 있으면 null.</summary>
        public string Team { get; private set; }

        /// <summary>[트레이드 제안]을 눌러 보낼 선수를 고르는 중인지.</summary>
        public bool TradePicking { get; private set; }

        void OnBackdrop(ClickEvent e)
        {
            if (e.target == _modal) Close();
        }

        public void Close()
        {
            _modal.RemoveFromClassList("pd-modal--open");
            PlayerId = null;
            ReleaseArmed = false;
            Team = null;
            TradePicking = false;
            _tradeText = "";
            SetTheme(null);
        }

        /// <summary>창 테두리를 그 구단 컬러로 (타 구단 선수). null이면 내 구단 컬러(화면 루트 테마).</summary>
        void SetTheme(string team)
        {
            if (_themeClass != null) _window.RemoveFromClassList(_themeClass);
            _themeClass = team == null ? null : LobbyBriefing.TeamThemeClass(team);
            if (_themeClass != null) _window.AddToClassList(_themeClass);
        }

        /// <summary>1군과 퓨처스 선수 (Phase 33부터 퓨처스도 연다 — 특별 훈련).</summary>
        static IEnumerable<PlayerData> Everyone(TeamRoster roster) => LeagueRosterBook.AllOf(roster).Concat(roster.Reserves);

        /// <summary>
        /// 내 구단 1군·퓨처스 선수, 또는 타 구단 선수(Phase 34 — 트레이드 대상)의 상세를 연다.
        /// 어디에도 없거나(자유계약·대체 선수 포함) 경기 중이면 열지 않고 false.
        /// </summary>
        public bool Open(string playerId)
        {
            var roster = _manager.MyRoster;
            if (roster == null || playerId == null || _manager.Season.InProgress != null) return false;
            string team = null;
            if (Everyone(roster).All(p => p.Id != playerId))
            {
                team = _manager.TradeTargets.FirstOrDefault(t => t.Id == playerId)?.Team;
                if (team == null) return false;
            }
            PlayerId = playerId;
            ReleaseArmed = false;
            Team = team;
            TradePicking = false;
            _tradeText = "";
            SetTheme(team);
            _modal.AddToClassList("pd-modal--open");
            Render();
            return true;
        }

        /// <summary>지금 선수단 기준 이 선수의 자리 (선발 타순·벤치). 투수거나 없으면 null.</summary>
        LineupSpot? SpotOf(TeamRoster roster, string id)
        {
            var batter = roster.Lineup.FirstOrDefault(p => p.Id == id);
            if (batter != null) return LineupSpot.Batting(batter.Order);
            int bench = roster.Bench.ToList().FindIndex(p => p.Id == id);
            return bench >= 0 ? LineupSpot.Bench(bench) : (LineupSpot?)null;
        }

        void Render()
        {
            var roster = IsOtherTeam ? _manager.Rosters?.Get(Team) : _manager.MyRoster;
            var p = roster == null ? null : Everyone(roster).FirstOrDefault(x => x.Id == PlayerId);
            if (p == null)
            {
                Close(); // 방출·은퇴·트레이드 등으로 선수단에서 빠졌다
                return;
            }
            var season = _manager.Season;
            int cond = season.ConditionOf(p.Id);

            Q<Label>("pd-name").text = p.Name;
            Q<Label>("pd-meta").text = string.Join(" · ", new[]
            {
                p.Number > 0 ? $"No.{p.Number}" : null, p.Age > 0 ? $"{p.Age}세" : null,
                ScenarioRosterSource.HandednessText(p), PositionMatcher.Label(p.Position),
            }.Where(s => s != null));
            Q<Label>("pd-role").text = IsOtherTeam ? $"{Team} · {FaMarketView.RoleText(roster, p, roster.Reserves.Contains(p))}" : RoleText(roster, p);

            var condition = Q<Label>("pd-condition");
            condition.text = "컨디션 " + PlayerCondition.Label(cond);
            for (int i = PlayerCondition.Min; i <= PlayerCondition.Max; i++) condition.EnableInClassList("pd-cond--" + i, i == cond);
            Q<Label>("pd-condition-note").text = ConditionNote(p, cond);

            var stats = Q("pd-stats");
            stats.Clear();
            var tiles = StatTiles(p);
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = new VisualElement { name = "pd-stat-" + i };
                tile.AddToClassList("pd-stat");
                tile.Add(Cell(tiles[i].label, "pd-stat__label"));
                tile.Add(Cell(tiles[i].value, "pd-stat__value"));
                stats.Add(tile);
            }
            Q<Label>("pd-season").text = "지난 시즌 · " + ScenarioRosterSource.SeasonLineText(p)
                + (Math.Abs(p.Form) >= 0.005 ? $" · 에이징 폼 {AgingCurve.FormText(p.Form)}" : "");

            bool other = IsOtherTeam;
            Show("pd-swap-title", !other);
            Show("pd-swap", !other);
            Show("pd-train-row", !other);
            Show("pd-train-note", !other);
            Show("pd-danger", !other);
            Show("pd-trade-zone", other);
            if (other)
            {
                _pickButton.style.display = DisplayStyle.None;
                RenderTrade(p);
                return;
            }
            RenderSwap(roster, p);
            RenderTraining(p);
            RenderRelease(p, roster.Reserves.Contains(p));
        }

        void Show(string name, bool visible) => Q(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>트레이드 구역: 이 선수의 가치·연봉·CPU 요구 가치, [트레이드 제안], 고르는 중이면 보낼 내 선수 목록.</summary>
        void RenderTrade(PlayerData p)
        {
            var season = _manager.Season;
            var target = _manager.TradeTargets.FirstOrDefault(t => t.Id == p.Id);
            double value = target?.Value ?? 0;
            Q<Label>("pd-trade-note").text = target == null ? ""
                : $"트레이드 가치 {TradeEvaluator.ValueText(value)} · 연봉 {Won(target.Salary)} · " +
                  $"CPU 요구 가치 {TradeEvaluator.ValueText(TradeEvaluator.RequiredValue(value))} 이상 (프리미엄 5%)";
            _tradeButton.text = TradePicking ? "제안 취소" : "트레이드 제안";
            _tradeButton.SetEnabled(season.InProgress == null);
            var result = Q<Label>("pd-trade-result");
            result.text = _tradeText;
            result.EnableInClassList("pd-trade__result--rejected", _tradeText.Length > 0 && _tradeRejected);
            result.EnableInClassList("pd-trade__result--blocked", _tradeText.Length > 0 && !_tradeRejected);
            result.style.display = _tradeText.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            var list = Q("pd-trade-list");
            Show("pd-trade-groups", TradePicking);
            list.style.display = TradePicking ? DisplayStyle.Flex : DisplayStyle.None;
            list.Clear();
            if (!TradePicking)
            {
                Show("pd-trade-pager", false);
                return;
            }
            var offers = _manager.TradeOffers;
            var mine = _manager.Rosters?.Get(season.MyTeam);
            for (int g = 0; g < _offerGroups.Length; g++)
            {
                int count = offers.Count(o => InGroup(o, g));
                _offerGroups[g].text = $"{OfferGroupTitles[g]} ({count})";
                _offerGroups[g].SetEnabled(count > 0);
                _offerGroups[g].EnableInClassList("pd-offer-group--active", g == OfferGroup);
            }
            var items = offers.Where(o => InGroup(o, OfferGroup)).ToList();
            int pages = Math.Max(1, (items.Count + OfferPageSize - 1) / OfferPageSize);
            OfferPage = Math.Min(OfferPage, pages - 1);
            Show("pd-trade-pager", pages > 1);
            Q<Label>("pd-offer-page").text = $"{OfferPage + 1} / {pages}쪽";
            _offerPrev.SetEnabled(OfferPage > 0);
            _offerNext.SetEnabled(OfferPage < pages - 1);
            AddOffers(list, items.Skip(OfferPage * OfferPageSize).Take(OfferPageSize), mine, p);
        }

        void AddOffers(VisualElement list, IEnumerable<TradeTarget> offers, TeamRoster mine, PlayerData theirs)
        {
            foreach (var o in offers)
            {
                var q = _manager.QuoteTrade(o.Id, theirs.Id);
                string id = o.Id;
                var button = new Button(() => ProposeTrade(id))
                {
                    name = "pd-offer-" + id,
                    text = $"{o.Player.Name} · {FaMarketView.RoleText(mine, o.Player, o.Futures)} · {o.Player.Position}" +
                           (o.Player.Age > 0 ? $" · {o.Player.Age}세" : "") + $" · 가치 {TradeEvaluator.ValueText(o.Value)}",
                };
                button.AddToClassList("btn");
                button.AddToClassList("pd-offer");
                bool open = q != null && q.Block == TradeBlock.None;
                button.SetEnabled(open);
                button.EnableInClassList("pd-offer--enough", open && q.WouldAccept);
                button.tooltip = q == null ? "" : !open ? TradeDesk.BlockText(q.Block)
                    : $"연봉 {Won(q.MySalary)} · 페이롤 {FranchiseFinance.BudgetDeltaText(q.PayrollDelta)} · {theirs.Name} → {TradeDesk.ArrivalText(q.Arrival)}";
                list.Add(button);
            }
        }

        void OnTrade() => PressTrade();

        /// <summary>[트레이드 제안] / [제안 취소]: 보낼 내 선수 목록을 열고 닫는다. 타 구단 선수가 아니면 false.</summary>
        public bool PressTrade()
        {
            if (!IsOtherTeam || PlayerId == null) return false;
            TradePicking = !TradePicking;
            _tradeText = "";
            OfferGroup = 0;
            OfferPage = 0;
            Render();
            return true;
        }

        /// <summary>
        /// 보낼 내 선수를 골라 제안한다 (로비 → SeasonManager). 거절("[거절] …")·차단이면 모달을 열어 둔 채 이유를 보이고,
        /// 성사되면 모달을 닫는다. 타 구단 선수를 보고 있지 않으면 null.
        /// </summary>
        public TradeOutcome ProposeTrade(string myPlayerId)
        {
            if (!IsOtherTeam || PlayerId == null) return null;
            var outcome = _trade(myPlayerId, PlayerId);
            if (outcome == null) return null;
            if (outcome.Accepted)
            {
                Close();
                return outcome;
            }
            _tradeText = outcome.Rejected ? TradeDesk.RejectedText : TradeDesk.BlockText(outcome.Block);
            _tradeRejected = outcome.Rejected;
            if (IsOpen) Render();
            return outcome;
        }

        /// <summary>마지막 제안 결과 문구 (거절·차단). 없으면 "".</summary>
        public string TradeResultText => _tradeText;

        static string Won(long won) => (won < 0 ? "−₩" : "₩") + FranchiseFinance.Won(won);

        /// <summary>
        /// 에이징 폼 게이지(−10% ~ +10%, 가운데 0)와 스프링캠프 특별 훈련 (Phase 33). 훈련 버튼은 24세 이하만 보이고,
        /// 막히면 잠그고 이유와 보유 CP를 보인다.
        /// </summary>
        void RenderTraining(PlayerData p)
        {
            double form = p.Form, max = ProgressionModifier.MaxForm;
            var label = Q<Label>("pd-form");
            label.text = "에이징 폼 " + AgingCurve.FormText(form);
            label.EnableInClassList("pd-form--up", form >= 0.005);
            label.EnableInClassList("pd-form--down", form <= -0.005);
            var fill = Q("pd-form-fill");
            fill.style.width = Length.Percent((float)(100 * (form + max) / (2 * max)));
            fill.EnableInClassList("pd-form-fill--up", form >= 0.005);
            fill.EnableInClassList("pd-form-fill--down", form <= -0.005);

            bool young = p.Age > 0 && p.Age <= SpringCamp.MaxTrainingAge;
            _trainButton.style.display = young ? DisplayStyle.Flex : DisplayStyle.None;
            var block = SpringCamp.Check(_manager.Season, _manager.Rosters, p.Id);
            _trainButton.SetEnabled(block == TrainingBlock.None);
            _trainButton.text = $"스프링캠프 특별 훈련 (CP {SpringCamp.TrainingCost} 소모)";
            int cp = _manager.Season.CampPoints;
            Q<Label>("pd-train-note").text = !young ? $"특별 훈련은 {SpringCamp.MaxTrainingAge}세 이하 유망주만 받을 수 있습니다. 보유 {cp} CP"
                : block == TrainingBlock.None ? $"보유 {cp} CP · 훈련하면 폼 +1%p (상한 +{(int)Math.Round(max * 100)}%)"
                : $"{SpringCamp.BlockText(block)} 보유 {cp} CP";
        }

        void OnTrain() => PressTrain();

        /// <summary>[스프링캠프 특별 훈련]: CP 1로 폼 +1%p (SeasonManager가 저장·알림). 모달은 열어 둔 채 다시 그린다.</summary>
        public TrainingOutcome PressTrain()
        {
            if (PlayerId == null) return null;
            var outcome = _manager.Train(PlayerId);
            if (IsOpen) Render();
            return outcome;
        }

        static string RoleText(TeamRoster roster, PlayerData p)
        {
            if (roster.Reserves.Contains(p)) return RookieGenerator.IsRookie(p) ? $"퓨처스 · {p.Memo}" : "퓨처스";
            if (roster.Lineup.Contains(p)) return $"선발 {p.Order}번 타자";
            if (roster.Bench.Contains(p)) return "벤치";
            if (roster.Starters.Contains(p)) return $"선발 로테이션 {p.Order}번";
            return "불펜";
        }

        /// <summary>컨디션 안내 (<see cref="ConditionRules"/>: 쉬면 +1, 야수는 출장 시 25% 확률 −1, 투수는 30구 −1 · 60구 −2).</summary>
        public static string ConditionNote(PlayerData p, int condition)
        {
            if (p.IsPitcher)
                return condition < RotationPicker.MinStartCondition
                    ? "보통 미만이면 선발 순번을 건너뜁니다. 등판하지 않으면 경기마다 한 단계 회복합니다."
                    : "30구 이상 던지면 한 단계, 60구 이상이면 두 단계 떨어집니다.";
            switch (PlayerCondition.Clamp(condition))
            {
                case PlayerCondition.Exhausted: return "휴식이 필요합니다. 벤치로 내리면 경기마다 한 단계씩 회복합니다.";
                case PlayerCondition.Tired: return "벤치에서 한 경기 쉬면 보통으로 회복합니다.";
                case PlayerCondition.Excellent: return "최상의 컨디션입니다. 선발 기용을 고려하세요.";
                default: return "출장하면 25% 확률로 한 단계 떨어지고, 쉬면 한 단계 오릅니다.";
            }
        }

        public static IReadOnlyList<(string label, string value)> StatTiles(PlayerData p)
        {
            var inv = CultureInfo.InvariantCulture;
            string Rate(double v) => v <= 0 ? "—" : v.ToString(v < 1 ? ".000" : "0.000", inv);
            string Fixed(double v) => v == 0 ? "—" : v.ToString("0.00", inv);
            if (p.IsPitcher)
                return new[]
                {
                    ("ERA", Fixed(p.Era)), ("FIP", Fixed(p.Fip)), ("WHIP", Fixed(p.Whip)),
                    ("승-패", $"{p.Wins}-{p.Losses}"), ("세이브·홀드", $"{p.Saves}·{p.Holds}"), ("WAR", Fixed(p.War)),
                };
            return new[]
            {
                ("타율", Rate(p.BattingAverage)), ("출루율", Rate(p.OnBasePercentage)), ("OPS", Rate(p.Ops)),
                ("홈런", p.HomeRuns.ToString(inv)), ("도루", p.StolenBases.ToString(inv)), ("WAR", Fixed(p.War)),
            };
        }

        /// <summary>교체 후보: 선발 타자면 벤치 선수 (같은 수비군 먼저, 컨디션 좋은 순), 벤치면 선발 타자 (컨디션 나쁜 순).</summary>
        public IReadOnlyList<PlayerData> SwapCandidates()
        {
            var roster = _manager.MyRoster;
            if (roster == null || PlayerId == null) return Array.Empty<PlayerData>();
            var season = _manager.Season;
            var lineupPlayer = roster.Lineup.FirstOrDefault(x => x.Id == PlayerId);
            if (lineupPlayer != null)
                return roster.Bench
                    .OrderByDescending(b => PositionMatcher.FitOf(lineupPlayer.Position, b.Position) != PositionFit.None)
                    .ThenByDescending(b => season.ConditionOf(b.Id)).ThenBy(b => b.Order).ToList();
            if (roster.Bench.Any(x => x.Id == PlayerId))
                return roster.Lineup.OrderBy(l => season.ConditionOf(l.Id)).ThenBy(l => l.Order).ToList();
            return Array.Empty<PlayerData>();
        }

        void RenderSwap(TeamRoster roster, PlayerData p)
        {
            var container = Q("pd-swap");
            container.Clear();
            var spot = SpotOf(roster, p.Id);
            var title = Q<Label>("pd-swap-title");
            _pickButton.style.display = spot.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!spot.HasValue)
            {
                title.text = roster.Reserves.Contains(p)
                    ? "퓨처스 선수 · 1군에 빈자리가 나면 같은 수비 위치부터 올라옵니다."
                    : "투수는 선발 로테이션 순서대로 등판합니다 (컨디션 보통 미만은 건너뜀).";
                return;
            }
            bool starter = !spot.Value.IsBench;
            title.text = starter ? "벤치 선수와 교체 (같은 수비군 · 컨디션 좋은 순)" : "선발 타자와 교체 (컨디션 나쁜 순)";
            foreach (var c in SwapCandidates())
            {
                string id = c.Id;
                int cond = _manager.Season.ConditionOf(id);
                var button = new Button(() => SwapWith(id))
                {
                    name = "pd-swap-" + id,
                    text = starter ? $"{c.Name} · {c.Position} · {PlayerCondition.Label(cond)}"
                        : $"{c.Order}번 {c.Name} · {c.Position} · {PlayerCondition.Label(cond)}",
                };
                button.AddToClassList("pd-swap-btn");
                button.AddToClassList("pd-swap-btn--cond" + PlayerCondition.Clamp(cond));
                container.Add(button);
            }
        }

        /// <summary>보고 있는 선수와 <paramref name="otherId"/>를 맞바꾸고(시즌에 저장) 모달을 닫는다. 못 바꾸면 false.</summary>
        public bool SwapWith(string otherId)
        {
            var roster = _manager.MyRoster;
            if (roster == null || PlayerId == null) return false;
            var a = SpotOf(roster, PlayerId);
            var b = SpotOf(roster, otherId);
            if (!a.HasValue || !b.HasValue) return false;
            Close();
            return _manager.SwapLineup(a.Value, b.Value);
        }

        void OnPick()
        {
            var roster = _manager.MyRoster;
            var spot = roster == null || PlayerId == null ? null : SpotOf(roster, PlayerId);
            Close();
            if (spot.HasValue) _pick(spot.Value);
        }

        void RenderRelease(PlayerData p, bool reserve)
        {
            if (reserve)
            {
                _releaseButton.SetEnabled(false);
                _releaseButton.EnableInClassList("pd-release--armed", false);
                _releaseButton.text = "방출";
                _releaseButton.tooltip = Q<Label>("pd-release-note").text = "퓨처스 선수는 방출할 수 없습니다 (1군 선수만).";
                return;
            }
            var block = StoveLeague.CheckRelease(_manager.Season, _manager.Rosters, p.Id);
            long penalty = StoveLeague.ReleasePenalty(_manager.Season, _manager.Rosters, p.Id);
            string penaltyText = FranchiseFinance.BudgetDeltaText(-penalty);
            _releaseButton.SetEnabled(block == ReleaseBlock.None);
            _releaseButton.EnableInClassList("pd-release--armed", ReleaseArmed);
            _releaseButton.text = ReleaseArmed ? $"방출 확정 · 위약금 {penaltyText}" : "방출";
            _releaseButton.tooltip = block == ReleaseBlock.None ? $"방출 · 위약금 {penaltyText} (연봉 50%)" : StoveLeague.ReleaseBlockText(block);
            Q<Label>("pd-release-note").text = block != ReleaseBlock.None ? StoveLeague.ReleaseBlockText(block)
                : ReleaseArmed ? "한 번 더 누르면 방출됩니다. 되돌릴 수 없습니다."
                : $"위약금 {penaltyText} (연봉 50%) · 선수는 FA 시장으로 갑니다.";
        }

        void OnRelease() => PressRelease();

        /// <summary>
        /// [방출]: 첫 누름은 확인 상태로만 바꾸고 null, 확인 상태에서 다시 누르면 방출하고(로비가 결과를 안내) 모달을 닫는다.
        /// 막힌 선수는 아무것도 하지 않고 null.
        /// </summary>
        public ReleaseOutcome PressRelease()
        {
            if (PlayerId == null) return null;
            if (StoveLeague.CheckRelease(_manager.Season, _manager.Rosters, PlayerId) != ReleaseBlock.None) return null;
            if (!ReleaseArmed)
            {
                ReleaseArmed = true;
                Render();
                return null;
            }
            string id = PlayerId;
            Close();
            return _release(id);
        }

        T Q<T>(string name) where T : VisualElement => _root.Q<T>(name);
        VisualElement Q(string name) => _root.Q(name);

        static Label Cell(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return l;
        }
    }
}
