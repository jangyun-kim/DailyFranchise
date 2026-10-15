using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    /// <summary>
    /// Phase 17 확률 엔진: 플래툰·득점권 클러치 보정, 보정 내역, 안타 종류 분배(HitDistributor)와
    /// Play-by-Play 난수 소비 수 보존. 기대값은 문서화된 공식을 손으로 풀어 쓴다.
    /// </summary>
    public class SabermetricsTests
    {
        const double Eps = 1e-9;

        sealed class Lookup : IPlayerLookup
        {
            readonly Dictionary<string, PlayerData> _players;
            public Lookup(params PlayerData[] players) => _players = players.ToDictionary(p => p.Id);
            public PlayerData FindPlayer(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;
        }

        static readonly MatchupSituation Empty = new MatchupSituation(0, BaseState.Empty);
        static readonly MatchupSituation Corners = new MatchupSituation(1, new BaseState("r1", null, "r3"));

        static PlayerData Batter(string id, Hand bats, double avg = 0.300, double obp = 0.370, double ops = 0, int hr = 0, double risp = 0) =>
            new PlayerData
            {
                Id = id, Name = id, Role = RosterRole.Lineup, Bats = bats,
                BattingAverage = avg, OnBasePercentage = obp, Ops = ops, HomeRuns = hr, ScoringPositionAverage = risp,
            };

        static PlayerData Pitcher(string id, Hand throws, double era = 4.30, double baa = 0.270, double hr9 = 0) =>
            new PlayerData { Id = id, Name = id, Role = RosterRole.Starter, Throws = throws, Era = era, OpponentAverage = baa, HomeRunsPer9 = hr9 };

        static MatchupContext Ctx(PlayerData b, PlayerData p, MatchupSituation s) =>
            new MatchupContext { Batter = b, Pitcher = p, Situation = s };

        // ───────── 플래툰 ─────────

        [TestCase(Hand.Left, Hand.Left, -0.05, "플래툰 열세(좌투 vs 좌타)")]
        [TestCase(Hand.Right, Hand.Right, -0.03, "플래툰 열세(우투 vs 우타)")]
        [TestCase(Hand.Right, Hand.Left, 0.03, "플래툰 우위(우투 vs 좌타)")]
        [TestCase(Hand.Left, Hand.Right, 0.03, "플래툰 우위(좌투 vs 우타)")]
        [TestCase(Hand.Left, Hand.Switch, 0.03, "플래툰 우위(좌투 vs 양타)")]
        [TestCase(Hand.Right, Hand.Switch, 0.03, "플래툰 우위(우투 vs 양타)")]
        public void 플래툰은_투구_타석_방향_조합으로_보정한다(Hand throws, Hand bats, double expected, string label)
        {
            var b = Batter("b", bats);
            var p = Pitcher("p", throws);
            var platoon = new PlatoonModifier();
            Assert.That(platoon.RelativeBonus(Ctx(b, p, Empty)), Is.EqualTo(expected));
            Assert.That(platoon.Label(Ctx(b, p, Empty)), Is.EqualTo(label));

            var calc = new MatchupCalculator(new Lookup(b, p), new IMatchupModifier[] { platoon });
            double raw = MatchupCalculator.Combine(b, p);
            Assert.That(calc.HitProbability("b", "p", Empty), Is.EqualTo(raw * (1 + expected)).Within(Eps));
        }

        [Test]
        public void 좌투_좌타는_안타_확률과_강공_성공률을_정확히_깎는다()
        {
            var b = Batter("b", Hand.Left);
            var p = Pitcher("p", Hand.Left);
            var calc = new MatchupCalculator(new Lookup(b, p), new IMatchupModifier[] { new PlatoonModifier() });
            var flat = new MatchupCalculator(new Lookup(b, p), Array.Empty<IMatchupModifier>());

            Assert.That(calc.HitProbability("b", "p", Empty) / flat.HitProbability("b", "p", Empty) - 1, Is.EqualTo(-0.05).Within(Eps));
            double ratio = calc.HitProbability("b", "p", Empty) / MatchupCalculator.NeutralHitProbability;
            Assert.That(calc.TacticSuccessRate(TacticKind.SwingAway, 0.5, "b", "p", Empty), Is.EqualTo(0.5 * ratio).Within(Eps));
            Assert.That(calc.TacticSuccessRate(TacticKind.SwingAway, 0.5, "b", "p", Empty),
                Is.LessThan(flat.TacticSuccessRate(TacticKind.SwingAway, 0.5, "b", "p", Empty)));
        }

        [Test]
        public void 데이터가_없는_리그_평균_선수에게는_플래툰을_적용하지_않는다()
        {
            var platoon = new PlatoonModifier();
            var lefty = Batter("b", Hand.Left);
            Assert.That(platoon.RelativeBonus(Ctx(lefty, MatchupCalculator.AveragePitcher, Empty)), Is.Zero);
            Assert.That(platoon.Label(Ctx(MatchupCalculator.AverageBatter, Pitcher("p", Hand.Left), Empty)), Is.Null);
            Assert.That(MatchupCalculator.IsLeagueAverage(MatchupCalculator.AverageBatter), Is.True);
            Assert.That(MatchupCalculator.IsLeagueAverage(lefty), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlatoonModifier(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlatoonModifier(0.03, -1.0));
        }

        // ───────── 득점권 클러치 ─────────

        [Test]
        public void 득점권_클러치는_득점권_타율_대비_시즌_타율로_선수마다_다르다()
        {
            var p = Pitcher("p", Hand.Right);
            var clutch = Batter("clutch", Hand.Right, avg: 0.300, risp: 0.330);
            var choke = Batter("choke", Hand.Right, avg: 0.300, risp: 0.270);
            Assert.That(ClutchModifier.RelativeBonus(Ctx(clutch, p, Corners)), Is.EqualTo(0.330 / 0.300 - 1).Within(Eps), "+10%");
            Assert.That(ClutchModifier.RelativeBonus(Ctx(choke, p, Corners)), Is.EqualTo(0.270 / 0.300 - 1).Within(Eps), "-10%");
            Assert.That(ClutchModifier.RelativeBonus(Ctx(clutch, p, Empty)), Is.Zero, "득점권이 아니면 보정 없음");
            Assert.That(new ClutchModifier().Label(Ctx(clutch, p, Corners)), Is.EqualTo("득점권 클러치"));
            Assert.That(new ClutchModifier().Label(Ctx(clutch, p, Empty)), Is.Null);
        }

        [Test]
        public void 득점권_기록이_없거나_비정상이면_리그_기본_5퍼센트이고_극단값은_자른다()
        {
            var p = Pitcher("p", Hand.Right);
            Assert.That(ClutchModifier.RelativeBonus(Ctx(Batter("none", Hand.Right), p, Corners)), Is.EqualTo(ClutchModifier.DefaultBonus));
            Assert.That(ClutchModifier.RelativeBonus(Ctx(MatchupCalculator.AverageBatter, p, Corners)), Is.EqualTo(0.05));
            Assert.That(ClutchModifier.RelativeBonus(Ctx(Batter("nan", Hand.Right, risp: double.NaN), p, Corners)), Is.EqualTo(0.05));
            Assert.That(ClutchModifier.RelativeBonus(Ctx(Batter("zeroavg", Hand.Right, avg: 0, risp: 0.300), p, Corners)), Is.EqualTo(0.05), "타율 0이면 나눗셈 안 함");
            Assert.That(ClutchModifier.RelativeBonus(Ctx(Batter("hot", Hand.Right, avg: 0.200, risp: 0.500), p, Corners)), Is.EqualTo(ClutchModifier.MaxBonus));
            Assert.That(ClutchModifier.RelativeBonus(Ctx(Batter("cold", Hand.Right, avg: 0.300, risp: 0.100), p, Corners)), Is.EqualTo(ClutchModifier.MinBonus));
            Assert.That(new ClutchModifier().Label(Ctx(Batter("none", Hand.Right), p, Corners)), Is.EqualTo("득점권"));
        }

        // ───────── 보정 내역 ─────────

        [Test]
        public void 보정_내역은_적용_순서대로_직전_직후_확률과_퍼센트를_담는다()
        {
            var b = Batter("b", Hand.Left, avg: 0.318, obp: 0.391, risp: 0.341);
            var p = Pitcher("p", Hand.Left, era: 3.21, baa: 0.238);
            var calc = new MatchupCalculator(new Lookup(b, p));
            var ctx = calc.ContextFor("b", "p", Corners);

            var list = calc.Adjustments(ctx);
            double raw = MatchupCalculator.Combine(b, p);
            Assert.That(list.Select(a => a.Label), Is.EqualTo(new[] { "플래툰 열세(좌투 vs 좌타)", "득점권 클러치" }));
            Assert.That(list[0].Before, Is.EqualTo(raw).Within(Eps));
            Assert.That(list[0].After, Is.EqualTo(raw * 0.95).Within(Eps));
            Assert.That(list[1].Before, Is.EqualTo(list[0].After));
            Assert.That(list[1].After, Is.EqualTo(calc.HitProbability(ctx)).Within(Eps), "마지막 직후 = 최종 확률");
            Assert.That(list.Select(a => a.Text), Is.EqualTo(new[] { "플래툰 열세(좌투 vs 좌타) -5%", "득점권 클러치 +7%" }));

            Assert.That(calc.Adjustments(calc.ContextFor("b", "p", Empty)).Select(a => a.Text), Is.EqualTo(new[] { "플래툰 열세(좌투 vs 좌타) -5%" }));
            Assert.That(calc.Adjustments(null), Is.Empty);
        }

        [Test]
        public void 효과가_0인_보정과_설명_없는_보정은_내역에_나오지_않는다()
        {
            var b = Batter("b", Hand.Right, avg: 0.300, risp: 0.300); // 클러치 0%
            var p = Pitcher("p", Hand.Left);                         // 좌투 vs 우타 +3%
            var calc = new MatchupCalculator(new Lookup(b, p),
                new IMatchupModifier[] { new PlatoonModifier(), new ClutchModifier(), new ScoringPositionModifier() });
            var list = calc.Adjustments(calc.ContextFor("b", "p", Corners));
            Assert.That(list.Select(a => a.Text), Is.EqualTo(new[] { "플래툰 우위(좌투 vs 우타) +3%" }), "클러치 0%·이름 없는 일괄 보정은 숨김");
            Assert.That(new MatchupAdjustment("x", 0, 0.1).RelativeChange, Is.Zero, "직전 0이면 0");
        }

        // ───────── 안타 종류 분배 ─────────

        static readonly PlayerData NoDataPitcher = MatchupCalculator.AveragePitcher;

        [Test]
        public void 두_선수_모두_데이터가_없으면_이전_고정_분포와_같다()
        {
            var mix = HitDistributor.Distribute(MatchupCalculator.AverageBatter, NoDataPitcher, HitDistributor.LeagueBaseline);
            Assert.That(mix, Is.EqualTo(HitDistributor.LeagueBaseline));
            var table = PlayByPlayGenerator.HitTable(mix, PlayByPlayGenerator.WalkShare);
            var legacy = new[] { 0.20, 0.55, 0.15, 0.02, 0.08 };
            for (int i = 0; i < legacy.Length; i++) Assert.That(table[i].weight, Is.EqualTo(legacy[i]).Within(1e-12), table[i].outcome.ToString());
            Assert.That(HitDistributor.Distribute(null, null, HitDistributor.PowerBaseline), Is.EqualTo(HitDistributor.PowerBaseline));
        }

        [Test]
        public void 장타율이_높은_타자는_교본_타자보다_홈런_장타_가중치가_높다()
        {
            // 교본: 리그 평균 수준 (.270 / OBP .345 / SLG .415 → ISO .145, 12홈런)
            var textbook = Batter("textbook", Hand.Right, avg: 0.270, obp: 0.345, ops: 0.760, hr: 12);
            var slugger = Batter("slugger", Hand.Right, avg: 0.270, obp: 0.360, ops: 0.960, hr: 35); // SLG .600
            var slap = Batter("slap", Hand.Right, avg: 0.300, obp: 0.360, ops: 0.700, hr: 1);         // SLG .340

            var t = HitDistributor.Distribute(textbook, NoDataPitcher, HitDistributor.LeagueBaseline);
            var s = HitDistributor.Distribute(slugger, NoDataPitcher, HitDistributor.LeagueBaseline);
            var c = HitDistributor.Distribute(slap, NoDataPitcher, HitDistributor.LeagueBaseline);

            Assert.That(s.HomeRun, Is.GreaterThan(t.HomeRun));
            Assert.That(t.HomeRun, Is.GreaterThan(c.HomeRun));
            Assert.That(s.ExtraBase, Is.GreaterThan(t.ExtraBase));
            Assert.That(c.Single, Is.GreaterThan(t.Single));
            // 교본 타자는 리그 기본 분포와 거의 같다.
            Assert.That(t.HomeRun, Is.EqualTo(HitDistributor.LeagueBaseline.HomeRun).Within(0.005));
            Assert.That(t.Single, Is.EqualTo(HitDistributor.LeagueBaseline.Single).Within(0.005));
        }

        [Test]
        public void 분배는_타자의_순장타율을_재현한다()
        {
            var b = Batter("b", Hand.Right, avg: 0.290, obp: 0.370, ops: 0.870, hr: 12); // SLG .500, ISO .210
            var mix = HitDistributor.Distribute(b, NoDataPitcher, HitDistributor.LeagueBaseline);
            Assert.That(0.290 * mix.ExtraBasesPerHit, Is.EqualTo(0.210).Within(1e-9), "타율 × 안타당 추가 루타 = ISO");
            Assert.That(HitDistributor.Iso(b), Is.EqualTo(0.210).Within(1e-12));
        }

        [Test]
        public void 피홈런이_많은_투수를_만나면_홈런_가중치가_오른다()
        {
            var b = Batter("b", Hand.Right, avg: 0.280, obp: 0.350, ops: 0.800, hr: 15);
            var stingy = HitDistributor.Distribute(b, Pitcher("stingy", Hand.Right, hr9: 0.45), HitDistributor.LeagueBaseline);
            var avg = HitDistributor.Distribute(b, Pitcher("avg", Hand.Right, hr9: 0.85), HitDistributor.LeagueBaseline);
            var gopher = HitDistributor.Distribute(b, Pitcher("gopher", Hand.Right, hr9: 1.60), HitDistributor.LeagueBaseline);
            Assert.That(stingy.HomeRun, Is.LessThan(avg.HomeRun));
            Assert.That(avg.HomeRun, Is.LessThan(gopher.HomeRun));
            Assert.That(HitDistributor.PitcherHomeRunTilt(Pitcher("x", Hand.Right, hr9: 0.85)), Is.EqualTo(1).Within(Eps));
            Assert.That(HitDistributor.PitcherHomeRunTilt(Pitcher("x", Hand.Right, hr9: 9.0)), Is.EqualTo(HitDistributor.MaxPitcherHomeRunTilt));
        }

        [Test]
        public void 강공_분포는_같은_타자라도_기본_진행보다_장타가_많다()
        {
            var b = Batter("b", Hand.Right, avg: 0.280, obp: 0.350, ops: 0.800, hr: 15);
            var normal = HitDistributor.Distribute(b, NoDataPitcher, HitDistributor.LeagueBaseline);
            var power = HitDistributor.Distribute(b, NoDataPitcher, HitDistributor.PowerBaseline);
            Assert.That(power.HomeRun, Is.GreaterThan(normal.HomeRun));
            Assert.That(power.ExtraBase, Is.GreaterThan(normal.ExtraBase));
        }

        static IEnumerable<TestCaseData> ExtremeBatters()
        {
            yield return new TestCaseData(Batter("hr0", Hand.Right, avg: 0.280, obp: 0.330, ops: 0.680, hr: 0)).SetName("홈런_0개");
            yield return new TestCaseData(Batter("nan", Hand.Right, avg: double.NaN, obp: double.NaN, ops: double.NaN, hr: 10)).SetName("NaN_기록");
            yield return new TestCaseData(Batter("inf", Hand.Right, avg: 0.3, obp: 0.3, ops: double.PositiveInfinity, hr: 10)).SetName("무한_OPS");
            yield return new TestCaseData(Batter("neg", Hand.Right, avg: -0.1, obp: -0.2, ops: -1, hr: -5)).SetName("음수_기록");
            yield return new TestCaseData(Batter("zero", Hand.Right, avg: 0, obp: 0, ops: 0, hr: 0)).SetName("기록_전무");
            yield return new TestCaseData(Batter("monster", Hand.Right, avg: 0.150, obp: 0.200, ops: 1.900, hr: 80)).SetName("극단_장타");
            yield return new TestCaseData(Batter("noslg", Hand.Right, avg: 0.990, obp: 0.995, ops: 1.000, hr: 0)).SetName("장타율이_타율보다_낮음");
        }

        [TestCaseSource(nameof(ExtremeBatters))]
        public void 극단_기록에도_가중치는_유한하고_양수이며_합이_1이다(PlayerData batter)
        {
            foreach (var pitcher in new[] { NoDataPitcher, Pitcher("p", Hand.Right, hr9: double.NaN), Pitcher("p2", Hand.Right, hr9: 50) })
            foreach (var baseline in new[] { HitDistributor.LeagueBaseline, HitDistributor.PowerBaseline })
            {
                var mix = HitDistributor.Distribute(batter, pitcher, baseline);
                var parts = new[] { mix.Single, mix.Double, mix.Triple, mix.HomeRun };
                Assert.That(parts.All(v => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0), Is.True, mix.ToString());
                Assert.That(parts.Sum(), Is.EqualTo(1).Within(1e-12));
                Assert.That(mix.Single, Is.GreaterThanOrEqualTo(HitDistributor.MinSingleShare - 1e-12), "단타 하한");
            }
        }

        [Test]
        public void 홈런_0개_타자도_홈런_가중치가_0이_아니고_하한_기울기를_쓴다()
        {
            var b = Batter("hr0", Hand.Right, avg: 0.280, obp: 0.330, ops: 0.680, hr: 0);
            Assert.That(HitDistributor.BatterHomeRunTilt(b), Is.EqualTo(HitDistributor.MinHomeRunTilt));
            Assert.That(HitDistributor.Distribute(b, NoDataPitcher, HitDistributor.LeagueBaseline).HomeRun, Is.GreaterThan(0));
            Assert.Throws<ArgumentException>(() => new HitMix(0, 0, 0, 0));
            Assert.Throws<ArgumentException>(() => new HitMix(double.NaN, 1, 1, 1));
        }

        // ───────── 가중치 랜덤 ─────────

        [Test]
        public void 가중치_뽑기는_구간대로_고르고_0_가중치는_건너뛴다()
        {
            var table = new[] { (PlayOutcome.Walk, 0.0), (PlayOutcome.Single, 0.5), (PlayOutcome.Double, 0.0), (PlayOutcome.HomeRun, 0.5) };
            Assert.That(PlayByPlayGenerator.Pick(table, 0.0), Is.EqualTo(PlayOutcome.Single));
            Assert.That(PlayByPlayGenerator.Pick(table, 0.4999), Is.EqualTo(PlayOutcome.Single));
            Assert.That(PlayByPlayGenerator.Pick(table, 0.5), Is.EqualTo(PlayOutcome.HomeRun));
            Assert.That(PlayByPlayGenerator.Pick(table, 0.9999999999999999), Is.EqualTo(PlayOutcome.HomeRun));
            Assert.Throws<ArgumentException>(() => PlayByPlayGenerator.Pick(new[] { (PlayOutcome.Single, 0.0) }, 0.5));
        }

        [Test]
        public void 같은_Seed로_많이_뽑으면_거포가_교본_타자보다_홈런을_더_친다()
        {
            int HomeRuns(PlayerData b)
            {
                var table = PlayByPlayGenerator.HitTable(HitDistributor.Distribute(b, NoDataPitcher, HitDistributor.LeagueBaseline), 0);
                var random = new SeededRandom(17);
                return Enumerable.Range(0, 5000).Count(_ => PlayByPlayGenerator.Pick(table, random.NextDouble()) == PlayOutcome.HomeRun);
            }

            int textbook = HomeRuns(Batter("t", Hand.Right, avg: 0.270, obp: 0.345, ops: 0.760, hr: 12));
            int slugger = HomeRuns(Batter("s", Hand.Right, avg: 0.270, obp: 0.360, ops: 0.960, hr: 35));
            Assert.That(slugger, Is.GreaterThan(textbook * 1.5), $"거포 {slugger} vs 교본 {textbook}");
        }

        // ───────── Play-by-Play 난수 소비 보존 ─────────

        static List<PitchEvent> AfterSingle()
        {
            var f = FixtureGenerator.CreateTactics(new ClutchSituation(1, FixtureGenerator.RunnersFor(5)), 3, 2);
            return PlayByPlayGenerator.UntilPlateAppearanceEnd(f.Branch.PathFor(f.Events, TacticKind.SwingAway), f.Branch.AfterIndex + 1);
        }

        static Lookup WithData() => new Lookup(
            Batter("home_b4", Hand.Left, avg: 0.292, obp: 0.378, ops: 0.912, hr: 25, risp: 0.305),
            Pitcher(Fixture8thBottom.Pitcher, Hand.Left, era: 3.21, baa: 0.238, hr9: 0.71));

        [Test]
        public void 분배가_바뀌어도_타석당_난수_소비_수는_그대로다()
        {
            foreach (var calc in new[] { new MatchupCalculator(null), new MatchupCalculator(WithData()) })
            {
                var path = AfterSingle();
                var random = new ScriptedRandom(0.1, 0.5, 0.9);
                var segment = new PlayByPlayGenerator(calc, random).Next(path);
                Assert.That(random.Draws, Is.Zero, "작전 전 구간은 난수를 쓰지 않음");

                segment.Branch.PathFor(path.Concat(segment.Events).ToList(), TacticKind.Default);
                Assert.That(random.Draws, Is.EqualTo(2), "기본 진행 = 출루 판정 1 + 결과 1");
                segment.Branch.PathFor(path.Concat(segment.Events).ToList(), TacticKind.SwingAway, TacticOutcome.Success);
                Assert.That(random.Draws, Is.EqualTo(3), "강공 성공 = 결과 1");
            }
        }

        [Test]
        public void 선수_데이터가_있어도_같은_Seed면_같은_타석_결과다()
        {
            List<PitchEvent> Run(int seed)
            {
                var path = AfterSingle();
                var gen = new PlayByPlayGenerator(new MatchupCalculator(WithData()), new SeededRandom(seed));
                var segment = gen.Next(path);
                var joined = path.Concat(segment.Events).ToList();
                return segment.Branch.PathFor(joined, TacticKind.SwingAway, TacticOutcome.Success).ToList();
            }

            var a = Run(99);
            var b = Run(99);
            Assert.That(a.Select(e => (e.Result, e.PlayType, e.RunsScored, e.RunnersAfter)),
                Is.EqualTo(b.Select(e => (e.Result, e.PlayType, e.RunsScored, e.RunnersAfter))));
            Assert.That(PitchEventValidator.ValidateSequence(a), Is.Empty);
        }
    }
}
