using System.Collections.Generic;

namespace DailyFranchise.Sim.Fixtures
{
    public sealed class ScenarioFixture
    {
        /// <summary>ScenarioCatalog의 시나리오 ID.</summary>
        public string Id;
        public string Title;
        public int AwayScoreBefore;
        public int HomeScoreBefore;
        public ZoneRuleConfig ZoneRule;
        public IReadOnlyList<PitchEvent> Events;
    }

    /// <summary>
    /// 테스트 장면: 8회말, 원정 3 : 홈 2, 1사 1·3루.
    /// 1타석(1~5구): 중앙 스트라이크 → 바깥쪽 낮은 볼 → 경계 스트라이크 → 파울 → 인플레이 적시타(동점).
    /// 2타석(6~8구): 삼진 시나리오. 삼진은 최소 3구가 필요하므로 별도 타석으로 구성했다.
    /// 선수 ID는 가상의 값이다.
    /// </summary>
    public static class Fixture8thBottom
    {
        public const string Pitcher = "away_sp";
        public const string Batter1 = "home_b3";
        public const string Batter2 = "home_b4";
        public const string RunnerOnFirst = "home_b1";
        public const string RunnerOnThird = "home_b2";

        public const double Batter1HeightCm = 183;
        public const double Batter2HeightCm = 178;

        public static ScenarioFixture Create()
        {
            var rule = ZoneRuleConfig.ProvisionalKbo;
            var zone1 = rule.ZoneFor(Batter1HeightCm);
            var zone2 = rule.ZoneFor(Batter2HeightCm);

            var corners = new BaseState(RunnerOnFirst, null, RunnerOnThird);
            var afterHit = new BaseState(Batter1, RunnerOnFirst, null);

            var events = new List<PitchEvent>
            {
                // 1. 존 중앙 스트라이크
                Pitch(Batter1, zone1, "포심", 146, 0, 77, PitchResult.CalledStrike, true,
                    new Count(0, 0), new Count(0, 1), corners, 1),
                // 2. 바깥쪽 낮은 볼
                Pitch(Batter1, zone1, "슬라이더", 134, -30, 42, PitchResult.Ball, false,
                    new Count(0, 1), new Count(1, 1), corners, 1),
                // 3. 경계 스트라이크 (존 우측 경계 안쪽 0.79cm)
                Pitch(Batter1, zone1, "체인지업", 128, 22.8, 60, PitchResult.CalledStrike, true,
                    new Count(1, 1), new Count(1, 2), corners, 1),
                // 4. 파울 (2스트라이크 이후 카운트 유지)
                Pitch(Batter1, zone1, "포심", 147, 8, 95, PitchResult.Foul, true,
                    new Count(1, 2), new Count(1, 2), corners, 1),
                // 5. 인플레이 적시타: 3루 주자 득점, 1루 주자 2루, 타자 1루
                new PitchEvent
                {
                    Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = 1,
                    CountBefore = new Count(1, 2),
                    PitcherId = Pitcher, BatterId = Batter1,
                    PitchType = "슬라이더", VelocityKmh = 135,
                    LocationX = -5, LocationY = 70, Zone = zone1,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 2), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = "우전 적시타 — 3루 주자 득점, 1루 주자 2루 진루",
                    PlayType = PlayType.Single, SprayAngleDeg = 25,
                    RunnersBefore = corners, RunnersAfter = afterHit,
                    RunsScored = 1, OutsAfter = 1,
                },
                // 6. 새 타석 초구 스트라이크
                Pitch(Batter2, zone2, "포심", 148, 15, 85, PitchResult.CalledStrike, true,
                    new Count(0, 0), new Count(0, 1), afterHit, 1),
                // 7. 존 밖 공에 헛스윙 (IsStrikeByRule=false지만 스트라이크)
                Pitch(Batter2, zone2, "슬라이더", 133, -28, 55, PitchResult.SwingingStrike, false,
                    new Count(0, 1), new Count(0, 2), afterHit, 1),
                // 8. 낮은 바깥쪽 헛스윙 삼진
                new PitchEvent
                {
                    Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = 1,
                    CountBefore = new Count(0, 2),
                    PitcherId = Pitcher, BatterId = Batter2,
                    PitchType = "포크", VelocityKmh = 131,
                    LocationX = 25.5, LocationY = 40, Zone = zone2,
                    Result = PitchResult.SwingingStrike, IsStrikeByRule = false,
                    CountAfter = new Count(0, 3), Outcome = PlateAppearanceOutcome.Strikeout,
                    RunnersBefore = afterHit, RunnersAfter = afterHit,
                    RunsScored = 0, OutsAfter = 2,
                },
            };

            return new ScenarioFixture
            {
                Id = ScenarioCatalog.EighthBottom,
                Title = "8회말 · 1사 1·3루 · 한 점 차",
                AwayScoreBefore = 3,
                HomeScoreBefore = 2,
                ZoneRule = rule,
                Events = events,
            };
        }

        /// <summary>타석을 끝내지 않는 투구 (주자·아웃 변화 없음).</summary>
        static PitchEvent Pitch(string batter, StrikeZone zone, string type, double kmh,
            double x, double y, PitchResult result, bool isStrike,
            Count before, Count after, BaseState runners, int outs)
        {
            return new PitchEvent
            {
                Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = outs,
                CountBefore = before,
                PitcherId = Pitcher, BatterId = batter,
                PitchType = type, VelocityKmh = kmh,
                LocationX = x, LocationY = y, Zone = zone,
                Result = result, IsStrikeByRule = isStrike,
                CountAfter = after, Outcome = PlateAppearanceOutcome.None,
                RunnersBefore = runners, RunnersAfter = runners,
                RunsScored = 0, OutsAfter = outs,
            };
        }
    }
}
