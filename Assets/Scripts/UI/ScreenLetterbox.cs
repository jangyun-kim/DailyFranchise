using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 1920×1080 고정 레이아웃을 어떤 화면 비율에서도 16:9 그대로 보여 준다 (Phase 43, 폴더블 커버·메인 화면).
    /// PanelSettings는 ScaleWithScreenSize + Expand라 패널 논리 크기가 항상 1920×1080 이상이 되고
    /// (배율 = min(가로비, 세로비)), 여기서 UIDocument 루트를 그 가운데 1920×1080으로 고정한다.
    /// 남는 공간은 카메라 배경색(레터박스/필러박스)으로 채워지고, 찌그러짐(Stretch)이나 재배치가 없다.
    /// 화면 크기가 바뀌면(접기·펴기) 패널 GeometryChangedEvent로 다시 맞춘다.
    /// </summary>
    public static class ScreenLetterbox
    {
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        /// <summary>Expand 모드 배율: 기준 해상도가 화면 안에 통째로 들어가는 최대 배율.</summary>
        public static float Scale(Vector2 screenPixels) =>
            Mathf.Min(screenPixels.x / ReferenceWidth, screenPixels.y / ReferenceHeight);

        /// <summary>패널 논리 크기 안에서 1920×1080 루트가 놓일 자리 (가운데 정렬).</summary>
        public static Rect Fit(Vector2 panelSize) => new Rect(
            Mathf.Max(0f, (panelSize.x - ReferenceWidth) / 2f),
            Mathf.Max(0f, (panelSize.y - ReferenceHeight) / 2f),
            ReferenceWidth, ReferenceHeight);

        /// <summary>실제 화면 픽셀에서 게임 화면이 차지하는 영역 (나머지는 레터박스).</summary>
        public static Rect ContentPixels(Vector2 screenPixels)
        {
            float s = Scale(screenPixels);
            var fit = Fit(screenPixels / s);
            return new Rect(fit.x * s, fit.y * s, fit.width * s, fit.height * s);
        }

        /// <summary>루트를 1920×1080 가운데 고정으로 만든다. 여러 번 불러도 콜백은 하나만 걸린다.</summary>
        public static void Apply(VisualElement root)
        {
            if (root == null) return;
            root.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            root.RegisterCallback<AttachToPanelEvent>(OnAttach);
            if (root.panel != null) Hook(root);
        }

        static void OnAttach(AttachToPanelEvent e) => Hook((VisualElement)e.currentTarget);

        static void Hook(VisualElement root)
        {
            var tree = root.panel.visualTree;
            tree.UnregisterCallback<GeometryChangedEvent, VisualElement>(OnTreeGeometry);
            tree.RegisterCallback<GeometryChangedEvent, VisualElement>(OnTreeGeometry, root);
            Layout(root);
        }

        static void OnTreeGeometry(GeometryChangedEvent e, VisualElement root) => Layout(root);

        static void Layout(VisualElement root)
        {
            if (root.panel == null) return;
            var size = root.panel.visualTree.layout.size;
            if (float.IsNaN(size.x) || float.IsNaN(size.y) || size.x <= 0f || size.y <= 0f) return;
            var fit = Fit(size);
            root.style.position = Position.Absolute;
            root.style.left = fit.x;
            root.style.top = fit.y;
            root.style.width = fit.width;
            root.style.height = fit.height;
            root.style.right = StyleKeyword.Auto;
            root.style.bottom = StyleKeyword.Auto;
        }

#if UNITY_ANDROID || UNITY_IOS
        // 모바일 기본 30fps는 터치 드래그가 끊겨 보인다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void MobileFrameRate() => Application.targetFrameRate = 60;
#endif
    }
}
