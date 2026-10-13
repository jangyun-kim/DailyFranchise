using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// 1920×1080 화면에서 요소가 화면·부모 밖으로 나가거나 텍스트가 잘리는지 검사한다.
    /// ABS 마커는 plot 안으로 clamp되고 overflow:hidden으로 잘리므로 검사에서 제외한다.
    /// </summary>
    public static class LayoutChecker
    {
        const float Tolerance = 0.5f;
        public const float ScreenWidth = 1920f;
        public const float ScreenHeight = 1080f;

        public static List<string> Check(VisualElement screen)
        {
            var issues = new List<string>();
            var r = screen.worldBound;
            if (Mathf.Abs(r.x) > Tolerance || Mathf.Abs(r.y) > Tolerance ||
                Mathf.Abs(r.width - ScreenWidth) > Tolerance || Mathf.Abs(r.height - ScreenHeight) > Tolerance)
                issues.Add($"화면 크기가 1920×1080이 아님: {r}");
            if (screen.Query<ScrollView>().ToList().Count > 0) issues.Add("ScrollView 존재");
            Visit(screen, issues);
            return issues;
        }

        static void Visit(VisualElement el, List<string> issues)
        {
            if (el.resolvedStyle.display == DisplayStyle.None) return;
            if (el.name == "abs-markers") return;

            var r = el.worldBound;
            if (r.xMin < -Tolerance || r.yMin < -Tolerance ||
                r.xMax > ScreenWidth + Tolerance || r.yMax > ScreenHeight + Tolerance)
                issues.Add($"화면 밖: {Describe(el)} {r}");

            if (el.parent != null && el.name != "screen")
            {
                var pr = el.parent.worldBound;
                if (r.xMin < pr.xMin - Tolerance || r.yMin < pr.yMin - Tolerance ||
                    r.xMax > pr.xMax + Tolerance || r.yMax > pr.yMax + Tolerance)
                    issues.Add($"부모 밖: {Describe(el)} {r} ⊄ {pr}");
            }

            if (el is TextElement te && !string.IsNullOrEmpty(te.text))
            {
                var cr = te.contentRect;
                bool nowrap = te.resolvedStyle.whiteSpace == WhiteSpace.NoWrap;
                Vector2 m = nowrap
                    ? te.MeasureTextSize(te.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined)
                    : te.MeasureTextSize(te.text, cr.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                if ((nowrap && m.x > cr.width + Tolerance) || m.y > cr.height + Tolerance)
                    issues.Add($"텍스트 잘림: {Describe(el)} '{te.text.Replace("\n", "\\n")}' 필요 {m} / 영역 {cr.size}");
            }

            foreach (var c in el.Children()) Visit(c, issues);
        }

        static string Describe(VisualElement el)
        {
            var parts = new List<string>();
            for (var e = el; e != null && parts.Count < 4; e = e.parent)
            {
                string cls = e.GetClasses().FirstOrDefault();
                parts.Insert(0, !string.IsNullOrEmpty(e.name) ? "#" + e.name : cls != null ? "." + cls : e.GetType().Name);
            }
            return string.Join(" > ", parts);
        }
    }
}
