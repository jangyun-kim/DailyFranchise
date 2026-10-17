using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;

namespace DailyFranchise.Sim
{
    /// <summary>
    /// 한 경기 박스스코어 만들기 (Phase 37). 장면 자리 ID(home_ = 내 구단, away_ = 상대) 기준으로 짓는다.
    /// 1) 퀵 시뮬레이션 구간 (<see cref="GeneratedGame"/>: 1~7회 · 8회초): 반 이닝마다 정해진 득점·안타·볼넷·실책 수를 그대로 지키며
    ///    타순대로 타석을 나눈다 (<see cref="HalfInning"/>). 타순은 1번부터 이어지고, 그 반 이닝 투수는 퀵 시뮬레이션 투수 운용
    ///    (Phase 38, <see cref="GeneratedGame.PitcherFor"/>)이 정한 투수다 (선발이 5~6이닝 뒤 구원에게 넘긴다).
    /// 2) Play-by-Play 구간 (8회말부터, 풀 이닝은 1회부터): 장면의 타석 결과를 그대로 센다.
    ///    삼진·볼넷·안타(1~3루타·홈런)·범타·희생번트(타수 아님). 타점 = 그 타석에 들어온 점수. 실점은 그때 던지던 투수에게.
    /// 3) 승·패·세이브 (<see cref="Decide"/>): 득점이 난 순서대로 마지막 역전(앞서기 시작한 순간)을 찾는다.
    ///    승리 = 그때 이긴 팀의 마지막 투수 (선발이 5이닝 미만이면 첫 구원 투수), 패전 = 그 점수를 내준 투수,
    ///    세이브 = 승리 투수가 아닌 이긴 팀 마지막 투수가 3점 차 이내 리드에서 올라와 1아웃 이상 잡았거나 3이닝 이상 던졌을 때.
    /// 자책점은 실점과 같게 센다 (실책 출루 뒤 실점도 자책으로 — 간이 규칙). 무승부면 승·패·세이브가 없다.
    /// </summary>
    public static class BoxScoreBuilder
    {
        public const string MyPrefix = "home_";
        public const string OpponentPrefix = "away_";
        public const string MyStarter = "home_sp";
        public const string OpponentStarter = "away_sp";
        /// <summary>선발 승리 요건 (5이닝 = 15아웃).</summary>
        public const int StarterWinOuts = 15;
        /// <summary>세이브 요건: 올라올 때 리드 3점 이내 (1아웃 이상) 또는 3이닝(9아웃) 이상.</summary>
        public const int SaveLead = 3;
        public const int SaveLongOuts = 9;

        // 선수 기록이 없을 때 쓰는 리그 평균 비율
        const double LeagueAverage = MatchupCalculator.LeagueAverage;
        const double LeagueOnBase = MatchupCalculator.LeagueOnBase;
        const double LeagueWalkRate = 0.09;
        const double LeagueDoubleShare = 0.18;
        const double LeagueTripleShare = 0.015;
        const double LeagueHomeRunShare = 0.10;
        const double LeagueStrikeoutsPerOut = 0.28;

        /// <summary>득점·투수 타임라인 한 칸 (반 이닝 하나 또는 투구 하나).</summary>
        struct Unit
        {
            public bool MyBatting;
            public string Pitcher;
            public int Runs;
            public int Outs;
        }

