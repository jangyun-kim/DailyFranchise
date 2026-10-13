using System.Collections.Generic;

namespace DailyFranchise.UI.Data
{
    public enum TeamSide
    {
        Away,
        Home,
    }

    public sealed class TeamInfo
    {
        public string Name;
        public string ShortName;
    }

    public sealed class PlayerInfo
    {
        public string Id;
        public string Name;
        public TeamSide Side;
        /// <summary>포지션 또는 보직 (예: "RF", "선발").</summary>
        public string Position;
        /// <summary>투타 (예: "좌투", "우타").</summary>
        public string Handedness;
        public string SeasonLine;
        public string TodayLine;
        /// <summary>투수: 장면 시작 전까지 오늘 던진 공 수. 타자는 0.</summary>
        public int PitchesBeforeScene;
        /// <summary>체력(투수) 또는 컨디션(타자) %. 없으면 null.</summary>
        public int? Fitness;
    }

    public sealed class RosterEntry
    {
        /// <summary>타순. 벤치·불펜은 null.</summary>
        public int? Order;
        public string Role;
        public string PlayerId;
        /// <summary>오른쪽 칸 짧은 표기 (예: "1-3", "준비 완료").</summary>
        public string Note;
    }

    /// <summary>선수·팀 표시 정보. 화면 표시 전용이며 경기 판정에는 쓰지 않는다.</summary>
    public interface IRosterSource
    {
        TeamInfo GetTeam(TeamSide side);
        /// <summary>모르는 ID여도 null을 반환하지 않고 ID를 이름으로 쓰는 대체 정보를 돌려준다.</summary>
        PlayerInfo GetPlayer(string playerId);
        /// <summary>화면 좌측 "내 선수단"에 표시할 팀.</summary>
        TeamSide MyTeam { get; }
        IReadOnlyList<RosterEntry> GetLineup(TeamSide side);
        IReadOnlyList<RosterEntry> GetBench(TeamSide side);
        IReadOnlyList<RosterEntry> GetBullpen(TeamSide side);
        string FeaturedPlayerId { get; }
    }

    public sealed class TeamBoxSeed
    {
        /// <summary>장면 시작 전까지의 이닝별 득점. 길이 = 기록이 있는 이닝 수 (진행 중인 반 이닝 포함 가능).</summary>
        public int[] RunsByInning;
        public int Hits;
        public int Errors;
        public int Walks;
    }

    /// <summary>장면 시작 시점의 스코어보드 기초값.</summary>
    public interface IScoreboardSource
    {
        TeamBoxSeed GetBoxSeed(TeamSide side);
    }
}
