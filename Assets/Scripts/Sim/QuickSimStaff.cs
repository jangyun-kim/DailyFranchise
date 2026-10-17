using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.Sim
{
    /// <summary>퀵 시뮬레이션 구간에서 투수 한 명이 던진 기록 (가상 투구 수 포함).</summary>
    public sealed class QuickSimAppearance
    {
        public QuickSimAppearance(string pitcherId) => PitcherId = pitcherId;

        public string PitcherId { get; }
        public int Outs { get; internal set; }
        public int Pitches { get; internal set; }
        public int RunsAllowed { get; internal set; }

        /// <summary>던진 이닝 (정수 이닝만 나온다 — 교체는 이닝 교대 때만).</summary>
        public int Innings => Outs / 3;

        public override string ToString() => $"{PitcherId} {Innings}이닝 {Pitches}구 {RunsAllowed}실점";
    }

    /// <summary>
    /// 퀵 시뮬레이션 구간의 한 팀 투수 운용 (Phase 38). 반 이닝 기록(<see cref="HalfInningLine"/>)으로 가상 투구 수를 세고,
    /// 한계에 닿으면 이닝 교대 때 다음 구원 투수로 바꾼다. 난수를 쓰지 않는다 — 득점·안타는 그대로이고 기록을 나눌 투수만 정한다.
    ///
    /// - 가상 투구 수 = <see cref="PitchesPerHalf"/> + <see cref="PitchesPerBaserunner"/> × (안타 + 볼넷 + 실책), 리그 평균 이닝당 ≈ 16.7구.
    /// - 선발: <see cref="StarterMaxOuts"/>(6이닝)를 채웠거나 <see cref="StarterPitchLimit"/>구 이상이면 강판.
    /// - 구원: 한 이닝(<see cref="RelieverMaxOuts"/>)씩 던지고 내려간다.
    /// - 다음 투수는 구원 순서(<see cref="ReliefOrder"/>)대로. 남은 투수가 없으면 지금 투수가 끝까지 던진다 (Fallback).
    /// 마지막 반 이닝 뒤에도 같은 규칙으로 바꿔 두므로 <see cref="CurrentPitcherId"/>는 Play-by-Play 구간 첫 이닝에 올라갈 투수다.
    /// </summary>
    public sealed class QuickSimStaff
    {
        public const int PitchesPerHalf = 11;
        public const int PitchesPerBaserunner = 4;
        public const int StarterMaxOuts = 18;
        public const int StarterPitchLimit = 95;
        public const int RelieverMaxOuts = 3;

        readonly List<QuickSimAppearance> _appearances = new List<QuickSimAppearance>();
        readonly List<string> _byHalf = new List<string>();
        readonly Queue<string> _relievers;

        QuickSimStaff(string starterId, IEnumerable<string> relievers)
        {
            StarterId = starterId ?? throw new ArgumentNullException(nameof(starterId));
            _relievers = new Queue<string>((relievers ?? Enumerable.Empty<string>())
                .Where(id => id != null && id != starterId).Distinct());
            _appearances.Add(new QuickSimAppearance(starterId));
        }

        /// <summary>
        /// 반 이닝들을 차례로 던진다.
        /// </summary>
        /// <param name="starterId">선발 (장면 자리 ID, 예: "away_sp").</param>
        /// <param name="relievers">퀵 시뮬레이션 구간에 올릴 구원 투수 (올릴 순서). null이면 없다.</param>
        /// <param name="halves">이 팀이 수비한 반 이닝 (순서대로).</param>
        public static QuickSimStaff Pitch(string starterId, IEnumerable<string> relievers, IEnumerable<HalfInningLine> halves)
        {
            var staff = new QuickSimStaff(starterId, relievers);
            foreach (var half in halves ?? Enumerable.Empty<HalfInningLine>()) staff.Throw(half);
            return staff;
        }

        /// <summary>반 이닝 하나의 가상 투구 수.</summary>
        public static int PitchesFor(HalfInningLine half) =>
            PitchesPerHalf + PitchesPerBaserunner * (half.Hits + half.Walks + half.Errors);

        /// <summary>
        /// 선수단의 퀵 시뮬레이션 구원 순서: 불펜 투수를 대기 순서대로, 마무리(<see cref="CloserOf"/>)는 Play-by-Play 구간(9회)에 아껴 둔다.
        /// 자리 ID는 <paramref name="prefix"/> + "rp" + 불펜 목록 순번(1부터, 경기 화면 불펜 자리와 같은 규칙). 선수단이 없으면 빈 목록.
        /// </summary>
        public static IReadOnlyList<string> ReliefOrder(TeamRoster roster, string prefix)
        {
            if (roster == null) return Array.Empty<string>();
            var closer = CloserOf(roster);
            return roster.Bullpen.Select((p, i) => (p, id: prefix + "rp" + (i + 1)))
                .Where(x => x.p.IsPitcher && x.p != closer).Select(x => x.id).ToList();
        }

        /// <summary>
        /// 불펜의 마무리: 보직이 "마무리"인 투수, 없으면(DB 보직이 "P"뿐인 선수단) 시즌 세이브가 가장 많은 투수 (세이브가 없으면 null).
        /// </summary>
        public static PlayerData CloserOf(TeamRoster roster)
        {
            if (roster == null) return null;
            var pen = roster.Bullpen.Where(p => p.IsPitcher).ToList();
            return pen.FirstOrDefault(p => p.Position == CpuRosterManager.CloserPosition)
                   ?? pen.Where(p => p.Saves > 0).OrderByDescending(p => p.Saves).FirstOrDefault();
        }

        public string StarterId { get; }

        /// <summary>등판 순서대로의 기록 (첫째가 선발).</summary>
        public IReadOnlyList<QuickSimAppearance> Appearances => _appearances;

        /// <summary>반 이닝별 투수 (<see cref="Pitch"/>에 넘긴 순서).</summary>
        public IReadOnlyList<string> PitcherByHalf => _byHalf;

        /// <summary>지금 마운드의 투수 = 다음 반 이닝을 던질 투수.</summary>
        public string CurrentPitcherId => _appearances[_appearances.Count - 1].PitcherId;

        public QuickSimAppearance Current => _appearances[_appearances.Count - 1];

        public QuickSimAppearance Find(string pitcherId) => _appearances.FirstOrDefault(a => a.PitcherId == pitcherId);

        /// <summary>남은 구원 투수가 없어 한계를 넘겨 던지고 있는지 (Fallback).</summary>
        public bool PastLimit => ReachedLimit(Current);

        void Throw(HalfInningLine half)
        {
            var a = Current;
            _byHalf.Add(a.PitcherId);
            a.Outs += 3;
            a.Pitches += PitchesFor(half);
            a.RunsAllowed += half.Runs;
            if (ReachedLimit(a) && _relievers.Count > 0) _appearances.Add(new QuickSimAppearance(_relievers.Dequeue()));
        }

        bool ReachedLimit(QuickSimAppearance a) =>
            a.PitcherId == StarterId ? a.Outs >= StarterMaxOuts || a.Pitches >= StarterPitchLimit : a.Outs >= RelieverMaxOuts;
    }
}
