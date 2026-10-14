using System;

namespace DailyFranchise.Sim
{
    /// <summary>타석 결과 종류 (상태 갱신 입력). 상황에 맞지 않는 결과는 <see cref="PlayResultResolver"/>가 가까운 결과로 바꾼다.</summary>
    public enum PlayOutcome
    {
        Single,
        Double,
        Triple,
        HomeRun,
        Walk,
        Strikeout,
        /// <summary>내야 땅볼. 타자만 아웃, 주자는 묶인다.</summary>
        GroundOut,
        /// <summary>땅볼 병살 (6-4-3). 1루 주자가 있고 2아웃 전에만 성립, 아니면 땅볼.</summary>
        DoublePlay,
        /// <summary>외야 뜬공. 주자는 묶인다.</summary>
        FlyOut,
        /// <summary>희생플라이. 3루 주자가 있고 2아웃 전에만 성립(3루 주자 득점), 아니면 뜬공.</summary>
        SacrificeFly,
        LineOut,
        PopOut,
        /// <summary>희생 번트·스퀴즈. 타자 아웃, 모든 주자 한 루씩 (3루 주자 득점). 2아웃이면 타자 아웃으로 이닝 종료.</summary>
        SacrificeBunt,
        /// <summary>스퀴즈 실패. 번트 뜬공 + 3루 주자 귀루 실패로 더블 아웃. 3루 주자가 있고 2아웃 전에만 성립, 아니면 내야 뜬공.</summary>
        FailedSqueeze,
    }

    /// <summary>타석 결과를 반영한 뒤의 상태.</summary>
    public readonly struct PlayResolution
    {
        /// <summary>실제로 적용한 결과 (예: 1루 주자가 없어 병살 → 땅볼).</summary>
        public readonly PlayOutcome Outcome;
        public readonly PlateAppearanceOutcome PlateAppearance;
        /// <summary>인플레이 타구 종류. 삼진·볼넷이면 None.</summary>
        public readonly PlayType PlayType;
        public readonly BaseState RunnersAfter;
        public readonly int OutsAfter;
        public readonly int RunsScored;

        public PlayResolution(PlayOutcome outcome, PlateAppearanceOutcome plateAppearance, PlayType playType,
            BaseState runnersAfter, int outsAfter, int runsScored)
        {
            Outcome = outcome;
            PlateAppearance = plateAppearance;
            PlayType = playType;
            RunnersAfter = runnersAfter;
            OutsAfter = outsAfter;
            RunsScored = runsScored;
        }

        /// <summary>3아웃으로 반 이닝이 끝났다.</summary>
        public bool EndsHalfInning => OutsAfter >= 3;
    }

