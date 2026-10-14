using System;

namespace DailyFranchise.Sim
{
    /// <summary>
    /// 작전 성공 판정·타 구단 경기 결과 등에 쓰는 난수. 테스트에서는 고정 Seed나 고정 값으로 바꿔 끼운다.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>[0, 1) 구간의 실수.</summary>
        double NextDouble();
        /// <summary>[0, maxExclusive) 구간의 정수.</summary>
        int Next(int maxExclusive);
    }

    /// <summary>System.Random 기반. 같은 Seed면 같은 수열을 낸다.</summary>
    public sealed class SeededRandom : IRandomSource
    {
        readonly Random _random;

        public SeededRandom(int seed) => _random = new Random(seed);

        /// <summary>실행마다 다른 Seed (실제 플레이용).</summary>
        public static SeededRandom FromClock() => new SeededRandom(Environment.TickCount);

        public double NextDouble() => _random.NextDouble();
        public int Next(int maxExclusive) => _random.Next(maxExclusive);
    }

    /// <summary>정해 둔 값을 차례로 되풀이해 내는 난수. 특정 분기를 강제로 재현할 때 쓴다 (테스트·디버그).</summary>
    public sealed class ScriptedRandom : IRandomSource
    {
        readonly double[] _values;
        int _next;

        /// <param name="values">[0, 1) 구간 값. NextDouble은 그대로, Next(n)은 floor(값 × n)을 낸다.</param>
        public ScriptedRandom(params double[] values)
        {
            if (values == null || values.Length == 0) throw new ArgumentException("값이 하나 이상 필요하다.", nameof(values));
            foreach (var v in values)
                if (v < 0 || v >= 1) throw new ArgumentOutOfRangeException(nameof(values), v, "[0, 1) 밖의 값");
            _values = values;
        }

        /// <summary>지금까지 꺼낸 값 수.</summary>
        public int Draws { get; private set; }

        public double NextDouble()
        {
            Draws++;
            var v = _values[_next];
            _next = (_next + 1) % _values.Length;
            return v;
        }

        public int Next(int maxExclusive) => (int)(NextDouble() * maxExclusive);
    }

    /// <summary>경기 최종 점수 (원정 : 홈).</summary>
    public readonly struct GameScore : IEquatable<GameScore>
    {
        public readonly int Away;
        public readonly int Home;

        public GameScore(int away, int home)
        {
            if (away < 0 || home < 0) throw new ArgumentOutOfRangeException(nameof(away), "점수는 음수일 수 없다.");
            Away = away;
            Home = home;
        }

        public bool Equals(GameScore other) => Away == other.Away && Home == other.Home;
        public override bool Equals(object obj) => obj is GameScore other && Equals(other);
        public override int GetHashCode() => Away * 397 ^ Home;
        public override string ToString() => $"{Away}:{Home}";
    }
}
