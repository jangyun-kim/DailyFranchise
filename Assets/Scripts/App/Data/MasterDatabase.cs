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
    /// 데이터는 Resources/Database 의 CSV(Teams.csv, SampleRoster_Export.csv)에서 읽는다 (Phase 26.5: 파이썬 파이프라인이
    /// 내보낸 실제 KBO 선수 데이터. 이전 가상 데이터 SampleRoster_Master.csv는 런타임에서 뺐다).
    /// 선수 행의 구단은 Teams.csv의 정식 이름(삼성 라이온즈) 또는 약칭(삼성)으로 적을 수 있다 — 약칭은 정식 이름으로 바꾼다.
    /// 구단 선수단은 1군 엔트리(선발 타순 · 벤치 앞 <see cref="FirstTeamBench"/>명 · 선발 · 불펜 앞 <see cref="FirstTeamBullpen"/>명,
    /// 각각 데이터 순서)와 나머지 퓨처스(<see cref="TeamRoster.Reserves"/>)로 나눈다.
    /// 선수 CSV는 기존 소문자 열 형식과 파이썬 마스터 CSV 형식(Year, Pos., Back Num., wRC+ …)을 모두 받는다.
    /// - 동기: <see cref="LoadFromResources"/> (테스트·즉시 필요할 때)
    /// - 비동기: <see cref="LoadFromResourcesAsync"/> 코루틴 (에셋은 ResourceRequest, 파싱은 작업 스레드)
    /// - 문자열: <see cref="FromCsv"/> (파서 검증용)
    /// 읽기에 실패하면 경고를 남기고 빈 DB(<see cref="Empty"/>)를 돌려준다. 빈 DB면 경기 화면은 기존 더미 이름을 쓴다.
    /// </summary>
    public sealed class MasterDatabase : IPlayerLookup
    {
        public const string TeamsResource = "Database/Teams";
        public const string RosterResource = "Database/SampleRoster_Export";
        /// <summary>화면 출처 표기용 선수 데이터 파일 이름.</summary>
        public const string RosterFileName = "SampleRoster_Export.csv";

        /// <summary>1군 엔트리에 넣는 벤치 야수 수 (<see cref="RosterLimits.MaxBench"/>와 같다).</summary>
        public const int FirstTeamBench = RosterLimits.MaxBench;
        /// <summary>1군 엔트리에 넣는 불펜 투수 수 (<see cref="RosterLimits.MaxBullpen"/>와 같다).</summary>
        public const int FirstTeamBullpen = RosterLimits.MaxBullpen;

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
            _rosters = players.GroupBy(p => p.Team).ToDictionary(g => g.Key, g =>
            {
                var bench = Of(g, RosterRole.Bench);
                var bullpen = Of(g, RosterRole.Bullpen);
                var reserves = bench.Skip(FirstTeamBench).Concat(bullpen.Skip(FirstTeamBullpen)).ToList();
                return new TeamRoster(g.Key, Of(g, RosterRole.Lineup), bench.Take(FirstTeamBench).ToList(), Of(g, RosterRole.Starter),
                    bullpen.Take(FirstTeamBullpen).ToList(), reserves);
            });

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

        /// <summary>
        /// 선수 행의 구단 표기 → 정식 이름. 정식 이름이 먼저, 아니면 약칭(short_name)이 한 구단에만 맞을 때.
        /// 모르는 구단이거나 약칭이 여러 구단에 겹치면 FormatException.
        /// </summary>
        static string TeamNameOf(List<TeamData> teams, string raw, int line)
        {
            if (teams.Any(t => t.Name == raw)) return raw;
            var byShort = teams.Where(t => t.ShortName == raw).ToList();
            if (byShort.Count == 1) return byShort[0].Name;
            if (byShort.Count > 1) throw new FormatException($"{line}행: 구단 약칭 '{raw}'이 여러 구단에 겹친다 ({string.Join(", ", byShort.Select(t => t.Name))})");
            throw new FormatException($"{line}행: 모르는 구단 '{raw}'");
        }

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
                int rowSeason = row.Int(Col(row, "season", "Year"));
                if (season.HasValue && season != rowSeason)
                    throw new FormatException($"{row.Line}행: 시즌 {rowSeason}이 앞 행의 {season}과 다르다.");
                season = rowSeason;

                var p = new PlayerData
                {
                    Name = row.Required("name"),
                    Team = TeamNameOf(teams, row.Required("team"), row.Line),
                    Position = row.Required(Col(row, "position", "Pos.")),
                    Throws = ParseHand(row, "throws"),
                    Bats = ParseHand(row, "bats"),
                    Memo = row.Optional("memo"),
                    Year = rowSeason,
                };
                p.Id = row.Optional("id") ?? $"{p.Team}-{rowSeason}-{p.Name}";
                p.Role = ParseRole(row, p.Position);
                p.Order = row.Has("order") ? row.Int("order")
                    : players.Count(x => x.Team == p.Team && x.Role == p.Role) + 1;
                ReadStats(row, p);
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

        /// <summary>
        /// 기록 열을 읽는다. 열 이름은 기존 로스터 형식(소문자)과 마스터 CSV 형식(KBO 표기)을 모두 받는다 (대소문자 무시).
        /// - 확률 계산이 쓰는 기존 기록(avg·obp·hr·ops·sb·era·baa·w·l·sv·hld·risp·hr9): 엄격 — 숫자가 아니면 FormatException
        ///   (엉뚱한 값이 0으로 바뀌어 확률을 조용히 비트는 것보다, 읽기 실패 → 더미 선수단 폴백이 낫다).
        /// - 표시 전용 확장 기록: 관대 — 열이 없거나 빈 칸·"-"·변환 실패면 0 (<see cref="CsvRow.SafeInt"/>).
        /// 마스터 CSV는 R·H·HR·BB·SO 열을 타자·투수가 함께 쓰므로 투수 행이면 허용 기록으로 읽는다.
        /// </summary>
        static void ReadStats(CsvRow row, PlayerData p)
        {
            p.Age = row.SafeInt("age");
            p.Number = row.SafeInt("Back Num.", "number", "back_num");
            p.Games = row.SafeInt("G");
            p.War = row.SafeDouble("WAR");

            // 확률 계산용 (엄격)
            p.BattingAverage = row.Double("avg");
            p.OnBasePercentage = row.Double("obp");
            p.Ops = row.Double("ops");
            p.StolenBases = row.Int("sb");
            p.ScoringPositionAverage = row.Double("risp");
            p.Era = row.Double("era");
            p.OpponentAverage = row.Double("baa");
            p.Wins = row.Int("w");
            p.Losses = row.Int("l");
            p.Saves = row.Int(Col(row, "sv", "S"));
            p.Holds = row.Int(Col(row, "hld", "HD"));
            p.HomeRunsPer9 = row.Double("hr9");

            // 타자 확장
            p.PlateAppearances = row.SafeInt("PA");
            p.EffectivePlateAppearances = row.SafeDouble("ePA");
            p.AtBats = row.SafeInt("AB");
            p.Doubles = row.SafeInt("2B");
            p.Triples = row.SafeInt("3B");
            p.TotalBases = row.SafeInt("TB");
            p.RunsBattedIn = row.SafeInt("RBI");
            p.CaughtStealing = row.SafeInt("CS");
            p.HitByPitch = row.SafeInt("HP", "HBP");
            p.IntentionalWalks = row.SafeInt("IB", "IBB");
            p.GroundIntoDoublePlays = row.SafeInt("GDP");
            p.SacrificeHits = row.SafeInt("SH", "SAC");
            p.SacrificeFlies = row.SafeInt("SF");
            p.RecordedSlugging = row.SafeDouble("SLG");
            p.RunsPerEffectivePa = row.SafeDouble("R/ePA");
            p.WrcPlus = row.SafeDouble("wRC+");
            p.OffensiveWar = row.SafeDouble("oWAR");
            p.DefensiveWar = row.SafeDouble("dWAR");

            // 투수 확장
            p.GamesStarted = row.SafeInt("GS");
            p.ReliefGames = row.SafeInt("GR");
            p.GamesFinished = row.SafeInt("GF");
            p.CompleteGames = row.SafeInt("CG");
            p.Shutouts = row.SafeInt("SHO");
            p.InningsPitched = row.SafeDouble("IP");
            p.EarnedRuns = row.SafeInt("ER");
            p.BattersFaced = row.SafeInt("TBF");
            p.ReachedOnError = row.SafeInt("ROE");
            p.Balks = row.SafeInt("BK");
            p.WildPitches = row.SafeInt("WP");
            p.RunsPer9 = row.SafeDouble("RA9");
            p.ReplacementRunsAllowed = row.SafeDouble("rRA");
            p.ReplacementRunsPer9 = row.SafeDouble("rRA9");
            p.Fip = row.SafeDouble("FIP");
            p.Whip = row.SafeDouble("WHIP");

            // 타자·투수 공용 열
            if (p.IsPitcher)
            {
                p.RunsAllowed = row.SafeInt("R");
                p.HitsAllowed = row.SafeInt("H");
                p.HomeRunsAllowed = row.SafeInt("hr");
                p.WalksAllowed = row.SafeInt("BB");
                p.PitcherStrikeouts = row.SafeInt("SO");
            }
            else
            {
                p.Runs = row.SafeInt("R");
                p.Hits = row.SafeInt("H");
                p.HomeRuns = row.Int("hr");
                p.Walks = row.SafeInt("BB");
                p.Strikeouts = row.SafeInt("SO");
            }
        }

        /// <summary>후보 중 헤더에 있는 열 이름. 하나도 없으면 첫 후보 (읽기 쪽에서 '열 없음' 처리).</summary>
        static string Col(CsvRow row, params string[] candidates) => row.Column(candidates) ?? candidates[0];

        /// <summary>R/L/S 또는 우/좌/양. 열이 없거나 빈 칸이면 우(R) — 투타 정보가 없는 마스터 CSV 대응.</summary>
        static Hand ParseHand(CsvRow row, string column)
        {
            var v = row.Optional(column);
            if (v == null) return Hand.Right;
            switch (v.ToUpperInvariant())
            {
                case "R": case "우": return Hand.Right;
                case "L": case "좌": return Hand.Left;
                case "S": case "양": return Hand.Switch;
                default: throw new FormatException($"{row.Line}행: '{column}' 값 '{v}'은 R/L/S가 아니다.");
            }
        }

        /// <summary>투수로 보는 포지션 표기 (role 열이 없을 때 추정용).</summary>
        static readonly HashSet<string> PitcherPositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "투수", "P", "SP", "RP", "선발", "중계", "셋업", "마무리" };

        /// <summary>
        /// role 열 값. 열이 없거나 빈 칸이면 추정한다 (마스터 CSV 대응): 투수 포지션이거나 타석 없이 이닝만 있으면 투수 —
        /// 선발 등판이 절반 이상이면 starter, 아니면 bullpen. 그 외 야수는 타순이 정해지지 않았으므로 bench.
        /// </summary>
        static RosterRole ParseRole(CsvRow row, string position)
        {
            var v = row.Optional("role");
            if (v == null)
            {
                bool pitcher = PitcherPositions.Contains(position)
                               || (row.SafeInt("PA") == 0 && row.SafeDouble("IP") > 0);
                if (!pitcher) return RosterRole.Bench;
                int starts = row.SafeInt("GS");
                return starts > 0 && starts * 2 >= row.SafeInt("G") ? RosterRole.Starter : RosterRole.Bullpen;
            }
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
