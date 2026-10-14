using System.Collections.Generic;
using DailyFranchise.Sim.Fixtures;

namespace DailyFranchise.UI.Data
{
    /// <summary>
    /// ScenarioCatalog 장면용 더미 표시 데이터. 팀·선수 이름과 기록은 모두 가상이다.
    /// 선수 정보는 모든 장면이 공유하고, 스코어보드 기초값만 장면별로 다르다.
    /// 장면별 이닝 득점 합은 해당 Fixture의 시작 점수와 일치해야 한다 (테스트로 검증).
    /// 선수·스코어보드 데이터는 내 구단을 홈으로 짜여 있다. 대진(<see cref="Matchup"/>)이 내 구단을 원정으로 주면
    /// 선수 소속·라인업·스코어보드의 원정 ↔ 홈을 뒤집고(<see cref="Mirrored"/>), 경기 화면은 장면도
    /// <see cref="ScenarioMirror"/>로 뒤집어 재생한다.
    /// </summary>
    public sealed class DummyScenarioData : IRosterSource, IScoreboardSource
    {
        /// <summary>더미 데이터가 짜인 기준: 내 구단 = 홈.</summary>
        const TeamSide NativeMySide = TeamSide.Home;

        static readonly Dictionary<string, (TeamBoxSeed away, TeamBoxSeed home)> BoxSeeds =
            new Dictionary<string, (TeamBoxSeed, TeamBoxSeed)>
            {
                [ScenarioCatalog.EighthBottom] = (
                    new TeamBoxSeed { RunsByInning = new[] { 0, 0, 1, 0, 0, 2, 0, 0 }, Hits = 7, Errors = 1, Walks = 2 },
                    new TeamBoxSeed { RunsByInning = new[] { 0, 0, 0, 1, 0, 1, 0, 0 }, Hits = 5, Errors = 0, Walks = 3 }),
                [ScenarioCatalog.EighthBottomTactics] = (
                    new TeamBoxSeed { RunsByInning = new[] { 0, 0, 1, 0, 0, 2, 0, 0 }, Hits = 7, Errors = 1, Walks = 2 },
                    new TeamBoxSeed { RunsByInning = new[] { 0, 0, 0, 1, 0, 1, 0, 0 }, Hits = 5, Errors = 0, Walks = 3 }),
                [ScenarioCatalog.HomeRun] = (
                    new TeamBoxSeed { RunsByInning = new[] { 0, 1, 0, 2, 0, 1, 0 }, Hits = 8, Errors = 0, Walks = 3 },
                    new TeamBoxSeed { RunsByInning = new[] { 0, 0, 1, 0, 0, 1, 0 }, Hits = 6, Errors = 1, Walks = 2 }),
                [ScenarioCatalog.DoublePlay] = (
                    new TeamBoxSeed { RunsByInning = new[] { 0, 0, 1, 0, 0, 0 }, Hits = 5, Errors = 1, Walks = 2 },
                    new TeamBoxSeed { RunsByInning = new[] { 1, 0, 0, 2, 0 }, Hits = 7, Errors = 0, Walks = 1 }),
                [ScenarioCatalog.InningEnd] = (
                    new TeamBoxSeed { RunsByInning = new[] { 1, 0, 0, 2, 0, 1, 1, 0 }, Hits = 9, Errors = 0, Walks = 2 },
                    new TeamBoxSeed { RunsByInning = new[] { 0, 2, 0, 0, 1, 0, 1, 0 }, Hits = 8, Errors = 1, Walks = 4 }),
            };

        readonly Dictionary<string, PlayerInfo> _players = new Dictionary<string, PlayerInfo>();
        readonly string _scenarioId;
        readonly Matchup _matchup;

