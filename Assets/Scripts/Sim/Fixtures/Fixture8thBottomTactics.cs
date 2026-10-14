using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Fixtures
{
    /// <summary>
    /// 작전 하나의 후속 구간. 실패 구간이 없으면 항상 성공 구간으로 이어진다 (기본 진행).
    /// </summary>
    public sealed class TacticBranch
    {
        /// <summary>성공 확률 (0~1). 실패 구간이 없으면 쓰지 않는다.</summary>
        public double SuccessRate = 1.0;
        /// <summary>성공 시 후속 투구. 호출할 때마다 새 이벤트를 만든다.</summary>
        public Func<IReadOnlyList<PitchEvent>> Success;
        /// <summary>실패 시 후속 투구. null이면 확률 판정 없이 항상 성공.</summary>
        public Func<IReadOnlyList<PitchEvent>> Failure;

        public bool IsGamble => Failure != null;

        public Func<IReadOnlyList<PitchEvent>> For(TacticOutcome outcome)
        {
            if (outcome == TacticOutcome.Success) return Success;
            return Failure ?? throw new ArgumentException("실패 구간이 없는 작전", nameof(outcome));
        }
    }

    /// <summary>
    /// 작전 분기점. AfterIndex 투구까지 재생한 뒤 멈추고, 고른 작전의 결과(성공/실패) 후속 투구를 이어 붙인다.
    /// </summary>
    public sealed class BranchPoint
    {
        /// <summary>이 인덱스의 투구를 보여준 뒤 작전을 기다린다. 기본 구간 길이 = AfterIndex + 1.</summary>
        public int AfterIndex;
        public IReadOnlyDictionary<TacticKind, TacticBranch> Options;

        public IEnumerable<TacticKind> Tactics => Options.Keys;

        public bool Offers(TacticKind tactic) => Options.ContainsKey(tactic);

        public TacticBranch this[TacticKind tactic] =>
            Options.TryGetValue(tactic, out var branch) ? branch
                : throw new ArgumentException($"이 분기점에 없는 작전: {tactic}", nameof(tactic));

        /// <summary>
        /// 작전 결과를 정한다. 확률 작전만 난수를 하나 소비한다 (roll &lt; SuccessRate → 성공).
        /// </summary>
        public TacticOutcome Resolve(TacticKind tactic, IRandomSource random) => Resolve(tactic, random, this[tactic].SuccessRate);

        /// <summary>
        /// 바깥에서 계산한 성공률(예: <see cref="MatchupCalculator"/>)로 작전 결과를 정한다.
        /// 확률 작전만 난수를 하나 소비한다 (roll &lt; successRate → 성공). 성공률은 [0, 1]이어야 한다.
        /// </summary>
        public TacticOutcome Resolve(TacticKind tactic, IRandomSource random, double successRate)
        {
            var branch = this[tactic];
            if (!branch.IsGamble) return TacticOutcome.Success;
            if (double.IsNaN(successRate) || successRate < 0 || successRate > 1)
                throw new ArgumentOutOfRangeException(nameof(successRate), successRate, "성공률은 0~1");
            if (random == null) throw new ArgumentNullException(nameof(random));
            return random.NextDouble() < successRate ? TacticOutcome.Success : TacticOutcome.Failure;
        }

        /// <summary>기본 구간 + 작전 결과 후속 구간. 기본 구간 이벤트 객체는 그대로 이어 쓴다.</summary>
        public List<PitchEvent> PathFor(IReadOnlyList<PitchEvent> baseEvents, TacticKind tactic,
            TacticOutcome outcome = TacticOutcome.Success)
        {
            if (baseEvents.Count != AfterIndex + 1)
                throw new ArgumentException($"기본 구간 길이 {baseEvents.Count} ≠ 분기점 {AfterIndex} + 1");
            return baseEvents.Concat(this[tactic].For(outcome)()).ToList();
        }
    }

    /// <summary>
    /// 8회말 1사 1·3루 작전 승부처. Fixture8thBottom의 1~2구(볼카운트 1-1)까지가 기본 구간이고,
    /// 그 뒤 작전에 따라 분기한다.
    /// - 기본 진행(확률 없음) / 강공 성공: Fixture8thBottom의 3~8구 그대로 (경계 스트라이크 → 파울 → 우전 적시타 → 다음 타자 삼진).
    /// - 강공 실패: 3구째 유격수 땅볼 병살(6-4-3)로 이닝 종료, 무득점.
    /// - 스퀴즈 성공: 3구째 스퀴즈(3루 주자 득점, 1루 주자 2루, 타자 1루 아웃) → 다음 타자 유격수 직선타로 이닝 종료.
    /// - 스퀴즈 실패: 3구째 번트가 포수 뜬공, 스타트 끊은 3루 주자가 귀루하지 못해 더블 아웃, 무득점 이닝 종료.
    /// </summary>
    public static class Fixture8thBottomTactics
    {
        public const int DecisionAfterIndex = 1;
        public const double SwingAwaySuccessRate = 0.5;
        public const double SqueezeSuccessRate = 0.6;

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
                    Options = new Dictionary<TacticKind, TacticBranch>
                    {
                        [TacticKind.Default] = new TacticBranch { Success = HitContinuation },
                        [TacticKind.SwingAway] = new TacticBranch
                        {
                            SuccessRate = SwingAwaySuccessRate,
                            Success = HitContinuation,
                            Failure = SwingAwayFailedContinuation,
                        },
                        [TacticKind.SqueezeBunt] = new TacticBranch
                        {
                            SuccessRate = SqueezeSuccessRate,
                            Success = SqueezeContinuation,
                            Failure = SqueezeFailedContinuation,
                        },
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

        /// <summary>강공 실패: 1-1에서 유격수 땅볼 병살. 1루 주자·타자 아웃, 3루 주자는 묶여 잔루.</summary>
        public static IReadOnlyList<PitchEvent> SwingAwayFailedContinuation()
        {
            var zone1 = ZoneRuleConfig.ProvisionalKbo.ZoneFor(Fixture8thBottom.Batter1HeightCm);
            var corners = new BaseState(Fixture8thBottom.RunnerOnFirst, null, Fixture8thBottom.RunnerOnThird);
            return new List<PitchEvent>
            {
                new PitchEvent
                {
                    Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = 1,
                    CountBefore = new Count(1, 1),
                    PitcherId = Fixture8thBottom.Pitcher, BatterId = Fixture8thBottom.Batter1,
                    PitchType = "싱커", VelocityKmh = 143,
                    LocationX = 9, LocationY = 54, Zone = zone1,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = "강공 실패 — 유격수 땅볼 병살(6-4-3), 이닝 종료, 3루 잔루",
                    PlayType = PlayType.GroundOut, SprayAngleDeg = -14,
                    RunnersBefore = corners, RunnersAfter = new BaseState(null, null, Fixture8thBottom.RunnerOnThird),
                    RunsScored = 0, OutsAfter = 3,
                },
            };
        }

        /// <summary>스퀴즈 실패: 번트가 포수 앞 뜬공, 홈으로 달리던 3루 주자 귀루 실패로 더블 아웃.</summary>
        public static IReadOnlyList<PitchEvent> SqueezeFailedContinuation()
        {
            var zone1 = ZoneRuleConfig.ProvisionalKbo.ZoneFor(Fixture8thBottom.Batter1HeightCm);
            var corners = new BaseState(Fixture8thBottom.RunnerOnFirst, null, Fixture8thBottom.RunnerOnThird);
            return new List<PitchEvent>
            {
                new PitchEvent
                {
                    Inning = 8, HalfInning = HalfInning.Bottom, OutsBefore = 1,
                    CountBefore = new Count(1, 1),
                    PitcherId = Fixture8thBottom.Pitcher, BatterId = Fixture8thBottom.Batter1,
                    PitchType = "포심", VelocityKmh = 148,
                    LocationX = 4, LocationY = 96, Zone = zone1,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = "스퀴즈 실패 — 번트 포수 뜬공, 3루 주자 귀루 못 해 더블 아웃, 이닝 종료",
                    PlayType = PlayType.PopOut, SprayAngleDeg = 6,
                    RunnersBefore = corners, RunnersAfter = new BaseState(Fixture8thBottom.RunnerOnFirst, null, null),
                    RunsScored = 0, OutsAfter = 3,
                },
            };
        }
    }
}
