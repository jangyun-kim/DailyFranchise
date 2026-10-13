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
        const int MaxPreviousMarkers = 3;

        readonly VisualElement _plot;
        readonly VisualElement _zone;
        readonly VisualElement _markers;
        readonly Label _count;
        readonly Label _result;
        readonly Label _detail;
        readonly Label _matchup;

        IReadOnlyList<PitchEvent> _events;
        int _index;
        int _paStart;

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

        public void Render(IReadOnlyList<PitchEvent> events, int index)
        {
            _events = events;
            _index = index;
            _paStart = ScenarioReadModel.PlateAppearanceStart(events, index);

            var e = events[index];
            _count.text = $"{e.CountBefore} → {e.CountAfter}";

            foreach (var cls in PitchPresentation.AllResultClasses) _result.RemoveFromClassList(cls);
            _result.AddToClassList(PitchPresentation.ResultClass(e));
            var swing = PitchPresentation.SwingNote(e);
            _result.text = swing == null
                ? PitchPresentation.ResultText(e)
                : $"{PitchPresentation.ResultText(e)}  <size=14>{swing}</size>";

            _detail.text = $"{e.PitchType} · {e.VelocityKmh:0} km/h · {PitchPresentation.ZoneNote(e)}";
            _matchup.text = $"{index - _paStart + 1}구째 · 투수 {e.PitcherId} → 타자 {e.BatterId}";

            LayoutPlot();
        }

        void LayoutPlot()
        {
            if (_events == null) return;
            float w = _plot.contentRect.width;
            float h = _plot.contentRect.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0) return;

            float scale = Mathf.Min(w / (2 * ViewHalfWidthCm), h / (ViewTopCm - ViewBottomCm));
            float centerX = w / 2f;
            float centerY = h / 2f;
            float midCm = (ViewTopCm + ViewBottomCm) / 2f;
            float ToPxX(double cm) => centerX + (float)cm * scale;
            float ToPxY(double cm) => centerY - ((float)cm - midCm) * scale;

            var e = _events[_index];
            var zone = e.Zone;
            _zone.style.left = ToPxX(-zone.HalfWidthCm);
            _zone.style.top = ToPxY(zone.TopCm);
            _zone.style.width = (float)(2 * zone.HalfWidthCm) * scale;
            _zone.style.height = (float)(zone.TopCm - zone.BottomCm) * scale;

            _markers.Clear();
            int first = Mathf.Max(_paStart, _index - MaxPreviousMarkers);
            for (int i = first; i <= _index; i++)
            {
                var p = _events[i];
                float x = Mathf.Clamp(ToPxX(p.LocationX), 0, w);
                float y = Mathf.Clamp(ToPxY(p.LocationY), 0, h);
                _markers.Add(CreateMarker(p, i - _paStart + 1, i == _index, x, y));
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
