using System.Collections;
using DailyFranchise.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>로비 ↔ 경기 화면 왕복과 SeasonManager 수명 검증.</summary>
    public class SceneFlowPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyManager();
            SceneManager.LoadScene(SeasonManager.LobbyScene);
            yield return WaitForScene(SeasonManager.LobbyScene);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyManager();
            yield return null;
        }

        static void DestroyManager()
        {
            if (SeasonManager.Instance != null) Object.DestroyImmediate(SeasonManager.Instance.gameObject);
        }

        static IEnumerator WaitForScene(string name)
        {
            float timeout = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(name), $"{name} 씬으로 전환되지 않음");
            yield return null;
            yield return null;
        }

        static VisualElement LobbyRoot() =>
            Object.FindAnyObjectByType<LobbyController>().GetComponent<UIDocument>().rootVisualElement;

        static void Submit(Button button)
        {
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        [UnityTest]
        public IEnumerator 로비는_일정_순위_라인업과_다음_경기를_표시하고_1920x1080에_맞는다()
        {
            var lobby = Object.FindAnyObjectByType<LobbyController>();
            Assert.That(lobby, Is.Not.Null);
            Assert.That(SeasonManager.Instance, Is.Not.Null, "로비 진입 시 SeasonManager 생성");

            var doc = lobby.GetComponent<UIDocument>();
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create();
            var panelCopy = Object.Instantiate(doc.panelSettings);
            panelCopy.targetTexture = rt;
            doc.panelSettings = panelCopy;
            yield return null;
            yield return null;

            var root = doc.rootVisualElement;
            Assert.That(root.Q("schedule").childCount, Is.EqualTo(10));
            Assert.That(root.Q("standings").childCount, Is.EqualTo(10));
            Assert.That(root.Q("roster").childCount, Is.EqualTo(9));
            Assert.That(root.Q<Label>("next-game").text, Does.StartWith("경기 1 · "));
            Assert.That(root.Q<Button>("start-game").enabledSelf, Is.True);

            var issues = LayoutChecker.Check(root.Q("screen"));
            Assert.That(issues, Is.Empty, string.Join("\n", issues));

            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(panelCopy);
        }

        [UnityTest]
        public IEnumerator 로비와_경기_화면을_세_번_왕복해도_상태가_이어지고_중복이_없다()
        {
            for (int round = 1; round <= 3; round++)
            {
                var lobbyRoot = LobbyRoot();
                Assert.That(lobbyRoot.Q<Label>("next-game").text, Does.StartWith($"경기 {round} · "), $"{round}회차 로비");

                Submit(lobbyRoot.Q<Button>("start-game"));
                Submit(lobbyRoot.Q<Button>("start-game")); // 연타해도 한 번만 시작
                yield return WaitForScene(SeasonManager.GameScene);

                var manager = SeasonManager.Instance;
                Assert.That(Object.FindObjectsByType<SeasonManager>().Length, Is.EqualTo(1));
                Assert.That(manager.Season.InProgress.Number, Is.EqualTo(round));

                var game = Object.FindAnyObjectByType<GameScreenController>();
                Assert.That(game, Is.Not.Null);
                Assert.That(game.HasExitHandler, Is.True, "SeasonManager가 경기 종료를 구독");

                // 자동 재생을 끝까지 보낸 뒤 [경기 종료]
                game.Playback.SetSpeed(4);
                game.Playback.SkipToEnd();
                yield return null;
                var exit = game.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ctl-game-end");
                Assert.That(exit.enabledSelf, Is.True, "장면 끝에서도 경기 종료 가능");
                Submit(exit);
                Submit(exit); // 연타해도 한 번만 처리
                yield return WaitForScene(SeasonManager.LobbyScene);

                Assert.That(manager.ExitsHandled, Is.EqualTo(round), "종료 요청이 회차당 정확히 한 번 처리됨");
                Assert.That(manager.Season.InProgress, Is.Null);
                Assert.That(manager.Season.GamesPlayed, Is.EqualTo(round));
                Assert.That(LobbyRoot().Q("game-" + round).ClassListContains("lobby-row--final"), Is.True);
            }

            Assert.That(LobbyRoot().Q<Label>("my-record").text, Does.Contain("진행 3 / 10경기"));
        }

        [UnityTest]
        public IEnumerator 경기_화면_단독_실행에서는_경기_종료가_장면_끝으로_이동한다()
        {
            DestroyManager();
            SceneManager.LoadScene(SeasonManager.GameScene);
            yield return WaitForScene(SeasonManager.GameScene);

            var game = Object.FindAnyObjectByType<GameScreenController>();
            Assert.That(game.HasExitHandler, Is.False);
            game.Playback.Pause();
            Submit(game.GetComponent<UIDocument>().rootVisualElement.Q<Button>("ctl-game-end"));
            yield return null;
            Assert.That(game.Playback.IsAtEnd, Is.True);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SeasonManager.GameScene), "씬은 그대로");
        }
    }
}
