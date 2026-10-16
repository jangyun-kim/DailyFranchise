using System.Collections;
using System.IO;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 25: 로비 [스카우트 / FA] → FA 시장 모달, 제시액·확률 표시, 협상 성공/결렬/확정 영입과 로비 반영, 저장.</summary>
    public class FaMarketPlayModeTests
    {
        static readonly string TestSavePath = Path.Combine(Application.temporaryCachePath, "playmode_fa_" + SaveManager.FileName);

        sealed class FixedRandom : IRandomSource
        {
            readonly double _value;
            public FixedRandom(double value) => _value = value;
            public double NextDouble() => _value;
            public int Next(int maxExclusive) => 0;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyManager();
            SeasonManager.SavePathOverride = TestSavePath;
            SeasonManager.LoadDatabaseSynchronously = true;
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

        static string[] BenchNames() =>
            Root.Q("bench").Children().Select(r => r.Q<Label>(className: "col-name").text).ToArray();

        [UnityTest]
        public IEnumerator FA_버튼으로_시장을_열면_목록과_협상_테이블이_보이고_1920x1080에_맞는다()
        {
            var (rt, panel) = RenderAt1080();
            yield return null;
            var scout = Root.Q<Button>("scout-fa");
            Assert.That(scout.enabledSelf, Is.True, "선수 DB가 있으면 열린다");
            Assert.That(Root.Q("fa-modal").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            Submit(scout);
            yield return null;
            yield return null;
            var fa = Lobby.FaMarket;
            Assert.That(fa.IsOpen, Is.True);
            Assert.That(Root.Q("fa-modal").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(Root.Q("fa-list").childCount, Is.EqualTo(15));
            Assert.That(fa.SelectedId, Is.EqualTo("BW010"), "WAR 1위가 먼저 골라진다");
            Assert.That(Root.Q("fa-BW010").ClassListContains("fa-row--selected"), Is.True);
            Assert.That(Text("fa-odds"), Is.EqualTo("영입 성공 확률 50%"), "요구액 100% · TP 0");
            Assert.That(Text("fa-offer"), Is.EqualTo("₩448,800 · 요구액의 100%"));
            Assert.That(Text("fa-budget"), Is.EqualTo("예산 ₩ 5,000,000"));
            Assert.That(Text("fa-cap"), Is.EqualTo("캡 여유 ₩1,980,000"));
            Assert.That(Text("fa-roster"), Is.EqualTo("1군 20 / 26 · 벤치 3/5 · 불펜 3/7"));
            Assert.That(Root.Q<Button>("fa-negotiate").enabledSelf, Is.True);
            Assert.That(Root.Q<Button>("fa-ceiling").enabledSelf, Is.False, "TP 0");

            var issues = LayoutChecker.Check(Root.Q("screen"));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));

            Submit(Root.Q<Button>("fa-close"));
            yield return null;
            Assert.That(fa.IsOpen, Is.False);
            Assert.That(Root.Q("fa-modal").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panel);
        }

        [UnityTest]
        public IEnumerator 제시액을_바꾸면_확률과_환급_안내만_다시_그린다()
        {
            Lobby.OpenFaMarket();
            yield return null;
            var fa = Lobby.FaMarket;
            int lists = fa.ListRenderCount;
            fa.Select("BW004");
            Assert.That(Text("fa-player-name"), Is.EqualTo(SeasonManager.Instance.Database.FindPlayer("BW004").Name));
            Assert.That(Text("fa-ask"), Is.EqualTo("요구액 ₩376,200 · 연봉 ₩342,000 + 인기 보정 10%"));

            Submit(Root.Q<Button>("fa-offer-minus5"));
            Submit(Root.Q<Button>("fa-offer-minus5"));
            Assert.That(fa.OfferPercent, Is.EqualTo(90));
            Assert.That(Text("fa-odds"), Is.EqualTo("영입 성공 확률 25%"));
            Assert.That(Text("fa-fee"), Is.EqualTo("수수료 ₩18,810 (시도마다) · 결렬 시 TP +18"));
            Submit(Root.Q<Button>("fa-offer-minus1"));
            Assert.That(Text("fa-fee"), Does.EndWith("헐값 제시(90% 미만) → 결렬 시 환급 없음"));

            for (int i = 0; i < 20; i++) fa.Step(5);
            Assert.That(fa.OfferPercent, Is.EqualTo(FaMarketView.MaxPercent));
            Assert.That(Text("fa-odds"), Is.EqualTo("영입 성공 확률 95%"));
            Assert.That(fa.ListRenderCount, Is.EqualTo(lists), "선택·제시액은 목록을 다시 만들지 않는다");
        }

        [UnityTest]
        public IEnumerator 협상에_성공하면_예산이_줄고_선수가_로비_벤치에_들어오며_저장된다()
        {
            var manager = SeasonManager.Instance;
            manager.NegotiationRandom = new FixedRandom(0.0);
            Lobby.OpenFaMarket();
            yield return null;
            var fa = Lobby.FaMarket;
            fa.Select("BW004");
            string name = manager.Database.FindPlayer("BW004").Name;

            Submit(Root.Q<Button>("fa-negotiate"));
            yield return null;
            Assert.That(manager.Season.Signings.Single().PlayerId, Is.EqualTo("BW004"));
            Assert.That(manager.Season.Budget, Is.EqualTo(5_000_000 - 18_810 - 376_200));
            Assert.That(Text("fa-result"), Does.StartWith($"영입 성공 · {name} → 벤치 합류 (계약금 ₩376,200"));
            Assert.That(Root.Q("fa-list").childCount, Is.EqualTo(14));
            Assert.That(fa.SelectedId, Is.EqualTo("BW010"), "다음 선수");
            Assert.That(Text("fa-budget"), Is.EqualTo("예산 ₩ 4,604,990"));
            Assert.That(Text("fa-roster"), Does.StartWith("1군 21 / 26 · 벤치 4/5"));

            // 로비 반영: 헤더 예산, 벤치 4번째
            Assert.That(Text("fo-budget"), Is.EqualTo("₩ 4,604,990"));
            Assert.That(BenchNames().Last(), Is.EqualTo(name));
            Assert.That(manager.MyRoster.Bench.Last().Team, Is.EqualTo(manager.Season.MyTeam));

            // 다시 켜도 영입이 남는다 (저장)
            DestroyManager();
            yield return null;
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForLobby();
            Assert.That(BenchNames().Last(), Is.EqualTo(name));
            Assert.That(SeasonManager.Instance.Season.Budget, Is.EqualTo(4_604_990));
        }

        [UnityTest]
        public IEnumerator 협상이_결렬되면_수수료만_내고_TP가_쌓이며_TP_100이면_확정_영입이_열린다()
        {
            var manager = SeasonManager.Instance;
            manager.NegotiationRandom = new FixedRandom(0.999);
            Lobby.OpenFaMarket();
            yield return null;
            var fa = Lobby.FaMarket;
            fa.Select("BW004");
            Submit(Root.Q<Button>("fa-negotiate"));
            yield return null;
            Assert.That((manager.Season.Budget, manager.Season.TrustPoints), Is.EqualTo((5_000_000 - 18_810, 18)));
            Assert.That(manager.Season.Signings, Is.Empty);
            Assert.That(Text("fa-result"), Does.Contain("협상 결렬").And.Contain("TP +18 환급"));
            Assert.That(Text("fo-trust"), Is.EqualTo("18 / 200"), "로비 헤더도 갱신");
            Assert.That(Text("fa-trust"), Is.EqualTo("신뢰도 TP 18 / 200"));
            Assert.That(fa.SelectedId, Is.EqualTo("BW004"), "결렬이면 같은 선수에 남는다");

            manager.Season.SetTrust(120, 0, 0, 0);
            fa.Refresh();
            var ceiling = Root.Q<Button>("fa-ceiling");
            Assert.That(ceiling.enabledSelf, Is.True);
            fa.Select("BW011"); // 서울 불펜, 연봉 126,000 → 요구액 138,600 · 수수료 6,930
            string name = manager.Database.FindPlayer("BW011").Name;
            Submit(ceiling);
            yield return null;
            Assert.That(manager.Season.TrustPoints, Is.EqualTo(20));
            Assert.That(manager.Season.Budget, Is.EqualTo(5_000_000 - 18_810 - 6_930 - 138_600));
            Assert.That(Text("fa-result"), Does.Contain("불펜 합류").And.Contain("TP 100 사용"));
            Assert.That(Text("bullpen"), Does.Contain(name));
            Assert.That(Text("bullpen-title"), Is.EqualTo("불펜 4명"));
            Assert.That(Root.Q<Button>("fa-ceiling").enabledSelf, Is.False, "TP 20");
        }

        [UnityTest]
        public IEnumerator 벤치가_차면_경고하고_협상_버튼을_잠근다()
        {
            var manager = SeasonManager.Instance;
            manager.NegotiationRandom = new FixedRandom(0.0);
            Lobby.OpenFaMarket();
            yield return null;
            var fa = Lobby.FaMarket;
            foreach (var id in new[] { "BW008", "BW007" })
            {
                fa.Select(id);
                Assert.That(fa.Negotiate().Signed, Is.True, id);
            }
            yield return null;
            Assert.That(BenchNames().Length, Is.EqualTo(5));

            var (rt, panel) = RenderAt1080();
            fa.Select("BW006"); // 야수
            yield return null;
            yield return null;
            Assert.That(Text("fa-warning"), Does.Contain("벤치 정원 5명"));
            Assert.That(Root.Q<Button>("fa-negotiate").enabledSelf, Is.False);
            Assert.That(fa.Negotiate().Block, Is.EqualTo(SigningBlock.BenchFull), "직접 불러도 막힌다");
            Assert.That(manager.Season.Signings.Count, Is.EqualTo(2));

            fa.Select("BW011"); // 투수는 된다
            Assert.That(Text("fa-warning"), Is.Empty);
            Assert.That(Root.Q<Button>("fa-negotiate").enabledSelf, Is.True);

            var issues = LayoutChecker.Check(Root.Q("screen"));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
            fa.Close();
            yield return null;
            issues = LayoutChecker.Check(Root.Q("screen"));
            Assert.That(issues, Is.Empty, "벤치 5명 로비\n" + string.Join("\n", issues));
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panel);
        }
    }
}
