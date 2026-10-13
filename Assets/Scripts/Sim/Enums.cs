namespace DailyFranchise.Sim
{
    public enum HalfInning
    {
        Top,
        Bottom,
    }

    /// <summary>투구 한 개의 직접 결과. 공 위치(존 안/밖)와는 별개다.</summary>
    public enum PitchResult
    {
        Ball,
        CalledStrike,
        SwingingStrike,
        Foul,
        InPlay,
    }

    /// <summary>이 투구로 타석이 끝났다면 그 방식. None이면 타석 계속.</summary>
    public enum PlateAppearanceOutcome
    {
        None,
        Strikeout,
        Walk,
        InPlay,
    }

    /// <summary>인플레이 타구 결과 종류. 인플레이가 아니면 None.</summary>
    public enum PlayType
    {
        None,
        Single,
        Double,
        Triple,
        HomeRun,
        GroundOut,
        FlyOut,
        LineOut,
        /// <summary>희생 번트 (스퀴즈 포함). 타자는 아웃, 주자 진루. 타수에 넣지 않는다.</summary>
        SacrificeBunt,
    }

    /// <summary>감독이 승부처에서 고를 수 있는 작전.</summary>
    public enum TacticKind
    {
        /// <summary>개입하지 않고 원래 흐름대로.</summary>
        Default,
        SwingAway,
        SqueezeBunt,
    }
}
