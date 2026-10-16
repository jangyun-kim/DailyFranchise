using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim.Data;
using UnityEngine.UIElements;

namespace DailyFranchise.App
{
    /// <summary>FA 시장 모달의 탭 (Phase 34).</summary>
    public enum FaTab
    {
        Market,
        Trade,
    }

    /// <summary>
    /// 로비 위에 뜨는 스토브리그 FA 시장 모달 (Phase 25). 순수 C# 뷰 — 로비(<see cref="LobbyController"/>)가 만들고 연다.
    /// 왼쪽은 시장 목록(<see cref="SeasonManager.Market"/>), 오른쪽은 고른 선수의 협상 테이블:
    /// 제시액(요구액의 50~150%, ±1·5%)과 그에 따른 성공 확률(<see cref="FrontOfficeTrust.SuccessRate"/>)·수수료·환급 TP·
    /// 성공 시 경기당 유지비 증가(연봉 ÷ 144, Phase 26)·캡 여유를 보이고, 막히는 이유(<see cref="StoveLeague.Check"/>)가 있으면 경고하고 버튼을 잠근다.
    /// [확정 영입]은 TP 100 이상일 때만 열린다. 협상 결과는 SeasonManager가 저장·알림하고 여기서 다시 그린다.
    /// 시장 목록은 열 때와 협상 뒤에만 다시 만들고, 선택·제시액 변경은 협상 테이블만 다시 그린다.
    /// Phase 34: 머리의 [FA 명단] / [타 구단 명단] 탭. 타 구단 명단은 리그 선수단 장부에서 내 구단·자유계약이 아닌 선수를
    /// 소속 구단별(<see cref="SeasonManager.TradeTargets"/>)로 보이고, 행을 누르면 선수 상세(트레이드 제안)를 연다.
    /// 오른쪽은 협상 테이블 대신 트레이드 데스크(평가 규칙·페이롤·마지막 결과).
    /// 1080p 한 화면 규칙(스크롤 없음)이라 명단은 쪽으로 나눈다: 한 쪽에 한 구단, 많으면 그 구단을 <see cref="TradePageSize"/>명씩.
    /// </summary>
    public sealed class FaMarketView
    {
        public const int MinPercent = 50;
        public const int MaxPercent = 150;

        /// <summary>타 구단 명단 한 쪽의 선수 줄 수 (구단 머리 줄 별도).</summary>
        public const int TradePageSize = 13;

        readonly VisualElement _root;
        readonly SeasonManager _manager;
        readonly VisualElement _modal;
        readonly Button _negotiate;
        readonly Button _ceiling;
        readonly Button _close;
        readonly (Button button, Action handler)[] _steps;
        readonly Button _tabMarket;
        readonly Button _tabTrade;
        readonly Button _tradePrev;
        readonly Button _tradeNext;
        readonly Func<string, bool> _openPlayer;

        string _resultText = "";
        bool _resultSuccess;
        string _tradeText = "";
        bool _tradeSuccess;

        /// <param name="openPlayer">타 구단 명단 행을 눌렀을 때 선수 상세를 연다 (로비). 없으면 강조만 한다.</param>
        public FaMarketView(VisualElement root, SeasonManager manager, Func<string, bool> openPlayer = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _modal = root.Q("fa-modal");
            _negotiate = root.Q<Button>("fa-negotiate");
            _ceiling = root.Q<Button>("fa-ceiling");
            _close = root.Q<Button>("fa-close");
            _tabMarket = root.Q<Button>("fa-tab-market");
            _tabTrade = root.Q<Button>("fa-tab-trade");
            _tradePrev = root.Q<Button>("fa-trade-prev");
            _tradeNext = root.Q<Button>("fa-trade-next");
            _openPlayer = openPlayer;
            _steps = new[]
            {
                (root.Q<Button>("fa-offer-minus5"), StepHandler(-5)), (root.Q<Button>("fa-offer-minus1"), StepHandler(-1)),
                (root.Q<Button>("fa-offer-plus1"), StepHandler(1)), (root.Q<Button>("fa-offer-plus5"), StepHandler(5)),
            };
            _negotiate.clicked += OnNegotiate;
            _ceiling.clicked += OnCeiling;
            _close.clicked += Close;
            foreach (var (button, handler) in _steps) button.clicked += handler;
            _tabMarket.clicked += ShowMarketTab;
            _tabTrade.clicked += ShowTradeTab;
            _tradePrev.clicked += PrevTradePage;
            _tradeNext.clicked += NextTradePage;
        }

