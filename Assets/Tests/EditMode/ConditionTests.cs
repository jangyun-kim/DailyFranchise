using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    /// <summary>Phase 22: 선수 컨디션 규칙·밸런스, 선발 로테이션, 컨디션 보정.</summary>
    public class ConditionTests
    {
        const double Eps = 1e-12;

        sealed class Conditions : IConditionSource
        {
            public readonly Dictionary<string, int> Values = new Dictionary<string, int>();
            public int ConditionOf(string id) => Values.TryGetValue(id, out int c) ? c : PlayerCondition.Default;
        }

        sealed class Lookup : IPlayerLookup
        {
            readonly Dictionary<string, PlayerData> _players;
            public Lookup(params PlayerData[] players) => _players = players.ToDictionary(p => p.Id);
            public PlayerData FindPlayer(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;
        }

        static GameParticipation Game(IEnumerable<string> batters = null, IDictionary<string, int> pitches = null, IEnumerable<string> rested = null)
        {
            var g = new GameParticipation();
            foreach (var b in batters ?? Enumerable.Empty<string>()) g.AddBatter(b);
            foreach (var kv in pitches ?? new Dictionary<string, int>()) g.AddPitches(kv.Key, kv.Value);
            foreach (var r in rested ?? Enumerable.Empty<string>()) g.AddRosterPlayer(r);
            return g;
        }

        [Test]
        public void 컨디션은_1에서_5단계이고_이름이_있다()
        {
            Assert.That(Enumerable.Range(1, 5).Select(PlayerCondition.Label), Is.EqualTo(new[] { "위험", "지침", "보통", "좋음", "최상" }));
            Assert.That((PlayerCondition.Clamp(0), PlayerCondition.Clamp(9), PlayerCondition.Default), Is.EqualTo((1, 5, 3)));
        }

        [TestCase(1, 0)]
        [TestCase(29, 0)]
        [TestCase(30, -1)]
        [TestCase(59, -1)]
        [TestCase(60, -2)]
        [TestCase(110, -2)]
        public void 투수는_30구_이상_마이너스1_60구_이상_마이너스2(int pitches, int delta)
        {
            var random = new ScriptedRandom(0.0);
            Assert.That(ConditionRules.DeltaFor("p", Game(pitches: new Dictionary<string, int> { ["p"] = pitches }), random), Is.EqualTo(delta));
            Assert.That(random.Draws, Is.Zero, "투수는 난수를 쓰지 않는다");
        }

        [Test]
        public void 야수는_25퍼센트_확률로_하락하고_결장하면_1_회복한다()
        {
            var game = Game(batters: new[] { "b" }, rested: new[] { "r" });
            Assert.That(ConditionRules.DeltaFor("b", game, new ScriptedRandom(0.2499)), Is.EqualTo(-1));
            Assert.That(ConditionRules.DeltaFor("b", game, new ScriptedRandom(0.25)), Is.Zero);
            var random = new ScriptedRandom(0.0);
            Assert.That(ConditionRules.DeltaFor("r", game, random), Is.EqualTo(1));
            Assert.That(random.Draws, Is.Zero, "휴식은 난수를 쓰지 않는다");
        }

        [Test]
        public void 적용은_1에서_5로_자르고_기록_없는_선수는_보통에서_시작한다()
        {
            var conditions = new Dictionary<string, int> { ["low"] = 1, ["high"] = 5, ["sp"] = 2 };
            var changes = ConditionRules.Apply(conditions,
                Game(batters: new[] { "low", "new" }, pitches: new Dictionary<string, int> { ["sp"] = 95 }, rested: new[] { "high" }),
                new ScriptedRandom(0.0)); // 야수 모두 하락
            Assert.That(changes.Select(c => c.PlayerId), Is.EqualTo(new[] { "high", "low", "new", "sp" }), "ID 순");
            Assert.That(conditions, Is.EquivalentTo(new Dictionary<string, int> { ["low"] = 1, ["high"] = 5, ["new"] = 2, ["sp"] = 1 }));
            Assert.That(changes.Single(c => c.PlayerId == "sp").Delta, Is.EqualTo(-1), "2 − 2 → 1에서 멈춘다");
            Assert.Throws<ArgumentNullException>(() => ConditionRules.Apply(conditions, Game(), null));
        }

        [Test]
        public void 같은_난수와_출장이면_같은_결과()
        {
            var batters = Enumerable.Range(1, 9).Select(i => "b" + i).ToArray();
            var a = new Dictionary<string, int>();
            var b = new Dictionary<string, int>();
            for (int g = 0; g < 10; g++)
            {
                ConditionRules.Apply(a, Game(batters), new SeededRandom(100 + g));
                ConditionRules.Apply(b, Game(batters.Reverse()), new SeededRandom(100 + g)); // 추가 순서가 달라도
            }
            Assert.That(a, Is.EquivalentTo(b));
        }

        [Test]
        public void 매일_나서는_타자는_10경기_동안_평균적으로_떨어지고_하루씩_쉬면_버틴다()
        {
            const int runs = 2000;
            var random = new SeededRandom(20261016);
            double everyday = 0, rotated = 0;
            int everydayAtBottom = 0;
            for (int run = 0; run < runs; run++)
            {
                var c1 = new Dictionary<string, int>();
                var c2 = new Dictionary<string, int>();
                for (int g = 1; g <= 10; g++)
                {
                    ConditionRules.Apply(c1, Game(batters: new[] { "b" }), random);
                    // 4경기마다 하루 휴식 (벤치 활용)
                    ConditionRules.Apply(c2, g % 4 == 0 ? Game(rested: new[] { "b" }) : Game(batters: new[] { "b" }), random);
                    Assert.That(c1["b"], Is.InRange(1, 5));
                }
                everyday += c1["b"];
                rotated += c2["b"];
                if (c1["b"] == PlayerCondition.Exhausted) everydayAtBottom++;
            }
            everyday /= runs;
            rotated /= runs;
            // 매일 출장: 기대 하락 10 × 0.25 = 2.5 (보통 3에서 1까지 잘림) → 평균 1점대
            Assert.That(everyday, Is.LessThan(2.0), $"매일 출장 평균 {everyday:0.00}");
            Assert.That(everyday, Is.GreaterThanOrEqualTo(1.0));
            Assert.That(everydayAtBottom, Is.LessThan(runs), "모두가 바닥은 아니다");
            Assert.That(rotated, Is.GreaterThan(everyday + 0.8), $"휴식을 섞으면 평균 {rotated:0.00}");
            Assert.That(rotated, Is.GreaterThan(2.3), "4경기마다 쉬면 보통 수준을 지킨다");
        }

        [Test]
        public void 결장하면_1씩_회복해_상한_5에_닿는다()
        {
            var c = new Dictionary<string, int> { ["b"] = 1 };
            var seen = new List<int>();
            for (int g = 0; g < 6; g++)
            {
                ConditionRules.Apply(c, Game(rested: new[] { "b" }), new ScriptedRandom(0.0));
                seen.Add(c["b"]);
            }
            Assert.That(seen, Is.EqualTo(new[] { 2, 3, 4, 5, 5, 5 }));
        }

        static TeamRoster Rotation(params string[] starters)
        {
            var sp = starters.Select((id, i) => new PlayerData { Id = id, Name = id, Role = RosterRole.Starter, Order = i + 1, Position = "선발" }).ToList();
            return new TeamRoster("가", null, null, sp, null);
        }

        [Test]
        public void 오늘_선발은_직전_선발_다음_순번_중_컨디션_보통_이상인_첫_투수()
        {
            var roster = Rotation("s1", "s2", "s3");
            var cond = new Conditions();
            Assert.That(RotationPicker.Pick(roster, cond).Id, Is.EqualTo("s1"), "직전 선발 모름 → 1번");
            Assert.That(RotationPicker.Pick(roster, cond, "s1").Id, Is.EqualTo("s2"));
            Assert.That(RotationPicker.Pick(roster, cond, "s3").Id, Is.EqualTo("s1"), "마지막 다음은 1번");
            Assert.That(RotationPicker.Pick(roster, cond, "다른 팀 투수").Id, Is.EqualTo("s1"));
            Assert.That(RotationPicker.Apply(roster, cond), Is.SameAs(roster), "이미 1번이면 그대로");

            cond.Values["s2"] = PlayerCondition.Tired;
            Assert.That(RotationPicker.Pick(roster, cond, "s1").Id, Is.EqualTo("s3"), "지친 2번은 건너뛴다");
            cond.Values["s1"] = 1;
            cond.Values["s3"] = 1;
            Assert.That(RotationPicker.Pick(roster, cond, "s1").Id, Is.EqualTo("s2"), "모두 보통 미만이면 가장 나은 투수");

            cond.Values.Clear();
            var today = RotationPicker.Apply(roster, cond, "s1");
            Assert.That(today.Starters.Select(p => (p.Id, p.Order)), Is.EqualTo(new[] { ("s2", 1), ("s1", 2), ("s3", 3) }));
            Assert.That(today.StartingPitcher.Id, Is.EqualTo("s2"));
            Assert.That(roster.Starters.Select(p => (p.Id, p.Order)), Is.EqualTo(new[] { ("s1", 1), ("s2", 2), ("s3", 3) }), "원본 불변");
            Assert.That(RotationPicker.Pick(new TeamRoster("나", null, null, null, null), cond), Is.Null);
        }

        [Test]
        public void 다섯_명_로테이션은_매_경기_던져도_선발이_위험에_고정되지_않는다()
        {
            var roster = Rotation("s1", "s2", "s3", "s4", "s5");
            var conditions = new Dictionary<string, int>();
            var source = new Conditions();
            var starts = new List<string>();
            string last = null;
            for (int g = 0; g < 20; g++)
            {
                foreach (var kv in conditions) source.Values[kv.Key] = kv.Value;
                var today = RotationPicker.Pick(roster, source, last);
                last = today.Id;
                Assert.That(source.ConditionOf(today.Id), Is.GreaterThanOrEqualTo(PlayerCondition.Normal), $"{g + 1}경기 선발 {today.Id}");
                starts.Add(today.Id);
                ConditionRules.Apply(conditions,
                    Game(pitches: new Dictionary<string, int> { [today.Id] = 95 }, rested: roster.Starters.Select(p => p.Id)), new ScriptedRandom(0.0));
            }
            Assert.That(starts.GroupBy(s => s).Select(g => g.Count()), Is.All.EqualTo(4), "20경기 = 5명 × 4번");
            Assert.That(starts.Take(5), Is.EqualTo(new[] { "s1", "s2", "s3", "s4", "s5" }));
        }

        static PlayerData Batter => new PlayerData { Id = "B", Role = RosterRole.Lineup, BattingAverage = 0.280, OnBasePercentage = 0.350 };
        static PlayerData Pitcher => new PlayerData { Id = "P", Role = RosterRole.Starter, Era = 3.80, OpponentAverage = 0.255 };
        static readonly MatchupSituation Empty = new MatchupSituation(0, BaseState.Empty);

        [TestCase(5, 3, 1.04, "타자 컨디션 최상 +4%")]
        [TestCase(1, 3, 0.92, "타자 컨디션 위험 -8%")]
        [TestCase(3, 5, 0.96, "투수 컨디션 최상 -4%")]
        [TestCase(3, 1, 1.08, "투수 컨디션 위험 +8%")]
        [TestCase(2, 4, 1.00, null)]
        [TestCase(1, 1, 0.92 * 1.08, "타자 컨디션 위험 -8%")]
        public void 컨디션_보정은_최상_4퍼센트_위험_8퍼센트(int batter, int pitcher, double factor, string firstLabel)
        {
            var players = new Lookup(Batter, Pitcher);
            var cond = new Conditions();
            cond.Values["B"] = batter;
            cond.Values["P"] = pitcher;
            double plain = new MatchupCalculator(players).HitProbability("B", "P", Empty);
            var calc = new MatchupCalculator(players, conditions: cond);
            Assert.That(calc.HitProbability("B", "P", Empty), Is.EqualTo(plain * factor).Within(Eps));
            var context = calc.ContextFor("B", "P", Empty);
            Assert.That((context.BatterCondition, context.PitcherCondition), Is.EqualTo((batter, pitcher)));
            var adjustments = calc.Adjustments(context).Where(a => a.Label.Contains("컨디션")).ToList();
            Assert.That(adjustments.FirstOrDefault().Label == null ? null : adjustments[0].Text, Is.EqualTo(firstLabel));
        }

        [Test]
        public void 컨디션을_모르거나_데이터가_없는_선수는_보정하지_않는다()
        {
            var players = new Lookup(Batter);
            var cond = new Conditions();
            cond.Values["B"] = 5;
            var calc = new MatchupCalculator(players, conditions: cond);
            var context = calc.ContextFor("B", "없는 투수", Empty);
            Assert.That(context.PitcherCondition, Is.Zero, "리그 평균 투수");
            Assert.That(context.BatterCondition, Is.EqualTo(5));
            Assert.That(new MatchupCalculator(players).ContextFor("B", "x", Empty).BatterCondition, Is.Zero, "공급자 없음");
            Assert.That(new ConditionModifier(ConditionSide.Batter).RelativeBonus(null), Is.Zero);
        }
    }
}
