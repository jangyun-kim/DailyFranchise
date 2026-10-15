using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.Sim.Fixtures
{
    /// <summary>승부처 시작 상황: 아웃 수(0~2)와 루상 주자.</summary>
    public readonly struct ClutchSituation : IEquatable<ClutchSituation>
    {
        public readonly int Outs;
        public readonly BaseState Runners;

        public ClutchSituation(int outs, BaseState runners)
        {
            if (outs < 0 || outs > 2) throw new ArgumentOutOfRangeException(nameof(outs), outs, "승부처 아웃 수는 0~2");
            Outs = outs;
            Runners = runners;
        }

        /// <summary>스퀴즈는 3루 주자가 있고 2사 전일 때만 고를 수 있다.</summary>
        public bool AllowsSqueeze => Runners.Third != null && Outs < 2;

        public bool Equals(ClutchSituation other) => Outs == other.Outs && Runners == other.Runners;
        public override bool Equals(object obj) => obj is ClutchSituation other && Equals(other);
        public override int GetHashCode() => Outs * 397 ^ Runners.GetHashCode();
        public override string ToString() => $"{FixtureGenerator.OutsText(Outs)} {Runners}";
    }

    /// <summary>퀵 시뮬레이션으로 만든 경기 하나: 1~7회 기록, 8회초, 8회말 승부처 상황과 장면.</summary>
    public sealed class GeneratedGame
    {
        /// <summary>1~7회 양 팀 기록.</summary>
        public QuickSimResult Early;
        /// <summary>승부처 직전 8회초 (원정 공격).</summary>
        public HalfInningLine EighthTop;
        public ClutchSituation Situation;
        /// <summary>8회말 작전 승부처 장면 (홈 공격, 내 구단 = 홈 기준).</summary>
        public ScenarioFixture Fixture;

        /// <summary>원정 1~8회 득점 (8회초 포함).</summary>
        public int[] AwayRunsByInning => Early.AwayRuns.Concat(new[] { EighthTop.Runs }).ToArray();
        /// <summary>홈 1~8회 득점. 8회말은 진행 중이라 0 (장면 득점은 화면이 더한다).</summary>
        public int[] HomeRunsByInning => Early.HomeRuns.Concat(new[] { 0 }).ToArray();

        public int AwayHits => Early.AwayHits + EighthTop.Hits;
        public int HomeHits => Early.HomeHits;
        public int AwayWalks => Early.AwayWalks + EighthTop.Walks;
        public int HomeWalks => Early.HomeWalks;
        public int AwayErrors => Early.AwayErrors;
        public int HomeErrors => Early.HomeErrors + EighthTop.Errors;

        public GameScore ScoreBefore => new GameScore(Fixture.AwayScoreBefore, Fixture.HomeScoreBefore);

        /// <summary>
        /// 9회초 원정 선두 타자 타순 (1~9). 퀵 시뮬레이션은 타순을 추적하지 않으므로 1~8회 원정 타석 수를
        /// 아웃 24개 + 안타 + 볼넷 + 홈 실책으로 어림해 이어 간다. 난수를 쓰지 않아 같은 경기면 늘 같다.
        /// </summary>
        public int AwayNinthLeadoffOrder =>
            (3 * FixtureGenerator.DecisionInning + AwayHits + AwayWalks + HomeErrors) % BattingOrder.Size + 1;
    }

    /// <summary>
    /// 퀵 시뮬레이션(<see cref="QuickSimEngine"/>) 결과와 무작위 승부처 상황으로 8회말 작전 승부처 장면을 만든다.
    /// 장면은 <see cref="Fixture8thBottomTactics"/>와 같은 틀(내 구단 = 홈, 3번 타자 타석, 1-1에서 작전)이고
    /// 아웃 수·주자·시작 점수만 바뀐다. 모든 경로는 <see cref="PitchEventValidator"/> 규칙을 지킨다.
    ///
    /// 난수 사용 순서: 1~7회 퀵 시뮬레이션 → 8회초 → 아웃 수 Next(3) → 주자 Next(8). 같은 Seed면 같은 경기.
    ///
    /// 작전 후속 구간 (R = 승부처 주자, O = 아웃):
    /// - 기본 진행 / 강공 성공: 경계 스트라이크 → 파울 → 우전 안타 (2·3루 주자 득점, 1루 주자 2루) → 다음 타자 삼진.
    /// - 강공 실패: 1루 주자가 있고 2사 전이면 유격수 병살(6-4-3, 다른 주자 그대로), 아니면 유격수 땅볼.
    /// - 스퀴즈(3루 주자·2사 전만): 성공 = 3루 주자 득점·다른 주자 한 루씩·타자 아웃 → 다음 타자 볼·파울·유격수 직선타.
    ///   실패 = 번트 포수 뜬공, 3루 주자 귀루 실패로 더블 아웃.
    /// </summary>
    public static class FixtureGenerator
    {
        /// <summary>생성 장면의 ID (ScenarioCatalog에는 없다).</summary>
        public const string ScenarioId = "generated";
        public const int SimulatedInnings = 7;
        public const int DecisionInning = 8;
        public const int DecisionAfterIndex = Fixture8thBottomTactics.DecisionAfterIndex;

        public const string Pitcher = Fixture8thBottom.Pitcher;
        public const string Batter1 = Fixture8thBottom.Batter1;
        public const string Batter2 = Fixture8thBottom.Batter2;

        /// <summary>루에 나가 있는 주자 ID. 낮은 루부터 직전 타자(2번) → 1번 → 9번 순으로 채운다.</summary>
        static readonly string[] RunnerIds = { "home_b2", "home_b1", "home_b9" };

        /// <summary>
        /// 경기를 만든다. 원정·홈은 장면 기준이다: 홈 = 8회말 승부처에서 공격하는 내 구단, 원정 = 상대.
        /// 선수단이 null이면 그 팀은 리그 평균으로 계산한다.
        /// </summary>
        public static GeneratedGame Generate(TeamRoster away, TeamRoster home, IRandomSource random, QuickSimEngine engine = null)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            engine = engine ?? new QuickSimEngine();
            var early = engine.Simulate(away, home, random, SimulatedInnings);
            var eighthTop = engine.SimulateHalf(away, home, random);
            var situation = RollSituation(random);
            return new GeneratedGame
            {
                Early = early,
                EighthTop = eighthTop,
                Situation = situation,
                Fixture = CreateTactics(situation, early.AwayTotal + eighthTop.Runs, early.HomeTotal),
            };
        }

        /// <summary>아웃 수(0~2, Next(3))와 주자 8가지(Next(8), 비트 0·1·2 = 1·2·3루)를 정한다.</summary>
        public static ClutchSituation RollSituation(IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            int outs = Clamp(random.Next(3), 0, 2);
            int mask = Clamp(random.Next(8), 0, 7);
            return new ClutchSituation(outs, RunnersFor(mask));
        }

        /// <summary>주자 비트(1루 = 1, 2루 = 2, 3루 = 4)를 루상 상태로 바꾼다.</summary>
        public static BaseState RunnersFor(int mask)
        {
            if (mask < 0 || mask > 7) throw new ArgumentOutOfRangeException(nameof(mask), mask, "0~7");
            var ids = new string[3];
            int next = 0;
            for (int b = 0; b < 3; b++)
                if ((mask & (1 << b)) != 0) ids[b] = RunnerIds[next++];
            return new BaseState(ids[0], ids[1], ids[2]);
        }

        /// <summary>8가지 주자 상태 × 아웃 0~2 = 24가지 상황 전부 (테스트용).</summary>
        public static IEnumerable<ClutchSituation> AllSituations()
        {
            for (int outs = 0; outs <= 2; outs++)
            for (int mask = 0; mask < 8; mask++)
                yield return new ClutchSituation(outs, RunnersFor(mask));
        }

        public static ScenarioFixture CreateTactics(ClutchSituation s, int awayScoreBefore, int homeScoreBefore)
        {
            var options = new Dictionary<TacticKind, TacticBranch>
            {
                [TacticKind.Default] = new TacticBranch { Success = () => HitContinuation(s) },
                [TacticKind.SwingAway] = new TacticBranch
                {
                    SuccessRate = Fixture8thBottomTactics.SwingAwaySuccessRate,
                    Success = () => HitContinuation(s),
                    Failure = () => SwingAwayFailedContinuation(s),
                },
            };
            if (s.AllowsSqueeze)
            {
                options[TacticKind.SqueezeBunt] = new TacticBranch
                {
                    SuccessRate = Fixture8thBottomTactics.SqueezeSuccessRate,
                    Success = () => SqueezeContinuation(s),
                    Failure = () => SqueezeFailedContinuation(s),
                };
            }

            return new ScenarioFixture
            {
                Id = ScenarioId,
                Title = $"{DecisionInning}회말 · {s} · {GapText(awayScoreBefore, homeScoreBefore)}",
                AwayScoreBefore = awayScoreBefore,
                HomeScoreBefore = homeScoreBefore,
                ZoneRule = ZoneRuleConfig.ProvisionalKbo,
                Events = BaseEvents(s),
                Branch = new BranchPoint { AfterIndex = DecisionAfterIndex, Options = options },
            };
        }

        /// <summary>작전 전 기본 구간: 초구 스트라이크 → 바깥쪽 낮은 볼 (1-1).</summary>
        public static IReadOnlyList<PitchEvent> BaseEvents(ClutchSituation s)
        {
            var zone = Zone1;
            return new List<PitchEvent>
            {
                Pitch(Batter1, zone, "포심", 146, 0, 77, PitchResult.CalledStrike, true,
                    new Count(0, 0), new Count(0, 1), s.Runners, s.Outs),
                Pitch(Batter1, zone, "슬라이더", 134, -30, 42, PitchResult.Ball, false,
                    new Count(0, 1), new Count(1, 1), s.Runners, s.Outs),
            };
        }

        /// <summary>기본 진행·강공 성공: 경계 스트라이크 → 파울 → 우전 안타 → 다음 타자 삼진.</summary>
        public static IReadOnlyList<PitchEvent> HitContinuation(ClutchSituation s)
        {
            var r = s.Runners;
            int runs = (r.Second != null ? 1 : 0) + (r.Third != null ? 1 : 0);
            var afterHit = new BaseState(Batter1, r.First, null);
            string play = runs > 0 ? $"우전 적시타 — {runs}점" : "우전 안타";
            if (r.First != null) play += ", 1루 주자 2루 진루";

            var zone1 = Zone1;
            var zone2 = Zone2;
            int outs = s.Outs;
            return new List<PitchEvent>
            {
                Pitch(Batter1, zone1, "체인지업", 128, 22.8, 60, PitchResult.CalledStrike, true,
                    new Count(1, 1), new Count(1, 2), r, outs),
                Pitch(Batter1, zone1, "포심", 147, 8, 95, PitchResult.Foul, true,
                    new Count(1, 2), new Count(1, 2), r, outs),
                new PitchEvent
                {
                    Inning = DecisionInning, HalfInning = HalfInning.Bottom, OutsBefore = outs,
                    CountBefore = new Count(1, 2),
                    PitcherId = Pitcher, BatterId = Batter1,
                    PitchType = "슬라이더", VelocityKmh = 135,
                    LocationX = -5, LocationY = 70, Zone = zone1,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 2), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = play,
                    PlayType = PlayType.Single, SprayAngleDeg = 25,
                    RunnersBefore = r, RunnersAfter = afterHit,
                    RunsScored = runs, OutsAfter = outs,
                },
                Pitch(Batter2, zone2, "포심", 148, 15, 85, PitchResult.CalledStrike, true,
                    new Count(0, 0), new Count(0, 1), afterHit, outs),
                Pitch(Batter2, zone2, "슬라이더", 133, -28, 55, PitchResult.SwingingStrike, false,
                    new Count(0, 1), new Count(0, 2), afterHit, outs),
                new PitchEvent
                {
                    Inning = DecisionInning, HalfInning = HalfInning.Bottom, OutsBefore = outs,
                    CountBefore = new Count(0, 2),
                    PitcherId = Pitcher, BatterId = Batter2,
                    PitchType = "포크", VelocityKmh = 131,
                    LocationX = 25.5, LocationY = 40, Zone = zone2,
                    Result = PitchResult.SwingingStrike, IsStrikeByRule = false,
                    CountAfter = new Count(0, 3), Outcome = PlateAppearanceOutcome.Strikeout,
                    RunnersBefore = afterHit, RunnersAfter = afterHit,
                    RunsScored = 0, OutsAfter = outs + 1,
                },
            };
        }

        /// <summary>강공 실패: 1루 주자가 있고 2사 전이면 병살, 아니면 유격수 땅볼. 다른 주자는 묶인다.</summary>
        public static IReadOnlyList<PitchEvent> SwingAwayFailedContinuation(ClutchSituation s)
        {
            var r = s.Runners;
            bool doublePlay = r.First != null && s.Outs < 2;
            int outsAfter = s.Outs + (doublePlay ? 2 : 1);
            var after = doublePlay ? new BaseState(null, r.Second, r.Third) : r;
            string play = (doublePlay ? "강공 실패 — 유격수 땅볼 병살(6-4-3)" : "강공 실패 — 유격수 땅볼") + InningEndText(outsAfter, after);

            return new List<PitchEvent>
            {
                new PitchEvent
                {
                    Inning = DecisionInning, HalfInning = HalfInning.Bottom, OutsBefore = s.Outs,
                    CountBefore = new Count(1, 1),
                    PitcherId = Pitcher, BatterId = Batter1,
                    PitchType = "싱커", VelocityKmh = 143,
                    LocationX = 9, LocationY = 54, Zone = Zone1,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = play,
                    PlayType = PlayType.GroundOut, SprayAngleDeg = -14,
                    RunnersBefore = r, RunnersAfter = after,
                    RunsScored = 0, OutsAfter = outsAfter,
                },
            };
        }

        /// <summary>스퀴즈 성공: 3루 주자 득점, 1·2루 주자 한 루씩, 타자 아웃 → 다음 타자 볼·파울·유격수 직선타.</summary>
        public static IReadOnlyList<PitchEvent> SqueezeContinuation(ClutchSituation s)
        {
            RequireSqueeze(s);
            var r = s.Runners;
            var afterBunt = new BaseState(null, r.First, r.Second);
            int outs1 = s.Outs + 1;
            int outs2 = outs1 + 1;
            string advance = r.First != null || r.Second != null ? ", 주자 한 루씩 진루" : "";
            var zone2 = Zone2;

            return new List<PitchEvent>
            {
                new PitchEvent
                {
                    Inning = DecisionInning, HalfInning = HalfInning.Bottom, OutsBefore = s.Outs,
                    CountBefore = new Count(1, 1),
                    PitcherId = Pitcher, BatterId = Batter1,
                    PitchType = "포심", VelocityKmh = 145,
                    LocationX = 6, LocationY = 70, Zone = Zone1,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = $"스퀴즈 번트 성공 — 3루 주자 득점{advance}, 타자 1루 아웃",
                    PlayType = PlayType.SacrificeBunt, SprayAngleDeg = 12,
                    RunnersBefore = r, RunnersAfter = afterBunt,
                    RunsScored = 1, OutsAfter = outs1,
                },
                Pitch(Batter2, zone2, "슬라이더", 133, -27, 52, PitchResult.Ball, false,
                    new Count(0, 0), new Count(1, 0), afterBunt, outs1),
                Pitch(Batter2, zone2, "포심", 147, 10, 90, PitchResult.Foul, true,
                    new Count(1, 0), new Count(1, 1), afterBunt, outs1),
                new PitchEvent
                {
                    Inning = DecisionInning, HalfInning = HalfInning.Bottom, OutsBefore = outs1,
                    CountBefore = new Count(1, 1),
                    PitcherId = Pitcher, BatterId = Batter2,
                    PitchType = "체인지업", VelocityKmh = 129,
                    LocationX = -6, LocationY = 58, Zone = zone2,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = "유격수 직선타" + InningEndText(outs2, afterBunt),
                    PlayType = PlayType.LineOut, SprayAngleDeg = -20,
                    RunnersBefore = afterBunt, RunnersAfter = afterBunt,
                    RunsScored = 0, OutsAfter = outs2,
                },
            };
        }

        /// <summary>스퀴즈 실패: 번트 포수 뜬공, 홈으로 뛰던 3루 주자 귀루 실패로 더블 아웃. 다른 주자는 그대로.</summary>
        public static IReadOnlyList<PitchEvent> SqueezeFailedContinuation(ClutchSituation s)
        {
            RequireSqueeze(s);
            var r = s.Runners;
            var after = new BaseState(r.First, r.Second, null);
            int outsAfter = s.Outs + 2;

            return new List<PitchEvent>
            {
                new PitchEvent
                {
                    Inning = DecisionInning, HalfInning = HalfInning.Bottom, OutsBefore = s.Outs,
                    CountBefore = new Count(1, 1),
                    PitcherId = Pitcher, BatterId = Batter1,
                    PitchType = "포심", VelocityKmh = 148,
                    LocationX = 4, LocationY = 96, Zone = Zone1,
                    Result = PitchResult.InPlay, IsStrikeByRule = true,
                    CountAfter = new Count(1, 1), Outcome = PlateAppearanceOutcome.InPlay,
                    PlayResult = "스퀴즈 실패 — 번트 포수 뜬공, 3루 주자 귀루 못 해 더블 아웃" + InningEndText(outsAfter, after),
                    PlayType = PlayType.PopOut, SprayAngleDeg = 6,
                    RunnersBefore = r, RunnersAfter = after,
                    RunsScored = 0, OutsAfter = outsAfter,
                },
            };
        }

        /// <summary>"무사", "1사", "2사".</summary>
        public static string OutsText(int outs) => outs == 0 ? "무사" : $"{outs}사";

        static string GapText(int away, int home)
        {
            int diff = Math.Abs(away - home);
            return diff == 0 ? "동점" : diff == 1 ? "한 점 차" : diff == 2 ? "두 점 차" : $"{diff}점 차";
        }

        /// <summary>3아웃이면 ", 이닝 종료" (+ 잔루 수).</summary>
        static string InningEndText(int outsAfter, BaseState left)
        {
            if (outsAfter < 3) return "";
            return left.RunnerCount > 0 ? $", 이닝 종료 · 잔루 {left.RunnerCount}" : ", 이닝 종료";
        }

        static void RequireSqueeze(ClutchSituation s)
        {
            if (!s.AllowsSqueeze) throw new ArgumentException($"스퀴즈를 할 수 없는 상황: {s}", nameof(s));
        }

        static StrikeZone Zone1 => ZoneRuleConfig.ProvisionalKbo.ZoneFor(Fixture8thBottom.Batter1HeightCm);
        static StrikeZone Zone2 => ZoneRuleConfig.ProvisionalKbo.ZoneFor(Fixture8thBottom.Batter2HeightCm);

        static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        static PitchEvent Pitch(string batter, StrikeZone zone, string type, double kmh,
            double x, double y, PitchResult result, bool isStrike, Count before, Count after, BaseState runners, int outs) =>
            FixtureBuilder.Pitch(DecisionInning, HalfInning.Bottom, Pitcher, batter, zone, type, kmh, x, y,
                result, isStrike, before, after, runners, outs);
    }
}
