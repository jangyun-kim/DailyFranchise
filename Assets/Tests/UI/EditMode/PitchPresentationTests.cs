using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    public class PitchPresentationTests
    {
        static PitchEvent Pitch(PitchResult r, PlateAppearanceOutcome o = PlateAppearanceOutcome.None, bool inZone = true) =>
            new PitchEvent { Result = r, Outcome = o, IsStrikeByRule = inZone };

        [TestCase(PitchResult.Ball, "BALL", "res--ball")]
        [TestCase(PitchResult.CalledStrike, "STRIKE", "res--strike")]
        [TestCase(PitchResult.SwingingStrike, "STRIKE", "res--strike")]
        [TestCase(PitchResult.Foul, "FOUL", "res--foul")]
        [TestCase(PitchResult.InPlay, "IN PLAY", "res--inplay")]
        public void 타석_진행중_결과_문구와_클래스(PitchResult result, string text, string cls)
        {
            var outcome = result == PitchResult.InPlay ? PlateAppearanceOutcome.InPlay : PlateAppearanceOutcome.None;
            var e = Pitch(result, outcome);
            Assert.That(PitchPresentation.ResultText(e), Is.EqualTo(text));
            Assert.That(PitchPresentation.ResultClass(e), Is.EqualTo(cls));
        }

        [TestCase(PitchResult.CalledStrike)]
        [TestCase(PitchResult.SwingingStrike)]
        public void 삼진은_결과보다_타석_종료가_우선(PitchResult result)
        {
            var e = Pitch(result, PlateAppearanceOutcome.Strikeout);
            Assert.That(PitchPresentation.ResultText(e), Is.EqualTo("STRIKE OUT"));
            Assert.That(PitchPresentation.ResultClass(e), Is.EqualTo("res--strikeout"));
        }

        [Test]
        public void 볼넷은_WALK()
        {
            var e = Pitch(PitchResult.Ball, PlateAppearanceOutcome.Walk, inZone: false);
            Assert.That(PitchPresentation.ResultText(e), Is.EqualTo("WALK"));
            Assert.That(PitchPresentation.ResultClass(e), Is.EqualTo("res--walk"));
        }

        [Test]
        public void 모든_결과_클래스가_목록에_있다()
        {
            foreach (PitchResult r in System.Enum.GetValues(typeof(PitchResult)))
            foreach (PlateAppearanceOutcome o in System.Enum.GetValues(typeof(PlateAppearanceOutcome)))
                Assert.That(PitchPresentation.AllResultClasses, Does.Contain(PitchPresentation.ResultClass(Pitch(r, o))));
        }

        [Test]
        public void 스윙_보조표기()
        {
            Assert.That(PitchPresentation.SwingNote(Pitch(PitchResult.CalledStrike)), Is.EqualTo("루킹"));
            Assert.That(PitchPresentation.SwingNote(Pitch(PitchResult.SwingingStrike)), Is.EqualTo("헛스윙"));
            Assert.That(PitchPresentation.SwingNote(Pitch(PitchResult.Ball)), Is.Null);
            Assert.That(PitchPresentation.SwingNote(Pitch(PitchResult.Foul)), Is.Null);
        }

        [Test]
        public void 존_표기는_IsStrikeByRule을_그대로_따른다()
        {
            Assert.That(PitchPresentation.ZoneNote(Pitch(PitchResult.SwingingStrike, inZone: false)), Is.EqualTo("존 밖"));
            Assert.That(PitchPresentation.ZoneNote(Pitch(PitchResult.Ball, inZone: true)), Is.EqualTo("존 안"));
        }

        [Test]
        public void 아웃_표기()
        {
            Assert.That(PitchPresentation.OutsText(0), Is.EqualTo("무사"));
            Assert.That(PitchPresentation.OutsText(2), Is.EqualTo("2사"));
        }

        [Test]
        public void 고정_8구의_결과_문구()
        {
            var texts = Fixture8thBottom.Create().Events.Select(PitchPresentation.ResultText);
            Assert.That(texts, Is.EqualTo(new[]
            {
                "STRIKE", "BALL", "STRIKE", "FOUL", "IN PLAY", "STRIKE", "STRIKE", "STRIKE OUT",
            }));
        }
    }
}
