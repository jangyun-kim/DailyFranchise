using System;
using System.Collections.Generic;

namespace DailyFranchise.Sim.Fixtures
{
    public sealed class ScenarioEntry
    {
        public string Id;
        /// <summary>디버그 전환기에 표시할 짧은 이름.</summary>
        public string Label;
        public Func<ScenarioFixture> Create;
    }

    /// <summary>재생·검증에 쓰는 고정 시나리오 목록. 첫 항목이 기본 장면이다.</summary>
    public static class ScenarioCatalog
    {
        public const string EighthBottom = "8th-bottom";
        public const string HomeRun = "home-run";
        public const string DoublePlay = "double-play";
        public const string InningEnd = "inning-end";
        public const string EighthBottomTactics = "8th-bottom-tactics";

        public static readonly IReadOnlyList<ScenarioEntry> All = new[]
        {
            new ScenarioEntry { Id = EighthBottom, Label = "8회말 적시타", Create = Fixture8thBottom.Create },
            new ScenarioEntry { Id = HomeRun, Label = "홈런", Create = FixtureHomeRun.Create },
            new ScenarioEntry { Id = DoublePlay, Label = "병살타", Create = FixtureDoublePlay.Create },
            new ScenarioEntry { Id = InningEnd, Label = "이닝 교대", Create = FixtureInningEnd.Create },
            new ScenarioEntry { Id = EighthBottomTactics, Label = "작전 승부처", Create = Fixture8thBottomTactics.Create },
        };

        public static ScenarioEntry Find(string id)
        {
            foreach (var e in All)
                if (e.Id == id) return e;
            throw new ArgumentException($"알 수 없는 시나리오: {id}", nameof(id));
        }
    }

    /// <summary>시나리오 Fixture 작성 도우미.</summary>
    internal static class FixtureBuilder
    {
        /// <summary>타석을 끝내지 않는 투구 (주자·아웃 변화 없음).</summary>
        public static PitchEvent Pitch(int inning, HalfInning half, string pitcher, string batter, StrikeZone zone,
            string type, double kmh, double x, double y, PitchResult result, bool isStrike,
            Count before, Count after, BaseState runners, int outs)
        {
            return new PitchEvent
            {
                Inning = inning, HalfInning = half, OutsBefore = outs,
                CountBefore = before,
                PitcherId = pitcher, BatterId = batter,
                PitchType = type, VelocityKmh = kmh,
                LocationX = x, LocationY = y, Zone = zone,
                Result = result, IsStrikeByRule = isStrike,
                CountAfter = after, Outcome = PlateAppearanceOutcome.None,
                RunnersBefore = runners, RunnersAfter = runners,
                RunsScored = 0, OutsAfter = outs,
            };
        }
    }

    /// <summary>
    /// 7회말 1사 1루, 원정 4 : 홈 2. 3구째 좌중간 투런 홈런으로 동점.
    /// </summary>
    public static class FixtureHomeRun
    {
        public const string Pitcher = "away_rp1";
        public const string Batter = "home_b5";
        public const string RunnerOnFirst = "home_b4";
        public const double BatterHeightCm = 180;

        public static ScenarioFixture Create()
        {
            var rule = ZoneRuleConfig.ProvisionalKbo;
            var zone = rule.ZoneFor(BatterHeightCm);
            var first = new BaseState(RunnerOnFirst, null, null);
            const int inning = 7;
            const HalfInning half = HalfInning.Bottom;

            return new ScenarioFixture
            {
                Id = ScenarioCatalog.HomeRun,
                Title = "7회말 · 1사 1루 · 두 점 차",
                AwayScoreBefore = 4,
                HomeScoreBefore = 2,
                ZoneRule = rule,
                Events = new List<PitchEvent>
                {
                    FixtureBuilder.Pitch(inning, half, Pitcher, Batter, zone, "슬라이더", 136, -26, 48,
                        PitchResult.Ball, false, new Count(0, 0), new Count(1, 0), first, 1),
                    FixtureBuilder.Pitch(inning, half, Pitcher, Batter, zone, "포심", 149, 6, 88,
                        PitchResult.CalledStrike, true, new Count(1, 0), new Count(1, 1), first, 1),
                    new PitchEvent
                    {
                        Inning = inning, HalfInning = half, OutsBefore = 1,
                        CountBefore = new Count(1, 1),
                        PitcherId = Pitcher, BatterId = Batter,
                        PitchType = "포심", VelocityKmh = 148,
                        LocationX = -4, LocationY = 92, Zone = zone,
                        Result = PitchResult.InPlay, IsStrikeByRule = true,
                        CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                        PlayResult = "좌중간 담장을 넘기는 투런 홈런",
                        PlayType = PlayType.HomeRun, SprayAngleDeg = -18,
                        RunnersBefore = first, RunnersAfter = BaseState.Empty,
                        RunsScored = 2, OutsAfter = 1,
                    },
                },
            };
        }
    }

