using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 고정 장면의 원정·홈을 맞바꾼 표시용 사본을 만든다. 장면 Fixture는 내 구단을 홈으로 짜여 있어,
    /// 일정상 내 구단이 원정인 경기는 이 사본을 재생한다. 원본 Fixture와 판정 규칙은 건드리지 않는다.
    /// - 반 이닝: N회초 → N회말, N회말 → (N+1)회초. 반 이닝 순서가 그대로 유지되어 PitchEventValidator 규칙을 지킨다.
    /// - 시작 점수: 원정 ↔ 홈.
    /// - 투구·타구·주자·득점·아웃은 그대로 (같은 선수가 같은 공을 던지고 친다).
    /// </summary>
    public static class ScenarioMirror
    {
        public static ScenarioFixture Mirror(ScenarioFixture f) => new ScenarioFixture
        {
            Id = f.Id,
            Title = f.Title,
            AwayScoreBefore = f.HomeScoreBefore,
            HomeScoreBefore = f.AwayScoreBefore,
            ZoneRule = f.ZoneRule,
            Events = Mirror(f.Events),
            Branch = f.Branch == null ? null : Mirror(f.Branch),
        };

        public static List<PitchEvent> Mirror(IReadOnlyList<PitchEvent> events) => events.Select(Mirror).ToList();

        public static PitchEvent Mirror(PitchEvent e)
        {
            bool top = e.HalfInning == HalfInning.Top;
            return new PitchEvent
            {
                Inning = top ? e.Inning : e.Inning + 1,
                HalfInning = top ? HalfInning.Bottom : HalfInning.Top,
                OutsBefore = e.OutsBefore,
                CountBefore = e.CountBefore,
                PitcherId = e.PitcherId,
                BatterId = e.BatterId,
                PitchType = e.PitchType,
                VelocityKmh = e.VelocityKmh,
                LocationX = e.LocationX,
                LocationY = e.LocationY,
                Zone = e.Zone,
                Result = e.Result,
                IsStrikeByRule = e.IsStrikeByRule,
                CountAfter = e.CountAfter,
                Outcome = e.Outcome,
                PlayResult = e.PlayResult,
                PlayType = e.PlayType,
                SprayAngleDeg = e.SprayAngleDeg,
                RunnersBefore = e.RunnersBefore,
                RunnersAfter = e.RunnersAfter,
                RunsScored = e.RunsScored,
                OutsAfter = e.OutsAfter,
            };
        }

        static BranchPoint Mirror(BranchPoint b) => new BranchPoint
        {
            AfterIndex = b.AfterIndex,
            Options = b.Options.ToDictionary(kv => kv.Key, kv => Mirror(kv.Value)),
        };

        static TacticBranch Mirror(TacticBranch t)
        {
            var success = t.Success;
            var failure = t.Failure;
            return new TacticBranch
            {
                SuccessRate = t.SuccessRate,
                Success = () => Mirror(success()),
                Failure = failure == null ? null : (System.Func<IReadOnlyList<PitchEvent>>)(() => Mirror(failure())),
            };
        }
    }
}
