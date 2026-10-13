using System;
using System.Collections.Generic;
using DailyFranchise.Sim;

namespace DailyFranchise.UI
{
    /// <summary>
    /// 고정 이벤트 목록의 재생 위치와 시간 진행. 엔진 없이 Tick(초)으로 구동해 EditMode에서 검증할 수 있다.
    /// 이벤트를 만들거나 바꾸지 않고, 몇 번째 투구까지 보여줄지만 정한다.
    /// 작전 분기점이 있으면 그 투구에서 멈추고(DecisionReached), ContinueWith로 후속 구간을 이어 받는다.
    /// </summary>
    public sealed class PitchPlayback
    {
        /// <summary>x1 기준 투구 간격 (초).</summary>
        public const float PitchIntervalSec = 2.0f;
        /// <summary>x1 기준 타석 종료 후 추가로 머무는 시간 (초). 타석 투구 분포를 보여주는 구간.</summary>
        public const float PlateAppearanceHoldSec = 1.5f;

        public static readonly int[] SupportedSpeeds = { 1, 2, 4 };

        IReadOnlyList<PitchEvent> _events;
        float _elapsed;
        int? _decisionIndex;

        public PitchPlayback(IReadOnlyList<PitchEvent> events)
        {
            if (events == null || events.Count == 0) throw new ArgumentException("재생할 이벤트가 없다.", nameof(events));
            _events = events;
        }

        /// <summary>현재 보여주는 투구 인덱스. -1은 첫 투구 전.</summary>
        public int Index { get; private set; } = ScreenState.BeforeFirstPitch;
        public int Count => _events.Count;
        public bool IsPlaying { get; private set; }
        public int Speed { get; private set; } = 1;
        public bool IsAtEnd => Index >= _events.Count - 1;
        public IReadOnlyList<PitchEvent> Events => _events;

        /// <summary>작전 분기점 투구 인덱스. 없거나 이미 작전을 골랐으면 null.</summary>
        public int? DecisionIndex => _decisionIndex;
        /// <summary>분기점에 도달해 작전 선택을 기다리는 중. 이때는 재생·앞으로 이동이 막힌다.</summary>
        public bool AwaitingDecision { get; private set; }

        /// <summary>분기점 투구를 보여준 직후 (재생은 이미 멈춘 상태).</summary>
        public event Action<int> DecisionReached;

        /// <summary>표시 위치가 바뀌었을 때 (재생·건너뛰기·처음으로).</summary>
        public event Action<int> IndexChanged;
        /// <summary>재생/정지 또는 배속이 바뀌었을 때.</summary>
        public event Action StateChanged;

        /// <summary>현재 위치에서 다음 투구까지 기다리는 시간 (배속 반영).</summary>
        public float CurrentDelay
        {
            get
            {
                float d = PitchIntervalSec;
                if (Index >= 0 && _events[Index].Outcome != PlateAppearanceOutcome.None) d += PlateAppearanceHoldSec;
                return d / Speed;
            }
        }

        /// <summary>afterIndex 투구를 보여준 뒤 멈추고 작전을 기다리게 한다.</summary>
        public void SetDecisionPoint(int afterIndex)
        {
            if (afterIndex < 0 || afterIndex >= _events.Count)
                throw new ArgumentOutOfRangeException(nameof(afterIndex), afterIndex, "분기점은 현재 이벤트 범위 안이어야 한다.");
            _decisionIndex = afterIndex;
        }

        /// <summary>
        /// 분기점에서 고른 작전의 전체 경로로 이어 간다. 이미 재생한 구간(현재 이벤트 전부)이
        /// 같은 객체로 앞에 있어야 한다. 재생 위치는 그대로 두고 분기점을 해제한다.
        /// </summary>
        public void ContinueWith(IReadOnlyList<PitchEvent> path)
        {
            if (!AwaitingDecision) throw new InvalidOperationException("작전 선택 대기 중이 아니다.");
            if (path == null || path.Count <= _events.Count)
                throw new ArgumentException("후속 구간이 없는 경로다.", nameof(path));
            for (int i = 0; i < _events.Count; i++)
                if (!ReferenceEquals(path[i], _events[i]))
                    throw new ArgumentException($"이음새 불일치: {i}번 투구가 이미 재생한 이벤트와 다르다.", nameof(path));

            _events = path;
            _decisionIndex = null;
            AwaitingDecision = false;
            _elapsed = 0;
            StateChanged?.Invoke();
        }

        public void Play()
        {
            if (IsAtEnd || IsPlaying || AwaitingDecision) return;
            IsPlaying = true;
            StateChanged?.Invoke();
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            StateChanged?.Invoke();
        }

        public void TogglePlay()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        public void SetSpeed(int speed)
        {
            if (Array.IndexOf(SupportedSpeeds, speed) < 0)
                throw new ArgumentOutOfRangeException(nameof(speed), speed, "지원 배속: 1, 2, 4");
            if (Speed == speed) return;
            Speed = speed;
            StateChanged?.Invoke();
        }

        public void Tick(float deltaSec)
        {
            if (!IsPlaying || deltaSec <= 0) return;
            _elapsed += deltaSec;
            while (IsPlaying && _elapsed >= CurrentDelay)
            {
                _elapsed -= CurrentDelay;
                MoveTo(Index + 1, keepElapsed: true);
            }
        }

        public void StepPitch() => SeekTo(Index + 1);

        /// <summary>현재 타석(또는 다음 타석)이 끝나는 투구로 이동.</summary>
        public void SkipToPlateAppearanceEnd()
        {
            int i = Index + 1;
            while (i < _events.Count - 1 && _events[i].Outcome == PlateAppearanceOutcome.None) i++;
            SeekTo(i);
        }

        /// <summary>현재(또는 다음) 반 이닝의 마지막 투구로 이동.</summary>
        public void SkipToHalfInningEnd()
        {
            int i = Index + 1;
            if (i >= _events.Count) return;
            var start = _events[i];
            while (i < _events.Count - 1 && _events[i].OutsAfter < 3
                   && _events[i + 1].Inning == start.Inning && _events[i + 1].HalfInning == start.HalfInning)
                i++;
            SeekTo(i);
        }

        public void SkipToEnd() => SeekTo(_events.Count - 1);

        public void Restart()
        {
            bool changed = IsPlaying || AwaitingDecision;
            IsPlaying = false;
            MoveTo(ScreenState.BeforeFirstPitch, keepElapsed: false);
            if (changed) StateChanged?.Invoke();
        }

        public void SeekTo(int index) => MoveTo(index, keepElapsed: false);

        void MoveTo(int index, bool keepElapsed)
        {
            // 작전을 고르기 전에는 분기점 너머로 갈 수 없다.
            int max = _events.Count - 1;
            if (_decisionIndex.HasValue) max = Math.Min(max, _decisionIndex.Value);
            int clamped = Math.Max(ScreenState.BeforeFirstPitch, Math.Min(index, max));
            if (!keepElapsed) _elapsed = 0;
            if (clamped == Index) return;
            Index = clamped;
            if (AwaitingDecision && Index != _decisionIndex) AwaitingDecision = false;
            IndexChanged?.Invoke(Index);

            if (_decisionIndex == Index)
            {
                AwaitingDecision = true;
                IsPlaying = false;
                _elapsed = 0;
                DecisionReached?.Invoke(Index);
                StateChanged?.Invoke();
            }
            else if (IsAtEnd && IsPlaying)
            {
                IsPlaying = false;
                _elapsed = 0;
                StateChanged?.Invoke();
            }
        }
    }
}
