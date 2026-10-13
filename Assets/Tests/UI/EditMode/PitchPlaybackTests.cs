using System;
using System.Collections.Generic;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    public class PitchPlaybackTests
    {
        // 고정 장면: 0~4구 = 1타석(4구째 인플레이로 종료), 5~7구 = 2타석(삼진).
        const int FirstPaEnd = 4;
        const int LastIndex = 7;

        static PitchPlayback Create() => new PitchPlayback(Fixture8thBottom.Create().Events);

        [Test]
        public void 첫_투구_전_정지_상태로_시작()
        {
            var p = Create();
            Assert.That(p.Index, Is.EqualTo(-1));
            Assert.That(p.IsPlaying, Is.False);
            Assert.That(p.Speed, Is.EqualTo(1));
        }

        [Test]
        public void 정지_중에는_시간이_흘러도_진행하지_않음()
        {
            var p = Create();
            p.Tick(100);
            Assert.That(p.Index, Is.EqualTo(-1));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void 배속에_따라_투구_간격이_줄어든다(int speed)
        {
            var p = Create();
            p.SetSpeed(speed);
            p.Play();
            float interval = PitchPlayback.PitchIntervalSec / speed;
            p.Tick(interval - 0.01f);
            Assert.That(p.Index, Is.EqualTo(-1));
            p.Tick(0.02f);
            Assert.That(p.Index, Is.EqualTo(0));
        }

        [Test]
        public void 타석_종료_후에는_더_오래_머문다()
        {
            var p = Create();
            p.SeekTo(FirstPaEnd);
            p.Play();
            p.Tick(PitchPlayback.PitchIntervalSec + 0.1f);
            Assert.That(p.Index, Is.EqualTo(FirstPaEnd));
            p.Tick(PitchPlayback.PlateAppearanceHoldSec);
            Assert.That(p.Index, Is.EqualTo(FirstPaEnd + 1));
        }

        [Test]
        public void 끝까지_재생하면_자동으로_멈춘다()
        {
            var p = Create();
            int stateChanges = 0;
            p.StateChanged += () => stateChanges++;
            p.SetSpeed(4);
            p.Play();
            p.Tick(1000);
            Assert.That(p.Index, Is.EqualTo(LastIndex));
            Assert.That(p.IsPlaying, Is.False);
            Assert.That(p.IsAtEnd, Is.True);
            Assert.That(stateChanges, Is.EqualTo(3)); // 배속, 재생, 자동 정지

            p.Play();
            Assert.That(p.IsPlaying, Is.False, "끝에서는 재생을 시작하지 않는다");
        }

        [Test]
        public void 한_번에_큰_시간이_흘러도_투구를_건너뛰지_않고_순서대로_알린다()
        {
            var p = Create();
            var seen = new List<int>();
            p.IndexChanged += seen.Add;
            p.Play();
            p.Tick(1000);
            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
        }

        [Test]
        public void 다음_타석은_타석이_끝나는_투구로_이동()
        {
            var p = Create();
            p.SkipToPlateAppearanceEnd();
            Assert.That(p.Index, Is.EqualTo(FirstPaEnd));
            p.SkipToPlateAppearanceEnd();
            Assert.That(p.Index, Is.EqualTo(LastIndex));
            p.SkipToPlateAppearanceEnd();
            Assert.That(p.Index, Is.EqualTo(LastIndex));
        }

        [Test]
        public void 타석_도중에서_다음_타석은_그_타석의_끝으로()
        {
            var p = Create();
            p.SeekTo(1);
            p.SkipToPlateAppearanceEnd();
            Assert.That(p.Index, Is.EqualTo(FirstPaEnd));
        }

        [Test]
        public void 이닝_종료와_경기_종료는_장면_마지막_투구로()
        {
            var p = Create();
            p.SkipToHalfInningEnd();
            Assert.That(p.Index, Is.EqualTo(LastIndex));

            p = Create();
            p.SkipToEnd();
            Assert.That(p.Index, Is.EqualTo(LastIndex));
        }

        [Test]
        public void 다음_투구는_한_칸씩()
        {
            var p = Create();
            p.StepPitch();
            p.StepPitch();
            Assert.That(p.Index, Is.EqualTo(1));
        }

        [Test]
        public void 처음으로는_첫_투구_전_정지_상태()
        {
            var p = Create();
            p.Play();
            p.Tick(5);
            p.Restart();
            Assert.That(p.Index, Is.EqualTo(-1));
            Assert.That(p.IsPlaying, Is.False);
        }

        [Test]
        public void 수동_이동_후_대기_시간은_처음부터_다시_센다()
        {
            var p = Create();
            p.Play();
            p.Tick(PitchPlayback.PitchIntervalSec - 0.1f);
            p.StepPitch(); // -1 → 0
            p.Tick(0.2f);
            Assert.That(p.Index, Is.EqualTo(0));
        }

        [Test]
        public void 지원하지_않는_배속은_거부()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create().SetSpeed(3));
        }

        [Test]
        public void 판정_점등_시간은_x1_기준_범위_안이고_배속에서_줄거나_생략된다()
        {
            float x1 = GameScreenController.FlashSecondsFor(1);
            float x2 = GameScreenController.FlashSecondsFor(2);
            float x4 = GameScreenController.FlashSecondsFor(4);
            Assert.That(x1, Is.InRange(0.25f, 0.45f));
            Assert.That(x2, Is.LessThan(x1).And.GreaterThan(0f));
            Assert.That(x4, Is.EqualTo(0f));
        }
    }
}
