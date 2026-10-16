using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>신인 한 명의 지명 (Phase 30).</summary>
    public sealed class DraftPick
    {
        public DraftPick(string team, int round, PlayerData player)
        {
            Team = team;
            Round = round;
            Player = player ?? throw new ArgumentNullException(nameof(player));
        }

        public string Team { get; }
        /// <summary>1부터. 1라운드가 구단의 첫 지명 ("특급 유망주").</summary>
        public int Round { get; }
        public PlayerData Player { get; }
    }

    /// <summary>오프시즌 신인 드래프트 한 번의 결과 (Phase 30).</summary>
    public sealed class DraftReport
    {
        /// <summary>모든 구단의 지명 (구단 이름 순, 구단 안에서는 라운드 순).</summary>
        public readonly List<DraftPick> Picks = new List<DraftPick>();
        /// <summary>내 구단 지명.</summary>
        public List<DraftPick> MyPicks = new List<DraftPick>();
    }

    /// <summary>
    /// 가상 신인 선수 생성 (Phase 30). 순수 C#. 마스터 CSV 선수와 같은 <see cref="PlayerData"/> 구조로 만들어 확률 계산기가
    /// 구분 없이 계산한다 (타자는 타율·출루율·OPS·HR/9 등 확률 계산이 쓰는 기록, 투수는 피안타율·평균자책·HR/9).
    /// - ID "p_rookie_{연도}_{Guid}" (Guid는 주입 난수로 만들어 시드가 같으면 같은 선수), 한국어 이름(성 + 두 글자), 18~22세.
    /// - 기록은 리그 평균 이하 (WAR −0.5 ~ +0.5, 타율 .200~.265, 평균자책 5.0~6.8): 바로 1군 주전감은 아니고,
    ///   24세 이하 유망주 에이징 커브(<see cref="AgingCurve"/> — 해마다 75% 확률로 폼 +0.5~3%p)로 자라 1군에 올라온다.
    /// - 라운드가 빠를수록 기록 품질이 높다: 1라운드 60~100%, 2라운드 30~70%, 그 뒤 0~50% (<see cref="QualityOf"/>).
    /// </summary>
    public static class RookieGenerator
    {
        public const string IdPrefix = "p_rookie_";
        public const int MinAge = 18;
        public const int MaxAge = 22;
        public const string PitcherPosition = "P";

        static readonly string[] Surnames =
        {
            "김", "김", "김", "이", "이", "박", "박", "최", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권", "황",
            "안", "송", "류", "홍", "전", "고", "문", "양", "손", "배",
        };

        static readonly string[] FirstSyllables =
        {
            "민", "서", "도", "예", "시", "주", "하", "지", "준", "현", "건", "우", "승", "태", "재", "동", "성", "영", "정", "원",
            "규", "진", "상", "형", "한", "선", "윤", "찬", "은", "유",
        };

        static readonly string[] SecondSyllables =
        {
            "준", "우", "호", "윤", "원", "현", "민", "진", "혁", "수", "빈", "성", "훈", "율", "후", "찬", "규", "석", "환", "범",
            "영", "철", "욱", "결", "웅", "재", "건", "태", "엽", "균",
        };

        /// <summary>라운드별 기록 품질 (0~1).</summary>
        public static double QualityOf(int round, IRandomSource random)
        {
            double lo = round <= 1 ? 0.6 : round == 2 ? 0.3 : 0.0;
            return lo + random.NextDouble() * 0.4 + (round >= 3 ? random.NextDouble() * 0.1 : 0);
        }

        /// <summary>"p_rookie_2027_3f2a…" — Guid 16바이트를 주입 난수로 만든다.</summary>
        public static string NewId(int year, IRandomSource random)
        {
            var bytes = new byte[16];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)random.Next(256);
            return $"{IdPrefix}{year}_{new Guid(bytes):N}";
        }

        public static bool IsRookie(PlayerData p) => p?.Id != null && p.Id.StartsWith(IdPrefix, StringComparison.Ordinal);

        /// <summary>리그에 없는 이름 (10번 안에 못 찾으면 겹친 이름 그대로).</summary>
        public static string NewName(IRandomSource random, ICollection<string> taken)
        {
            string name = null;
            for (int i = 0; i < 10; i++)
            {
                name = Surnames[random.Next(Surnames.Length)] + FirstSyllables[random.Next(FirstSyllables.Length)]
                    + SecondSyllables[random.Next(SecondSyllables.Length)];
                if (taken == null || !taken.Contains(name)) break;
            }
            taken?.Add(name);
            return name;
        }

        /// <summary>
        /// 신인 한 명. <paramref name="position"/>이 "P"면 불펜 투수, 아니면 그 수비 위치의 벤치 야수 (둘 다 퓨처스에 들어간다).
        /// </summary>
        public static PlayerData Create(string team, int year, int round, string position, int order, IRandomSource random,
            ICollection<string> takenNames = null)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            bool pitcher = position == PitcherPosition;
            double q = Math.Min(1, QualityOf(round, random));
            var p = new PlayerData
            {
                Id = NewId(year, random),
                Name = NewName(random, takenNames),
                Team = team,
                Position = position,
                Role = pitcher ? RosterRole.Bullpen : RosterRole.Bench,
                Order = order,
                Memo = $"{year} 신인 {round}R",
                Year = year,
                Age = MinAge + random.Next(MaxAge - MinAge + 1),
                Number = 50 + random.Next(50),
                War = Math.Round(Math.Max(-0.5, Math.Min(0.5, -0.5 + q + (random.NextDouble() - 0.5) * 0.2)), 2),
            };
            p.Throws = random.NextDouble() < (pitcher ? 0.7 : 0.85) ? Hand.Right : Hand.Left;
            double bats = random.NextDouble();
            p.Bats = pitcher ? p.Throws : bats < 0.6 ? Hand.Right : bats < 0.95 ? Hand.Left : Hand.Switch;
            if (pitcher) FillPitching(p, q, random);
            else FillBatting(p, q, random);
            return p;
        }

        static double Jitter(IRandomSource random, double size) => (random.NextDouble() * 2 - 1) * size;

        static void FillBatting(PlayerData p, double q, IRandomSource random)
        {
            double avg = Math.Round(0.200 + 0.055 * q + Jitter(random, 0.01), 3);
            double obp = Math.Round(avg + 0.055 + 0.02 * q + Jitter(random, 0.01), 3);
            double slg = Math.Round(avg + 0.08 + 0.07 * q + Jitter(random, 0.015), 3);
            p.Games = 10 + random.Next(31);
            p.PlateAppearances = (int)Math.Round(p.Games * (2.2 + random.NextDouble()));
            p.EffectivePlateAppearances = p.PlateAppearances;
            p.AtBats = (int)Math.Round(p.PlateAppearances * 0.88);
            p.BattingAverage = avg;
            p.OnBasePercentage = obp;
            p.RecordedSlugging = slg;
            p.Ops = Math.Round(obp + slg, 3);
            p.ScoringPositionAverage = Math.Round(avg + Jitter(random, 0.02), 3);
            p.Hits = (int)Math.Round(p.AtBats * avg);
            p.Doubles = (int)Math.Round(p.Hits * 0.18);
            p.Triples = random.NextDouble() < 0.3 ? 1 : 0;
            p.HomeRuns = (int)Math.Round(q * 3 * random.NextDouble());
            p.TotalBases = p.Hits + p.Doubles + 2 * p.Triples + 3 * p.HomeRuns;
            p.Runs = (int)Math.Round(p.Hits * 0.45);
            p.RunsBattedIn = (int)Math.Round(p.Hits * 0.4) + p.HomeRuns;
            bool fast = p.Position == "CF" || p.Position == "SS" || p.Position == "2B";
            p.StolenBases = random.Next(fast ? 8 : 3);
            p.CaughtStealing = random.Next(2);
            p.Walks = (int)Math.Round(p.PlateAppearances * 0.08);
            p.HitByPitch = random.Next(3);
            p.Strikeouts = (int)Math.Round(p.PlateAppearances * (0.27 - 0.05 * q));
            p.GroundIntoDoublePlays = random.Next(4);
            p.WrcPlus = Math.Round(55 + 40 * q + Jitter(random, 5), 1);
            p.RunsPerEffectivePa = Math.Round(0.08 + 0.04 * q, 3);
            p.OffensiveWar = Math.Round(p.War * 0.8, 2);
            p.DefensiveWar = Math.Round(p.War - p.OffensiveWar, 2);
        }

        static void FillPitching(PlayerData p, double q, IRandomSource random)
        {
            double era = Math.Round(6.6 - 1.6 * q + Jitter(random, 0.2), 2);
            p.Games = 3 + random.Next(13);
            p.ReliefGames = p.Games;
            p.InningsPitched = Math.Round(p.Games * (1 + random.NextDouble()), 0);
            p.Era = era;
            p.EarnedRuns = (int)Math.Round(era * p.InningsPitched / 9);
            p.RunsAllowed = p.EarnedRuns + random.Next(3);
            p.RunsPer9 = p.InningsPitched > 0 ? Math.Round(p.RunsAllowed * 9 / p.InningsPitched, 2) : 0;
            p.OpponentAverage = Math.Round(0.310 - 0.03 * q + Jitter(random, 0.008), 3);
            p.Fip = Math.Round(era - 0.2 + Jitter(random, 0.3), 2);
            p.Whip = Math.Round(1.85 - 0.3 * q + Jitter(random, 0.05), 2);
            p.HomeRunsPer9 = Math.Round(1.5 - 0.4 * q + Jitter(random, 0.1), 2);
            p.HitsAllowed = (int)Math.Round(p.InningsPitched * (p.Whip - 0.45));
            p.WalksAllowed = (int)Math.Round(p.InningsPitched * 0.45);
            p.HomeRunsAllowed = (int)Math.Round(p.HomeRunsPer9 * p.InningsPitched / 9);
            p.PitcherStrikeouts = (int)Math.Round(p.InningsPitched * (0.6 + 0.3 * q));
            p.BattersFaced = (int)Math.Round(p.InningsPitched * 4.6);
            p.Wins = random.Next(2);
            p.Losses = random.Next(3);
            p.Holds = random.Next(2);
        }
    }

    /// <summary>
    /// 오프시즌 신인 드래프트 (Phase 30, <see cref="SeasonState.TransitionToNextSeason(LeagueRosterBook, IRandomSource)"/>의 에이징 뒤).
    /// - 선수단이 있는 구단마다(구단 이름 순) <see cref="MinPicks"/>~<see cref="MaxPicks"/>명을 지명해 시즌 신인 목록에 남긴다
    ///   (<see cref="SeasonState.Rookies"/> — 장부가 그 구단 퓨처스 끝에 넣고 저장 파일에 남는다).
    /// - 포지션: 1라운드는 구단에서 가장 부족한 자리 (은퇴하지 않은 1군·퓨처스 인원 ÷ <see cref="TargetDepth"/>가 가장 작은 자리,
    ///   같으면 난수). 2라운드부터는 <see cref="NeedPickChance"/>(65%) 확률로 부족한 자리, 아니면 무작위 (투수 반, 야수 위치 반).
    /// - 뉴스: 1라운드 지명을 "[드래프트]"로 최대 <see cref="MaxDraftNews"/>건 (내 구단 먼저).
    /// </summary>
    public static class RookieDraft
    {
        public const int MinPicks = 3;
        public const int MaxPicks = 5;
        public const double NeedPickChance = 0.65;
        public const int MaxDraftNews = 3;

        /// <summary>자리별 목표 인원 (1군 + 퓨처스). 투수는 구단 절반쯤.</summary>
        public static readonly IReadOnlyDictionary<string, int> TargetDepth = new Dictionary<string, int>
        {
            ["C"] = 3, ["1B"] = 2, ["2B"] = 2, ["3B"] = 2, ["SS"] = 2, ["LF"] = 2, ["CF"] = 2, ["RF"] = 2,
            [RookieGenerator.PitcherPosition] = 14,
        };

        public static DraftReport Process(SeasonState season, LeagueRosterBook book, IRandomSource random)
        {
            if (season == null) throw new ArgumentNullException(nameof(season));
            if (random == null) throw new ArgumentNullException(nameof(random));
            var report = new DraftReport();
            if (book == null) return report;

            bool Active(PlayerData p) => !LeagueRosterBook.IsReplacement(p) && season.ProgressionOf(p.Id)?.Retired != true;
            var names = new HashSet<string>(book.Rosters.SelectMany(r => LeagueRosterBook.AllOf(r).Concat(r.Reserves)).Select(p => p.Name)
                .Concat(season.Rookies.Select(p => p.Name)));
            var teams = book.Rosters
                .Where(r => r.Team != LeagueRosterBook.FreeAgentTeam && LeagueRosterBook.AllOf(r).Any())
                .OrderBy(r => r.Team, StringComparer.Ordinal).ToList();

            foreach (var roster in teams)
            {
                var players = LeagueRosterBook.AllOf(roster).Concat(roster.Reserves).Where(Active).ToList();
                var depth = TargetDepth.Keys.ToDictionary(k => k, k => players.Count(p => KeyOf(p) == k));
                int benchOrder = players.Where(p => !p.IsPitcher && p.Role == RosterRole.Bench).Select(p => p.Order).DefaultIfEmpty(0).Max();
                int penOrder = players.Where(p => p.Role == RosterRole.Bullpen).Select(p => p.Order).DefaultIfEmpty(0).Max();
                int picks = MinPicks + random.Next(MaxPicks - MinPicks + 1);
                for (int round = 1; round <= picks; round++)
                {
                    string position = round == 1 || random.NextDouble() < NeedPickChance ? MostNeeded(depth, random) : RandomPosition(random);
                    int order = position == RookieGenerator.PitcherPosition ? ++penOrder : ++benchOrder;
                    var rookie = RookieGenerator.Create(roster.Team, season.Year, round, position, order, random, names);
                    depth[position]++;
                    if (!season.AddRookie(rookie)) continue;
                    report.Picks.Add(new DraftPick(roster.Team, round, rookie));
                }
            }
            report.MyPicks = report.Picks.Where(p => p.Team == season.MyTeam).ToList();

            foreach (var pick in report.Picks.Where(p => p.Round == 1).OrderByDescending(p => p.Team == season.MyTeam).Take(MaxDraftNews))
                season.AddNews(NewsWriter.Drafted(pick.Team, pick.Player.Name, pick.Player.Position, pick.Round, season.Year));
            return report;
        }

        /// <summary>목표 인원 키: 투수는 "P", 야수는 수비 위치 (목록에 없는 위치 — DH 등 — 는 null).</summary>
        static string KeyOf(PlayerData p)
        {
            if (p.IsPitcher) return RookieGenerator.PitcherPosition;
            string pos = p.Position?.Trim().ToUpperInvariant();
            return pos != null && TargetDepth.ContainsKey(pos) ? pos : null;
        }

        /// <summary>인원 ÷ 목표가 가장 작은 자리 (같으면 난수로 하나).</summary>
        public static string MostNeeded(IReadOnlyDictionary<string, int> depth, IRandomSource random)
        {
            double Ratio(string k) => depth[k] / (double)TargetDepth[k];
            double min = TargetDepth.Keys.Min(Ratio);
            var tied = TargetDepth.Keys.Where(k => Math.Abs(Ratio(k) - min) < 1e-9).ToList();
            return tied[random.Next(tied.Count)];
        }

        static string RandomPosition(IRandomSource random) =>
            random.NextDouble() < 0.5
                ? RookieGenerator.PitcherPosition
                : PositionMatcher.FieldPositions[random.Next(PositionMatcher.FieldPositions.Count)];
    }
}
