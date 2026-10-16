using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim;

namespace DailyFranchise.App
{
    /// <summary>시즌 진행 단계 (Phase 32). 저장 파일에는 정수로 남는다 — 없는 이전 파일은 0(정규시즌).</summary>
    public enum SeasonPhase
    {
        /// <summary>정규시즌 일정 진행 중 (일정을 다 치르면 곧바로 포스트시즌으로).</summary>
        RegularSeason = 0,
        /// <summary>한국시리즈 진행 중.</summary>
        PostSeason = 1,
        /// <summary>우승 팀이 정해졌고 결산(<see cref="SeasonState.TransitionToNextSeason"/>)을 기다린다.</summary>
        OffSeason = 2,
    }

    /// <summary>
    /// 한국시리즈 (Phase 32): 정규시즌 1위(<see cref="TopSeed"/>)와 2위(<see cref="SecondSeed"/>)의 7전 4선승제. 순수 C#.
    /// - 홈: 1·2·6·7차전 1위 구장, 3·4·5차전 2위 구장 (2-3-2). 날짜는 정규시즌 마지막 경기 이틀 뒤부터, 2·5차전 뒤 하루 이동일.
    /// - 한 팀이 <see cref="WinsToClinch"/>승에 닿으면 남은 경기를 바로 지운다.
    /// - 무승부는 어느 쪽 승리도 아니다. 7경기를 다 치렀는데 우승 팀이 없으면(무승부 때문에) 1위 구장에서 한 경기를 더 잡는다.
    /// 시리즈 경기는 정규시즌 순위표에 반영하지 않는다.
    /// </summary>
    public sealed class PostseasonSeries
    {
        public const int WinsToClinch = 4;
        public const int ScheduledGames = 7;
        public const string Title = "한국시리즈";

        readonly List<ScheduledGame> _games;

        public PostseasonSeries(int year, string topSeed, string secondSeed, IEnumerable<ScheduledGame> games, int nextIndex = 0)
        {
            if (string.IsNullOrEmpty(topSeed) || string.IsNullOrEmpty(secondSeed) || topSeed == secondSeed)
                throw new ArgumentException("시리즈 두 팀이 잘못됐다.");
            Year = year;
            TopSeed = topSeed;
            SecondSeed = secondSeed;
            _games = (games ?? throw new ArgumentNullException(nameof(games))).ToList();
            NextIndex = Math.Max(0, Math.Min(nextIndex, _games.Count));
            Recount();
        }

        /// <summary>시즌 연도 (날짜 요일 계산용).</summary>
        public int Year { get; }
        /// <summary>정규시즌 1위 (홈 어드밴티지).</summary>
        public string TopSeed { get; }
        /// <summary>정규시즌 2위.</summary>
        public string SecondSeed { get; }
        public IReadOnlyList<ScheduledGame> Games => _games;
        /// <summary>다음 경기 인덱스 (= 치른 경기 수).</summary>
        public int NextIndex { get; private set; }
        public int TopWins { get; private set; }
        public int SecondWins { get; private set; }
        public int Draws { get; private set; }

        public string Champion => TopWins >= WinsToClinch ? TopSeed : SecondWins >= WinsToClinch ? SecondSeed : null;
        public bool IsOver => Champion != null;
        public ScheduledGame NextGame => IsOver || NextIndex >= _games.Count ? null : _games[NextIndex];
        /// <summary>가장 최근에 끝난 시리즈 경기. 없으면 null.</summary>
        public ScheduledGame LastPlayed => NextIndex > 0 ? _games[NextIndex - 1] : null;

        public bool Involves(string team) => team == TopSeed || team == SecondSeed;
        public string OpponentOf(string team) => team == TopSeed ? SecondSeed : team == SecondSeed ? TopSeed : null;
        public int WinsOf(string team) => team == TopSeed ? TopWins : team == SecondSeed ? SecondWins : 0;

        /// <summary>1·2위로 시리즈를 짠다. <paramref name="lastRegularDate"/>("5.26 (화)")를 못 읽으면 날짜는 "1차전" 꼴.</summary>
        public static PostseasonSeries Create(string topSeed, string secondSeed, int year, string lastRegularDate,
            IReadOnlyDictionary<string, string> stadiums = null)
        {
            var games = new List<ScheduledGame>();
            var start = SeasonState.DateOf(lastRegularDate, year);
            var day = start?.AddDays(2);
            for (int n = 1; n <= ScheduledGames; n++)
            {
                games.Add(NewGame(n, topSeed, secondSeed, day, stadiums));
                if (day.HasValue) day = day.Value.AddDays(n == 2 || n == 5 ? 2 : 1);
            }
            return new PostseasonSeries(year, topSeed, secondSeed, games);
        }

        /// <summary>n차전: 3·4·5차전은 2위 홈, 나머지(1·2·6·7, 추가 경기)는 1위 홈.</summary>
        static ScheduledGame NewGame(int number, string top, string second, DateTime? day, IReadOnlyDictionary<string, string> stadiums)
        {
            bool secondHome = number >= 3 && number <= 5;
            string home = secondHome ? second : top, away = secondHome ? top : second;
            return new ScheduledGame
            {
                Number = number,
                Date = day.HasValue ? SeasonState.DateText(day.Value) : $"{number}차전",
                AwayTeam = away,
                HomeTeam = home,
                Stadium = stadiums != null && stadiums.TryGetValue(home, out var s) ? s : "",
            };
        }

        /// <summary>다음 경기 결과를 기록한다. 우승이 정해지면 남은 경기를 지우고, 무승부로 경기가 모자라면 한 경기를 더한다.</summary>
        public void Record(GameScore score)
        {
            var game = NextGame ?? throw new InvalidOperationException("남은 시리즈 경기가 없다.");
            game.Score = score;
            game.Status = GameStatus.Final;
            NextIndex++;
            Recount();
            if (IsOver)
            {
                _games.RemoveRange(NextIndex, _games.Count - NextIndex);
                return;
            }
            if (NextIndex >= _games.Count)
            {
                var last = SeasonState.DateOf(game.Date, Year);
                _games.Add(NewGame(_games.Count + 1, TopSeed, SecondSeed, last?.AddDays(1), null));
                _games[_games.Count - 1].Stadium = _games[0].Stadium;
            }
        }

        void Recount()
        {
            TopWins = SecondWins = Draws = 0;
            for (int i = 0; i < NextIndex; i++)
            {
                var g = _games[i];
                if (!g.Score.HasValue) continue;
                var s = g.Score.Value;
                if (s.Away == s.Home) { Draws++; continue; }
                string winner = s.Away > s.Home ? g.AwayTeam : g.HomeTeam;
                if (winner == TopSeed) TopWins++;
                else SecondWins++;
            }
        }

        /// <summary>"삼성 2승 - 1승 LG" (내 구단이 있으면 내 구단을 앞에). 약칭은 구단 이름 첫 단어.</summary>
        public string RecordText(string myTeam = null)
        {
            bool flip = myTeam == SecondSeed;
            string a = flip ? SecondSeed : TopSeed, b = flip ? TopSeed : SecondSeed;
            string text = $"{Short(a)} {WinsOf(a)}승 - {WinsOf(b)}승 {Short(b)}";
            return Draws > 0 ? $"{text} ({Draws}무)" : text;
        }

        static string Short(string team) => string.IsNullOrEmpty(team) ? "" : team.Split(' ')[0];
    }
}
