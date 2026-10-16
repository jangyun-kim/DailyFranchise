using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.Sim;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// 경기 종료 후 시즌 진행 (Phase 15): 승/패/무 판정 → 타 구단 결과 → 순위 재정렬(IComparer 타이브레이커) →
    /// 일정 전진 → 즉시 저장. 타 구단 결과 난수는 고정 Seed만 쓰고, 저장은 임시 경로에만 한다.
    /// </summary>
    public class SeasonProgressionTests
    {
        const int Seed = 20261015;

        string _dir;
        SaveManager _saves;
        List<string> _warnings;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "DailyFranchiseProgressionTests_" + Guid.NewGuid().ToString("N"));
            _warnings = new List<string>();
            _saves = new SaveManager(Path.Combine(_dir, SaveManager.FileName), _warnings.Add);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        static PostGameReport Play(SeasonState s, GameScore score, int seed, SaveManager saves)
        {
            s.StartNextGame();
            return PostGameProcessor.Process(s, score, new SeededRandom(seed), saves);
        }

        // ───────── 승/패/무 판정과 승률 ─────────

        [TestCase(2, 5, GameOutcome.Win)]   // 1경기 부산 홈: 원정 2 : 홈 5
        [TestCase(6, 1, GameOutcome.Loss)]
        [TestCase(4, 4, GameOutcome.Draw)]
        public void 최종_점수로_승패무를_판정해_양_팀_성적과_승률을_갱신한다(int away, int home, GameOutcome expected)
        {
            var s = SeasonState.CreateDummy();
            var me = s.MyStanding;          // 23승 18패 0무
            var opp = s.StandingOf("LG 트윈스"); // 24승 16패 2무

            var report = Play(s, new GameScore(away, home), Seed, _saves);

            Assert.That(report.MyOutcome, Is.EqualTo(expected));
            var (mine, theirs) = expected == GameOutcome.Win ? ((24, 18, 0), (24, 17, 2))
                : expected == GameOutcome.Loss ? ((23, 19, 0), (25, 16, 2))
                : ((23, 18, 1), (24, 16, 3));
            Assert.That((me.Wins, me.Losses, me.Draws), Is.EqualTo(mine));
            Assert.That((opp.Wins, opp.Losses, opp.Draws), Is.EqualTo(theirs));
            Assert.That(me.WinPct, Is.EqualTo((double)me.Wins / (me.Wins + me.Losses)).Within(1e-12), "승률 = 승 / (승 + 패)");
            Assert.That(s.Schedule[0].Status, Is.EqualTo(GameStatus.Final));
            Assert.That(s.Schedule[0].Score, Is.EqualTo(new GameScore(away, home)));
        }

        [Test]
        public void 승률은_무승부를_빼고_계산해_표기한다()
        {
            Assert.That(new StandingRow { Wins = 24, Losses = 18 }.WinPctText, Is.EqualTo("0.571"));
            Assert.That(new StandingRow { Wins = 23, Losses = 18, Draws = 1 }.WinPctText, Is.EqualTo("0.561"));
            Assert.That(new StandingRow { Draws = 3 }.WinPct, Is.Zero);
        }

        // ───────── 일정 전진 ─────────

        [Test]
        public void 경기가_끝나면_일정이_다음_경기로_전진한다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.NextGame.Number, Is.EqualTo(1));

            Play(s, new GameScore(1, 3), Seed, _saves);
            Assert.That((s.NextIndex, s.NextGame.Number, s.InProgress), Is.EqualTo((1, 2, (ScheduledGame)null)));

            Play(s, new GameScore(3, 2), Seed + 1, _saves);
            Assert.That(s.NextGame.Number, Is.EqualTo(3), "두 경기 뒤 다음 경기는 3경기");
            Assert.That(s.LastFinished.Number, Is.EqualTo(2));
        }

        [Test]
        public void 마지막_경기가_끝나면_시즌이_끝나고_더_진행하지_않는다()
        {
            var s = SeasonState.CreateDummy();
            for (int i = 0; i < s.Schedule.Count; i++) Play(s, new GameScore(1, 2), Seed + i, null);
            Assert.That(s.IsSeasonOver, Is.True);
            Assert.That(s.NextGame, Is.Null);
            Assert.Throws<InvalidOperationException>(() => PostGameProcessor.Process(s, new GameScore(0, 0), new SeededRandom(Seed), null));
        }

        // ───────── 타 구단 결과 ─────────

        [Test]
        public void 타_구단도_같은_날_한_경기씩_치러_모든_구단의_경기수가_늘어난다()
        {
            var s = SeasonState.CreateDummy();
            var before = s.Standings.ToDictionary(r => r.Team, r => r.GamesPlayed);

            var report = Play(s, new GameScore(2, 5), Seed, _saves);

            Assert.That(report.DayResults, Has.Count.EqualTo(5), "10개 구단 → 5경기");
            Assert.That(report.DayResults.SelectMany(r => new[] { r.AwayTeam, r.HomeTeam }), Is.Unique);
            foreach (var row in s.Standings)
                Assert.That(row.GamesPlayed, Is.EqualTo(before[row.Team] + 1), row.Team);
            Assert.That(s.Standings.Sum(r => r.Wins), Is.EqualTo(s.Standings.Sum(r => r.Losses)), "승 합 = 패 합");
        }

        [Test]
        public void 시즌을_진행해도_내_구단만_오르지_않고_순위표는_항상_정렬돼_있다()
        {
            var s = SeasonState.CreateDummy();
            for (int i = 0; i < s.Schedule.Count; i++)
            {
                Play(s, s.Schedule[i].HomeTeam == s.MyTeam ? new GameScore(0, 9) : new GameScore(9, 0), Seed + i, null);
                for (int k = 1; k < s.Standings.Count; k++)
                {
                    Assert.That(SeasonState.StandingOrder.Compare(s.Standings[k - 1], s.Standings[k]), Is.LessThanOrEqualTo(0), $"{i + 1}경기 후 {k}번째");
                    Assert.That(s.Standings[k].Rank, Is.GreaterThanOrEqualTo(s.Standings[k - 1].Rank));
                }
            }
            // 내 구단이 10연승해도 타 구단 승수도 늘어난다.
            Assert.That(s.Standings.Where(r => r.Team != s.MyTeam).Sum(r => r.Wins), Is.GreaterThan(26 + 24 + 22 + 21 + 20 + 19 + 18 + 17 + 15));
        }

        // ───────── 순위 변동 ─────────

        [Test]
        public void 반영_직전_순위가_남아_순위_변동을_표시한다()
        {
            var standings = new List<StandingRow>
            {
                new StandingRow { Team = "상대", Wins = 11, Losses = 9 },  // .550 1위
                new StandingRow { Team = "나", Wins = 10, Losses = 9 },    // .526 2위
                new StandingRow { Team = "쉬는팀", Wins = 9, Losses = 10 },
            };
            var schedule = new List<ScheduledGame> { new ScheduledGame { Number = 1, AwayTeam = "상대", HomeTeam = "나" } };
            var s = new SeasonState("t", "나", schedule, standings);
            Assert.That(s.MyStanding.PreviousRank, Is.Zero);
            Assert.That(s.MyStanding.RankChangeText, Is.Empty, "첫 경기 전에는 변동 표시가 없다");

            var report = Play(s, new GameScore(1, 4), Seed, null); // 나 11-9 .550, 상대 11-10 .524

            Assert.That((report.RankBefore, report.RankAfter), Is.EqualTo((2, 1)));
            Assert.That((s.MyStanding.PreviousRank, s.MyStanding.RankChange, s.MyStanding.RankChangeText), Is.EqualTo((2, 1, "+1")));
            var opp = s.StandingOf("상대");
            Assert.That((opp.RankChange, opp.RankChangeText), Is.EqualTo((-1, "-1")));
            Assert.That(s.StandingOf("쉬는팀").RankChangeText, Is.Empty, "순위 그대로면 빈칸");
            Assert.That(LobbyController.LastResultText(s), Is.EqualTo("경기 1 · 상대 vs 나 · 승 1:4 · 2위 → 1위"));
        }

        [Test]
        public void 지난_경기_요약은_치른_경기가_없으면_대시()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(LobbyController.LastResultText(s), Is.EqualTo("—"));
            Play(s, new GameScore(3, 3), Seed, null);
            Assert.That(LobbyController.LastResultText(s), Does.StartWith("경기 1 · LG 트윈스 vs 삼성 라이온즈 · 무 3:3 · "));
        }

        // ───────── IComparer 타이브레이커 ─────────

        [Test]
        public void 타이브레이커는_승률_다승_무승부_잔여경기_순이다()
        {
            var cmp = new StandingComparer(144);
            var pct = new StandingRow { Wins = 6, Losses = 4 };
            var lowPct = new StandingRow { Wins = 50, Losses = 50 };
            Assert.That(cmp.Compare(pct, lowPct), Is.LessThan(0), "승률 높은 쪽이 위");

            var moreWins = new StandingRow { Wins = 20, Losses = 20 };
            var fewerWins = new StandingRow { Wins = 10, Losses = 10 };
            Assert.That(cmp.Compare(moreWins, fewerWins), Is.LessThan(0), "승률 같으면 다승");

            var moreDraws = new StandingRow { Wins = 10, Losses = 10, Draws = 2 };
            Assert.That(cmp.Compare(moreDraws, fewerWins), Is.LessThan(0), "승률·다승 같으면 무승부 많은 쪽");

            // 승률·다승·무승부가 같고 경기 수가 다른 경우는 0승일 때만 생긴다 → 잔여 경기 많은 쪽(덜 진 쪽)이 위.
            var winless = new StandingRow { Wins = 0, Losses = 3 };
            var winlessMore = new StandingRow { Wins = 0, Losses = 5 };
            Assert.That((cmp.RemainingGames(winless), cmp.RemainingGames(winlessMore)), Is.EqualTo((141, 139)));
            Assert.That(cmp.Compare(winless, winlessMore), Is.LessThan(0), "잔여 경기 많은 쪽");

            Assert.That(cmp.Compare(new StandingRow { Wins = 3, Losses = 2 }, new StandingRow { Wins = 3, Losses = 2 }), Is.Zero, "모두 같으면 0");
            Assert.That(cmp.Compare(pct, null), Is.LessThan(0), "null은 맨 뒤");
            Assert.Throws<ArgumentOutOfRangeException>(() => new StandingComparer(0));
        }

        [Test]
        public void 시즌_순위표는_IComparer로_정렬되고_완전_동률은_공동_순위로_기존_순서를_지킨다()
        {
            var standings = new List<StandingRow>
            {
                new StandingRow { Team = "갑", Wins = 0, Losses = 5 },
                new StandingRow { Team = "을", Wins = 5, Losses = 5 },
                new StandingRow { Team = "병", Wins = 5, Losses = 5, Draws = 1 },
                new StandingRow { Team = "정", Wins = 0, Losses = 3 },
                new StandingRow { Team = "무", Wins = 5, Losses = 5 },
            };
            var s = new SeasonState("t", "을", new List<ScheduledGame> { new ScheduledGame { Number = 1, AwayTeam = "을", HomeTeam = "병" } }, standings);
            Assert.That(s.Standings.Select(r => r.Team), Is.EqualTo(new[] { "병", "을", "무", "정", "갑" }));
            Assert.That(s.Standings.Select(r => r.Rank), Is.EqualTo(new[] { 1, 2, 2, 4, 5 }));
        }

        // ───────── 즉시 저장 ─────────

        [Test]
        public void 경기_결과는_로비_복귀_전에_바로_저장돼_다시_불러와도_같다()
        {
            var s = SeasonState.CreateDummy();
            var report = Play(s, new GameScore(2, 5), Seed, _saves);

            Assert.That(report.Saved, Is.True);
            Assert.That(_saves.HasSave, Is.True);
            var loaded = _saves.LoadOrCreate(() => throw new AssertionException("새 시즌을 만들면 안 된다"), out var status);

            Assert.That(status, Is.EqualTo(SeasonLoadStatus.Loaded));
            Assert.That(loaded.NextGame.Number, Is.EqualTo(2), "전진한 일정이 저장된다");
            Assert.That(loaded.Schedule[0].Score, Is.EqualTo(new GameScore(2, 5)));
            Assert.That(loaded.Standings.Select(r => (r.Rank, r.Team, r.Wins, r.Losses, r.Draws, r.PreviousRank)),
                Is.EqualTo(s.Standings.Select(r => (r.Rank, r.Team, r.Wins, r.Losses, r.Draws, r.PreviousRank))), "순위표·직전 순위까지 같다");
            Assert.That(_warnings, Is.Empty);
        }

        [Test]
        public void 직전_순위가_없는_이전_저장_파일도_읽힌다()
        {
            var s = SeasonState.CreateDummy();
            Play(s, new GameScore(2, 5), Seed, null);
            string json = SaveManager.ToJson(s);
            string legacy = System.Text.RegularExpressions.Regex.Replace(json, @",\s*""PreviousRank"":\s*\d+", "");
            Assert.That(legacy, Does.Not.Contain("PreviousRank"));

            var loaded = SaveManager.FromJson(legacy);
            Assert.That(loaded.Standings.All(r => r.PreviousRank == 0 && r.RankChangeText == ""), Is.True);
            Assert.That(loaded.NextGame.Number, Is.EqualTo(2));
        }

        [Test]
        public void 직전_순위가_범위를_벗어난_저장_파일은_손상으로_본다()
        {
            var data = SeasonState.CreateDummy().ToSaveData();
            data.Standings[0].PreviousRank = 11;
            Assert.Throws<FormatException>(() => SeasonState.FromSaveData(data));
            data.Standings[0].PreviousRank = -1;
            Assert.Throws<FormatException>(() => SeasonState.FromSaveData(data));
        }
    }
}
