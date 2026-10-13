using System.Collections.Generic;
using DailyFranchise.Sim;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 재생 위치 하나에 대한 화면 표시 값. Index -1은 "첫 투구 전" 상태다.
    /// 이벤트 목록을 읽기만 하며 판정하지 않는다.
    /// </summary>
    public sealed class ScreenState
    {
        public const int BeforeFirstPitch = -1;

        public int Index;
        /// <summary>마지막으로 던진 투구. Index가 -1이면 null.</summary>
        public PitchEvent Pitch;

        public int Inning;
        public HalfInning Half;
        public int OutsBefore;
        public int Outs;
        public BaseState RunnersBefore;
        public BaseState Runners;
        public Count Count;
        public bool PlateAppearanceEnded;

        public string PitcherId;
        public string BatterId;
        public StrikeZone Zone;
        /// <summary>현재 타석 몇 구째인지. 투구 전이면 0.</summary>
        public int PitchNumberInPlateAppearance;
        public int PlateAppearanceStart;
        public int PitcherPitchesInScene;

        public int AwayScore;
        public int HomeScore;

        public bool HomeBatting => Half == HalfInning.Bottom;

        public static ScreenState Build(IReadOnlyList<PitchEvent> events, int awayScoreBefore, int homeScoreBefore, int index)
        {
            if (index < 0)
            {
                var first = events[0];
                return new ScreenState
                {
                    Index = BeforeFirstPitch,
                    Inning = first.Inning,
                    Half = first.HalfInning,
                    OutsBefore = first.OutsBefore,
                    Outs = first.OutsBefore,
                    RunnersBefore = first.RunnersBefore,
                    Runners = first.RunnersBefore,
                    Count = first.CountBefore,
                    PitcherId = first.PitcherId,
                    BatterId = first.BatterId,
                    Zone = first.Zone,
                    AwayScore = awayScoreBefore,
                    HomeScore = homeScoreBefore,
                };
            }

            var e = events[index];
            int paStart = ScenarioReadModel.PlateAppearanceStart(events, index);
            return new ScreenState
            {
                Index = index,
                Pitch = e,
                Inning = e.Inning,
                Half = e.HalfInning,
                OutsBefore = e.OutsBefore,
                Outs = e.OutsAfter,
                RunnersBefore = e.RunnersBefore,
                Runners = e.RunnersAfter,
                Count = e.CountAfter,
                PlateAppearanceEnded = e.Outcome != PlateAppearanceOutcome.None,
                PitcherId = e.PitcherId,
                BatterId = e.BatterId,
                Zone = e.Zone,
                PitchNumberInPlateAppearance = index - paStart + 1,
                PlateAppearanceStart = paStart,
                PitcherPitchesInScene = ScenarioReadModel.PitcherPitchesThrough(events, index),
                AwayScore = awayScoreBefore + ScenarioReadModel.RunsThrough(events, index, HalfInning.Top),
                HomeScore = homeScoreBefore + ScenarioReadModel.RunsThrough(events, index, HalfInning.Bottom),
            };
        }
    }
}