        Action StepHandler(int delta) => () => Step(delta);

        /// <summary>버튼 연결을 끊는다 (로비가 꺼질 때).</summary>
        public void Detach()
        {
            _negotiate.clicked -= OnNegotiate;
            _ceiling.clicked -= OnCeiling;
            _close.clicked -= Close;
            foreach (var (button, handler) in _steps) button.clicked -= handler;
            _tabMarket.clicked -= ShowMarketTab;
            _tabTrade.clicked -= ShowTradeTab;
            _tradePrev.clicked -= PrevTradePage;
            _tradeNext.clicked -= NextTradePage;
        }

        /// <summary>지금 탭 (처음은 FA 명단). 닫았다 열어도 유지한다.</summary>
        public FaTab Tab { get; private set; } = FaTab.Market;

        /// <summary>타 구단 명단에서 마지막으로 누른 선수 ID. 없으면 null.</summary>
        public string SelectedTradeId { get; private set; }

        /// <summary>타 구단 명단의 지금 쪽 (0부터). 닫았다 열어도 유지하고, 쪽 수가 줄면 마지막 쪽으로.</summary>
        public int TradePage { get; private set; }

        /// <summary>타 구단 명단의 쪽 수 (선수가 없어도 1).</summary>
        public int TradePageCount => Math.Max(1, TradePages().Count);

        void PrevTradePage() => StepTradePage(-1);
        void NextTradePage() => StepTradePage(1);

        /// <summary>타 구단 명단 쪽을 넘긴다 (처음·끝에서 멈춘다).</summary>
        public void StepTradePage(int delta)
        {
            TradePage = Math.Max(0, Math.Min(TradePageCount - 1, TradePage + delta));
            if (IsOpen && Tab == FaTab.Trade) RenderTradeList();
        }

        /// <summary>그 선수가 있는 쪽 (없으면 -1).</summary>
        public int TradePageOf(string playerId) => TradePages().FindIndex(pg => pg.Rows.Any(t => t.Id == playerId));

        /// <summary>한 쪽 = 한 구단의 선수 최대 <see cref="TradePageSize"/>명 (구단 순서·명단 순서 유지).</summary>
        List<(string Team, int Total, int Part, int Parts, List<TradeTarget> Rows)> TradePages()
        {
            var pages = new List<(string, int, int, int, List<TradeTarget>)>();
            foreach (var group in _manager.TradeTargets.GroupBy(t => t.Team))
            {
                var all = group.ToList();
                int parts = (all.Count + TradePageSize - 1) / TradePageSize;
                for (int i = 0; i < parts; i++)
                    pages.Add((group.Key, all.Count, i + 1, parts, all.Skip(i * TradePageSize).Take(TradePageSize).ToList()));
            }
            return pages;
        }

        void ShowMarketTab() => ShowTab(FaTab.Market);
        void ShowTradeTab() => ShowTab(FaTab.Trade);

        public void ShowTab(FaTab tab)
        {
            Tab = tab;
            Refresh();
        }

        /// <summary>타 구단 명단의 선수를 누른다: 강조하고 선수 상세(트레이드 제안)를 연다. 열었으면 true.</summary>
        public bool OpenTradePlayer(string playerId)
        {
            SelectedTradeId = playerId;
            int page = TradePageOf(playerId);
            if (page >= 0 && page != TradePage)
            {
                TradePage = page;
                if (IsOpen && Tab == FaTab.Trade) RenderTradeList();
            }
            if (IsOpen) RenderTradeSelection();
            return _openPlayer != null && _openPlayer(playerId);
        }

        /// <summary>트레이드 결과를 트레이드 데스크에 남긴다 (로비가 제안 뒤 부른다).</summary>
        public void ReportTrade(TradeOutcome outcome)
        {
            if (outcome == null) return;
            var q = outcome.Quote;
            if (outcome.Accepted)
            {
                _tradeText = $"트레이드 성사 · {q.Mine.Name} → {q.Partner} · {q.Theirs.Name} → {TradeDesk.ArrivalText(q.Arrival)} 합류 " +
                             $"(페이롤 {FranchiseFinance.BudgetDeltaText(q.PayrollDelta)})";
                _tradeSuccess = true;
                if (SelectedTradeId == q.Theirs.Id) SelectedTradeId = null;
            }
            else
            {
                _tradeText = outcome.Rejected ? TradeDesk.RejectedText : TradeDesk.BlockText(outcome.Block);
                _tradeSuccess = false;
            }
            if (IsOpen) Refresh();
        }
        public bool IsOpen => _modal.ClassListContains("fa-modal--open");

