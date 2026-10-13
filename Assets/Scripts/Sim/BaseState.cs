using System;
using System.Collections.Generic;

namespace DailyFranchise.Sim
{
    /// <summary>루상 주자. 각 루에는 주자 ID가 들어가며 null이면 빈 루다.</summary>
    public readonly struct BaseState : IEquatable<BaseState>
    {
        public readonly string First;
        public readonly string Second;
        public readonly string Third;

        public BaseState(string first, string second, string third)
        {
            First = first;
            Second = second;
            Third = third;
        }

        public static BaseState Empty => default;

        public int RunnerCount => (First != null ? 1 : 0) + (Second != null ? 1 : 0) + (Third != null ? 1 : 0);

        public bool Equals(BaseState other) =>
            First == other.First && Second == other.Second && Third == other.Third;
        public override bool Equals(object obj) => obj is BaseState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(First, Second, Third);
        public static bool operator ==(BaseState a, BaseState b) => a.Equals(b);
        public static bool operator !=(BaseState a, BaseState b) => !a.Equals(b);

        /// <summary>"1·3루", "만루", "주자 없음" 형태의 표기.</summary>
        public override string ToString()
        {
            if (RunnerCount == 0) return "주자 없음";
            if (RunnerCount == 3) return "만루";
            var parts = new List<string>(3);
            if (First != null) parts.Add("1");
            if (Second != null) parts.Add("2");
            if (Third != null) parts.Add("3");
            return string.Join("·", parts) + "루";
        }
    }
}
