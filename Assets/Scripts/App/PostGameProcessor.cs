using System;
using System.Collections.Generic;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>경기 종료 처리 한 번의 결과 (로그·테스트용).</summary>
    public sealed class PostGameReport
    {
        public ScheduledGame Game;
        public GameScore Score;
        public GameOutcome MyOutcome;
        /// <summary>이 경기 내 구단 득점·실점 (시즌 누적 RS/RA에 더해진 값).</summary>
        public int MyRunsScored;
        public int MyRunsAllowed;
        /// <summary>반영 직전 / 직후 내 구단 순위.</summary>
        public int RankBefore;
        public int RankAfter;
        /// <summary>그날 경기 결과 전체 (첫 항목이 내 경기).</summary>
        public IReadOnlyList<GameRecord> DayResults;
        /// <summary>반영 직전 / 직후 예산·팬 만족도와 실제 변화량 (범위 제한 뒤).</summary>
        public int BudgetBefore;
        public int BudgetAfter;
        public int FanApprovalBefore;
        public int FanApprovalAfter;
        public FinanceChange Finance;
        /// <summary>이 경기 재정에 적용된 팬 만족도 구간(경기 전 기준)과 해금 슬롯 (Phase 23).</summary>
        public FanMood MoodBefore;
        public FandomSlot SlotsApplied;
        /// <summary>이 경기 뒤 새로 해금된 팬덤 슬롯.</summary>
        public FandomSlot NewlyUnlocked;
        /// <summary>
        /// 컨디션 변화 (경기를 치른 두 구단 선수, ID 순). 출장 기록 없이 처리했으면 비어 있다.
        /// 타 구단끼리의 경기는 점수만 만들므로 그 선수들의 컨디션은 바뀌지 않는다.
        /// </summary>
        public IReadOnlyList<ConditionChange> Conditions = new List<ConditionChange>();
        /// <summary>바로 저장에 성공했는지. 디스크 오류면 false (진행 상황은 메모리에 남아 다음 저장 때 다시 쓴다).</summary>
        public bool Saved;
    }

    /// <summary>
    /// 경기가 끝난 뒤의 시즌 진행. 씬과 무관한 순수 C#이라 EditMode에서 그대로 검증한다.
    /// 1) 진행 중인 경기를 최종 점수로 끝내 내 구단·상대의 승/패/무와 득점·실점(RS/RA)을 기록한다.
    /// 2) 같은 날 타 구단 경기를 난수로 만들어 함께 기록한다 (내 구단만 순위·득실점이 쌓이지 않게).
    /// 3) 순위표를 다시 정렬하고 일정을 다음 경기로 전진한다.
    /// 4) 내 구단 승/무/패로 예산(관중 수입)·팬 만족도를 바꾼다. 경기 전 팬 만족도 구간(관중 배수)과 해금한 팬덤 슬롯을 반영하고
    ///    (<see cref="FranchiseFinance.For(GameOutcome, FanMood, FandomSlot)"/>, 범위 제한), 그 뒤 슬롯 해금을 갱신한다.
    /// 5) 출장 기록이 있으면 두 구단 선수 컨디션을 바꾼다 (<see cref="ConditionRules"/>, 전용 난수).
    /// 6) 곧바로 저장해, 로비로 돌아가기 전에 꺼져도 결과가 남는다.
    /// </summary>
    public static class PostGameProcessor
    {
        /// <param name="finalScore">내 경기 최종 점수 (일정의 원정 : 홈 기준).</param>
        /// <param name="leagueRandom">타 구단 대진·결과 난수.</param>
        /// <param name="saves">저장 대상. null이면 저장하지 않는다.</param>
        /// <param name="participation">경기 화면의 출장 기록. null이면 컨디션을 바꾸지 않는다.</param>
        /// <param name="conditionRandom">
        /// 컨디션 하락 판정 난수. 타 구단 경기 난수(<paramref name="leagueRandom"/>)와 분리해, 컨디션 판정이 순위 결과를 바꾸지 않게 한다.
        /// </param>
        public static PostGameReport Process(SeasonState season, GameScore finalScore, IRandomSource leagueRandom, SaveManager saves,
            GameParticipation participation = null, IRandomSource conditionRandom = null)
        {
            if (participation != null && conditionRandom == null)
                throw new ArgumentNullException(nameof(conditionRandom), "출장 기록이 있으면 컨디션 난수가 필요하다.");
            if (season == null) throw new ArgumentNullException(nameof(season));
            var game = season.InProgress ?? throw new InvalidOperationException("진행 중인 경기가 없다.");
            int rankBefore = season.MyStanding?.Rank ?? 0;

            season.FinishCurrentGame(finalScore, leagueRandom);
            var mine = season.LastDayResults[0];
            var outcome = mine.OutcomeFor(season.MyTeam);
            int budgetBefore = season.Budget, fanBefore = season.FanApproval;
            var mood = FranchiseFinance.MoodOf(fanBefore);
            var slots = season.UnlockedSlots;
            var finance = season.ApplyFinance(FranchiseFinance.For(outcome, mood, slots));
            var unlocked = season.UpdateFandomUnlocks();
            var conditions = participation == null ? new List<ConditionChange>() : season.ApplyConditions(participation, conditionRandom);

            return new PostGameReport
            {
                Game = game,
                Score = finalScore,
                MyOutcome = outcome,
                BudgetBefore = budgetBefore,
                BudgetAfter = season.Budget,
                FanApprovalBefore = fanBefore,
                FanApprovalAfter = season.FanApproval,
                Finance = finance,
                MoodBefore = mood,
                SlotsApplied = slots,
                NewlyUnlocked = unlocked,
                Conditions = conditions,
                MyRunsScored = mine.RunsFor(season.MyTeam),
                MyRunsAllowed = mine.RunsAgainst(season.MyTeam),
                RankBefore = rankBefore,
                RankAfter = season.MyStanding?.Rank ?? 0,
                DayResults = new List<GameRecord>(season.LastDayResults),
                Saved = saves != null && saves.Save(season),
            };
        }
    }
}
