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
        readonly VisualElement _root;

        public PlayerDetailView(VisualElement root, SeasonManager manager, Func<string, ReleaseOutcome> release, Action<LineupSpot> pick)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _release = release ?? throw new ArgumentNullException(nameof(release));
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
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
            _modal.UnregisterCallback<ClickEvent>(OnBackdrop);
        }

        public bool IsOpen => _modal.ClassListContains("pd-modal--open");

        /// <summary>보고 있는 선수 ID (닫혀 있으면 null).</summary>
        public string PlayerId { get; private set; }

        /// <summary>[방출]을 한 번 눌러 확인 상태인지.</summary>
        public bool ReleaseArmed { get; private set; }

        void OnBackdrop(ClickEvent e)
        {
            if (e.target == _modal) Close();
        }

        public void Close()
        {
            _modal.RemoveFromClassList("pd-modal--open");
            PlayerId = null;
            ReleaseArmed = false;
        }

        /// <summary>1군과 퓨처스 선수 (Phase 33부터 퓨처스도 연다 — 특별 훈련).</summary>
        static IEnumerable<PlayerData> Everyone(TeamRoster roster) => LeagueRosterBook.AllOf(roster).Concat(roster.Reserves);

        /// <summary>내 구단 1군·퓨처스 선수의 상세를 연다. 선수단에 없거나 경기 중이면 열지 않고 false.</summary>
        public bool Open(string playerId)
        {
            var roster = _manager.MyRoster;
            if (roster == null || playerId == null || _manager.Season.InProgress != null) return false;
            if (Everyone(roster).All(p => p.Id != playerId)) return false;
            PlayerId = playerId;
            ReleaseArmed = false;
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
            var roster = _manager.MyRoster;
            var p = Everyone(roster).FirstOrDefault(x => x.Id == PlayerId);
            if (p == null)
            {
                Close(); // 방출·은퇴 등으로 선수단에서 빠졌다
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
            Q<Label>("pd-role").text = RoleText(roster, p);

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

            RenderSwap(roster, p);
            RenderTraining(p);
            RenderRelease(p, roster.Reserves.Contains(p));
        }

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
