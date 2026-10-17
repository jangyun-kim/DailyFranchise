using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 런타임 툴팁 (Phase 43). UI Toolkit 런타임 패널은 VisualElement.tooltip을 그리지 않으므로 직접 띄운다.
    /// - 마우스: tooltip이 있는 요소 위에 HoverDelayMs 머물면 표시, 벗어나면 숨김.
    /// - 터치·펜: LongPressMs 길게 누르면 표시. 길게 누른 뒤 손을 떼도 클릭은 일어나지 않고,
    ///   말풍선은 다음 터치까지 남는다. 짧게 탭하면 원래 클릭 그대로.
    /// tooltip이 없어도 HoldClass가 붙은 요소는 길게 누르기만 인정한다(클릭 억제, 말풍선 없음) —
    /// 작전 버튼처럼 누르고 있는 동안 PointerEnter 미리보기를 보고 떼도 실행되지 않게 할 때 쓴다.
    /// </summary>
    public sealed class TouchTooltip
    {
        public const string BubbleName = "touch-tooltip";
        public const string HoldClass = "touch-hold";
        public const long LongPressMs = 450;
        public const long HoverDelayMs = 500;
        /// <summary>이만큼(논리 px) 움직이면 길게 누르기가 아니라 드래그로 본다.</summary>
        public const float MoveTolerance = 12f;
        const float Margin = 8f;

        readonly VisualElement _root;
        readonly Label _bubble;
        VisualElement _shownFor;

        VisualElement _pressOwner;
        int _pressId = -1;
        Vector2 _pressStart;
        IVisualElementScheduledItem _pressTimer;
        bool _suppressClick;

        VisualElement _hoverOwner;
        IVisualElementScheduledItem _hoverTimer;

        /// <summary>루트에 한 번만 설치한다. 이미 있으면 그 인스턴스를 돌려준다.</summary>
        public static TouchTooltip Install(VisualElement root)
        {
            var existing = root.Q<Label>(BubbleName);
            if (existing?.userData is TouchTooltip t) return t;
            return new TouchTooltip(root);
        }

        TouchTooltip(VisualElement root)
        {
            _root = root;
            _bubble = new Label { name = BubbleName, pickingMode = PickingMode.Ignore, userData = this };
            var s = _bubble.style;
            s.position = Position.Absolute;
            s.display = DisplayStyle.None;
            s.maxWidth = 560;
            s.whiteSpace = WhiteSpace.Normal;
            s.fontSize = 20;
            s.color = new Color(0.93f, 0.95f, 0.97f);
            s.backgroundColor = new Color(0.08f, 0.10f, 0.13f, 0.96f);
            s.paddingLeft = s.paddingRight = 14;
            s.paddingTop = s.paddingBottom = 10;
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 8;
            s.borderLeftWidth = s.borderRightWidth = s.borderTopWidth = s.borderBottomWidth = 1;
            s.borderLeftColor = s.borderRightColor = s.borderTopColor = s.borderBottomColor = new Color(0.35f, 0.42f, 0.50f);
            root.Add(_bubble);
            _bubble.RegisterCallback<GeometryChangedEvent>(_ => Place());

            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCancelEvent>(_ => CancelPress(), TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerOverEvent>(OnPointerOver, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(e => { if (e.target == _root) EndHover(); });
        }

        /// <summary>말풍선이 떠 있는지 / 내용 / 어느 요소의 것인지 (테스트·진단용).</summary>
        public bool IsShowing => _bubble.resolvedStyle.display != DisplayStyle.None && _shownFor != null;
        public string Text => _bubble.text;
        public VisualElement ShownFor => _shownFor;
        public Label Bubble => _bubble;

        /// <summary>요소부터 루트까지 올라가며 툴팁(또는 길게 누르기)을 가진 첫 요소.</summary>
        public VisualElement OwnerOf(VisualElement e)
        {
            for (; e != null && e != _root; e = e.parent)
                if (e != _bubble && (!string.IsNullOrEmpty(e.tooltip) || e.ClassListContains(HoldClass))) return e;
            return null;
        }

        public void Show(VisualElement owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.tooltip)) { Hide(); return; }
            _shownFor = owner;
            _bubble.text = owner.tooltip;
            _bubble.style.display = DisplayStyle.Flex;
            _bubble.BringToFront();
            Place();
        }

        public void Hide()
        {
            _shownFor = null;
            _bubble.style.display = DisplayStyle.None;
        }

        void Place()
        {
            if (_shownFor == null || _shownFor.panel == null) return;
            var owner = _root.WorldToLocal(_shownFor.worldBound);
            var size = _bubble.layout.size;
            if (float.IsNaN(size.x)) size = Vector2.zero;
            float w = _root.layout.width, h = _root.layout.height;
            float x = Mathf.Clamp(owner.center.x - size.x / 2f, Margin, Mathf.Max(Margin, w - size.x - Margin));
            float y = owner.yMin - size.y - Margin;
            if (y < Margin) y = owner.yMax + Margin;
            y = Mathf.Clamp(y, Margin, Mathf.Max(Margin, h - size.y - Margin));
            _bubble.style.left = x;
            _bubble.style.top = y;
        }

        static bool IsMouse(IPointerEvent e) => e.pointerType == UnityEngine.UIElements.PointerType.mouse;

        void OnPointerDown(PointerDownEvent e)
        {
            Hide();
            CancelPress();
            EndHover();
            _suppressClick = false;
            if (IsMouse(e)) return;
            var owner = OwnerOf(e.target as VisualElement);
            if (owner == null) return;
            _pressOwner = owner;
            _pressId = e.pointerId;
            _pressStart = e.position;
            _pressTimer = _root.schedule.Execute(OnLongPress).StartingIn(LongPressMs);
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (_pressTimer == null || e.pointerId != _pressId) return;
            if (((Vector2)e.position - _pressStart).magnitude > MoveTolerance) CancelPress();
        }

        /// <summary>길게 누르기 판정 (테스트에서 타이머를 기다리지 않고 부를 수 있다).</summary>
        public void OnLongPress()
        {
            var owner = _pressOwner;
            _pressTimer?.Pause();
            _pressTimer = null;
            if (owner == null) return;
            _suppressClick = true;
            Show(owner);
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (_suppressClick && e.pointerId == _pressId)
            {
                // 버튼(Clickable)이 손 뗌을 클릭으로 받지 않게 막고, 잡고 있던 포인터는 취소로 풀어 준다.
                e.StopImmediatePropagation();
                var captured = _root.panel?.GetCapturingElement(e.pointerId) as VisualElement;
                if (captured != null)
                    using (var cancel = PointerCancelEvent.GetPooled(e))
                    {
                        cancel.target = captured;
                        captured.SendEvent(cancel);
                    }
            }
            _pressTimer?.Pause();
            _pressTimer = null;
            _pressOwner = null;
        }

        void OnClick(ClickEvent e)
        {
            if (!_suppressClick) return;
            _suppressClick = false;
            e.StopImmediatePropagation();
        }

        void CancelPress()
        {
            _pressTimer?.Pause();
            _pressTimer = null;
            _pressOwner = null;
        }

        void OnPointerOver(PointerOverEvent e)
        {
            if (!IsMouse(e)) return;
            var owner = OwnerOf(e.target as VisualElement);
            if (owner == _hoverOwner) return;
            EndHover();
            if (owner == null || string.IsNullOrEmpty(owner.tooltip)) return;
            _hoverOwner = owner;
            _hoverTimer = _root.schedule.Execute(() => { if (_hoverOwner == owner) Show(owner); }).StartingIn(HoverDelayMs);
        }

        void EndHover()
        {
            _hoverTimer?.Pause();
            _hoverTimer = null;
            if (_hoverOwner != null && _shownFor == _hoverOwner) Hide();
            _hoverOwner = null;
        }
    }
}
