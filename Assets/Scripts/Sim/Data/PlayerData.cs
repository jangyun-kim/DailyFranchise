using System;
using System.Collections.Generic;

namespace DailyFranchise.Sim.Data
{
    public enum Hand
    {
        Right,
        Left,
        /// <summary>스위치 타자 (타석 전용).</summary>
        Switch,
    }

    /// <summary>선수단 안의 역할. 경기 화면의 타순·벤치·불펜·선발 칸과 대응한다.</summary>
    public enum RosterRole
    {
        /// <summary>선발 타순 (Order 1~9).</summary>
        Lineup,
        /// <summary>벤치 야수 (대타·대주자).</summary>
        Bench,
        /// <summary>선발 투수 (Order 1이 오늘 선발).</summary>
        Starter,
        /// <summary>불펜 투수.</summary>
        Bullpen,
    }

    /// <summary>
    /// 외부 데이터(CSV 등)에서 읽은 선수 한 명. 경기 판정에는 아직 쓰지 않고 표시용으로만 쓴다.
    /// 야수 기록은 타자 칸, 투수 기록은 투수 칸만 의미가 있다.
    /// </summary>
    public sealed class PlayerData
    {
        public string Id;
        public string Name;
        /// <summary>소속 구단 정식 이름 (예: "부산 웨이브스").</summary>
        public string Team;
        /// <summary>수비 위치 또는 투수 보직 (예: "RF", "대타", "선발", "마무리").</summary>
        public string Position;
        public Hand Throws;
        public Hand Bats;
        public RosterRole Role;
        /// <summary>역할 안 순서. 타순이면 1~9, 그 외는 표시 순서.</summary>
        public int Order;
        /// <summary>짧은 특징 메모 (예: "장타", "주력 상"). 없으면 null.</summary>
        public string Memo;

        // 타자 시즌 기록
        public double BattingAverage;
        /// <summary>출루율. 0이면 기록 없음 (확률 계산은 타율로 추정).</summary>
        public double OnBasePercentage;
        public int HomeRuns;
        public double Ops;
        public int StolenBases;
        /// <summary>득점권 타율. 0이면 기록 없음 (득점권 보정은 리그 기본값).</summary>
        public double ScoringPositionAverage;

        /// <summary>장타율 = OPS − 출루율. 둘 중 하나라도 없으면 0 (기록 없음).</summary>
        public double SluggingPercentage => Ops > 0 && OnBasePercentage > 0 ? Ops - OnBasePercentage : 0;

        // 투수 시즌 기록
        public double Era;
        /// <summary>피안타율. 0이면 기록 없음 (확률 계산은 평균자책으로 추정).</summary>
        public double OpponentAverage;
        public int Wins;
        public int Losses;
        public int Saves;
        public int Holds;
        /// <summary>9이닝당 피홈런. 0이면 기록 없음 (타구 분배는 리그 평균).</summary>
        public double HomeRunsPer9;

        public bool IsPitcher => Role == RosterRole.Starter || Role == RosterRole.Bullpen;
    }

    /// <summary>선수 ID로 선수 데이터를 찾는다. 확률 계산기(<see cref="DailyFranchise.Sim.MatchupCalculator"/>)가 DB 구현을 모르게 하는 경계.</summary>
    public interface IPlayerLookup
    {
        /// <summary>없으면 null.</summary>
        PlayerData FindPlayer(string playerId);
    }

    /// <summary>구단 기본 정보.</summary>
    public sealed class TeamData
    {
        public string Name;
        public string ShortName;
        public string Stadium;
    }

    /// <summary>한 구단의 오늘 선수단. 각 목록은 Order 순으로 정렬돼 있다.</summary>
    public sealed class TeamRoster
    {
        public readonly string Team;
        public readonly IReadOnlyList<PlayerData> Lineup;
        public readonly IReadOnlyList<PlayerData> Bench;
        public readonly IReadOnlyList<PlayerData> Starters;
        public readonly IReadOnlyList<PlayerData> Bullpen;

        public TeamRoster(string team, IReadOnlyList<PlayerData> lineup, IReadOnlyList<PlayerData> bench,
            IReadOnlyList<PlayerData> starters, IReadOnlyList<PlayerData> bullpen)
        {
            Team = team ?? throw new ArgumentNullException(nameof(team));
            Lineup = lineup ?? Array.Empty<PlayerData>();
            Bench = bench ?? Array.Empty<PlayerData>();
            Starters = starters ?? Array.Empty<PlayerData>();
            Bullpen = bullpen ?? Array.Empty<PlayerData>();
        }

        /// <summary>오늘 선발 투수. 없으면 null.</summary>
        public PlayerData StartingPitcher => Starters.Count > 0 ? Starters[0] : null;

        /// <summary>타순(1~9)의 선수. 없으면 null.</summary>
        public PlayerData AtOrder(int order)
        {
            foreach (var p in Lineup)
                if (p.Order == order) return p;
            return null;
        }
    }
}
