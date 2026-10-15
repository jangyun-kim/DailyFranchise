using System;
using System.Collections.Generic;
using System.Linq;

namespace DailyFranchise.Sim.Data
{
    /// <summary>
    /// 경기 중에만 쓰는 투수 운용 상태: 지금 마운드의 투수, 강판된 투수, 교체 후보.
    /// 선수는 모두 사본(<see cref="PlayerData.CloneAs"/>)으로 들고 있어 원본 선수단(<see cref="TeamRoster"/>)·시즌 데이터는
    /// 바뀌지 않는다. 경기가 끝나면 버린다 (로비의 라인업 카드 <see cref="LineupCard"/>와 별개).
    ///
    /// 규칙:
    /// - 교체 후보 = 대기 선수(벤치·불펜) 중 투수(<see cref="PlayerData.IsPitcher"/>)만. 야수는 후보가 아니다.
    /// - 강판된 투수는 이 경기에 다시 등판할 수 없다. 지금 마운드의 투수도 후보가 아니다.
    /// </summary>
    public sealed class InGameRoster
    {
        readonly List<PlayerData> _pool;
        readonly List<string> _used = new List<string>();

        /// <param name="startingPitcherId">처음 마운드에 있는 투수 ID.</param>
        /// <param name="pool">교체 후보가 될 수 있는 대기 선수 (야수가 섞여 있어도 된다). 사본으로 보관한다.</param>
        public InGameRoster(string startingPitcherId, IEnumerable<PlayerData> pool)
        {
            CurrentPitcherId = startingPitcherId ?? throw new ArgumentNullException(nameof(startingPitcherId));
            _pool = (pool ?? Enumerable.Empty<PlayerData>()).Where(p => p != null)
                .Select(p => p.CloneAs(p.Role, p.Order, p.Position)).ToList();
            if (_pool.Select(p => p.Id).Distinct().Count() != _pool.Count)
                throw new ArgumentException("대기 선수 ID가 중복된다.", nameof(pool));
        }

        /// <summary>선수단의 오늘 선발과 벤치·불펜으로 만든다. 선발이 없으면 ArgumentException.</summary>
        public static InGameRoster From(TeamRoster roster)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            var starter = roster.StartingPitcher ?? throw new ArgumentException($"{roster.Team} 오늘 선발이 없다.", nameof(roster));
            return new InGameRoster(starter.Id, roster.Bench.Concat(roster.Bullpen));
        }

        public string CurrentPitcherId { get; private set; }

        /// <summary>강판된 투수 ID (강판 순).</summary>
        public IReadOnlyList<string> UsedPitchers => _used;

        /// <summary>대기 선수 사본 전체 (야수 포함).</summary>
        public IReadOnlyList<PlayerData> Pool => _pool;

        public bool IsUsed(string pitcherId) => _used.Contains(pitcherId);

        /// <summary>지금 올릴 수 있는 구원 투수 (대기 순서 그대로).</summary>
        public IReadOnlyList<PlayerData> ReliefCandidates =>
            _pool.Where(p => p.IsPitcher && p.Id != CurrentPitcherId && !IsUsed(p.Id)).ToList();

        /// <summary>목록에 보일 투수 전체 (강판된 투수 포함, 지금 투수 제외). 야수는 빠진다.</summary>
        public IReadOnlyList<PlayerData> BullpenPitchers =>
            _pool.Where(p => p.IsPitcher && p.Id != CurrentPitcherId).ToList();

        /// <summary>이 선수를 지금 올릴 수 있는지. 안 되면 reason에 이유.</summary>
        public bool CanBringIn(string pitcherId, out string reason)
        {
            var p = _pool.FirstOrDefault(x => x.Id == pitcherId);
            reason = p == null ? "대기 명단에 없는 선수"
                : !p.IsPitcher ? "투수가 아닌 선수"
                : pitcherId == CurrentPitcherId ? "이미 마운드에 있는 투수"
                : IsUsed(pitcherId) ? "이미 강판된 투수 (재등판 불가)"
                : null;
            return reason == null;
        }

        public bool CanBringIn(string pitcherId) => CanBringIn(pitcherId, out _);

        /// <summary>투수를 바꾼다: 지금 투수는 강판(재등판 불가) 처리된다. 규칙에 어긋나면 false, 상태는 그대로.</summary>
        public bool BringIn(string pitcherId)
        {
            if (!CanBringIn(pitcherId)) return false;
            _used.Add(CurrentPitcherId);
            CurrentPitcherId = pitcherId;
            return true;
        }
    }
}
