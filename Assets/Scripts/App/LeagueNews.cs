using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;

namespace DailyFranchise.App
{
    /// <summary>리그 뉴스 종류 (Phase 28). 저장 파일에는 정수로 남는다 — 순서를 바꾸지 말고 뒤에만 붙인다.</summary>
    public enum NewsKind
    {
        /// <summary>모르는 종류 (저장 파일의 알 수 없는 값).</summary>
        General = 0,
        /// <summary>내 구단 경기 결과.</summary>
        Game = 1,
        /// <summary>타 구단 경기·리그 소식.</summary>
        League = 2,
        /// <summary>순위 변동 (선두 등극 등).</summary>
        Standings = 3,
        /// <summary>FA 영입.</summary>
        Signing = 4,
        /// <summary>FA 협상 결렬.</summary>
        NegotiationFailed = 5,
        /// <summary>선수 방출.</summary>
        Release = 6,
        /// <summary>케미스트리 발동.</summary>
        Chemistry = 7,
        /// <summary>팬덤 특전 해금.</summary>
        Fandom = 8,
        /// <summary>시즌 결산·오프시즌.</summary>
        Offseason = 9,
    }

    /// <summary>
    /// 리그 뉴스 한 건 (Phase 28). 말머리("[오피셜]")와 본문, 관련 구단을 따로 들고 있어 CPU 구단 소식(추후 CPU 영입·방출)도
    /// 같은 틀로 담는다. 날짜는 일정 표기("5.19 (화)") 또는 "스토브리그".
    /// </summary>
    public sealed class NewsItem
    {
        public NewsItem(NewsKind kind, string tag, string team, string headline, int year, string date)
        {
            if (string.IsNullOrEmpty(headline)) throw new ArgumentException("본문이 비어 있다.", nameof(headline));
            Kind = kind;
            Tag = tag ?? "";
            Team = team ?? "";
            Headline = headline;
            Year = year;
            Date = date ?? "";
        }

        public NewsKind Kind { get; }
        /// <summary>말머리 ("[오피셜]", "[단독]" …). 없으면 "".</summary>
        public string Tag { get; }
        /// <summary>관련 구단 (내 구단 또는 CPU 구단). 없으면 "".</summary>
        public string Team { get; }
        /// <summary>말머리를 뺀 본문.</summary>
        public string Headline { get; }
        public int Year { get; }
        public string Date { get; }

        /// <summary>말머리 + 본문: "[오피셜] 삼성 라이온즈, FA 시장에서 구자욱과 ₩ 15억에 전격 계약!".</summary>
        public string Text => Tag.Length == 0 ? Headline : Tag + " " + Headline;

        public override string ToString() => Text;
    }

    /// <summary>
    /// 리그 뉴스 문장 틀 (Phase 28). 문장 생성기 없이 변수 보간으로만 만든다. 조사(와/과·을/를)는 앞 글자 받침으로 고른다.
    /// 경기 뉴스는 한 경기에 한 건 — 끝내기 → 무승부 → 난타전 → 대승/대패 → 영봉 → 연장 → 일반(3연승·연패 이상이면 덧붙임) 순으로 고른다.
    /// </summary>
    public static class NewsWriter
    {
        public const string OfficialTag = "[오피셜]";
        public const string ExclusiveTag = "[단독]";
        public const string BreakingTag = "[속보]";
        public const string WalkOffTag = "[끝내기]";
        public const string GameTag = "[경기]";
        public const string LeagueTag = "[리그]";
        public const string StandingsTag = "[순위]";
        public const string LineupTag = "[라인업]";
        public const string ClubTag = "[구단]";

        /// <summary>두 팀 득점 합이 이 이상이면 난타전.</summary>
        public const int SlugfestRuns = 15;
        /// <summary>점수 차가 이 이상이면 대승/대패.</summary>
        public const int BlowoutMargin = 7;
        /// <summary>타 구단 경기는 점수 차가 이 이상일 때만 소식으로 낸다 (하루 최대 한 건).</summary>
        public const int LeagueBlowoutMargin = 6;
        /// <summary>이 이상 연승·연패면 일반 경기 뉴스에 덧붙인다.</summary>
        public const int StreakToMention = 3;
        /// <summary>오프시즌 뉴스 날짜 표기.</summary>
        public const string StoveLeagueDate = "스토브리그";

