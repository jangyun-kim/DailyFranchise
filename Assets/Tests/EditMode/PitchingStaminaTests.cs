using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.Sim.Tests
{
    /// <summary>Phase 19: 투수 체력(투구 수)·체력 저하 보정·수비 반 이닝·투수 교체 규칙.</summary>
    public class PitchingStaminaTests
    {
        const double Eps = 1e-12;

        sealed class Lookup : IPlayerLookup
        {
            readonly Dictionary<string, PlayerData> _players;
            public Lookup(params PlayerData[] players) => _players = players.ToDictionary(p => p.Id);
            public PlayerData FindPlayer(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;
        }

        /// <summary>투수 ID → 고정 투구 수 (테스트에서 바꿔 가며 쓴다).</summary>
        sealed class Counts : IPitcherStateSource
        {
            public readonly Dictionary<string, int> Pitches = new Dictionary<string, int>();
            public PitcherState StateOf(string id) =>
                Pitches.TryGetValue(id, out int n) ? new PitcherState(id, PitcherState.IsStarterSlot(id), n) : null;
        }

        /// <summary>경로에서 실제로 던진 공을 세는 공급자 (화면 컨트롤러와 같은 방식).</summary>
        sealed class PathCounts : IPitcherStateSource
        {
            public List<PitchEvent> Path = new List<PitchEvent>();
            public int Before;
            public PitcherState StateOf(string id) =>
                new PitcherState(id, PitcherState.IsStarterSlot(id), Before + PitcherState.PitchesBy(Path, id, Path.Count - 1));
        }

        static PlayerData Batter => new PlayerData { Id = "away_b1", Role = RosterRole.Lineup, BattingAverage = 0.280, OnBasePercentage = 0.350 };
        static PlayerData Pitcher(string id) => new PlayerData { Id = id, Role = RosterRole.Starter, Era = 3.80, OpponentAverage = 0.255 };
        static readonly MatchupSituation Empty = new MatchupSituation(0, BaseState.Empty);

        [TestCase(true, 0, 100, FatigueLevel.Fresh)]
        [TestCase(true, 70, 30, FatigueLevel.Fresh)]
        [TestCase(true, 71, 29, FatigueLevel.Tired)]
        [TestCase(true, 99, 1, FatigueLevel.Tired)]
        [TestCase(true, 100, 0, FatigueLevel.Exhausted)]
        [TestCase(true, 130, 0, FatigueLevel.Exhausted)]
        [TestCase(false, 21, 30, FatigueLevel.Fresh)]
        [TestCase(false, 22, 27, FatigueLevel.Tired)]
        [TestCase(false, 30, 0, FatigueLevel.Exhausted)]
        public void 체력은_한계_투구_수_선발_100_구원_30_기준이다(bool starter, int pitches, int percent, FatigueLevel level)
        {
            var s = new PitcherState("p", starter, pitches);
            Assert.That(s.Limit, Is.EqualTo(starter ? 100 : 30));
            Assert.That(s.StaminaPercent, Is.EqualTo(percent));
            Assert.That(s.Level, Is.EqualTo(level));
        }

        [Test]
        public void 투구_수는_실제_투구_이벤트를_세고_더하기는_새_상태다()
        {
            var f = Fixture8thBottom.Create();
            int last = f.Events.Count - 1;
            Assert.That(PitcherState.PitchesBy(f.Events, Fixture8thBottom.Pitcher, last), Is.EqualTo(f.Events.Count(e => e.PitcherId == Fixture8thBottom.Pitcher)));
            Assert.That(PitcherState.PitchesBy(f.Events, Fixture8thBottom.Pitcher, -1), Is.Zero);
            Assert.That(PitcherState.PitchesBy(f.Events, "없음", last), Is.Zero);
            Assert.That(PitcherState.IsStarterSlot("away_sp"), Is.True);
            Assert.That(PitcherState.IsStarterSlot("home_rp1"), Is.False);

            var s = new PitcherState("home_sp", true, 95);
            var after = s.AddPitches(5);
            Assert.That((s.PitchCount, after.PitchCount, after.Level), Is.EqualTo((95, 100, FatigueLevel.Exhausted)));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.AddPitches(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PitcherState("p", true, -1));
        }

        [Test]
        public void 체력_저하는_5퍼센트_고갈은_10퍼센트_피안타_보정이고_툴팁에_보인다()
        {
            var players = new Lookup(Batter, Pitcher("away_sp"));
            var counts = new Counts();
            var calc = new MatchupCalculator(players, pitcherStates: counts);
            var plain = new MatchupCalculator(players);
            Assert.That(calc.Modifiers.Select(m => m.GetType()),
                Is.EqualTo(new[] { typeof(PlatoonModifier), typeof(ClutchModifier), typeof(StaminaModifier), typeof(ConditionModifier), typeof(ConditionModifier),
                    typeof(ProgressionModifier), typeof(ProgressionModifier) }), "Phase 29: 에이징 폼 보정 두 개 추가");
            Assert.That(plain.Modifiers.Select(m => m.GetType()), Is.EqualTo(calc.Modifiers.Select(m => m.GetType())),
                "기본 보정 목록은 같다 (공급자가 없으면 체력 보정은 작동하지 않을 뿐)");

            double baseline = plain.HitProbability("away_b1", "away_sp", Empty);
            Assert.That(calc.HitProbability("away_b1", "away_sp", Empty), Is.EqualTo(baseline).Within(Eps), "상태를 모르면 보정 없음");

            counts.Pitches["away_sp"] = 60;
            Assert.That(calc.HitProbability("away_b1", "away_sp", Empty), Is.EqualTo(baseline).Within(Eps), "체력 40%");
            Assert.That(calc.Adjustments(calc.ContextFor("away_b1", "away_sp", Empty)).Select(a => a.Label), Has.None.Contains("체력"));

            counts.Pitches["away_sp"] = 70;
            Assert.That(calc.HitProbability("away_b1", "away_sp", Empty), Is.EqualTo(baseline).Within(Eps), "70구 = 체력 30% → 아직 보정 없음");

            counts.Pitches["away_sp"] = 71;
            Assert.That(calc.PitcherStateOf("away_sp").Level, Is.EqualTo(FatigueLevel.Tired));
            Assert.That(calc.HitProbability("away_b1", "away_sp", Empty), Is.EqualTo(baseline * 1.05).Within(Eps), "체력 29% → +5%");
            Assert.That(calc.Adjustments(calc.ContextFor("away_b1", "away_sp", Empty)).Last().Text, Is.EqualTo("체력 저하 +5%"));

            counts.Pitches["away_sp"] = 99;
            Assert.That(calc.HitProbability("away_b1", "away_sp", Empty), Is.EqualTo(baseline * 1.05).Within(Eps), "99구까지는 저하 단계");

            counts.Pitches["away_sp"] = 100;
            Assert.That(calc.HitProbability("away_b1", "away_sp", Empty), Is.EqualTo(baseline * 1.10).Within(Eps), "고갈 → +10%");
            Assert.That(calc.Adjustments(calc.ContextFor("away_b1", "away_sp", Empty)).Last().Text, Is.EqualTo("체력 고갈: 피안타율 급증 +10%"));

            // 강공 성공률도 같은 비율로 오른다 (계산기 공식 그대로).
            double swingFresh = plain.TacticSuccessRate(TacticKind.SwingAway, 0.3, "away_b1", "away_sp", Empty);
            double swingTired = calc.TacticSuccessRate(TacticKind.SwingAway, 0.3, "away_b1", "away_sp", Empty);
            Assert.That(swingTired, Is.EqualTo(swingFresh * 1.10).Within(1e-9));

            // 데이터 없는 대결(리그 평균)에도 체력은 적용된다.
            var noData = new MatchupCalculator(null, pitcherStates: counts);
            Assert.That(noData.HitProbability("x", "away_sp", Empty),
                Is.EqualTo(MatchupCalculator.NeutralHitProbability * 1.10).Within(Eps));
        }

        [Test]
        public void 타석이_진행될수록_투구_수가_늘고_한계에서_확률이_뛴다()
        {
            var f = FixtureGenerator.CreateTactics(new ClutchSituation(0, BaseState.Empty), 3, 3);
            var players = new Lookup(new PlayerData { Id = "home_b3", Role = RosterRole.Lineup, BattingAverage = 0.300 }, Pitcher(FixtureGenerator.Pitcher));
            var source = new PathCounts { Before = 80 };
            var calc = new MatchupCalculator(players, pitcherStates: source);
            var gen = new PlayByPlayGenerator(calc, new ScriptedRandom(0.99)); // 모두 범타 → 이닝 안에서 타석이 이어진다

            var path = f.Branch.PathFor(f.Events, TacticKind.Default).ToList();
            path = PlayByPlayGenerator.UntilPlateAppearanceEnd(path, f.Branch.AfterIndex + 1);
            source.Path = path;
            int previous = source.StateOf(FixtureGenerator.Pitcher).PitchCount;
            Assert.That(previous, Is.EqualTo(80 + path.Count));

            var segment = gen.Next(path);
            Assert.That(segment, Is.Not.Null);
            var next = segment.Branch.PathFor(path.Concat(segment.Events).ToList(), TacticKind.Default);
            source.Path = next.ToList();
            int now = source.StateOf(FixtureGenerator.Pitcher).PitchCount;
            Assert.That(now - previous, Is.InRange(3, 5), "한 타석 = 작전 전 2구 + 결정구 1~3구");

            double fresh = new MatchupCalculator(players).HitProbability("home_b3", FixtureGenerator.Pitcher, Empty);
            source.Before = 100 - (now - 80); // 다음 판정 시점에 정확히 100구
            Assert.That(source.StateOf(FixtureGenerator.Pitcher).Level, Is.EqualTo(FatigueLevel.Exhausted));
            Assert.That(calc.HitProbability("home_b3", FixtureGenerator.Pitcher, Empty), Is.EqualTo(fresh * 1.10).Within(Eps),
                "플래툰 등 기존 보정 위에 고갈 +10%");
        }

        [Test]
        public void 수비_반_이닝은_0아웃_주자_없음으로_시작하고_작전은_기본_진행뿐이다()
        {
            var f = FixtureGenerator.CreateTactics(new ClutchSituation(2, BaseState.Empty), 2, 2);
            var path = PlayByPlayGenerator.UntilPlateAppearanceEnd(f.Branch.PathFor(f.Events, TacticKind.SwingAway, TacticOutcome.Failure),
                f.Branch.AfterIndex + 1);
            Assert.That(path.Last().OutsAfter, Is.EqualTo(3));
            var gen = new PlayByPlayGenerator(null, new ScriptedRandom(0.99));
            Assert.That(gen.Next(path), Is.Null, "공격은 3아웃으로 끝");

            var (inning, half) = PlayByPlayGenerator.NextHalf(8, HalfInning.Bottom);
            Assert.That((inning, half), Is.EqualTo((9, HalfInning.Top)));
            Assert.That(PlayByPlayGenerator.NextHalf(9, HalfInning.Top), Is.EqualTo((9, HalfInning.Bottom)));

            var segment = gen.StartHalf(path, inning, half, "away_b4", "home_sp", userBatting: false);
            Assert.That(segment.Events.Select(e => (e.Inning, e.HalfInning, e.OutsBefore, e.PitcherId, e.BatterId)),
                Is.All.EqualTo((9, HalfInning.Top, 0, "home_sp", "away_b4")));
            Assert.That(segment.Events[0].RunnersBefore, Is.EqualTo(BaseState.Empty));
            Assert.That(segment.Branch.Tactics, Is.EqualTo(new[] { TacticKind.Default }), "수비는 강공·스퀴즈 없음");
            Assert.That(segment.Branch.AfterIndex, Is.EqualTo(path.Count + 1));

            // 수비 반 이닝을 끝까지 돌린다: 다음 타자는 상대 타순, 투수는 그대로, 모든 경로는 규칙을 지킨다.
            var played = path.Concat(segment.Events).ToList();
            var branch = segment.Branch;
            int guard = 0;
            while (branch != null)
            {
                Assert.That(++guard, Is.LessThanOrEqualTo(PlayByPlayGenerator.MaxPlateAppearancesPerHalf));
                played = PlayByPlayGenerator.UntilPlateAppearanceEnd(branch.PathFor(played, TacticKind.Default), branch.AfterIndex + 1);
                var next = gen.Next(played, "home_sp", userBatting: false);
                if (next != null)
                {
                    Assert.That(next.Branch.Tactics, Is.EqualTo(new[] { TacticKind.Default }));
                    played.AddRange(next.Events);
                }
                branch = next?.Branch;
            }
            Assert.That(PitchEventValidator.ValidateSequence(played), Is.Empty);
            var ninth = played.Where(e => e.Inning == 9).ToList();
            Assert.That(ninth.Select(e => e.PitcherId).Distinct(), Is.EqualTo(new[] { "home_sp" }));
            Assert.That(ninth.Select(e => e.BatterId).Distinct(), Is.EqualTo(new[] { "away_b4", "away_b5", "away_b6" }), "0.99 → 삼자 범퇴");
            Assert.That(ninth.Last().OutsAfter, Is.EqualTo(3));
        }

        [Test]
        public void 작전_대기_중_투수를_바꾸면_남은_투구는_새_투수가_던진다()
        {
            var f = FixtureGenerator.CreateTactics(new ClutchSituation(2, BaseState.Empty), 2, 2);
            var path = PlayByPlayGenerator.UntilPlateAppearanceEnd(f.Branch.PathFor(f.Events, TacticKind.SwingAway, TacticOutcome.Failure),
                f.Branch.AfterIndex + 1);
            var gen = new PlayByPlayGenerator(null, new ScriptedRandom(0.0)); // 출루 → 볼넷/안타
            var segment = gen.StartHalf(path, 9, HalfInning.Top, "away_b1", "home_sp", userBatting: false);
            var played = path.Concat(segment.Events).ToList();

            var relief = gen.Relieve(played, "home_rp1", userBatting: false);
            Assert.That(relief.AfterIndex, Is.EqualTo(segment.Branch.AfterIndex));
            Assert.That(relief.Tactics, Is.EqualTo(new[] { TacticKind.Default }));
            var full = relief.PathFor(played, TacticKind.Default);
            var pa = full.Skip(path.Count).ToList();
            Assert.That(pa.Take(2).Select(e => e.PitcherId), Is.All.EqualTo("home_sp"), "이미 던진 2구는 그대로");
            Assert.That(pa.Skip(2).Select(e => e.PitcherId), Is.All.EqualTo("home_rp1"));
            Assert.That(pa.Skip(2), Is.Not.Empty);
            Assert.That(PitchEventValidator.ValidateSequence(full), Is.Empty);

            // 다음 타자도 새 투수
            var cut = PlayByPlayGenerator.UntilPlateAppearanceEnd(full, relief.AfterIndex + 1);
            var next = gen.Next(cut, "home_rp1", userBatting: false);
            Assert.That(next.Events.Select(e => e.PitcherId), Is.All.EqualTo("home_rp1"));
            Assert.That(next.BatterId, Is.EqualTo("away_b2"));

            Assert.Throws<ArgumentException>(() => gen.Relieve(cut, "home_rp2", false), "타석이 끝난 뒤(1-1 아님)는 교체 분기 불가");
        }

        [TestCase(9, HalfInning.Bottom, 3, 4, true)]
        [TestCase(9, HalfInning.Bottom, 4, 4, false)]
        [TestCase(9, HalfInning.Top, 3, 4, false)]
        [TestCase(8, HalfInning.Bottom, 3, 4, false)]
        [TestCase(10, HalfInning.Bottom, 2, 5, true)]
        public void 홈이_앞서면_9회_이후_말_공격은_없거나_끝내기로_끝난다(int inning, HalfInning half, int away, int home, bool expected)
        {
            Assert.That(PlayByPlayGenerator.HomeWinsInBottom(inning, half, away, home), Is.EqualTo(expected));
        }

        static TeamRoster Roster()
        {
            PlayerData P(string id, RosterRole role, int order, string pos) =>
                new PlayerData { Id = id, Name = id, Team = "가", Role = role, Order = order, Position = pos, Era = 3.5 };
            return new TeamRoster("가",
                Enumerable.Range(1, 9).Select(i => P("b" + i, RosterRole.Lineup, i, "CF")).ToList(),
                new[] { P("ph1", RosterRole.Bench, 1, "대타"), P("pr1", RosterRole.Bench, 2, "대주자") },
                new[] { P("sp", RosterRole.Starter, 1, "선발"), P("sp2", RosterRole.Starter, 2, "선발") },
                new[] { P("rp1", RosterRole.Bullpen, 1, "마무리"), P("rp2", RosterRole.Bullpen, 2, "셋업"), P("rp3", RosterRole.Bullpen, 3, "좌완") });
        }

        [Test]
        public void 불펜_후보에는_투수만_있고_야수와_다른_선발은_없다()
        {
            var game = InGameRoster.From(Roster());
            Assert.That(game.CurrentPitcherId, Is.EqualTo("sp"));
            Assert.That(game.Pool.Select(p => p.Id), Is.EqualTo(new[] { "ph1", "pr1", "rp1", "rp2", "rp3" }), "벤치·불펜 대기 명단");
            Assert.That(game.ReliefCandidates.Select(p => p.Id), Is.EqualTo(new[] { "rp1", "rp2", "rp3" }));
            Assert.That(game.ReliefCandidates.All(p => p.IsPitcher), Is.True);
            Assert.That(game.CanBringIn("ph1", out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("투수가 아닌 선수"));
            Assert.That(game.BringIn("pr1"), Is.False, "야수는 마운드에 못 올린다");
            Assert.That(game.CanBringIn("sp2", out reason), Is.False, "로테이션 선발은 대기 명단이 아니다");
            Assert.That(reason, Is.EqualTo("대기 명단에 없는 선수"));
            Assert.That(game.CurrentPitcherId, Is.EqualTo("sp"), "실패하면 그대로");
        }

        [Test]
        public void 강판된_투수는_재등판할_수_없다()
        {
            var game = InGameRoster.From(Roster());
            Assert.That(game.BringIn("rp1"), Is.True);
            Assert.That(game.CurrentPitcherId, Is.EqualTo("rp1"));
            Assert.That(game.UsedPitchers, Is.EqualTo(new[] { "sp" }));
            Assert.That(game.ReliefCandidates.Select(p => p.Id), Is.EqualTo(new[] { "rp2", "rp3" }), "지금 투수 제외");
            Assert.That(game.CanBringIn("rp1", out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("이미 마운드에 있는 투수"));

            Assert.That(game.BringIn("rp2"), Is.True);
            Assert.That(game.UsedPitchers, Is.EqualTo(new[] { "sp", "rp1" }));
            Assert.That(game.CanBringIn("rp1", out reason), Is.False, "강판된 구원 투수 재등판 불가");
            Assert.That(reason, Is.EqualTo("이미 강판된 투수 (재등판 불가)"));
            Assert.That(game.BringIn("rp1"), Is.False);
            Assert.That(game.CanBringIn("sp"), Is.False, "강판된 선발도 불가");
            Assert.That(game.BullpenPitchers.Select(p => (p.Id, game.IsUsed(p.Id))),
                Is.EqualTo(new[] { ("rp1", true), ("rp3", false) }), "목록: 강판 표시 + 지금 투수 제외");
            Assert.That(game.CurrentPitcherId, Is.EqualTo("rp2"));
        }

        [Test]
        public void 인게임_교체는_원본_선수단을_바꾸지_않는다()
        {
            var roster = Roster();
            var bullpen = roster.Bullpen.ToList();
            var game = InGameRoster.From(roster);
            game.BringIn("rp1");
            game.Pool.First(p => p.Id == "rp2").Role = RosterRole.Starter; // 사본을 고쳐도

            Assert.That(roster.StartingPitcher.Id, Is.EqualTo("sp"), "선발 로테이션 그대로");
            Assert.That(roster.Bullpen, Is.EqualTo(bullpen), "같은 선수 객체·순서");
            Assert.That(roster.Bullpen.Select(p => p.Role), Is.All.EqualTo(RosterRole.Bullpen));
            Assert.That(game.Pool.Any(p => roster.Bullpen.Contains(p)), Is.False, "대기 명단은 사본");
            Assert.Throws<ArgumentException>(() => InGameRoster.From(new TeamRoster("나", null, null, null, null)), "선발 없음");
        }

        [Test]
        public void 아홉회초_선두_타자는_퀵_시뮬_기록으로_정해지고_같은_경기면_같다()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var a = FixtureGenerator.Generate(null, null, new SeededRandom(seed));
                var b = FixtureGenerator.Generate(null, null, new SeededRandom(seed));
                Assert.That(a.AwayNinthLeadoffOrder, Is.InRange(1, 9));
                Assert.That(a.AwayNinthLeadoffOrder, Is.EqualTo(b.AwayNinthLeadoffOrder));
                Assert.That(a.AwayNinthLeadoffOrder,
                    Is.EqualTo((24 + a.AwayHits + a.AwayWalks + a.HomeErrors) % 9 + 1));
            }
        }
    }
}
