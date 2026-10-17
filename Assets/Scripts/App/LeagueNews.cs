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
        /// <summary>선수 은퇴 (Phase 29).</summary>
        Retirement = 10,
        /// <summary>오프시즌 폭풍 성장 (Phase 29).</summary>
        Breakout = 11,
        /// <summary>신인 드래프트 (Phase 30).</summary>
        Draft = 12,
        /// <summary>한국시리즈 개막·경기 결과 (Phase 32).</summary>
        Postseason = 13,
        /// <summary>한국시리즈 우승 (Phase 32).</summary>
        Championship = 14,
        /// <summary>1:1 트레이드 성사 (Phase 34).</summary>
        Trade = 15,
        /// <summary>경기 중 부상 (Phase 36).</summary>
        Injury = 16,
        /// <summary>부상 회복 (Phase 36).</summary>
        Recovery = 17,
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
        public const string RetirementTag = "[은퇴]";
        public const string BreakoutTag = "[성장]";
        public const string DraftTag = "[드래프트]";
        public const string PostseasonTag = "[한국시리즈]";
        public const string ChampionTag = "[챔피언]";
        public const string InjuryTag = "[부상]";
        public const string RecoveryTag = "[복귀]";
        public const string PerkTag = "[특전]";

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

        /// <summary>
        /// 1:1 트레이드: "삼성 라이온즈, LG 트윈스와 1:1 대형 트레이드 성사! (A 내주고 B 영입)" (A = 보낸 선수).
        /// Phase 35: 예전 "(A &lt;-&gt; B)"는 줄바꿈 엔진이 하이픈 뒤에서 끊어("&lt;-" / "&gt;") 읽기 어려웠다 — 하이픈 없는 문장으로 바꾸고,
        /// 괄호 덩어리는 줄바꿈 없는 공백(NBSP)으로 묶어 표시(<see cref="DisplayText"/>)할 때 한 줄로 내린다.
        /// </summary>
        public static NewsItem Traded(string team, string partner, string sentName, string receivedName, int year, string date) =>
            new NewsItem(NewsKind.Trade, OfficialTag, team,
                $"{team}, {WaGwa(partner)} 1:1 대형 트레이드 성사! " + Unbroken($"({sentName} 내주고 {receivedName} 영입)"), year, date);

        /// <summary>줄바꿈 없는 공백 (U+00A0).</summary>
        public const char NoBreakSpace = ' ';

        /// <summary>공백을 줄바꿈 없는 공백으로 바꿔 한 덩어리로 묶는다 (<see cref="DisplayText"/>가 이 덩어리를 끊지 않는다).</summary>
        public static string Unbroken(string text) => text?.Replace(' ', NoBreakSpace);

        /// <summary>
        /// 화면에 그릴 헤드라인: 줄바꿈 없는 공백으로 묶은 덩어리가 있으면 그 앞에서 줄을 바꿔 덩어리를 다음 줄에 통째로 둔다.
        /// UI Toolkit 글자 엔진은 한글을 글자 사이마다 끊을 수 있어 NBSP·&lt;nobr&gt;로도 "(표준혁 내" / "주고 …)" 끊김을 막지 못했다
        /// (Phase 35 실측). 저장·비교용 문장(<see cref="NewsItem.Headline"/>)은 그대로다.
        /// </summary>
        public static string DisplayText(string headline)
        {
            int nb = string.IsNullOrEmpty(headline) ? -1 : headline.IndexOf(NoBreakSpace);
            if (nb < 0) return headline;
            int space = headline.LastIndexOf(' ', nb);
            return space < 0 ? headline : headline.Substring(0, space) + "\n" + headline.Substring(space + 1);
        }

        /// <summary>"[부상] 삼성 라이온즈 구자욱, 경기 중 부상으로 4경기 결장!" (Phase 36).</summary>
        public static NewsItem Injured(string team, string playerName, int games, int year, string date) =>
            new NewsItem(NewsKind.Injury, InjuryTag, team, $"{team} {playerName}, 경기 중 부상으로 {games}경기 결장!", year, date);

        /// <summary>"[복귀] 삼성 라이온즈 구자욱, 부상 털고 퓨처스 복귀!" (Phase 36).</summary>
        public static NewsItem Recovered(string team, string playerName, int year, string date) =>
            new NewsItem(NewsKind.Recovery, RecoveryTag, team, $"{team} {playerName}, 부상 털고 퓨처스 복귀!", year, date);

        public static NewsItem NegotiationFailed(string team, string playerName, int year, string date) =>
            new NewsItem(NewsKind.NegotiationFailed, BreakingTag, team, $"{playerName}, {team}의 제안 거절… 협상 결렬", year, date);

        /// <summary>팬덤 레벨업 (Phase 42): "[특전] 삼성 라이온즈 팬덤 폭발! 구단 굿즈 완판으로 특별 예산 확보!".</summary>
        public static NewsItem FandomLevelUp(string team, FandomLevelUp reward, int year, string date) =>
            new NewsItem(NewsKind.Fandom, PerkTag, team, $"{team} 팬덤 폭발! 구단 굿즈 완판으로 특별 예산 확보!", year, date);

        /// <summary>[수뇌부 설득] 영입 (Phase 42).</summary>
        public static NewsItem Persuaded(string team, string playerName, long salary, int year, string date) =>
            new NewsItem(NewsKind.Signing, OfficialTag, team,
                $"{team}, 수뇌부 설득 끝에 캡을 넘겨 {playerName} 영입! (연봉 {FranchiseFinance.BudgetText(salary)})", year, date);

        /// <summary>CPU 구단의 FA 영입 (Phase 41): "[오피셜] LG 트윈스, 자유계약 시장에서 진세훈과 전격 계약!".</summary>
        public static NewsItem CpuSigned(string team, string playerName, int year, string date) =>
            new NewsItem(NewsKind.Signing, OfficialTag, team, $"{team}, 자유계약 시장에서 {WaGwa(playerName)} 전격 계약!", year, date);

        /// <summary>뉴스 한 줄에 이름을 적는 방출 선수 수 (나머지는 "외 N명").</summary>
        public const int CpuReleaseNames = 2;

        /// <summary>
        /// CPU 구단 방출 요약 (Phase 41): "[단독] 타 구단 베테랑 5명 방출, FA 시장 합류 (KIA 김선빈 · LG 김현수 외 3명)".
        /// </summary>
        public static NewsItem CpuReleases(IReadOnlyList<(string Team, string Name)> released, int year, string date)
        {
            var names = string.Join(" · ", released.Take(CpuReleaseNames).Select(r => $"{LobbyBriefing.TeamMark(r.Team)} {r.Name}"));
            string more = released.Count > CpuReleaseNames ? $" 외 {released.Count - CpuReleaseNames}명" : "";
            return new NewsItem(NewsKind.Release, ExclusiveTag, LeagueRosterBook.FreeAgentTeam,
                $"타 구단 베테랑 {released.Count}명 방출, FA 시장 합류 " + Unbroken($"({names}{more})"), year, date);
        }

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

        /// <summary>
        /// 은퇴: "삼성 라이온즈의 오승환, 정든 그라운드를 떠나 현역 은퇴 선언!" (자유계약 선수는 "자유계약 신분의 …").
        /// </summary>
        public static NewsItem Retired(string team, string playerName, int age, int year) =>
            new NewsItem(NewsKind.Retirement, RetirementTag, team,
                team == LeagueRosterBook.FreeAgentTeam
                    ? $"자유계약 신분의 {playerName}({age}세), 현역 은퇴 선언"
                    : $"{team}의 {playerName}, 정든 그라운드를 떠나 현역 은퇴 선언!",
                year, StoveLeagueDate);

        /// <summary>폭풍 성장: "삼성 라이온즈 김영웅, 비시즌 폭풍 성장! 기량 +3%".</summary>
        public static NewsItem Breakout(string team, string playerName, double gain, int year) =>
            new NewsItem(NewsKind.Breakout, BreakoutTag, team,
                $"{(team == LeagueRosterBook.FreeAgentTeam ? "자유계약" : team)} {playerName}, 비시즌 폭풍 성장! 기량 {AgingCurve.FormText(gain)}",
                year, StoveLeagueDate);

        /// <summary>
        /// 신인 지명: "삼성 라이온즈, 1라운드에서 특급 유망주 김민준(포수) 지명!" (2라운드부터는 "유망주").
        /// </summary>
        public static NewsItem Drafted(string team, string playerName, string position, int round, int year) =>
            new NewsItem(NewsKind.Draft, DraftTag, team,
                $"{team}, {round}라운드에서 {(round <= 1 ? "특급 유망주" : "유망주")} {playerName}({PositionMatcher.Label(position)}) 지명!",
                year, StoveLeagueDate);

        /// <summary>
        /// 한국시리즈 개막: 내 구단이 나가면 "삼성 라이온즈, 2026 한국시리즈 진출! LG 트윈스와 7전 4선승제 격돌",
        /// 아니면 "2026 한국시리즈 개막: KIA 타이거즈 vs LG 트윈스 · 삼성 라이온즈 가을야구 탈락".
        /// </summary>
        public static NewsItem PostseasonBegins(PostseasonSeries series, string myTeam, int year)
        {
            string date = series.Games.Count > 0 ? series.Games[0].Date : StoveLeagueDate;
            return series.Involves(myTeam)
                ? new NewsItem(NewsKind.Postseason, PostseasonTag, myTeam,
                    $"{myTeam}, {year} 한국시리즈 진출! {WaGwa(series.OpponentOf(myTeam))} 7전 4선승제 격돌", year, date)
                : new NewsItem(NewsKind.Postseason, PostseasonTag, series.TopSeed,
                    $"{year} 한국시리즈 개막: {series.TopSeed} vs {series.SecondSeed} · {myTeam} 가을야구 탈락", year, date);
        }

        /// <summary>
        /// 내 한국시리즈 경기 결과: "삼성 라이온즈, 한국시리즈 3차전 LG 트윈스에 5:3 승리 · 시리즈 2승 1패"
        /// (끝내기면 "9회말 끝내기 승리", 무승부면 "무승부").
        /// </summary>
        public static NewsItem SeriesGame(string myTeam, ScheduledGame game, PostseasonSeries series, int finalInning, bool walkOff, int year)
        {
            var record = new GameRecord { AwayTeam = game.AwayTeam, HomeTeam = game.HomeTeam, Score = game.Score.Value };
            string opp = game.AwayTeam == myTeam ? game.HomeTeam : game.AwayTeam;
            var outcome = record.OutcomeFor(myTeam);
            string s = ScoreText(record.RunsFor(myTeam), record.RunsAgainst(myTeam));
            string result = outcome == GameOutcome.Draw ? $"{WaGwa(opp)} {s} 무승부"
                : walkOff ? $"{opp}에 {finalInning}회말 {s} 끝내기 {(outcome == GameOutcome.Win ? "승리" : "패배")}"
                : $"{opp}에 {s} {(outcome == GameOutcome.Win ? "승리" : "패배")}";
            int mine = series.WinsOf(myTeam), theirs = series.WinsOf(opp);
            return new NewsItem(NewsKind.Postseason, PostseasonTag, myTeam,
                $"{myTeam}, 한국시리즈 {game.Number}차전 {result} · 시리즈 {mine}승 {theirs}패", year, game.Date);
        }

        /// <summary>우승: "삼성 라이온즈, 2026 시즌 대망의 한국시리즈 우승!".</summary>
        public static NewsItem Champion(string champion, PostseasonSeries series, int year) =>
            new NewsItem(NewsKind.Championship, ChampionTag, champion, $"{champion}, {year} 시즌 대망의 한국시리즈 우승!",
                year, series?.LastPlayed?.Date ?? StoveLeagueDate);

        /// <summary>시즌 결산: "2026 시즌 종료! 2027 스토브리그 개막 · 삼성 라이온즈 최종 3위".</summary>
        public static NewsItem SeasonClosed(string myTeam, SeasonRollover summary) =>
            new NewsItem(NewsKind.Offseason, LeagueTag, myTeam,
                summary.Rank > 0
                    ? $"{summary.Year} 시즌 종료! {summary.Year + 1} 스토브리그 개막 · {myTeam} 최종 {summary.Rank}위"
                    : $"{summary.Year} 시즌 종료! {summary.Year + 1} 스토브리그 개막",
                summary.Year + 1, StoveLeagueDate);
    }
}
