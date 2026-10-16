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

        // 기본 정보 (마스터 CSV). 0이면 기록 없음.
        /// <summary>기록 연도 (CSV Year/season).</summary>
        public int Year;
        public int Age;
        /// <summary>
        /// 에이징 커브 폼 (Phase 29): 안타(출루) 확률 상대 보정, [-<see cref="ProgressionModifier.MaxForm"/>, +MaxForm].
        /// +면 더 좋은 선수 (타자는 안타 확률 ↑, 투수는 피안타 확률 ↓). 마스터 CSV에는 없고, 시즌 저장의 선수 변동치를
        /// 선수단 장부가 사본에 덧씌운 값이다 (<see cref="WithProgression"/>). 원본 DB 선수는 늘 0.
        /// </summary>
        public double Form;
        /// <summary>등번호 (CSV Back Num.).</summary>
        public int Number;

        // 공통 시즌 기록. 마스터 CSV는 타자·투수가 G·WAR 열을 함께 쓴다.
        public int Games;
        public double War;

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

        // 타자 확장 기록 (표시 전용, 확률 계산에는 아직 쓰지 않는다)
        public int PlateAppearances;
        /// <summary>유효 타석 (CSV ePA).</summary>
        public double EffectivePlateAppearances;
        public int AtBats;
        public int Runs;
        public int Hits;
        public int Doubles;
        public int Triples;
        public int TotalBases;
        public int RunsBattedIn;
        public int CaughtStealing;
        public int Walks;
        public int HitByPitch;
        public int IntentionalWalks;
        public int Strikeouts;
        public int GroundIntoDoublePlays;
        public int SacrificeHits;
        public int SacrificeFlies;
        /// <summary>CSV SLG 열 원값 (표시용). 확률 계산은 <see cref="SluggingPercentage"/>를 쓴다.</summary>
        public double RecordedSlugging;
        /// <summary>유효 타석당 득점 기여 (CSV R/ePA).</summary>
        public double RunsPerEffectivePa;
        /// <summary>조정 득점 창출력 (CSV wRC+). 0이면 기록 없음.</summary>
        public double WrcPlus;
        public double OffensiveWar;
        public double DefensiveWar;

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

        // 투수 확장 기록 (표시 전용). R·H·HR·BB·SO 열은 투수 행이면 아래 허용 기록으로 읽는다.
        public int GamesStarted;
        /// <summary>구원 등판 (CSV GR).</summary>
        public int ReliefGames;
        /// <summary>경기 마무리 (CSV GF).</summary>
        public int GamesFinished;
        public int CompleteGames;
        public int Shutouts;
        /// <summary>이닝. CSV 표기 그대로 (예: 152.1 = 152⅓이닝).</summary>
        public double InningsPitched;
        public int EarnedRuns;
        public int RunsAllowed;
        public int HitsAllowed;
        public int HomeRunsAllowed;
        public int WalksAllowed;
        public int PitcherStrikeouts;
        /// <summary>상대 타자 수 (CSV TBF).</summary>
        public int BattersFaced;
        /// <summary>실책 출루 허용 (CSV ROE).</summary>
        public int ReachedOnError;
        public int Balks;
        public int WildPitches;
        /// <summary>9이닝당 실점.</summary>
        public double RunsPer9;
        /// <summary>대체 수준 대비 실점 (CSV rRA).</summary>
        public double ReplacementRunsAllowed;
        public double ReplacementRunsPer9;
        /// <summary>수비 무관 평균자책. 0이면 기록 없음.</summary>
        public double Fip;
        /// <summary>이닝당 출루 허용률. 0이면 기록 없음.</summary>
        public double Whip;

        public bool IsPitcher => Role == RosterRole.Starter || Role == RosterRole.Bullpen;

        /// <summary>모든 기록을 그대로 두고 역할·순서·위치만 바꾼 사본 (라인업 카드 적용용).</summary>
        /// <summary>나이·폼만 바꾼 사본 (Phase 29 — 원본 DB 선수는 건드리지 않는다).</summary>
        public PlayerData WithProgression(int age, double form)
        {
            var c = (PlayerData)MemberwiseClone();
            c.Age = age;
            c.Form = ProgressionModifier.ClampForm(form);
            return c;
        }

        public PlayerData CloneAs(RosterRole role, int order, string position)
        {
            var c = (PlayerData)MemberwiseClone();
            c.Role = role;
            c.Order = order;
            c.Position = position;
            return c;
        }
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

    /// <summary>
    /// 한 구단의 오늘 선수단 (1군). 각 목록은 Order 순으로 정렬돼 있다.
    /// <see cref="Reserves"/>는 1군 엔트리 밖의 퓨처스(2군) 선수 — 경기·라인업·정원·페이롤에 쓰지 않는다 (Phase 26.5).
    /// </summary>
    public sealed class TeamRoster
    {
        public readonly string Team;
        public readonly IReadOnlyList<PlayerData> Lineup;
        public readonly IReadOnlyList<PlayerData> Bench;
        public readonly IReadOnlyList<PlayerData> Starters;
        public readonly IReadOnlyList<PlayerData> Bullpen;
        /// <summary>퓨처스(2군) 선수 (벤치·불펜 순서 그대로 이어짐). 없으면 빈 목록.</summary>
        public readonly IReadOnlyList<PlayerData> Reserves;

        public TeamRoster(string team, IReadOnlyList<PlayerData> lineup, IReadOnlyList<PlayerData> bench,
            IReadOnlyList<PlayerData> starters, IReadOnlyList<PlayerData> bullpen, IReadOnlyList<PlayerData> reserves = null)
        {
            Team = team ?? throw new ArgumentNullException(nameof(team));
            Lineup = lineup ?? Array.Empty<PlayerData>();
            Bench = bench ?? Array.Empty<PlayerData>();
            Starters = starters ?? Array.Empty<PlayerData>();
            Bullpen = bullpen ?? Array.Empty<PlayerData>();
            Reserves = reserves ?? Array.Empty<PlayerData>();
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
