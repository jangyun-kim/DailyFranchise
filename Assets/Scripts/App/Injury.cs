using System;
using System.Collections.Generic;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>부상·회복 한 건 (Phase 36, 로그·뉴스·테스트용).</summary>
    public readonly struct InjuryEvent
    {
        public InjuryEvent(string playerId, string team, string name, int games)
        {
            PlayerId = playerId;
            Team = team;
            Name = name;
            Games = games;
        }

        public string PlayerId { get; }
        public string Team { get; }
        /// <summary>선수 이름 (장부에서 못 찾으면 ID).</summary>
        public string Name { get; }
        /// <summary>부상이면 결장 경기 수, 회복이면 0.</summary>
        public int Games { get; }
        public override string ToString() => Games > 0 ? $"{Name} 부상 {Games}경기" : $"{Name} 회복";
    }

    /// <summary>
    /// 부상 규칙 (Phase 36). 경기 결산에서 그 경기에 출장한 선수(타석에 섰거나 1구 이상 던진 선수)마다 경기 전 컨디션으로 확률을 정해 주사위를 굴린다.
    /// - 위험(1) 5% · 지침(2) 1% · 보통 이상 0.1% (불의의 부상).
    /// - 부상이면 결장 경기 수 3~10 (가벼운 부상 3경기 ~ 중상 10경기, 고르게).
    /// - 선수 ID 순서(ordinal)대로 선수마다 난수 하나, 부상이면 경기 수용 난수 하나를 더 쓴다 → 같은 난수·같은 출장이면 같은 결과.
    ///   전용 난수를 받는다 (컨디션·타 구단 경기 난수와 섞지 않는다 — 부상 판정이 순위·컨디션 결과를 바꾸지 않게).
    /// - 부상 선수는 퓨처스로 내려가고(<see cref="SigningRecord.Injury"/>), 내 구단 경기가 끝날 때마다 남은 경기가 1씩 준다.
    ///   0이 되면 회복 — 컨디션 보통, 퓨처스에서 1군 등록 대기 (<see cref="SigningRecord.Recovery"/>).
    /// </summary>
    public static class InjuryRules
    {
        public const double ExhaustedChance = 0.05;
        public const double TiredChance = 0.01;
        public const double HealthyChance = 0.001;
        public const int MinGames = 3;
        public const int MaxGames = 10;

        /// <summary>경기 전 컨디션별 부상 확률.</summary>
        public static double ChanceFor(int condition)
        {
            int c = PlayerCondition.Clamp(condition);
            return c <= PlayerCondition.Exhausted ? ExhaustedChance : c == PlayerCondition.Tired ? TiredChance : HealthyChance;
        }

        /// <summary>결장 경기 수 3~10 (난수 하나).</summary>
        public static int GamesFor(IRandomSource random) =>
            Math.Min(MaxGames, MinGames + random.Next(MaxGames - MinGames + 1));

        /// <summary>
        /// 출장 선수의 부상 판정. <paramref name="candidates"/>는 ID 순으로 정렬해 쓴다. 부상이면 (ID, 결장 경기 수).
        /// </summary>
        public static List<(string id, int games)> Roll(IEnumerable<string> candidates, Func<string, int> conditionBefore, IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var list = new List<string>(candidates ?? Array.Empty<string>());
            list.Sort(StringComparer.Ordinal);
            var hurt = new List<(string, int)>();
            foreach (var id in list)
                if (random.NextDouble() < ChanceFor(conditionBefore(id)))
                    hurt.Add((id, GamesFor(random)));
            return hurt;
        }

        /// <summary>부상 아이콘 (의료 십자).</summary>
        public const string Icon = "✚";

        /// <summary>목록·뱃지 글자: "부상(4경기)".</summary>
        public static string BadgeText(int games) => $"부상({games}경기)";

        /// <summary>아이콘을 붙인 뱃지: "✚ 부상(4경기)".</summary>
        public static string Badge(int games) => Icon + " " + BadgeText(games);

        /// <summary>좁은 칸용: "✚ 부상(4)".</summary>
        public static string ShortBadge(int games) => $"{Icon} 부상({games})";

        /// <summary>막힌 이유 문구: "부상 중입니다 (회복까지 4경기). 회복 뒤 퓨처스에서 1군 등록할 수 있습니다."</summary>
        public static string LockedText(int games) => $"부상 중입니다 (회복까지 {games}경기). 회복 뒤 퓨처스에서 1군 등록할 수 있습니다.";
    }
}
