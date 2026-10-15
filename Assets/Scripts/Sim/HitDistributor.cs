using System;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.Sim
{
    /// <summary>안타 한 개가 단타·2루타·3루타·홈런 중 무엇이 될지의 가중치 (합 1).</summary>
    public readonly struct HitMix
    {
        public HitMix(double single, double @double, double triple, double homeRun)
        {
            double sum = single + @double + triple + homeRun;
            if (!(sum > 0) || double.IsInfinity(sum) || single < 0 || @double < 0 || triple < 0 || homeRun < 0)
                throw new ArgumentException($"가중치가 잘못됐다: {single}, {@double}, {triple}, {homeRun}");
            Single = single / sum;
            Double = @double / sum;
            Triple = triple / sum;
            HomeRun = homeRun / sum;
        }

        public double Single { get; }
        public double Double { get; }
        public double Triple { get; }
        public double HomeRun { get; }

        public double ExtraBase => Double + Triple + HomeRun;

        /// <summary>안타당 추가 루타 (2루타 1, 3루타 2, 홈런 3). 타율을 곱하면 순장타율(ISO).</summary>
        public double ExtraBasesPerHit => Double + 2 * Triple + 3 * HomeRun;

        public double this[PlayOutcome outcome]
        {
            get
            {
                switch (outcome)
                {
                    case PlayOutcome.Single: return Single;
                    case PlayOutcome.Double: return Double;
                    case PlayOutcome.Triple: return Triple;
                    case PlayOutcome.HomeRun: return HomeRun;
                    default: return 0;
                }
            }
        }

        public override string ToString() => $"1B {Single:P1} · 2B {Double:P1} · 3B {Triple:P1} · HR {HomeRun:P1}";
    }

    /// <summary>
    /// 안타 종류 분배기. 기본 분포(리그 평균 대결)를 타자의 장타력과 투수의 피홈런으로 기울인다. 순수 계산이며 난수는 쓰지 않는다.
    ///
    /// 1) 장타력: 순장타율 ISO = 장타율 − 타율 (장타율 = OPS − 출루율). 리그 분포 그대로면 ISO는
    ///    타율 × 안타당 추가 루타. 타자의 실제 ISO가 나오도록 장타(2·3루타·홈런) 가중치에 같은 배율 k를 곱한다.
    /// 2) 홈런 기울기: 타자 홈런 수 / <see cref="LeagueHomeRuns"/>의 제곱근 × 투수 9이닝당 피홈런 / <see cref="LeagueHomeRunsPer9"/>.
    ///    각각 범위로 자른다. 홈런 0개 타자도 하한(<see cref="MinHomeRunTilt"/>)이 있어 가중치가 0이 되지 않는다.
    /// 3) 단타 몫은 남은 비율이며 <see cref="MinSingleShare"/> 아래로 내려가지 않는다.
    ///
    /// 안전망: 기록이 없거나 NaN·무한·음수면 그 항목은 리그 평균(배율 1)으로 본다. 타자·투수 모두 데이터가 없으면
    /// 기본 분포를 그대로 돌려준다 (이전 고정 분포와 같은 결과).
    /// </summary>
    public static class HitDistributor
    {
        /// <summary>규정 타석 주전 타자의 리그 평균 홈런 수 (시즌 누적 기준).</summary>
        public const double LeagueHomeRuns = 12;
        public const double LeagueHomeRunsPer9 = 0.85;
        public const double MinIso = 0.02;
        public const double MaxIso = 0.40;
        public const double MinHomeRunTilt = 0.5;
        public const double MaxBatterHomeRunTilt = 1.8;
        public const double MaxPitcherHomeRunTilt = 2.0;
        public const double MinPitcherHomeRunTilt = 0.5;
        /// <summary>안타 중 단타 최소 몫 (극단 장타자도 단타가 나온다).</summary>
        public const double MinSingleShare = 0.30;

        /// <summary>기본 진행 출루의 안타 분포 (이전 고정표 단타 .55·2루타 .15·3루타 .02·홈런 .08을 안타끼리 정규화).</summary>
        public static readonly HitMix LeagueBaseline = new HitMix(0.55, 0.15, 0.02, 0.08);

        /// <summary>강공 성공(장타 위주) 기본 분포.</summary>
        public static readonly HitMix PowerBaseline = new HitMix(0.50, 0.28, 0.04, 0.18);

        /// <summary>
        /// 이 대결의 안타 분포. <paramref name="baseline"/>은 리그 평균 대결 기준이며 (강공처럼 장타 쪽으로 이미 기운 분포도 된다),
        /// 리그 기본 분포 대비 타자·투수가 바꾸는 배율을 그 위에 곱한다.
        /// </summary>
        public static HitMix Distribute(PlayerData batter, PlayerData pitcher, HitMix baseline)
        {
            var (xbh, hr) = Multipliers(batter, pitcher);
            if (xbh == 1 && hr == 1) return baseline;

            double d = baseline.Double * xbh;
            double t = baseline.Triple * xbh;
            double h = baseline.HomeRun * xbh * hr;
            double extra = d + t + h;
            double maxExtra = 1 - MinSingleShare;
            if (extra > maxExtra)
            {
                double shrink = maxExtra / extra;
                d *= shrink;
                t *= shrink;
                h *= shrink;
                extra = maxExtra;
            }
            double single = 1 - extra;
            if (!IsFinite(d) || !IsFinite(t) || !IsFinite(h) || !IsFinite(single) || single <= 0) return baseline;
            return new HitMix(single, d, t, h);
        }

        /// <summary>
        /// 리그 기본 분포 대비 배율: 장타 전체 배율 k와 홈런 추가 기울기. 데이터가 없으면 (1, 1).
        /// </summary>
        public static (double extraBase, double homeRun) Multipliers(PlayerData batter, PlayerData pitcher)
        {
            double hrTilt = BatterHomeRunTilt(batter) * PitcherHomeRunTilt(pitcher);
            double iso = Iso(batter);
            if (double.IsNaN(iso)) return (1, hrTilt);

            double avg = batter.BattingAverage;
            var b = LeagueBaseline;
            double perHit = b.Double + 2 * b.Triple + 3 * b.HomeRun * hrTilt;
            double k = iso / (avg * perHit);
            return IsFinite(k) && k > 0 ? (k, hrTilt) : (1, hrTilt);
        }

        /// <summary>순장타율 (장타율 − 타율, [<see cref="MinIso"/>, <see cref="MaxIso"/>]). 기록이 없으면 NaN.</summary>
        public static double Iso(PlayerData batter)
        {
            if (batter == null || MatchupCalculator.IsLeagueAverage(batter)) return double.NaN;
            double avg = batter.BattingAverage;
            double slg = batter.SluggingPercentage;
            if (!IsRate(avg) || !IsFinite(slg) || slg <= 0 || slg > 4) return double.NaN;
            return Math.Max(MinIso, Math.Min(MaxIso, slg - avg));
        }

        /// <summary>타자 홈런 기울기 √(홈런 / 리그 평균). 기록 없는 타자는 1, 홈런 0개는 하한.</summary>
        public static double BatterHomeRunTilt(PlayerData batter)
        {
            if (batter == null || MatchupCalculator.IsLeagueAverage(batter) || !IsRate(batter.BattingAverage)) return 1;
            if (batter.HomeRuns <= 0) return MinHomeRunTilt;
            return Math.Max(MinHomeRunTilt, Math.Min(MaxBatterHomeRunTilt, Math.Sqrt(batter.HomeRuns / LeagueHomeRuns)));
        }

        /// <summary>투수 홈런 기울기 (9이닝당 피홈런 / 리그 평균). 기록이 없으면 1.</summary>
        public static double PitcherHomeRunTilt(PlayerData pitcher)
        {
            if (pitcher == null || MatchupCalculator.IsLeagueAverage(pitcher)) return 1;
            double hr9 = pitcher.HomeRunsPer9;
            if (!IsFinite(hr9) || hr9 <= 0) return 1;
            return Math.Max(MinPitcherHomeRunTilt, Math.Min(MaxPitcherHomeRunTilt, hr9 / LeagueHomeRunsPer9));
        }

        static bool IsRate(double v) => IsFinite(v) && v > 0 && v <= 1;
        static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
