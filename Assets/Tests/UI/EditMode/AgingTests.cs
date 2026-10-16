using System;
using System.Collections.Generic;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 29: 에이징 커브(성장·전성기·노쇠화), 폼 보정(±10%), 오프시즌 나이 증가와 은퇴, 마스터 데이터 불변, 저장.</summary>
    public class AgingTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;
        const string Samsung = "삼성 라이온즈";

        static string IdOf(string name) => Real.Players.First(p => p.Name == name).Id;

        static SeasonState FinishedSeason()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(29);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(3, 2), random);
            }
            return s;
        }

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Real, s.Signings, s.Progression);

        static IEnumerable<PlayerData> Everyone(LeagueRosterBook book) =>
            book.Rosters.SelectMany(r => LeagueRosterBook.AllOf(r).Concat(r.Reserves));

        // ---------- 에이징 커브 ----------

        [Test]
        public void 나이_구간은_24세_이하_유망주_25에서_31세_전성기_32세_이상_노쇠화()
        {
            Assert.That(AgingCurve.StageOf(0), Is.EqualTo(AgeStage.Unknown));
            Assert.That(AgingCurve.StageOf(20), Is.EqualTo(AgeStage.Prospect));
            Assert.That(AgingCurve.StageOf(24), Is.EqualTo(AgeStage.Prospect));
            Assert.That(AgingCurve.StageOf(25), Is.EqualTo(AgeStage.Peak));
            Assert.That(AgingCurve.StageOf(31), Is.EqualTo(AgeStage.Peak));
            Assert.That(AgingCurve.StageOf(32), Is.EqualTo(AgeStage.Decline));
        }

        [Test]
        public void 유망주는_대부분_오르고_전성기는_소폭_노쇠화는_해마다_더_떨어진다()
        {
            var random = new SeededRandom(7);
            var prospect = Enumerable.Range(0, 400).Select(_ => AgingCurve.FormDelta(22, random)).ToList();
            Assert.That(prospect.All(d => d >= 0 && d <= AgingCurve.ProspectMaxGrowth), Is.True);
            Assert.That(prospect.Count(d => d > 0) / 400.0, Is.EqualTo(AgingCurve.ProspectGrowthChance).Within(0.06), "약 75% 성장");

            var peak = Enumerable.Range(0, 400).Select(_ => AgingCurve.FormDelta(28, random)).ToList();
            Assert.That(peak.All(d => Math.Abs(d) <= AgingCurve.PeakSwing), Is.True);

            var at32 = Enumerable.Range(0, 200).Select(_ => AgingCurve.FormDelta(32, random)).ToList();
            var at36 = Enumerable.Range(0, 200).Select(_ => AgingCurve.FormDelta(36, random)).ToList();
            Assert.That(at32.All(d => d >= -0.015 - 1e-12 && d <= -0.005 + 1e-12), Is.True, "32세: −1.0% ± 0.5%");
            Assert.That(at36.All(d => d >= -0.035 - 1e-12 && d <= -0.025 + 1e-12), Is.True, "36세 이상: 최대 −3.0% ± 0.5%");
            Assert.That(AgingCurve.FormDelta(0, random), Is.Zero, "나이 모름");
        }

        [Test]
        public void 은퇴_확률은_40세_이상_무조건_37세부터_나이에_따라_기량이_무너지면_더_높다()
        {
            Assert.That(AgingCurve.RetirementChance(36, 0), Is.Zero);
            Assert.That(AgingCurve.RetirementChance(37, 0), Is.EqualTo(0.2).Within(1e-9));
            Assert.That(AgingCurve.RetirementChance(39, 0), Is.EqualTo(0.6).Within(1e-9));
            Assert.That(AgingCurve.RetirementChance(40, 0.1), Is.EqualTo(1));
            Assert.That(AgingCurve.RetirementChance(34, -0.08), Is.EqualTo(0.5));
            Assert.That(AgingCurve.RetirementChance(33, -0.10), Is.Zero);
            Assert.That(AgingCurve.RetirementChance(0, -0.10), Is.Zero);
        }

        // ---------- 폼 보정 ----------

        static PlayerData Batter(double form) => new PlayerData
        {
            Id = "b", Name = "타자", Role = RosterRole.Lineup, BattingAverage = 0.280, OnBasePercentage = 0.350, Form = form,
        };

        static PlayerData Pitcher(double form) => new PlayerData
        {
            Id = "p", Name = "투수", Role = RosterRole.Starter, Era = 4.0, OpponentAverage = 0.260, Form = form,
        };

        sealed class Lookup : IPlayerLookup
        {
            readonly Dictionary<string, PlayerData> _players;
            public Lookup(params PlayerData[] players) => _players = players.ToDictionary(p => p.Id);
            public PlayerData FindPlayer(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;
        }

        static readonly MatchupSituation Empty = new MatchupSituation(0, BaseState.Empty);

        [Test]
        public void 폼_보정은_타자는_안타_확률을_올리고_투수는_내리며_10퍼센트로_자른다()
        {
            var only = new IMatchupModifier[] { new ProgressionModifier(ConditionSide.Batter), new ProgressionModifier(ConditionSide.Pitcher) };
            double raw = MatchupCalculator.Combine(Batter(0), Pitcher(0));

            double up = new MatchupCalculator(new Lookup(Batter(0.05), Pitcher(0)), only).HitProbability("b", "p", Empty);
            Assert.That(up, Is.EqualTo(raw * 1.05).Within(1e-9));
            double ace = new MatchupCalculator(new Lookup(Batter(0), Pitcher(0.05)), only).HitProbability("b", "p", Empty);
            Assert.That(ace, Is.EqualTo(raw * 0.95).Within(1e-9));
            double monster = new MatchupCalculator(new Lookup(Batter(0.9), Pitcher(-0.9)), only).HitProbability("b", "p", Empty);
            Assert.That(monster, Is.EqualTo(raw * 1.10 * 1.10).Within(1e-9), "양쪽 모두 ±10%에서 멈춘다");

            var calc = new MatchupCalculator(new Lookup(Batter(0.03), Pitcher(-0.04)));
            var labels = calc.Adjustments(calc.ContextFor("b", "p", Empty)).Select(a => a.Text).ToList();
            Assert.That(labels, Does.Contain("타자 성장세 +3%").And.Contain("투수 노쇠화 +4%"));
            Assert.That(MatchupCalculator.DefaultModifiers().OfType<ProgressionModifier>().Count(), Is.EqualTo(2), "기본 보정에 포함");
            Assert.That(new MatchupCalculator(new Lookup(Batter(0))).HitProbability("b", "x", Empty),
                Is.EqualTo(new MatchupCalculator(new Lookup(Batter(0)), Array.Empty<IMatchupModifier>()).HitProbability("b", "x", Empty)),
                "폼 0과 리그 평균 투수는 보정 없음");
        }

        [Test]
        public void 폼은_여러_해_쌓여도_10퍼센트를_넘지_않는다()
        {
            var random = new SeededRandom(1);
            double young = 0, old = 0;
            for (int year = 0; year < 15; year++)
            {
                young = ProgressionModifier.ClampForm(young + AgingCurve.FormDelta(20, random));
                old = ProgressionModifier.ClampForm(old + AgingCurve.FormDelta(38, random));
            }
            Assert.That(young, Is.EqualTo(ProgressionModifier.MaxForm));
            Assert.That(old, Is.EqualTo(-ProgressionModifier.MaxForm));
            Assert.That(new PlayerProgression("x", 1, 0.5, false, 0).Form, Is.EqualTo(0.10));
            Assert.That(Batter(0).WithProgression(30, -3).Form, Is.EqualTo(-0.10));
        }

        // ---------- 오프시즌 ----------

        [Test]
        public void 결산하면_모든_선수가_한_살_먹고_40세_이상은_은퇴해_장부와_시장에서_빠진다()
        {
            var s = FinishedSeason();
            var before = Book(s);
            int playersBefore = Everyone(before).Count(p => !LeagueRosterBook.IsReplacement(p));
            long payrollBefore = StoveLeague.Payroll(before.Get(Samsung), s.Signings);
            string oh = IdOf("오승환"), song = IdOf("송은범");

            var summary = s.TransitionToNextSeason(before, new SeededRandom(2027));
            var aging = summary.Aging;
            Assert.That(aging.Aged, Is.EqualTo(playersBefore), "1군·퓨처스 전원");

            Assert.That(s.ProgressionOf(oh).Retired, Is.True, "41 → 42세 무조건 은퇴");
            Assert.That(s.ProgressionOf(song).Retired, Is.True, "40 → 41세");
            Assert.That(s.ProgressionOf(oh).RetiredYear, Is.EqualTo(2027));
            Assert.That(aging.Retired.All(r => r.Player.Age >= AgingCurve.CollapseAge), Is.True);
            Assert.That(aging.Retired.Select(r => r.Player.Name), Does.Contain("오승환").And.Contain("송은범"));

            var after = Book(s);
            Assert.That(after.Find(oh).player, Is.Null);
            Assert.That(after.Find(song).player, Is.Null);
            Assert.That(StoveLeague.Market(after, "LG 트윈스", FanMood.Happy).Any(a => a.Id == oh), Is.False, "다른 구단 시점 시장에도 없다");
            Assert.That(Everyone(after).Count(p => !LeagueRosterBook.IsReplacement(p)), Is.EqualTo(playersBefore - aging.Retired.Count));
            Assert.That(StoveLeague.Payroll(after.Get(Samsung), s.Signings), Is.LessThan(payrollBefore), "은퇴 연봉만큼 캡 여유");
            Assert.That(RosterLimits.CountOf(after.Get(Samsung)), Is.LessThanOrEqualTo(RosterLimits.CountOf(before.Get(Samsung))));
            Assert.That(LineupCard.FromRoster(after.Get(Samsung)), Is.Not.Null, "타순 빈자리는 벤치에서 채운다");

            // 남은 선수는 모두 한 살 많다
            var oldAges = Everyone(before).Where(p => !LeagueRosterBook.IsReplacement(p)).ToDictionary(p => p.Id, p => p.Age);
            foreach (var p in Everyone(after).Where(p => !LeagueRosterBook.IsReplacement(p)))
            {
                Assert.That(p.Age, Is.EqualTo(oldAges[p.Id] + 1), p.Name);
                Assert.That(s.ProgressionOf(p.Id).AgeDelta, Is.EqualTo(1), p.Name);
            }

            Assert.That(s.RecentNews.Select(n => n.Text), Does.Contain("[은퇴] 삼성 라이온즈의 오승환, 정든 그라운드를 떠나 현역 은퇴 선언!"));
            Assert.That(s.RecentNews.Last(n => n.Kind != NewsKind.Draft).Kind, Is.EqualTo(NewsKind.Retirement),
                "은퇴 뉴스가 가장 최신 (Phase 30부터 그 뒤에 신인 드래프트 뉴스)");
            Assert.That(summary.Text, Does.EndWith($"은퇴 {aging.Retired.Count}명 · 신인 {summary.Draft.MyPicks.Count}명"));
        }

        static void PlayOut(SeasonState s)
        {
            var random = new SeededRandom(s.Year);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(2, 3), random);
            }
        }

        [Test]
        public void 마스터_데이터는_바뀌지_않고_두_해가_지나면_나이가_2살_많은_사본을_쓴다()
        {
            var s = FinishedSeason();
            string koo = IdOf("구자욱");
            var original = Real.FindPlayer(koo);
            int baseAge = original.Age;
            s.TransitionToNextSeason(Book(s), new SeededRandom(1));
            PlayOut(s);
            s.TransitionToNextSeason(Book(s), new SeededRandom(2));

            var now = Book(s).Find(koo).player;
            Assert.That(now.Age, Is.EqualTo(baseAge + 2));
            Assert.That(now.Form, Is.EqualTo(s.ProgressionOf(koo).Form));
            Assert.That(now, Is.Not.SameAs(original));
            Assert.That((original.Age, original.Form), Is.EqualTo((baseAge, 0.0)), "마스터 DB 선수는 그대로");
            Assert.That(Real.GetRoster(Samsung).Lineup.First(p => p.Id == koo).Age, Is.EqualTo(baseAge));
            Assert.That(s.ProgressionOf(koo).AgeDelta, Is.EqualTo(2));
            Assert.That(s.Progression.Values.Where(p => p.Retired).All(p => p.RetiredYear == 2027 || p.RetiredYear == 2028), Is.True);
        }

        [Test]
        public void 변동치가_없으면_장부는_DB_선수단을_그대로_쓴다()
        {
            var s = FinishedSeason();
            Assert.That(Book(s).Get(Samsung), Is.SameAs(Real.GetRoster(Samsung)));
            s.TransitionToNextSeason(); // 장부 없이 결산 (Phase 27 동작) — 에이징 없음
            Assert.That(s.Progression, Is.Empty);
        }

        [Test]
        public void 은퇴_선수가_사용자_라인업_벤치에_있으면_빼고_타순이면_기본_라인업으로()
        {
            var s = FinishedSeason();
            var roster = Real.GetRoster(Samsung);
            var card = LineupCard.FromRoster(roster);
            string catcher = IdOf("강민호"); // 39세가 되는 선발 포수 — 은퇴할 수도 있다
            s.MyLineup = card;
            s.TransitionToNextSeason(Book(s), new SeededRandom(2027));
            bool retired = s.ProgressionOf(catcher).Retired;
            if (retired) Assert.That(s.MyLineup, Is.Null, "타순 선수 은퇴 → 기본 라인업");
            else Assert.That(s.MyLineup, Is.Not.Null);

            var s2 = FinishedSeason();
            var bench = new LineupCard(card.Batting, card.Bench.Append(IdOf("오승환")));
            s2.MyLineup = bench;
            s2.TransitionToNextSeason(Book(s2), new SeededRandom(5));
            if (s2.MyLineup != null) Assert.That(s2.MyLineup.Bench, Does.Not.Contain(IdOf("오승환")));
        }

        [Test]
        public void 폭풍_성장은_성장_뉴스가_된다()
        {
            Assert.That(NewsWriter.Breakout(Samsung, "김영웅", 0.028, 2027).Text, Is.EqualTo("[성장] 삼성 라이온즈 김영웅, 비시즌 폭풍 성장! 기량 +3%"));
            Assert.That(NewsWriter.Retired(LeagueRosterBook.FreeAgentTeam, "홍길동", 40, 2027).Text, Is.EqualTo("[은퇴] 자유계약 신분의 홍길동(40세), 현역 은퇴 선언"));

            // 실제 데이터 유망주 22명 — 이 Seed에서 폭풍 성장이 나오면 뉴스 최대 3건, 구단 소속
            var s = FinishedSeason();
            var aging = s.TransitionToNextSeason(Book(s), new SeededRandom(2027)).Aging;
            var news = s.RecentNews.Where(n => n.Kind == NewsKind.Breakout).ToList();
            Assert.That(news.Count, Is.EqualTo(Math.Min(OffseasonAging.MaxBreakoutNews, aging.Breakouts.Count)));
            Assert.That(aging.Breakouts.All(b => b.Gain >= AgingCurve.BreakoutGrowth && b.Player.Age <= AgingCurve.ProspectMaxAge), Is.True);
        }

        [Test]
        public void 선수_변동치는_저장되고_이전_저장_파일은_원본_그대로_읽힌다()
        {
            var s = FinishedSeason();
            s.TransitionToNextSeason(Book(s), new SeededRandom(3));
            var loaded = SaveManager.FromJson(SaveManager.ToJson(s));
            Assert.That(loaded.Progression.Count, Is.EqualTo(s.Progression.Count));
            foreach (var p in s.Progression.Values)
            {
                var l = loaded.ProgressionOf(p.PlayerId);
                Assert.That((l.AgeDelta, l.Retired, l.RetiredYear), Is.EqualTo((p.AgeDelta, p.Retired, p.RetiredYear)), p.PlayerId);
                Assert.That(l.Form, Is.EqualTo(p.Form).Within(1e-6), p.PlayerId + " (JSON 소수 표기)");
            }
            Assert.That(Book(loaded).Find(IdOf("오승환")).player, Is.Null, "불러와도 은퇴 유지");

            var data = s.ToSaveData();
            data.Progression = null;
            Assert.That(SeasonState.FromSaveData(data).Progression, Is.Empty, "Phase 29 이전 파일");
            data.Progression = new List<ProgressionData>
            {
                null, new ProgressionData { Id = "" }, new ProgressionData { Id = "x", AgeDelta = -3, Form = 0.7 },
            };
            var odd = SeasonState.FromSaveData(data).ProgressionOf("x");
            Assert.That((odd.AgeDelta, odd.Form), Is.EqualTo((0, 0.10)), "범위 밖 값은 맞춘다");
        }

        [Test]
        public void 덧씌운_폼은_경기_확률_계산까지_흘러간다()
        {
            var s = SeasonState.CreateDummy();
            string koo = IdOf("구자욱");
            s.SetProgression(new PlayerProgression(koo, 1, 0.06, false, 0));
            var batter = Book(s).Find(koo).player;
            Assert.That((batter.Form, batter.Age), Is.EqualTo((0.06, Real.FindPlayer(koo).Age + 1)));
            var only = new IMatchupModifier[] { new ProgressionModifier(ConditionSide.Batter) };
            double withForm = new MatchupCalculator(new Lookup(batter), only).HitProbability(koo, "x", Empty);
            double plain = new MatchupCalculator(new Lookup(Real.FindPlayer(koo)), only).HitProbability(koo, "x", Empty);
            Assert.That(withForm, Is.EqualTo(plain * 1.06).Within(1e-9));
        }
    }
}
