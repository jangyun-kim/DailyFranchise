using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 39: MVP 코어 루프 배선 — 오늘의 결정의 해결 동선(부상·뎁스 → 시장 포지션·퓨처스 후보), 시장 포지션 필터,
    /// 최근 출장 기록·최근 폼(저장 포함), 헤더 카드 기준 데이터 꼬리표.
    /// </summary>
    public class CoreLoopTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;
        const string Samsung = "삼성 라이온즈";

        static TeamRoster Mine => Real.GetRoster(Samsung);

        /// <summary>벤치에서 포수를 뺀 선수단 (백업 포수 없음).</summary>
        static TeamRoster WithoutBackupCatcher(TeamRoster r) =>
            new TeamRoster(r.Team, r.Lineup, r.Bench.Where(p => p.Position != "C").ToList(), r.Starters, r.Bullpen, r.Reserves);

        // ---------- 오늘의 결정 → 해결 동선 ----------

        [Test]
        public void 벤치에_백업_포수가_없으면_포수_뎁스_부족을_경고하고_시장과_퓨처스_후보로_이어진다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(LobbyBriefing.Decisions(s, Mine, Mine.Starters[0], null).Where(i => i.Key.StartsWith("depth-")), Is.Empty,
                "실제 삼성 벤치에는 포수(이병헌)가 있다");

            var thin = WithoutBackupCatcher(Mine);
            var item = LobbyBriefing.Decisions(s, thin, thin.Starters[0], null).Single(i => i.Key == "depth-C");
            Assert.That(item.Severity, Is.EqualTo(BriefingSeverity.Caution));
            Assert.That(item.Title, Is.EqualTo("포수 뎁스 부족 · 백업 포수 없음"));
            Assert.That(item.MarketPosition, Is.EqualTo("C"));
            var futuresCatcher = thin.Reserves.Where(p => p.Position == "C").OrderByDescending(p => p.War).First();
            Assert.That(item.FuturesId, Is.EqualTo(futuresCatcher.Id), "퓨처스 포수 중 WAR 1위");
            Assert.That(item.Detail, Does.Contain("퓨처스 후보 " + futuresCatcher.Name));
            Assert.That(item.PlayerId, Is.Null, "포지션 문제라 선수 상세가 아니라 버튼으로 해결");
        }

        [Test]
        public void 지친_주전_포수를_대신할_벤치가_없으면_추천_대신_시장과_퓨처스_후보를_붙인다()
        {
            var s = SeasonState.CreateDummy();
            var catcher = Mine.AtOrder(5);
            s.SetCondition(catcher.Id, PlayerCondition.Exhausted);
            var withBackup = LobbyBriefing.Decisions(s, Mine, null, null).Single(i => i.Key == "cond-" + catcher.Id);
            Assert.That(withBackup.Detail, Is.EqualTo("추천 교체: 벤치 이병헌 (C · 보통)"));
            Assert.That(withBackup.FuturesId, Is.Null, "벤치 대안이 있으면 퓨처스 후보는 붙이지 않는다");
            Assert.That(withBackup.MarketPosition, Is.EqualTo("C"), "시장 동선은 늘 열려 있다");

            var thin = WithoutBackupCatcher(Mine);
            var none = LobbyBriefing.Decisions(s, thin, null, null).Single(i => i.Key == "cond-" + catcher.Id);
            Assert.That(none.Detail, Does.StartWith("포수 자리를 대신할 벤치 선수 없음"));
            Assert.That(none.FuturesId, Is.Not.Null);
            Assert.That(none.PlayerId, Is.EqualTo(catcher.Id));
        }

        [Test]
        public void 부상자_명단의_1군_선수도_오늘의_결정에_올라_대체_동선을_준다()
        {
            var s = SeasonState.CreateDummy();
            var catcher = Mine.AtOrder(5);
            s.Injure(Samsung, catcher.Id, 7);
            // 부상 선수는 퓨처스 부상자 명단으로 (Phase 36 장부 규칙과 같은 모양)
            var r = Mine;
            var hurt = new TeamRoster(r.Team, r.Lineup.Where(p => p.Id != catcher.Id).ToList(), r.Bench, r.Starters, r.Bullpen,
                r.Reserves.Append(catcher).ToList());
            var item = LobbyBriefing.Decisions(s, hurt, null, null).Single(i => i.Key == "injury-" + catcher.Id);
            Assert.That(item.Title, Is.EqualTo($"부상 · {catcher.Name} (포수) · 7경기 결장"));
            Assert.That(item.MarketPosition, Is.EqualTo("C"));
            Assert.That(item.FuturesId, Is.Not.Null.And.Not.EqualTo(catcher.Id), "다친 선수 본인은 후보가 아니다");
            Assert.That(item.PlayerId, Is.EqualTo(catcher.Id));
        }

        // ---------- 시장 포지션 필터 ----------

        [Test]
        public void 시장_필터는_수비_위치를_포수_내야_외야_투수로_묶는다()
        {
            Assert.That(FaMarketView.FilterKeyOf("C"), Is.EqualTo("C"));
            Assert.That(FaMarketView.FilterKeyOf("3B"), Is.EqualTo("IF"));
            Assert.That(FaMarketView.FilterKeyOf("ss"), Is.EqualTo("IF"));
            Assert.That(FaMarketView.FilterKeyOf("CF"), Is.EqualTo("OF"));
            Assert.That(FaMarketView.FilterKeyOf("P"), Is.EqualTo("P"));
            Assert.That(FaMarketView.FilterKeyOf("DH"), Is.Null, "지명타자는 전체");
            Assert.That(FaMarketView.FilterKeyOf(null), Is.Null);

            var c = new PlayerData { Position = "C", Role = RosterRole.Bench };
            var ss = new PlayerData { Position = "SS", Role = RosterRole.Bench };
            var rp = new PlayerData { Position = "P", Role = RosterRole.Bullpen };
            Assert.That(FaMarketView.Matches(c, "C") && !FaMarketView.Matches(ss, "C") && !FaMarketView.Matches(rp, "C"), Is.True);
            Assert.That(FaMarketView.Matches(ss, "IF") && !FaMarketView.Matches(c, "IF"), Is.True, "포수는 내야가 아니다");
            Assert.That(FaMarketView.Matches(rp, "P") && !FaMarketView.Matches(c, "P"), Is.True);
            Assert.That(new[] { c, ss, rp }.All(p => FaMarketView.Matches(p, null)), Is.True);
        }

        // ---------- 최근 출장 · 최근 폼 ----------

        static BoxScore Box(string batter, int ab, int h, string pitcher = null, int outs = 0, int er = 0)
        {
            var box = new BoxScore();
            var b = box.Batter(batter);
            b.PlateAppearances = ab;
            b.AtBats = ab;
            b.Hits = h;
            if (pitcher != null)
            {
                var p = box.Pitcher(pitcher);
                p.Outs = outs;
                p.EarnedRuns = er;
            }
            return box;
        }

        [Test]
        public void 경기마다_선수별_한_줄이_최신_먼저_쌓이고_10경기까지만_남는다()
        {
            var stats = new SeasonStatsTracker();
            for (int g = 1; g <= 12; g++) stats.Add(Box("kim", 4, g % 2, "oh", 3, g == 12 ? 1 : 0), $"5.{g}", "LG 트윈스");
            var log = stats.RecentOf("kim");
            Assert.That(log.Count, Is.EqualTo(SeasonStatsTracker.LogSize));
            Assert.That(log[0].Date, Is.EqualTo("5.12"), "최신 먼저");
            Assert.That(log[0].Opponent, Is.EqualTo("LG 트윈스"));
            Assert.That(log[0].LineText, Is.EqualTo("4타수 0안타"));
            Assert.That(log[1].LineText, Is.EqualTo("4타수 1안타"));
            Assert.That(log[0].Pitching, Is.Null, "kim은 투수 기록이 없다");
            Assert.That(stats.RecentOf("oh")[0].LineText, Is.EqualTo("1이닝 1자책"));
            Assert.That(stats.BatterOf("kim").AtBats, Is.EqualTo(48), "시즌 누적은 12경기 전부");
            Assert.That(stats.RecentOf("nobody"), Is.Empty);
            stats.Add(Box("kim", 4, 4));
            Assert.That(stats.RecentOf("kim")[0].Date, Is.EqualTo(""), "날짜를 모르면 빈 칸");
            stats.Reset();
            Assert.That(stats.RecentOf("kim"), Is.Empty);
        }

        [Test]
        public void 최근_폼은_최근_5경기를_시즌과_견줘_상승세_하락세를_보인다()
        {
            var stats = new SeasonStatsTracker();
            Assert.That(stats.FormText("kim", false), Is.Null, "기록 없음");
            for (int g = 0; g < 10; g++) stats.Add(Box("kim", 4, 0), "d" + g, "KT 위즈");       // 시즌 초반 무안타
            for (int g = 0; g < 5; g++) stats.Add(Box("kim", 4, 2), "e" + g, "KT 위즈");        // 최근 5경기 .500
            Assert.That(stats.FormText("kim", false), Is.EqualTo("최근 5경기 타율 .500 · 시즌 .167 ▲ 상승세"));
            for (int g = 0; g < 5; g++) stats.Add(Box("kim", 4, 0), "f" + g, "KT 위즈");
            Assert.That(stats.FormText("kim", false), Does.EndWith("▼ 하락세"));

            for (int g = 0; g < 3; g++) stats.Add(Box("x", 0, 0, "oh", 9, 1), "g" + g, "KT 위즈");
            Assert.That(stats.FormText("oh", true), Is.EqualTo("최근 3경기 평균자책 3.00 · 시즌 3.00 · 평소대로"));
        }

        [Test]
        public void 최근_출장_기록은_저장_파일로_되살아난다()
        {
            var s = SeasonState.CreateDummy();
            s.Stats.Add(Box("kim", 4, 1), "5.15 (금)", "LG 트윈스");
            s.Stats.Add(Box("kim", 3, 2, "oh", 18, 2), "5.16 (토)", "SSG 랜더스");
            var loaded = SeasonState.FromSaveData(s.ToSaveData());
            var log = loaded.Stats.RecentOf("kim");
            Assert.That(log.Select(g => g.Date), Is.EqualTo(new[] { "5.16 (토)", "5.15 (금)" }), "순서 유지 (최신 먼저)");
            Assert.That(log[0].LineText, Is.EqualTo("3타수 2안타"));
            Assert.That(loaded.Stats.RecentOf("oh")[0].Pitching.Outs, Is.EqualTo(18));
            Assert.That(loaded.Stats.RecentOf("oh")[0].Batting, Is.Null, "타석 없는 투수 줄은 타격 없음으로");

            var old = s.ToSaveData();
            old.GameLogs = null; // Phase 39 이전 저장 파일
            Assert.That(SeasonState.FromSaveData(old).Stats.RecentOf("kim"), Is.Empty);
        }

        // ---------- 헤더 축 분리 ----------

        [Test]
        public void 헤더는_카드_기준_데이터_연도를_게임_내_시즌과_따로_밝힌다()
        {
            Assert.That(LobbyController.DataBasisText(2024), Is.EqualTo("카드 기준 · 2024 KBO 기록"));
            Assert.That(LobbyController.DataBasisText(null), Is.EqualTo("카드 기준 · 불러오는 중"));
            Assert.That(LobbyController.DataBasisText(0), Is.EqualTo("카드 기준 · 불러오는 중"));
        }
    }
}
