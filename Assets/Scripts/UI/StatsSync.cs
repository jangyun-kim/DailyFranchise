namespace DailyFranchise.UI
{
    /// <summary>
    /// 기록(점수·H·아웃·주자·타자 오늘 기록)을 화면에 언제 반영할지 정한다.
    /// 인플레이 연출이 도는 동안에는 직전 투구까지의 기록을 보여주고, 연출이 끝나면 현재 투구까지 반영한다.
    /// 시간이나 엔진에 의존하지 않으므로 EditMode에서 그대로 검증할 수 있다.
    /// </summary>
    public sealed class StatsSync
    {
        int _target = ScreenState.BeforeFirstPitch;

        /// <summary>기록 표시에 쓸 재생 위치. 표시 대기 중이면 Target - 1.</summary>
        public int StatsIndex { get; private set; } = ScreenState.BeforeFirstPitch;

        /// <summary>연출 완료를 기다리는 기록이 있는지.</summary>
        public bool IsPending => StatsIndex != _target;

        /// <param name="animating">이 투구의 필드 연출이 시작되었는지 (FieldView.Show의 반환값).</param>
        public void OnIndexChanged(int index, bool animating)
        {
            _target = index;
            StatsIndex = animating ? index - 1 : index;
        }

        /// <returns>기록이 새로 반영되었으면 true (화면을 다시 그려야 함).</returns>
        public bool OnAnimationCompleted()
        {
            if (!IsPending) return false;
            StatsIndex = _target;
            return true;
        }

        public void Reset()
        {
            _target = ScreenState.BeforeFirstPitch;
            StatsIndex = ScreenState.BeforeFirstPitch;
        }
    }
}
