using System;
using System.Collections.Generic;
using DailyFranchise.Sim;

namespace DailyFranchise.App
{
    /// <summary>경기 종료 처리 한 번의 결과 (로그·테스트용).</summary>
    public sealed class PostGameReport
    {
        public ScheduledGame Game;
        public GameScore Score;
        public GameOutcome MyOutcome;
        /// <summary>반영 직전 / 직후 내 구단 순위.</summary>
        public int RankBefore;
        public int RankAfter;
        /// <summary>그날 경기 결과 전체 (첫 항목이 내 경기).</summary>
        public IReadOnlyList<GameRecord> DayResults;
        /// <summary>바로 저장에 성공했는지. 디스크 오류면 false (진행 상황은 메모리에 남아 다음 저장 때 다시 쓴다).</summary>
        public bool Saved;
    }

    /// <summary>
    /// 경기가 끝난 뒤의 시즌 진행. 씬과 무관한 순수 C#이라 EditMode에서 그대로 검증한다.
    /// 1) 진행 중인 경기를 최종 점수로 끝내 내 구단·상대의 승/패/무를 기록한다.
    /// 2) 같은 날 타 구단 경기를 난수로 만들어 함께 기록한다 (내 구단만 순위가 오르지 않게).
    /// 3) 순위표를 다시 정렬하고 일정을 다음 경기로 전진한다.
    /// 4) 곧바로 저장해, 로비로 돌아가기 전에 꺼져도 결과가 남는다.
    /// </summary>
    public static class PostGameProcessor
    {
        /// <param name="finalScore">내 경기 최종 점수 (일정의 원정 : 홈 기준).</param>
        /// <param name="leagueRandom">타 구단 대진·결과 난수.</param>
        /// <param name="saves">저장 대상. null이면 저장하지 않는다.</param>
        public static PostGameReport Process(SeasonState season, GameScore finalScore, IRandomSource leagueRandom, SaveManager saves)
        {
            if (season == null) throw new ArgumentNullException(nameof(season));
            var game = season.InProgress ?? throw new InvalidOperationException("진행 중인 경기가 없다.");
            int rankBefore = season.MyStanding?.Rank ?? 0;

            season.FinishCurrentGame(finalScore, leagueRandom);

            return new PostGameReport
            {
                Game = game,
                Score = finalScore,
                MyOutcome = season.MyOutcome(game).Value,
                RankBefore = rankBefore,
                RankAfter = season.MyStanding?.Rank ?? 0,
                DayResults = new List<GameRecord>(season.LastDayResults),
                Saved = saves != null && saves.Save(season),
            };
        }
    }
}
