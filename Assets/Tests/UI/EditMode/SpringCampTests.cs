using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;
using UnityEngine;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 33: 캠프 포인트(결산 지급·이월·저장), 특별 훈련(24세 이하·내 선수·CP·상한), 스프링캠프 리포트.</summary>
    public class SpringCampTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;
        const string Samsung = "삼성 라이온즈";

        static string IdOf(string name) => Real.Players.First(p => p.Name == name).Id;

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Real, s.Signings, s.Progression, s.Rookies);

        static void FinishSeason(SeasonState s)
        {
            var random = new SeededRandom(33);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(3, 2), random);
            }
        }

        // ---------- 캠프 포인트 ----------

        [Test]
        public void 결산마다_CP_5를_주고_쓰지_않으면_이월하며_저장된다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.CampPoints, Is.Zero);
            FinishSeason(s);
            var summary = s.TransitionToNextSeason();
            Assert.That(s.CampPoints, Is.EqualTo(SpringCamp.PointsPerSeason));
            Assert.That((summary.Camp.PointsGranted, summary.Camp.PointsTotal, summary.Camp.Year), Is.EqualTo((5, 5, 2027)));
            FinishSeason(s);
            s.TransitionToNextSeason();
            Assert.That(s.CampPoints, Is.EqualTo(10), "이월");

            var loaded = SeasonState.FromSaveData(JsonUtility.FromJson<SeasonSaveData>(JsonUtility.ToJson(s.ToSaveData())));
            Assert.That(loaded.CampPoints, Is.EqualTo(10));
            var old = s.ToSaveData();
            old.CampPoints = 0;
            Assert.That(SeasonState.FromSaveData(old).CampPoints, Is.Zero, "필드가 없는 이전 파일");
            old.CampPoints = -3;
            Assert.That(SeasonState.FromSaveData(old).CampPoints, Is.Zero, "음수는 0");
            Assert.That(s.SpendCampPoints(11), Is.False);
            Assert.That(s.CampPoints, Is.EqualTo(10));
        }

        // ---------- 특별 훈련 ----------

        [Test]
        public void 특별_훈련은_CP_1로_폼을_1퍼센트포인트_올리고_마스터_데이터는_그대로다()
        {
            var s = SeasonState.CreateDummy();
            string young = IdOf("김영웅"); // 20세 3B
            Assert.That(SpringCamp.Check(s, Book(s), young), Is.EqualTo(TrainingBlock.NoPoints));
            s.SetCampPoints(3);
            Assert.That(SpringCamp.Check(s, Book(s), young), Is.EqualTo(TrainingBlock.None));

            var o = SpringCamp.Train(s, Book(s), young);
            Assert.That((o.Trained, o.FormBefore, o.PointsLeft), Is.EqualTo((true, 0.0, 2)));
            Assert.That(o.FormAfter, Is.EqualTo(0.01).Within(1e-12));
            Assert.That(s.CampPoints, Is.EqualTo(2));
            var p = s.ProgressionOf(young);
            Assert.That((p.AgeDelta, p.Retired), Is.EqualTo((0, false)), "나이·은퇴는 그대로");
            Assert.That(Book(s).Find(young).player.Form, Is.EqualTo(0.01).Within(1e-12), "장부 사본에 덧씌운다");
            Assert.That(Real.FindPlayer(young).Form, Is.Zero, "마스터 DB 불변");

            SpringCamp.Train(s, Book(s), young);
            Assert.That(s.ProgressionOf(young).Form, Is.EqualTo(0.02).Within(1e-12));
        }

        [Test]
        public void 훈련은_24세_이하_내_선수만_CP가_있고_상한_전일_때만_된다()
        {
            var s = SeasonState.CreateDummy();
            s.SetCampPoints(5);
            Assert.That(SpringCamp.Check(s, Book(s), IdOf("구자욱")), Is.EqualTo(TrainingBlock.TooOld), "31세");
            Assert.That(SpringCamp.Check(s, Book(s), IdOf("이병헌")), Is.EqualTo(TrainingBlock.None), "24세 벤치");
            Assert.That(SpringCamp.Check(s, Book(s), IdOf("김호진")), Is.EqualTo(TrainingBlock.None), "18세 퓨처스");
            Assert.That(SpringCamp.Check(s, Book(s), "nobody"), Is.EqualTo(TrainingBlock.NotMine));
            Assert.That(SpringCamp.Check(s, Book(s), null), Is.EqualTo(TrainingBlock.NotMine));

            string young = IdOf("김영웅");
            s.SetProgression(new PlayerProgression(young, 1, 0.095, false, 0));
            var o = SpringCamp.Train(s, Book(s), young);
            Assert.That(o.FormAfter, Is.EqualTo(ProgressionModifier.MaxForm).Within(1e-12), "상한에서 자른다");
            Assert.That(s.ProgressionOf(young).AgeDelta, Is.EqualTo(1), "에이징 나이 증가 유지");
            Assert.That(SpringCamp.Check(s, Book(s), young), Is.EqualTo(TrainingBlock.MaxForm));
            int cp = s.CampPoints;
            Assert.That(SpringCamp.Train(s, Book(s), young).Trained, Is.False);
            Assert.That(s.CampPoints, Is.EqualTo(cp), "막히면 CP를 쓰지 않는다");

            s.StartNextGame();
            Assert.That(SpringCamp.Check(s, Book(s), IdOf("이병헌")), Is.EqualTo(TrainingBlock.InGame));
            foreach (TrainingBlock b in System.Enum.GetValues(typeof(TrainingBlock)))
                Assert.That(SpringCamp.BlockText(b), b == TrainingBlock.None ? Is.Empty : Is.Not.Empty, b.ToString());
        }

        [Test]
        public void 결산_뒤_퓨처스_신인도_훈련할_수_있다()
        {
            var s = SeasonState.CreateDummy();
            FinishSeason(s);
            s.TransitionToNextSeason(Book(s), new SeededRandom(2027));
            var rookie = s.Rookies.First(r => r.Team == Samsung); // Phase 40.5: 타 구단도 지명하므로 내 구단 신인
            Assert.That(Book(s).Get(Samsung).Reserves.Any(p => p.Id == rookie.Id), Is.True);
            var o = SpringCamp.Train(s, Book(s), rookie.Id);
            Assert.That(o.Trained, Is.True);
            Assert.That(Book(s).Get(Samsung).Reserves.Single(p => p.Id == rookie.Id).Form, Is.EqualTo(0.01).Within(1e-12));
            Assert.That(s.CampPoints, Is.EqualTo(SpringCamp.PointsPerSeason - 1));
        }

        // ---------- 리포트 ----------

        [Test]
        public void 스프링캠프_리포트는_내_구단의_큰_성장_베테랑_하락_은퇴_신인을_담는다()
        {
            var s = SeasonState.CreateDummy();
            FinishSeason(s);
            var summary = s.TransitionToNextSeason(Book(s), new SeededRandom(2027));
            var camp = summary.Camp;
            var mine = summary.Aging.FormChanges.Where(c => c.Team == Samsung).ToList();
            Assert.That(mine, Is.Not.Empty);

            Assert.That(camp.Growth.Select(g => g.Player.Id),
                Is.EquivalentTo(mine.Where(c => c.Delta >= SpringCampReport.GrowthThreshold - 1e-9).Select(c => c.Player.Id)));
            Assert.That(camp.Growth.Select(g => g.Delta), Is.Ordered.Descending);
            Assert.That(camp.Growth.All(g => g.Player.Age <= AgingCurve.ProspectMaxAge), Is.True, "+1.5%p 이상은 유망주 구간에서만 나온다");
            Assert.That(camp.Decline.All(d => d.Player.Age >= 32 && d.Delta <= -SpringCampReport.DeclineThreshold + 1e-9), Is.True);
            Assert.That(camp.Decline.Select(d => d.Delta), Is.Ordered);
            Assert.That(camp.Decline, Is.Not.Empty, "삼성은 베테랑이 많다");
            Assert.That(camp.Retired.Select(r => r.Id), Is.EquivalentTo(summary.Aging.Retired.Where(r => r.Team == Samsung).Select(r => r.Player.Id)));
            Assert.That(camp.Rookies.Select(r => r.Player.Id), Is.EqualTo(summary.Draft.MyPicks.Select(r => r.Player.Id)));
            Assert.That(summary.Aging.FormChanges.Any(c => summary.Aging.Retired.Any(r => r.Player.Id == c.Player.Id)), Is.False, "은퇴 선수는 폼 변화 목록에 없다");

            Assert.That(SpringCampReport.DeltaText(0.024), Is.EqualTo("+2.4%p"));
            Assert.That(SpringCampReport.DeltaText(-0.015), Is.EqualTo("−1.5%p"));
        }
    }
}