        /// <summary>앞말 마지막 글자에 받침이 있는지. 한글이 아니면 숫자만 읽고(영·일·삼·육·칠·팔) 나머지는 받침 없음으로 본다.</summary>
        public static bool HasBatchim(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            char c = word[word.Length - 1];
            if (c >= '가' && c <= '힣') return (c - '가') % 28 != 0;
            return "013678".IndexOf(c) >= 0;
        }

        /// <summary>"구자욱과" / "김지찬과" / "코너와".</summary>
        public static string WaGwa(string word) => word + (HasBatchim(word) ? "과" : "와");

        /// <summary>승리 팀 점수를 앞에 쓴 스코어 ("5:3"). 무승부는 그대로.</summary>
        static string ScoreText(int a, int b) => a >= b ? $"{a}:{b}" : $"{b}:{a}";

        /// <summary>
        /// 내 경기 결과 뉴스 한 건. <paramref name="finalInning"/>은 경기가 끝난 이닝(모르면 0),
        /// <paramref name="walkOff"/>는 9회 이후 말 공격에서 홈이 앞서며 끝났는지. <paramref name="streak"/>은 이 경기를 포함한
        /// 연승(+)·연패(−) 수.
        /// </summary>
        public static NewsItem Game(string myTeam, GameRecord game, int finalInning, bool walkOff, int streak, int year, string date)
        {
            string opp = game.AwayTeam == myTeam ? game.HomeTeam : game.AwayTeam;
            int my = game.RunsFor(myTeam), their = game.RunsAgainst(myTeam);
            var outcome = game.OutcomeFor(myTeam);
            string s = ScoreText(my, their);
            bool win = outcome == GameOutcome.Win;
            int total = my + their, margin = Math.Abs(my - their);
            bool extra = finalInning > PlayByPlayGenerator.FinalInning;

            string tag = GameTag, text;
            if (walkOff && outcome != GameOutcome.Draw)
            {
                tag = WalkOffTag;
                text = win
                    ? $"{myTeam}, {WaGwa(opp)}의 접전 끝에 {finalInning}회말 {s} 끝내기 승리!"
                    : $"{myTeam}, {opp}에 {finalInning}회말 {s} 끝내기 패배…";
            }
            else if (outcome == GameOutcome.Draw)
                text = extra ? $"{myTeam}, {WaGwa(opp)} {finalInning}회 연장 혈투 끝 {s} 무승부" : $"{myTeam}, {WaGwa(opp)} {s} 무승부";
            else if (total >= SlugfestRuns)
                text = $"{myTeam}, {WaGwa(opp)}의 난타전 끝에 {s} {(win ? "승리!" : "패배")}";
            else if (margin >= BlowoutMargin)
                text = win ? $"{myTeam}, {opp} 상대로 {s} 대승!" : $"{myTeam}, {opp}에 {s} 대패… 마운드 붕괴";
            else if (their == 0)
                text = $"{myTeam} 마운드, {opp} 타선 봉쇄! {s} 영봉승";
            else if (my == 0)
                text = $"{myTeam} 타선 침묵… {opp}에 {s} 영봉패";
            else if (extra)
                text = win ? $"{myTeam}, 연장 {finalInning}회 승부 끝에 {opp} 꺾고 {s} 승리" : $"{myTeam}, {opp}에 연장 {finalInning}회 {s} 패배";
            else
            {
                text = win ? $"{myTeam}, {opp}에 {s} 승리" : $"{myTeam}, {opp}에 {s} 패배";
                if (Math.Abs(streak) >= StreakToMention) text += streak > 0 ? $" · {streak}연승 질주" : $" · {-streak}연패 수렁";
            }
            return new NewsItem(NewsKind.Game, tag, myTeam, text, year, date);
        }

