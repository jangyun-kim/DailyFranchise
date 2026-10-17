using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace DailyFranchise.App
{
    /// <summary>
    /// 구단 운영(Front Office) 모달 (Phase 42). 로비 헤더의 예산·팬 만족도·시즌·신뢰도 영역을 누르면 연다. 순수 C# 뷰 — 시즌을 읽기만 한다.
    /// - 수뇌부 신뢰도: 보유 TP 게이지, 이번 시즌 적립(상한 150), 얻는 법(3연승·한국시리즈 진출·우승·협상 결렬 환급),
    ///   쓰는 법(수뇌부 설득 TP 10 · 확정 영입 TP 100), 샐러리캡·페이롤·여유.
    /// - 팬 만족도·팬덤 레벨: 게이지, 변동 규칙(승·패·라이벌 승리), 다음 레벨까지와 다음 보상.
    /// - 해금한 팬덤 특전: 슬롯 3개(해금/잠김과 효과) + 레벨 보상 (최근 <see cref="LevelRowsShown"/>개, 나머지는 한 줄 요약).
    /// </summary>
    public sealed class FrontOfficeView
    {
        public const int LevelRowsShown = 4;

        readonly VisualElement _root;
        readonly SeasonManager _manager;
        readonly VisualElement _modal;
        readonly Button _close;

        public FrontOfficeView(VisualElement root, SeasonManager manager)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _modal = root.Q("fo-modal");
            _close = root.Q<Button>("fo-close");
            _close.clicked += Close;
            _modal.RegisterCallback<ClickEvent>(OnBackdrop);
        }

        public void Detach()
        {
            _close.clicked -= Close;
            _modal.UnregisterCallback<ClickEvent>(OnBackdrop);
        }

        public bool IsOpen => _modal.ClassListContains("fo-modal--open");

        void OnBackdrop(ClickEvent e)
        {
            if (e.target == _modal) Close();
        }

        public void Open()
        {
            _modal.AddToClassList("fo-modal--open");
            Render();
        }

        public void Close() => _modal.RemoveFromClassList("fo-modal--open");

        /// <summary>열려 있으면 다시 그린다 (경기·영입 뒤).</summary>
        public void Refresh()
        {
            if (IsOpen) Render();
        }

        void Render()
        {
            var s = _manager.Season;
            // 신뢰도
            Q<Label>("fom-trust").text = $"{s.TrustPoints} TP";
            Q("fom-trust-fill").style.width = Length.Percent(100f * s.TrustPoints / FrontOfficeTrust.MaxTrust);
            Q<Label>("fom-trust-season").text =
                $"보유 상한 {FrontOfficeTrust.MaxTrust} · 이번 시즌 적립 {s.TrustEarnedThisSeason} / {FrontOfficeTrust.MaxSeasonEarn} · 시즌 넘길 때 {FrontOfficeTrust.CarryOver}까지 이월";
            Q<Label>("fom-trust-earn").text =
                $"얻기 · {FrontOfficeTrust.WinStreakStep}연승마다 +{FrontOfficeTrust.WinStreakTrust} · 한국시리즈 진출 +{FrontOfficeTrust.PostseasonTrust} · " +
                $"우승 +{FrontOfficeTrust.ChampionTrust} · 성의 있는 협상 결렬 환급";
            Q<Label>("fom-trust-use").text =
                $"쓰기 · 수뇌부 설득 TP {FrontOfficeTrust.PersuadeCost} (샐러리캡에 막힌 영입 1건 캡 초과 허용) · 확정 영입 TP {FrontOfficeTrust.Ceiling} (100% 영입)";
            long room = StoveLeague.CapRoom(s, _manager.Rosters);
            Q<Label>("fom-cap").text = $"샐러리캡 {FranchiseFinance.BudgetText(s.SalaryCap)} · 페이롤 {FranchiseFinance.BudgetText(s.Payroll)} · 여유 " +
                                       (s.Payroll > s.SalaryCap ? $"없음 (캡 초과 {FranchiseFinance.BudgetText(s.Payroll - s.SalaryCap)})" : FranchiseFinance.BudgetText(room));

            // 팬덤
            int fan = s.FanApproval;
            Q<Label>("fom-fan").text = $"{fan}% · 팬덤 Lv.{s.FandomLevel}";
            var fill = Q("fom-fan-fill");
            fill.style.width = Length.Percent(fan);
            var mood = FranchiseFinance.MoodOf(fan);
            fill.EnableInClassList("fo-fill--happy", mood == FanMood.Happy);
            fill.EnableInClassList("fo-fill--angry", mood == FanMood.Angry);
            Q<Label>("fom-fan-rule").text =
                $"승 {FranchiseFinance.FanDeltaText(FranchiseFinance.WinFan)} · 패 {FranchiseFinance.FanDeltaText(FranchiseFinance.LossFan)} · " +
                $"{FandomRules.RivalMaxRank}위 이내 라이벌 승리 추가 {FranchiseFinance.FanDeltaText(FandomRules.RivalWinFan)} · 한국시리즈 우승 100%";
            var next = FandomRules.RewardFor(s.FandomLevel + 1);
            Q<Label>("fom-next").text =
                $"{FandomRules.LevelUpApproval}% 도달 시 Lv.{next.Level} · {next.Text} (게이지는 {FandomRules.LevelResetApproval}%로) · 앞으로 {Math.Max(0, FandomRules.LevelUpApproval - fan)}%p";

            // 특전 목록
            var list = Q("fom-perks");
            list.Clear();
            foreach (var slot in FandomRules.Slots)
            {
                bool on = (s.UnlockedSlots & slot) != 0;
                list.Add(Row("fom-slot-" + slot.ToString().ToLowerInvariant(), $"{FandomRules.Label(slot)} {(on ? "해금" : "잠김")}",
                    FandomRules.Hint(slot), on));
            }
            var levels = FandomRules.RewardsUpTo(s.FandomLevel);
            int hidden = Math.Max(0, levels.Count - LevelRowsShown);
            if (hidden > 0)
                list.Add(Row("fom-level-older", $"Lv.1~{hidden} 보상", $"특별 예산 등 {hidden}건 지급 완료", true));
            foreach (var r in levels.Skip(hidden))
                list.Add(Row("fom-level-" + r.Level, $"Lv.{r.Level} 달성", r.Text, true));
            if (levels.Count == 0)
                list.Add(Row("fom-level-none", "팬덤 레벨 보상 없음", $"팬 만족도 {FandomRules.LevelUpApproval}%에 닿으면 Lv.1 · {FandomRules.RewardFor(1).Text}", false));
        }

        static VisualElement Row(string name, string title, string detail, bool unlocked)
        {
            var row = new VisualElement { name = name };
            row.AddToClassList("fom-perk");
            row.EnableInClassList("fom-perk--on", unlocked);
            var t = new Label(title);
            t.AddToClassList("fom-perk__title");
            var d = new Label(detail);
            d.AddToClassList("fom-perk__detail");
            row.Add(t);
            row.Add(d);
            return row;
        }

        T Q<T>(string name) where T : VisualElement => _root.Q<T>(name);
        VisualElement Q(string name) => _root.Q(name);
    }
}
