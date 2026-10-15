using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DailyFranchise.Sim.Data;
using UnityEngine;

namespace DailyFranchise.App.Data
{
    /// <summary>
    /// 런타임 마스터 데이터(구단·선수·시즌)를 메모리에 들고 있는다. 만든 뒤에는 읽기 전용이다.
    /// 데이터는 Resources/Database 의 CSV(Teams.csv, SampleRoster.csv)에서 읽는다.
    /// - 동기: <see cref="LoadFromResources"/> (테스트·즉시 필요할 때)
    /// - 비동기: <see cref="LoadFromResourcesAsync"/> 코루틴 (에셋은 ResourceRequest, 파싱은 작업 스레드)
    /// - 문자열: <see cref="FromCsv"/> (파서 검증용)
    /// 읽기에 실패하면 경고를 남기고 빈 DB(<see cref="Empty"/>)를 돌려준다. 빈 DB면 경기 화면은 기존 더미 이름을 쓴다.
    /// </summary>
    public sealed class MasterDatabase : IPlayerLookup
    {
        public const string TeamsResource = "Database/Teams";
        public const string RosterResource = "Database/SampleRoster";
        /// <summary>화면 출처 표기용 선수 데이터 파일 이름.</summary>
        public const string RosterFileName = "SampleRoster.csv";

        readonly List<TeamData> _teams;
        readonly List<PlayerData> _players;
        readonly Dictionary<string, PlayerData> _byId;
        readonly Dictionary<string, TeamRoster> _rosters;

        MasterDatabase(int season, List<TeamData> teams, List<PlayerData> players)
        {
            Season = season;
            _teams = teams;
            _players = players;
            _byId = players.ToDictionary(p => p.Id);
            _rosters = players.GroupBy(p => p.Team).ToDictionary(g => g.Key, g => new TeamRoster(g.Key,
                Of(g, RosterRole.Lineup), Of(g, RosterRole.Bench), Of(g, RosterRole.Starter), Of(g, RosterRole.Bullpen)));

            static List<PlayerData> Of(IEnumerable<PlayerData> team, RosterRole role) =>
                team.Where(p => p.Role == role).OrderBy(p => p.Order).ToList();
        }

        public static MasterDatabase Empty => new MasterDatabase(0, new List<TeamData>(), new List<PlayerData>());

        /// <summary>선수 데이터의 시즌 연도. 빈 DB면 0.</summary>
        public int Season { get; }
        public IReadOnlyList<TeamData> Teams => _teams;
        public IReadOnlyList<PlayerData> Players => _players;
        public bool IsEmpty => _players.Count == 0;

        public TeamData FindTeam(string name) => _teams.FirstOrDefault(t => t.Name == name);

        public PlayerData FindPlayer(string id) => id != null && _byId.TryGetValue(id, out var p) ? p : null;

        /// <summary>구단의 선수단. 선수 데이터가 없는 구단이면 null.</summary>
        public TeamRoster GetRoster(string team) => team != null && _rosters.TryGetValue(team, out var r) ? r : null;

        /// <summary>CSV 문자열로 만든다. 형식·내용 오류는 행 번호가 담긴 FormatException.</summary>
        public static MasterDatabase FromCsv(string teamsCsv, string rosterCsv)
        {
            var teams = new List<TeamData>();
            foreach (var row in CsvReader.Parse(teamsCsv))
            {
                var team = new TeamData
                {
                    Name = row.Required("name"),
                    ShortName = row.Required("short_name"),
                    Stadium = row.Optional("stadium"),
                };
                if (teams.Any(t => t.Name == team.Name)) throw new FormatException($"{row.Line}행: 구단 중복 '{team.Name}'");
                teams.Add(team);
            }

            var players = new List<PlayerData>();
            int? season = null;
            foreach (var row in CsvReader.Parse(rosterCsv))
            {
                int rowSeason = row.Int("season");
                if (season.HasValue && season != rowSeason)
                    throw new FormatException($"{row.Line}행: 시즌 {rowSeason}이 앞 행의 {season}과 다르다.");
                season = rowSeason;

                var p = new PlayerData
                {
                    Id = row.Required("id"),
                    Name = row.Required("name"),
                    Team = row.Required("team"),
                    Position = row.Required("position"),
                    Throws = ParseHand(row, "throws"),
                    Bats = ParseHand(row, "bats"),
                    Role = ParseRole(row),
                    Order = row.Int("order"),
                    Memo = row.Optional("memo"),
                    BattingAverage = row.Double("avg"),
                    OnBasePercentage = row.Double("obp"),
                    HomeRuns = row.Int("hr"),
                    Ops = row.Double("ops"),
                    StolenBases = row.Int("sb"),
                    ScoringPositionAverage = row.Double("risp"),
                    Era = row.Double("era"),
                    OpponentAverage = row.Double("baa"),
                    Wins = row.Int("w"),
                    Losses = row.Int("l"),
                    Saves = row.Int("sv"),
                    Holds = row.Int("hld"),
                    HomeRunsPer9 = row.Double("hr9"),
                };
                if (teams.All(t => t.Name != p.Team)) throw new FormatException($"{row.Line}행: 모르는 구단 '{p.Team}'");
                if (players.Any(x => x.Id == p.Id)) throw new FormatException($"{row.Line}행: 선수 ID 중복 '{p.Id}'");
                if (p.Role == RosterRole.Lineup)
                {
                    if (p.Order < 1 || p.Order > 9) throw new FormatException($"{row.Line}행: 타순은 1~9 ({p.Order})");
                    if (players.Any(x => x.Team == p.Team && x.Role == RosterRole.Lineup && x.Order == p.Order))
                        throw new FormatException($"{row.Line}행: {p.Team} 타순 {p.Order} 중복");
                }
                players.Add(p);
            }

            return new MasterDatabase(season ?? 0, teams, players);
        }