        /// <summary>그날 타 구단 경기 중 점수 차가 가장 큰 경기가 <see cref="LeagueBlowoutMargin"/> 이상이면 한 건, 아니면 null.</summary>
        public static NewsItem LeagueHighlight(IEnumerable<GameRecord> otherGames, int year, string date)
        {
            var best = otherGames?.Where(g => g != null)
                .OrderByDescending(g => Math.Abs(g.Score.Away - g.Score.Home)).FirstOrDefault();
            if (best == null || Math.Abs(best.Score.Away - best.Score.Home) < LeagueBlowoutMargin) return null;
            bool awayWon = best.Score.Away > best.Score.Home;
            string winner = awayWon ? best.AwayTeam : best.HomeTeam, loser = awayWon ? best.HomeTeam : best.AwayTeam;
            return new NewsItem(NewsKind.League, LeagueTag, winner,
                $"{winner}, {loser}에 {ScoreText(best.Score.Away, best.Score.Home)} 대승", year, date);
        }

        /// <summary>선두 등극 (직전 1위가 아니었고 지금 1위). 공동 1위면 "공동 선두".</summary>
        public static NewsItem TookLead(string myTeam, bool shared, int year, string date) =>
            new NewsItem(NewsKind.Standings, StandingsTag, myTeam, shared ? $"{myTeam}, 공동 선두 도약!" : $"{myTeam}, 단독 선두 등극!", year, date);

        public static NewsItem FandomUnlocked(string myTeam, FandomSlot slot, int year, string date) =>
            new NewsItem(NewsKind.Fandom, ClubTag, myTeam, $"{myTeam} 팬덤 특전 '{FandomRules.Label(slot)}' 해금!", year, date);

        /// <summary>FA 영입. 확정 영입이면 TP 카드를 쓴 문장.</summary>
        public static NewsItem Signed(string team, string playerName, long salary, bool ceiling, int year, string date) =>
            new NewsItem(NewsKind.Signing, OfficialTag, team, ceiling
                ? $"{team}, 확정 영입 카드로 {playerName} 영입! (연봉 {FranchiseFinance.BudgetText(salary)})"
                : $"{team}, FA 시장에서 {WaGwa(playerName)} {FranchiseFinance.BudgetText(salary)}에 전격 계약!", year, date);

        public static NewsItem NegotiationFailed(string team, string playerName, int year, string date) =>
            new NewsItem(NewsKind.NegotiationFailed, BreakingTag, team, $"{playerName}, {team}의 제안 거절… 협상 결렬", year, date);

        public static NewsItem Released(string team, string playerName, int year, string date) =>
            new NewsItem(NewsKind.Release, ExclusiveTag, team, $"{team}, 성적 부진으로 {playerName} 방출 결정.", year, date);

        public static NewsItem ChemistryActivated(string team, ChemistryKind kind, int year, string date) =>
            new NewsItem(NewsKind.Chemistry, LineupTag, team, $"{team}, '{ChemistryRules.Label(kind)}' 케미스트리 발동!", year, date);

        /// <summary>선수단이 바뀐 뒤 새로 발동한 케미스트리마다 한 건 (전에도 발동 중이던 종류는 빼고, 열거형 순서).</summary>
        public static List<NewsItem> NewChemistry(string team, IEnumerable<ChemistryKind> before, ActiveChemistry after, int year, string date)
        {
            var old = new HashSet<ChemistryKind>(before ?? Enumerable.Empty<ChemistryKind>());
            return (after?.Kinds ?? new List<ChemistryKind>()).Where(k => !old.Contains(k))
                .Select(k => ChemistryActivated(team, k, year, date)).ToList();
        }

        /// <summary>시즌 결산: "2026 시즌 종료! 2027 스토브리그 개막 · 삼성 라이온즈 최종 3위".</summary>
        public static NewsItem SeasonClosed(string myTeam, SeasonRollover summary) =>
            new NewsItem(NewsKind.Offseason, LeagueTag, myTeam,
                summary.Rank > 0
                    ? $"{summary.Year} 시즌 종료! {summary.Year + 1} 스토브리그 개막 · {myTeam} 최종 {summary.Rank}위"
                    : $"{summary.Year} 시즌 종료! {summary.Year + 1} 스토브리그 개막",
                summary.Year + 1, StoveLeagueDate);
    }
}
