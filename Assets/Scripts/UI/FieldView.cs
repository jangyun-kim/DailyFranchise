using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 중앙 미니 필드의 루 점유, 타구선, 주자 이동 연출.
    /// PitchEvent의 PlayType·SprayAngleDeg·RunnersBefore/After를 그대로 그리며 판정하지 않는다.
    /// 시간은 Tick(배속 반영 초)으로 진행해 재생기와 같은 시계를 쓴다.
    /// </summary>
    public sealed class FieldView
    {
        /// <summary>x1 기준 타구선이 끝까지 그려지는 시간.</summary>
        public const float LineSeconds = 0.45f;
        /// <summary>x1 기준 주자 이동 시간 (타구선 이후).</summary>
        public const float RunSeconds = 0.9f;
        public const float TotalSeconds = LineSeconds + RunSeconds;

        // USS 토큰(--white, --gold, --muted)과 같은 색.
        static readonly Color HitColor = new Color32(0xF5, 0xF7, 0xFA, 0xFF);
        static readonly Color HomeRunColor = new Color32(0xF2, 0xC2, 0x30, 0xFF);
        static readonly Color OutColor = new Color32(0x87, 0x96, 0xA6, 0xFF);

        readonly VisualElement _ballLayer;
        readonly VisualElement _tokens;
        readonly VisualElement[] _bases;

        bool _lineVisible;
        float _lineProgress;
        Vector2 _ballEnd;
        PlayType _playType;
        Animation _anim;

        sealed class RunnerMove
        {
            public VisualElement Token;
            public List<Vector2> Path;
        }

        sealed class Animation
        {
            public float Elapsed;
            public List<RunnerMove> Moves;
            public BaseState Final;
        }

        public FieldView(VisualElement root)
        {
            var stage = root.Q("diamond-stage");
            _bases = new[] { null, root.Q("base-1"), root.Q("base-2"), root.Q("base-3") };

            var fx = new VisualElement { name = "field-fx", pickingMode = PickingMode.Ignore };
            fx.AddToClassList("field-fx");
            _ballLayer = new VisualElement { name = "batted-ball", pickingMode = PickingMode.Ignore };
            _ballLayer.AddToClassList("field-fx__layer");
            _ballLayer.generateVisualContent += DrawBattedBall;
            _tokens = new VisualElement { name = "runner-tokens", pickingMode = PickingMode.Ignore };
            _tokens.AddToClassList("field-fx__layer");
            fx.Add(_ballLayer);
            fx.Add(_tokens);
            stage.Add(fx);
        }

        /// <summary>
        /// 인플레이 연출(타구선 + 주자 이동)이 끝까지 재생된 순간. 주자가 목적 루(홈 포함)에 닿는 시점과 같다.
        /// 다른 Show/Reset으로 중단된 연출에서는 발생하지 않는다.
        /// </summary>
        public event System.Action AnimationCompleted;

        public bool IsAnimating => _anim != null;
        /// <summary>현재 무대에 있는 주자·타자 토큰 수 (찌꺼기 검사용).</summary>
        public int TokenCount => _tokens.childCount;
        public bool BattedBallVisible => _lineVisible;
        public float LineProgress => _lineProgress;
        public Vector2 BattedBallEnd => _ballEnd;

        /// <summary>주자 토큰의 현재 중심 좌표 (무대 좌표). 테스트·디버그용.</summary>
        public Dictionary<string, Vector2> TokenCenters()
        {
            var d = new Dictionary<string, Vector2>();
            foreach (var t in _tokens.Children())
                d[(string)t.userData] = new Vector2(t.style.left.value.value, t.style.top.value.value);
            return d;
        }

        /// <summary>진행 중인 연출과 토큰·타구선·루 점유 표시를 모두 지운다. 시나리오 전환 시 사용.</summary>
        public void Reset()
        {
            _anim = null;
            _lineVisible = false;
            _lineProgress = 0f;
            PlaceStatic(BaseState.Empty);
        }

        /// <param name="animate">인플레이일 때 타구선·주자 이동을 재생할지. false면 최종 상태를 바로 그린다.</param>
        /// <returns>연출을 시작했으면 true. 이때 끝나면 AnimationCompleted가 발생한다.</returns>
        public bool Show(ScreenState s, bool animate)
        {
            _anim = null;
            var e = s.Pitch;
            bool inPlay = e != null && e.Result == PitchResult.InPlay && e.SprayAngleDeg.HasValue;
            _lineVisible = inPlay;
            if (inPlay)
            {
                _playType = e.PlayType;
                _ballEnd = FieldGeometry.BattedBallEnd(e.SprayAngleDeg.Value, e.PlayType);
            }

            if (!inPlay || !animate)
            {
                _lineProgress = inPlay ? 1f : 0f;
                PlaceStatic(s.Runners);
                return false;
            }

            _lineProgress = 0f;
            _tokens.Clear();
            SetBases(e.RunnersBefore);
            _anim = new Animation { Moves = BuildMoves(e), Final = e.RunnersAfter };
            Apply(0f);
            return true;
        }

        /// <param name="scaledSeconds">배속을 곱한 경과 시간.</param>
        public void Tick(float scaledSeconds)
        {
            if (_anim == null || scaledSeconds <= 0) return;
            _anim.Elapsed += scaledSeconds;
            if (_anim.Elapsed >= TotalSeconds)
            {
                var final = _anim.Final;
                _anim = null;
                _lineProgress = 1f;
                PlaceStatic(final);
                AnimationCompleted?.Invoke();
                return;
            }
            Apply(_anim.Elapsed);
        }

        void Apply(float t)
        {
            _lineProgress = Mathf.Clamp01(t / LineSeconds);
            float run = Mathf.Clamp01((t - LineSeconds) / RunSeconds);
            foreach (var m in _anim.Moves) Place(m.Token, FieldGeometry.PointOnPath(m.Path, run));
            _ballLayer.MarkDirtyRepaint();
        }

        /// <summary>
        /// 주자·타자 이동 경로. 안타면 루상에서 사라진 주자는 홈인.
        /// 아웃 타구면 사라진 주자 중 앞선 주자부터 득점 수만큼 홈인, 나머지는 아웃으로 보고
        /// 다음 루 방향으로 뛰다가(포스 아웃) 연출이 끝날 때 사라진다.
        /// </summary>
        List<RunnerMove> BuildMoves(PitchEvent e)
        {
            var before = Occupants(e.RunnersBefore);
            var after = Occupants(e.RunnersAfter);
            bool hit = PlayTypeRules.IsHit(e.PlayType);
            int runsBudget = e.RunsScored - (e.PlayType == PlayType.HomeRun ? 1 : 0);

            var moves = new List<RunnerMove>();
            foreach (var r in before.OrderByDescending(r => r.Value))
            {
                int to;
                if (after.TryGetValue(r.Key, out int b)) to = b;
                else if (hit || runsBudget > 0) { to = 4; runsBudget--; }
                else to = r.Value < 3 ? r.Value + 1 : r.Value;
                moves.Add(new RunnerMove { Token = AddToken(r.Key, false), Path = FieldGeometry.RunPath(r.Value, to) });
            }

            int batterTo = hit
                ? (e.PlayType == PlayType.HomeRun ? 4 : after.TryGetValue(e.BatterId, out int bb) ? bb : 1)
                : 1; // 아웃 타구: 1루로 뛰다가 아웃
            moves.Add(new RunnerMove { Token = AddToken(e.BatterId, true), Path = FieldGeometry.RunPath(0, batterTo) });
            return moves;
        }

        void PlaceStatic(BaseState bases)
        {
            _tokens.Clear();
            SetBases(bases);
            foreach (var r in Occupants(bases))
                Place(AddToken(r.Key, false), FieldGeometry.BasePoint(r.Value));
            _ballLayer.MarkDirtyRepaint();
        }

        void SetBases(BaseState s)
        {
            _bases[1].EnableInClassList("base--occupied", s.First != null);
            _bases[2].EnableInClassList("base--occupied", s.Second != null);
            _bases[3].EnableInClassList("base--occupied", s.Third != null);
        }

        VisualElement AddToken(string id, bool batter)
        {
            var token = new VisualElement { name = "runner-" + id, userData = id, pickingMode = PickingMode.Ignore };
            token.AddToClassList("runner-token");
            if (batter) token.AddToClassList("runner-token--batter");
            _tokens.Add(token);
            return token;
        }

        static void Place(VisualElement token, Vector2 center)
        {
            // .runner-token은 translate -50%로 중심 정렬된다.
            token.style.left = center.x;
            token.style.top = center.y;
        }

        void DrawBattedBall(MeshGenerationContext mgc)
        {
            if (!_lineVisible || _lineProgress <= 0f) return;
            var color = _playType == PlayType.HomeRun ? HomeRunColor
                : PlayTypeRules.IsHit(_playType) ? HitColor
                : OutColor;
            var end = Vector2.Lerp(FieldGeometry.Home, _ballEnd, _lineProgress);

            var p = mgc.painter2D;
            p.strokeColor = color;
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(FieldGeometry.Home);
            p.LineTo(end);
            p.Stroke();

            p.fillColor = color;
            p.BeginPath();
            p.Arc(end, 5f, 0f, 360f);
            p.Fill();
        }

        static Dictionary<string, int> Occupants(BaseState s)
        {
            var d = new Dictionary<string, int>();
            if (s.First != null) d[s.First] = 1;
            if (s.Second != null) d[s.Second] = 2;
            if (s.Third != null) d[s.Third] = 3;
            return d;
        }
    }
}
