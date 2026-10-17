using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>반영 직전 / 직후 예산·팬 만족도와 실제 변화량 (범위 제한 뒤). 예산은 long (Phase 26).</summary>
        public long BudgetBefore;
        public long BudgetAfter;
        public int FanApprovalBefore;
        public int FanApprovalAfter;
        public FinanceChange Finance;
        /// <summary>관중 수입 (팬 구간 배수·굿즈 포함, Phase 26 명세).</summary>
        public long Income;
        /// <summary>선수단 유지비 = 페이롤 ÷ 144 (Phase 26).</summary>
        public long PayrollExpense;
        /// <summary>순이익 = 관중 수입 − 유지비 (예산 0 아래로 내려가 잘리기 전 값).</summary>
        public long NetIncome => Income - PayrollExpense;

        /// <summary>"관중 수입 +₩5,000만 · 유지비 −₩2,791만 · 순이익 +₩2,208만".</summary>
        public string FinanceText =>
            $"관중 수입 {FranchiseFinance.BudgetDeltaText(Income)} · 유지비 {FranchiseFinance.BudgetDeltaText(-PayrollExpense)} · 순이익 {FranchiseFinance.BudgetDeltaText(NetIncome)}";
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
        /// <summary>시즌 개인 성적에 더한 박스스코어 (Phase 37). 없었거나 한국시리즈 경기면 null.</summary>
        public BoxScore Box;
        /// <summary>이 경기에서 다친 선수 (Phase 36, ID 순). 부상 난수·장부가 없으면 비어 있다.</summary>
        public IReadOnlyList<InjuryEvent> Injuries = new List<InjuryEvent>();
        /// <summary>이 경기를 끝으로 부상에서 회복한 선수 (Phase 36, ID 순).</summary>
        public IReadOnlyList<InjuryEvent> Recoveries = new List<InjuryEvent>();
        /// <summary>이 경기로 발행한 리그 뉴스 (Phase 28, 발행 순). 저장 전에 시즌 뉴스에 들어간다.</summary>
        public IReadOnlyList<NewsItem> News = new List<NewsItem>();
        /// <summary>한국시리즈 경기였는지 (Phase 32 — 정규시즌 순위표·타 구단 경기는 바뀌지 않는다).</summary>
        public bool Postseason;
        /// <summary>이 경기로 한국시리즈 우승 팀이 정해졌으면 그 팀, 아니면 null.</summary>
        public string Champion;
        /// <summary>내 구단이 우승해 받은 보상 (상금·팬 만족도 실제 변화). 없으면 null.</summary>
        public FinanceChange? ChampionAward;
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
    ///    같은 정산에서 선수단 유지비(페이롤 ÷ 144, <see cref="FranchiseFinance.PayrollPerGame"/>)를 뺀다 (Phase 26).
    /// 5) 출장 기록이 있으면 두 구단 선수 컨디션을 바꾼다 (<see cref="ConditionRules"/>, 전용 난수).
    /// 6) 리그 뉴스를 발행한다 (Phase 28, <see cref="NewsWriter"/>): 내 경기 결과(끝내기·난타전·대승 등) 한 건, 선두 등극,
    ///    새로 해금한 팬덤 특전, 그날 타 구단 경기 중 큰 점수 차 경기 한 건.
    /// 6.5) 부상 (Phase 36, <see cref="InjuryRules"/>): 먼저 부상 선수의 남은 경기를 1씩 줄여 0이면 회복시키고([복귀] 뉴스 — 내 구단만),
    ///    부상 난수와 장부가 있으면 출장 선수(두 구단)의 부상을 경기 전 컨디션으로 판정해 퓨처스로 내린다([부상] 뉴스).
    /// 6.7) 박스스코어가 있으면 시즌 개인 성적에 더한다 (Phase 37, 정규시즌 경기만 — 한국시리즈 기록은 시즌 성적에 넣지 않는다).
    /// 7) 곧바로 저장해, 로비로 돌아가기 전에 꺼져도 결과가 남는다.
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
        /// <param name="finalInning">경기가 끝난 이닝 (뉴스용, 모르면 0).</param>
        /// <param name="walkOff">9회 이후 말 공격에서 홈이 앞서며 끝났는지 (끝내기 뉴스).</param>
        /// <param name="injuryRandom">부상 판정 난수 (Phase 36). null이면 새 부상은 없다 (남은 경기는 그래도 준다).</param>
        /// <param name="book">경기 전 선수단 장부 (부상 선수의 소속·이름). null이면 새 부상은 없다.</param>
        /// <param name="boxScore">선수 DB ID 기준 박스스코어 (Phase 37). null이면 개인 성적이 바뀌지 않는다.</param>
        public static PostGameReport Process(SeasonState season, GameScore finalScore, IRandomSource leagueRandom, SaveManager saves,
            GameParticipation participation = null, IRandomSource conditionRandom = null, int finalInning = 0, bool walkOff = false,
            IRandomSource injuryRandom = null, LeagueRosterBook book = null, BoxScore boxScore = null)
        {
            if (participation != null && conditionRandom == null)
                throw new ArgumentNullException(nameof(conditionRandom), "출장 기록이 있으면 컨디션 난수가 필요하다.");
            if (season == null) throw new ArgumentNullException(nameof(season));
            var game = season.InProgress ?? throw new InvalidOperationException("진행 중인 경기가 없다.");
            int rankBefore = season.MyStanding?.Rank ?? 0;
            bool postseason = season.Phase == SeasonPhase.PostSeason;
            // 경기 전 재정 (한국시리즈 우승 상금이 경기 정산의 팬 구간을 바꾸지 않도록 먼저 읽는다)
            long budgetBefore = season.Budget;
            int fanBefore = season.FanApproval;
            var mood = FranchiseFinance.MoodOf(fanBefore);
            var slots = season.UnlockedSlots;

            season.FinishCurrentGame(finalScore, leagueRandom, finalInning, walkOff);
            var mine = season.LastDayResults[0];
            var outcome = mine.OutcomeFor(season.MyTeam);
            string champion = postseason && season.Phase == SeasonPhase.OffSeason ? season.Postseason?.Champion : null;
            var income = FranchiseFinance.For(outcome, mood, slots);
            long expense = FranchiseFinance.PayrollPerGame(season.Payroll);
            var finance = season.ApplyFinance(new FinanceChange(income.BudgetDelta - expense, income.FanDelta));
            var unlocked = season.UpdateFandomUnlocks();
            var conditions = participation == null ? new List<ConditionChange>() : season.ApplyConditions(participation, conditionRandom);
            var recoveries = season.TickInjuries(book);
            var injuries = participation != null && injuryRandom != null && book != null
                ? season.ApplyInjuries(participation, conditions, book, injuryRandom)
                : new List<InjuryEvent>();
            var news = postseason
                ? PublishPostseasonNews(season, game, unlocked)
                : PublishNews(season, game, mine, rankBefore, unlocked, finalInning, walkOff);
            news.AddRange(PublishInjuryNews(season, game, injuries, recoveries));
            var box = postseason ? null : boxScore;
            season.Stats.Add(box, game?.Date, game == null ? null : game.AwayTeam == season.MyTeam ? game.HomeTeam : game.AwayTeam);

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
                Income = income.BudgetDelta,
                PayrollExpense = expense,
                MoodBefore = mood,
                SlotsApplied = slots,
                NewlyUnlocked = unlocked,
                Conditions = conditions,
                Box = box,
                Injuries = injuries,
                Recoveries = recoveries,
                News = news,
                MyRunsScored = mine.RunsFor(season.MyTeam),
                MyRunsAllowed = mine.RunsAgainst(season.MyTeam),
                RankBefore = rankBefore,
                RankAfter = season.MyStanding?.Rank ?? 0,
                DayResults = new List<GameRecord>(season.LastDayResults),
                Postseason = postseason,
                Champion = champion,
                ChampionAward = champion != null && champion == season.MyTeam ? season.ChampionAward : null,
                Saved = saves != null && saves.Save(season),
            };
        }

        /// <summary>[복귀] (내 구단 선수만) → [부상] (두 구단) 뉴스를 시즌 피드에 넣고 돌려준다.</summary>
        static List<NewsItem> PublishInjuryNews(SeasonState season, ScheduledGame game, IEnumerable<InjuryEvent> injuries,
            IEnumerable<InjuryEvent> recoveries)
        {
            var news = recoveries.Where(r => r.Team == season.MyTeam)
                .Select(r => NewsWriter.Recovered(r.Team, r.Name, season.Year, game.Date))
                .Concat(injuries.Select(i => NewsWriter.Injured(i.Team, i.Name, i.Games, season.Year, game.Date)))
                .ToList();
            foreach (var n in news) season.AddNews(n);
            return news;
        }

        /// <summary>한국시리즈 경기: 시즌이 낸 경기 결과·우승 뉴스에 새 팬덤 특전 뉴스만 더한다 (선두 등극·타 구단 소식 없음).</summary>
        static List<NewsItem> PublishPostseasonNews(SeasonState season, ScheduledGame game, FandomSlot unlocked)
        {
            var news = new List<NewsItem>(season.LastPostseasonNews);
            foreach (var slot in FandomRules.Slots)
                if ((unlocked & slot) != 0)
                {
                    var n = NewsWriter.FandomUnlocked(season.MyTeam, slot, season.Year, game.Date);
                    season.AddNews(n);
                    news.Add(n);
                }
            return news;
        }

        static List<NewsItem> PublishNews(SeasonState season, ScheduledGame game, GameRecord mine, int rankBefore, FandomSlot unlocked,
            int finalInning, bool walkOff)
        {
            int year = season.Year;
            string date = game.Date;
            var news = new List<NewsItem>
            {
                NewsWriter.Game(season.MyTeam, mine, finalInning, walkOff, season.MyStreak(), year, date),
            };
            var me = season.MyStanding;
            if (me != null && me.Rank == 1 && rankBefore != 1)
                news.Add(NewsWriter.TookLead(season.MyTeam, season.Standings.Count(r => r.Rank == 1) > 1, year, date));
            foreach (var slot in FandomRules.Slots)
                if ((unlocked & slot) != 0) news.Add(NewsWriter.FandomUnlocked(season.MyTeam, slot, year, date));
            var league = NewsWriter.LeagueHighlight(season.LastDayResults.Skip(1), year, date);
            if (league != null) news.Add(league);
            foreach (var n in news) season.AddNews(n);
            return news;
        }
    }
}
