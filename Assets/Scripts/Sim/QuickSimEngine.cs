using System;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.Sim
{
    /// <summary>반 이닝 하나의 간이 기록. Errors는 이 반 이닝에 수비 팀이 범한 실책이다.</summary>
    public readonly struct HalfInningLine
    {
        public readonly int Runs;
        public readonly int Hits;
        public readonly int Walks;
        public readonly int Errors;

        public HalfInningLine(int runs, int hits, int walks, int errors)
        {
            Runs = runs;
            Hits = hits;
            Walks = walks;
            Errors = errors;
        }

        public override string ToString() => $"{Runs}R {Hits}H {Walks}B {Errors}E";
    }

    /// <summary>퀵 시뮬레이션 결과 (원정·홈 이닝별 득점과 누적 H·E·B).</summary>
    public sealed class QuickSimResult
    {
        /// <summary>원정 이닝별 득점 (길이 = 시뮬레이션한 이닝 수).</summary>
        public readonly int[] AwayRuns;
        public readonly int[] HomeRuns;
        public int AwayHits, HomeHits;
        public int AwayWalks, HomeWalks;
        /// <summary>원정 팀이 수비에서 범한 실책.</summary>
        public int AwayErrors;
        public int HomeErrors;

        /// <summary>
        /// 반 이닝별 기록 (Phase 37 — 박스스코어가 선수별 타석으로 나눠 준다, <see cref="BoxScoreBuilder"/>).
        /// 원정 = 회초, 홈 = 회말. 실책은 그 반 이닝 수비 팀의 실책.
        /// </summary>
        public readonly HalfInningLine[] AwayHalves;
        public readonly HalfInningLine[] HomeHalves;

        public QuickSimResult(int innings)
        {
            AwayRuns = new int[innings];
            HomeRuns = new int[innings];
            AwayHalves = new HalfInningLine[innings];
            HomeHalves = new HalfInningLine[innings];
        }

        public int Innings => AwayRuns.Length;
        public int AwayTotal => Sum(AwayRuns);
        public int HomeTotal => Sum(HomeRuns);
        public GameScore Score => new GameScore(AwayTotal, HomeTotal);

        static int Sum(int[] runs)
        {
            int n = 0;
            foreach (var r in runs) n += r;
            return n;
        }

        public override string ToString() =>
            $"원정 [{string.Join(",", AwayRuns)}] {AwayTotal} : 홈 [{string.Join(",", HomeRuns)}] {HomeTotal}";
    }

    /// <summary>
    /// 승부처 직전까지의 경기를 이닝 단위로 빠르게 계산한다. 타석 단위 시뮬레이션이 아니라 반 이닝마다 간이 수식으로
    /// 득점을 정한다. 순수 C#이며 난수는 <see cref="IRandomSource"/>로만 받는다 (같은 Seed → 같은 결과).
    ///
    /// 공격 팀 타선 OPS(라인업 평균)와 수비 팀 선발 투수 평균자책으로:
    /// 1) 득점 확률  p = 기본 득점률 × (팀 OPS / 리그 OPS)² × (평균자책 / 리그 평균자책),  [5%, 60%]
    /// 2) 득점하면 1점, 이후 추가 득점 확률 q = 0.40 × (팀 OPS / 리그 OPS) ([10%, 60%])로 한 점씩 더 낸다.
    ///    한 이닝 득점은 <see cref="MaxRunsPerInning"/>를 넘지 않는다 (럭비 스코어 방지).
    /// 3) 안타 = 득점 + 잔루 안타 (Phase 38, <see cref="StrandedHits"/>): 난수 한 번으로 득점한 이닝은 0~3개, 무득점 이닝도
    ///    35%쯤 1~2개를 더 채운다 (리그 평균 대결 기대값 9이닝 팀당 안타 ≈ 9.8개, 타율 ≈ .264). 볼넷·실책은 반 이닝마다 한 번씩
    ///    판정하고, 잔루가 3명을 넘지 않게 안타를 자른다 (안타 + 볼넷 + 실책 − 득점 ≤ 3).
    /// 득점은 안타 수와 상관없이 1)·2)로만 정해지고, 반 이닝당 난수 사용 횟수도 Phase 37과 같다 — 같은 Seed면 득점이 그대로다.
    /// 득점 확률은 선발 평균자책으로만 계산한다. 투수 교체(가상 투구 수)는 결과 기록으로 따로 정한다 (<see cref="QuickSimStaff"/>).
    /// 선수단·기록이 없으면 리그 평균.
    /// 반 이닝마다 난수를 (득점 1 + 추가 득점 0~4 + 안타 1 + 볼넷 1 + 실책 1)번 쓴다. 순서: 1회초, 1회말, 2회초 …
    /// </summary>
    public sealed class QuickSimEngine
    {
        public const double LeagueOps = 0.740;
        public const double LeagueOnBase = MatchupCalculator.LeagueOnBase;
        public const double LeagueEra = MatchupCalculator.LeagueEra;
        /// <summary>리그 평균 대결에서 반 이닝에 1점 이상 낼 확률.</summary>
        public const double BaseScoringRate = 0.28;
        public const double MinScoringRate = 0.05;
        public const double MaxScoringRate = 0.60;
        /// <summary>리그 평균 타선이 득점한 이닝에서 한 점을 더 낼 확률.</summary>
        public const double BaseExtraRunRate = 0.40;
        public const double MinExtraRunRate = 0.10;
        public const double MaxExtraRunRate = 0.60;
        /// <summary>득점한 이닝에서 득점 수보다 안타가 3·2·1개 더 나올 기본 확률 (나머지 30%는 0개).</summary>
        public const double ScoredExtraHit3Rate = 0.10;
        public const double ScoredExtraHit2Rate = 0.25;
        public const double ScoredExtraHit1Rate = 0.35;
        /// <summary>무득점 이닝에서 안타가 2·1개 나올 기본 확률 (나머지 65%는 무안타).</summary>
        public const double ScorelessHit2Rate = 0.09;
        public const double ScorelessHit1Rate = 0.26;
        /// <summary>반 이닝 잔루 상한 (타자 아웃만 3개라 루상 주자는 3명까지).</summary>
        public const int MaxLeftOnBase = 3;
        public const double BaseWalkRate = 0.30;
        public const double ErrorRate = 0.04;
        /// <summary>계산에 쓰는 평균자책 범위 (비정상 기록이 확률을 무너뜨리지 않게).</summary>
        public const double MinEra = 1.0;
        public const double MaxEra = 9.0;
        public const int DefaultMaxRunsPerInning = 5;

        /// <summary>한 이닝 득점 상한.</summary>
        public int MaxRunsPerInning { get; }

        public QuickSimEngine(int maxRunsPerInning = DefaultMaxRunsPerInning)
        {
            if (maxRunsPerInning < 1) throw new ArgumentOutOfRangeException(nameof(maxRunsPerInning), maxRunsPerInning, "1 이상");
            MaxRunsPerInning = maxRunsPerInning;
        }

        /// <summary>라인업 평균 OPS. OPS 기록이 있는 타자가 없거나 선수단이 없으면 리그 평균.</summary>
        public static double TeamOps(TeamRoster roster) => LineupAverage(roster, p => p.Ops, LeagueOps);

        /// <summary>라인업 평균 출루율. 기록이 없으면 리그 평균.</summary>
        public static double TeamOnBase(TeamRoster roster) => LineupAverage(roster, p => p.OnBasePercentage, LeagueOnBase);

        /// <summary>선발 투수 평균자책 ([1.0, 9.0]). 선발이 없거나 기록이 없으면(0) 리그 평균.</summary>
        public static double StarterEra(TeamRoster roster)
        {
            var era = roster?.StartingPitcher?.Era ?? 0;
            if (double.IsNaN(era) || era <= 0) return LeagueEra;
            return Math.Max(MinEra, Math.Min(MaxEra, era));
        }

        /// <summary>반 이닝에 1점 이상 낼 확률.</summary>
        public static double ScoringProbability(double teamOps, double pitcherEra)
        {
            double ratio = teamOps / LeagueOps;
            double p = BaseScoringRate * ratio * ratio * (pitcherEra / LeagueEra);
            return Clamp(p, MinScoringRate, MaxScoringRate);
        }

        /// <summary>득점한 이닝에서 한 점을 더 낼 확률.</summary>
        public static double ExtraRunProbability(double teamOps) =>
            Clamp(BaseExtraRunRate * teamOps / LeagueOps, MinExtraRunRate, MaxExtraRunRate);

        /// <summary>공격 팀 <paramref name="offense"/>가 수비 팀 <paramref name="defense"/>의 선발을 상대로 한 반 이닝.</summary>
        public HalfInningLine SimulateHalf(TeamRoster offense, TeamRoster defense, IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            double ops = TeamOps(offense);
            double ratio = ops / LeagueOps;

            int runs = 0;
            if (random.NextDouble() < ScoringProbability(ops, StarterEra(defense)))
            {
                runs = 1;
                double more = ExtraRunProbability(ops);
                while (runs < MaxRunsPerInning && random.NextDouble() < more) runs++;
            }

            int hits = runs + StrandedHits(runs, ratio, random.NextDouble());
            int walks = random.NextDouble() < Clamp(BaseWalkRate * TeamOnBase(offense) / LeagueOnBase, 0, 0.9) ? 1 : 0;
            int errors = random.NextDouble() < ErrorRate ? 1 : 0;
            hits = Math.Max(runs, Math.Min(hits, runs + MaxLeftOnBase - walks - errors));
            return new HalfInningLine(runs, hits, walks, errors);
        }

        /// <summary>
        /// 득점보다 더 나오는 안타 (잔루로 남은 주자의 안타). 난수 하나(<paramref name="roll"/>)를 누적 확률 칸으로 나눈다:
        /// 득점한 이닝은 3·2·1·0개 = 10·25·35·30%, 무득점 이닝은 2·1·0개 = 9·26·65% (확률은 팀 OPS / 리그 OPS 배, 합 ≤ 95%).
        /// </summary>
        public static int StrandedHits(int runs, double opsRatio, double roll)
        {
            double k = Clamp(opsRatio, 0, 3);
            if (runs > 0)
            {
                double scale = Math.Min(k, 0.95 / (ScoredExtraHit3Rate + ScoredExtraHit2Rate + ScoredExtraHit1Rate));
                double c3 = ScoredExtraHit3Rate * scale, c2 = c3 + ScoredExtraHit2Rate * scale, c1 = c2 + ScoredExtraHit1Rate * scale;
                return roll < c3 ? 3 : roll < c2 ? 2 : roll < c1 ? 1 : 0;
            }
            double s = Math.Min(k, 0.95 / (ScorelessHit2Rate + ScorelessHit1Rate));
            double d2 = ScorelessHit2Rate * s, d1 = d2 + ScorelessHit1Rate * s;
            return roll < d2 ? 2 : roll < d1 ? 1 : 0;
        }

        /// <summary>1회부터 <paramref name="innings"/>회까지 원정·홈 반 이닝을 번갈아 계산한다.</summary>
        public QuickSimResult Simulate(TeamRoster away, TeamRoster home, IRandomSource random, int innings = 7)
        {
            if (innings < 1) throw new ArgumentOutOfRangeException(nameof(innings), innings, "1 이상");
            var result = new QuickSimResult(innings);
            for (int i = 0; i < innings; i++)
            {
                var top = SimulateHalf(away, home, random);
                result.AwayHalves[i] = top;
                result.AwayRuns[i] = top.Runs;
                result.AwayHits += top.Hits;
                result.AwayWalks += top.Walks;
                result.HomeErrors += top.Errors;

                var bottom = SimulateHalf(home, away, random);
                result.HomeHalves[i] = bottom;
                result.HomeRuns[i] = bottom.Runs;
                result.HomeHits += bottom.Hits;
                result.HomeWalks += bottom.Walks;
                result.AwayErrors += bottom.Errors;
            }
            return result;
        }

        static double LineupAverage(TeamRoster roster, Func<PlayerData, double> stat, double fallback)
        {
            if (roster == null) return fallback;
            double sum = 0;
            int n = 0;
            foreach (var p in roster.Lineup)
            {
                double v = stat(p);
                if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) continue;
                sum += v;
                n++;
            }
            return n == 0 ? fallback : sum / n;
        }

        static double Clamp(double v, double min, double max) => double.IsNaN(v) ? min : Math.Max(min, Math.Min(max, v));
    }
}