        /// <summary>고른 선수 ID. 없으면 null.</summary>
        public string SelectedId { get; private set; }

        /// <summary>제시액 (요구액 대비 %).</summary>
        public int OfferPercent { get; private set; } = 100;

        /// <summary>시장에서 고른 선수. 시장에 없으면 null.</summary>
        public FreeAgent Selected => SelectedId == null ? null : _manager.Market.FirstOrDefault(a => a.Id == SelectedId);

        /// <summary>지금 제시액 (원).</summary>
        public long Offer => Selected == null ? 0 : StoveLeague.OfferAt(Selected.Ask, OfferPercent);

        /// <summary>시장 목록을 새로 만든 횟수 (선택·제시액만 바꾸면 늘지 않는다 — 테스트·진단용).</summary>
        public int ListRenderCount { get; private set; }

        public void Open()
        {
            _modal.AddToClassList("fa-modal--open");
            _resultText = "";
            if (Selected == null) Select(_manager.Market.FirstOrDefault()?.Id);
            Refresh();
        }

        public void Close() => _modal.RemoveFromClassList("fa-modal--open");

        public void Select(string playerId)
        {
            SelectedId = playerId;
            OfferPercent = 100;
            if (!IsOpen) return;
            RenderSelection();
            RenderTable();
        }

        public void Step(int delta)
        {
            OfferPercent = Math.Max(MinPercent, Math.Min(MaxPercent, OfferPercent + delta));
            if (IsOpen) RenderTable();
        }

        /// <summary>[협상 시도]. 버튼이 잠겨 있어도 직접 부르면 규칙대로 막힌 결과를 돌려준다.</summary>
        public SigningOutcome Negotiate()
        {
            var agent = Selected;
            if (agent == null) return null;
            return Report(_manager.Negotiate(agent, Offer));
        }

        /// <summary>[확정 영입 (TP 100 소모)].</summary>
        public SigningOutcome SignWithCeiling()
        {
            var agent = Selected;
            if (agent == null) return null;
            return Report(_manager.SignWithCeiling(agent));
        }

        void OnNegotiate() => Negotiate();
        void OnCeiling() => SignWithCeiling();

        SigningOutcome Report(SigningOutcome o)
        {
            var name = o.Agent.Player.Name;
            if (o.Block != SigningBlock.None)
            {
                _resultText = StoveLeague.BlockText(o.Block);
                _resultSuccess = false;
            }
            else if (o.Signed)
            {
                string where = o.Agent.Player.IsPitcher ? "불펜" : "벤치";
                _resultText = $"영입 성공 · {name} → {where} 합류 (연봉 {Won(o.Salary)} · 수수료 {Won(o.Negotiation.Fee)}" +
                              (o.Negotiation.CeilingUsed ? " · TP 100 사용)" : ")");
                _resultSuccess = true;
                SelectedId = null; // 영입한 선수는 시장에서 빠진다 → 다음 선수
            }
            else
            {
                int tp = o.Negotiation.TrustDelta;
                _resultText = $"협상 결렬 · {name} · 수수료 {Won(o.Negotiation.Fee)} · " +
                              (tp > 0 ? $"신뢰도 TP +{tp} 환급" : "헐값 제시라 TP 환급 없음");
                _resultSuccess = false;
            }
            if (IsOpen) Refresh();
            return o;
        }

        /// <summary>요약·시장 목록·협상 테이블을 모두 다시 그린다 (열 때·협상 뒤).</summary>
        public void Refresh()
        {
            if (!IsOpen) return;
            RenderTabs();
            if (Tab == FaTab.Trade)
            {
                RenderSummary();
                RenderTradeList();
                RenderTradePanel();
                return;
            }
            if (Selected == null)
            {
                SelectedId = _manager.Market.FirstOrDefault()?.Id;
                OfferPercent = 100;
            }
            RenderSummary();
            RenderList();
            RenderTable();
        }

