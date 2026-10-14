using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim;

namespace DailyFranchise.App
{
    public enum GameStatus
    {
        Scheduled,
        InProgress,
        Final,
    }

    public enum GameOutcome
    {
        Win,
        Loss,
        Draw,
    }

    /// <summary>하루 경기 결과 한 건 (내 경기 또는 타 구단 경기).</summary>
    public sealed class GameRecord
    {
        public string AwayTeam;
        public string HomeTeam;
        public GameScore Score;

        public GameOutcome OutcomeFor(string team)
        {
            if (team != AwayTeam && team != HomeTeam) throw new ArgumentException($"이 경기 팀이 아니다: {team}", nameof(team));
            if (Score.Away == Score.Home) return GameOutcome.Draw;
            bool awayWon = Score.Away > Score.Home;
            return (team == AwayTeam) == awayWon ? GameOutcome.Win : GameOutcome.Loss;
        }
    }

    public sealed class ScheduledGame
    {
        public int Number;
        public string Date;
        public string AwayTeam;
        public string HomeTeam;
        public string Stadium;
        public GameStatus Status;
        /// <summary>종료된 경기의 최종 점수. 끝나지 않았으면 null.</summary>
        public GameScore? Score;

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

        /// <summary>KBO 승률 = 승 / (승 + 패). 무승부는 제외한다.</summary>
        public double WinPct => Wins + Losses == 0 ? 0 : (double)Wins / (Wins + Losses);

        /// <summary>승률 표기 (예: 0.561).</summary>
        public string WinPctText => WinPct.ToString("0.000", CultureInfo.InvariantCulture);

        public void Apply(GameOutcome outcome)
        {
            switch (outcome)
            {
                case GameOutcome.Win: Wins++; break;
                case GameOutcome.Loss: Losses++; break;
                default: Draws++; break;
            }
        }
    }

    /// <summary>
    /// 시즌 진행 상태. 씬과 무관한 순수 C#이며 SeasonManager가 씬 전환 동안 들고 있는다.
    /// 경기가 끝나면 내 경기 점수와 같은 날 타 구단 경기 결과(난수)를 성적에 반영하고 순위를 다시 매긴다.
    /// </summary>
    public sealed class SeasonState
    {
        readonly List<ScheduledGame> _schedule;
        readonly List<StandingRow> _standings;

        /// <summary>타 구단 경기 무승부 확률 (KBO는 연장 12회 무승부가 있다).</summary>
        public const double OtherGameDrawRate = 0.03;

        readonly List<GameRecord> _lastDay = new List<GameRecord>();

        public SeasonState(string seasonTitle, string myTeam, List<ScheduledGame> schedule, List<StandingRow> standings)
        {
            if (schedule == null || schedule.Count == 0) throw new ArgumentException("일정이 비어 있다.", nameof(schedule));
            SeasonTitle = seasonTitle;
            MyTeam = myTeam;
            _schedule = schedule;
            _standings = standings;
            Rerank();
        }

        public string SeasonTitle { get; }
        public string MyTeam { get; }
        public IReadOnlyList<ScheduledGame> Schedule => _schedule;
        public IReadOnlyList<StandingRow> Standings => _standings;

        /// <summary>다음에 치를 경기의 일정 인덱스. 시즌이 끝나면 Schedule.Count.</summary>
        public int NextIndex { get; private set; }
        /// <summary>진행 중인 경기. 로비에 있으면 null.</summary>
        public ScheduledGame InProgress { get; private set; }

        /// <summary>가장 최근에 끝난 날의 경기 결과 (첫 항목이 내 경기). 아직 없으면 비어 있다.</summary>
        public IReadOnlyList<GameRecord> LastDayResults => _lastDay;

        /// <summary>가장 최근에 끝난 내 경기. 없으면 null.</summary>
        public ScheduledGame LastFinished => NextIndex > 0 ? _schedule[NextIndex - 1] : null;

        public ScheduledGame NextGame => NextIndex < _schedule.Count ? _schedule[NextIndex] : null;
        public bool IsSeasonOver => NextIndex >= _schedule.Count && InProgress == null;
        public int GamesPlayed => NextIndex;

        public StandingRow MyStanding => StandingOf(MyTeam);

        public StandingRow StandingOf(string team)
        {
            foreach (var row in _standings)
                if (row.Team == team) return row;
            return null;
        }

        /// <summary>내 구단 기준 경기 결과. 끝나지 않은 경기면 null.</summary>
        public GameOutcome? MyOutcome(ScheduledGame game) =>
            game.Score.HasValue
                ? new GameRecord { AwayTeam = game.AwayTeam, HomeTeam = game.HomeTeam, Score = game.Score.Value }.OutcomeFor(MyTeam)
                : (GameOutcome?)null;

