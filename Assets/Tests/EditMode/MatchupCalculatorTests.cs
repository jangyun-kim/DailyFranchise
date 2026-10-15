using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    /// <summary>MatchupCalculator 수식·보정·Clamp 검증. 기대값은 문서화된 공식을 손으로 풀어 쓴다.</summary>
    public class MatchupCalculatorTests
    {
        const double Eps = 1e-9;

        sealed class Lookup : IPlayerLookup
        {
            readonly Dictionary<string, PlayerData> _players;
            public Lookup(params PlayerData[] players) => _players = players.ToDictionary(p => p.Id);
            public PlayerData FindPlayer(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;
        }

        sealed class FixedModifier : IMatchupModifier
        {
            readonly Func<double, double> _f;
            public FixedModifier(Func<double, double> f) => _f = f;
            public double Adjust(double probability, MatchupContext context) => _f(probability);
        }

        static PlayerData Batter(string id, double avg, double obp = 0) =>
            new PlayerData { Id = id, Name = id, Role = RosterRole.Lineup, BattingAverage = avg, OnBasePercentage = obp };

        static PlayerData Pitcher(string id, double era, double baa = 0) =>
            new PlayerData { Id = id, Name = id, Role = RosterRole.Starter, Era = era, OpponentAverage = baa };

        static readonly MatchupSituation Empty = new MatchupSituation(0, BaseState.Empty);
        static readonly MatchupSituation Corners = new MatchupSituation(1, new BaseState("r1", null, "r3"));

        static MatchupCalculator NoModifiers(params PlayerData[] players) =>
            new MatchupCalculator(new Lookup(players), Array.Empty<IMatchupModifier>());

        [Test]
        public void 타자_안타력은_타율_두_배와_출루율의_평균()
        {
            Assert.That(MatchupCalculator.BatterHitRate(Batter("b", 0.300, 0.380)), Is.EqualTo((0.600 + 0.380) / 3).Within(Eps));
            // 출루율이 없으면 타율 + 리그 (출루율 − 타율) = .300 + .075
            Assert.That(MatchupCalculator.BatterHitRate(Batter("b", 0.300)), Is.EqualTo((0.600 + 0.375) / 3).Within(Eps));
        }

        [Test]
        public void 투수_허용률은_피안타율과_평균자책_환산값의_평균()
        {
            Assert.That(MatchupCalculator.PitcherAllowedRate(Pitcher("p", 4.30, 0.270)), Is.EqualTo(0.270).Within(Eps), "리그 평균");
            Assert.That(MatchupCalculator.PitcherAllowedRate(Pitcher("p", 2.15, 0.230)), Is.EqualTo((0.230 + 0.135) / 2).Within(Eps));
            Assert.That(MatchupCalculator.PitcherAllowedRate(Pitcher("p", 2.15)), Is.EqualTo(0.135).Within(Eps), "피안타율 없으면 평균자책 환산만");
        }

        [Test]
        public void 리그_평균_대결의_기준_확률()
        {
            // B₀ = (2 × .270 + .345) / 3 = .295, P₀ = .270 → H₀ = .2825
            Assert.That(MatchupCalculator.NeutralHitProbability, Is.EqualTo(0.2825).Within(Eps));
        }

        [Test]
        public void 샘플_대결의_타석_확률과_작전_성공률을_공식대로_계산한다()
        {
            // 성지환(좌타 .318 / .391, 득점권 .341) vs 염태준(좌투 3.21 / .238), 1사 1·3루
            // Phase 17 기본 보정: 플래툰 좌투 vs 좌타 −5% → 득점권 클러치 .341 / .318 − 1 (≈ +7.2%)
            var batter = Batter("b", 0.318, 0.391);
            batter.Bats = Hand.Left;
            batter.ScoringPositionAverage = 0.341;
            var pitcher = Pitcher("p", 3.21, 0.238);
            pitcher.Throws = Hand.Left;
            var calc = new MatchupCalculator(new Lookup(batter, pitcher));
            double b = (2 * 0.318 + 0.391) / 3;
            double p = (0.238 + 0.270 * 3.21 / 4.30) / 2;
            double h = (b + p) / 2 * 0.95 * (0.341 / 0.318);
            Assert.That(calc.HitProbability("b", "p", Corners), Is.EqualTo(h).Within(Eps));
            Assert.That(calc.HitProbability("b", "p", Empty), Is.EqualTo((b + p) / 2 * 0.95).Within(Eps), "주자 없으면 플래툰만");

            double r = h / 0.2825;
            Assert.That(calc.TacticSuccessRate(TacticKind.SwingAway, 0.5, "b", "p", Corners), Is.EqualTo(0.5 * r).Within(Eps));
            Assert.That(calc.TacticSuccessRate(TacticKind.SqueezeBunt, 0.6, "b", "p", Corners), Is.EqualTo(0.6 * (1 + 0.5 * (r - 1))).Within(Eps));
            Assert.That(Math.Round(0.5 * r * 100), Is.EqualTo(51));
            Assert.That(Math.Round(0.6 * (1 + 0.5 * (r - 1)) * 100), Is.EqualTo(60));
        }

        [Test]
        public void 좋은_타자일수록_좋은_투수일수록_확률이_오르고_내린다()
        {
            var calc = NoModifiers(Batter("weak", 0.220, 0.280), Batter("avg", 0.270, 0.345), Batter("star", 0.340, 0.420),
                Pitcher("ace", 2.10, 0.210), Pitcher("mid", 4.30, 0.270), Pitcher("bad", 6.50, 0.310));

            double Swing(string b, string p) => calc.TacticSuccessRate(TacticKind.SwingAway, 0.5, b, p, Empty);
            Assert.That(Swing("weak", "mid"), Is.LessThan(Swing("avg", "mid")));
            Assert.That(Swing("avg", "mid"), Is.LessThan(Swing("star", "mid")));
            Assert.That(Swing("star", "ace"), Is.LessThan(Swing("star", "mid")));
            Assert.That(Swing("star", "mid"), Is.LessThan(Swing("star", "bad")));
            Assert.That(Swing("avg", "mid"), Is.EqualTo(0.5).Within(Eps), "평균 대결은 장면 기본값");
        }

        [Test]
        public void 스퀴즈는_강공보다_스탯_차이에_절반만_반응한다()
        {
            var calc = NoModifiers(Batter("star", 0.340, 0.420), Pitcher("bad", 5.00, 0.290));
            double swing = calc.TacticSuccessRate(TacticKind.SwingAway, 0.5, "star", "bad", Empty) / 0.5 - 1;
            double squeeze = calc.TacticSuccessRate(TacticKind.SqueezeBunt, 0.6, "star", "bad", Empty) / 0.6 - 1;
            Assert.That(swing, Is.GreaterThan(0));
            Assert.That(squeeze, Is.EqualTo(swing * 0.5).Within(Eps));
        }

        [Test]
        public void 두_선수_모두_데이터가_없으면_장면_기본_성공률을_그대로_쓴다()
        {
            var calc = new MatchupCalculator(new Lookup());
            Assert.That(calc.TacticSuccessRate(TacticKind.SqueezeBunt, 0.6, "x", "y", Corners), Is.EqualTo(0.6), "득점권 보정도 하지 않음");
            Assert.That(calc.TacticSuccessRate(TacticKind.SwingAway, 0.5, "x", "y", Corners), Is.EqualTo(0.5));
            Assert.That(calc.ContextFor("x", "y", Corners), Is.Null);
            Assert.That(new MatchupCalculator(null).TacticSuccessRate(TacticKind.SwingAway, 0.5, "x", "y", Empty), Is.EqualTo(0.5));
        }

        [Test]
        public void 한쪽만_데이터가_있으면_다른_쪽은_리그_평균_선수()
        {
            var calc = NoModifiers(Batter("star", 0.340, 0.420), Pitcher("ace", 2.10, 0.210));
            var withAvgPitcher = NoModifiers(Batter("star", 0.340, 0.420), Pitcher("lg", 4.30, 0.270));
            Assert.That(calc.TacticSuccessRate(TacticKind.SwingAway, 0.5, "star", "missing", Empty),
                Is.EqualTo(withAvgPitcher.TacticSuccessRate(TacticKind.SwingAway, 0.5, "star", "lg", Empty)).Within(Eps));
            Assert.That(calc.ContextFor("missing", "ace", Empty).Batter.Name, Is.EqualTo("리그 평균 타자"));
        }

        [Test]
        public void 기본_진행은_확률이_1()
        {
            var calc = NoModifiers(Batter("b", 0.1), Pitcher("p", 9));
            Assert.That(calc.TacticSuccessRate(TacticKind.Default, 0.3, "b", "p", Empty), Is.EqualTo(1));
        }

        [Test]
        public void 작전_성공률은_5에서_95퍼센트로_잘린다()
        {
            var calc = NoModifiers(Batter("god", 1.0, 1.0), Batter("zero", 0.0, 0.001), Pitcher("worst", 27, 0.6), Pitcher("best", 0.0, 0.050));
            Assert.That(calc.TacticSuccessRate(TacticKind.SwingAway, 0.9, "god", "worst", Empty), Is.EqualTo(MatchupCalculator.MaxTacticRate));
            Assert.That(calc.TacticSuccessRate(TacticKind.SwingAway, 0.1, "zero", "best", Empty), Is.EqualTo(MatchupCalculator.MinTacticRate));
            Assert.That(calc.TacticSuccessRate(TacticKind.SqueezeBunt, 1.0, "god", "worst", Empty), Is.EqualTo(MatchupCalculator.MaxTacticRate));
        }

        [TestCase(double.NaN)]
        [TestCase(-0.3)]
        [TestCase(double.PositiveInfinity)]
        public void 비정상_기록은_리그_평균으로_보고_확률은_0에서_1_사이(double bad)
        {
            var weird = new MatchupCalculator(new Lookup(Batter("b", bad, bad), Pitcher("p", bad, bad)));
            foreach (var s in new[] { Empty, Corners })
            {
                double h = weird.HitProbability("b", "p", s);
                Assert.That(h, Is.InRange(0.0, 1.0));
                Assert.That(double.IsNaN(h), Is.False);
                double rate = weird.TacticSuccessRate(TacticKind.SwingAway, 0.5, "b", "p", s);
                Assert.That(rate, Is.InRange(MatchupCalculator.MinTacticRate, MatchupCalculator.MaxTacticRate));
            }
            Assert.That(MatchupCalculator.BatterHitRate(Batter("b", bad, bad)), Is.EqualTo(0.295).Within(Eps));
        }

        [Test]
        public void 기록이_1을_넘으면_1로_자른다()
        {
            Assert.That(MatchupCalculator.BatterHitRate(Batter("b", 3.0, 5.0)), Is.EqualTo(1.0));
            Assert.That(MatchupCalculator.PitcherAllowedRate(Pitcher("p", 999, 2.0)), Is.InRange(0.0, 1.0));
        }

        [Test]
        public void 보정은_인터페이스로_끼우고_결과는_매번_0에서_1로_자른다()
        {
            var players = new Lookup(Batter("b", 0.300, 0.380), Pitcher("p", 4.30, 0.270));
            double raw = NoModifiers(Batter("b", 0.300, 0.380), Pitcher("p", 4.30, 0.270)).HitProbability("b", "p", Empty);

            var doubled = new MatchupCalculator(players, new[] { new FixedModifier(p => p * 2) });
            Assert.That(doubled.HitProbability("b", "p", Empty), Is.EqualTo(raw * 2).Within(Eps));

            Assert.That(new MatchupCalculator(players, new[] { new FixedModifier(p => p * 100) }).HitProbability("b", "p", Empty), Is.EqualTo(1));
            Assert.That(new MatchupCalculator(players, new[] { new FixedModifier(p => -5) }).HitProbability("b", "p", Empty), Is.EqualTo(0));
            Assert.That(new MatchupCalculator(players, new[] { new FixedModifier(p => double.NaN) }).HitProbability("b", "p", Empty), Is.EqualTo(0));

            // 기본 보정은 플래툰 → 득점권 클러치 (Phase 17) → 투수 체력 (Phase 20, 투수 상태를 모르면 작동하지 않는다).
            // 두 선수 모두 우(기본값) → 우투 vs 우타 −3%, 득점권 기록 없음 → +5%
            var defaults = new MatchupCalculator(players);
            Assert.That(defaults.Modifiers.Select(m => m.GetType()),
                Is.EqualTo(new[] { typeof(PlatoonModifier), typeof(ClutchModifier), typeof(StaminaModifier) }));
            Assert.That(defaults.HitProbability("b", "p", Corners), Is.EqualTo(raw * 0.97 * 1.05).Within(Eps));
            Assert.That(defaults.HitProbability("b", "p", new MatchupSituation(2, new BaseState("r1", null, null))), Is.EqualTo(raw * 0.97).Within(Eps), "1루만은 득점권 아님");
            // 이전 일괄 득점권 보정도 그대로 끼울 수 있다.
            Assert.That(new MatchupCalculator(players, new[] { new ScoringPositionModifier() }).HitProbability("b", "p", Corners), Is.EqualTo(raw * 1.05).Within(Eps));

            Assert.Throws<ArgumentException>(() => new MatchupCalculator(players, new IMatchupModifier[] { null }));
        }

        [Test]
        public void 잘못된_입력은_거부한다()
        {
            var calc = NoModifiers();
            Assert.Throws<ArgumentOutOfRangeException>(() => calc.TacticSuccessRate(TacticKind.SwingAway, 1.2, "b", "p", Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => calc.TacticSuccessRate(TacticKind.SwingAway, double.NaN, "b", "p", Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchupSituation(3, BaseState.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoringPositionModifier(double.NaN));
        }

        [Test]
        public void 승부처_성공률은_분기점_투구의_타자_투수와_직후_상황으로_계산한다()
        {
            var f = Fixture8thBottomTactics.Create();
            var decision = f.Events[f.Branch.AfterIndex];
            Assert.That(decision.BatterId, Is.EqualTo(Fixture8thBottom.Batter1));
            Assert.That(decision.PitcherId, Is.EqualTo(Fixture8thBottom.Pitcher));

            var calc = new MatchupCalculator(new Lookup(Batter(Fixture8thBottom.Batter1, 0.318, 0.391), Pitcher(Fixture8thBottom.Pitcher, 3.21, 0.238)));
            var situation = MatchupSituation.After(decision);
            Assert.That(situation.RunnersInScoringPosition, Is.True, "1사 1·3루");
            foreach (var t in new[] { TacticKind.SwingAway, TacticKind.SqueezeBunt })
                Assert.That(calc.TacticSuccessRate(f.Branch, f.Events, t),
                    Is.EqualTo(calc.TacticSuccessRate(t, f.Branch[t].SuccessRate, decision.BatterId, decision.PitcherId, situation)));
            Assert.That(calc.TacticSuccessRate(f.Branch, f.Events, TacticKind.Default), Is.EqualTo(1));
            Assert.That(calc.DecisionContext(f.Branch, f.Events).Batter.BattingAverage, Is.EqualTo(0.318));

            // 데이터 없는 계산기는 장면 설정값 그대로 (Phase 8 동작 보존)
            var none = new MatchupCalculator(null);
            Assert.That(none.TacticSuccessRate(f.Branch, f.Events, TacticKind.SqueezeBunt), Is.EqualTo(Fixture8thBottomTactics.SqueezeSuccessRate));
            Assert.That(none.TacticSuccessRate(f.Branch, f.Events, TacticKind.SwingAway), Is.EqualTo(Fixture8thBottomTactics.SwingAwaySuccessRate));
            Assert.Throws<ArgumentException>(() => calc.TacticSuccessRate(f.Branch, f.Events.Take(1).ToList(), TacticKind.SwingAway));
        }
    }

    public class BranchResolveWithRateTests
    {
        [TestCase(0.3, 0.29, TacticOutcome.Success)]
        [TestCase(0.3, 0.30, TacticOutcome.Failure)]
        [TestCase(0.61, 0.60, TacticOutcome.Success)]
        [TestCase(0.0, 0.0, TacticOutcome.Failure)]
        [TestCase(1.0, 0.9999, TacticOutcome.Success)]
        public void 주입한_성공률로_판정한다(double rate, double roll, TacticOutcome expected)
        {
            var branch = Fixture8thBottomTactics.Create().Branch;
            var random = new ScriptedRandom(roll);
            Assert.That(branch.Resolve(TacticKind.SqueezeBunt, random, rate), Is.EqualTo(expected));
            Assert.That(random.Draws, Is.EqualTo(1));
        }

        [Test]
        public void 확률_없는_작전은_주입값과_무관하게_성공하고_난수를_쓰지_않는다()
        {
            var branch = Fixture8thBottomTactics.Create().Branch;
            var random = new ScriptedRandom(0.5);
            Assert.That(branch.Resolve(TacticKind.Default, random, 0.0), Is.EqualTo(TacticOutcome.Success));
            Assert.That(random.Draws, Is.Zero);
        }

        [TestCase(-0.01)]
        [TestCase(1.01)]
        [TestCase(double.NaN)]
        public void 범위_밖_성공률은_거부(double rate)
        {
            var branch = Fixture8thBottomTactics.Create().Branch;
            Assert.Throws<ArgumentOutOfRangeException>(() => branch.Resolve(TacticKind.SwingAway, new ScriptedRandom(0.5), rate));
        }
    }
}
