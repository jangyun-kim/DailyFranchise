using System;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;

namespace DailyFranchise.App
{
    /// <summary>
    /// 로비 위에 뜨는 스토브리그 FA 시장 모달 (Phase 25). 순수 C# 뷰 — 로비(<see cref="LobbyController"/>)가 만들고 연다.
    /// 왼쪽은 시장 목록(<see cref="SeasonManager.Market"/>), 오른쪽은 고른 선수의 협상 테이블:
    /// 제시액(요구액의 50~150%, ±1·5%)과 그에 따른 성공 확률(<see cref="FrontOfficeTrust.SuccessRate"/>)·수수료·환급 TP·
    /// 성공 시 경기당 유지비 증가(연봉 ÷ 144, Phase 26)·캡 여유를 보이고, 막히는 이유(<see cref="StoveLeague.Check"/>)가 있으면 경고하고 버튼을 잠근다.
    /// [확정 영입]은 TP 100 이상일 때만 열린다. 협상 결과는 SeasonManager가 저장·알림하고 여기서 다시 그린다.
    /// 시장 목록은 열 때와 협상 뒤에만 다시 만들고, 선택·제시액 변경은 협상 테이블만 다시 그린다.
    /// </summary>
    public sealed class FaMarketView
    {
        public const int MinPercent = 50;
        public const int MaxPercent = 150;

        readonly VisualElement _root;
        readonly SeasonManager _manager;
        readonly VisualElement _modal;
        readonly Button _negotiate;
        readonly Button _ceiling;
        readonly Button _close;
        readonly (Button button, Action handler)[] _steps;

        string _resultText = "";
        bool _resultSuccess;

        public FaMarketView(VisualElement root, SeasonManager manager)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _modal = root.Q("fa-modal");
            _negotiate = root.Q<Button>("fa-negotiate");
            _ceiling = root.Q<Button>("fa-ceiling");
            _close = root.Q<Button>("fa-close");
            _steps = new[]
            {
                (root.Q<Button>("fa-offer-minus5"), StepHandler(-5)), (root.Q<Button>("fa-offer-minus1"), StepHandler(-1)),
                (root.Q<Button>("fa-offer-plus1"), StepHandler(1)), (root.Q<Button>("fa-offer-plus5"), StepHandler(5)),
            };
            _negotiate.clicked += OnNegotiate;
            _ceiling.clicked += OnCeiling;
            _close.clicked += Close;
            foreach (var (button, handler) in _steps) button.clicked += handler;
        }

        Action StepHandler(int delta) => () => Step(delta);

        /// <summary>버튼 연결을 끊는다 (로비가 꺼질 때).</summary>
        public void Detach()
        {
            _negotiate.clicked -= OnNegotiate;
            _ceiling.clicked -= OnCeiling;
            _close.clicked -= Close;
            foreach (var (button, handler) in _steps) button.clicked -= handler;
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
            if (Selected == null)
            {
                SelectedId = _manager.Market.FirstOrDefault()?.Id;
                OfferPercent = 100;
            }
            RenderSummary();
            RenderList();
            RenderTable();
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
