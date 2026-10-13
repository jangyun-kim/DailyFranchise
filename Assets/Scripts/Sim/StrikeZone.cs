using System;

namespace DailyFranchise.Sim
{
    /// <summary>
    /// 한 타자에게 적용되는 스트라이크 존 (홈플레이트 통과 평면, cm 단위).
    /// X: 홈플레이트 중심 기준, 포수 시점 오른쪽이 +.
    /// Y: 지면 기준 높이.
    /// </summary>
    public readonly struct StrikeZone : IEquatable<StrikeZone>
    {
        public readonly double HalfWidthCm;
        public readonly double TopCm;
        public readonly double BottomCm;
        /// <summary>판정 시 존을 공 반지름만큼 넓혀 "공의 일부라도 걸치면 스트라이크"를 표현. 0이면 공 중심 기준.</summary>
        public readonly double BallRadiusCm;

        public StrikeZone(double halfWidthCm, double topCm, double bottomCm, double ballRadiusCm)
        {
            if (halfWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidthCm));
            if (topCm <= bottomCm) throw new ArgumentException("존 상단은 하단보다 높아야 한다.");
            if (ballRadiusCm < 0) throw new ArgumentOutOfRangeException(nameof(ballRadiusCm));
            HalfWidthCm = halfWidthCm;
            TopCm = topCm;
            BottomCm = bottomCm;
            BallRadiusCm = ballRadiusCm;
        }

        public bool Equals(StrikeZone other) =>
            HalfWidthCm == other.HalfWidthCm && TopCm == other.TopCm &&
            BottomCm == other.BottomCm && BallRadiusCm == other.BallRadiusCm;
        public override bool Equals(object obj) => obj is StrikeZone other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(HalfWidthCm, TopCm, BottomCm, BallRadiusCm);

        public override string ToString() =>
            $"±{HalfWidthCm:0.##} × [{BottomCm:0.##}, {TopCm:0.##}] (r={BallRadiusCm:0.##})";
    }

    /// <summary>
    /// 리그별 존 규칙 데이터. 리그·시즌별로 값을 바꿔 끼울 수 있게 코드와 분리한다.
    /// </summary>
    public sealed class ZoneRuleConfig
    {
        /// <summary>홈플레이트 폭 17인치 (야구 규칙상 고정값).</summary>
        public const double StandardPlateWidthCm = 43.18;

        public string Id { get; }
        public double PlateWidthCm { get; }
        /// <summary>홈플레이트 좌우 각각에 더하는 확장 폭.</summary>
        public double HorizontalExpansionCm { get; }
        /// <summary>존 상단 = 신장 × TopRatio.</summary>
        public double TopRatio { get; }
        /// <summary>존 하단 = 신장 × BottomRatio.</summary>
        public double BottomRatio { get; }
        public double BallRadiusCm { get; }
        /// <summary>값이 공식 출처로 확인되지 않았으면 true. UI·문서에 "공식 재현"이라 표기하지 않는다.</summary>
        public bool IsProvisional { get; }

        public ZoneRuleConfig(string id, double plateWidthCm, double horizontalExpansionCm,
            double topRatio, double bottomRatio, double ballRadiusCm, bool isProvisional)
        {
            if (topRatio <= bottomRatio) throw new ArgumentException("TopRatio는 BottomRatio보다 커야 한다.");
            Id = id;
            PlateWidthCm = plateWidthCm;
            HorizontalExpansionCm = horizontalExpansionCm;
            TopRatio = topRatio;
            BottomRatio = bottomRatio;
            BallRadiusCm = ballRadiusCm;
            IsProvisional = isProvisional;
        }

        /// <summary>
        /// 임시 KBO 유사 규칙. 비율·확장 폭은 기획 문서 수치(상단 56.35%, 하단 27.64%, 좌우 +2cm)를
        /// 그대로 옮긴 것으로, 시즌별 공식 수치 확인 전까지 IsProvisional=true로 둔다.
        /// </summary>
        public static readonly ZoneRuleConfig ProvisionalKbo =
            new ZoneRuleConfig("kbo-provisional", StandardPlateWidthCm, 2.0, 0.5635, 0.2764, 0.0, true);

        public StrikeZone ZoneFor(double batterHeightCm)
        {
            if (batterHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(batterHeightCm));
            return new StrikeZone(
                PlateWidthCm / 2.0 + HorizontalExpansionCm,
                batterHeightCm * TopRatio,
                batterHeightCm * BottomRatio,
                BallRadiusCm);
        }
    }

    /// <summary>스트라이크 존 판정의 유일한 구현. 엔진과 UI 모두 이 함수만 사용한다.</summary>
    public static class StrikeZoneRule
    {
        public static bool IsStrike(in StrikeZone zone, double locationX, double locationY)
        {
            double r = zone.BallRadiusCm;
            return Math.Abs(locationX) <= zone.HalfWidthCm + r
                && locationY <= zone.TopCm + r
                && locationY >= zone.BottomCm - r;
        }
    }
}