    /// <summary>
    /// 타석 결과 → 주자·아웃·득점 갱신. 순수 계산이며 난수를 쓰지 않는다. 결과는 <see cref="PitchEventValidator"/> 규칙을 지킨다.
    ///
    /// 진루 규칙 (MVP 단순화):
    /// - 단타: 2·3루 주자 득점, 1루 주자 2루, 타자 1루.   2루타: 2·3루 주자 득점, 1루 주자 3루, 타자 2루.
    /// - 3루타: 모든 주자 득점, 타자 3루.   홈런: 모든 주자 + 타자 득점.   볼넷: 밀어내기 진루만.
    /// - 아웃 타구(삼진·땅볼·뜬공·직선타·내야 뜬공): 타자만 아웃, 주자는 묶인다.
    /// - 병살·희생플라이·희생 번트·스퀴즈 실패는 위 결과 설명대로. 3아웃이 되는 타구에서는 득점이 없다.
    /// </summary>
    public static class PlayResultResolver
    {
        public static PlayResolution Resolve(int outsBefore, BaseState runners, string batterId, PlayOutcome outcome)
        {
            if (outsBefore < 0 || outsBefore > 2)
                throw new ArgumentOutOfRangeException(nameof(outsBefore), outsBefore, "타석이 진행 중인 아웃 수는 0~2");
            if (string.IsNullOrEmpty(batterId)) throw new ArgumentException("타자 ID가 없다.", nameof(batterId));

            var r = runners;
            switch (Applicable(outsBefore, r, outcome))
            {
                case PlayOutcome.Single:
                    return Hit(PlayOutcome.Single, PlayType.Single, new BaseState(batterId, r.First, null), Count(r.Second, r.Third));
                case PlayOutcome.Double:
                    return Hit(PlayOutcome.Double, PlayType.Double, new BaseState(null, batterId, r.First), Count(r.Second, r.Third));
                case PlayOutcome.Triple:
                    return Hit(PlayOutcome.Triple, PlayType.Triple, new BaseState(null, null, batterId), r.RunnerCount);
                case PlayOutcome.HomeRun:
                    return Hit(PlayOutcome.HomeRun, PlayType.HomeRun, BaseState.Empty, r.RunnerCount + 1);

                case PlayOutcome.Walk:
                {
                    var (after, runs) = BaseRunning.ForceAdvanceOnWalk(r, batterId);
                    return new PlayResolution(PlayOutcome.Walk, PlateAppearanceOutcome.Walk, PlayType.None, after, outsBefore, runs);
                }
                case PlayOutcome.Strikeout:
                    return new PlayResolution(PlayOutcome.Strikeout, PlateAppearanceOutcome.Strikeout, PlayType.None, r, outsBefore + 1, 0);

                case PlayOutcome.GroundOut: return Out(PlayOutcome.GroundOut, PlayType.GroundOut, r, 1, 0);
                case PlayOutcome.FlyOut: return Out(PlayOutcome.FlyOut, PlayType.FlyOut, r, 1, 0);
                case PlayOutcome.LineOut: return Out(PlayOutcome.LineOut, PlayType.LineOut, r, 1, 0);
                case PlayOutcome.PopOut: return Out(PlayOutcome.PopOut, PlayType.PopOut, r, 1, 0);

                case PlayOutcome.DoublePlay:
                    return Out(PlayOutcome.DoublePlay, PlayType.GroundOut, new BaseState(null, r.Second, r.Third), 2, 0);
                case PlayOutcome.SacrificeFly:
                    return Out(PlayOutcome.SacrificeFly, PlayType.FlyOut, new BaseState(r.First, r.Second, null), 1, 1);
                case PlayOutcome.SacrificeBunt:
                    return outsBefore == 2
                        ? Out(PlayOutcome.SacrificeBunt, PlayType.SacrificeBunt, r, 1, 0)
                        : Out(PlayOutcome.SacrificeBunt, PlayType.SacrificeBunt, new BaseState(null, r.First, r.Second), 1,
                            r.Third != null ? 1 : 0);
                case PlayOutcome.FailedSqueeze:
                    return Out(PlayOutcome.FailedSqueeze, PlayType.PopOut, new BaseState(r.First, r.Second, null), 2, 0);

                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
            }

            PlayResolution Hit(PlayOutcome o, PlayType type, BaseState after, int runs) =>
                new PlayResolution(o, PlateAppearanceOutcome.InPlay, type, after, outsBefore, runs);

            PlayResolution Out(PlayOutcome o, PlayType type, BaseState after, int outs, int runs)
            {
                int outsAfter = outsBefore + outs;
                // 3아웃이 되는 타구에서는 득점이 인정되지 않는다.
                return new PlayResolution(o, PlateAppearanceOutcome.InPlay, type, after, outsAfter, outsAfter >= 3 ? 0 : runs);
            }
        }

        /// <summary>상황에 맞지 않는 결과를 가까운 결과로 바꾼다 (병살 → 땅볼, 희생플라이 → 뜬공, 스퀴즈 실패 → 내야 뜬공).</summary>
        public static PlayOutcome Applicable(int outsBefore, BaseState runners, PlayOutcome outcome)
        {
            switch (outcome)
            {
                case PlayOutcome.DoublePlay:
                    return runners.First != null && outsBefore < 2 ? outcome : PlayOutcome.GroundOut;
                case PlayOutcome.SacrificeFly:
                    return runners.Third != null && outsBefore < 2 ? outcome : PlayOutcome.FlyOut;
                case PlayOutcome.FailedSqueeze:
                    return runners.Third != null && outsBefore < 2 ? outcome : PlayOutcome.PopOut;
                default:
                    return outcome;
            }
        }

        static int Count(params string[] runners)
        {
            int n = 0;
            foreach (var r in runners)
                if (r != null) n++;
            return n;
        }
    }
}
