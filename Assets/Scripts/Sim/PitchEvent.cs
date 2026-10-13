namespace DailyFranchise.Sim
{
    /// <summary>
    /// 경기 엔진이 투구마다 생성하는 이벤트. UI는 이 값을 시각화만 하고 판정하지 않는다.
    /// 일관성 규칙은 <see cref="PitchEventValidator"/>가 검증한다.
    /// </summary>
    public sealed class PitchEvent
    {
        public int Inning;
        public HalfInning HalfInning;
        public int OutsBefore;
        public Count CountBefore;

        public string PitcherId;
        public string BatterId;
        public string PitchType;
        public double VelocityKmh;

        /// <summary>홈플레이트 통과 지점 X (cm, 포수 시점 오른쪽 +).</summary>
        public double LocationX;
        /// <summary>홈플레이트 통과 지점 높이 Y (cm, 지면 기준).</summary>
        public double LocationY;
        /// <summary>이 투구에 적용된 타자별 존. UI는 이 값으로 존을 그린다.</summary>
        public StrikeZone Zone;

        public PitchResult Result;
        /// <summary>공 위치만으로 본 규칙상 스트라이크 여부. 스윙·파울·인플레이 결과와는 독립.</summary>
        public bool IsStrikeByRule;

        public Count CountAfter;
        public PlateAppearanceOutcome Outcome;
        /// <summary>인플레이 결과 설명 (예: "우전 적시타"). 인플레이가 아니면 null.</summary>
        public string PlayResult;

        public BaseState RunnersBefore;
        public BaseState RunnersAfter;
        public int RunsScored;
        public int OutsAfter;
    }
}
