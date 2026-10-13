using System;
using System.Collections.Generic;
using DailyFranchise.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 중앙 우하단 ABS 존. PitchEvent의 좌표·존·결과를 그대로 그리며 판정하지 않는다.
    /// 좌표계: X는 포수 시점 오른쪽 +, Y는 지면 기준 높이 (cm).
    /// </summary>
    public sealed class AbsZoneView
    {
        // 표시 범위 (cm). 범위를 벗어난 공은 가장자리에 붙여 그린다.
        const float ViewHalfWidthCm = 45f;
        const float ViewTopCm = 125f;
        const float ViewBottomCm = 25f;
        /// <summary>타석 진행 중 함께 보여줄 이전 투구 수.</summary>
        public const int MaxPreviousMarkers = 3;
        /// <summary>타석 종료 시 보여줄 투구 분포 최대 개수 (결정구 포함).</summary>
        public const int MaxPlateAppearanceMarkers = 6;

        public const string FlashClass = "abs__zone--flash";

        readonly VisualElement _plot;
        readonly VisualElement _zone;
        readonly VisualElement _markers;
        readonly Label _count;
        readonly Label _result;
        readonly Label _detail;
        readonly Label _matchup;

        IReadOnlyList<PitchEvent> _events;
        ScreenState _state;
        bool _showHistory = true;
        IVisualElementScheduledItem _flashEnd;

        public AbsZoneView(VisualElement root)
        {
            _plot = root.Q("abs-plot");
            _zone = root.Q("abs-zone");
            _markers = root.Q("abs-markers");
            _count = root.Q<Label>("abs-count");
            _result = root.Q<Label>("abs-result");
            _detail = root.Q<Label>("abs-detail");
            _matchup = root.Q<Label>("abs-matchup");
            _plot.RegisterCallback<GeometryChangedEvent>(_ => LayoutPlot());
        }

        public bool IsFlashing => _zone.ClassListContains(FlashClass);

        /// <param name="showHistory">false면 현재 공만 표시 (x4 간략 모드).</param>
        public void Render(IReadOnlyList<PitchEvent> events, ScreenState state,
            Func<string, string> nameOf, bool showHistory)
        {
            _events = events;
            _state = state;
            _showHistory = showHistory;

            SetResultClass(_result, null);
            var e = state.Pitch;
            if (e == null)
            {
                _count.text = state.Count.ToString();
                _result.text = "투구 대기";
                _detail.text = $"존 높이 {state.Zone.BottomCm:0.0}–{state.Zone.TopCm:0.0} cm";
                _matchup.text = $"투수 {nameOf(state.PitcherId)} → 타자 {nameOf(state.BatterId)}";
            }
            else
            {
                _count.text = $"{e.CountBefore} → {e.CountAfter}";
                SetResultClass(_result, PitchPresentation.ResultClass(e));
                var swing = PitchPresentation.SwingNote(e);
                _result.text = swing == null
                    ? PitchPresentation.ResultText(e)
                    : $"{PitchPresentation.ResultText(e)}  <size=14>{swing}</size>";
                _detail.text = $"{e.PitchType} · {e.VelocityKmh:0} km/h · {PitchPresentation.ZoneNote(e)}";
                _matchup.text = $"{state.PitchNumberInPlateAppearance}구째 · 투수 {nameOf(e.PitcherId)} → 타자 {nameOf(e.BatterId)}";
            }

            LayoutPlot();
        }

        /// <summary>존 테두리를 결과 색으로 잠깐 점등. seconds가 0 이하면 생략.</summary>
        public void Flash(PitchEvent e, float seconds)
        {
            _flashEnd?.Pause();
            ClearFlash();
            if (e == null || seconds <= 0) return;

            _zone.AddToClassList(FlashClass);
            SetResultClass(_zone, PitchPresentation.ResultClass(e));
            _flashEnd = _zone.schedule.Execute(ClearFlash).StartingIn((long)(seconds * 1000));
        }

        /// <summary>예약된 점등 해제를 취소하고 즉시 기본 테두리로 돌린다.</summary>
        public void CancelFlash()
        {
            _flashEnd?.Pause();
            ClearFlash();
        }

        void ClearFlash()
        {
            _zone.RemoveFromClassList(FlashClass);
            SetResultClass(_zone, null);
        }

        static void SetResultClass(VisualElement el, string cls)
        {
            foreach (var c in PitchPresentation.AllResultClasses) el.RemoveFromClassList(c);
            if (cls != null) el.AddToClassList(cls);
        }

        void LayoutPlot()
        {
            if (_state == null) return;
            float w = _plot.contentRect.width;
            float h = _plot.contentRect.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0) return;

            float scale = Mathf.Min(w / (2 * ViewHalfWidthCm), h / (ViewTopCm - ViewBottomCm));
            float centerX = w / 2f;
            float centerY = h / 2f;
            float midCm = (ViewTopCm + ViewBottomCm) / 2f;
            float ToPxX(double cm) => centerX + (float)cm * scale;
            float ToPxY(double cm) => centerY - ((float)cm - midCm) * scale;

            var zone = _state.Zone;
            _zone.style.left = ToPxX(-zone.HalfWidthCm);
            _zone.style.top = ToPxY(zone.TopCm);
            _zone.style.width = (float)(2 * zone.HalfWidthCm) * scale;
            _zone.style.height = (float)(zone.TopCm - zone.BottomCm) * scale;

            _markers.Clear();
            if (_state.Pitch == null) return;

            int index = _state.Index;
            int paStart = _state.PlateAppearanceStart;
            int maxPrevious = !_showHistory ? 0
                : _state.PlateAppearanceEnded ? MaxPlateAppearanceMarkers - 1
                : MaxPreviousMarkers;
            int first = Mathf.Max(paStart, index - maxPrevious);
            for (int i = first; i <= index; i++)
            {
                var p = _events[i];
                float x = Mathf.Clamp(ToPxX(p.LocationX), 0, w);
                float y = Mathf.Clamp(ToPxY(p.LocationY), 0, h);
                _markers.Add(CreateMarker(p, i - paStart + 1, i == index, x, y));
            }
        }

        static VisualElement CreateMarker(PitchEvent e, int number, bool current, float x, float y)
        {
            var marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.AddToClassList("marker");
            marker.AddToClassList(current ? "marker--current" : "marker--prev");
            marker.AddToClassList(PitchPresentation.ResultClass(e));
            marker.style.left = x;
            marker.style.top = y;

            var shape = new VisualElement { pickingMode = PickingMode.Ignore };
            shape.AddToClassList("marker__shape");
            marker.Add(shape);

            var num = new Label(number.ToString()) { pickingMode = PickingMode.Ignore };
            num.AddToClassList("marker__num");
            marker.Add(num);
            return marker;
        }
    }
}
