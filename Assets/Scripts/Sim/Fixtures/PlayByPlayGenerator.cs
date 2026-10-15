using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Fixtures
{
    /// <summary>다음 타자의 작전 전 구간(초구 스트라이크 → 볼, 1-1)과 그 타석의 작전 분기점.</summary>
    public sealed class PlateAppearanceSegment
    {
        public string BatterId;
        /// <summary>작전 전 투구 2개. 이미 재생한 경로 뒤에 이어 붙인다.</summary>
        public IReadOnlyList<PitchEvent> Events;
        /// <summary>AfterIndex는 이어 붙인 전체 경로 기준 인덱스 (이 구간 마지막 투구).</summary>
        public BranchPoint Branch;
    }

    /// <summary>
    /// 인게임 Play-by-Play 순환: 승부처 타석이 끝나면 <see cref="PlayResultResolver"/>로 갱신된 아웃·주자 상태에서
    /// 다음 타순 타자(<see cref="BattingOrder"/>)의 타석을 만들고, 그 타석도 1-1에서 작전을 기다린다. 3아웃이면 멈춘다.
    ///
    /// 타석 결과 (작전 후속 구간, 모두 <see cref="PitchEventValidator"/> 규칙을 지킨다):
    /// - 기본 진행: 출루 확률 = <see cref="MatchupCalculator.HitProbability(string, string, MatchupSituation)"/>
    ///   ([<see cref="MinOnBaseRate"/>, <see cref="MaxOnBaseRate"/>]로 자름). 출루면 볼넷(<see cref="WalkShare"/>) 또는 안타,
    ///   아니면 삼진·땅볼·병살·뜬공·희생플라이·직선타·내야 뜬공 중 하나 (상황에 안 맞는 결과는 Resolver가 바꾼다).
    /// - 강공: 성공 = 장타 위주 안타, 실패 = 병살(가능하면)·뜬공·삼진.
    /// - 안타 종류(단타·2루타·3루타·홈런)는 <see cref="HitDistributor"/>가 타자 장타율·홈런 수와 투수 피홈런으로 기울인
    ///   가중치로 뽑는다. 두 선수 모두 데이터가 없으면 이전 고정 분포와 같다.
    /// - 스퀴즈(3루 주자·2아웃 전만): 성공 = 3루 주자 득점·주자 한 루씩·타자 아웃, 실패 = 번트 뜬공 더블 아웃.
    /// 난수는 작전을 고른 순간(후속 구간을 만들 때)에만 소비한다. 다음 타석의 작전 전 구간은 난수를 쓰지 않는다.
    /// 난수 소비 수는 분배 방식과 무관하다: 기본 진행 = 출루 판정 1 + 결과 1, 강공 = 결과 1 (성공·실패 판정은 BranchPoint).
    ///
    /// 무한 순환 방지: 3아웃이면 즉시 멈추고, 한 반 이닝 타석 수가 <see cref="MaxPlateAppearancesPerHalf"/>에 닿아도 멈춘다.
    ///
    /// Phase 19 수비 이닝·투수 교체:
    /// - <see cref="StartHalf"/>: 새 반 이닝 첫 타석 (0아웃·주자 없음). 내 구단 수비(userBatting = false)면 작전은 기본 진행만.
    /// - <see cref="Next"/>의 pitcherId: 다음 타석을 던질 투수 (교체 반영). null이면 직전 투구의 투수.
    /// - <see cref="Relieve"/>: 1-1 작전 대기 중 투수를 바꾸면 남은 투구(결정구)를 새 투수로 다시 만든다. 이미 던진 2구는 그대로.
    /// - 경기 끝 규칙: <see cref="FinalInning"/>회 이후 말 공격은 홈이 앞서면 하지 않고, 하다가 홈이 앞서면 끝내기로 멈춘다.
    /// </summary>
    public sealed class PlayByPlayGenerator
    {
        public const int MaxPlateAppearancesPerHalf = 15;
        public const double MinOnBaseRate = 0.05;
        /// <summary>기본 진행 출루 확률 상한. 기록이 비정상이어도 아웃이 나와 이닝이 끝나도록.</summary>
        public const double MaxOnBaseRate = 0.60;
        /// <summary>작전 전 구간 길이 (초구 + 2구, 1-1).</summary>
        public const int PitchesBeforeDecision = 2;
        public const double DefaultBatterHeightCm = 180;
        /// <summary>정규 이닝 마지막 회.</summary>
        public const int FinalInning = 9;

        /// <summary>기본 진행 출루 중 볼넷 몫. 나머지는 안타이며 종류는 <see cref="HitDistributor"/>가 나눈다.</summary>
        public const double WalkShare = 0.20;

        // 아웃 결과 분포 (기본 진행)
        static readonly (PlayOutcome outcome, double weight)[] Outs =
        {
            (PlayOutcome.Strikeout, 0.25), (PlayOutcome.GroundOut, 0.20), (PlayOutcome.DoublePlay, 0.15),
            (PlayOutcome.FlyOut, 0.15), (PlayOutcome.SacrificeFly, 0.10), (PlayOutcome.LineOut, 0.08),
            (PlayOutcome.PopOut, 0.07),
        };

        // 강공 실패
        static readonly (PlayOutcome outcome, double weight)[] SwingOuts =
        {
            (PlayOutcome.DoublePlay, 0.45), (PlayOutcome.FlyOut, 0.25), (PlayOutcome.Strikeout, 0.30),
        };

        // 타석마다 돌려 쓰는 구종 (작전 전 2구 + 결정구)
        static readonly (string type, double kmh)[] Pitches =
        {
            ("포심", 147), ("슬라이더", 134), ("체인지업", 129), ("커브", 121), ("포크", 132), ("투심", 144),
        };

        readonly MatchupCalculator _calculator;
        readonly IRandomSource _random;

        /// <param name="calculator">기본 진행 출루 확률 계산. null이면 리그 평균 대결.</param>
        /// <param name="random">작전 후속 구간(타석 결과) 난수.</param>
        public PlayByPlayGenerator(MatchupCalculator calculator, IRandomSource random)
        {
            _calculator = calculator ?? new MatchupCalculator(null);
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// 경로를 fromIndex 이후 처음 끝나는 타석까지 자른다 (승부처 장면의 각본 후속 타자를 떼어낸다).
        /// 끝나는 타석이 없으면 전체를 그대로 돌려준다.
        /// </summary>
        public static List<PitchEvent> UntilPlateAppearanceEnd(IReadOnlyList<PitchEvent> path, int fromIndex)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            for (int i = Math.Max(0, fromIndex); i < path.Count; i++)
                if (path[i].Outcome != PlateAppearanceOutcome.None)
                    return path.Take(i + 1).ToList();
            return path.ToList();
        }

        /// <summary>마지막 투구가 속한 반 이닝에서 끝난 타석 수.</summary>
        public static int PlateAppearancesInHalf(IReadOnlyList<PitchEvent> played)
        {
            if (played == null || played.Count == 0) return 0;
            var last = played[played.Count - 1];
            return played.Count(e => e.Inning == last.Inning && e.HalfInning == last.HalfInning
                                     && e.Outcome != PlateAppearanceOutcome.None);
        }

        /// <summary>
        /// 다음 타자가 타석에 설 수 있는지: 마지막 투구로 타석이 끝났고, 3아웃 전이며, 타석 수 한도 안이고, 다음 타자가 루상에 없다.
        /// </summary>
        public static bool HalfInningContinues(IReadOnlyList<PitchEvent> played)
        {
            if (played == null || played.Count == 0) return false;
            var last = played[played.Count - 1];
            if (last.Outcome == PlateAppearanceOutcome.None || last.OutsAfter >= 3) return false;
            if (PlateAppearancesInHalf(played) >= MaxPlateAppearancesPerHalf) return false;
            if (BattingOrder.OrderOf(last.BatterId) == null) return false;
            string next = BattingOrder.Next(last.BatterId);
            var r = last.RunnersAfter;
            return next != r.First && next != r.Second && next != r.Third;
        }

        /// <summary>다음 타자의 작전 전 구간과 분기점. 반 이닝이 끝났으면(<see cref="HalfInningContinues"/>가 거짓) null.</summary>
        /// <param name="pitcherId">다음 타석을 던질 투수. null이면 직전 투구의 투수 그대로.</param>
        /// <param name="userBatting">내 구단 공격이면 true (강공·스퀴즈 제공), 수비면 기본 진행만.</param>
        public PlateAppearanceSegment Next(IReadOnlyList<PitchEvent> played, string pitcherId = null, bool userBatting = true)
        {
            if (!HalfInningContinues(played)) return null;
            var last = played[played.Count - 1];
            var pa = new AtBat(last.Inning, last.HalfInning, pitcherId ?? last.PitcherId, BattingOrder.Next(last.BatterId),
                last.OutsAfter, last.RunnersAfter, PlateAppearancesInHalf(played));
            return Segment(pa, played.Count, userBatting);
        }

        /// <summary>새 반 이닝의 첫 타석 (0아웃·주자 없음). 구간은 played 뒤에 이어 붙인다.</summary>
        public PlateAppearanceSegment StartHalf(IReadOnlyList<PitchEvent> played, int inning, HalfInning half,
            string batterId, string pitcherId, bool userBatting)
        {
            if (played == null) throw new ArgumentNullException(nameof(played));
            if (BattingOrder.OrderOf(batterId) == null) throw new ArgumentException($"타순 자리 ID가 아니다: {batterId}", nameof(batterId));
            if (pitcherId == null) throw new ArgumentNullException(nameof(pitcherId));
            var pa = new AtBat(inning, half, pitcherId, batterId, 0, BaseState.Empty, 0);
            return Segment(pa, played.Count, userBatting);
        }

        /// <summary>
        /// 1-1 작전 대기 중 투수 교체: 마지막 투구가 이 타석의 작전 전 구간(1-1)이어야 한다. 남은 투구는 새 투수가 던지는
        /// 분기점(AfterIndex = 마지막 투구)을 새로 만든다. 난수는 쓰지 않는다 (작전을 고를 때 쓴다).
        /// </summary>
        public BranchPoint Relieve(IReadOnlyList<PitchEvent> played, string newPitcherId, bool userBatting)
        {
            if (played == null || played.Count < PitchesBeforeDecision) throw new ArgumentException("작전 전 구간이 없다.", nameof(played));
            if (newPitcherId == null) throw new ArgumentNullException(nameof(newPitcherId));
            var last = played[played.Count - 1];
            if (last.Outcome != PlateAppearanceOutcome.None || last.CountAfter.Balls != 1 || last.CountAfter.Strikes != 1)
                throw new ArgumentException("1-1 작전 대기 중에만 투수를 바꿀 수 있다.", nameof(played));
            var pa = new AtBat(last.Inning, last.HalfInning, newPitcherId, last.BatterId,
                last.OutsAfter, last.RunnersAfter, PlateAppearancesInHalf(played));
            return new BranchPoint { AfterIndex = played.Count - 1, Options = OptionsFor(pa, userBatting) };
        }

        /// <summary>다음 반 이닝 (N회초 → N회말, N회말 → N+1회초).</summary>
        public static (int inning, HalfInning half) NextHalf(int inning, HalfInning half) =>
            half == HalfInning.Top ? (inning, HalfInning.Bottom) : (inning + 1, HalfInning.Top);

        /// <summary><see cref="FinalInning"/>회 이후 말 공격에서 홈이 앞선다 (말 공격 생략 또는 끝내기).</summary>
        public static bool HomeWinsInBottom(int inning, HalfInning half, int awayScore, int homeScore) =>
            inning >= FinalInning && half == HalfInning.Bottom && homeScore > awayScore;

        PlateAppearanceSegment Segment(AtBat pa, int startIndex, bool userBatting)
        {
            var events = new List<PitchEvent>
            {
                pa.Pitch(0, PitchResult.CalledStrike, Count.Zero),
                pa.Pitch(1, PitchResult.Ball, new Count(0, 1)),
            };
            return new PlateAppearanceSegment
            {
                BatterId = pa.BatterId,
                Events = events,
                Branch = new BranchPoint { AfterIndex = startIndex + PitchesBeforeDecision - 1, Options = OptionsFor(pa, userBatting) },
            };
        }

        Dictionary<TacticKind, TacticBranch> OptionsFor(AtBat pa, bool userBatting)
        {
            var options = new Dictionary<TacticKind, TacticBranch>
            {
                [TacticKind.Default] = new TacticBranch { Success = () => pa.Finish(RollDefault(pa)) },
            };
            if (!userBatting) return options; // 수비: 상대 타자에게 작전을 낼 수 없다.
            options[TacticKind.SwingAway] = new TacticBranch
            {
                SuccessRate = Fixture8thBottomTactics.SwingAwaySuccessRate,
                Success = () => pa.Finish(Roll(HitTable(MixFor(pa, HitDistributor.PowerBaseline), 0))),
                Failure = () => pa.Finish(Roll(SwingOuts)),
            };
            if (pa.Runners.Third != null && pa.Outs < 2)
            {
                options[TacticKind.SqueezeBunt] = new TacticBranch
                {
                    SuccessRate = Fixture8thBottomTactics.SqueezeSuccessRate,
                    Success = () => pa.Finish(PlayOutcome.SacrificeBunt),
                    Failure = () => pa.Finish(PlayOutcome.FailedSqueeze),
                };
            }
            return options;
        }

        PlayOutcome RollDefault(AtBat pa)
        {
            double p = _calculator.HitProbability(pa.BatterId, pa.PitcherId, new MatchupSituation(pa.Outs, pa.Runners));
            p = Math.Max(MinOnBaseRate, Math.Min(MaxOnBaseRate, p));
            return Roll(_random.NextDouble() < p ? HitTable(MixFor(pa, HitDistributor.LeagueBaseline), WalkShare) : Outs);
        }

        /// <summary>이 타석 타자·투수의 안타 분포. 둘 다 데이터가 없으면 기본 분포.</summary>
        HitMix MixFor(AtBat pa, HitMix baseline)
        {
            var context = _calculator.ContextFor(pa.BatterId, pa.PitcherId, new MatchupSituation(pa.Outs, pa.Runners));
            return context == null ? baseline : HitDistributor.Distribute(context.Batter, context.Pitcher, baseline);
        }

        /// <summary>출루 결과표: 볼넷 walkShare + 안타 (1 − walkShare) × 분포. 강공 성공은 walkShare 0.</summary>
        public static (PlayOutcome outcome, double weight)[] HitTable(HitMix mix, double walkShare)
        {
            double hits = 1 - walkShare;
            return new[]
            {
                (PlayOutcome.Walk, walkShare), (PlayOutcome.Single, hits * mix.Single), (PlayOutcome.Double, hits * mix.Double),
                (PlayOutcome.Triple, hits * mix.Triple), (PlayOutcome.HomeRun, hits * mix.HomeRun),
            };
        }

        PlayOutcome Roll((PlayOutcome outcome, double weight)[] table) => Pick(table, _random.NextDouble());

        /// <summary>
        /// 가중치 표에서 [0, 1) 값 하나로 결과를 고른다. 가중치 0인 항목은 뽑히지 않고, 끝까지 못 고르면(부동소수 오차)
        /// 가중치가 있는 마지막 항목. 가중치 합이 0 이하·NaN이면 <see cref="ArgumentException"/> (분배기가 미리 막는다).
        /// </summary>
        public static PlayOutcome Pick((PlayOutcome outcome, double weight)[] table, double unit)
        {
            double total = table.Sum(t => t.weight);
            if (!(total > 0) || double.IsInfinity(total)) throw new ArgumentException("가중치 합이 0 이하다.", nameof(table));
            double roll = unit * total;
            PlayOutcome last = table[0].outcome;
            foreach (var (outcome, weight) in table)
            {
                if (weight <= 0) continue;
                if (roll < weight) return outcome;
                roll -= weight;
                last = outcome;
            }
            return last;
        }

        /// <summary>타석 하나: 1-1까지의 투구와, 결과를 받아 결정구(들)를 만드는 부분.</summary>
        sealed class AtBat
        {
            readonly int _inning;
            readonly HalfInning _half;
            readonly StrikeZone _zone;
            readonly int _variant;

            public readonly string BatterId;
            public readonly string PitcherId;
            public readonly int Outs;
            public readonly BaseState Runners;

            public AtBat(int inning, HalfInning half, string pitcherId, string batterId, int outs, BaseState runners, int variant)
            {
                _inning = inning;
                _half = half;
                _variant = variant;
                BatterId = batterId;
                PitcherId = pitcherId;
                Outs = outs;
                Runners = runners;
                _zone = ZoneRuleConfig.ProvisionalKbo.ZoneFor(HeightFor(batterId));
            }

            /// <summary>타석을 끝내지 않는 투구. 스트라이크는 존 중앙, 볼은 바깥쪽 낮게.</summary>
            public PitchEvent Pitch(int seq, PitchResult result, Count before)
            {
                var (type, kmh) = Pitches[(_variant + seq) % Pitches.Length];
                bool strike = result != PitchResult.Ball;
                var (x, y) = strike ? Center : Outside;
                return FixtureBuilder.Pitch(_inning, _half, PitcherId, BatterId, _zone, type, kmh, x, y,
                    result, strike, before, CountRules.Apply(before, result).After, Runners, Outs);
            }

            /// <summary>1-1에서 결과까지의 투구. 볼넷은 볼 3개, 삼진은 스트라이크 2개, 인플레이는 1구.</summary>
            public IReadOnlyList<PitchEvent> Finish(PlayOutcome requested)
            {
                var res = PlayResultResolver.Resolve(Outs, Runners, BatterId, requested);
                var count = new Count(1, 1);
                var events = new List<PitchEvent>();
                int seq = PitchesBeforeDecision;

                if (res.PlateAppearance == PlateAppearanceOutcome.Walk)
                {
                    for (int i = 0; i < 2; i++, seq++)
                    {
                        events.Add(Pitch(seq, PitchResult.Ball, count));
                        count = events[events.Count - 1].CountAfter;
                    }
                    events.Add(Last(seq, PitchResult.Ball, count, res, Outside));
                }
                else if (res.PlateAppearance == PlateAppearanceOutcome.Strikeout)
                {
                    events.Add(Pitch(seq++, PitchResult.CalledStrike, count));
                    count = events[0].CountAfter;
                    events.Add(Last(seq, PitchResult.SwingingStrike, count, res, Center));
                }
                else
                {
                    var last = Last(seq, PitchResult.InPlay, count, res, Center);
                    last.PlayType = res.PlayType;
                    last.SprayAngleDeg = SprayAngle(res.Outcome);
                    last.PlayResult = PlayText(res) + InningEndText(res);
                    events.Add(last);
                }
                return events;
            }

            PitchEvent Last(int seq, PitchResult result, Count before, PlayResolution res, (double x, double y) at)
            {
                var (type, kmh) = Pitches[(_variant + seq) % Pitches.Length];
                var t = CountRules.Apply(before, result);
                return new PitchEvent
                {
                    Inning = _inning, HalfInning = _half, OutsBefore = Outs,
                    CountBefore = before,
                    PitcherId = PitcherId, BatterId = BatterId,
                    PitchType = type, VelocityKmh = kmh,
                    LocationX = at.x, LocationY = at.y, Zone = _zone,
                    Result = result, IsStrikeByRule = StrikeZoneRule.IsStrike(_zone, at.x, at.y),
                    CountAfter = t.After, Outcome = t.Outcome,
                    RunnersBefore = Runners, RunnersAfter = res.RunnersAfter,
                    RunsScored = res.RunsScored, OutsAfter = res.OutsAfter,
                };
            }

            (double x, double y) Center => (0, (_zone.TopCm + _zone.BottomCm) / 2);
            (double x, double y) Outside => (-(_zone.HalfWidthCm + 10), _zone.BottomCm - 5);

            double SprayAngle(PlayOutcome o)
            {
                // 같은 결과도 타석마다 방향이 조금씩 다르게 (좌·중·우).
                double side = new[] { -1.0, 0.2, 1.0 }[_variant % 3];
                switch (o)
                {
                    case PlayOutcome.Single: return 26 * side;
                    case PlayOutcome.Double: return 32 * side;
                    case PlayOutcome.Triple: return 38 * side;
                    case PlayOutcome.HomeRun: return 20 * side;
                    case PlayOutcome.DoublePlay: return -14;
                    case PlayOutcome.GroundOut: return 18 * side;
                    case PlayOutcome.FlyOut: return 15 * side;
                    case PlayOutcome.SacrificeFly: return 30;
                    case PlayOutcome.LineOut: return -24 * side;
                    case PlayOutcome.PopOut: return 6;
                    case PlayOutcome.SacrificeBunt: return 12;
                    case PlayOutcome.FailedSqueeze: return 6;
                    default: return 0;
                }
            }

            string PlayText(PlayResolution res)
            {
                int runs = res.RunsScored;
                string dir = _variant % 3 == 0 ? "좌" : _variant % 3 == 1 ? "중" : "우";
                switch (res.Outcome)
                {
                    case PlayOutcome.Single: return runs > 0 ? $"{dir}전 적시타 — {runs}점" : $"{dir}전 안타";
                    case PlayOutcome.Double: return runs > 0 ? $"{dir}중간 적시 2루타 — {runs}점" : $"{dir}중간 2루타";
                    case PlayOutcome.Triple: return runs > 0 ? $"{dir}중간 적시 3루타 — {runs}점" : $"{dir}중간 3루타";
                    case PlayOutcome.HomeRun:
                        return $"{dir}월 " + (runs == 1 ? "솔로 홈런" : runs == 2 ? "투런 홈런" : runs == 3 ? "스리런 홈런" : "만루 홈런");
                    case PlayOutcome.GroundOut: return "유격수 땅볼";
                    case PlayOutcome.DoublePlay: return "유격수 땅볼 병살(6-4-3)";
                    case PlayOutcome.FlyOut: return "중견수 뜬공";
                    case PlayOutcome.SacrificeFly: return runs > 0 ? $"우익수 희생플라이 — {runs}점" : "우익수 뜬공";
                    case PlayOutcome.LineOut: return "3루수 직선타";
                    case PlayOutcome.PopOut: return "2루수 내야 뜬공";
                    case PlayOutcome.SacrificeBunt:
                        return runs > 0 ? "스퀴즈 번트 성공 — 3루 주자 득점, 타자 1루 아웃"
                            : Runners.RunnerCount > 0 && res.OutsAfter < 3 ? "희생 번트 — 주자 한 루씩 진루, 타자 1루 아웃" : "번트 — 타자 1루 아웃";
                    case PlayOutcome.FailedSqueeze: return "스퀴즈 실패 — 번트 포수 뜬공, 3루 주자 귀루 못 해 더블 아웃";
                    default: return res.Outcome.ToString();
                }
            }

            static string InningEndText(PlayResolution res)
            {
                if (!res.EndsHalfInning) return "";
                int left = res.RunnersAfter.RunnerCount;
                return left > 0 ? $", 이닝 종료 · 잔루 {left}" : ", 이닝 종료";
            }
        }

        /// <summary>존 높이용 타자 신장. 승부처 3·4번은 고정 장면 값, 나머지는 기본값.</summary>
        static double HeightFor(string batterId)
        {
            switch (BattingOrder.OrderOf(batterId))
            {
                case 3: return Fixture8thBottom.Batter1HeightCm;
                case 4: return Fixture8thBottom.Batter2HeightCm;
                default: return DefaultBatterHeightCm;
            }
        }
    }
}
