using System.Collections.Generic;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;
using UnityEngine;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 41: CPU 구단 스토브리그 AI — 오프시즌 결산 때 잉여 노장 방출(FA 시장 합류), 1군 뎁스 빈자리 FA 영입(구단당 최대 2명,
    /// 캡 검사), 뉴스, 저장. 실제 10개 구단 2024 데이터.
    /// </summary>
    public class CpuStoveLeagueTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;
        const string Samsung = TestLeague.Samsung;
        const string Kia = "KIA 타이거즈";
        const string Lg = TestLeague.Lg;

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Real, s.Signings, s.Progression, s.Rookies);

        static SeasonState FinishedSeason()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(41);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(3, 2), random);
            }
            s.SetSalaryCapBase(StoveLeague.OpeningCap(Real));
            return s;
        }

        static SeasonRollover Rollover(SeasonState s, int seed = 2027) =>
            s.TransitionToNextSeason(Book(s), new SeededRandom(seed), rebuild: () => Book(s));

        /// <summary>그 구단 벤치 포수를 모두 자유계약으로 보내 포수 빈자리를 만든다 (사용자식 기록, CPU 방출 아님).</summary>
        static void StripBenchCatchers(SeasonState s, string team)
        {
            foreach (var c in Book(s).Get(team).Bench.Where(p => p.Position == "C").ToList())
                s.AddSigning(new SigningRecord(c.Id, team, LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
        }

        // ---------- 오프시즌 결산 전체 ----------

        [Test]
        public void 결산하면_CPU_구단이_조건에_맞는_잉여_노장만_방출해_FA_시장이_채워진다()
        {
            var s = FinishedSeason();
            var before = Book(s);
            var summary = Rollover(s);
            var stove = summary.Stove;
            Assert.That(stove, Is.Not.Null);
            Assert.That(stove.Releases, Is.Not.Empty, "2024 데이터 + 1년 에이징이면 방출할 노장이 있다");
            foreach (var (team, p) in stove.Releases)
            {
                Assert.That(team, Is.Not.EqualTo(Samsung), "내 구단은 AI가 건드리지 않는다");
                Assert.That(p.Age, Is.GreaterThanOrEqualTo(CpuStoveLeague.ReleaseMinAge), p.Name);
                Assert.That(p.Form, Is.LessThanOrEqualTo(CpuStoveLeague.ReleaseMaxForm + 1e-9), p.Name);
                Assert.That(p.War, Is.LessThan(CpuStoveLeague.ReleaseMaxWar), p.Name);
                var club = before.Get(team);
                Assert.That(club.Lineup.Concat(club.Starters).Concat(club.Bullpen).Any(x => x.Id == p.Id), Is.False,
                    $"{p.Name}: 선발 타순·로테이션·1군 불펜은 보호 (벤치·퓨처스만)");
                Assert.That(RookieGenerator.IsRookie(p), Is.False);
            }
            Assert.That(stove.Releases.GroupBy(r => r.Team).Max(g => g.Count()), Is.LessThanOrEqualTo(CpuStoveLeague.MaxReleasesPerTeam));
            Assert.That(s.Signings.Count(r => r.ByCpu && r.IsRelease), Is.EqualTo(stove.Releases.Count));

            // 다른 구단이 데려가지 않은 방출 선수는 FA 시장에 있다
            var after = Book(s);
            var market = StoveLeague.Market(after, Samsung, FanMood.Normal).Select(a => a.Id).ToList();
            var signed = new HashSet<string>(stove.Signings.Select(x => x.Player.Id));
            var free = stove.Releases.Where(r => !signed.Contains(r.Player.Id)).ToList();
            Assert.That(free, Is.Not.Empty, "FA 시장에 남은 CPU 방출 선수가 있다");
            Assert.That(free.Select(r => r.Player.Id), Is.SubsetOf(market));
            foreach (var r in stove.Releases)
                Assert.That(after.Get(r.Team).Bench.Concat(after.Get(r.Team).Reserves).Any(x => x.Id == r.Player.Id), Is.False, "원 소속에서 빠졌다");
        }

        [Test]
        public void 결산의_CPU_영입은_구단당_최대_2명이고_그_구단_빈자리를_채우며_캡_안이다()
        {
            var s = FinishedSeason();
            var summary = Rollover(s);
            var stove = summary.Stove;
            Assert.That(stove.Signings.GroupBy(x => x.Team).All(g => g.Count() <= CpuStoveLeague.MaxSigningsPerTeam), Is.True);
            var after = Book(s);
            foreach (var (team, p, salary, need) in stove.Signings)
            {
                Assert.That(team, Is.Not.EqualTo(Samsung));
                Assert.That(CpuStoveLeague.Fills(need, p), Is.True, $"{team} {p.Name}: 빈자리 {need}");
                var club = after.Get(team);
                Assert.That(LeagueRosterBook.AllOf(club).Concat(club.Reserves).Any(x => x.Id == p.Id), Is.True, $"{p.Name}은(는) {team} 소속");
                Assert.That(StoveLeague.Payroll(club, s.Signings), Is.LessThanOrEqualTo(s.SalaryCapBase), team);
                Assert.That(stove.Releases.Any(r => r.Team == team && r.Player.Id == p.Id), Is.False, "자기가 방출한 선수는 다시 데려오지 않는다");
            }
            Assert.That(s.Signings.Count(r => r.ByCpu && r.ToTeam != LeagueRosterBook.FreeAgentTeam), Is.EqualTo(stove.Signings.Count));
        }

        [Test]
        public void 같은_시즌을_다시_결산해도_CPU_스토브리그는_같은_결과다()
        {
            string Run()
            {
                var s = FinishedSeason();
                var st = Rollover(s).Stove;
                return string.Join("|", st.Releases.Select(r => r.Team + r.Player.Id)) + "#" + string.Join("|", st.Signings.Select(x => x.Team + x.Player.Id));
            }
            Assert.That(Run(), Is.EqualTo(Run()), "난수 없이 결정적");
        }

        [Test]
        public void 장부_재구성_함수가_없으면_CPU_스토브리그를_하지_않는다()
        {
            var s = FinishedSeason();
            var summary = s.TransitionToNextSeason(Book(s), new SeededRandom(2027));
            Assert.That(summary.Stove, Is.Null);
            Assert.That(s.Signings.Any(r => r.ByCpu), Is.False);
        }

        // ---------- 뎁스 빈자리 → FA 영입 ----------

        [Test]
        public void 벤치_포수가_없는_구단은_FA_시장의_포수를_가장_먼저_데려온다()
        {
            var s = SeasonState.CreateDummy();
            s.SetSalaryCapBase(StoveLeague.OpeningCap(Real));
            StripBenchCatchers(s, Kia);
            StripBenchCatchers(s, Lg); // LG 백업 포수도 시장으로 → 포수 FA 여럿
            Assert.That(CpuStoveLeague.NeedOf(s, Book(s).Get(Kia)), Is.EqualTo(CpuStoveLeague.NeedCatcher));
            var catchers = StoveLeague.Market(Book(s), Samsung, FanMood.Normal).Where(a => a.Player.Position == "C").ToList();
            Assert.That(catchers.Count, Is.GreaterThanOrEqualTo(2));
            var best = catchers.OrderByDescending(a => TradeEvaluator.Value(a.Player, a.Salary)).First();

            var report = CpuStoveLeague.Process(s, () => Book(s), new[] { Kia });
            Assert.That(report.Releases, Is.Empty, "에이징 전이라 폼 하락 노장 없음");
            Assert.That(report.Signings.First().Need, Is.EqualTo(CpuStoveLeague.NeedCatcher), "포수가 최우선");
            Assert.That(report.Signings.First().Player.Id, Is.EqualTo(best.Id), "포수 중 가성비(가치) 1위");
            Assert.That(report.Signings.Count, Is.InRange(1, CpuStoveLeague.MaxSigningsPerTeam));
            var kia = Book(s).Get(Kia);
            Assert.That(CpuStoveLeague.NeedOf(s, kia), Is.Not.EqualTo(CpuStoveLeague.NeedCatcher), "포수 빈자리가 메워졌다");
            Assert.That(kia.Bench.Concat(kia.Reserves).Any(p => p.Id == best.Id), Is.True);
            var news = s.LatestNews(1).Single();
            Assert.That(news.Tag, Is.EqualTo(NewsWriter.OfficialTag));
            Assert.That(news.Headline, Does.StartWith($"{Kia}, 자유계약 시장에서 ").And.EndWith("전격 계약!"));
        }

        [Test]
        public void 빈자리가_여럿이어도_구단당_2명까지만_영입한다()
        {
            var s = SeasonState.CreateDummy();
            s.SetSalaryCapBase(StoveLeague.OpeningCap(Real) * 3);
            // KIA 벤치 전원과 LG 벤치 전원을 시장으로 → KIA는 포수·내야·외야 모두 빈자리, 시장에는 후보 다수
            foreach (var team in new[] { Kia, Lg })
                foreach (var p in Book(s).Get(team).Bench.ToList())
                    s.AddSigning(new SigningRecord(p.Id, team, LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            var report = CpuStoveLeague.Process(s, () => Book(s), new[] { Kia });
            Assert.That(report.Signings.Count, Is.EqualTo(CpuStoveLeague.MaxSigningsPerTeam), "하드 리밋");
            Assert.That(report.Signings.Select(x => x.Need), Is.EqualTo(new[] { CpuStoveLeague.NeedCatcher, CpuStoveLeague.NeedPitcher })
                .Or.EqualTo(new[] { CpuStoveLeague.NeedCatcher, CpuStoveLeague.NeedInfield }), "포수 다음 우선순위");
        }

        [Test]
        public void 사용자가_내보낸_좋은_선수는_빈자리가_없어도_CPU가_전력_보강으로_가로채_1군에_올린다()
        {
            var s = SeasonState.CreateDummy();
            s.SetSalaryCapBase(StoveLeague.OpeningCap(Real) * 3);
            var gu = Book(s).Get(Samsung).Lineup.Single(p => p.Name == "구자욱"); // LF, WAR 4.39
            s.AddSigning(new SigningRecord(gu.Id, Samsung, LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            var kiaBefore = Book(s).Get(Kia);
            Assert.That(CpuStoveLeague.NeedOf(s, kiaBefore), Is.Null, "KIA 1군에는 빈자리가 없다");

            var report = CpuStoveLeague.Process(s, () => Book(s), new[] { Kia, Lg });
            var signing = report.Signings.Single();
            Assert.That((signing.Team, signing.Player.Id, signing.Need), Is.EqualTo((Kia, gu.Id, CpuStoveLeague.NeedOutfield)),
                "순서가 먼저인 KIA가 가로챈다 (LG는 못 데려간다)");
            var sent = report.SentDown.Single();
            Assert.That(sent.Team, Is.EqualTo(Kia));
            Assert.That(PositionMatcher.GroupOf(sent.SentDown.Position), Is.EqualTo("OF"), "가장 약한 벤치 외야수가 퓨처스로");
            var kia = Book(s).Get(Kia);
            Assert.That(kia.Bench.Any(p => p.Id == gu.Id), Is.True, "영입 선수는 1군 벤치");
            Assert.That(kia.Reserves.Any(p => p.Id == sent.SentDown.Id), Is.True);
            Assert.That(RosterLimits.CountOf(kia), Is.EqualTo(RosterLimits.CountOf(kiaBefore)), "1군 정원 그대로");
            Assert.That(StoveLeague.Market(Book(s), Samsung, FanMood.Normal).Any(a => a.Id == gu.Id), Is.False, "시장에서 사라졌다");
            Assert.That(s.LatestNews(1).Single().Headline, Is.EqualTo($"{Kia}, 자유계약 시장에서 구자욱과 전격 계약!"));
        }

        [Test]
        public void 시장에_1군_약한_선수보다_확실히_나은_선수가_없으면_보강하지_않는다()
        {
            var s = SeasonState.CreateDummy();
            s.SetSalaryCapBase(StoveLeague.OpeningCap(Real) * 3);
            var weakest = Book(s).Get(Samsung).Bench.OrderBy(p => p.War).First();
            s.AddSigning(new SigningRecord(weakest.Id, Samsung, LeagueRosterBook.FreeAgentTeam, 0), isBatter: true);
            var report = CpuStoveLeague.Process(s, () => Book(s), new[] { Kia });
            Assert.That(report.Signings, Is.Empty, $"{weakest.Name}(WAR {weakest.War})로는 보강 폭 {CpuStoveLeague.UpgradeMargin} 미달");
        }

        [Test]
        public void 캡_여유가_없으면_영입하지_않는다()
        {
            var s = SeasonState.CreateDummy();
            s.SetSalaryCapBase(1); // 모든 구단이 캡 초과
            StripBenchCatchers(s, Kia);
            StripBenchCatchers(s, Lg);
            var report = CpuStoveLeague.Process(s, () => Book(s), new[] { Kia, Lg });
            Assert.That(report.Signings, Is.Empty);
            Assert.That(s.Signings.Any(r => r.ByCpu), Is.False);
        }

        [Test]
        public void 빈자리_규칙과_채울_수_있는_선수()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(CpuStoveLeague.NeedOf(s, null), Is.Null);
            var c = new PlayerData { Position = "C", Role = RosterRole.Bench };
            var ss = new PlayerData { Position = "SS", Role = RosterRole.Bench };
            var lf = new PlayerData { Position = "LF", Role = RosterRole.Bench };
            var rp = new PlayerData { Position = "P", Role = RosterRole.Bullpen };
            Assert.That(CpuStoveLeague.Fills("C", c) && !CpuStoveLeague.Fills("C", ss), Is.True);
            Assert.That(CpuStoveLeague.Fills("IF", ss) && !CpuStoveLeague.Fills("IF", c) && !CpuStoveLeague.Fills("IF", lf), Is.True);
            Assert.That(CpuStoveLeague.Fills("OF", lf) && CpuStoveLeague.Fills("P", rp) && !CpuStoveLeague.Fills("P", lf), Is.True);
            Assert.That(CpuStoveLeague.ReleaseCandidates(s, Book(s).Get(Kia)), Is.Empty, "에이징 전(폼 0)에는 방출 후보 없음");
        }

        // ---------- 저장 · 뉴스 ----------

        [Test]
        public void CPU_이적_기록은_저장_파일에서_CPU_표시와_함께_되살아나고_장부도_같다()
        {
            var s = FinishedSeason();
            Rollover(s);
            Assert.That(s.Signings.Any(r => r.ByCpu), Is.True);
            string json = JsonUtility.ToJson(s.ToSaveData());
            var loaded = SeasonState.FromSaveData(JsonUtility.FromJson<SeasonSaveData>(json));
            Assert.That(loaded.Signings.Select(r => (r.PlayerId, r.ToTeam, r.ByCpu)), Is.EqualTo(s.Signings.Select(r => (r.PlayerId, r.ToTeam, r.ByCpu))));
            foreach (var team in Real.Teams.Select(t => t.Name))
                Assert.That(LeagueRosterBook.AllOf(Book(loaded).Get(team)).Select(p => p.Id),
                    Is.EqualTo(LeagueRosterBook.AllOf(Book(s).Get(team)).Select(p => p.Id)), team);
            var old = JsonUtility.FromJson<SeasonSaveData>(json.Replace("\"Cpu\"", "\"Cpu_old\""));
            Assert.That(SeasonState.FromSaveData(old).Signings.Any(r => r.ByCpu), Is.False, "Phase 41 이전 파일");
        }

        [Test]
        public void 뉴스_문장()
        {
            var n = NewsWriter.CpuSigned(Lg, "진세훈", 2027, "5.15 (금)");
            Assert.That((n.Kind, n.Tag, n.Headline), Is.EqualTo((NewsKind.Signing, "[오피셜]", "LG 트윈스, 자유계약 시장에서 진세훈과 전격 계약!")));
            Assert.That(NewsWriter.CpuSigned(Lg, "김현수", 2027, "").Headline, Does.Contain("김현수와"));
            var r = NewsWriter.CpuReleases(new[] { (Kia, "김선빈"), (Lg, "김현수"), (Kia, "최형우") }, 2027, "");
            Assert.That(r.Tag, Is.EqualTo("[단독]"));
            Assert.That(r.Headline.Replace(NewsWriter.NoBreakSpace, ' '), Is.EqualTo("타 구단 베테랑 3명 방출, FA 시장 합류 (KIA 김선빈 · LG 김현수 외 1명)"));
        }

        [Test]
        public void 결산_뉴스에_CPU_방출_요약과_영입이_실린다()
        {
            var s = FinishedSeason();
            var stove = Rollover(s).Stove;
            var news = s.LatestNews(SeasonState.MaxNews).ToList();
            if (stove.Releases.Count > 0)
                Assert.That(news.Any(n => n.Kind == NewsKind.Release && n.Headline.StartsWith($"타 구단 베테랑 {stove.Releases.Count}명 방출")), Is.True);
            int signingNews = news.Count(n => n.Kind == NewsKind.Signing && n.Headline.Contains("자유계약 시장에서"));
            Assert.That(signingNews, Is.EqualTo(System.Math.Min(stove.Signings.Count, CpuStoveLeague.MaxSigningNews)));
        }
    }
}
