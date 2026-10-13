using System;
using System.Collections.Generic;
using DailyFranchise.Sim;
using UnityEngine;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 미니 필드(.diamond-stage 420×400) 좌표 계산. USS의 베이스 위치와 같은 값을 쓴다.
    /// 루 번호: 0 = 타석(홈), 1~3루, 4 = 홈인.
    /// </summary>
    public static class FieldGeometry
    {
        public const float StageWidth = 420f;
        public const float StageHeight = 400f;
        /// <summary>타구 끝점이 무대 가장자리에서 떨어져야 하는 최소 거리.</summary>
        public const float EdgeMargin = 12f;

        public static readonly Vector2 Home = new Vector2(210, 350);
        public static readonly Vector2 First = new Vector2(340, 220);
        public static readonly Vector2 Second = new Vector2(210, 90);
        public static readonly Vector2 Third = new Vector2(80, 220);

        public static Vector2 BasePoint(int b)
        {
            switch (b)
            {
                case 0:
                case 4: return Home;
                case 1: return First;
                case 2: return Second;
                case 3: return Third;
                default: throw new ArgumentOutOfRangeException(nameof(b), b, null);
            }
        }

        /// <summary>from루에서 to루까지 거치는 베이스 점 목록 (양 끝 포함).</summary>
        public static List<Vector2> RunPath(int from, int to)
        {
            if (from < 0 || to > 4 || to < from) throw new ArgumentException($"잘못된 진루 {from}→{to}");
            var pts = new List<Vector2>();
            for (int b = from; b <= to; b++) pts.Add(BasePoint(b));
            return pts;
        }

        /// <summary>경로 위 진행률 t(0~1) 지점. 베이스 사이 구간은 같은 시간 비율로 나눈다.</summary>
        public static Vector2 PointOnPath(IReadOnlyList<Vector2> pts, float t)
        {
            if (pts.Count == 1) return pts[0];
            t = Mathf.Clamp01(t);
            float seg = t * (pts.Count - 1);
            int i = Mathf.Min((int)seg, pts.Count - 2);
            return Vector2.Lerp(pts[i], pts[i + 1], seg - i);
        }

        /// <summary>
        /// 홈에서 각도 방향으로 내야(다이아몬드) 경계까지의 거리.
        /// 다이아몬드 경계 |x-cx| + |y-cy| = d 를 풀면 t = 2d / (|sinθ| + cosθ). 파울 지역(±45° 밖)은 45°로 본다.
        /// </summary>
        public static float InfieldEdgeDistance(double angleDeg)
        {
            float a = Mathf.Min(Mathf.Abs((float)angleDeg), 45f) * Mathf.Deg2Rad;
            float diagonal = Home.y - Second.y; // 홈~2루 = 2d
            return diagonal / (Mathf.Sin(a) + Mathf.Cos(a));
        }

        /// <summary>
        /// 타구 종류별 화면상 길이 (px). 실제 비거리가 아니라 연출용이다.
        /// 안타는 내야 경계를 넘어 외야까지, 땅볼은 내야 안에서 끝난다.
        /// </summary>
        public static float BattedBallLength(double angleDeg, PlayType t)
        {
            float edge = InfieldEdgeDistance(angleDeg);
            switch (t)
            {
                case PlayType.SacrificeBunt: return edge * 0.25f;
                case PlayType.GroundOut: return edge * 0.6f;
                case PlayType.LineOut: return edge + 20f;
                case PlayType.Single: return edge + 45f;
                case PlayType.FlyOut: return edge + 80f;
                case PlayType.Double: return edge + 100f;
                case PlayType.Triple: return edge + 125f;
                case PlayType.HomeRun: return edge + 150f;
                default: return 0f;
            }
        }

        /// <summary>
        /// 타구 끝점. 각도 0 = 2루·중견 방향(화면 위), 양수 = 1루 쪽(화면 오른쪽).
        /// 무대 밖으로 나가면 같은 방향으로 길이를 줄인다.
        /// </summary>
        public static Vector2 BattedBallEnd(double angleDeg, PlayType t)
        {
            float rad = (float)(angleDeg * Math.PI / 180.0);
            var dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
            float len = BattedBallLength(angleDeg, t);

            float minX = EdgeMargin, maxX = StageWidth - EdgeMargin;
            float minY = EdgeMargin, maxY = StageHeight - EdgeMargin;
            if (dir.x > 1e-4f) len = Mathf.Min(len, (maxX - Home.x) / dir.x);
            if (dir.x < -1e-4f) len = Mathf.Min(len, (minX - Home.x) / dir.x);
            if (dir.y < -1e-4f) len = Mathf.Min(len, (minY - Home.y) / dir.y);
            if (dir.y > 1e-4f) len = Mathf.Min(len, (maxY - Home.y) / dir.y);
            return Home + dir * Mathf.Max(0, len);
        }
    }
}
