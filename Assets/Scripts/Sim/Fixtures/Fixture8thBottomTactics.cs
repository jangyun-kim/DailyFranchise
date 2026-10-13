using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Fixtures
{
    /// <summary>
    /// 작전 분기점. AfterIndex 투구까지 재생한 뒤 멈추고, 고른 작전의 후속 투구를 이어 붙인다.
    /// </summary>
    public sealed class BranchPoint
    {
        /// <summary>이 인덱스의 투구를 보여준 뒤 작전을 기다린다. 기본 구간 길이 = AfterIndex + 1.</summary>
        public int AfterIndex;
        /// <summary>작전별 후속 투구. 호출할 때마다 새 이벤트를 만든다.</summary>
        public IReadOnlyDictionary<TacticKind, Func<IReadOnlyList<PitchEvent>>> Continuations;

        public IEnumerable<TacticKind> Tactics => Continuations.Keys;

        /// <summary>기본 구간 + 작전 후속 구간. 기본 구간 이벤트 객체는 그대로 이어 쓴다.</summary>
        public List<PitchEvent> PathFor(IReadOnlyList<PitchEvent> baseEvents, TacticKind tactic)
        {
            if (baseEvents.Count != AfterIndex + 1)
                throw new ArgumentException($"기본 구간 길이 {baseEvents.Count} ≠ 분기점 {AfterIndex} + 1");
            if (!Continuations.TryGetValue(tactic, out var make))
                throw new ArgumentException($"이 분기점에 없는 작전: {tactic}", nameof(tactic));
            return baseEvents.Concat(make()).ToList();
        }
    }

    /// <summary>
    /// 8회말 1사 1·3루 작전 승부처. Fixture8thBottom의 1~2구(볼카운트 1-1)까지가 기본 구간이고,
    /// 그 뒤 작전에 따라 분기한다.
    /// - 기본 진행 / 강공: Fixture8thBottom의 3~8구 그대로 (경계 스트라이크 → 파울 → 우전 적시타 → 다음 타자 삼진).
    /// - 스퀴즈 번트: 3구째 스퀴즈 성공(3루 주자 득점, 1루 주자 2루, 타자 1루 아웃) → 다음 타자 유격수 직선타로 이닝 종료.
    /// </summary>
    public static class Fixture8thBottomTactics
    {
        public const int DecisionAfterIndex = 1;

        public static ScenarioFixture Create()
        {
            var original = Fixture8thBottom.Create();
            return new ScenarioFixture
            {
                Id = ScenarioCatalog.EighthBottomTactics,
                Title = "8회말 · 1사 1·3루 · 작전 승부처",
                AwayScoreBefore = original.AwayScoreBefore,
                HomeScoreBefore = original.HomeScoreBefore,
                ZoneRule = original.ZoneRule,
                Events = original.Events.Take(DecisionAfterIndex + 1).ToList(),
                Branch = new BranchPoint
                {
                    AfterIndex = DecisionAfterIndex,
                    Continuations = new Dictionary<TacticKind, Func<IReadOnlyList<PitchEvent>>>
                    {
                        [TacticKind.Default] = HitContinuation,
                        [TacticKind.SwingAway] = HitContinuation,
                        [TacticKind.SqueezeBunt] = SqueezeContinuation,
                    },
                },
            };
        }

        /// <summary>기존 Fixture8thBottom의 3~8구.</summary>
        public static IReadOnlyList<PitchEvent> HitContinuation() =>
            Fixture8thBottom.Create().Events.Skip(DecisionAfterIndex + 1).ToList();

        public static IReadOnlyList<PitchEvent> SqueezeContinuation()
        {
            var rule = ZoneRuleConfig.ProvisionalKbo;
            var zone1 = rule.ZoneFor(Fixture8thBottom.Batter1HeightCm);
            var zone2 = rule.ZoneFor(Fixture8thBottom.Batter2HeightCm);
            const string pitcher = Fixture8thBottom.Pitcher;
            var corners = new BaseState(Fixture8thBottom.RunnerOnFirst, null, Fixture8thBottom.RunnerOnThird);
            var second = new BaseState(null, Fixture8thBottom.RunnerOnFirst, null);

            return new List<PitchEvent>
            {
                // 3. 스퀴즈 번트: 1-1에서 1루 쪽으로 대어 3루 주자 홈인
                new PitchEvent
                {
                    Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = 1,
                    CountBefore = new Count(1, 1),
                    PitcherId = pitcher, BatterId = Fixture8thBottom.Batter1,
                    PitchType = "포심", VelocityKmh = 145,
                    LocationX = 6, LocationY = 70, Zone = zone1,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = "스퀴즈 번트 성공 — 3루 주자 득점, 1루 주자 2루, 타자 1루 아웃",
                    PlayType = PlayType.SacrificeBunt, SprayAngleDeg = 12,
                    RunnersBefore = corners, RunnersAfter = second,
                    RunsScored = 1, OutsAfter = 2,
                },
                // 4~6. 다음 타자: 볼 → 파울 → 유격수 직선타 (이닝 종료)
                FixtureBuilder.Pitch(8, HalfInning.Bottom, pitcher, Fixture8thBottom.Batter2, zone2, "슬라이더", 133, -27, 52,
                    PitchResult.Ball, false, new Count(0, 0), new Count(1, 0), second, 2),
                FixtureBuilder.Pitch(8, HalfInning.Bottom, pitcher, Fixture8thBottom.Batter2, zone2, "포심", 147, 10, 90,
                    PitchResult.Foul, true, new Count(1, 0), new Count(1, 1), second, 2),
                new PitchEvent
                {
                    Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = 2,
                    CountBefore = new Count(1, 1),
                    PitcherId = pitcher, BatterId = Fixture8thBottom.Batter2,
                    PitchType = "체인지업", VelocityKmh = 129,
                    LocationX = -6, LocationY = 58, Zone = zone2,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = "유격수 직선타 — 이닝 종료, 2루 잔루",
                    PlayType = PlayType.LineOut, SprayAngleDeg = -20,
                    RunnersBefore = second, RunnersAfter = second,
                    RunsScored = 0, OutsAfter = 3,
                },
            };
        }
    }
}
