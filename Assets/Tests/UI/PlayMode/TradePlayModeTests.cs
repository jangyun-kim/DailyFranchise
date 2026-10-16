using System.Collections;
using System.IO;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 34: FA 모달 [타 구단 명단] 탭 → 타 구단 선수 상세 → [트레이드 제안] → 보낼 내 선수 고르기 → 거절(모달 유지)·성사
    /// (라인업 즉시 반영·안내·[오피셜] 뉴스·트레이드 데스크·저장). 실제 데이터는 삼성 1개 구단이라 2개 구단 가상 선수단
    /// (Assets/Tests/Fixtures/TestLeague_TwoTeams.csv)을 선수 DB로 넣는다.
    /// </summary>
    public class TradePlayModeTests
    {
        static readonly string TestSavePath = Path.Combine(Application.temporaryCachePath, "playmode_trade_" + SaveManager.FileName);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyManager();
            SeasonManager.SavePathOverride = TestSavePath;
            SeasonManager.LoadDatabaseSynchronously = true;
            SeasonManager.DatabaseOverride = MasterDatabase.FromCsv(
                Resources.Load<TextAsset>(MasterDatabase.TeamsResource).text,
                File.ReadAllText(Path.Combine(Application.dataPath, "Tests", "Fixtures", "TestLeague_TwoTeams.csv")));
            DeleteSave();
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForLobby();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyManager();
            DeleteSave();
            SeasonManager.SavePathOverride = null;
            SeasonManager.LoadDatabaseSynchronously = false;
            SeasonManager.DatabaseOverride = null;
            yield return null;
        }

        static void DeleteSave()
        {
            if (File.Exists(TestSavePath)) File.Delete(TestSavePath);
        }

        static void DestroyManager()
        {
            if (SeasonManager.Instance != null) Object.DestroyImmediate(SeasonManager.Instance.gameObject);
        }

        static IEnumerator WaitForLobby()
        {
            float timeout = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().name != SeasonManager.LobbyScene && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SeasonManager.LobbyScene));
            yield return null;
            yield return null;
        }

        static LobbyController Lobby => Object.FindAnyObjectByType<LobbyController>();
        static VisualElement Root => Lobby.GetComponent<UIDocument>().rootVisualElement;
        static string Text(string name) => Root.Q<Label>(name).text;

        static void Click(VisualElement element)
        {
            using (var click = ClickEvent.GetPooled())
            {
                click.target = element;
                element.SendEvent(click);
            }
        }

        static void Submit(Button button)
        {
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        static (RenderTexture, PanelSettings) RenderAt1080()
        {
            var doc = Lobby.GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panel = Object.Instantiate(doc.panelSettings);
            panel.targetTexture = rt;
            doc.panelSettings = panel;
            return (rt, panel);
        }

        static void Dispose((RenderTexture rt, PanelSettings panel) r)
        {
            r.rt.Release();
            Object.Destroy(r.rt);
            Object.Destroy(r.panel);
        }

        static void AssertLayout(string label)
        {
            var issues = LayoutChecker.Check(Root.Q("screen"));
            Assert.That(issues, Is.Empty, $"[{label}]\n" + string.Join("\n", issues));
        }

        static int TradeRows() => Root.Q("fa-trade-list").Query(className: "fa-row--item").ToList().Count;

        static string LineupName(int order) => Root.Q("lineup-" + order).Q<Label>(className: "col-name").text;

        [UnityTest, Timeout(120000)]
        public IEnumerator 타_구단_명단에서_트레이드를_제안해_거절과_성사를_보고_라인업과_뉴스에_바로_반영된다()
        {
            var manager = SeasonManager.Instance;
            var r = RenderAt1080();
            yield return null;
            Assert.That(LineupName(4), Is.EqualTo("표준혁"));

            // 1) FA 모달 → [타 구단 명단]
            Lobby.OpenFaMarket();
            yield return null;
            var fa = Lobby.FaMarket;
            Assert.That(fa.Tab, Is.EqualTo(FaTab.Market));
            Submit(Root.Q<Button>("fa-tab-trade"));
            yield return null;
            yield return null;
            Assert.That(fa.Tab, Is.EqualTo(FaTab.Trade));
            Assert.That(Text("fa-title"), Is.EqualTo("스토브리그 · 트레이드"));
            Assert.That(Root.Q("fa-trade-list").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Root.Q("fa-list").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(Root.Q("fa-table").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(Root.Q("fa-trade-panel").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Root.Q("fa-group-LG 트윈스"), Is.Not.Null, "소속 팀별 머리 줄");
            // 한 화면 규칙(스크롤 없음): LG 15명 → 13명 + 2명, 두 쪽
            Assert.That(fa.TradePageCount, Is.EqualTo(2));
            Assert.That(TradeRows(), Is.EqualTo(FaMarketView.TradePageSize));
            Assert.That(Text("fa-trade-page"), Is.EqualTo("1 / 2쪽"));
            Assert.That(Root.Q("fa-group-LG 트윈스").Q<Label>().text, Is.EqualTo("LG 트윈스 · 15명 (1/2)"));
            AssertLayout("타 구단 명단 1쪽");
            Submit(Root.Q<Button>("fa-trade-next"));
            yield return null;
            Assert.That(fa.TradePage, Is.EqualTo(1));
            Assert.That(TradeRows(), Is.EqualTo(2));
            Assert.That(Root.Q<Button>("fa-trade-next").enabledSelf, Is.False, "마지막 쪽");
            AssertLayout("타 구단 명단 2쪽");
            Submit(Root.Q<Button>("fa-trade-prev"));
            yield return null;
            Assert.That(fa.TradePage, Is.EqualTo(0));
            for (int guard = 0; guard < 5 && fa.TradePage < fa.TradePageOf("BW004"); guard++)
            {
                Submit(Root.Q<Button>("fa-trade-next"));
                yield return null;
            }
            var row = Root.Q("fa-trade-BW004");
            Assert.That(row.Q<Label>(className: "fa-col-team").text, Is.EqualTo("선발 4번"));
            Assert.That(row.Q<Label>(className: "fa-col-ask").text, Is.EqualTo("가치 2.08"));
            Assert.That(Text("fa-market-note"), Does.StartWith("타 구단 1개 · 15명"));

            // 2) 진세훈 → 선수 상세 (타 구단: 교체·훈련·방출 대신 트레이드)
            Click(row);
            yield return null;
            var detail = Lobby.PlayerDetail;
            Assert.That(detail.IsOpen, Is.True);
            Assert.That(detail.IsOtherTeam, Is.True);
            Assert.That(Text("pd-name"), Is.EqualTo("진세훈"));
            Assert.That(Text("pd-role"), Is.EqualTo("LG 트윈스 · 선발 4번"));
            Assert.That(Root.Q("pd-trade-zone").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Root.Q("pd-danger").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "남의 선수는 방출할 수 없다");
            Assert.That(Root.Q("pd-train-row").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(Root.Q("pd-window").ClassListContains("team--lg"), Is.True, "상대 구단 컬러");
            Assert.That(Text("pd-trade-note"), Does.Contain("트레이드 가치 2.08").And.Contain("CPU 요구 가치 2.18"));
            AssertLayout("타 구단 선수 상세");

            // 3) [트레이드 제안] → 보낼 선수 목록 (1군 타자 · 투수)
            Submit(Root.Q<Button>("pd-trade"));
            yield return null;
            yield return null;
            Assert.That(detail.TradePicking, Is.True);
            Assert.That(Root.Q("pd-trade-list").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            int batters = manager.TradeOffers.Count(o => !o.Futures && !o.Player.IsPitcher);
            int pitchers = manager.TradeOffers.Count(o => !o.Futures && o.Player.IsPitcher);
            Assert.That(batters + pitchers, Is.EqualTo(20), "1군 20명");
            Assert.That(detail.OfferGroup, Is.EqualTo(0), "1군 타자부터");
            Assert.That(Root.Q<Button>("pd-offer-group-0").text, Is.EqualTo($"1군 타자 ({batters})"));
            Assert.That(Root.Query<Button>(className: "pd-offer").ToList().Count, Is.EqualTo(System.Math.Min(batters, PlayerDetailView.OfferPageSize)));
            Assert.That(Root.Q<Button>("pd-offer-WV004").ClassListContains("pd-offer--enough"), Is.True, "가치 2.38 ≥ 2.18");
            Assert.That(Root.Q<Button>("pd-offer-WV011").ClassListContains("pd-offer--enough"), Is.False);
            AssertLayout("보낼 선수 목록");
            Submit(Root.Q<Button>("pd-offer-group-1"));
            yield return null;
            Assert.That(detail.OfferGroup, Is.EqualTo(1));
            Assert.That(Root.Query<Button>(className: "pd-offer").ToList().Count, Is.EqualTo(System.Math.Min(pitchers, PlayerDetailView.OfferPageSize)));
            Assert.That(Root.Query<Button>(className: "pd-offer").ToList().All(b => b.name != "pd-offer-WV004"), Is.True);
            AssertLayout("보낼 선수 목록 · 투수");
            Submit(Root.Q<Button>("pd-offer-group-0"));
            yield return null;

            // 4) 거절: 피준서(가치 0) → 모달 유지, 아무것도 바뀌지 않는다
            Submit(Root.Q<Button>("pd-offer-WV011"));
            yield return null;
            Assert.That(detail.IsOpen, Is.True, "거절이면 모달 유지");
            Assert.That(Text("pd-trade-result"), Is.EqualTo("[거절] 상대 구단이 제안을 매몰차게 거절했습니다."));
            Assert.That(manager.Season.Signings, Is.Empty);
            Assert.That(LineupName(4), Is.EqualTo("표준혁"));

            // 5) 성사: 표준혁 → 진세훈이 바로 4번 1루수
            long payroll = manager.Season.Payroll;
            Submit(Root.Q<Button>("pd-offer-WV004"));
            yield return null;
            yield return null;
            Assert.That(detail.IsOpen, Is.False, "성사되면 상세가 닫힌다");
            Assert.That(manager.Season.Signings.Count, Is.EqualTo(2));
            Assert.That(LineupName(4), Is.EqualTo("진세훈"), "라인업 즉시 반영 (김헌곤 룰 — 받은 1루수가 그 자리로)");
            Assert.That(manager.Season.Payroll, Is.EqualTo(payroll - 477_000_000 + 342_000_000));
            Assert.That(Text("lineup-hint"), Is.EqualTo("트레이드 성사 · 표준혁 내주고 진세훈 영입 (LG 트윈스) · 진세훈 선발 라인업 합류 · 1루수 자리: 진세훈"));
            Assert.That(Root.Q("news-0").Q<Label>(className: "news-tag").text, Is.EqualTo("[오피셜]"));
            Assert.That(Root.Q("news-0").Q<Label>(className: "news-text").text,
                Is.EqualTo("삼성 라이온즈, LG 트윈스와 1:1 대형 트레이드 성사!\n(표준혁\u00A0내주고\u00A0진세훈\u00A0영입)"),
                "Phase 35: 괄호 덩어리는 다음 줄에 통째로");
            Assert.That(fa.IsOpen, Is.True, "FA 모달은 열린 채 트레이드 데스크에 결과");
            Assert.That(Text("fa-trade-result"), Does.StartWith("트레이드 성사 · 표준혁 → LG 트윈스 · 진세훈 → 선발 라인업 합류"));
            Assert.That(fa.TradePageOf("BW004"), Is.EqualTo(-1));
            Assert.That(fa.TradePageOf("WV004"), Is.GreaterThanOrEqualTo(0), "보낸 선수는 이제 LG 명단");
            AssertLayout("트레이드 성사");

            // 6) 다시 켜도 남는다
            DestroyManager();
            yield return null;
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForLobby();
            Assert.That(LineupName(4), Is.EqualTo("진세훈"));
            Assert.That(SeasonManager.Instance.Season.Signings.All(s => s.IsTrade), Is.True);
            Dispose(r);
        }

        [UnityTest]
        public IEnumerator 탭을_FA_명단으로_돌리면_기존_협상_테이블로_돌아온다()
        {
            Lobby.OpenFaMarket();
            yield return null;
            var fa = Lobby.FaMarket;
            fa.ShowTab(FaTab.Trade);
            yield return null;
            fa.ShowTab(FaTab.Market);
            yield return null;
            Assert.That(Text("fa-title"), Is.EqualTo("스토브리그 · FA 시장"));
            Assert.That(Root.Q("fa-table").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Root.Q("fa-trade-panel").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(Root.Q("fa-list").childCount, Is.Zero, "Phase 35: FA 시장은 자유계약 선수만 — LG 선수는 타 구단 명단(트레이드)에만");
            Assert.That(Root.Q<Button>("fa-tab-market").ClassListContains("fa-tab--active"), Is.True);
        }
    }
}
