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
        /// <summary>
        /// 저장 형식 버전. 형식이 바뀌면 올리고, 다른 버전 파일은 손상으로 보고 새 시즌을 시작한다.
        /// 2 (Phase 26.5): 가상 리그(부산 웨이브스 등) → 실제 KBO 구단. 이전 저장 파일의 구단·선수는 지금 데이터에 없어 새 시즌으로 시작한다.
        /// </summary>
        public const int CurrentVersion = 2;

        public int Version;

        /// <summary>
        /// 화폐 단위 버전 (Phase 26). 0 = 이전 단위(예산 ₩5,000,000 시작) → 불러올 때 금액 ×1,000.
        /// 1 = 현실화 단위(예산 ₩50억 시작). 형식 <see cref="Version"/>과 따로 둬 이전 파일을 손상으로 보지 않는다.
        /// </summary>
        public int MoneyUnitVersion;
        public const int CurrentMoneyUnitVersion = 1;
        /// <summary>표시용 시즌 제목 (연도에서 다시 만든다 — 읽을 때는 <see cref="Year"/>가 없는 이전 파일의 연도 추정에만 쓴다).</summary>
        public string SeasonTitle;
        /// <summary>
        /// 시즌 연도 (Phase 27). 0이면 이 필드가 없는 이전 저장 파일 — 제목 앞 네 자리(예: "2026 시즌 · 5월")에서,
        /// 그것도 없으면 <see cref="SeasonState.DefaultYear"/>로 읽는다. 형식 <see cref="Version"/>은 그대로라 이전 파일도 이어서 읽힌다.
        /// </summary>
        public int Year;
        public string MyTeam;
        /// <summary>다음에 치를 경기의 일정 인덱스 (= 치른 경기 수).</summary>
        public int NextIndex;
        public List<ScheduledGameData> Schedule = new List<ScheduledGameData>();
        /// <summary>순위 순서대로 저장한다 (동률 순서 유지).</summary>
        public List<StandingData> Standings = new List<StandingData>();
        /// <summary>
        /// 사용자가 바꾼 내 구단 타자 라인업. 바꾼 적이 없으면 빈 목록(= 선수 DB 기본 라인업).
        /// 이 필드가 없는 이전 저장 파일도 그대로 읽힌다.
        /// </summary>
        public LineupSaveData Lineup = new LineupSaveData();

        /// <summary>
        /// 재정 필드(<see cref="Budget"/>, <see cref="FanApproval"/>)가 저장돼 있는지 (Phase 21).
        /// 이 필드가 없는 이전 저장 파일은 false로 읽혀 기본값(₩5,000,000 · 50%)을 쓴다 — 저장된 0과 "없음"을 구분하기 위한 표시.
        /// </summary>
        public bool HasFinance;
        /// <summary>Phase 26부터 long. 이전 파일의 int 값도 그대로 읽힌다 (단위 변환은 <see cref="MoneyUnitVersion"/>).</summary>
        public long Budget;
        public int FanApproval;

        /// <summary>
        /// 팬덤 슬롯(<see cref="FandomSlots"/>, <see cref="FandomSlot"/> 비트)이 저장돼 있는지 (Phase 23).
        /// 이 필드가 없는 이전 저장 파일은 false로 읽혀 해금 없음에서 시작한다.
        /// </summary>
        public bool HasFandom;
        public int FandomSlots;

        /// <summary>
        /// 프런트 신뢰도·신뢰 대출 필드가 저장돼 있는지 (Phase 23, <see cref="FrontOfficeTrust"/>).
        /// 이 필드가 없는 이전 저장 파일은 false로 읽혀 TP 0·대출 없음에서 시작한다.
        /// </summary>
        public bool HasTrust;
        public int TrustPoints;
        public int TrustEarnedThisSeason;
        public long CapRepay;
        public long CapFlex;

        /// <summary>FA 영입 기록 (Phase 25, 영입 순). 이 필드가 없는 이전 저장 파일은 빈 목록.</summary>
        public List<SigningData> Signings = new List<SigningData>();

        /// <summary>
        /// 선수 컨디션 (Phase 22). 기록된 선수만 담고, 없는 선수는 보통(3). 이 필드가 없는 이전 저장 파일은 빈 목록(전원 보통).
        /// JsonUtility가 Dictionary를 다루지 못해 목록으로 저장한다.
        /// </summary>
        public List<ConditionData> Conditions = new List<ConditionData>();

        /// <summary>이번 시즌 개인 성적 (Phase 37, 선수 ID 순). 없는 이전 저장 파일은 빈 목록 (전원 0).</summary>
        public List<BattingStatData> BattingStats = new List<BattingStatData>();
        public List<PitchingStatData> PitchingStats = new List<PitchingStatData>();

        /// <summary>부상 중인 선수와 남은 결장 경기 (Phase 36, 선수 ID 순). 없는 이전 저장 파일은 빈 목록 (전원 건강).</summary>
        public List<InjuryData> Injuries = new List<InjuryData>();

        /// <summary>구단별 직전 선발 (선발 로테이션 순번, Phase 22). 없는 이전 저장 파일은 1번부터.</summary>
        public List<LastStarterData> LastStarters = new List<LastStarterData>();

        /// <summary>최근 리그 뉴스 (Phase 28, 오래된 것부터 최대 <see cref="SeasonState.MaxNews"/>건). 없는 이전 저장 파일은 빈 피드.</summary>
        public List<NewsData> News = new List<NewsData>();

        /// <summary>선수 변동치 (Phase 29, 선수 ID 순). 없는 이전 저장 파일은 모두 원본 그대로 (마스터 CSV는 바꾸지 않는다).</summary>
        public List<ProgressionData> Progression = new List<ProgressionData>();

        /// <summary>
        /// 드래프트 신인 (Phase 30, 지명 순). 마스터 CSV에 없는 선수라 기록 전체를 <see cref="DailyFranchise.Sim.Data.PlayerData"/> 그대로 담는다.
        /// 없는 이전 저장 파일은 빈 목록.
        /// </summary>
        public List<DailyFranchise.Sim.Data.PlayerData> Rookies = new List<DailyFranchise.Sim.Data.PlayerData>();

        /// <summary>캠프 포인트 (Phase 33). 없는 이전 저장 파일은 0.</summary>
        public int CampPoints;

        /// <summary>시즌 단계 (Phase 32, <see cref="SeasonPhase"/> 정수). 없는 이전 저장 파일은 0 = 정규시즌.</summary>
        public int Phase;

        /// <summary>한국시리즈 (Phase 32). 정규시즌이거나 없는 이전 저장 파일은 비어 있다.</summary>
        public PostseasonData Postseason = new PostseasonData();
    }

    [Serializable]
    public sealed class PostseasonData
    {
        /// <summary>정규시즌 1위 (비어 있으면 시리즈 없음).</summary>
        public string TopSeed;
        public string SecondSeed;
        /// <summary>치른 경기 수 (읽을 때는 앞에서부터 이어진 종료 경기 수로 다시 센다).</summary>
        public int NextIndex;
        /// <summary>시리즈 경기 (우승이 정해지면 남은 경기는 지워져 저장되지 않는다).</summary>
        public List<ScheduledGameData> Games = new List<ScheduledGameData>();
    }

    [Serializable]
    public sealed class ProgressionData
    {
        public string Id;
        /// <summary>원본 나이에 더할 햇수.</summary>
        public int AgeDelta;
        /// <summary>누적 폼 (안타 확률 상대 보정, ±0.10).</summary>
        public double Form;
        public bool Retired;
        public int RetiredYear;
    }

    [Serializable]
    public sealed class NewsData
    {
        /// <summary><see cref="NewsKind"/> 정수 값.</summary>
        public int Kind;
        public string Tag;
        public string Team;
        public string Headline;
        public int Year;
        public string Date;
    }

    [Serializable]
    public sealed class SigningData
    {
        public string PlayerId;
        public string FromTeam;
        public string ToTeam;
        public long Salary;
        /// <summary>1:1 트레이드의 한쪽 (Phase 34 — 연달아 저장된 두 기록이 한 건). 없는 이전 파일은 false.</summary>
        public bool Trade;
        /// <summary>1군 ↔ 퓨처스 맞바꾸기에서 올라온 선수 (Phase 35 — PlayerId가 말소된 선수). 이적 기록·이전 파일은 비어 있다.</summary>
        public string CallUp;
        /// <summary>부상(1)·회복(2) 기록 (Phase 36, <see cref="InjuryMark"/> 정수). 이적 기록·이전 파일은 0.</summary>
        public int Injury;
    }

    [Serializable]
    public sealed class BattingStatData
    {
        public string Id;
        public DailyFranchise.Sim.Data.BattingLine Line;
    }

    [Serializable]
    public sealed class PitchingStatData
    {
        public string Id;
        public DailyFranchise.Sim.Data.PitchingLine Line;
    }

    [Serializable]
    public sealed class InjuryData
    {
        public string Id;
        /// <summary>부상 당시 소속 (회복 기록을 남길 구단).</summary>
        public string Team;
        /// <summary>회복까지 남은 경기 (1 이상).</summary>
        public int Games;
    }

    [Serializable]
    public sealed class LastStarterData
    {
        public string Team;
        public string PlayerId;
    }

    [Serializable]
    public sealed class ConditionData
    {
        public string Id;
        public int Value;
    }

    [Serializable]
    public sealed class LineupSaveData
    {
        /// <summary>타순 1~9번 선수 ID.</summary>
        public List<string> Batting = new List<string>();
        /// <summary>타순별 수비 위치 (Batting과 같은 길이).</summary>
        public List<string> Positions = new List<string>();
        public List<string> Bench = new List<string>();

        public bool IsEmpty => (Batting == null || Batting.Count == 0) && (Bench == null || Bench.Count == 0);
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
        /// <summary>가장 최근 경기일 직전 순위 (0 = 없음). 이 필드가 없는 이전 저장 파일은 0으로 읽힌다.</summary>
        public int PreviousRank;
        /// <summary>시즌 누적 득점·실점. 이 필드가 없는 이전 저장 파일은 0으로 읽힌다.</summary>
        public int RunsScored;
        public int RunsAllowed;
    }
}
