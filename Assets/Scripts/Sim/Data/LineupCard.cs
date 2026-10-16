using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Data
{
    /// <summary>라인업 카드의 한 자리: 선발 타순(0~8) 또는 벤치 순서(0부터).</summary>
    public readonly struct LineupSpot : IEquatable<LineupSpot>
    {
        public readonly bool IsBench;
        /// <summary>0부터. 선발이면 타순 − 1.</summary>
        public readonly int Index;

        LineupSpot(bool bench, int index)
        {
            IsBench = bench;
            Index = index;
        }

        /// <param name="order">타순 1~9.</param>
        public static LineupSpot Batting(int order) => new LineupSpot(false, order - 1);
        /// <param name="index">벤치 순서 0부터.</param>
        public static LineupSpot Bench(int index) => new LineupSpot(true, index);

        public bool Equals(LineupSpot other) => IsBench == other.IsBench && Index == other.Index;
        public override bool Equals(object obj) => obj is LineupSpot other && Equals(other);
        public override int GetHashCode() => IsBench ? -1 - Index : Index;
        public static bool operator ==(LineupSpot a, LineupSpot b) => a.Equals(b);
        public static bool operator !=(LineupSpot a, LineupSpot b) => !a.Equals(b);
        public override string ToString() => IsBench ? $"벤치 {Index + 1}" : $"{Index + 1}번";
    }

    /// <summary>선발 타순 한 자리: 선수와 그 자리의 수비 위치.</summary>
    public readonly struct LineupSlot : IEquatable<LineupSlot>
    {
        public readonly string PlayerId;
        public readonly string Position;

        public LineupSlot(string playerId, string position)
        {
            PlayerId = playerId;
            Position = position;
        }

        public bool Equals(LineupSlot other) => PlayerId == other.PlayerId && Position == other.Position;
        public override bool Equals(object obj) => obj is LineupSlot other && Equals(other);
        public override int GetHashCode() => (PlayerId?.GetHashCode() ?? 0) * 397 ^ (Position?.GetHashCode() ?? 0);
        public override string ToString() => $"{PlayerId}({Position})";
    }

    /// <summary>
    /// 내 구단 타자 라인업 카드 (선발 9명 + 벤치). 선수 ID만 들고, 기록은 <see cref="TeamRoster"/>에서 가져온다.
    /// 교체 규칙 (포지션 적합성은 보지 않는다):
    /// - 선발 ↔ 선발: 두 자리를 통째로 맞바꾼다. 선수는 자기 수비 위치를 그대로 갖고 타순만 바뀐다.
    /// - 선발 ↔ 벤치: 벤치 선수가 그 타순·수비 위치로 들어가고, 빠진 선발 선수는 그 벤치 자리로 간다.
    /// - 벤치 ↔ 벤치, 같은 자리, 범위 밖 자리: 바꾸지 않는다 (예외 없이 false).
    /// </summary>
    public sealed class LineupCard
    {
        public const int BattingSlots = 9;

        readonly LineupSlot[] _batting;
        readonly List<string> _bench;

        /// <exception cref="ArgumentException">선발이 9명이 아니거나, 빈 ID·중복 선수가 있을 때.</exception>
        public LineupCard(IEnumerable<LineupSlot> batting, IEnumerable<string> bench)
        {
            _batting = (batting ?? throw new ArgumentNullException(nameof(batting))).ToArray();
            _bench = (bench ?? throw new ArgumentNullException(nameof(bench))).ToList();
            if (_batting.Length != BattingSlots)
                throw new ArgumentException($"선발 타순은 {BattingSlots}명이어야 한다: {_batting.Length}명", nameof(batting));
            var ids = _batting.Select(s => s.PlayerId).Concat(_bench).ToList();
            if (ids.Any(string.IsNullOrEmpty)) throw new ArgumentException("빈 선수 ID가 있다.");
            if (ids.Distinct().Count() != ids.Count) throw new ArgumentException("라인업에 같은 선수가 두 번 있다.");
            if (_batting.Any(s => string.IsNullOrEmpty(s.Position))) throw new ArgumentException("수비 위치가 빈 타순이 있다.");
        }

        /// <summary>선수단의 기본 라인업(데이터 순서 그대로). 타순이 9명이 아니면 null.</summary>
        public static LineupCard FromRoster(TeamRoster roster)
        {
            if (roster == null || roster.Lineup.Count != BattingSlots) return null;
            return new LineupCard(roster.Lineup.Select(p => new LineupSlot(p.Id, p.Position)), roster.Bench.Select(p => p.Id));
        }

        public IReadOnlyList<LineupSlot> Batting => _batting;
        public IReadOnlyList<string> Bench => _bench;

        public bool Contains(LineupSpot spot) =>
            spot.Index >= 0 && spot.Index < (spot.IsBench ? _bench.Count : _batting.Length);

        /// <summary>그 자리의 선수 ID. 범위 밖이면 null.</summary>
        public string PlayerAt(LineupSpot spot) =>
            !Contains(spot) ? null : spot.IsBench ? _bench[spot.Index] : _batting[spot.Index].PlayerId;

        /// <summary>두 자리를 맞바꿀 수 있는지 (둘 다 범위 안, 서로 다르고, 적어도 하나는 선발).</summary>
        public bool CanSwap(LineupSpot a, LineupSpot b) =>
            Contains(a) && Contains(b) && a != b && !(a.IsBench && b.IsBench);

        /// <returns>바꿨으면 true. 바꿀 수 없는 조합이면 아무것도 바꾸지 않고 false.</returns>
        public bool TrySwap(LineupSpot a, LineupSpot b)
        {
            if (!CanSwap(a, b)) return false;
            if (!a.IsBench && !b.IsBench)
            {
                (_batting[a.Index], _batting[b.Index]) = (_batting[b.Index], _batting[a.Index]);
                return true;
            }

            var (starter, bench) = a.IsBench ? (b, a) : (a, b);
            var slot = _batting[starter.Index];
            _batting[starter.Index] = new LineupSlot(_bench[bench.Index], slot.Position);
            _bench[bench.Index] = slot.PlayerId;
            return true;
        }

        public LineupCard Clone() => new LineupCard(_batting, _bench);

        /// <summary>
        /// 이 카드가 선수단과 맞는지: 카드의 선수 집합이 선수단 타자(선발 + 벤치) 집합과 같아야 한다.
        /// 데이터가 바뀌어 선수가 빠지거나 늘면 false (그때는 기본 라인업을 쓴다).
        /// </summary>
        public bool Matches(TeamRoster roster)
        {
            if (roster == null) return false;
            var expected = new HashSet<string>(roster.Lineup.Concat(roster.Bench).Select(p => p.Id));
            var actual = _batting.Select(s => s.PlayerId).Concat(_bench).ToList();
            return expected.Count == actual.Count && expected.SetEquals(actual);
        }

        /// <summary>
        /// 이 카드대로 선수단을 다시 짠다. 선발은 타순·수비 위치를, 벤치는 순서와 그 벤치 자리의 보직 이름
        /// (원래 그 자리 선수의 보직, 예: "대타")을 받는다. 기록은 원본 그대로이며 원본 선수단은 바꾸지 않는다.
        /// </summary>
        /// <exception cref="ArgumentException">카드가 선수단과 맞지 않을 때.</exception>
        public TeamRoster ApplyTo(TeamRoster roster)
        {
            if (!Matches(roster)) throw new ArgumentException("라인업 카드가 선수단과 맞지 않는다.", nameof(roster));
            var byId = roster.Lineup.Concat(roster.Bench).ToDictionary(p => p.Id);

            var lineup = _batting.Select((slot, i) =>
                Copy(byId[slot.PlayerId], RosterRole.Lineup, i + 1, slot.Position)).ToList();
            var bench = _bench.Select((id, i) =>
                Copy(byId[id], RosterRole.Bench, i + 1, i < roster.Bench.Count ? roster.Bench[i].Position : "대타")).ToList();
            return new TeamRoster(roster.Team, lineup, bench, roster.Starters, roster.Bullpen, roster.Reserves);
        }

        static PlayerData Copy(PlayerData p, RosterRole role, int order, string position) => p.CloneAs(role, order, position);
    }
}
