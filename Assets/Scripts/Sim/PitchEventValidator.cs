using System.Collections.Generic;

namespace DailyFranchise.Sim
{
    /// <summary>
    /// PitchEvent의 result / isStrikeByRule / 위치 / 카운트 / 주자·아웃이 서로 모순되지 않는지 검사한다.
    /// MVP 범위: 도루·폭투·견제 등 투구 사이 주자 이동은 지원하지 않으므로,
    /// 타석을 끝내지 않은 투구에서는 주자·아웃·득점 변화가 없어야 한다.
    /// </summary>
    public static class PitchEventValidator
    {
        public static List<string> Validate(PitchEvent e)
        {
            var errors = new List<string>();

            if (e.Inning < 1) errors.Add($"이닝이 1 미만: {e.Inning}");
            if (e.OutsBefore < 0 || e.OutsBefore > 2) errors.Add($"투구 전 아웃 수가 범위 밖: {e.OutsBefore}");
            if (e.OutsAfter < e.OutsBefore || e.OutsAfter > 3)
                errors.Add($"투구 후 아웃 수가 잘못됨: {e.OutsBefore} → {e.OutsAfter}");
            if (e.RunsScored < 0) errors.Add($"득점이 음수: {e.RunsScored}");
            if (e.VelocityKmh <= 0) errors.Add($"구속이 0 이하: {e.VelocityKmh}");

            bool inZone = StrikeZoneRule.IsStrike(e.Zone, e.LocationX, e.LocationY);
            if (e.IsStrikeByRule != inZone)
                errors.Add($"IsStrikeByRule={e.IsStrikeByRule} 이지만 위치 ({e.LocationX}, {e.LocationY})는 존 {(inZone ? "안" : "밖")}");
            if (e.Result == PitchResult.CalledStrike && !inZone)
                errors.Add("CalledStrike인데 공이 존 밖");
            if (e.Result == PitchResult.Ball && inZone)
                errors.Add("Ball인데 공이 존 안");

            if (!e.CountBefore.IsLive)
            {
                errors.Add($"투구 전 카운트가 잘못됨: {e.CountBefore}");
                return errors;
            }

            var transition = CountRules.Apply(e.CountBefore, e.Result);
            if (e.CountAfter != transition.After)
                errors.Add($"카운트 변화 불일치: {e.CountBefore} + {e.Result} → 기대 {transition.After}, 실제 {e.CountAfter}");
            if (e.Outcome != transition.Outcome)
                errors.Add($"타석 결과 불일치: 기대 {transition.Outcome}, 실제 {e.Outcome}");

            switch (transition.Outcome)
            {
                case PlateAppearanceOutcome.None:
                    if (e.RunnersAfter != e.RunnersBefore || e.OutsAfter != e.OutsBefore || e.RunsScored != 0)
                        errors.Add("타석이 끝나지 않은 투구에서 주자·아웃·득점이 변함");
                    break;

                case PlateAppearanceOutcome.Strikeout:
                    if (e.OutsAfter != e.OutsBefore + 1) errors.Add("삼진인데 아웃이 1 늘지 않음");
                    if (e.RunnersAfter != e.RunnersBefore || e.RunsScored != 0)
                        errors.Add("삼진인데 주자·득점이 변함");
                    break;

                case PlateAppearanceOutcome.Walk:
                {
                    var (bases, runs) = BaseRunning.ForceAdvanceOnWalk(e.RunnersBefore, e.BatterId);
                    if (e.RunnersAfter != bases || e.RunsScored != runs || e.OutsAfter != e.OutsBefore)
                        errors.Add($"볼넷 진루 불일치: 기대 {bases} / {runs}득점, 실제 {e.RunnersAfter} / {e.RunsScored}득점");
                    break;
                }

                case PlateAppearanceOutcome.InPlay:
                {
                    if (string.IsNullOrEmpty(e.PlayResult)) errors.Add("인플레이인데 PlayResult가 없음");
                    // 타자 + 기존 주자는 모두 (루에 남음 | 득점 | 아웃) 중 하나가 되어야 한다.
                    int before = e.RunnersBefore.RunnerCount + 1;
                    int after = e.RunnersAfter.RunnerCount + e.RunsScored + (e.OutsAfter - e.OutsBefore);
                    if (before != after)
                        errors.Add($"인플레이 주자 수 보존 위반: 타자+주자 {before}명, 결과 {after}명");
                    ValidateBattedBall(e, errors);
                    break;
                }
            }

            if (transition.Outcome != PlateAppearanceOutcome.InPlay)
            {
                if (e.PlayResult != null) errors.Add("인플레이가 아닌데 PlayResult가 있음");
                if (e.PlayType != PlayType.None) errors.Add($"인플레이가 아닌데 PlayType이 {e.PlayType}");
                if (e.SprayAngleDeg != null) errors.Add("인플레이가 아닌데 SprayAngleDeg가 있음");
            }

            return errors;
        }

