using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 23~24: 샘플 선수 DB에 리그 백분위 태그(상위 20%)를 매겼을 때 두 구단의 케미스트리와,
    /// 로비 라인업 교체(<see cref="LineupCard"/>)에 따라 케미스트리가 켜지고 꺼지는지.
    /// </summary>
    public class ChemistryDatabaseTests
    {
        static readonly MasterDatabase Db = TestLeague.TwoTeams;
        static readonly System.Collections.Generic.IReadOnlyDictionary<string, ChemistryTag> Tags = ChemistryTagger.Tag(Db.Players);
        static TeamRoster Busan => Db.GetRoster("삼성 라이온즈");
        static TeamRoster Seoul => Db.GetRoster("LG 트윈스");

        static ActiveChemistry Evaluate(LineupCard card) => ChemistryRules.Evaluate(card.ApplyTo(Busan), Tags);

        [Test]
        public void 샘플_DB_태그는_백분위대로_나뉜다()
        {
            // 타자 21명(100타석 이상) → 성향별 상위 ceil(4.2) = 5등, 투수 14명(30이닝 이상) → Ace ceil(2.8) = 3명
            Assert.That(Tags.Count(kv => kv.Value == ChemistryTag.Ace), Is.EqualTo(3));
            Assert.That(Tags.Count(kv => kv.Value != ChemistryTag.Ace), Is.EqualTo(14), "30%일 때 19명 → 20%에서 14명");
            Assert.That(Tags.Keys.Select(Db.FindPlayer).All(p => p != null), Is.True);
            Assert.That(Tags.Where(kv => kv.Value != ChemistryTag.Ace).Select(kv => Db.FindPlayer(kv.Key)).All(p => !p.IsPitcher), Is.True);
            Assert.That(new[] { "WV001", "WV002", "WV008", "WV012" }.All(id => Tags[id] == ChemistryTag.Speed), Is.True);
            Assert.That(new[] { "WV004", "WV009", "WV010" }.All(id => Tags[id] == ChemistryTag.Power), Is.True);
            Assert.That(Tags.ContainsKey("WV003") || Tags.ContainsKey("WV005") || Tags.ContainsKey("WV011"), Is.False, "성지환·길태윤·대타 2번은 태그 없음");
        }

        [Test]
        public void 기본_라인업은_부산만_발야구이고_서울은_케미스트리가_없다()
        {
            var board = new ChemistryBoard(Tags, Busan, Seoul);
            var busan = board.ActiveFor("삼성 라이온즈");
            Assert.That(busan.Kinds, Is.EqualTo(new[] { ChemistryKind.SpeedBall }));
            Assert.That(busan.Members(ChemistryKind.SpeedBall), Is.EquivalentTo(new[] { "WV001", "WV002", "WV008" }));
            // 서울: Glove 2B·SS·DH → 센터라인은 2명, Power 4·5번 → 연속 2명
            Assert.That(board.ActiveFor("LG 트윈스").Kinds, Is.Empty);
        }

        [Test]
        public void Speed_멤버를_빼면_발야구가_꺼지고_다른_Speed를_넣으면_다시_켜진다()
        {
            var card = LineupCard.FromRoster(Busan);
            Assert.That(card.TrySwap(LineupSpot.Batting(8), LineupSpot.Bench(1)), Is.True); // 여민규(Speed) ↔ 대타 WV011
            Assert.That(Evaluate(card).Kinds, Is.Empty);
            Assert.That(card.TrySwap(LineupSpot.Batting(8), LineupSpot.Bench(2)), Is.True); // WV011 ↔ 대주자 국태민(Speed)
            var active = Evaluate(card);
            Assert.That(active.Kinds, Is.EqualTo(new[] { ChemistryKind.SpeedBall }));
            Assert.That(active.Members(ChemistryKind.SpeedBall), Is.EquivalentTo(new[] { "WV001", "WV002", "WV012" }));
        }

        [Test]
        public void 타순을_바꿔_Power_3명을_잇대면_거포_클린업이_켜진다()
        {
            var card = LineupCard.FromRoster(Busan);
            Assert.That(card.TrySwap(LineupSpot.Batting(5), LineupSpot.Batting(9)), Is.True); // 표준혁(4번)·탁현서(9 → 5번)
            Assert.That(Evaluate(card).Has(ChemistryKind.PowerCleanup), Is.False, "4·5번 2명");
            Assert.That(card.TrySwap(LineupSpot.Batting(3), LineupSpot.Bench(0)), Is.True); // 대타 소윤호(Power) → 3번
            var active = Evaluate(card);
            Assert.That(active.Kinds, Is.EqualTo(new[] { ChemistryKind.SpeedBall, ChemistryKind.PowerCleanup }));
            Assert.That(active.Members(ChemistryKind.PowerCleanup), Is.EquivalentTo(new[] { "WV010", "WV004", "WV009" }));
            Assert.That(ChemistryText.Summary(active), Is.EqualTo("활성화됨: 발야구 · 거포 클린업"));
        }

        [Test]
        public void 로비_케미스트리_문구와_배지()
        {
            Assert.That(ChemistryText.Summary(null), Is.EqualTo("활성화된 케미스트리 없음"));
            var noSpeed = LineupCard.FromRoster(Busan);
            noSpeed.TrySwap(LineupSpot.Batting(1), LineupSpot.Bench(1)); // 하윤재(Speed) ↔ 대타 WV011
            var none = Evaluate(noSpeed);
            Assert.That(ChemistryText.Summary(none), Is.EqualTo("활성화된 케미스트리 없음"));
            Assert.That(ChemistryText.Key(none), Is.Empty);
            Assert.That(ChemistryText.Key(null), Is.Empty);

            var basic = Evaluate(LineupCard.FromRoster(Busan));
            Assert.That(ChemistryText.Key(basic), Is.EqualTo("SpeedBall:WV001,WV002,WV008"));
            var swapped = LineupCard.FromRoster(Busan);
            swapped.TrySwap(LineupSpot.Batting(1), LineupSpot.Batting(2)); // 멤버끼리 타순만 바꿈
            Assert.That(ChemistryText.Key(Evaluate(swapped)), Is.EqualTo(ChemistryText.Key(basic)), "결과가 같으면 열쇠도 같다");

            Assert.That(new[] { ChemistryTag.Speed, ChemistryTag.Glove, ChemistryTag.Power, ChemistryTag.Ace, ChemistryTag.None }.Select(ChemistryText.Badge),
                Is.EqualTo(new[] { "주", "수", "장", "", "" }));
            Assert.That(ChemistryText.BadgeClass(ChemistryTag.Glove), Is.EqualTo("chem-badge--glove"));
            Assert.That(ChemistryText.BadgeClass(ChemistryTag.None), Is.EqualTo("chem-badge--none"));
            Assert.That(ChemistryText.BadgeTooltip(ChemistryTag.Speed), Is.EqualTo("주루 · 도루 시도율 리그 상위 20%"));
            Assert.That(ChemistryText.BadgeTooltip(ChemistryTag.None), Is.Empty);
        }
    }
}
