using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>1군 ↔ 퓨처스 맞바꾸기가 막히는 이유 (Phase 35).</summary>
    public enum RosterMoveBlock
    {
        None,
        /// <summary>경기 중.</summary>
        InGame,
        /// <summary>내 구단 선수가 아니다 (또는 선수단이 없다).</summary>
        NotMine,
        /// <summary>선발 타순(1~9번)이나 선발 로테이션 — 먼저 벤치·불펜으로 옮겨야 말소할 수 있다.</summary>
        Starter,
        /// <summary>둘 다 1군이거나 둘 다 퓨처스 — 1군 벤치·불펜 한 명과 퓨처스 한 명이어야 한다.</summary>
        SameSide,
        /// <summary>타자와 투수 — 타자는 타자(벤치), 투수는 투수(불펜)끼리만.</summary>
        Mismatch,
        /// <summary>부상 중인 선수 (Phase 36) — 회복할 때까지 1군에 올릴 수 없다.</summary>
        Injured,
    }

    /// <summary>맞바꾸기 한 번의 결과.</summary>
    public sealed class RosterMoveOutcome
    {
        public RosterMoveBlock Block;
        /// <summary>퓨처스로 내려간 선수 (막혔으면 null일 수 있다).</summary>
        public PlayerData Down;
        /// <summary>1군으로 올라온 선수.</summary>
        public PlayerData Up;
        public bool Moved => Block == RosterMoveBlock.None;
    }

    /// <summary>
    /// 1군 등록·말소 (Phase 35, Call-up / Send-down): 1군 벤치 타자 ↔ 퓨처스 타자, 불펜 투수 ↔ 퓨처스 투수를 1:1로 맞바꾼다.
    /// - 정원: 늘 한 명이 올라오고 한 명이 내려가므로 1군 26 · 벤치 5 · 불펜 7이 그대로다 (넘치거나 모자라지 않는다).
    /// - 선발 타순(사용자 라인업 카드 기준 1~9번)과 선발 로테이션은 막는다 — 먼저 벤치·불펜으로 옮긴 뒤 말소한다.
    /// - 연봉 기록은 바뀌지 않는다 (같은 구단). 단, 페이롤은 원래 규칙대로 1군 선수 연봉 합계라 두 선수 연봉 차만큼 움직인다.
    /// - 기록은 이적 기록과 같은 목록에 차례대로 남는다 (<see cref="SigningRecord.Move"/>, <see cref="LeagueRosterBook.TryMove"/>).
    /// 판정은 라인업 카드를 적용한 내 선수단(<see cref="SeasonManager.MyRoster"/>)으로 한다 — 로비에 보이는 그대로.
    /// </summary>
    public static class RosterMoves
    {
        enum Side { None, Lineup, Rotation, Bench, Bullpen, Futures }

        static Side SideOf(TeamRoster r, string id)
        {
            if (r == null || id == null) return Side.None;
            if (r.Lineup.Any(p => p.Id == id)) return Side.Lineup;
            if (r.Starters.Any(p => p.Id == id)) return Side.Rotation;
            if (r.Bench.Any(p => p.Id == id)) return Side.Bench;
            if (r.Bullpen.Any(p => p.Id == id)) return Side.Bullpen;
            if (r.Reserves.Any(p => p.Id == id)) return Side.Futures;
            return Side.None;
        }

        static PlayerData Find(TeamRoster r, string id) =>
            LeagueRosterBook.AllOf(r).Concat(r.Reserves).FirstOrDefault(p => p.Id == id);

        /// <summary>이 선수가 맞바꾸기에 나설 수 있는지 (1군 벤치·불펜 또는 퓨처스, 대체 선수·부상 선수 제외).</summary>
        public static bool IsMovable(TeamRoster my, string id, SeasonState season = null)
        {
            var side = SideOf(my, id);
            return (side == Side.Bench || side == Side.Bullpen || side == Side.Futures) && !LeagueRosterBook.IsReplacement(Find(my, id))
                   && season?.IsInjured(id) != true;
        }

        /// <summary>선발 타순·로테이션 선수인지 (먼저 벤치·불펜으로 옮겨야 한다).</summary>
        public static bool IsStarter(TeamRoster my, string id)
        {
            var side = SideOf(my, id);
            return side == Side.Lineup || side == Side.Rotation;
        }

        /// <summary>퓨처스 선수인지.</summary>
        public static bool IsFutures(TeamRoster my, string id) => SideOf(my, id) == Side.Futures;

        /// <summary>
        /// 맞바꿀 수 있는 상대 (명단 순서): 벤치 타자 → 퓨처스 타자, 불펜 투수 → 퓨처스 투수, 퓨처스 타자 → 벤치, 퓨처스 투수 → 불펜.
        /// 선발 타순·로테이션 선수나 내 선수가 아니면 빈 목록. <paramref name="season"/>이 있으면 부상 선수는 나서지도, 상대로 나오지도 않는다 (Phase 36).
        /// </summary>
        public static IReadOnlyList<PlayerData> Partners(TeamRoster my, string id, SeasonState season = null)
        {
            if (!IsMovable(my, id, season)) return Array.Empty<PlayerData>();
            var p = Find(my, id);
            IEnumerable<PlayerData> pool = SideOf(my, id) == Side.Futures
                ? (p.IsPitcher ? my.Bullpen : my.Bench)
                : my.Reserves.Where(x => x.IsPitcher == p.IsPitcher);
            return pool.Where(x => !LeagueRosterBook.IsReplacement(x) && season?.IsInjured(x.Id) != true).ToList();
        }

        /// <summary>두 선수(순서 무관 — 한 명은 1군 벤치·불펜, 한 명은 퓨처스)를 맞바꿀 수 있는지.</summary>
        public static RosterMoveBlock Check(SeasonState season, TeamRoster my, string a, string b)
        {
            if (season == null) throw new ArgumentNullException(nameof(season));
            if (season.InProgress != null) return RosterMoveBlock.InGame;
            Side sa = SideOf(my, a), sb = SideOf(my, b);
            if (sa == Side.None || sb == Side.None || a == b) return RosterMoveBlock.NotMine;
            if (LeagueRosterBook.IsReplacement(Find(my, a)) || LeagueRosterBook.IsReplacement(Find(my, b))) return RosterMoveBlock.NotMine;
            if (season.IsInjured(a) || season.IsInjured(b)) return RosterMoveBlock.Injured;
            if (sa == Side.Lineup || sa == Side.Rotation || sb == Side.Lineup || sb == Side.Rotation) return RosterMoveBlock.Starter;
            if ((sa == Side.Futures) == (sb == Side.Futures)) return RosterMoveBlock.SameSide;
            if (Find(my, a).IsPitcher != Find(my, b).IsPitcher) return RosterMoveBlock.Mismatch;
            return RosterMoveBlock.None;
        }

        /// <summary>
        /// 맞바꾼다 (순서 무관). 막히면 아무것도 바꾸지 않는다. 되면 맞바꾸기 기록을 남기고 라인업 카드 벤치를 고친다
        /// (<see cref="SeasonState.AddRosterMove"/>) — 장부·페이롤은 호출한 쪽이 다시 짠다.
        /// </summary>
        public static RosterMoveOutcome Apply(SeasonState season, TeamRoster my, string a, string b)
        {
            var outcome = new RosterMoveOutcome { Block = Check(season, my, a, b) };
            if (my != null)
            {
                bool aDown = SideOf(my, a) != Side.Futures;
                outcome.Down = Find(my, aDown ? a : b);
                outcome.Up = Find(my, aDown ? b : a);
            }
            if (!outcome.Moved) return outcome;
            season.AddRosterMove(SigningRecord.Move(my.Team, outcome.Down.Id, outcome.Up.Id));
            return outcome;
        }

        public static string BlockText(RosterMoveBlock block)
        {
            switch (block)
            {
                case RosterMoveBlock.InGame: return "경기 중에는 1군 등록·말소를 할 수 없습니다.";
                case RosterMoveBlock.NotMine: return "내 구단 선수끼리만 맞바꿀 수 있습니다.";
                case RosterMoveBlock.Starter: return "선발 라인업(1~9번)·선발 로테이션 선수는 먼저 벤치·불펜으로 옮긴 뒤 말소할 수 있습니다.";
                case RosterMoveBlock.SameSide: return "1군 벤치·불펜 한 명과 퓨처스 한 명을 맞바꿉니다.";
                case RosterMoveBlock.Mismatch: return "타자는 타자(벤치)와, 투수는 투수(불펜)와만 맞바꿀 수 있습니다.";
                case RosterMoveBlock.Injured: return "부상 중인 선수는 회복할 때까지 1군에 등록할 수 없습니다.";
                default: return "";
            }
        }
    }
}
