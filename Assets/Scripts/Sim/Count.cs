using System;

namespace DailyFranchise.Sim
{
    /// <summary>볼카운트. 볼넷·삼진 직후에는 4볼 / 3스트라이크 값을 가질 수 있다.</summary>
    public readonly struct Count : IEquatable<Count>
    {
        public readonly int Balls;
        public readonly int Strikes;

        public Count(int balls, int strikes)
        {
            Balls = balls;
            Strikes = strikes;
        }

        public static Count Zero => new Count(0, 0);

        /// <summary>타석 진행 중에 가능한 카운트인지 (0~3볼, 0~2스트라이크).</summary>
        public bool IsLive => Balls >= 0 && Balls <= 3 && Strikes >= 0 && Strikes <= 2;

        public bool Equals(Count other) => Balls == other.Balls && Strikes == other.Strikes;
        public override bool Equals(object obj) => obj is Count other && Equals(other);
        public override int GetHashCode() => Balls * 10 + Strikes;
        public static bool operator ==(Count a, Count b) => a.Equals(b);
        public static bool operator !=(Count a, Count b) => !a.Equals(b);

        public override string ToString() => $"{Balls}-{Strikes}";
    }
}
