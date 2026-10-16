using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    /// <summary>Phase 23: 리그 백분위 성향 태그, 케미스트리 발동 조건, 케미스트리 보정(카운터·상한·툴팁).</summary>
    public class ChemistryTests
    {
        const double Eps = 1e-9;

        sealed class Lookup : IPlayerLookup
        {
            readonly Dictionary<string, PlayerData> _players;
            public Lookup(IEnumerable<PlayerData> players, IDictionary<string, string> aliases = null)
            {
                _players = players.ToDictionary(p => p.Id);
                foreach (var kv in aliases ?? new Dictionary<string, string>()) _players[kv.Key] = _players[kv.Value];
            }
            public PlayerData FindPlayer(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;
        }

        /// <summary>단타 50 + 볼넷 10 = 출루 기회 60, 타율 .250, 200타석.</summary>
        static PlayerData Bat(string id, int sb = 0, int cs = 0, double dwar = 0, double iso = 0, int pa = 200) =>
            new PlayerData
            {
                Id = id, Name = id, Role = RosterRole.Lineup, PlateAppearances = pa, Hits = 50, Walks = 10,
                StolenBases = sb, CaughtStealing = cs, DefensiveWar = dwar,
                BattingAverage = 0.250, OnBasePercentage = 0.320, RecordedSlugging = iso > 0 ? 0.250 + iso : 0,
            };

        static PlayerData Arm(string id, double fip, double ip = 50, string team = null) =>
            new PlayerData { Id = id, Name = id, Team = team, Role = RosterRole.Starter, InningsPitched = ip, Fip = fip, Era = 4.30 };

        static ChemistryTag TagOf(IReadOnlyDictionary<string, ChemistryTag> tags, string id) =>
            tags.TryGetValue(id, out var t) ? t : ChemistryTag.None;

        // ---------- 태그 ----------

        [Test]
        public void 상위_20퍼센트만_태그를_받는다()
        {
            // 10명 → ceil(2) = 2등까지. 도루 시도 10, 9, … 1 (전부 성공)
            var league = Enumerable.Range(0, 10).Select(i => Bat($"s{i}", sb: 10 - i)).ToList();
            var tags = ChemistryTagger.Tag(league);
            Assert.That(ChemistryTagger.TopPercent, Is.EqualTo(0.20));
            Assert.That(league.Select(p => TagOf(tags, p.Id)),
                Is.EqualTo(new[] { ChemistryTag.Speed, ChemistryTag.Speed }.Concat(Enumerable.Repeat(ChemistryTag.None, 8))));
        }

        [Test]
        public void 도루_성공률_65퍼센트_미만은_시도가_많아도_Speed가_아니고_자리를_물려주지_않는다()
        {
            var league = Enumerable.Range(0, 10).Select(i => Bat($"s{i}", sb: 10 - i)).ToList();
            league[1] = Bat("s1", sb: 2, cs: 7); // 시도 9 (2등), 성공률 22%
            var tags = ChemistryTagger.Tag(league);
            Assert.That(TagOf(tags, "s1"), Is.EqualTo(ChemistryTag.None));
            Assert.That(TagOf(tags, "s0"), Is.EqualTo(ChemistryTag.Speed));
            Assert.That(TagOf(tags, "s2"), Is.EqualTo(ChemistryTag.None), "3등은 여전히 범위 밖");
            Assert.That(ChemistryTagger.StealSuccess(Bat("x", sb: 13, cs: 7)), Is.EqualTo(0.65).Within(Eps));
        }

        [Test]
        public void 같은_점수는_같은_등수라_경계에서_함께_들어간다()
        {
            var sb = new[] { 10, 9, 9, 8, 5, 4, 3, 2, 1, 1 };
            var league = sb.Select((v, i) => Bat($"s{i}", sb: v)).ToList();
            var tags = ChemistryTagger.Tag(league);
            Assert.That(league.Count(p => TagOf(tags, p.Id) == ChemistryTag.Speed), Is.EqualTo(3), "2등 동점 두 명 모두");
        }

        [Test]
        public void 한_선수는_순위_비율이_가장_높은_태그_하나만_받는다()
        {
            var league = Enumerable.Range(0, 10).Select(i => Bat($"n{i}")).ToList();
            league[0] = Bat("x", sb: 20, iso: 0.250);  // 도루 1등, 장타 2등 → Speed
            league[1] = Bat("y", sb: 10, iso: 0.300);  // 도루 2등, 장타 1등 → Power
            league[2] = Bat("z", dwar: 1.0);           // 수비 1등
            league[3] = Bat("w", iso: 0.100);          // 장타 3등 → 상위 20%(2등) 밖
            var tags = ChemistryTagger.Tag(league);
            Assert.That(new[] { "x", "y", "z", "w" }.Select(id => TagOf(tags, id)),
                Is.EqualTo(new[] { ChemistryTag.Speed, ChemistryTag.Power, ChemistryTag.Glove, ChemistryTag.None }));
            Assert.That(TagOf(tags, "n4"), Is.EqualTo(ChemistryTag.None), "점수 0은 자격 없음");
        }

        [Test]
        public void 같은_순위_비율이면_Speed_Glove_Power_순으로_고른다()
        {
            var league = Enumerable.Range(0, 10).Select(i => Bat($"n{i}")).ToList();
            league[0] = Bat("x", sb: 10, dwar: 1.0, iso: 0.300);
            Assert.That(TagOf(ChemistryTagger.Tag(league), "x"), Is.EqualTo(ChemistryTag.Speed));
        }

        [Test]
        public void 타석이_적은_타자는_빠지고_투수는_FIP_상위_20퍼센트가_Ace()
        {
            var league = new List<PlayerData> { Bat("rookie", sb: 50, pa: 99), Bat("vet", sb: 5) };
            league.AddRange(new[] { 2.5, 3.0, 3.5, 4.0, 4.5 }.Select((fip, i) => Arm($"p{i}", fip)));
            league.Add(Arm("short", 1.0, ip: 29));
            var tags = ChemistryTagger.Tag(league);
            Assert.That(TagOf(tags, "rookie"), Is.EqualTo(ChemistryTag.None));
            Assert.That(TagOf(tags, "vet"), Is.EqualTo(ChemistryTag.Speed), "대상 1명 → 1등");
            Assert.That(tags.Where(kv => kv.Value == ChemistryTag.Ace).Select(kv => kv.Key), Is.EqualTo(new[] { "p0" }), "5명 → ceil(1)");
            Assert.That(ChemistryTagger.Tag(null), Is.Empty);
        }

        [Test]
        public void 점수_공식()
        {
            var p = new PlayerData { Hits = 100, Doubles = 20, Triples = 2, HomeRuns = 8, Walks = 25, HitByPitch = 5, StolenBases = 12, CaughtStealing = 3 };
            Assert.That(ChemistryTagger.SpeedScore(p), Is.EqualTo(15.0 / 100).Within(Eps), "단타 70 + 볼넷 25 + 사구 5");
            Assert.That(ChemistryTagger.GloveScore(new PlayerData { DefensiveWar = 0.5, PlateAppearances = 300 }), Is.EqualTo(1.0).Within(Eps));
            Assert.That(ChemistryTagger.PowerScore(new PlayerData { BattingAverage = 0.280, OnBasePercentage = 0.350, Ops = 0.850 }), Is.EqualTo(0.220).Within(Eps), "SLG = OPS − OBP");
            Assert.That(ChemistryTagger.PowerScore(new PlayerData { BattingAverage = 0.280 }), Is.Zero);
        }

        // ---------- 발동 ----------

        static PlayerData Slot(string id, string team, int order, string pos) =>
            new PlayerData { Id = id, Name = id, Team = team, Role = RosterRole.Lineup, Order = order, Position = pos, BattingAverage = 0.270, OnBasePercentage = 0.345 };

        static TeamRoster Roster(string team, IEnumerable<PlayerData> lineup, params PlayerData[] starters) =>
            new TeamRoster(team, lineup.ToList(), null, starters, null);

        static readonly string[] Positions = { "CF", "SS", "RF", "1B", "3B", "LF", "C", "2B", "DH" };

        static List<PlayerData> Lineup(string team) =>
            Enumerable.Range(1, 9).Select(o => Slot($"{team}{o}", team, o, Positions[o - 1])).ToList();

        [Test]
        public void 발야구는_Speed_3명이면_타순과_무관하게_발동한다()
        {
            var lineup = Lineup("A");
            var tags = new Dictionary<string, ChemistryTag> { ["A1"] = ChemistryTag.Speed, ["A6"] = ChemistryTag.Speed };
            Assert.That(ChemistryRules.Evaluate(Roster("A", lineup), tags).Kinds, Is.Empty, "2명");
            tags["A9"] = ChemistryTag.Speed;
            var active = ChemistryRules.Evaluate(Roster("A", lineup), tags);
            Assert.That(active.Kinds, Is.EqualTo(new[] { ChemistryKind.SpeedBall }));
            Assert.That(active.Members(ChemistryKind.SpeedBall), Is.EquivalentTo(new[] { "A1", "A6", "A9" }));
            Assert.That(active.Team, Is.EqualTo("A"));
        }

        [Test]
        public void 철벽_센터라인은_C_2B_SS_CF_수비_위치의_Glove만_센다()
        {
            var lineup = Lineup("A");
            // A1 CF, A2 SS, A6 LF(센터라인 아님)
            var tags = new Dictionary<string, ChemistryTag> { ["A1"] = ChemistryTag.Glove, ["A2"] = ChemistryTag.Glove, ["A6"] = ChemistryTag.Glove };
            Assert.That(ChemistryRules.Evaluate(Roster("A", lineup), tags).Has(ChemistryKind.CenterLineGlove), Is.False);
            tags["A7"] = ChemistryTag.Glove; // C
            var active = ChemistryRules.Evaluate(Roster("A", lineup), tags);
            Assert.That(active.Members(ChemistryKind.CenterLineGlove), Is.EquivalentTo(new[] { "A1", "A2", "A7" }));
        }

        [Test]
        public void 거포_클린업은_연속_타순_3명_이상이다()
        {
            var lineup = Lineup("A");
            var tags = new[] { "A3", "A4", "A6" }.ToDictionary(id => id, _ => ChemistryTag.Power);
            Assert.That(ChemistryRules.Evaluate(Roster("A", lineup), tags).Has(ChemistryKind.PowerCleanup), Is.False, "3·4·6번");

            tags = new[] { "A1", "A4", "A5", "A6", "A7" }.ToDictionary(id => id, _ => ChemistryTag.Power);
            Assert.That(ChemistryRules.Evaluate(Roster("A", lineup), tags).Members(ChemistryKind.PowerCleanup),
                Is.EquivalentTo(new[] { "A4", "A5", "A6", "A7" }), "끊긴 1번은 빠진다");

            // 타순은 Order 기준 (목록 순서가 섞여 있어도)
            var shuffled = new[] { lineup[4], lineup[2], lineup[3] }.Concat(lineup.Where(p => p.Order < 3 || p.Order > 5));
            tags = new[] { "A3", "A4", "A5" }.ToDictionary(id => id, _ => ChemistryTag.Power);
            Assert.That(ChemistryRules.Evaluate(Roster("A", shuffled), tags).Has(ChemistryKind.PowerCleanup), Is.True);
        }

        [Test]
        public void 선수단이_없으면_null이고_라벨은_한글()
        {
            Assert.That(ChemistryRules.Evaluate(null, null), Is.Null);
            Assert.That(Enum.GetValues(typeof(ChemistryKind)).Cast<ChemistryKind>().Select(ChemistryRules.Label),
                Is.EqualTo(new[] { "발야구", "철벽 센터라인", "거포 클린업" }));
            Assert.That(ChemistryRules.IsCenterLine("SS") && !ChemistryRules.IsCenterLine("LF") && !ChemistryRules.IsCenterLine(null), Is.True);
        }

        // ---------- 보정 ----------

        /// <summary>
        /// A: 1·2·6번 Speed, 3·4·5번 Power (발야구 + 거포 클린업), 투수 Apit.
        /// B: 7·8·2번(C·2B·SS) Glove (철벽 센터라인), 투수 Bpit(보통) · Bace(Ace).
        /// </summary>
        sealed class Fixture
        {
            public readonly List<PlayerData> Players = new List<PlayerData>();
            public readonly ChemistryBoard Board;
            public readonly MatchupCalculator Calc;

            public Fixture(bool bGlove = true, IDictionary<string, string> aliases = null)
            {
                var a = Lineup("A");
                var b = Lineup("B");
                var apit = Arm("Apit", 4.0, team: "A");
                var bpit = Arm("Bpit", 4.0, team: "B");
                var bace = Arm("Bace", 2.5, team: "B");
                Players.AddRange(a.Concat(b).Append(apit).Append(bpit).Append(bace));

                var tags = new Dictionary<string, ChemistryTag> { ["Bace"] = ChemistryTag.Ace };
                foreach (var id in new[] { "A1", "A2", "A6" }) tags[id] = ChemistryTag.Speed;
                foreach (var id in new[] { "A3", "A4", "A5" }) tags[id] = ChemistryTag.Power;
                if (bGlove) foreach (var id in new[] { "B2", "B7", "B8" }) tags[id] = ChemistryTag.Glove;

                Board = new ChemistryBoard(tags, Roster("A", a, apit), Roster("B", b, bpit));
                var lookup = new Lookup(Players, aliases);
                Calc = new MatchupCalculator(lookup, new IMatchupModifier[] { new ChemistryModifier(Board, lookup) });
            }

            public double Raw(string batter, string pitcher) =>
                MatchupCalculator.Combine(Players.First(p => p.Id == batter), Players.First(p => p.Id == pitcher));
        }

        static MatchupSituation On(string first = null, string second = null, string third = null) =>
            new MatchupSituation(0, new BaseState(first, second, third));

        [Test]
        public void 보드는_두_구단_케미스트리와_태그를_안다()
        {
            var f = new Fixture();
            Assert.That(f.Board.ActiveFor("A").Kinds, Is.EqualTo(new[] { ChemistryKind.SpeedBall, ChemistryKind.PowerCleanup }));
            Assert.That(f.Board.ActiveFor("B").Kinds, Is.EqualTo(new[] { ChemistryKind.CenterLineGlove }));
            Assert.That(f.Board.ActiveFor("C"), Is.Null);
            Assert.That(f.Board.ActiveFor(null), Is.Null);
            Assert.That(f.Board.TagOf("Bace"), Is.EqualTo(ChemistryTag.Ace));
            Assert.That(f.Board.TagOf("nobody"), Is.EqualTo(ChemistryTag.None));
        }

        [Test]
        public void 철벽_센터라인은_상대_타자를_3퍼센트_막고_거포_멤버에겐_절반만()
        {
            var f = new Fixture();
            Assert.That(f.Calc.HitProbability("A7", "Bpit", On()), Is.EqualTo(f.Raw("A7", "Bpit") * 0.97).Within(Eps));
            Assert.That(f.Calc.HitProbability("A3", "Bpit", On()), Is.EqualTo(f.Raw("A3", "Bpit") * 0.985).Within(Eps), "주자 없음 → 거포 보너스 없음");
            Assert.That(f.Calc.HitProbability("B1", "Apit", On()), Is.EqualTo(f.Raw("B1", "Apit")).Within(Eps), "A는 수비 케미 없음");
        }

        [Test]
        public void 발야구는_Speed_주자가_1루나_2루에_있을_때만이고_철벽_상대면_절반()
        {
            var f = new Fixture(bGlove: false);
            double raw = f.Raw("A7", "Bpit");
            Assert.That(f.Calc.HitProbability("A7", "Bpit", On(first: "A1")), Is.EqualTo(raw * 1.04).Within(Eps));
            Assert.That(f.Calc.HitProbability("A7", "Bpit", On(second: "A2")), Is.EqualTo(raw * 1.04).Within(Eps));
            Assert.That(f.Calc.HitProbability("A7", "Bpit", On(third: "A1")), Is.EqualTo(raw).Within(Eps), "3루 주자는 도루 위협 아님");
            Assert.That(f.Calc.HitProbability("A7", "Bpit", On(first: "A8")), Is.EqualTo(raw).Within(Eps), "멤버가 아닌 주자");

            var g = new Fixture();
            Assert.That(g.Calc.HitProbability("A7", "Bpit", On(first: "A1")), Is.EqualTo(raw * (1 + 0.02 - 0.03)).Within(Eps));
        }

        [Test]
        public void 거포_클린업은_주자_있을_때_멤버_타석에만이고_Ace_상대면_절반()
        {
            var f = new Fixture(bGlove: false);
            Assert.That(f.Calc.HitProbability("A4", "Bpit", On(third: "A7")), Is.EqualTo(f.Raw("A4", "Bpit") * 1.04).Within(Eps));
            Assert.That(f.Calc.HitProbability("A4", "Bace", On(third: "A7")), Is.EqualTo(f.Raw("A4", "Bace") * 1.02).Within(Eps));
            Assert.That(f.Calc.HitProbability("A7", "Bpit", On(third: "A3")), Is.EqualTo(f.Raw("A7", "Bpit")).Within(Eps), "멤버 아닌 타자");
        }

        [Test]
        public void 세_케미스트리는_합산하고_합계는_6퍼센트로_자른다()
        {
            // 발야구 +4 + 거포 +4 = +8 → +6
            var f = new Fixture(bGlove: false);
            Assert.That(f.Calc.HitProbability("A4", "Bpit", On(first: "A1")), Is.EqualTo(f.Raw("A4", "Bpit") * 1.06).Within(Eps));
            // 철벽 상대: 발야구 +2, 철벽 −1.5 (거포 멤버), 거포 +4 → +4.5
            var g = new Fixture();
            Assert.That(g.Calc.HitProbability("A4", "Bpit", On(first: "A1")), Is.EqualTo(g.Raw("A4", "Bpit") * 1.045).Within(Eps));
            // Ace까지: +2 − 1.5 + 2 = +2.5
            Assert.That(g.Calc.HitProbability("A4", "Bace", On(first: "A1")), Is.EqualTo(g.Raw("A4", "Bace") * 1.025).Within(Eps));
        }

        [Test]
        public void 장면_주자_ID는_선수_DB_ID로_바꿔_본다()
        {
            var f = new Fixture(bGlove: false, aliases: new Dictionary<string, string> { ["home_b1"] = "A1" });
            Assert.That(f.Calc.HitProbability("A7", "Bpit", On(first: "home_b1")), Is.EqualTo(f.Raw("A7", "Bpit") * 1.04).Within(Eps));
        }

        [Test]
        public void 소속_없는_리그_평균_선수는_보정이_없다()
        {
            var f = new Fixture();
            double plain = new MatchupCalculator(new Lookup(f.Players), Array.Empty<IMatchupModifier>()).HitProbability("A7", "unknown", On(first: "A1"));
            Assert.That(f.Calc.HitProbability("A7", "unknown", On(first: "A1")), Is.EqualTo(plain).Within(Eps));
            Assert.That(new ChemistryModifier(f.Board).RelativeBonus(null), Is.Zero);
            Assert.Throws<ArgumentNullException>(() => new ChemistryModifier(null));
        }

        [Test]
        public void 툴팁은_적용된_케미스트리를_보인다()
        {
            var f = new Fixture();
            var context = f.Calc.ContextFor("A4", "Bpit", On(first: "A1"));
            var adj = f.Calc.Adjustments(context);
            Assert.That(adj.Select(a => a.Label), Is.EqualTo(new[] { "케미 발야구·철벽 센터라인·거포 클린업" }));
            Assert.That(adj[0].RelativeChange, Is.EqualTo(0.045).Within(1e-6));
            Assert.That(f.Calc.Adjustments(f.Calc.ContextFor("B1", "Apit", On())), Is.Empty, "적용 없음 → 내역 없음");
        }

        [Test]
        public void 기본_보정_목록에는_케미스트리가_없다()
        {
            Assert.That(MatchupCalculator.DefaultModifiers().OfType<ChemistryModifier>(), Is.Empty);
        }
    }
}