        /// <param name="early">퀵 시뮬레이션 구간. null이면 없다 (풀 이닝·고정 장면).</param>
        /// <param name="path">장면에서 실제로 진행한 투구 (경기 끝 경로). null이면 없다.</param>
        /// <param name="resolve">장면 자리 → 선수 데이터 (타격·투구 성향). null이거나 모르는 자리면 리그 평균으로 나눈다.</param>
        /// <param name="random">퀵 시뮬레이션 구간 타석 배분 난수. <paramref name="early"/>가 있으면 필요하다.</param>
        public static BoxScore Build(GeneratedGame early, IReadOnlyList<PitchEvent> path, Func<string, PlayerData> resolve, IRandomSource random)
        {
            if (early != null && random == null) throw new ArgumentNullException(nameof(random));
            var box = new BoxScore();
            var units = new List<Unit>();
            int myOrder = 1, theirOrder = 1;
            if (early != null)
            {
                for (int i = 0; i < early.Early.Innings; i++)
                {
                    units.Add(Half(box, OpponentPrefix, ref theirOrder, early.Early.AwayHalves[i], early.PitcherFor(true, i), resolve, random));
                    units.Add(Half(box, MyPrefix, ref myOrder, early.Early.HomeHalves[i], early.PitcherFor(false, i), resolve, random));
                }
                int eighth = early.Early.Innings;
                units.Add(Half(box, OpponentPrefix, ref theirOrder, early.EighthTop, early.PitcherFor(true, eighth), resolve, random));
            }
            foreach (var e in path ?? Array.Empty<PitchEvent>())
                units.Add(Record(box, e));
            foreach (var sp in new[] { MyStarter, OpponentStarter })
                if (box.Pitching.TryGetValue(sp, out var line)) line.GamesStarted = 1;
            Decide(box, units);
            return box;
        }

        /// <summary>퀵 시뮬레이션 구간 배분용 난수 시드 (경기 내용으로 정해 같은 경기는 같은 박스스코어).</summary>
        public static int SeedFor(GeneratedGame game)
        {
            unchecked
            {
                int h = 17;
                foreach (var r in game.Early.AwayRuns) h = h * 31 + r;
                foreach (var r in game.Early.HomeRuns) h = h * 31 + r;
                h = h * 31 + game.AwayHits;
                h = h * 31 + game.HomeHits * 7 + game.AwayWalks * 3 + game.HomeWalks;
                h = h * 31 + game.Situation.Outs * 8 + game.Situation.Runners.RunnerCount;
                return h;
            }
        }

        // ---------- 퀵 시뮬레이션 반 이닝 ----------

        enum Kind { Out, Hit, Walk, Error }

        /// <summary>
        /// 반 이닝 하나를 타석으로 나눈다: 아웃 3 · 안타 · 볼넷 · 실책 출루 수를 그대로 쓰고 마지막 타석은 세 번째 아웃.
        /// 결과는 남은 개수 × 타자 성향(타율·볼넷 비율·출루율)으로 고른다. 홈런은 그 반 이닝 득점보다 많을 수 없고,
        /// 타점은 홈런 타자 1점씩, 남은 점수는 뒤쪽 안타부터 1점씩. 아웃은 투수 탈삼진 비율로 삼진.
        /// </summary>
        static Unit Half(BoxScore box, string prefix, ref int order, HalfInningLine line, string pitcher, Func<string, PlayerData> resolve,
            IRandomSource random)
        {
            int outs = 3, hits = line.Hits, walks = line.Walks, errors = line.Errors, homeRunBudget = line.Runs;
            var pitcherData = resolve?.Invoke(pitcher);
            var p = box.Pitcher(pitcher);
            var hitters = new List<BattingLine>();
            var homers = new List<BattingLine>();
            int guard = 0;
            while (outs > 0 && guard++ < 40)
            {
                string id = prefix + "b" + order;
                order = order % BattingOrder.Size + 1;
                var data = resolve?.Invoke(id);
                var b = box.Batter(id);
                b.PlateAppearances++;
                var kind = Choose(outs, hits, walks, errors, data, random);
                switch (kind)
                {
                    case Kind.Out:
                        outs--;
                        b.AtBats++;
                        p.Outs++;
                        if (random.NextDouble() < StrikeoutsPerOut(pitcherData))
                        {
                            b.Strikeouts++;
                            p.Strikeouts++;
                        }
                        break;
                    case Kind.Walk:
                        walks--;
                        b.Walks++;
                        p.WalksAllowed++;
                        break;
                    case Kind.Error:
                        errors--;
                        b.AtBats++;
                        break;
                    default:
                        hits--;
                        b.AtBats++;
                        b.Hits++;
                        p.HitsAllowed++;
                        double roll = random.NextDouble();
                        double hr = homeRunBudget > 0 ? Share(data, d => d.HomeRuns, LeagueHomeRunShare) : 0;
                        double triple = Share(data, d => d.Triples, LeagueTripleShare);
                        double dbl = Share(data, d => d.Doubles, LeagueDoubleShare);
                        if (roll < hr)
                        {
                            b.HomeRuns++;
                            homeRunBudget--;
                            homers.Add(b);
                        }
                        else if (roll < hr + triple) b.Triples++;
                        else if (roll < hr + triple + dbl) b.Doubles++;
                        hitters.Add(b);
                        break;
                }
            }
            // 타점: 홈런 타자 1점씩, 남은 점수는 뒤쪽 안타부터
            int runs = line.Runs;
            foreach (var h in homers)
            {
                h.RunsBattedIn++;
                runs--;
            }
            var singlesUp = hitters.Where(h => !homers.Contains(h)).Reverse().ToList();
            for (int i = 0; runs > 0 && singlesUp.Count > 0; i = (i + 1) % singlesUp.Count, runs--) singlesUp[i].RunsBattedIn++;
            p.RunsAllowed += line.Runs;
            p.EarnedRuns += line.Runs;
            return new Unit { MyBatting = prefix == MyPrefix, Pitcher = pitcher, Runs = line.Runs, Outs = 3 };
        }

