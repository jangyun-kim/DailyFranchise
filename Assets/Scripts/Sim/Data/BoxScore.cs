using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Data
{
    /// <summary>한 경기 타자 한 명의 기록 (Phase 37).</summary>
    [Serializable]
    public sealed class BattingLine
    {
        public int Games;
        public int PlateAppearances;
        public int AtBats;
        public int Hits;
        public int Doubles;
        public int Triples;
        public int HomeRuns;
        public int Walks;
        public int Strikeouts;
        public int RunsBattedIn;

        public void Add(BattingLine o)
        {
            Games += o.Games;
            PlateAppearances += o.PlateAppearances;
            AtBats += o.AtBats;
            Hits += o.Hits;
            Doubles += o.Doubles;
            Triples += o.Triples;
            HomeRuns += o.HomeRuns;
            Walks += o.Walks;
            Strikeouts += o.Strikeouts;
            RunsBattedIn += o.RunsBattedIn;
        }

        public override string ToString() => $"{AtBats}타수 {Hits}안타 {HomeRuns}홈런 {RunsBattedIn}타점 {Walks}볼넷";
    }

    /// <summary>한 경기 투수 한 명의 기록 (Phase 37). 이닝은 아웃 수로 센다.</summary>
    [Serializable]
    public sealed class PitchingLine
    {
        public int Games;
        public int GamesStarted;
        public int Outs;
        public int HitsAllowed;
        public int WalksAllowed;
        public int Strikeouts;
        public int RunsAllowed;
        public int EarnedRuns;
        public int Wins;
        public int Losses;
        public int Saves;

        public void Add(PitchingLine o)
        {
            Games += o.Games;
            GamesStarted += o.GamesStarted;
            Outs += o.Outs;
            HitsAllowed += o.HitsAllowed;
            WalksAllowed += o.WalksAllowed;
            Strikeouts += o.Strikeouts;
            RunsAllowed += o.RunsAllowed;
            EarnedRuns += o.EarnedRuns;
            Wins += o.Wins;
            Losses += o.Losses;
            Saves += o.Saves;
        }

        public override string ToString() => $"{Outs / 3}.{Outs % 3}이닝 {EarnedRuns}자책 {Strikeouts}삼진 {Wins}승 {Losses}패 {Saves}세이브";
    }

    /// <summary>
    /// 한 경기 박스스코어 (Phase 37, <see cref="BoxScoreBuilder"/>). 선수 ID별 타격·투구 기록과 승·패·세이브 투수.
    /// 경기 화면은 장면 자리 ID(home_b3 …)로 짓고, 바깥으로 넘기기 전에 선수 DB ID로 바꾼다 (<see cref="Remap"/>).
    /// </summary>
    public sealed class BoxScore
    {
        readonly Dictionary<string, BattingLine> _batting = new Dictionary<string, BattingLine>();
        readonly Dictionary<string, PitchingLine> _pitching = new Dictionary<string, PitchingLine>();

        public IReadOnlyDictionary<string, BattingLine> Batting => _batting;
        public IReadOnlyDictionary<string, PitchingLine> Pitching => _pitching;

        public string WinningPitcher { get; set; }
        public string LosingPitcher { get; set; }
        public string SavePitcher { get; set; }

        /// <summary>이 타자의 기록 (처음이면 출장 1경기로 만든다).</summary>
        public BattingLine Batter(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("타자 ID가 비어 있다.", nameof(id));
            if (!_batting.TryGetValue(id, out var line)) _batting[id] = line = new BattingLine { Games = 1 };
            return line;
        }

        /// <summary>이 투수의 기록 (처음이면 등판 1경기로 만든다).</summary>
        public PitchingLine Pitcher(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("투수 ID가 비어 있다.", nameof(id));
            if (!_pitching.TryGetValue(id, out var line)) _pitching[id] = line = new PitchingLine { Games = 1 };
            return line;
        }

        /// <summary>
        /// ID를 바꾼 사본 (장면 자리 → 선수 DB ID). <paramref name="idOf"/>가 null을 돌려주는 자리(선수 DB에 없는 더미 선수)는 뺀다.
        /// 두 자리가 같은 선수면 합친다 (경기 수는 1).
        /// </summary>
        public BoxScore Remap(Func<string, string> idOf)
        {
            if (idOf == null) throw new ArgumentNullException(nameof(idOf));
            var box = new BoxScore();
            foreach (var kv in _batting.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var id = idOf(kv.Key);
                if (string.IsNullOrEmpty(id)) continue;
                var line = box.Batter(id);
                line.Add(kv.Value);
                line.Games = 1;
            }
            foreach (var kv in _pitching.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var id = idOf(kv.Key);
                if (string.IsNullOrEmpty(id)) continue;
                var line = box.Pitcher(id);
                line.Add(kv.Value);
                line.Games = 1;
                line.GamesStarted = Math.Min(1, line.GamesStarted);
            }
            box.WinningPitcher = MapOrNull(WinningPitcher, idOf);
            box.LosingPitcher = MapOrNull(LosingPitcher, idOf);
            box.SavePitcher = MapOrNull(SavePitcher, idOf);
            return box;
        }

        static string MapOrNull(string id, Func<string, string> idOf) => id == null ? null : idOf(id);
    }
}
