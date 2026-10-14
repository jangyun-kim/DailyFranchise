using System;
using System.Collections.Generic;

namespace DailyFranchise.App
{
    /// <summary>
    /// <see cref="SeasonState"/>의 JsonUtility 직렬화 형태. JsonUtility는 공개 필드·List·[Serializable] 클래스만
    /// 다루므로 읽기 전용 속성·Nullable 점수는 여기서 평평한 필드로 풀어 둔다.
    /// 경기 중(InProgress) 상태는 저장하지 않는다 (저장은 로비에서만 일어난다).
    /// </summary>
    [Serializable]
    public sealed class SeasonSaveData
    {
        /// <summary>저장 형식 버전. 형식이 바뀌면 올리고, 다른 버전 파일은 손상으로 보고 새 시즌을 시작한다.</summary>
        public const int CurrentVersion = 1;

        public int Version;
        public string SeasonTitle;
        public string MyTeam;
        /// <summary>다음에 치를 경기의 일정 인덱스 (= 치른 경기 수).</summary>
        public int NextIndex;
        public List<ScheduledGameData> Schedule = new List<ScheduledGameData>();
        /// <summary>순위 순서대로 저장한다 (동률 순서 유지).</summary>
        public List<StandingData> Standings = new List<StandingData>();
    }

    [Serializable]
    public sealed class ScheduledGameData
    {
        public int Number;
        public string Date;
        public string AwayTeam;
        public string HomeTeam;
        public string Stadium;
        public bool Final;
        /// <summary>Final일 때만 의미가 있다.</summary>
        public int AwayScore;
        public int HomeScore;
    }

    [Serializable]
    public sealed class StandingData
    {
        public string Team;
        public int Wins;
        public int Losses;
        public int Draws;
    }
}
