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
    /// <summary>
    /// Phase 25~26.5: 로비 [스카우트 / FA] → FA 시장 모달, 방출(위약금·자유계약), 제시액·확률 표시, 협상 성공/결렬/확정 영입과
    /// 로비 반영, 저장. 실제 데이터(삼성 2024, 1개 구단)라 처음 시장은 비어 있고, 방출한 선수가 시장에 나온다.
    /// </summary>
    public class FaMarketPlayModeTests
    {
        static readonly string TestSavePath = Path.Combine(Application.temporaryCachePath, "playmode_fa_" + SaveManager.FileName);

        const string Heongon = "p_삼성_2024_김헌곤";   // 벤치 1번, WAR 0.56 → 연봉 1억 1,400만
        const string Jigwang = "p_삼성_2024_최지광";   // 불펜 7번, WAR 1.56 → 연봉 2억 6,400만
        const long Budget0 = 5_000_000_000;
        const long Payroll0 = 6_294_000_000;

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

        static string[] BenchNames() =>
            Root.Q("bench").Children().Select(r => r.Q<Label>(className: "col-name").text).ToArray();

        static void AssertLayout(string label)
        {
            var issues = LayoutChecker.Check(Root.Q("screen"));
            Assert.That(issues, Is.Empty, $"[{label}]\n" + string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator FA_버튼으로_시장을_열면_재정과_정원이_보이고_다른_구단_데이터가_없으면_시장은_비어_있다()
        {
            var r = RenderAt1080();
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
            Assert.That(Root.Q("fa-list").childCount, Is.Zero, "실제 데이터는 삼성 1개 구단 → 처음엔 데려올 선수가 없다");
            Assert.That(Text("fa-market-note"), Is.EqualTo("시장에 남은 선수가 없습니다."));
            Assert.That(fa.SelectedId, Is.Null);
            Assert.That(Text("fa-player-name"), Is.EqualTo("—"));
            Assert.That(Root.Q<Button>("fa-negotiate").enabledSelf, Is.False);
            Assert.That(Root.Q<Button>("fa-ceiling").enabledSelf, Is.False);
            Assert.That(Text("fa-budget"), Is.EqualTo("예산 ₩ 50억"));
            Assert.That(Text("fa-cap"), Is.EqualTo("캡 여유 ₩15억 7,350만"), "캡 78.675억 − 페이롤 62.94억");
            Assert.That(Text("fa-roster"), Is.EqualTo("1군 26 / 26 · 벤치 5/5 · 불펜 7/7"));
            AssertLayout("빈 시장");

            Submit(Root.Q<Button>("fa-close"));
            yield return null;
            Assert.That(fa.IsOpen, Is.False);
            Assert.That(Root.Q("fa-modal").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Dispose(r);
        }

        [UnityTest]
        public IEnumerator 벤치_선수를_방출하면_위약금이_빠지고_FA_시장에_나타나며_1920x1080에_맞는다()
        {
            var manager = SeasonManager.Instance;
            var r = RenderAt1080();
            yield return null;
            yield return null;
            Assert.That(Root.Query<Button>(className: "lobby-release").ToList(), Is.Empty, "Phase 31: 목록에는 방출 버튼이 없다 (선수 상세 안에서만)");
            Assert.That(Root.Q("bullpen").Children().Count(), Is.EqualTo(7), "1군 불펜 칩 7개");
            AssertLayout("1군 26명 로비");

            // 선발 타자는 상세에서 방출할 수 있고 (Phase 30), 로테이션 투수는 막힌다
            Click(Root.Q("lineup-1"));
            yield return null;
            Assert.That(Lobby.PlayerDetail.IsOpen, Is.True);
            Assert.That(Root.Q<Button>("pd-release").enabledSelf, Is.True, "Phase 30: 선발 타자도 방출할 수 있다");
            Submit(Root.Q<Button>("pd-close"));
            Click(Root.Q("rotation-1"));
            yield return null;
            Assert.That(Root.Q<Button>("pd-release").enabledSelf, Is.False, "로테이션 투수는 막힌다");
            Assert.That(Text("pd-release-note"), Is.EqualTo(StoveLeague.ReleaseBlockText(ReleaseBlock.Starter)));
            Submit(Root.Q<Button>("pd-close"));
            Assert.That(Lobby.PlayerDetail.IsOpen, Is.False);

            // 벤치 1번 행 → 선수 상세 → [방출] (확인 상태) → [방출 확정]
            Click(Root.Q("bench-1"));
            yield return null;
            Assert.That(Text("pd-name"), Is.EqualTo("김헌곤"));
            var release = Root.Q<Button>("pd-release");
            Assert.That(release.enabledSelf, Is.True);
            Assert.That(release.tooltip, Is.EqualTo("방출 · 위약금 −₩5,700만 (연봉 50%)"));
            AssertLayout("선수 상세");
            Submit(release);
            yield return null;
            Assert.That(Lobby.PlayerDetail.ReleaseArmed, Is.True, "첫 누름은 확인 상태로만");
            Assert.That(release.text, Is.EqualTo("방출 확정 · 위약금 −₩5,700만"));
            Assert.That(manager.Season.Budget, Is.EqualTo(Budget0), "아직 방출하지 않았다");
            Submit(release);
            yield return null;
            Assert.That(Lobby.PlayerDetail.IsOpen, Is.False, "방출하면 상세가 닫힌다");
            Assert.That(manager.Season.Budget, Is.EqualTo(Budget0 - 57_000_000));
            Assert.That(BenchNames(), Is.EqualTo(new[] { "김현준", "윤정빈", "이병헌", "안주형" }));
            Assert.That(Text("lineup-hint"), Is.EqualTo("방출 · 김헌곤 → FA 시장 (위약금 −₩5,700만)"));
            Assert.That(Text("fo-budget"), Is.EqualTo("₩ 49억 4,300만"));
            Assert.That(manager.Season.Payroll, Is.EqualTo(Payroll0 - 114_000_000), "페이롤에서 빠진다");
            Assert.That(Lobby.Selection.Selected, Is.Null, "방출하면 선택이 풀린다");

            Lobby.OpenFaMarket();
            yield return null;
            Assert.That(Root.Q("fa-list").childCount, Is.EqualTo(1));
            var freed = Root.Q("fa-" + Heongon);
            Assert.That(freed.Q<Label>(className: "fa-col-name").text, Is.EqualTo("김헌곤"));
            Assert.That(freed.Q<Label>(className: "fa-col-team").text, Is.EqualTo(LeagueRosterBook.FreeAgentTeam));
            Assert.That(Text("fa-roster"), Is.EqualTo("1군 25 / 26 · 벤치 4/5 · 불펜 7/7"));
            AssertLayout("방출 뒤 시장");

            // 다시 켜도 방출이 남는다
            DestroyManager();
            yield return null;
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForLobby();
            Assert.That(BenchNames(), Has.None.EqualTo("김헌곤"));
            Assert.That(SeasonManager.Instance.Season.Budget, Is.EqualTo(Budget0 - 57_000_000));
            Dispose(r);
        }

        [UnityTest]
        public IEnumerator 제시액을_바꾸면_확률과_환급_안내만_다시_그린다()
        {
            Lobby.ReleasePlayer(Heongon);
            Lobby.OpenFaMarket();
            yield return null;
            var fa = Lobby.FaMarket;
            int lists = fa.ListRenderCount;
            Assert.That(fa.SelectedId, Is.EqualTo(Heongon), "시장 첫 선수가 골라진다");
            Assert.That(Text("fa-player-name"), Is.EqualTo("김헌곤"));
            Assert.That(Text("fa-ask"), Is.EqualTo("요구액 ₩1억 2,540만 · 연봉 ₩1억 1,400만 + 인기 보정 10%"));
            Assert.That(Text("fa-odds"), Is.EqualTo("영입 성공 확률 50%"), "요구액 100% · TP 0");
            Assert.That(Text("fa-offer"), Is.EqualTo("₩1억 2,540만 · 요구액의 100%"));

            Submit(Root.Q<Button>("fa-offer-minus5"));
            Submit(Root.Q<Button>("fa-offer-minus5"));
            Assert.That(fa.OfferPercent, Is.EqualTo(90));
            Assert.That(Text("fa-odds"), Is.EqualTo("영입 성공 확률 25%"));
            Assert.That(Text("fa-fee"), Is.EqualTo("수수료 ₩627만 (시도마다) · 결렬 시 TP +6"));
            Assert.That(Text("fa-after"), Does.StartWith("성공 시 경기당 유지비 +₩78만"), "연봉 1억 1,286만 ÷ 144");
            Submit(Root.Q<Button>("fa-offer-minus1"));
            Assert.That(Text("fa-fee"), Does.EndWith("헐값 제시(90% 미만) → 결렬 시 환급 없음"));

            for (int i = 0; i < 20; i++) fa.Step(5);
            Assert.That(fa.OfferPercent, Is.EqualTo(FaMarketView.MaxPercent));
            Assert.That(Text("fa-odds"), Is.EqualTo("영입 성공 확률 95%"));
            Assert.That(fa.ListRenderCount, Is.EqualTo(lists), "선택·제시액은 목록을 다시 만들지 않는다");
        }

        static string[] NewsCells(int index) =>
            Root.Q("news-" + index).Query<Label>().ToList().Select(l => l.text).ToArray();

        [UnityTest]
        public IEnumerator Phase28_방출과_FA_영입이_말머리를_달고_리그_뉴스_패널에_바로_오른다()
        {
            var r = RenderAt1080();
            yield return null;
            var news = Root.Q("news");
            Assert.That(news.childCount, Is.EqualTo(1));
            Assert.That(news[0].ClassListContains("news-empty"), Is.True, "새 시즌은 빈 피드");

            var manager = SeasonManager.Instance;
            manager.NegotiationRandom = new FixedRandom(0.0);
            Lobby.ReleasePlayer(Heongon);
            yield return null;
            Assert.That(NewsCells(0), Is.EqualTo(new[] { "[단독]", "삼성 라이온즈, 성적 부진으로 김헌곤 방출 결정.", "2026 · 5.15 (금)" }));
            Assert.That(Root.Q("news-0").Q<Label>(className: "news-tag").ClassListContains("news-tag--exclusive"), Is.True);
            Assert.That(Root.Q("news-0").ClassListContains("news-row--latest"), Is.True);

            Lobby.OpenFaMarket();
            yield return null;
            Submit(Root.Q<Button>("fa-negotiate"));
            yield return null;
            Assert.That(NewsCells(0), Is.EqualTo(new[] { "[오피셜]", "삼성 라이온즈, FA 시장에서 김헌곤과 ₩ 1억 2,540만에 전격 계약!", "2026 · 5.15 (금)" }));
            Assert.That(Root.Q("news-0").Q<Label>(className: "news-tag").ClassListContains("news-tag--official"), Is.True);
            Assert.That(NewsCells(1)[1], Is.EqualTo("삼성 라이온즈, 성적 부진으로 김헌곤 방출 결정."));
            Assert.That(Root.Q("news-1").ClassListContains("news-row--latest"), Is.False);
            Lobby.FaMarket.Close();
            yield return null;
            AssertLayout("뉴스 2건");

            // 뉴스는 5건까지만 보이고 저장된다
            for (int i = 0; i < 6; i++)
                manager.Season.AddNews(new NewsItem(NewsKind.League, NewsWriter.LeagueTag, "KIA 타이거즈", $"KIA 타이거즈, 한화 이글스에 1{i}:2 대승", 2026, "5.15 (금)"));
            manager.Saves.Save(manager.Season);
            Dispose(r);
            DestroyManager();
            yield return null;
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForLobby();
            Assert.That(SeasonManager.Instance.Season.RecentNews.Count, Is.EqualTo(8));
            Assert.That(Root.Q("news").childCount, Is.EqualTo(LobbyController.NewsShown), "패널은 최신 5건");
            Assert.That(NewsCells(0)[1], Is.EqualTo("KIA 타이거즈, 한화 이글스에 15:2 대승"));
        }

        [UnityTest]
        public IEnumerator 방출한_선수와_다시_협상해_성공하면_벤치로_돌아오고_페이롤이_새_계약으로_잡히며_저장된다()
        {
            var manager = SeasonManager.Instance;
            manager.NegotiationRandom = new FixedRandom(0.0);
            Lobby.ReleasePlayer(Heongon);
            Lobby.OpenFaMarket();
            yield return null;

            Submit(Root.Q<Button>("fa-negotiate"));
            yield return null;
            var fa = Lobby.FaMarket;
            Assert.That(manager.Season.Signings.Select(s => (s.PlayerId, s.FromTeam, s.ToTeam)),
                Is.EqualTo(new[] { (Heongon, manager.Season.MyTeam, LeagueRosterBook.FreeAgentTeam), (Heongon, LeagueRosterBook.FreeAgentTeam, manager.Season.MyTeam) }));
            Assert.That(manager.Season.Budget, Is.EqualTo(Budget0 - 57_000_000 - 6_270_000), "위약금 + 수수료 (연봉은 경기마다)");
            Assert.That(manager.Season.Payroll, Is.EqualTo(Payroll0 - 114_000_000 + 125_400_000), "새 계약 연봉");
            Assert.That(Text("fa-result"), Does.StartWith("영입 성공 · 김헌곤 → 벤치 합류 (연봉 ₩1억 2,540만 · 수수료 ₩627만"));
            Assert.That(Root.Q("fa-list").childCount, Is.Zero);
            Assert.That(fa.SelectedId, Is.Null);
            Assert.That(Text("fa-budget"), Is.EqualTo("예산 ₩ 49억 3,673만"));
            Assert.That(Text("fa-roster"), Is.EqualTo("1군 26 / 26 · 벤치 5/5 · 불펜 7/7"));

            // 로비 반영: 헤더 예산·유지비, 벤치 끝
            Assert.That(Text("fo-budget"), Is.EqualTo("₩ 49억 3,673만"));
            Assert.That(Text("fo-budget-delta"), Is.EqualTo("승 +₩5,000만 · 유지비 −₩4,378만"));
            Assert.That(BenchNames().Last(), Is.EqualTo("김헌곤"));
            Assert.That(manager.MyRoster.Bench.Last().Team, Is.EqualTo(manager.Season.MyTeam));

            // 다시 켜도 남는다 (저장)
            DestroyManager();
            yield return null;
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForLobby();
            Assert.That(BenchNames().Last(), Is.EqualTo("김헌곤"));
            Assert.That(SeasonManager.Instance.Season.Budget, Is.EqualTo(Budget0 - 57_000_000 - 6_270_000));
        }

        [UnityTest]
        public IEnumerator 협상이_결렬되면_수수료만_내고_TP가_쌓이며_TP_100이면_확정_영입이_열린다()
        {
            var manager = SeasonManager.Instance;
            manager.NegotiationRandom = new FixedRandom(0.999);
            Lobby.ReleasePlayer(Heongon);   // 위약금 5,700만
            Lobby.ReleasePlayer(Jigwang);   // 위약금 1억 3,200만
            Lobby.OpenFaMarket();
            yield return null;
            var fa = Lobby.FaMarket;
            Assert.That(Root.Q("fa-list").childCount, Is.EqualTo(2));
            fa.Select(Heongon);
            Submit(Root.Q<Button>("fa-negotiate"));
            yield return null;
            long afterReleases = Budget0 - 57_000_000 - 132_000_000;
            Assert.That((manager.Season.Budget, manager.Season.TrustPoints), Is.EqualTo((afterReleases - 6_270_000, 6)));
            Assert.That(Text("fa-result"), Does.Contain("협상 결렬").And.Contain("TP +6 환급"));
            Assert.That(Text("fo-trust"), Is.EqualTo("6 / 200"), "로비 헤더도 갱신");
            Assert.That(Text("fa-trust"), Is.EqualTo("신뢰도 TP 6 / 200"));
            Assert.That(fa.SelectedId, Is.EqualTo(Heongon), "결렬이면 같은 선수에 남는다");
            Assert.That(Root.Q<Button>("fa-ceiling").enabledSelf, Is.False, "TP 6");

            manager.Season.SetTrust(120, 0, 0, 0);
            fa.Refresh();
            fa.Select(Jigwang); // 연봉 2억 6,400만 → 요구액 2억 9,040만 · 수수료 1,452만
            var ceiling = Root.Q<Button>("fa-ceiling");
            Assert.That(ceiling.enabledSelf, Is.True);
            Submit(ceiling);
            yield return null;
            Assert.That(manager.Season.TrustPoints, Is.EqualTo(20));
            Assert.That(manager.Season.Budget, Is.EqualTo(afterReleases - 6_270_000 - 14_520_000));
            Assert.That(Text("fa-result"), Does.Contain("최지광 → 불펜 합류").And.Contain("TP 100 사용"));
            Assert.That(Root.Q("bullpen").Children().Select(c => c.Q<Label>().text), Has.Some.StartsWith("최지광("));
            Assert.That(Text("bullpen-title"), Is.EqualTo("불펜 7명"));
            Assert.That(Root.Q<Button>("fa-ceiling").enabledSelf, Is.False, "TP 20");
        }

        [UnityTest]
        public IEnumerator 예산이_수수료보다_적으면_경고하고_협상_버튼을_잠근다()
        {
            var manager = SeasonManager.Instance;
            Lobby.ReleasePlayer(Heongon);
            manager.Season.SetFinance(6_270_000 - 1, 50);
            var r = RenderAt1080();
            Lobby.OpenFaMarket();
            yield return null;
            yield return null;
            var fa = Lobby.FaMarket;
            Assert.That(Text("fa-warning"), Is.EqualTo("예산이 협상 수수료보다 적습니다."));
            Assert.That(Root.Q<Button>("fa-negotiate").enabledSelf, Is.False);
            Assert.That(fa.Negotiate().Block, Is.EqualTo(SigningBlock.Budget), "직접 불러도 막힌다");
            Assert.That(manager.Season.Signings.Count, Is.EqualTo(1), "방출 기록만");
            AssertLayout("예산 부족 경고");

            manager.Season.SetFinance(Budget0, 50);
            fa.Refresh();
            Assert.That(Text("fa-warning"), Is.Empty);
            Assert.That(Root.Q<Button>("fa-negotiate").enabledSelf, Is.True);
            Dispose(r);
        }

        [UnityTest]
        public IEnumerator 불펜_칩의_방출과_위약금이_예산보다_크면_잠긴다()
        {
            var manager = SeasonManager.Instance;
            var lobby = Lobby;
            string penId = manager.MyRoster.Bullpen[0].Id;
            var chip = Root.Q("bullpen-" + penId);
            Click(chip);
            yield return null;
            Assert.That(Lobby.PlayerDetail.PlayerId, Is.EqualTo(penId), "불펜 칩을 누르면 선수 상세");
            Submit(Root.Q<Button>("pd-release"));
            Submit(Root.Q<Button>("pd-release"));
            yield return null;
            Assert.That(Root.Q("bullpen").Children().Count(), Is.EqualTo(6));
            Assert.That(Text("bullpen-title"), Is.EqualTo("불펜 6명"));

            manager.Season.SetFinance(1, 50);
            lobby.enabled = false;
            lobby.enabled = true;
            yield return null;
            Click(Root.Q("bench-1"));
            yield return null;
            var release = Root.Q<Button>("pd-release");
            Assert.That(release.enabledSelf, Is.False);
            Assert.That(Text("pd-release-note"), Is.EqualTo("예산이 위약금보다 적어 방출할 수 없습니다."));
            Assert.That(release.tooltip, Is.EqualTo("예산이 위약금보다 적어 방출할 수 없습니다."));
            var o = lobby.ReleasePlayer(manager.MyRoster.Bench[0].Id);
            Assert.That(o.Block, Is.EqualTo(ReleaseBlock.Budget), "직접 불러도 막힌다");
            Assert.That(Text("lineup-hint"), Is.EqualTo("예산이 위약금보다 적어 방출할 수 없습니다."));
            Assert.That(Root.Q<Button>("start-game").enabledSelf, Is.True, "방출이 막혀도 경기는 시작할 수 있다");
        }
    }
}