    /// <summary>
    /// 6회초 무사 1루, 원정 1 : 홈 3. 3구째 유격수 땅볼 병살타 (6-4-3)로 2사 주자 없음.
    /// </summary>
    public static class FixtureDoublePlay
    {
        public const string Pitcher = "home_sp";
        public const string Batter = "away_b5";
        public const string RunnerOnFirst = "away_b4";
        public const double BatterHeightCm = 176;

        public static ScenarioFixture Create()
        {
            var rule = ZoneRuleConfig.ProvisionalKbo;
            var zone = rule.ZoneFor(BatterHeightCm);
            var first = new BaseState(RunnerOnFirst, null, null);
            const int inning = 6;
            const HalfInning half = HalfInning.Top;

            return new ScenarioFixture
            {
                Id = ScenarioCatalog.DoublePlay,
                Title = "6회초 · 무사 1루 · 두 점 차",
                AwayScoreBefore = 1,
                HomeScoreBefore = 3,
                ZoneRule = rule,
                Events = new List<PitchEvent>
                {
                    FixtureBuilder.Pitch(inning, half, Pitcher, Batter, zone, "투심", 145, 12, 60,
                        PitchResult.Foul, true, new Count(0, 0), new Count(0, 1), first, 0),
                    FixtureBuilder.Pitch(inning, half, Pitcher, Batter, zone, "커브", 121, -5, 40,
                        PitchResult.Ball, false, new Count(0, 1), new Count(1, 1), first, 0),
                    new PitchEvent
                    {
                        Inning = inning, HalfInning = half, OutsBefore = 0,
                        CountBefore = new Count(1, 1),
                        PitcherId = Pitcher, BatterId = Batter,
                        PitchType = "투심", VelocityKmh = 144,
                        LocationX = -14, LocationY = 55, Zone = zone,
                        Result = PitchResult.InPlay, IsStrikeByRule = true,
                        CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                        PlayResult = "유격수 땅볼 병살타 (6-4-3)",
                        PlayType = PlayType.GroundOut, SprayAngleDeg = -8,
                        RunnersBefore = first, RunnersAfter = BaseState.Empty,
                        RunsScored = 0, OutsAfter = 2,
                    },
                },
            };
        }
    }

    /// <summary>
    /// 8회말 2사 2루, 원정 5 : 홈 4. 3구째 우익수 뜬공으로 3아웃(잔루 1),
    /// 이어서 9회초 원정 공격 첫 타자에게 2구.
    /// </summary>
    public static class FixtureInningEnd
    {
        public const string AwayPitcher = "away_rp1";
        public const string HomeBatter = "home_b8";
        public const string RunnerOnSecond = "home_b7";
        public const string HomePitcher = "home_rp1";
        public const string AwayBatter = "away_b1";

        public static ScenarioFixture Create()
        {
            var rule = ZoneRuleConfig.ProvisionalKbo;
            var homeZone = rule.ZoneFor(175);
            var awayZone = rule.ZoneFor(182);
            var second = new BaseState(null, RunnerOnSecond, null);

            return new ScenarioFixture
            {
                Id = ScenarioCatalog.InningEnd,
                Title = "8회말 · 2사 2루 · 한 점 차 → 9회초",
                AwayScoreBefore = 5,
                HomeScoreBefore = 4,
                ZoneRule = rule,
                Events = new List<PitchEvent>
                {
                    FixtureBuilder.Pitch(8, HalfInning.Bottom, AwayPitcher, HomeBatter, homeZone, "슬라이더", 134, 20, 62,
                        PitchResult.SwingingStrike, true, new Count(0, 0), new Count(0, 1), second, 2),
                    FixtureBuilder.Pitch(8, HalfInning.Bottom, AwayPitcher, HomeBatter, homeZone, "포심", 147, 28, 96,
                        PitchResult.Ball, false, new Count(0, 1), new Count(1, 1), second, 2),
                    new PitchEvent
                    {
                        Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = 2,
                        CountBefore = new Count(1, 1),
                        PitcherId = AwayPitcher, BatterId = HomeBatter,
                        PitchType = "포심", VelocityKmh = 148,
                        LocationX = 3, LocationY = 80, Zone = homeZone,
                        Result = PitchResult.InPlay, IsStrikeByRule = true,
                        CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                        PlayResult = "우익수 뜬공 — 이닝 종료, 2루 잔루",
                        PlayType = PlayType.FlyOut, SprayAngleDeg = 30,
                        RunnersBefore = second, RunnersAfter = second,
                        RunsScored = 0, OutsAfter = 3,
                    },
                    FixtureBuilder.Pitch(9, HalfInning.Top, HomePitcher, AwayBatter, awayZone, "포심", 151, -2, 85,
                        PitchResult.CalledStrike, true, new Count(0, 0), new Count(0, 1), BaseState.Empty, 0),
                    FixtureBuilder.Pitch(9, HalfInning.Top, HomePitcher, AwayBatter, awayZone, "포크", 133, 5, 38,
                        PitchResult.Ball, false, new Count(0, 1), new Count(1, 1), BaseState.Empty, 0),
                },
            };
        }
    }
}
