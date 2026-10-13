using System;

namespace DailyFranchise.Sim
{
    public readonly struct CountTransition
    {
        public readonly Count After;
        public readonly PlateAppearanceOutcome Outcome;

        public CountTransition(Count after, PlateAppearanceOutcome outcome)
        {
            After = after;
            Outcome = outcome;
        }
    }

    public static class CountRules
    {
        /// <summary>투구 결과에 따른 카운트 변화와 타석 종료 여부.</summary>
        public static CountTransition Apply(Count before, PitchResult result)
        {
            if (!before.IsLive)
                throw new ArgumentException($"진행 중인 타석의 카운트가 아니다: {before}", nameof(before));

            switch (result)
            {
                case PitchResult.Ball:
                {
                    var after = new Count(before.Balls + 1, before.Strikes);
                    return new CountTransition(after,
                        after.Balls == 4 ? PlateAppearanceOutcome.Walk : PlateAppearanceOutcome.None);
                }
                case PitchResult.CalledStrike:
                case PitchResult.SwingingStrike:
                {
                    var after = new Count(before.Balls, before.Strikes + 1);
                    return new CountTransition(after,
                        after.Strikes == 3 ? PlateAppearanceOutcome.Strikeout : PlateAppearanceOutcome.None);
                }
                case PitchResult.Foul:
                    // 2스트라이크 이후 파울은 카운트 변화 없음 (번트 파울 삼진은 MVP 범위 밖).
                    return new CountTransition(
                        before.Strikes < 2 ? new Count(before.Balls, before.Strikes + 1) : before,
                        PlateAppearanceOutcome.None);
                case PitchResult.InPlay:
                    return new CountTransition(before, PlateAppearanceOutcome.InPlay);
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result, null);
            }
        }
    }

    public static class BaseRunning
    {
        /// <summary>볼넷 시 밀어내기 진루. 반환값은 진루 후 루 상태와 득점 수.</summary>
        public static (BaseState after, int runs) ForceAdvanceOnWalk(BaseState before, string batterId)
        {
            if (before.First == null)
                return (new BaseState(batterId, before.Second, before.Third), 0);
            if (before.Second == null)
                return (new BaseState(batterId, before.First, before.Third), 0);
            if (before.Third == null)
                return (new BaseState(batterId, before.First, before.Second), 0);
            return (new BaseState(batterId, before.First, before.Second), 1);
        }
    }
}
