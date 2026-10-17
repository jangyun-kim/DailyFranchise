using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.Sim.Data;

namespace DailyFranchise.App
{
    /// <summary>CPU 스토브리그 AI 한 번의 결과 (Phase 41).</summary>
    public sealed class CpuStoveReport
    {
        /// <summary>방출 (구단, 방출 직전 선수 사본). 구단 순서 → 가치 낮은 순.</summary>
        public readonly List<(string Team, PlayerData Player)> Releases = new List<(string, PlayerData)>();
        /// <summary>FA 영입 (구단, 선수, 연봉, 채운 자리 "C"·"IF"·"OF"·"P"). 영입 순서.</summary>
        public readonly List<(string Team, PlayerData Player, long Salary, string Need)> Signings =
            new List<(string, PlayerData, long, string)>();
        /// <summary>전력 보강 영입으로 퓨처스로 내려간 1군 선수 (구단, 내려간 선수, 올라온 영입 선수 ID).</summary>
        public readonly List<(string Team, PlayerData SentDown, string CalledUpId)> SentDown = new List<(string, PlayerData, string)>();
    }

    /// <summary>
    /// CPU(타 구단) 스토브리그 AI (Phase 41). 오프시즌 결산(<see cref="SeasonState.TransitionToNextSeason"/>)에서 에이징·드래프트 뒤에 돈다.
    /// 결과는 사용자 이적과 같은 이적 기록(<see cref="SigningRecord.ByCpu"/>)으로 남아 장부·저장·FA 시장에 그대로 반영된다. 난수를 쓰지 않는다.
    ///
    /// 1) 방출: 구단마다 벤치·퓨처스의 잉여 노장 — <see cref="ReleaseMinAge"/>세 이상, 폼 <see cref="ReleaseMaxForm"/> 이하(노쇠화로 하락 중),
    ///    WAR <see cref="ReleaseMaxWar"/> 미만 — 을 가치 낮은 순으로 최대 <see cref="MaxReleasesPerTeam"/>명 자유계약으로 보낸다.
    ///    선발 타순·선발 로테이션·1군 불펜·신인·부상 선수·대체 선수는 보호한다.
    /// 2) 영입: 지난 시즌 순위가 낮은 구단부터 1군 뎁스의 빈자리(<see cref="NeedOf"/>)를 찾아 FA 시장에서 그 자리 선수 중
    ///    가성비(<see cref="TradeEvaluator.Value"/>, 연봉 = WAR 연봉)가 가장 좋은 선수를 데려온다. 빈자리가 없으면 가장 취약한 자리 —
    ///    1군 벤치(포수·내야·외야)·불펜에서 가치가 가장 낮은 선수보다 <see cref="UpgradeMargin"/> 이상 나은 FA가 있는 자리 (보강 폭 큰 순) —
    ///    를 보강한다: 영입 선수는 정원이 차 있어 퓨처스로 가고, 곧바로 1군 맞바꾸기(<see cref="SigningRecord.Move"/>)로 그 약한 선수와 자리를 바꾼다.
    ///    페이롤 + 연봉 ≤ 리그 캡 기준만 본다. 구단당 최대 <see cref="MaxSigningsPerTeam"/>명, 이번 겨울 자기가 방출한 선수는 다시 데려오지 않는다.
    /// 3) 뉴스: 방출은 한 줄 요약, 영입은 가치 높은 순 <see cref="MaxSigningNews"/>건 ("[오피셜] LG 트윈스, 자유계약 시장에서 ○○과 전격 계약!").
    /// </summary>
    public static class CpuStoveLeague
    {
        public const int ReleaseMinAge = 34;
        public const double ReleaseMaxForm = -0.02;
        public const double ReleaseMaxWar = 1.0;
        public const int MaxReleasesPerTeam = 2;
        public const int MaxSigningsPerTeam = 2;
        public const int MaxSigningNews = 6;
        /// <summary>전력 보강 영입의 최소 가치 차이 (FA 가치 − 1군 약한 선수 가치).</summary>
        public const double UpgradeMargin = 0.5;

        public const string NeedCatcher = "C";
        public const string NeedInfield = "IF";
        public const string NeedOutfield = "OF";
        public const string NeedPitcher = "P";