        /// <summary>Resources에서 동기로 읽는다. 실패하면 경고 후 빈 DB.</summary>
        public static MasterDatabase LoadFromResources(Action<string> warn = null)
        {
            try
            {
                return FromCsv(ReadText(Resources.Load<TextAsset>(TeamsResource), TeamsResource),
                    ReadText(Resources.Load<TextAsset>(RosterResource), RosterResource));
            }
            catch (FormatException e)
            {
                return Fail(e, warn);
            }
        }

        /// <summary>
        /// Resources에서 비동기로 읽는 코루틴. 에셋 로드는 ResourceRequest로, CSV 파싱은 작업 스레드에서 하고
        /// 끝나면 메인 스레드에서 <paramref name="done"/>을 부른다. 실패하면 경고 후 빈 DB로 부른다.
        /// </summary>
        public static IEnumerator LoadFromResourcesAsync(Action<MasterDatabase> done, Action<string> warn = null)
        {
            if (done == null) throw new ArgumentNullException(nameof(done));
            var teamsRequest = Resources.LoadAsync<TextAsset>(TeamsResource);
            var rosterRequest = Resources.LoadAsync<TextAsset>(RosterResource);
            yield return teamsRequest;
            yield return rosterRequest;

            string teamsCsv, rosterCsv;
            try
            {
                // TextAsset.text는 메인 스레드에서만 읽을 수 있다.
                teamsCsv = ReadText(teamsRequest.asset as TextAsset, TeamsResource);
                rosterCsv = ReadText(rosterRequest.asset as TextAsset, RosterResource);
            }
            catch (FormatException e)
            {
                done(Fail(e, warn));
                yield break;
            }

            var parse = Task.Run(() => FromCsv(teamsCsv, rosterCsv));
            while (!parse.IsCompleted) yield return null;

            if (parse.IsFaulted)
            {
                var error = parse.Exception?.GetBaseException();
                if (!(error is FormatException format)) throw error ?? new InvalidOperationException("DB 파싱 실패");
                done(Fail(format, warn));
            }
            else
            {
                done(parse.Result);
            }
        }

        /// <summary>R/L/S 또는 우/좌/양.</summary>
        static Hand ParseHand(CsvRow row, string column)
        {
            var v = row.Required(column);
            switch (v.ToUpperInvariant())
            {
                case "R": case "우": return Hand.Right;
                case "L": case "좌": return Hand.Left;
                case "S": case "양": return Hand.Switch;
                default: throw new FormatException($"{row.Line}행: '{column}' 값 '{v}'은 R/L/S가 아니다.");
            }
        }

        static RosterRole ParseRole(CsvRow row)
        {
            var v = row.Required("role");
            switch (v.ToLowerInvariant())
            {
                case "lineup": return RosterRole.Lineup;
                case "bench": return RosterRole.Bench;
                case "starter": return RosterRole.Starter;
                case "bullpen": return RosterRole.Bullpen;
                default: throw new FormatException($"{row.Line}행: role '{v}'은 lineup/bench/starter/bullpen이 아니다.");
            }
        }

        static string ReadText(TextAsset asset, string path) =>
            asset != null ? asset.text : throw new FormatException($"Resources/{path} 를 찾을 수 없다.");

        static MasterDatabase Fail(FormatException e, Action<string> warn)
        {
            (warn ?? Debug.LogWarning)($"[MasterDatabase] 데이터를 읽지 못해 더미 선수단을 씁니다: {e.Message}");
            return Empty;
        }
    }
}