        /// <summary>
        /// 타구 종류·방향과 주자·타자 이동이 맞는지 검사한다.
        /// 위치 표기: 타석 0, 1~3루, 홈인 4. 주자는 뒤로 갈 수 없고 앞 주자를 추월할 수 없다.
        /// MVP 범위: 안타 중 주루사(아웃)와 야수선택·실책 출루는 지원하지 않는다.
        /// </summary>
        static void ValidateBattedBall(PitchEvent e, List<string> errors)
        {
            if (e.PlayType == PlayType.None)
            {
                errors.Add("인플레이인데 PlayType이 None");
                return;
            }

            bool hit = PlayTypeRules.IsHit(e.PlayType);
            if (e.SprayAngleDeg == null)
            {
                errors.Add("인플레이인데 SprayAngleDeg가 없음");
            }
            else
            {
                double limit = hit ? PlayTypeRules.FairAngleLimitDeg : PlayTypeRules.MaxAngleDeg;
                if (System.Math.Abs(e.SprayAngleDeg.Value) > limit)
                    errors.Add($"타구 각도 {e.SprayAngleDeg}°가 {e.PlayType} 허용 범위(±{limit}°) 밖");
            }

            var before = Occupants(e.RunnersBefore);
            var after = Occupants(e.RunnersAfter);
            if (before.ContainsKey(e.BatterId)) errors.Add("타자가 이미 루상에 있음");
            foreach (var id in after.Keys)
                if (id != e.BatterId && !before.ContainsKey(id))
                    errors.Add($"투구 전에 없던 주자 {id}가 루상에 있음");

            int outsMade = e.OutsAfter - e.OutsBefore;
            int batterAt = after.TryGetValue(e.BatterId, out var b) ? b : 0;

            // (출발 위치, 도착 위치). 루상에서 사라진 주자는 안타면 홈인(4), 아웃 타구면 판별 불가라 순서 검사에서 제외.
            var moves = new List<(int from, int to)>();

            if (hit)
            {
                int bases = PlayTypeRules.BasesForHit(e.PlayType);
                if (outsMade != 0) errors.Add("안타인데 아웃이 발생 (안타 중 주루사는 미지원)");
                if (bases == 4)
                {
                    if (batterAt != 0) errors.Add("홈런 타자가 루상에 남아 있음");
                }
                else if (batterAt != bases)
                {
                    errors.Add($"{e.PlayType} 타자는 {bases}루에 있어야 하나 실제 {(batterAt == 0 ? "루상에 없음" : batterAt + "루")}");
                }

                int scored = bases == 4 ? 1 : 0;
                foreach (var r in before)
                {
                    bool home = !after.TryGetValue(r.Key, out int to);
                    if (home) scored++;
                    moves.Add((r.Value, home ? 4 : to));
                }
                moves.Add((0, bases == 4 ? 4 : batterAt));
                if (e.RunsScored != scored)
                    errors.Add($"안타 득점 불일치: 홈인 {scored}명, RunsScored {e.RunsScored}");
            }
            else
            {
                if (outsMade < 1) errors.Add($"{e.PlayType}인데 아웃이 늘지 않음");
                if (batterAt != 0) errors.Add($"{e.PlayType}인데 타자가 루상에 있음");
                if (e.OutsAfter == 3 && e.RunsScored != 0) errors.Add("3아웃이 된 타구에서는 득점이 인정되지 않음");
                foreach (var r in before)
                    if (after.TryGetValue(r.Key, out int to))
                        moves.Add((r.Value, to));
            }

            foreach (var m in moves)
                if (m.to < m.from) errors.Add($"주자 역주행: {m.from}루 → {m.to}루");
            for (int i = 0; i < moves.Count; i++)
            for (int j = 0; j < moves.Count; j++)
            {
                var x = moves[i];
                var y = moves[j];
                if (x.from < y.from && !(x.to < y.to || (x.to == 4 && y.to == 4)))
                    errors.Add($"주자 추월: {x.from}→{x.to} 주자가 {y.from}→{y.to} 주자보다 앞서거나 같음");
            }
        }