        /// <param name="rebuild">지금 시즌 기록(이적·변동치·신인)으로 다시 짠 장부.</param>
        /// <param name="teamOrder">CPU 구단이 움직이는 순서 (지난 시즌 순위 낮은 구단부터). 내 구단·자유계약은 건너뛴다.</param>
        public static CpuStoveReport Process(SeasonState season, Func<LeagueRosterBook> rebuild, IEnumerable<string> teamOrder)
        {
            if (season == null) throw new ArgumentNullException(nameof(season));
            if (rebuild == null) throw new ArgumentNullException(nameof(rebuild));
            var report = new CpuStoveReport();
            var book = rebuild();
            if (book == null) return report;
            var teams = (teamOrder ?? Enumerable.Empty<string>())
                .Where(t => t != season.MyTeam && t != LeagueRosterBook.FreeAgentTeam && book.Get(t) is TeamRoster r && LeagueRosterBook.AllOf(r).Any())
                .Distinct().ToList();

            // 1) 방출
            foreach (var team in teams)
                foreach (var p in ReleaseCandidates(season, book.Get(team)).Take(MaxReleasesPerTeam))
                {
                    season.AddSigning(SigningRecord.CpuRelease(p.Id, team), isBatter: !p.IsPitcher);
                    report.Releases.Add((team, p));
                }
            if (report.Releases.Count > 0) book = rebuild();

            // 2) 영입
            var releasedBy = new HashSet<(string, string)>(report.Releases.Select(r => (r.Team, r.Player.Id)));
            foreach (var team in teams)
                for (int n = 0; n < MaxSigningsPerTeam; n++)
                {
                    var roster = book.Get(team);
                    long payroll = StoveLeague.Payroll(roster, season.Signings);
                    var market = StoveLeague.Market(book, season.MyTeam, FanMood.Normal)
                        .Where(a => !releasedBy.Contains((team, a.Id)) && !season.IsInjured(a.Id))
                        .Where(a => payroll + a.Salary <= season.SalaryCapBase)
                        .Select(a => (agent: a, value: TradeEvaluator.Value(a.Player, a.Salary)))
                        .Where(x => x.value > 0)
                        .OrderByDescending(x => x.value).ThenBy(x => x.agent.Id, StringComparer.Ordinal).ToList();
                    string need = NeedOf(season, roster);
                    FreeAgent pick = null;
                    PlayerData weak = null;
                    if (need != null) pick = market.Where(x => Fills(need, x.agent.Player)).Select(x => x.agent).FirstOrDefault();
                    else (need, pick, weak) = BestUpgrade(season, roster, market);
                    if (pick == null) break;
                    var record = SigningRecord.CpuSigning(pick.Id, team, pick.Salary);
                    season.AddSigning(record, isBatter: !pick.Player.IsPitcher);
                    report.Signings.Add((team, pick.Player, pick.Salary, need));
                    book = book.With(new[] { record });
                    if (weak != null && book.Get(team).Reserves.Any(p => p.Id == pick.Id))
                    {
                        var move = SigningRecord.Move(team, weak.Id, pick.Id);
                        var moved = book.With(new[] { move });
                        if (moved.Applied.Count > book.Applied.Count)
                        {
                            season.AddRosterMove(move);
                            report.SentDown.Add((team, weak, pick.Id));
                            book = moved;
                        }
                    }
                }

            Publish(season, report);
            return report;
        }

        /// <summary>방출 후보: 벤치·퓨처스의 잉여 노장 (가치 낮은 순). 선발 타순·로테이션·1군 불펜·신인·부상·대체 선수는 제외.</summary>
        public static IEnumerable<PlayerData> ReleaseCandidates(SeasonState season, TeamRoster roster)
        {
            if (roster == null) return Enumerable.Empty<PlayerData>();
            return roster.Bench.Concat(roster.Reserves)
                .Where(p => !LeagueRosterBook.IsReplacement(p) && !RookieGenerator.IsRookie(p) && !season.IsInjured(p.Id))
                .Where(p => p.Age >= ReleaseMinAge && p.Form <= ReleaseMaxForm + 1e-9 && p.War < ReleaseMaxWar)
                .OrderBy(p => TradeEvaluator.Value(p, StoveLeague.SalaryOf(p, roster.Team, season.Signings)))
                .ThenBy(p => p.Id, StringComparer.Ordinal);
        }

