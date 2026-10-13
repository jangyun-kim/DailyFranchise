using System;
using System.Linq;
using DailyFranchise.App;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    public class SeasonStateTests
    {
        [Test]
        public void 더미_시즌은_내_구단_경기_일정과_10개_구단_순위를_가진다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.Schedule, Has.Count.EqualTo(10));
            Assert.That(s.Schedule.All(g => g.AwayTeam == s.MyTeam || g.HomeTeam == s.MyTeam), Is.True);
            Assert.That(s.Standings.Select(r => r.Team), Is.Unique);
            Assert.That(s.Standings, Has.Count.EqualTo(10));
            Assert.That(s.Standings.Select(r => r.Rank), Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(s.MyStanding, Is.Not.Null);
            Assert.That(s.Schedule.Select(g => g.Number), Is.EqualTo(Enumerable.Range(1, 10)));
        }

        [Test]
        public void 첫_경기는_경기_화면_더미와_같은_매치업()
        {
            var first = SeasonState.CreateDummy().NextGame;
            Assert.That((first.AwayTeam, first.HomeTeam), Is.EqualTo(("서울 블루윙스", "부산 웨이브스")));
        }

        [Test]
        public void 경기_시작과_종료로_다음_경기로_넘어간다()
        {
            var s = SeasonState.CreateDummy();
            var g1 = s.StartNextGame();
            Assert.That(g1.Number, Is.EqualTo(1));
            Assert.That(g1.Status, Is.EqualTo(GameStatus.InProgress));
            Assert.That(s.InProgress, Is.SameAs(g1));
            Assert.Throws<InvalidOperationException>(() => s.StartNextGame(), "진행 중에 또 시작할 수 없음");

            s.FinishCurrentGame();
            Assert.That(g1.Status, Is.EqualTo(GameStatus.Final));
            Assert.That(s.InProgress, Is.Null);
            Assert.That(s.GamesPlayed, Is.EqualTo(1));
            Assert.That(s.NextGame.Number, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => s.FinishCurrentGame(), "진행 중인 경기 없이 종료 불가");
        }

        [Test]
        public void 모든_경기를_마치면_시즌이_끝난다()
        {
            var s = SeasonState.CreateDummy();
            for (int i = 0; i < s.Schedule.Count; i++)
            {
                Assert.That(s.IsSeasonOver, Is.False);
                s.StartNextGame();
                s.FinishCurrentGame();
            }
            Assert.That(s.IsSeasonOver, Is.True);
            Assert.That(s.NextGame, Is.Null);
            Assert.Throws<InvalidOperationException>(() => s.StartNextGame());
        }

        [Test]
        public void 승차는_1위_기준으로_계산된다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.Standings[0].GamesBehind, Is.EqualTo("—"));
            // 1위 26-15, 3위 23-18 → ((26-23) + (18-15)) / 2 = 3.0
            Assert.That(s.Standings[2].GamesBehind, Is.EqualTo("3.0"));
        }
    }
}