        /// <param name="matchup">그릴 대진. null이면 <see cref="Matchup.Default"/>.</param>
        public DummyScenarioData(string scenarioId = ScenarioCatalog.EighthBottom, Matchup matchup = null)
        {
            if (!BoxSeeds.ContainsKey(scenarioId))
                throw new System.ArgumentException($"더미 스코어보드가 없는 시나리오: {scenarioId}", nameof(scenarioId));
            _scenarioId = scenarioId;
            _matchup = matchup ?? Matchup.Default;

            AddPitcher(Fixture8thBottom.Pitcher, "강민혁", TeamSide.Away, "선발", "좌투",
                "시즌 11승 6패 · 평균자책 3.18", "오늘 7.1이닝 · 2실점", 87, 78);

            AddBatter(Fixture8thBottom.RunnerOnFirst, "윤재민", "CF", "우투좌타", "타율 .298 · 31도루", 3, 1, null, 82);
            AddBatter(Fixture8thBottom.RunnerOnThird, "박서진", "SS", "우투우타", "타율 .276 · 7홈런", 3, 1, null, 75);
            AddBatter(Fixture8thBottom.Batter1, "최도윤", "RF", "좌투좌타", "타율 .312 · 18홈런 · OPS .874", 3, 1, null, 71);
            AddBatter(Fixture8thBottom.Batter2, "한지후", "1B", "우투우타", "타율 .287 · 24홈런 · OPS .901", 3, 0, "1볼넷", 68);
            AddBatter("home_b5", "정우석", "3B", "우투우타", "타율 .265 · 12홈런", 3, 0, null, 70);
            AddBatter("home_b6", "임태오", "LF", "좌투좌타", "타율 .281 · 9홈런", 3, 1, null, 74);
            AddBatter("home_b7", "서현준", "C", "우투우타", "타율 .248 · 6홈런", 2, 0, "1볼넷", 66);
            AddBatter("home_b8", "남궁율", "2B", "우투좌타", "타율 .271 · 3홈런", 2, 1, "1볼넷", 80);
            AddBatter("home_b9", "문하람", "DH", "우투우타", "타율 .259 · 14홈런", 2, 0, null, 72);

            AddBatter("home_ph1", "조은찬", "대타", "좌타", "좌타 · 장타형 · 대타 타율 .301", null, 0, null, 90);
            AddBatter("home_ph2", "류시온", "대타", "우타", "우타 · 정교형 · 대타 타율 .288", null, 0, null, 88);
            AddBatter("home_pr1", "권태하", "대주자", "우타", "주력 상 · 도루 성공률 86%", null, 0, null, 95);

            AddPitcher("home_rp1", "진도겸", TeamSide.Home, "마무리", "우투", "시즌 28세이브 · 평균자책 2.41", "오늘 구원 등판", 0, 100);
            AddPitcher("home_rp2", "송유찬", TeamSide.Home, "좌완", "좌투", "시즌 18홀드 · 평균자책 3.05", "몸 푸는 중", 0, 84);
            AddPitcher("home_rp3", "하지완", TeamSide.Home, "롱릴리프", "우투", "시즌 4승 · 평균자책 4.12", "대기", 0, 100);
            AddPitcher("home_sp", "오준혁", TeamSide.Home, "선발", "우투", "시즌 9승 7패 · 평균자책 3.62", "오늘 5이닝 · 1실점", 78, 81);

            AddPitcher("away_rp1", "백건우", TeamSide.Away, "셋업", "우투", "시즌 22홀드 · 평균자책 2.97", "오늘 구원 등판", 0, 96);
            AddBatter("away_b1", "김라온", "CF", "우투좌타", "타율 .305 · 27도루", 4, 2, null, 85, TeamSide.Away);
            AddBatter("away_b4", "배무진", "1B", "우투우타", "타율 .291 · 21홈런", 2, 1, "1볼넷", 77, TeamSide.Away);
            AddBatter("away_b5", "탁성호", "LF", "좌투좌타", "타율 .268 · 15홈런", 2, 0, null, 73, TeamSide.Away);
        }