        static Kind Choose(int outs, int hits, int walks, int errors, PlayerData data, IRandomSource random)
        {
            int reach = hits + walks + errors;
            if (reach == 0) return Kind.Out;
            double avg = Rate(data?.BattingAverage ?? 0, LeagueAverage);
            double obp = Rate(data?.OnBasePercentage ?? 0, LeagueOnBase);
            double walkRate = data != null && data.PlateAppearances > 0 ? (double)data.Walks / data.PlateAppearances : LeagueWalkRate;
            double wOut = outs == 1 ? 0 : outs * Clamp((1 - obp) / (1 - LeagueOnBase), 0.3, 3); // 세 번째 아웃은 마지막 타석
            double wHit = hits * Clamp(avg / LeagueAverage, 0.3, 3);
            double wWalk = walks * Clamp(walkRate / LeagueWalkRate, 0.3, 3);
            double wError = errors;
            double roll = random.NextDouble() * (wOut + wHit + wWalk + wError);
            if ((roll -= wHit) < 0 && hits > 0) return Kind.Hit;
            if ((roll -= wWalk) < 0 && walks > 0) return Kind.Walk;
            if ((roll -= wError) < 0 && errors > 0) return Kind.Error;
            if (wOut > 0) return Kind.Out;
            return hits > 0 ? Kind.Hit : walks > 0 ? Kind.Walk : Kind.Error;
        }

        static double Share(PlayerData data, Func<PlayerData, int> count, double fallback) =>
            data != null && data.Hits > 0 ? Clamp((double)count(data) / data.Hits, 0, 0.5) : fallback;

        static double StrikeoutsPerOut(PlayerData pitcher)
        {
            if (pitcher == null || pitcher.InningsPitched <= 0 || pitcher.PitcherStrikeouts <= 0) return LeagueStrikeoutsPerOut;
            double whole = Math.Floor(pitcher.InningsPitched);
            double outs = whole * 3 + Math.Round((pitcher.InningsPitched - whole) * 10);
            return outs <= 0 ? LeagueStrikeoutsPerOut : Clamp(pitcher.PitcherStrikeouts / outs, 0.05, 0.6);
        }

        static double Rate(double v, double fallback) => double.IsNaN(v) || v <= 0 ? fallback : v;

        static double Clamp(double v, double min, double max) => double.IsNaN(v) ? min : Math.Max(min, Math.Min(max, v));

        // ---------- Play-by-Play ----------

