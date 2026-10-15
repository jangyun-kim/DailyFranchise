using System;
using System.Linq;

namespace DailyFranchise.Sim.Data
{
    /// <summary>CPU 구단의 투수 교체 한 번.</summary>
    public sealed class CpuPitchingChange
    {
        public CpuPitchingChange(string from, string to, string reason)
        {
            From = from;
            To = to;
            Reason = reason;
        }

        public string From { get; }
        public string To { get; }
        /// <summary>교체 사유 (예: "체력 고갈", "6회 이후 4실점").</summary>
        public string Reason { get; }

        public override string ToString() => $"{From} → {To} ({Reason})";
    }

    /// <summary>
    /// CPU(상대) 구단 투수 운용 기본 AI. 투수 운용 상태는 <see cref="InGameRoster"/>(경기 중 사본)에 두고, 이 클래스는 판단만 한다.
    /// 교체는 이닝 교대 시점, CPU가 수비를 시작하는 반 이닝 직전에만 한다 (타석 중간에는 바꾸지 않는다).
    ///
    /// 교체 조건 (<see cref="ShouldReplace"/>): 지금 투수가
    /// - 체력 고갈(<see cref="FatigueLevel.Exhausted"/>: 선발 100구·구원 30구 이상), 또는
    /// - <see cref="LateInning"/>회 이후이고 마운드에서 허용한 실점이 선발 <see cref="StarterRunLimit"/>점·구원 <see cref="RelieverRunLimit"/>점 이상.
    /// 새 투수 (<see cref="ChooseReliever"/>): 9회 이후는 마무리 우선, 그 전에는 마무리를 아끼고 대기 순서대로. 남은 투수가 없으면 그대로 둔다.
    /// 난수를 쓰지 않는다 — 같은 경기 흐름이면 늘 같은 교체.
    /// </summary>
    public sealed class CpuRosterManager
    {
        public const int LateInning = 6;
        public const int StarterRunLimit = 4;
        public const int RelieverRunLimit = 3;
        public const string CloserPosition = "마무리";

        public CpuRosterManager(InGameRoster roster)
        {
            Roster = roster ?? throw new ArgumentNullException(nameof(roster));
        }

        public InGameRoster Roster { get; }

        /// <summary>교체 사유. 바꿀 필요가 없으면 null.</summary>
        public static string ReplaceReason(PitcherState current, int runsAllowed, int inning)
        {
            if (current == null) return null;
            if (current.Level == FatigueLevel.Exhausted) return "체력 고갈";
            int limit = current.IsStarter ? StarterRunLimit : RelieverRunLimit;
            return inning >= LateInning && runsAllowed >= limit ? $"{LateInning}회 이후 {runsAllowed}실점" : null;
        }

        public static bool ShouldReplace(PitcherState current, int runsAllowed, int inning) =>
            ReplaceReason(current, runsAllowed, inning) != null;

        /// <summary>올릴 구원 투수. 후보가 없으면 null.</summary>
        public PlayerData ChooseReliever(int inning)
        {
            var candidates = Roster.ReliefCandidates;
            if (candidates.Count == 0) return null;
            var closer = candidates.FirstOrDefault(p => p.Position == CloserPosition);
            if (inning >= Fixtures.PlayByPlayGenerator.FinalInning) return closer ?? candidates[0];
            return candidates.FirstOrDefault(p => p.Position != CloserPosition) ?? candidates[0];
        }

        /// <summary>
        /// CPU 수비 반 이닝이 시작되기 직전에 부른다. 바꿔야 하고 올릴 투수가 있으면 교체하고 그 내용을, 아니면 null.
        /// </summary>
        /// <param name="inning">시작하는 반 이닝의 회.</param>
        /// <param name="current">지금 투수의 체력 (ID는 <see cref="InGameRoster.CurrentPitcherId"/>와 같아야 한다).</param>
        /// <param name="runsAllowed">지금 투수가 마운드에서 허용한 실점.</param>
        public CpuPitchingChange OnHalfInningStart(int inning, PitcherState current, int runsAllowed)
        {
            if (current != null && current.PitcherId != Roster.CurrentPitcherId)
                throw new ArgumentException("지금 마운드의 투수 상태가 아니다.", nameof(current));
            string reason = ReplaceReason(current, runsAllowed, inning);
            if (reason == null) return null;
            var reliever = ChooseReliever(inning);
            if (reliever == null) return null;
            string from = Roster.CurrentPitcherId;
            Roster.BringIn(reliever.Id);
            return new CpuPitchingChange(from, reliever.Id, reason);
        }
    }
}
