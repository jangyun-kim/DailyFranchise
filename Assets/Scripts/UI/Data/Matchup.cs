using System;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.UI.Data
{
    /// <summary>
    /// 경기 화면에 그릴 대진. 원정·홈은 일정표 기준이고, 내 구단이 어느 쪽인지 함께 싣는다.
    /// 바깥(시즌 흐름)이 넘기지 않으면 경기 화면은 <see cref="Default"/>(서울 원정 vs 부산 홈)로 그린다.
    /// 선수단(<see cref="TeamRoster"/>)이 실려 있으면 경기 화면은 더미 이름 대신 그 선수들로 그린다
    /// (<see cref="ScenarioRosterSource"/>). 선수단이 없는 쪽은 더미 이름을 그대로 쓴다.
    /// </summary>
    public sealed class Matchup
    {
        public readonly TeamInfo Away;
        public readonly TeamInfo Home;
        public readonly TeamSide MySide;
        /// <summary>원정 선수단. 없으면 null.</summary>
        public readonly TeamRoster AwayRoster;
        /// <summary>홈 선수단. 없으면 null.</summary>
        public readonly TeamRoster HomeRoster;

        public Matchup(TeamInfo away, TeamInfo home, TeamSide mySide, TeamRoster awayRoster = null, TeamRoster homeRoster = null)
        {
            Away = away ?? throw new ArgumentNullException(nameof(away));
            Home = home ?? throw new ArgumentNullException(nameof(home));
            MySide = mySide;
            AwayRoster = awayRoster;
            HomeRoster = homeRoster;
        }

        /// <summary>정식 팀명으로 만든다. 약칭은 연고지(첫 단어)다 (예: "부산 웨이브스" → "부산").</summary>
        public static Matchup FromNames(string awayTeam, string homeTeam, TeamSide mySide,
            TeamRoster awayRoster = null, TeamRoster homeRoster = null) =>
            new Matchup(TeamFromName(awayTeam), TeamFromName(homeTeam), mySide, awayRoster, homeRoster);

        /// <summary>씬 단독 실행용 기본 대진: 서울 블루윙스(원정) vs 부산 웨이브스(홈, 내 구단).</summary>
        public static Matchup Default => FromNames("서울 블루윙스", "부산 웨이브스", TeamSide.Home);

        public TeamInfo Get(TeamSide side) => side == TeamSide.Away ? Away : Home;

        public TeamRoster RosterOf(TeamSide side) => side == TeamSide.Away ? AwayRoster : HomeRoster;

        /// <summary>어느 쪽이든 선수단이 실려 있는지.</summary>
        public bool HasRosters => AwayRoster != null || HomeRoster != null;

        public override string ToString() => $"{Away.Name} vs {Home.Name} (내 구단 {MySide})";

        /// <summary>정식 팀명으로 팀 정보를 만든다. 약칭은 연고지(첫 단어).</summary>
        public static TeamInfo TeamFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("팀명이 비어 있다.", nameof(name));
            return new TeamInfo { Name = name, ShortName = name.Split(' ')[0] };
        }
    }
}
