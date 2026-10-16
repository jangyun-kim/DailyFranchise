using System.Linq;
using DailyFranchise.App.Data;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 23: 샘플 선수 DB에 리그 백분위 태그를 매겼을 때 두 구단의 기본 라인업 케미스트리.</summary>
    public class ChemistryDatabaseTests
    {
        static readonly MasterDatabase Db = MasterDatabase.LoadFromResources();

        [Test]
        public void 샘플_DB_태그는_백분위대로_나뉜다()
        {
            var tags = ChemistryTagger.Tag(Db.Players);
            // 타자 21명(100타석 이상) → 성향별 상위 ceil(6.3) = 7등, 투수 14명(30이닝 이상) → Ace ceil(2.8) = 3명
            Assert.That(tags.Count(kv => kv.Value == ChemistryTag.Ace), Is.EqualTo(3));
            Assert.That(tags.Keys.Select(Db.FindPlayer).All(p => p != null), Is.True);
            Assert.That(tags.Where(kv => kv.Value != ChemistryTag.Ace).Select(kv => Db.FindPlayer(kv.Key)).All(p => !p.IsPitcher), Is.True);
        }

        [Test]
        public void 부산은_발야구와_3_4_5번_거포_클린업_서울은_철벽_센터라인()
        {
            var tags = ChemistryTagger.Tag(Db.Players);
            var board = new ChemistryBoard(tags, Db.GetRoster("부산 웨이브스"), Db.GetRoster("서울 블루윙스"));

            var busan = board.ActiveFor("부산 웨이브스");
            Assert.That(busan.Kinds, Is.EqualTo(new[] { ChemistryKind.SpeedBall, ChemistryKind.PowerCleanup }));
            Assert.That(busan.Members(ChemistryKind.SpeedBall), Is.EquivalentTo(new[] { "WV001", "WV002", "WV006", "WV008" }));
            Assert.That(busan.Members(ChemistryKind.PowerCleanup), Is.EquivalentTo(new[] { "WV003", "WV004", "WV005" }), "9번 DH는 끊겨 빠진다");

            var seoul = board.ActiveFor("서울 블루윙스");
            Assert.That(seoul.Kinds, Is.EqualTo(new[] { ChemistryKind.CenterLineGlove }));
            Assert.That(seoul.Members(ChemistryKind.CenterLineGlove), Is.EquivalentTo(new[] { "BW002", "BW003", "BW008" }), "3B·DH Glove는 센터라인 밖");
        }
    }
}
