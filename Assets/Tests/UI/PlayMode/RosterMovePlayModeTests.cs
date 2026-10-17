using System.Collections;
using System.IO;
using System.Linq;
using DailyFranchise.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 35: 로비 선수 상세 → [1군 말소 · 퓨처스와 맞바꾸기] → 퓨처스 타자 고르기 → 로비 벤치·안내 즉시 반영·저장,
    /// 선발 타자는 잠김, 퓨처스 선수는 [1군 등록]. 실제 데이터(삼성 구단 전체 선수단 — 1군 밖은 퓨처스)로 돌린다.
    /// </summary>
    public class RosterMovePlayModeTests
    {
        static readonly string TestSavePath = Path.Combine(Application.temporaryCachePath, "playmode_move_" + SaveManager.FileName);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyManager();
            SeasonManager.SavePathOverride = TestSavePath;
            SeasonManager.InjuryRandomOverride = new DailyFranchise.Sim.ScriptedRandom(0.999); // 우연한 부상 없이 (Phase 36)
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
            SeasonManager.InjuryRandomOverride = null;
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

        static string[] BenchNames() =>
            Root.Q("bench").Children().Select(r => r.Q<Label>(className: "col-name").text).ToArray();

        static int MoveButtons() => Root.Q("pd-move-list").Query<Button>(className: "pd-offer").ToList().Count;

        [UnityTest, Timeout(120000)]
        public IEnumerator 벤치_선수를_퓨처스_타자와_맞바꾸면_로비_벤치에_바로_보이고_정원이_그대로다()
        {
            var manager = SeasonManager.Instance;
            var r = RenderAt1080();
            yield return null;
            var before = manager.MyRoster;
            Assert.That(before.Reserves.Count(p => !p.IsPitcher), Is.GreaterThan(0), "실제 데이터: 퓨처스 타자가 있다");
            var bench = before.Bench[0];
            int count = RosterLimits.CountOf(before);
            var partners = RosterMoves.Partners(before, bench.Id);
            Assert.That(partners.All(p => !p.IsPitcher && before.Reserves.Contains(p)), Is.True);

            // 1) 벤치 선수 상세 → [1군 말소 · 퓨처스와 맞바꾸기]
            Assert.That(Lobby.OpenPlayer(bench.Id), Is.True);
            yield return null;
            var detail = Lobby.PlayerDetail;
            Assert.That(Root.Q("pd-move-zone").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            var move = Root.Q<Button>("pd-move");
            Assert.That((move.text, move.enabledSelf), Is.EqualTo(("1군 말소 · 퓨처스와 맞바꾸기", true)));
            Assert.That(Text("pd-move-note"), Does.Contain("정원 그대로"));
            AssertLayout("벤치 선수 상세");

            Submit(move);
            yield return null;
            yield return null;
            Assert.That(detail.MovePicking, Is.True);
            Assert.That(Root.Q<Button>("pd-move").text, Is.EqualTo("취소"));
            Assert.That(MoveButtons(), Is.EqualTo(System.Math.Min(partners.Count, PlayerDetailView.MovePageSize)));
            Assert.That(Root.Q("pd-swap").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "고르는 동안 교체·훈련·방출은 접는다");
            Assert.That(Root.Q("pd-move-pager").resolvedStyle.display,
                Is.EqualTo(partners.Count > PlayerDetailView.MovePageSize ? DisplayStyle.Flex : DisplayStyle.None));
            AssertLayout("퓨처스 타자 목록");
            if (partners.Count > PlayerDetailView.MovePageSize)
            {
                Submit(Root.Q<Button>("pd-move-next"));
                yield return null;
                Assert.That(detail.MovePage, Is.EqualTo(1));
                AssertLayout("퓨처스 타자 목록 2쪽");
                Submit(Root.Q<Button>("pd-move-prev"));
                yield return null;
            }

            // 2) 첫 퓨처스 타자를 고른다 → 모달 닫힘 · 벤치 그 자리에 · 안내
            var up = partners[0];
            Submit(Root.Q<Button>("pd-move-" + up.Id));
            yield return null;
            yield return null;
            Assert.That(detail.IsOpen, Is.False);
            var after = manager.MyRoster;
            Assert.That(after.Bench[0].Id, Is.EqualTo(up.Id), "같은 벤치 자리");
            Assert.That(after.Reserves.Any(p => p.Id == bench.Id), Is.True);
            Assert.That((RosterLimits.CountOf(after), after.Bench.Count, after.Bullpen.Count), Is.EqualTo((count, before.Bench.Count, before.Bullpen.Count)));
            Assert.That(BenchNames()[0], Is.EqualTo(up.Name), "로비 벤치 즉시 반영");
            Assert.That(Text("lineup-hint"), Is.EqualTo($"1군 등록 · {up.Name} (벤치) · 퓨처스 말소 · {bench.Name}"));
            AssertLayout("맞바꾼 뒤 로비");

            // 3) 선발 타자는 잠김, 내려간 선수는 [1군 등록]
            Assert.That(Lobby.OpenPlayer(after.AtOrder(1).Id), Is.True);
            yield return null;
            Assert.That(Root.Q<Button>("pd-move").enabledSelf, Is.False);
            Assert.That(Text("pd-move-note"), Does.Contain("먼저 벤치·불펜으로"));
            detail.Close();
            Assert.That(Lobby.OpenPlayer(bench.Id), Is.True);
            yield return null;
            Assert.That(Root.Q<Button>("pd-move").text, Is.EqualTo("1군 등록 · 벤치와 맞바꾸기"));
            AssertLayout("퓨처스 선수 상세");
            detail.Close();

            // 4) 다시 켜도 남는다
            DestroyManager();
            yield return null;
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForLobby();
            Assert.That(SeasonManager.Instance.MyRoster.Bench[0].Id, Is.EqualTo(up.Id));
            Assert.That(SeasonManager.Instance.Season.Signings.Single().IsRosterMove, Is.True);
            Dispose(r);
        }
    }
}