        /// <summary>투구 하나: 타석이 끝났으면 타자·투수 기록, 아웃·득점은 그때 투수에게.</summary>
        static Unit Record(BoxScore box, PitchEvent e)
        {
            bool myBatting = e.PitcherId != null ? e.PitcherId.StartsWith(OpponentPrefix, StringComparison.Ordinal)
                : e.BatterId != null && e.BatterId.StartsWith(MyPrefix, StringComparison.Ordinal);
            int outs = Math.Max(0, e.OutsAfter - e.OutsBefore);
            var p = e.PitcherId == null ? null : box.Pitcher(e.PitcherId);
            if (p != null)
            {
                p.Outs += outs;
                p.RunsAllowed += e.RunsScored;
                p.EarnedRuns += e.RunsScored;
            }
            if (e.Outcome != PlateAppearanceOutcome.None && e.BatterId != null)
            {
                var b = box.Batter(e.BatterId);
                b.PlateAppearances++;
                b.RunsBattedIn += e.RunsScored;
                switch (e.Outcome)
                {
                    case PlateAppearanceOutcome.Strikeout:
                        b.AtBats++;
                        b.Strikeouts++;
                        if (p != null) p.Strikeouts++;
                        break;
                    case PlateAppearanceOutcome.Walk:
                        b.Walks++;
                        if (p != null) p.WalksAllowed++;
                        break;
                    default:
                        if (e.PlayType == PlayType.SacrificeBunt) break;
                        b.AtBats++;
                        if (e.PlayType == PlayType.Single || e.PlayType == PlayType.Double || e.PlayType == PlayType.Triple
                            || e.PlayType == PlayType.HomeRun)
                        {
                            b.Hits++;
                            if (p != null) p.HitsAllowed++;
                            if (e.PlayType == PlayType.Double) b.Doubles++;
                            else if (e.PlayType == PlayType.Triple) b.Triples++;
                            else if (e.PlayType == PlayType.HomeRun) b.HomeRuns++;
                        }
                        break;
                }
            }
            return new Unit { MyBatting = myBatting, Pitcher = e.PitcherId, Runs = e.RunsScored, Outs = outs };
        }

        // ---------- 승·패·세이브 ----------

        static void Decide(BoxScore box, IReadOnlyList<Unit> units)
        {
            int mine = 0, theirs = 0;
            // 팀별(true = 내 구단) 지금 투수, 등판 순서, 올라올 때 리드
            var current = new Dictionary<bool, string> { [true] = MyStarter, [false] = OpponentStarter };
            var order = new Dictionary<bool, List<string>> { [true] = new List<string>(), [false] = new List<string>() };
            var entryLead = new Dictionary<string, int>();
            string winPitcher = null, losePitcher = null;
            bool? leader = null;
            foreach (var u in units)
            {
                bool fieldingMine = !u.MyBatting;
                if (u.Pitcher != null)
                {
                    current[fieldingMine] = u.Pitcher;
                    if (!order[fieldingMine].Contains(u.Pitcher))
                    {
                        order[fieldingMine].Add(u.Pitcher);
                        entryLead[u.Pitcher] = fieldingMine ? mine - theirs : theirs - mine;
                    }
                }
                if (u.Runs <= 0) continue;
                if (u.MyBatting) mine += u.Runs;
                else theirs += u.Runs;
                bool? now = mine > theirs ? true : theirs > mine ? false : (bool?)null;
                if (now.HasValue && now != leader)
                {
                    winPitcher = current[now.Value];
                    losePitcher = u.Pitcher ?? current[!now.Value];
                }
                leader = now;
            }
            if (!leader.HasValue || winPitcher == null) return; // 무승부
            bool winner = leader.Value;
            string starter = winner ? MyStarter : OpponentStarter;
            var pitchers = order[winner];
            if (winPitcher == starter && Outs(box, starter) < StarterWinOuts)
            {
                var reliever = pitchers.FirstOrDefault(id => id != starter);
                if (reliever != null) winPitcher = reliever;
            }
            box.WinningPitcher = winPitcher;
            box.LosingPitcher = losePitcher;
            box.Pitcher(winPitcher).Wins++;
            if (losePitcher != null) box.Pitcher(losePitcher).Losses++;

            string closer = pitchers.LastOrDefault();
            if (closer == null || closer == winPitcher || closer == starter) return;
            int lead = entryLead.TryGetValue(closer, out int l) ? l : 0;
            int outs = Outs(box, closer);
            if (lead >= 1 && lead <= SaveLead && outs >= 1 || outs >= SaveLongOuts)
            {
                box.SavePitcher = closer;
                box.Pitcher(closer).Saves++;
            }
        }

        static int Outs(BoxScore box, string pitcher) => box.Pitching.TryGetValue(pitcher, out var p) ? p.Outs : 0;
    }
}