        /// <summary>탭 버튼·제목·열 머리·목록/오른쪽 패널 전환.</summary>
        void RenderTabs()
        {
            bool trade = Tab == FaTab.Trade;
            _tabMarket.EnableInClassList("fa-tab--active", !trade);
            _tabTrade.EnableInClassList("fa-tab--active", trade);
            Q<Label>("fa-title").text = trade ? "스토브리그 · 트레이드" : "스토브리그 · FA 시장";
            Q<Label>("fa-head-team").text = trade ? "역할" : "소속";
            Q<Label>("fa-head-ask").text = trade ? "트레이드 가치" : "요구액";
            Q("fa-list").style.display = trade ? DisplayStyle.None : DisplayStyle.Flex;
            Q("fa-trade-list").style.display = trade ? DisplayStyle.Flex : DisplayStyle.None;
            Q("fa-trade-pager").style.display = trade ? DisplayStyle.Flex : DisplayStyle.None;
            Q("fa-table").style.display = trade ? DisplayStyle.None : DisplayStyle.Flex;
            Q("fa-trade-panel").style.display = trade ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>타 구단 명단을 다시 만든 횟수 (테스트·진단용).</summary>
        public int TradeListRenderCount { get; private set; }

        /// <summary>타 구단 명단 지금 쪽: 구단 머리 줄 + 선수 줄 (1군 역할 또는 "퓨처스", 트레이드 가치) + 쪽 넘김.</summary>
        void RenderTradeList()
        {
            TradeListRenderCount++;
            var list = Q("fa-trade-list");
            list.Clear();
            var targets = _manager.TradeTargets;
            var pages = TradePages();
            TradePage = Math.Max(0, Math.Min(Math.Max(1, pages.Count) - 1, TradePage));
            if (pages.Count > 0)
            {
                var page = pages[TradePage];
                var head = new VisualElement { name = "fa-group-" + page.Team };
                head.AddToClassList("lobby-row");
                head.AddToClassList("fa-group");
                head.AddToClassList(LobbyBriefing.TeamThemeClass(page.Team));
                head.Add(Cell(page.Parts > 1 ? $"{page.Team} · {page.Total}명 ({page.Part}/{page.Parts})" : $"{page.Team} · {page.Total}명", "fa-col-name"));
                list.Add(head);
                var roster = _manager.Rosters?.Get(page.Team);
                foreach (var t in page.Rows)
                {
                    var p = t.Player;
                    var row = new VisualElement { name = "fa-trade-" + t.Id, userData = t.Id };
                    row.AddToClassList("lobby-row");
                    row.AddToClassList("fa-row");
                    row.AddToClassList("fa-row--item");
                    row.EnableInClassList("fa-row--futures", t.Futures);
                    row.Add(Cell(p.Name, "fa-col-name"));
                    row.Add(Cell(RoleText(roster, p, t.Futures), "fa-col-team"));
                    row.Add(Cell(p.Position, "fa-col-pos"));
                    row.Add(Cell(p.Age > 0 ? p.Age + "세" : "—", "fa-col-age"));
                    row.Add(Cell(p.War.ToString("0.00", CultureInfo.InvariantCulture), "fa-col-war"));
                    row.Add(Cell(StoveLeague.KeyStat(p), "fa-col-key"));
                    row.Add(Cell("가치 " + TradeEvaluator.ValueText(t.Value), "fa-col-ask"));
                    row.tooltip = $"연봉 {Won(t.Salary)} · 누르면 선수 상세와 [트레이드 제안]";
                    string id = t.Id;
                    row.RegisterCallback<ClickEvent>(_ => OpenTradePlayer(id));
                    list.Add(row);
                }
            }
            Q<Label>("fa-trade-page").text = pages.Count == 0 ? "—" : $"{TradePage + 1} / {pages.Count}쪽";
            _tradePrev.SetEnabled(TradePage > 0);
            _tradeNext.SetEnabled(TradePage < pages.Count - 1);
            Q<Label>("fa-market-note").text = targets.Count == 0
                ? "지금 선수 DB에는 타 구단 선수단이 없습니다. 구단 데이터가 들어오면 이 명단에 소속 팀별로 나옵니다."
                : $"타 구단 {targets.Select(t => t.Team).Distinct().Count()}개 · {targets.Count}명 · 선수를 누르면 상세와 [트레이드 제안]";
            RenderTradeSelection();
        }

        /// <summary>장부 역할: "선발 3번" · "벤치" · "로테이션" · "불펜" · "퓨처스".</summary>
        public static string RoleText(TeamRoster roster, PlayerData p, bool futures)
        {
            if (futures || roster == null) return "퓨처스";
            var batter = roster.Lineup.FirstOrDefault(x => x.Id == p.Id);
            if (batter != null) return $"선발 {batter.Order}번";
            if (roster.Bench.Any(x => x.Id == p.Id)) return "벤치";
            if (roster.Starters.Any(x => x.Id == p.Id)) return "로테이션";
            return "불펜";
        }

        void RenderTradeSelection()
        {
            foreach (var row in Q("fa-trade-list").Children())
                row.EnableInClassList("fa-row--selected", row.userData is string id && id == SelectedTradeId);
        }

        void RenderTradePanel()
        {
            var season = _manager.Season;
            var mine = _manager.Rosters?.Get(season.MyTeam);
            var inv = CultureInfo.InvariantCulture;
            Q<Label>("fa-trade-howto").text =
                "1. 왼쪽에서 데려올 선수를 누르면 선수 상세가 열립니다.\n" +
                "2. [트레이드 제안]을 누르고 보낼 내 선수(1군·벤치·퓨처스) 1명을 고르면 즉시 평가합니다.\n" +
                "3. 받은 선수는 1군 빈자리(타자 벤치·투수 불펜), 없으면 퓨처스로 갑니다. 내준 선발 타자 자리는 같은 수비 위치 선수가 메웁니다.";
            Q<Label>("fa-trade-rule").text =
                $"가치 = (WAR + 폼 보정) × 적정 연봉 ÷ 실제 연봉 · {TradeEvaluator.YoungMaxAge}세 이하 ×{TradeEvaluator.YoungMultiplier.ToString("0.0", inv)} · " +
                $"{TradeEvaluator.VeteranMinAge}세 이상 ×{TradeEvaluator.VeteranMultiplier.ToString("0.0", inv)}\n" +
                $"CPU는 내 선수 가치가 상대 가치 × {TradeEvaluator.CpuPremium.ToString("0.00", inv)} 이상일 때만 수락합니다 (거절해도 패널티 없음).";
            Q<Label>("fa-trade-payroll").text = mine == null ? "—"
                : $"{Won(StoveLeague.Payroll(mine, season.Signings))} / 캡 {Won(season.SalaryCap)} (여유 {Won(StoveLeague.CapRoom(season, _manager.Rosters))})";
            var result = Q<Label>("fa-trade-result");
            result.text = _tradeText;
            result.EnableInClassList("fa-modal__result--success", _tradeText.Length > 0 && _tradeSuccess);
            result.EnableInClassList("fa-modal__result--failure", _tradeText.Length > 0 && !_tradeSuccess);
        }

        void RenderSummary()
        {
            var season = _manager.Season;
            var mine = _manager.Rosters?.Get(season.MyTeam);
            Q<Label>("fa-budget").text = "예산 " + FranchiseFinance.BudgetText(season.Budget);
            Q<Label>("fa-cap").text = "캡 여유 " + Won(StoveLeague.CapRoom(season, _manager.Rosters));
            Q<Label>("fa-cap").tooltip =
                $"페이롤 {Won(StoveLeague.Payroll(mine, season.Signings))} / 시즌 캡 {Won(season.SalaryCap)}";
            Q<Label>("fa-trust").text = "신뢰도 TP " + FrontOfficeTrust.TrustText(season.TrustPoints);
            Q<Label>("fa-roster").text = mine == null ? "1군 —"
                : $"1군 {RosterLimits.CountOf(mine)} / {RosterLimits.MaxRoster} · 벤치 {mine.Bench.Count}/{RosterLimits.MaxBench} · 불펜 {mine.Bullpen.Count}/{RosterLimits.MaxBullpen}";
        }

        void RenderList()
        {
            ListRenderCount++;
            var list = Q("fa-list");
            list.Clear();
            var market = _manager.Market;
            foreach (var a in market)
            {
                var p = a.Player;
                var row = new VisualElement { name = "fa-" + a.Id, userData = a.Id };
                row.AddToClassList("lobby-row");
                row.AddToClassList("fa-row");
                row.AddToClassList("fa-row--item");
                row.Add(Cell(p.Name, "fa-col-name"));
                row.Add(Cell(a.Team, "fa-col-team"));
                row.Add(Cell(p.Position, "fa-col-pos"));
                row.Add(Cell(p.Age > 0 ? p.Age + "세" : "—", "fa-col-age"));
                row.Add(Cell(p.War.ToString("0.00", CultureInfo.InvariantCulture), "fa-col-war"));
                row.Add(Cell(StoveLeague.KeyStat(p), "fa-col-key"));
                row.Add(Cell(Won(a.Ask), "fa-col-ask"));
                string id = a.Id;
                row.RegisterCallback<ClickEvent>(_ => Select(id));
                list.Add(row);
            }
            Q<Label>("fa-market-note").text = market.Count == 0
                ? "시장에 남은 선수가 없습니다."
                : $"요구액 = WAR 연봉 × (1 + 인기 보정 {StoveLeague.PremiumPercent(FranchiseFinance.MoodOf(_manager.Season.FanApproval))}%) · 팬 만족도가 높을수록 싸다";
            RenderSelection();
        }

        void RenderSelection()
        {
            foreach (var row in Q("fa-list").Children())
                row.EnableInClassList("fa-row--selected", (row.userData as string) == SelectedId);
        }

        void RenderTable()
        {
            var season = _manager.Season;
            var agent = Selected;
            var result = Q<Label>("fa-result");
            result.text = _resultText;
            result.EnableInClassList("fa-modal__result--success", _resultText.Length > 0 && _resultSuccess);
            result.EnableInClassList("fa-modal__result--failure", _resultText.Length > 0 && !_resultSuccess);
            if (agent == null)
            {
                Q<Label>("fa-player-name").text = "—";
                foreach (var n in new[] { "fa-player-meta", "fa-ask", "fa-fee", "fa-after", "fa-warning" }) Q<Label>(n).text = "";
                Q<Label>("fa-offer").text = "—";
                Q<Label>("fa-odds").text = "—";
                _negotiate.SetEnabled(false);
                _ceiling.SetEnabled(false);
                return;
            }

            var p = agent.Player;
            long offer = Offer, fee = FrontOfficeTrust.FeeOf(agent.Ask);
            Q<Label>("fa-player-name").text = p.Name;
            Q<Label>("fa-player-meta").text =
                $"{agent.Team} · {p.Position} · {(p.Age > 0 ? p.Age + "세 · " : "")}WAR {p.War.ToString("0.00", CultureInfo.InvariantCulture)} · {StoveLeague.KeyStat(p)}";
            Q<Label>("fa-ask").text =
                $"요구액 {Won(agent.Ask)} · 연봉 {Won(agent.Salary)} + 인기 보정 {StoveLeague.PremiumPercent(FranchiseFinance.MoodOf(season.FanApproval))}%";
            Q<Label>("fa-offer").text = $"{Won(offer)} · 요구액의 {OfferPercent}%";
            double rate = FrontOfficeTrust.SuccessRate(offer, agent.Ask, season.TrustPoints);
            Q<Label>("fa-odds").text = $"영입 성공 확률 {Math.Round(rate * 100, MidpointRounding.AwayFromZero)}%";
            int refund = FrontOfficeTrust.RefundOnFailure(offer, agent.Ask);
            Q<Label>("fa-fee").text = $"수수료 {Won(fee)} (시도마다) · " +
                                     (FrontOfficeTrust.IsGoodFaith(offer, agent.Ask) ? $"결렬 시 TP +{refund}" : "헐값 제시(90% 미만) → 결렬 시 환급 없음");
            long capRoom = StoveLeague.CapRoom(season, _manager.Rosters);
            Q<Label>("fa-after").text =
                $"성공 시 경기당 유지비 +{Won(FranchiseFinance.PayrollPerGame(offer))} · 캡 여유 {Won(capRoom - offer)}";

            var block = StoveLeague.Check(season, _manager.Rosters, agent, offer);
            Q<Label>("fa-warning").text = StoveLeague.BlockText(block);
            _negotiate.SetEnabled(block == SigningBlock.None);
            var ceilingBlock = StoveLeague.Check(season, _manager.Rosters, agent, agent.Ask, ceiling: true);
            _ceiling.SetEnabled(ceilingBlock == SigningBlock.None);
            _ceiling.tooltip = ceilingBlock == SigningBlock.None
                ? $"TP 100을 쓰고 요구액 {Won(agent.Ask)}에 100% 영입"
                : StoveLeague.BlockText(ceilingBlock);
        }

        /// <summary>"₩3억 7,620만" / 음수 "−₩2,000만" (<see cref="FranchiseFinance.Won"/>).</summary>
        static string Won(long won) => (won < 0 ? "−₩" : "₩") + FranchiseFinance.Won(won);

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