        /// <summary>
        /// 1군 뎁스의 빈자리 (우선순위 순, 없으면 null): 벤치에 건강한 포수 없음 → "C", 1군 불펜이 정원 미만 → "P",
        /// 벤치에 내야수 없음 → "IF", 외야수 없음 → "OF", 벤치가 정원 미만이면 벤치에 적은 쪽(내야·외야).
        /// </summary>
        public static string NeedOf(SeasonState season, TeamRoster roster)
        {
            if (roster == null) return null;
            var bench = roster.Bench.Where(p => !p.IsPitcher && !season.IsInjured(p.Id) && !LeagueRosterBook.IsReplacement(p)).ToList();
            int Count(string group) => bench.Count(p => PositionMatcher.GroupOf(p.Position) == group);
            if (Count(PositionMatcher.Catcher) == 0) return NeedCatcher;
            if (roster.Bullpen.Count < RosterLimits.MaxBullpen) return NeedPitcher;
            int infield = Count("IF"), outfield = Count("OF");
            if (infield == 0) return NeedInfield;
            if (outfield == 0) return NeedOutfield;
            if (roster.Bench.Count < RosterLimits.MaxBench) return infield <= outfield ? NeedInfield : NeedOutfield;
            return null;
        }

        /// <summary>
        /// 가장 취약한 자리 보강 (빈자리가 없을 때): 자리(포수·내야·외야 = 1군 벤치, 투수 = 1군 불펜)마다 가장 약한 선수와
        /// 그 자리 FA 최고 가치를 견줘, 차이가 <see cref="UpgradeMargin"/> 이상인 자리 중 차이가 가장 큰 곳. 없으면 (null, null, null).
        /// </summary>
        static (string need, FreeAgent pick, PlayerData weak) BestUpgrade(SeasonState season, TeamRoster roster,
            IReadOnlyList<(FreeAgent agent, double value)> market)
        {
            (string, FreeAgent, PlayerData) best = (null, null, null);
            double bestGain = UpgradeMargin - 1e-9;
            foreach (var need in new[] { NeedCatcher, NeedInfield, NeedOutfield, NeedPitcher })
            {
                var pool = need == NeedPitcher ? roster.Bullpen : roster.Bench;
                var weak = pool.Where(p => Fills(need, p) && !LeagueRosterBook.IsReplacement(p) && !season.IsInjured(p.Id))
                    .Select(p => (p, value: TradeEvaluator.Value(p, StoveLeague.SalaryOf(p, roster.Team, season.Signings))))
                    .OrderBy(x => x.value).ThenBy(x => x.p.Id, StringComparer.Ordinal).FirstOrDefault();
                if (weak.p == null) continue;
                var cand = market.FirstOrDefault(x => Fills(need, x.agent.Player));
                if (cand.agent == null || cand.value - weak.value <= bestGain) continue;
                bestGain = cand.value - weak.value;
                best = (need, cand.agent, weak.p);
            }
            return best;
        }

        /// <summary>그 빈자리를 채울 수 있는 선수인지.</summary>
        public static bool Fills(string need, PlayerData p)
        {
            if (p == null) return false;
            if (need == NeedPitcher) return p.IsPitcher;
            if (p.IsPitcher) return false;
            return PositionMatcher.GroupOf(p.Position) == (need == NeedCatcher ? PositionMatcher.Catcher : need);
        }

        static void Publish(SeasonState season, CpuStoveReport report)
        {
            if (report.Releases.Count > 0)
                season.AddNews(NewsWriter.CpuReleases(report.Releases.Select(r => (r.Team, r.Player.Name)).ToList(), season.Year, season.NewsDate));
            foreach (var s in report.Signings.OrderByDescending(s => TradeEvaluator.Value(s.Player, s.Salary)).Take(MaxSigningNews).Reverse())
                season.AddNews(NewsWriter.CpuSigned(s.Team, s.Player.Name, season.Year, season.NewsDate)); // 가장 큰 영입이 맨 위(최신)
        }
    }
}
