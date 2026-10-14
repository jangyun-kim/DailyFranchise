using System;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    public enum LineupClick
    {
        /// <summary>선택한 선수가 없었고, 누른 선수를 골랐다.</summary>
        Selected,
        /// <summary>이미 고른 선수를 다시 눌러 선택을 풀었다.</summary>
        Deselected,
        /// <summary>고른 선수와 바꿀 수 없는 선수(예: 벤치끼리)를 눌러 선택을 그쪽으로 옮겼다.</summary>
        Reselected,
        /// <summary>두 번째 선수를 눌러 맞바꾸기를 요청했다. 선택은 풀린다.</summary>
        Swap,
    }

    /// <summary>
    /// 로비 라인업 Click-to-Swap 상태. 첫 클릭으로 선수를 고르고, 두 번째 클릭으로 맞바꾼다.
    /// 화면·데이터와 무관한 순수 상태 기계이며, 바꿀 수 있는지는 호출 쪽이 넘기는 판정(예: <see cref="LineupCard.CanSwap"/>)으로 정한다.
    /// </summary>
    public sealed class LineupSelection
    {
        /// <summary>지금 고른 자리. 없으면 null.</summary>
        public LineupSpot? Selected { get; private set; }

        /// <summary><see cref="LineupClick.Swap"/>일 때 맞바꿀 두 자리 (먼저 고른 자리, 나중 자리).</summary>
        public (LineupSpot first, LineupSpot second)? PendingSwap { get; private set; }

        public LineupClick Click(LineupSpot spot, Func<LineupSpot, LineupSpot, bool> canSwap)
        {
            if (canSwap == null) throw new ArgumentNullException(nameof(canSwap));
            PendingSwap = null;

            if (!Selected.HasValue)
            {
                Selected = spot;
                return LineupClick.Selected;
            }

            var first = Selected.Value;
            if (first == spot)
            {
                Selected = null;
                return LineupClick.Deselected;
            }

            if (!canSwap(first, spot))
            {
                Selected = spot;
                return LineupClick.Reselected;
            }

            Selected = null;
            PendingSwap = (first, spot);
            return LineupClick.Swap;
        }

        public void Clear()
        {
            Selected = null;
            PendingSwap = null;
        }
    }
}
