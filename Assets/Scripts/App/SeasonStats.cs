using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>
    /// 타자 한 명의 이번 시즌 누적 기록 (Phase 37). 비율 기록은 누적값으로 계산하고, 분모가 0이면 0 (<see cref="HasRecord"/>로 "기록 없음"을 가린다).
    /// 출루율 = (안타 + 볼넷) ÷ (타수 + 볼넷) — 몸에 맞는 공·희생플라이는 따로 세지 않는다.
    /// </summary>
    public sealed class BatterSeasonStats
    {
        public BatterSeasonStats(string playerId) => PlayerId = playerId;

        public string PlayerId { get; }
        public readonly BattingLine Line = new BattingLine();

        public int Games => Line.Games;
        public int PlateAppearances => Line.PlateAppearances;
        public int AtBats => Line.AtBats;
        public int Hits => Line.Hits;
        public int Doubles => Line.Doubles;
        public int Triples => Line.Triples;
        public int HomeRuns => Line.HomeRuns;
        public int Walks => Line.Walks;
        public int Strikeouts => Line.Strikeouts;
        public int RunsBattedIn => Line.RunsBattedIn;

        public bool HasRecord => PlateAppearances > 0;
        public int TotalBases => Hits + Doubles + 2 * Triples + 3 * HomeRuns;
        public double Average => SeasonStatsText.Ratio(Hits, AtBats);
        public double OnBase => SeasonStatsText.Ratio(Hits + Walks, AtBats + Walks);
        public double Slugging => SeasonStatsText.Ratio(TotalBases, AtBats);
        public double Ops => OnBase + Slugging;
    }

    /// <summary>투수 한 명의 이번 시즌 누적 기록 (Phase 37). 이닝은 아웃 수로 쌓고 표시할 때 바꾼다. 아웃이 없으면 평균자책 0.</summary>
    public sealed class PitcherSeasonStats
    {
        public PitcherSeasonStats(string playerId) => PlayerId = playerId;

        public string PlayerId { get; }
        public readonly PitchingLine Line = new PitchingLine();

        public int Games => Line.Games;
        public int GamesStarted => Line.GamesStarted;
        public int Outs => Line.Outs;
        public int Wins => Line.Wins;
        public int Losses => Line.Losses;
        public int Saves => Line.Saves;
        public int EarnedRuns => Line.EarnedRuns;
        public int Strikeouts => Line.Strikeouts;
        public int HitsAllowed => Line.HitsAllowed;
        public int WalksAllowed => Line.WalksAllowed;

        public bool HasRecord => Games > 0;
        /// <summary>이닝 (소수, 8⅓ = 8.333…).</summary>
        public double Innings => Outs / 3.0;
        /// <summary>평균자책 = 자책 × 27 ÷ 아웃. 아웃이 없으면 0.</summary>
        public double Era => Outs == 0 ? 0 : EarnedRuns * 27.0 / Outs;
        /// <summary>WHIP = (피안타 + 볼넷) × 3 ÷ 아웃. 아웃이 없으면 0.</summary>
        public double Whip => Outs == 0 ? 0 : (HitsAllowed + WalksAllowed) * 3.0 / Outs;
    }

    /// <summary>
    /// 이번 시즌 개인 성적 (Phase 37, <see cref="SeasonState.Stats"/>). 선수 DB ID → 누적 기록. 경기 결산마다 박스스코어를 더하고
    /// (<see cref="Add"/>, 정규시즌 경기만), 시즌을 넘기면 비운다. 화면 표시용이다 — 경기 계산은 여전히 선수 DB 기록(+ 변동치)을 쓴다.
    /// </summary>
    public sealed class SeasonStatsTracker
    {
        readonly Dictionary<string, BatterSeasonStats> _batters = new Dictionary<string, BatterSeasonStats>();
        readonly Dictionary<string, PitcherSeasonStats> _pitchers = new Dictionary<string, PitcherSeasonStats>();

        public IReadOnlyDictionary<string, BatterSeasonStats> Batters => _batters;
        public IReadOnlyDictionary<string, PitcherSeasonStats> Pitchers => _pitchers;

        /// <summary>기록이 바뀔 때마다 오른다 (화면 갱신 판단용, 저장하지 않는다).</summary>
        public int Version { get; private set; }

        public bool IsEmpty => _batters.Count == 0 && _pitchers.Count == 0;

        /// <summary>타자 기록. 없으면 0으로 찬 빈 기록 (목록에는 넣지 않는다).</summary>
        public BatterSeasonStats BatterOf(string playerId) =>
            playerId != null && _batters.TryGetValue(playerId, out var s) ? s : new BatterSeasonStats(playerId);

        public PitcherSeasonStats PitcherOf(string playerId) =>
            playerId != null && _pitchers.TryGetValue(playerId, out var s) ? s : new PitcherSeasonStats(playerId);

        /// <summary>한 경기 박스스코어(선수 DB ID 기준)를 더한다.</summary>
        public void Add(BoxScore box)
        {
            if (box == null) return;
            foreach (var kv in box.Batting) Batter(kv.Key).Line.Add(kv.Value);
            foreach (var kv in box.Pitching) Pitcher(kv.Key).Line.Add(kv.Value);
            Version++;
        }

        /// <summary>저장 파일에서 한 선수 기록을 되살린다 (음수는 0으로).</summary>
        public void Restore(string playerId, BattingLine batting, PitchingLine pitching)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            if (batting != null) Batter(playerId).Line.Add(NonNegative(batting));
            if (pitching != null) Pitcher(playerId).Line.Add(NonNegative(pitching));
            Version++;
        }

        public void Reset()
        {
            _batters.Clear();
            _pitchers.Clear();
            Version++;
        }

        BatterSeasonStats Batter(string id)
        {
            if (!_batters.TryGetValue(id, out var s)) _batters[id] = s = new BatterSeasonStats(id);
            return s;
        }

        PitcherSeasonStats Pitcher(string id)
        {
            if (!_pitchers.TryGetValue(id, out var s)) _pitchers[id] = s = new PitcherSeasonStats(id);
            return s;
        }

        static BattingLine NonNegative(BattingLine b) => new BattingLine
        {
            Games = Math.Max(0, b.Games), PlateAppearances = Math.Max(0, b.PlateAppearances), AtBats = Math.Max(0, b.AtBats),
            Hits = Math.Max(0, b.Hits), Doubles = Math.Max(0, b.Doubles), Triples = Math.Max(0, b.Triples), HomeRuns = Math.Max(0, b.HomeRuns),
            Walks = Math.Max(0, b.Walks), Strikeouts = Math.Max(0, b.Strikeouts), RunsBattedIn = Math.Max(0, b.RunsBattedIn),
        };

        static PitchingLine NonNegative(PitchingLine p) => new PitchingLine
        {
            Games = Math.Max(0, p.Games), GamesStarted = Math.Max(0, p.GamesStarted), Outs = Math.Max(0, p.Outs),
            HitsAllowed = Math.Max(0, p.HitsAllowed), WalksAllowed = Math.Max(0, p.WalksAllowed), Strikeouts = Math.Max(0, p.Strikeouts),
            RunsAllowed = Math.Max(0, p.RunsAllowed), EarnedRuns = Math.Max(0, p.EarnedRuns), Wins = Math.Max(0, p.Wins),
            Losses = Math.Max(0, p.Losses), Saves = Math.Max(0, p.Saves),
        };
    }

    /// <summary>시즌 기록 표기 (Phase 37). 기록이 없으면(분모 0) 비율은 "-.---", 평균자책은 "-.--".</summary>
    public static class SeasonStatsText
    {
        public const string NoRate = "-.---";
        public const string NoEra = "-.--";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>분모가 0이면 0 (0으로 나누지 않는다).</summary>
        public static double Ratio(int numerator, int denominator) => denominator <= 0 ? 0 : (double)numerator / denominator;

        /// <summary>".333" / "1.000". 분모가 없으면 "-.---".</summary>
        public static string Rate(double value, bool has) => !has ? NoRate : value.ToString(value < 1 ? ".000" : "0.000", Inv);

        public static string Era(PitcherSeasonStats s) => s.Outs == 0 ? NoEra : s.Era.ToString("0.00", Inv);

        /// <summary>"8 1/3" · "7" · "0".</summary>
        public static string Innings(int outs) => outs % 3 == 0 ? (outs / 3).ToString(Inv) : $"{outs / 3} {outs % 3}/3";
    }

    /// <summary>팀 내 기록 1위 부문 (Phase 37 — 이번 시즌).</summary>
    public enum SeasonLeaderCategory
    {
        BattingAverage,
        HomeRuns,
        RunsBattedIn,
        Wins,
        Era,
        Saves,
    }

    /// <summary>팀 내 기록 1위 한 줄. 기록을 낸 선수가 없으면 <see cref="Player"/>가 null.</summary>
    public sealed class SeasonLeader
    {
        public SeasonLeaderCategory Category;
        public PlayerData Player;
        public string ValueText;
        public string CategoryText => SeasonLeaders.CategoryName(Category);
    }

    /// <summary>
    /// 팀 내 기록 1위 (Phase 37): 타율 · 홈런 · 타점 · 다승 · 평균자책 · 세이브. 대상은 지금 내 선수단(1군·퓨처스)의 이번 시즌 기록.
    /// - 타율: 규정 타석(팀 경기 수 × 3.1) 이상, 평균자책: 규정 이닝(팀 경기 수 × 1이닝) 이상. 팀이 아직 경기를 안 했으면 기록이 있는 선수 전원.
    /// - 누적 부문(홈런·타점·다승·세이브)은 1 이상만. 같으면 타석·이닝이 많은 쪽, 그다음 ID 순.
    /// </summary>
    public static class SeasonLeaders
    {
        public const double QualifyingPaPerGame = 3.1;

        public static readonly IReadOnlyList<SeasonLeaderCategory> Categories = (SeasonLeaderCategory[])Enum.GetValues(typeof(SeasonLeaderCategory));

        public static IReadOnlyList<SeasonLeader> For(TeamRoster roster, SeasonStatsTracker stats, int teamGames)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            var players = roster == null ? new List<PlayerData>() : LeagueRosterBook.AllOf(roster).Concat(roster.Reserves)
                .Where(p => !LeagueRosterBook.IsReplacement(p)).GroupBy(p => p.Id).Select(g => g.First()).ToList();
            return Categories.Select(c => Leader(c, players, stats, teamGames)).ToList();
        }

        static SeasonLeader Leader(SeasonLeaderCategory c, List<PlayerData> players, SeasonStatsTracker stats, int games)
        {
            var inv = CultureInfo.InvariantCulture;
            int minPa = (int)Math.Ceiling(QualifyingPaPerGame * games);
            int minOuts = 3 * games;
            var bats = players.Select(p => (p, s: stats.BatterOf(p.Id))).Where(x => x.s.HasRecord).ToList();
            var arms = players.Select(p => (p, s: stats.PitcherOf(p.Id))).Where(x => x.s.HasRecord).ToList();
            (PlayerData p, string v) best;
            switch (c)
            {
                case SeasonLeaderCategory.BattingAverage:
                    best = bats.Where(x => x.s.PlateAppearances >= Math.Max(1, minPa) && x.s.AtBats > 0)
                        .OrderByDescending(x => x.s.Average).ThenByDescending(x => x.s.PlateAppearances).ThenBy(x => x.p.Id, StringComparer.Ordinal)
                        .Select(x => (x.p, SeasonStatsText.Rate(x.s.Average, true))).FirstOrDefault();
                    break;
                case SeasonLeaderCategory.HomeRuns:
                    best = Top(bats.Select(x => (x.p, x.s.HomeRuns, x.s.PlateAppearances)), "개");
                    break;
                case SeasonLeaderCategory.RunsBattedIn:
                    best = Top(bats.Select(x => (x.p, x.s.RunsBattedIn, x.s.PlateAppearances)), "점");
                    break;
                case SeasonLeaderCategory.Wins:
                    best = Top(arms.Select(x => (x.p, x.s.Wins, x.s.Outs)), "승");
                    break;
                case SeasonLeaderCategory.Saves:
                    best = Top(arms.Select(x => (x.p, x.s.Saves, x.s.Outs)), "개");
                    break;
                default:
                    best = arms.Where(x => x.s.Outs >= Math.Max(1, minOuts))
                        .OrderBy(x => x.s.Era).ThenByDescending(x => x.s.Outs).ThenBy(x => x.p.Id, StringComparer.Ordinal)
                        .Select(x => (x.p, SeasonStatsText.Era(x.s))).FirstOrDefault();
                    break;
            }
            return new SeasonLeader { Category = c, Player = best.p, ValueText = best.p == null ? "—" : best.v };
        }

        /// <summary>누적 부문 1위: 값이 1 이상인 선수 중 큰 순 → 타석·아웃 많은 순 → ID 순.</summary>
        static (PlayerData, string) Top(IEnumerable<(PlayerData p, int value, int tie)> list, string unit) =>
            list.Where(x => x.value > 0).OrderByDescending(x => x.value).ThenByDescending(x => x.tie).ThenBy(x => x.p.Id, StringComparer.Ordinal)
                .Select(x => (x.p, x.value + unit)).FirstOrDefault();

        public static string CategoryName(SeasonLeaderCategory c)
        {
            switch (c)
            {
                case SeasonLeaderCategory.BattingAverage: return "타율";
                case SeasonLeaderCategory.HomeRuns: return "홈런";
                case SeasonLeaderCategory.RunsBattedIn: return "타점";
                case SeasonLeaderCategory.Wins: return "다승";
                case SeasonLeaderCategory.Era: return "평균자책";
                case SeasonLeaderCategory.Saves: return "세이브";
                default: throw new ArgumentOutOfRangeException(nameof(c), c, null);
            }
        }
    }
}
