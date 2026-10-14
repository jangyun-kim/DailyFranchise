using System;

namespace DailyFranchise.UI.Data
{
    /// <summary>
    /// 경기 화면에 그릴 대진. 원정·홈은 일정표 기준이고, 내 구단이 어느 쪽인지 함께 싣는다.
    /// 바깥(시즌 흐름)이 넘기지 않으면 경기 화면은 <see cref="Default"/>(서울 원정 vs 부산 홈)로 그린다.
    /// </summary>
    public sealed class Matchup
    {
        public readonly TeamInfo Away;
        public readonly TeamInfo Home;
        public readonly TeamSide MySide;

        public Matchup(TeamInfo away, TeamInfo home, TeamSide mySide)
        {
            Away = away ?? throw new ArgumentNullException(nameof(away));
            Home = home ?? throw new ArgumentNullException(nameof(home));
            MySide = mySide;
        }

        /// <summary>정식 팀명으로 만든다. 약칭은 연고지(첫 단어)다 (예: "부산 웨이브스" → "부산").</summary>
        public static Matchup FromNames(string awayTeam, string homeTeam, TeamSide mySide) =>
            new Matchup(Team(awayTeam), Team(homeTeam), mySide);

        /// <summary>씬 단독 실행용 기본 대진: 서울 블루윙스(원정) vs 부산 웨이브스(홈, 내 구단).</summary>
        public static Matchup Default => FromNames("서울 블루윙스", "부산 웨이브스", TeamSide.Home);

        public TeamInfo Get(TeamSide side) => side == TeamSide.Away ? Away : Home;

        public override string ToString() => $"{Away.Name} vs {Home.Name} (내 구단 {MySide})";

        static TeamInfo Team(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("팀명이 비어 있다.", nameof(name));
            return new TeamInfo { Name = name, ShortName = name.Split(' ')[0] };
        }
    }
}
