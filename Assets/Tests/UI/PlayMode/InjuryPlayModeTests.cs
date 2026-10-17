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
    /// Phase 36: 부상 → 로비 뉴스 [부상] · 라인업에서 빠짐 · 부상자 명단 칩 "✚ 부상(N)" · 선수 상세의 1군 등록·방출 잠금과 이유,
    /// N경기를 치르면 칩이 사라지고 1군 등록이 열린다. 실제 데이터(삼성)로 돌린다 (타 구단 부상 선수의 트레이드 잠금은 TradePlayModeTests).
    /// </summary>
    public class InjuryPlayModeTests
    {
        static readonly string TestSavePath = Path.Combine(Application.temporaryCachePath, "playmode_injury_" + SaveManager.FileName);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyManager();
            SeasonManager.SavePathOverride = TestSavePath;
            SeasonManager.InjuryRandomOverride = new ScriptedRandom(0.999);
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

        /// <summary>로비 라인업 행(선발 9명)의 선수 ID.</summary>
        static string[] LineupIds() => Root.Q("roster").Children().Select(r => r.userData).OfType<DailyFranchise.Sim.Data.LineupSpot>()
            .Select(s => SeasonManager.Instance.CurrentLineup.PlayerAt(s)).ToArray();

        [UnityTest, Timeout(120000)]
        public IEnumerator 선발_타자가_다치면_뉴스와_부상자_명단에_보이고_1군_등록이_잠기며_N경기_뒤_풀린다()
        {
            var manager = SeasonManager.Instance;
            var r = RenderAt1080();
            yield return null;
            var hurt = manager.MyRoster.Lineup[0];
            int count = RosterLimits.CountOf(manager.MyRoster);

            // 1) 부상 (디버그 강제 부상 — 경기 결산과 같은 기록·뉴스)
            Assert.That(manager.ForceInjury(hurt.Id, 3), Is.True);
            yield return null;
            yield return null;
            Assert.That(manager.Season.InjuryGamesOf(hurt.Id), Is.EqualTo(3));
            Assert.That(manager.MyRoster.Lineup.Any(p => p.Id == hurt.Id), Is.False, "라인업에서 빠진다");
            Assert.That(manager.MyRoster.Reserves.Any(p => p.Id == hurt.Id), Is.True, "퓨처스로 내려간다");
            Assert.That(manager.RosterProblem, Is.Null, "빈 타순은 김헌곤 룰로 메워 경기를 그대로 시작할 수 있다");
            Assert.That(RosterLimits.CountOf(manager.MyRoster), Is.EqualTo(count), "퓨처스에서 올라와 정원 그대로");
            Assert.That(LineupIds(), Has.No.Member(hurt.Id));

            Assert.That(Root.Q("news-0").Q<Label>(className: "news-tag").text, Is.EqualTo("[부상]"));
            Assert.That(Root.Q("news-0").Q<Label>(className: "news-text").text, Does.Contain($"{hurt.Name}, 경기 중 부상으로 3경기 결장!"));
            var chip = Root.Q<Label>("inj-" + hurt.Id);
            Assert.That(chip, Is.Not.Null, "부상자 명단 칩");
            Assert.That(chip.text, Is.EqualTo($"{hurt.Name} ✚ 부상(3)"));
            Assert.That(Text("futures-rookies"), Does.StartWith("✚ 부상자 명단 1명"));
            AssertLayout("부상자 명단");

            // 2) 선수 상세: 1군 등록·방출 잠금, 이유
            Assert.That(Lobby.OpenPlayer(hurt.Id), Is.True);
            yield return null;
            Assert.That(Text("pd-condition"), Is.EqualTo("✚ 부상(3경기)"));
            Assert.That((Root.Q<Button>("pd-move").text, Root.Q<Button>("pd-move").enabledSelf), Is.EqualTo(("1군 등록 불가 · 부상 중", false)));
            Assert.That(Text("pd-move-note"), Is.EqualTo(InjuryRules.LockedText(3)));
            Assert.That(Root.Q<Button>("pd-release").enabledSelf, Is.False);
            Assert.That(Text("pd-release-note"), Is.EqualTo(StoveLeague.ReleaseBlockText(ReleaseBlock.Injured)));
            Assert.That(Lobby.PlayerDetail.PressMove(), Is.False, "눌러도 열리지 않는다");
            AssertLayout("부상 선수 상세");
            Lobby.PlayerDetail.Close();
            yield return null;

            // 3) 3경기를 치르면 회복 → 칩이 사라지고 1군 등록이 열린다
            for (int i = 0; i < 3; i++)
            {
                Assert.That(manager.Season.IsInjured(hurt.Id), Is.True, $"{i}경기 뒤에도 부상 중");
                manager.Season.StartNextGame();
                manager.AdvanceSeason(manager.Season.ScheduleScore(4, 2));
            }
            yield return null;
            yield return null;
            Assert.That(manager.Season.IsInjured(hurt.Id), Is.False);
            Assert.That(Root.Q<Label>("inj-" + hurt.Id), Is.Null, "회복하면 부상 칩이 사라진다");
            Assert.That(Root.Q("news-0").Q<Label>(className: "news-tag").text, Is.EqualTo("[복귀]"));
            Assert.That(Lobby.OpenPlayer(hurt.Id), Is.True);
            yield return null;
            Assert.That(Text("pd-condition"), Is.EqualTo("컨디션 보통"));
            Assert.That(Root.Q<Button>("pd-move").enabledSelf, Is.True, "퓨처스에서 1군 등록할 수 있다");
            Assert.That(Root.Q<Button>("pd-move").text, Is.EqualTo("1군 등록 · 벤치와 맞바꾸기"));
            Lobby.PlayerDetail.Close();
            Dispose(r);
        }
    }
}
