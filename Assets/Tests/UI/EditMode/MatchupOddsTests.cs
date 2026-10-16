using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using DailyFranchise.UI.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>샘플 DB 선수 기록으로 승부처 작전 성공률이 계산되고 설명 문구가 만들어지는지 검증.</summary>
    public class MatchupOddsTests
    {
        static readonly MasterDatabase Db = TestLeague.TwoTeams;

        static ScenarioRosterSource Source(Matchup matchup) =>
            new ScenarioRosterSource(new DummyScenarioData(ScenarioCatalog.EighthBottomTactics, matchup), "SampleRoster.csv");

        [Test]
        public void 샘플_로스터에_확률_계산용_출루율과_피안타율이_있다()
        {
            Assert.That(Db.FindPlayer("WV003").OnBasePercentage, Is.EqualTo(0.391));
            Assert.That(Db.FindPlayer("BW010").OpponentAverage, Is.EqualTo(0.238));
            foreach (var p in Db.Players)
            {
                if (p.IsPitcher)
                {
                    Assert.That(p.Era, Is.GreaterThan(0), p.Name);
                    Assert.That(p.OpponentAverage, Is.InRange(0.150, 0.350), p.Name);
                }
                else
                {
                    Assert.That(p.BattingAverage, Is.InRange(0.200, 0.400), p.Name);
                    Assert.That(p.OnBasePercentage, Is.GreaterThan(p.BattingAverage), p.Name);
                }
            }
        }

        [Test]
        public void 서울전_승부처는_성지환_대_염태준_기록으로_계산한다()
        {
            var source = Source(Matchup.FromNames("LG 트윈스", "삼성 라이온즈", TeamSide.Home,
                Db.GetRoster("LG 트윈스"), Db.GetRoster("삼성 라이온즈")));
            var f = ScenarioCatalog.Find(ScenarioCatalog.EighthBottomTactics).Create();
            var calc = new MatchupCalculator(source);

            var ctx = calc.DecisionContext(f.Branch, f.Events);
            Assert.That((ctx.Batter.Name, ctx.Pitcher.Name), Is.EqualTo(("성지환", "염태준")));

            double swing = calc.TacticSuccessRate(f.Branch, f.Events, TacticKind.SwingAway);
            double squeeze = calc.TacticSuccessRate(f.Branch, f.Events, TacticKind.SqueezeBunt);
            Assert.That(swing, Is.Not.EqualTo(Fixture8thBottomTactics.SwingAwaySuccessRate), "고정 50%가 아님");
            Assert.That(squeeze, Is.Not.EqualTo(Fixture8thBottomTactics.SqueezeSuccessRate), "고정 60%가 아님");
            // Phase 17: 좌투 vs 좌타 플래툰 −5%와 성지환 득점권 클러치(.341 / .318)가 반영된다.
            Assert.That(TacticText.Odds(f.Branch[TacticKind.SwingAway], swing), Is.EqualTo("성공 확률 51% · 실패 49%"));
            Assert.That(TacticText.Odds(f.Branch[TacticKind.SqueezeBunt], squeeze), Is.EqualTo("성공 확률 60% · 실패 40%"));
            Assert.That(TacticText.Basis(ctx), Is.EqualTo("근거 · 성지환 타율 .318 vs 염태준 평균자책 3.21 · 득점권"));
            Assert.That(TacticText.Adjustments(calc.Adjustments(ctx)), Is.EqualTo("보정 · 플래툰 열세(좌투 vs 좌타) -5% · 득점권 클러치 +7%"));
        }

        [Test]
        public void 상대_선수단이_없는_원정_경기는_리그_평균_투수와_대결로_계산한다()
        {
            var source = Source(Matchup.FromNames("삼성 라이온즈", "SSG 랜더스", TeamSide.Away, Db.GetRoster("삼성 라이온즈"), null));
            var f = ScenarioMirror.Mirror(ScenarioCatalog.Find(ScenarioCatalog.EighthBottomTactics).Create());
            var calc = new MatchupCalculator(source);

            var ctx = calc.DecisionContext(f.Branch, f.Events);
            Assert.That((ctx.Batter.Name, ctx.Pitcher.Name), Is.EqualTo(("성지환", "리그 평균 투수")));
            double vsAverage = calc.TacticSuccessRate(f.Branch, f.Events, TacticKind.SwingAway);
            Assert.That(vsAverage, Is.GreaterThan(Fixture8thBottomTactics.SwingAwaySuccessRate), "좋은 타자 + 득점권");
        }

        [Test]
        public void 더미_데이터만_있으면_장면_기본_성공률과_기존_문구를_쓴다()
        {
            var f = ScenarioCatalog.Find(ScenarioCatalog.EighthBottomTactics).Create();
            IRosterSource dummy = new DummyScenarioData(ScenarioCatalog.EighthBottomTactics);
            var calc = new MatchupCalculator(dummy as IPlayerLookup); // 경기 화면과 같은 방식: 더미는 조회 불가 → null
            Assert.That(calc.DecisionContext(f.Branch, f.Events), Is.Null);
            Assert.That(TacticText.Basis(null), Is.Null);
            var squeeze = f.Branch[TacticKind.SqueezeBunt];
            Assert.That(TacticText.Odds(squeeze, calc.TacticSuccessRate(f.Branch, f.Events, TacticKind.SqueezeBunt)),
                Is.EqualTo(TacticText.Odds(squeeze)).And.EqualTo("성공 확률 60% · 실패 40%"));
        }

        [Test]
        public void 성공_실패_퍼센트는_합이_100이다()
        {
            var squeeze = Fixture8thBottomTactics.Create().Branch[TacticKind.SqueezeBunt];
            Assert.That(TacticText.Odds(squeeze, 0.6151), Is.EqualTo("성공 확률 62% · 실패 38%"));
            Assert.That(TacticText.Odds(squeeze, 0.4949), Is.EqualTo("성공 확률 49% · 실패 51%"));
            Assert.That(TacticText.Odds(Fixture8thBottomTactics.Create().Branch[TacticKind.Default], 0.3),
                Is.EqualTo("확률 판정 없음 · 원래 흐름대로 진행"));
        }
    }
}
