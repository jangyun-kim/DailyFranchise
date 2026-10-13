using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// GameScreen 씬을 1920×1080 RenderTexture에 렌더링해 레이아웃과 재생 연출을 검증한다.
    /// PanelSettings 에셋을 직접 바꾸지 않도록 복제본에 RenderTexture를 연결한다.
    /// </summary>
    public class GameScreenPlayModeTests
    {
        GameScreenController _controller;
        UIDocument _doc;
        PanelSettings _panelCopy;
        RenderTexture _rt;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("GameScreen");
            yield return null;

            _controller = Object.FindAnyObjectByType<GameScreenController>();
            Assert.That(_controller, Is.Not.Null, "GameScreen 씬에 GameScreenController가 없음");
            _doc = _controller.GetComponent<UIDocument>();

            _rt = new RenderTexture((int)LayoutChecker.ScreenWidth, (int)LayoutChecker.ScreenHeight, 24);
            _rt.Create();
            _panelCopy = Object.Instantiate(_doc.panelSettings);
            _panelCopy.targetTexture = _rt;
            _doc.panelSettings = _panelCopy;

            _controller.Playback.Pause();
            _controller.Playback.Restart();
            for (int i = 0; i < 3; i++) yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            if (_panelCopy != null) Object.Destroy(_panelCopy);
        }

        VisualElement Screen => _doc.rootVisualElement.Q("screen");

        [UnityTest]
        public IEnumerator 모든_재생_위치에서_레이아웃_넘침과_텍스트_잘림이_없다()
        {
            var playback = _controller.Playback;
            for (int index = -1; index < playback.Count; index++)
            {
                playback.SeekTo(index);
                yield return null;
                yield return null;
                var issues = LayoutChecker.Check(Screen);
                Assert.That(issues, Is.Empty, $"[위치 {index}]\n" + string.Join("\n", issues));
            }
        }

        [UnityTest]
        public IEnumerator x4_재생_중에는_현재_공만_표시하고_레이아웃이_유지된다()
        {
            var playback = _controller.Playback;
            playback.SeekTo(3);
            playback.SetSpeed(4);
            playback.Play();
            yield return null;
            yield return null;

            Assert.That(_doc.rootVisualElement.Q("abs-markers").childCount, Is.EqualTo(1));
            var issues = LayoutChecker.Check(Screen);
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        [UnityTest]
        public IEnumerator x2_자동_재생이_끝까지_진행되고_판정_점등이_해제된다()
        {
            var playback = _controller.Playback;
            int flashes = 0;
            playback.IndexChanged += _ => { if (_controller.AbsZone.IsFlashing) flashes++; };
            playback.SetSpeed(2);
            playback.Play();

            float timeout = Time.realtimeSinceStartup + 20f;
            while (playback.IsPlaying && Time.realtimeSinceStartup < timeout) yield return null;

            Assert.That(playback.IsPlaying, Is.False, "제한 시간 안에 끝나지 않음");
            Assert.That(playback.Index, Is.EqualTo(playback.Count - 1));
            Assert.That(_controller.AbsZone.IsFlashing, Is.True, "마지막 투구 직후 점등 중이어야 함");

            yield return new WaitForSecondsRealtime(GameScreenController.FlashSecondsFor(2) + 0.3f);
            Assert.That(_controller.AbsZone.IsFlashing, Is.False, "점등이 해제되지 않음");
            Assert.That(flashes, Is.EqualTo(playback.Count), "모든 투구 직후 점등되어야 함");
        }

        [UnityTest]
        public IEnumerator x4에서는_판정_점등을_생략한다()
        {
            var playback = _controller.Playback;
            playback.SetSpeed(4);
            playback.StepPitch();
            yield return null;
            Assert.That(_controller.AbsZone.IsFlashing, Is.False);
        }

        [UnityTest]
        public IEnumerator 더미_데이터로_미연결_문구가_사라진다()
        {
            _controller.Playback.SeekTo(4);
            yield return null;
            _doc.rootVisualElement.Query<Label>().ForEach(l =>
                Assert.That(l.text, Does.Not.Contain("미연결"), $"'{l.name}' 라벨에 미연결 문구"));
            Assert.That(_doc.rootVisualElement.Q<Label>("batter-name").text, Is.EqualTo("최도윤"));
        }
    }
}