        static Dictionary<string, int> Occupants(BaseState s)
        {
            var d = new Dictionary<string, int>();
            if (s.First != null) d[s.First] = 1;
            if (s.Second != null) d[s.Second] = 2;
            if (s.Third != null) d[s.Third] = 3;
            return d;
        }

        /// <summary>단일 이벤트 검사 + 연속된 이벤트 사이의 상태 연결을 검사한다.</summary>
        public static List<string> ValidateSequence(IReadOnlyList<PitchEvent> events)
        {
            var errors = new List<string>();
            for (int i = 0; i < events.Count; i++)
            {
                foreach (var err in Validate(events[i]))
                    errors.Add($"[{i + 1}구] {err}");

                if (i == 0) continue;
                var prev = events[i - 1];
                var cur = events[i];
                string tag = $"[{i}→{i + 1}구]";

                if (prev.OutsAfter == 3)
                {
                    bool nextHalf = prev.HalfInning == HalfInning.Top
                        ? cur.Inning == prev.Inning && cur.HalfInning == HalfInning.Bottom
                        : cur.Inning == prev.Inning + 1 && cur.HalfInning == HalfInning.Top;
                    if (!nextHalf) errors.Add($"{tag} 3아웃 후 다음 반 이닝으로 넘어가지 않음");
                    if (cur.OutsBefore != 0 || cur.RunnersBefore != BaseState.Empty || cur.CountBefore != Count.Zero)
                        errors.Add($"{tag} 새 반 이닝은 0아웃·주자 없음·0-0으로 시작해야 함");
                    continue;
                }

                if (cur.Inning != prev.Inning || cur.HalfInning != prev.HalfInning)
                    errors.Add($"{tag} 3아웃 전에 이닝이 바뀜");
                if (cur.OutsBefore != prev.OutsAfter)
                    errors.Add($"{tag} 아웃 연결 불일치: {prev.OutsAfter} → {cur.OutsBefore}");
                if (cur.RunnersBefore != prev.RunnersAfter)
                    errors.Add($"{tag} 주자 연결 불일치: {prev.RunnersAfter} → {cur.RunnersBefore}");

                if (prev.Outcome == PlateAppearanceOutcome.None)
                {
                    if (cur.BatterId != prev.BatterId) errors.Add($"{tag} 타석 도중 타자가 바뀜");
                    if (cur.CountBefore != prev.CountAfter)
                        errors.Add($"{tag} 카운트 연결 불일치: {prev.CountAfter} → {cur.CountBefore}");
                }
                else if (cur.CountBefore != Count.Zero)
                {
                    errors.Add($"{tag} 새 타석이 0-0으로 시작하지 않음: {cur.CountBefore}");
                }
            }
            return errors;
        }
    }
}
