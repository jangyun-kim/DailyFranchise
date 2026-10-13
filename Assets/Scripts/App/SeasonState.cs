using System;
using System.Collections.Generic;

namespace DailyFranchise.App
{
    public enum GameStatus
    {
        Scheduled,
        InProgress,
        Final,
    }

    public sealed class ScheduledGame
    {
        public int Number;
        public string Date;
        public string AwayTeam;
        public string HomeTeam;
        public string Stadium;
        public GameStatus Status;

        public string MatchupText => $"{AwayTeam} vs {HomeTeam}";
    }

    public sealed class StandingRow
    {
        public int Rank;
        public string Team;
        public int Wins;
        public int Losses;
        public int Draws;
        /// <summary>승차 표기 (1위는 "—").</summary>
        public string GamesBehind;
    }

    /// <summary>
    /// 시즌 진행 상태. 씬과 무관한 순수 C#이며 SeasonManager가 씬 전환 동안 들고 있는다.
    /// 이번 범위는 "몇 번째 경기를 하는 중인지"만 다루고, 경기 결과를 순위에 반영하지 않는다.
    /// </summary>
    public sealed class SeasonState
    {
        readonly List<ScheduledGame> _schedule;
        readonly List<StandingRow> _standings;

        public SeasonState(string seasonTitle, string myTeam, List<ScheduledGame> schedule, List<StandingRow> standings)
        {
            if (schedule == null || schedule.Count == 0) throw new ArgumentException("일정이 비어 있다.", nameof(schedule));
            SeasonTitle = seasonTitle;
            MyTeam = myTeam;
            _schedule = schedule;
            _standings = standings;
        }

        public string SeasonTitle { get; }
        public string MyTeam { get; }
        public IReadOnlyList<ScheduledGame> Schedule => _schedule;
        public IReadOnlyList<StandingRow> Standings => _standings;

        /// <summary>다음에 치를 경기의 일정 인덱스. 시즌이 끝나면 Schedule.Count.</summary>
        public int NextIndex { get; private set; }
        /// <summary>진행 중인 경기. 로비에 있으면 null.</summary>
        public ScheduledGame InProgress { get; private set; }

        public ScheduledGame NextGame => NextIndex < _schedule.Count ? _schedule[NextIndex] : null;
        public bool IsSeasonOver => NextIndex >= _schedule.Count && InProgress == null;
        public int GamesPlayed => NextIndex;

        public StandingRow MyStanding
        {
            get
            {
                foreach (var row in _standings)
                    if (row.Team == MyTeam) return row;
                return null;
            }
        }

        public ScheduledGame StartNextGame()
        {
            if (InProgress != null) throw new InvalidOperationException($"이미 진행 중인 경기가 있다: {InProgress.Number}");
            var game = NextGame ?? throw new InvalidOperationException("남은 경기가 없다.");
            game.Status = GameStatus.InProgress;
            InProgress = game;
            return game;
        }

        public void FinishCurrentGame()
        {
            if (InProgress == null) throw new InvalidOperationException("진행 중인 경기가 없다.");
            InProgress.Status = GameStatus.Final;
            InProgress = null;
            NextIndex++;
        }

        /// <summary>가상 구단 10개, 내 구단 부산 웨이브스, 10경기 일정의 더미 시즌.</summary>
        public static SeasonState CreateDummy()
        {
            const string my = "부산 웨이브스";
            var opponents = new[]
            {
                "서울 블루윙스", "인천 하버스", "대구 썬더스", "광주 레드폭스", "대전 코메츠",
                "수원 나이츠", "창원 마리너스", "서울 그리폰스", "고양 스타즈", "서울 블루윙스",
            };
            var dates = new[]
            {
                "5.15 (금)", "5.16 (토)", "5.17 (일)", "5.19 (화)", "5.20 (수)",
                "5.21 (목)", "5.22 (금)", "5.23 (토)", "5.24 (일)", "5.26 (화)",
            };

            var schedule = new List<ScheduledGame>();
            for (int i = 0; i < opponents.Length; i++)
            {
                bool home = i % 3 != 1; // 첫 경기는 경기 화면 더미와 같은 서울(원정) vs 부산(홈)
                string away = home ? opponents[i] : my;
                string homeTeam = home ? my : opponents[i];
                schedule.Add(new ScheduledGame
                {
                    Number = i + 1,
                    Date = dates[i],
                    AwayTeam = away,
                    HomeTeam = homeTeam,
                    Stadium = homeTeam.Split(' ')[0] + " 구장",
                });
            }

            var standings = new List<StandingRow>
            {
                new StandingRow { Team = "대구 썬더스", Wins = 26, Losses = 15, Draws = 1 },
                new StandingRow { Team = "서울 블루윙스", Wins = 24, Losses = 16, Draws = 2 },
                new StandingRow { Team = "부산 웨이브스", Wins = 23, Losses = 18, Draws = 0 },
                new StandingRow { Team = "광주 레드폭스", Wins = 22, Losses = 19, Draws = 1 },
                new StandingRow { Team = "수원 나이츠", Wins = 21, Losses = 20, Draws = 0 },
                new StandingRow { Team = "인천 하버스", Wins = 20, Losses = 21, Draws = 1 },
                new StandingRow { Team = "창원 마리너스", Wins = 19, Losses = 22, Draws = 0 },
                new StandingRow { Team = "대전 코메츠", Wins = 18, Losses = 23, Draws = 1 },
                new StandingRow { Team = "서울 그리폰스", Wins = 17, Losses = 24, Draws = 0 },
                new StandingRow { Team = "고양 스타즈", Wins = 15, Losses = 27, Draws = 0 },
            };
            var leader = standings[0];
            for (int i = 0; i < standings.Count; i++)
            {
                var row = standings[i];
                row.Rank = i + 1;
                double gb = ((leader.Wins - row.Wins) + (row.Losses - leader.Losses)) / 2.0;
                row.GamesBehind = i == 0 ? "—" : gb.ToString("0.0");
            }

            return new SeasonState("2026 시즌 · 5월", my, schedule, standings);
        }
    }
}