        public ScheduledGame StartNextGame()
        {
            if (InProgress != null) throw new InvalidOperationException($"이미 진행 중인 경기가 있다: {InProgress.Number}");
            var game = NextGame ?? throw new InvalidOperationException("남은 경기가 없다.");
            game.Status = GameStatus.InProgress;
            InProgress = game;
            return game;
        }

        /// <summary>내 구단·상대 득점을 진행 중인 경기 일정의 원정 : 홈 점수로 바꾼다.</summary>
        public GameScore ScheduleScore(int myRuns, int opponentRuns)
        {
            var game = InProgress ?? throw new InvalidOperationException("진행 중인 경기가 없다.");
            return game.HomeTeam == MyTeam ? new GameScore(opponentRuns, myRuns) : new GameScore(myRuns, opponentRuns);
        }

        /// <summary>
        /// 진행 중인 경기를 최종 점수로 끝낸다. 내 경기와, 남은 구단끼리 짝지은 같은 날 타 구단 경기(난수)를
        /// 성적에 반영한 뒤 순위를 다시 매긴다.
        /// </summary>
        /// <param name="score">내 경기 최종 점수 (일정의 원정 : 홈 기준).</param>
        /// <param name="random">타 구단 대진·결과용 난수. 테스트는 고정 Seed를 넣는다.</param>
        public void FinishCurrentGame(GameScore score, IRandomSource random)
        {
            if (InProgress == null) throw new InvalidOperationException("진행 중인 경기가 없다.");
            if (random == null) throw new ArgumentNullException(nameof(random));

            var game = InProgress;
            _lastDay.Clear();
            _lastDay.Add(new GameRecord { AwayTeam = game.AwayTeam, HomeTeam = game.HomeTeam, Score = score });
            _lastDay.AddRange(SimulateOtherGames(game, random));
            foreach (var r in _lastDay)
            {
                StandingOf(r.AwayTeam)?.Apply(r.OutcomeFor(r.AwayTeam));
                StandingOf(r.HomeTeam)?.Apply(r.OutcomeFor(r.HomeTeam));
            }
            Rerank();

            game.Score = score;
            game.Status = GameStatus.Final;
            InProgress = null;
            NextIndex++;
        }

        /// <summary>내 경기 두 팀을 뺀 구단을 무작위로 짝지어 경기 결과를 만든다. 홀수면 한 팀은 쉰다.</summary>
        List<GameRecord> SimulateOtherGames(ScheduledGame mine, IRandomSource random)
        {
            var teams = _standings.Select(r => r.Team)
                .Where(t => t != mine.AwayTeam && t != mine.HomeTeam)
                .ToList();
            // Fisher-Yates 섞기
            for (int i = teams.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var tmp = teams[i];
                teams[i] = teams[j];
                teams[j] = tmp;
            }

            var games = new List<GameRecord>();
            for (int i = 0; i + 1 < teams.Count; i += 2)
                games.Add(new GameRecord { AwayTeam = teams[i], HomeTeam = teams[i + 1], Score = RandomScore(random) });
            return games;
        }

        static GameScore RandomScore(IRandomSource random)
        {
            if (random.NextDouble() < OtherGameDrawRate)
            {
                int tie = random.Next(8);
                return new GameScore(tie, tie);
            }
            int loser = random.Next(8);
            int winner = loser + 1 + random.Next(6);
            return random.NextDouble() < 0.5 ? new GameScore(winner, loser) : new GameScore(loser, winner);
        }

        /// <summary>
        /// KBO 순위 규정 중 승률 → 다승까지 적용한다. 둘 다 같으면 같은 순위를 주고 기존 순서를 유지한다 (안정 정렬).
        /// 승차는 1위 기준 ((1위 승 - 승) + (패 - 1위 패)) / 2.
        /// </summary>
        public void Rerank()
        {
            var ordered = _standings
                .OrderByDescending(r => r.WinPct)
                .ThenByDescending(r => r.Wins)
                .ToList();
            _standings.Clear();
            _standings.AddRange(ordered);
            if (_standings.Count == 0) return;

            var leader = _standings[0];
            for (int i = 0; i < _standings.Count; i++)
            {
                var row = _standings[i];
                var prev = i > 0 ? _standings[i - 1] : null;
                row.Rank = prev != null && prev.WinPct == row.WinPct && prev.Wins == row.Wins ? prev.Rank : i + 1;
                double gb = ((leader.Wins - row.Wins) + (row.Losses - leader.Losses)) / 2.0;
                row.GamesBehind = i == 0 ? "—" : gb.ToString("0.0", CultureInfo.InvariantCulture);
            }
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
            return new SeasonState("2026 시즌 · 5월", my, schedule, standings);
        }
    }
}
