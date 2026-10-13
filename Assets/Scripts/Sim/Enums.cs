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
}
