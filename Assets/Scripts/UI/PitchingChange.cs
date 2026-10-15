namespace DailyFranchise.UI
{
    /// <summary>경기 화면의 투수 교체 기록 하나 (내 구단 수동 교체 또는 CPU 자동 교체).</summary>
    public sealed class PitchingChange
    {
        public PitchingChange(int afterIndex, string from, string to, bool byCpu, string reason = null)
        {
            AfterIndex = afterIndex;
            From = from;
            To = to;
            ByCpu = byCpu;
            Reason = reason;
        }

        /// <summary>
        /// 교체 시점 투구 인덱스. 내 구단 교체는 작전 대기 투구(1-1), CPU 교체는 이닝 교대 직전 반 이닝의 마지막 투구.
        /// </summary>
        public int AfterIndex { get; }
        public string From { get; }
        public string To { get; }
        /// <summary>CPU(상대) 구단의 자동 교체인가.</summary>
        public bool ByCpu { get; }
        /// <summary>CPU 교체 사유 (예: "체력 고갈"). 내 구단 교체는 null.</summary>
        public string Reason { get; }

        public override string ToString() => $"{(ByCpu ? "CPU " : "")}{From} → {To} @{AfterIndex}";
    }
}