        public TeamSide MyTeam => _matchup.MySide;
        public string FeaturedPlayerId => Fixture8thBottom.Batter1;

        /// <summary>내 구단이 원정이라 더미 데이터의 원정 ↔ 홈을 뒤집었는지. 이때 장면도 뒤집어 재생해야 한다.</summary>
        public bool Mirrored => _matchup.MySide != NativeMySide;

        public Matchup Matchup => _matchup;

        public TeamInfo GetTeam(TeamSide side) => _matchup.Get(side);

        public PlayerInfo GetPlayer(string playerId)
        {
            if (playerId != null && _players.TryGetValue(playerId, out var p)) return p;
            return new PlayerInfo
            {
                Id = playerId, Name = playerId ?? "—", Position = "—", Handedness = "—",
                SeasonLine = "시즌 기록 없음", TodayLine = "—",
            };
        }

        public IReadOnlyList<RosterEntry> GetLineup(TeamSide side)
        {
            if (side != MyTeam) return new RosterEntry[0];
            return new[]
            {
                Slot(1, Fixture8thBottom.RunnerOnFirst),
                Slot(2, Fixture8thBottom.RunnerOnThird),
                Slot(3, Fixture8thBottom.Batter1),
                Slot(4, Fixture8thBottom.Batter2),
                Slot(5, "home_b5"),
                Slot(6, "home_b6"),
                Slot(7, "home_b7"),
                Slot(8, "home_b8"),
                Slot(9, "home_b9"),
            };
        }

        public IReadOnlyList<RosterEntry> GetBench(TeamSide side)
        {
            if (side != MyTeam) return new RosterEntry[0];
            return new[]
            {
                new RosterEntry { Role = "대타", PlayerId = "home_ph1", Note = "좌/장타" },
                new RosterEntry { Role = "대타", PlayerId = "home_ph2", Note = "우/정교" },
                new RosterEntry { Role = "대주자", PlayerId = "home_pr1", Note = "주력 상" },
            };
        }

        public IReadOnlyList<RosterEntry> GetBullpen(TeamSide side)
        {
            if (side != MyTeam) return new RosterEntry[0];
            return new[]
            {
                new RosterEntry { Role = "마무리", PlayerId = "home_rp1", Note = "준비" },
                new RosterEntry { Role = "좌완", PlayerId = "home_rp2", Note = "84%" },
                new RosterEntry { Role = "롱릴리프", PlayerId = "home_rp3", Note = "100%" },
            };
        }

        public TeamBoxSeed GetBoxSeed(TeamSide side)
        {
            var seeds = BoxSeeds[_scenarioId];
            return Native(side) == TeamSide.Away ? seeds.away : seeds.home;
        }

        /// <summary>화면 기준 쪽 → 더미 데이터가 짜인 기준 쪽.</summary>
        TeamSide Native(TeamSide side) => !Mirrored ? side : side == TeamSide.Away ? TeamSide.Home : TeamSide.Away;

        RosterEntry Slot(int order, string id) =>
            new RosterEntry { Order = order, Role = _players[id].Position, PlayerId = id };

        void AddPitcher(string id, string name, TeamSide side, string role, string hand,
            string season, string today, int pitches, int fitness)
        {
            _players[id] = new PlayerInfo
            {
                Id = id, Name = name, Side = Native(side), Position = role, Handedness = hand,
                SeasonLine = season, TodayLine = today, PitchesBeforeScene = pitches, Fitness = fitness,
            };
        }

        void AddBatter(string id, string name, string pos, string hand, string season,
            int? atBats, int hits, string extra, int fitness, TeamSide side = TeamSide.Home)
        {
            _players[id] = new PlayerInfo
            {
                Id = id, Name = name, Side = Native(side), Position = pos, Handedness = hand,
                SeasonLine = season, TodayAtBats = atBats, TodayHits = hits, TodayExtra = extra, Fitness = fitness,
            };
        }
    }
}
