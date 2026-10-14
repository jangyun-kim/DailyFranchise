using DailyFranchise.Sim;
using DailyFranchise.UI.Data;

namespace DailyFranchise.UI
{
    /// <summary>
    /// [경기 종료] 때 경기 화면이 바깥으로 넘기는 결과. 점수는 화면 기준(원정 : 홈)이고,
    /// 화면에서 내 구단이 어느 쪽인지 함께 실어 시즌 일정의 원정·홈과 맞출 수 있게 한다.
    /// </summary>
    public readonly struct GameResult
    {
        public readonly GameScore Score;
        public readonly TeamSide MyTeamSide;

        public GameResult(GameScore score, TeamSide myTeamSide)
        {
            Score = score;
            MyTeamSide = myTeamSide;
        }

        public int MyRuns => MyTeamSide == TeamSide.Home ? Score.Home : Score.Away;
        public int OpponentRuns => MyTeamSide == TeamSide.Home ? Score.Away : Score.Home;

        public override string ToString() => $"{Score} (내 구단 {MyTeamSide})";
    }
}
