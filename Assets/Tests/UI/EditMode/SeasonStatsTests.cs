using System.Collections.Generic;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using DailyFranchise.Sim.Fixtures;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 37: 시즌 개인 성적 — 박스스코어(장면 타석 그대로 · 퀵 시뮬 구간 배분 · 승패세이브), 시즌 누적(SeasonStatsTracker),
    /// 0으로 나누기 방어, 결산 반영(한국시리즈 제외)·시즌 넘김 초기화, 저장/불러오기·이전 파일, 팀 내 기록 1위.
    /// </summary>
    public class SeasonStatsTests
    {
        static readonly MasterDatabase Db = TestLeague.TwoTeams;
        const string Samsung = TestLeague.Samsung;

        /// <summary>타석을 끝낸 투구 하나.</summary>
        static PitchEvent Pa(int inning, HalfInning half, string pitcher, string batter, PlateAppearanceOutcome outcome,
            PlayType play = PlayType.None, int runs = 0, int outsBefore = 0, int outsAfter = -1) => new PitchEvent
        {
            Inning = inning, HalfInning = half, PitcherId = pitcher, BatterId = batter, Outcome = outcome, PlayType = play,
            RunsScored = runs, OutsBefore = outsBefore,
            OutsAfter = outsAfter >= 0 ? outsAfter : outcome == PlateAppearanceOutcome.Strikeout || play == PlayType.GroundOut || play == PlayType.FlyOut ? outsBefore + 1 : outsBefore,
        };

        /// <summary>그 반 이닝 삼자범퇴 (세 타자 삼진).</summary>
        static IEnumerable<PitchEvent> Side(int inning, HalfInning half, string pitcher, string prefix, int firstOrder)
        {
            for (int i = 0; i < 3; i++)
                yield return Pa(inning, half, pitcher, prefix + "b" + ((firstOrder + i - 1) % 9 + 1), PlateAppearanceOutcome.Strikeout, outsBefore: i);
        }

        // ---------- 박스스코어: 장면 타석 ----------

        [Test]
        public void 홈런을_친_타자는_홈런_안타_타점이_오르고_리드를_지킨_선발이_승리_상대_선발이_패전()
        {
            var path = new List<PitchEvent>();
            path.AddRange(Side(1, HalfInning.Top, "home_sp", "away_", 1));
            path.Add(Pa(1, HalfInning.Bottom, "away_sp", "home_b1", PlateAppearanceOutcome.InPlay, PlayType.HomeRun, runs: 1));
            path.AddRange(Side(1, HalfInning.Bottom, "away_sp", "home_", 2));
            path.AddRange(Side(2, HalfInning.Top, "home_sp", "away_", 4));

            var box = BoxScoreBuilder.Build(null, path, null, null);
            var hr = box.Batting["home_b1"];
            Assert.That((hr.PlateAppearances, hr.AtBats, hr.Hits, hr.HomeRuns, hr.RunsBattedIn, hr.Games), Is.EqualTo((1, 1, 1, 1, 1, 1)));
            Assert.That(box.Batting["home_b2"].Strikeouts, Is.EqualTo(1));
            Assert.That((box.WinningPitcher, box.LosingPitcher, box.SavePitcher), Is.EqualTo(("home_sp", "away_sp", (string)null)));
            var win = box.Pitching["home_sp"];
            Assert.That((win.Wins, win.Losses, win.Outs, win.Strikeouts, win.EarnedRuns, win.GamesStarted), Is.EqualTo((1, 0, 6, 6, 0, 1)),
                "다른 투수가 없으면 5이닝 미만이어도 선발이 승리");
            var loss = box.Pitching["away_sp"];
            Assert.That((loss.Losses, loss.HitsAllowed, loss.EarnedRuns, loss.Outs), Is.EqualTo((1, 1, 1, 3)));
        }

        [Test]
        public void 볼넷_희생번트는_타수가_아니고_2루타_3루타를_구분한다()
        {
            var path = new List<PitchEvent>
            {
                Pa(1, HalfInning.Bottom, "away_sp", "home_b1", PlateAppearanceOutcome.Walk),
                Pa(1, HalfInning.Bottom, "away_sp", "home_b2", PlateAppearanceOutcome.InPlay, PlayType.SacrificeBunt, outsAfter: 1),
                Pa(1, HalfInning.Bottom, "away_sp", "home_b3", PlateAppearanceOutcome.InPlay, PlayType.Double, runs: 1, outsBefore: 1),
                Pa(1, HalfInning.Bottom, "away_sp", "home_b4", PlateAppearanceOutcome.InPlay, PlayType.Triple, runs: 1, outsBefore: 1),
                Pa(1, HalfInning.Bottom, "away_sp", "home_b5", PlateAppearanceOutcome.InPlay, PlayType.GroundOut, outsBefore: 1),
            };
            var box = BoxScoreBuilder.Build(null, path, null, null);
            var b1 = box.Batting["home_b1"];
            Assert.That((b1.PlateAppearances, b1.AtBats, b1.Walks), Is.EqualTo((1, 0, 1)));
            var b2 = box.Batting["home_b2"];
            Assert.That((b2.PlateAppearances, b2.AtBats), Is.EqualTo((1, 0)), "희생번트는 타수가 아니다");
            Assert.That((box.Batting["home_b3"].Doubles, box.Batting["home_b3"].RunsBattedIn), Is.EqualTo((1, 1)));
            Assert.That((box.Batting["home_b4"].Triples, box.Batting["home_b4"].Hits), Is.EqualTo((1, 1)));
            Assert.That((box.Batting["home_b5"].AtBats, box.Batting["home_b5"].Hits), Is.EqualTo((1, 0)));
            var p = box.Pitching["away_sp"];
            Assert.That((p.Outs, p.WalksAllowed, p.HitsAllowed, p.EarnedRuns), Is.EqualTo((2, 1, 2, 2)), "희생번트·땅볼 2아웃");
        }

        [Test]
        public void 선발이_5이닝을_못_채우면_첫_구원이_승리하고_3점_차_이내에_올라와_막은_마무리는_세이브()
        {
            var path = new List<PitchEvent>();
            path.AddRange(Side(1, HalfInning.Top, "home_sp", "away_", 1));
            path.Add(Pa(1, HalfInning.Bottom, "away_sp", "home_b1", PlateAppearanceOutcome.InPlay, PlayType.HomeRun, runs: 1));
            path.AddRange(Side(1, HalfInning.Bottom, "away_sp", "home_", 2));
            path.AddRange(Side(2, HalfInning.Top, "home_sp", "away_", 4));   // 선발 6아웃에서 강판
            path.AddRange(Side(2, HalfInning.Bottom, "away_sp", "home_", 5));
            path.AddRange(Side(3, HalfInning.Top, "home_rp2", "away_", 7));  // 첫 구원
            path.AddRange(Side(3, HalfInning.Bottom, "away_rp1", "home_", 8));
            path.AddRange(Side(4, HalfInning.Top, "home_rp1", "away_", 1));  // 1점 차 마무리

            var box = BoxScoreBuilder.Build(null, path, null, null);
            Assert.That((box.WinningPitcher, box.LosingPitcher, box.SavePitcher), Is.EqualTo(("home_rp2", "away_sp", "home_rp1")));
            Assert.That((box.Pitching["home_sp"].Wins, box.Pitching["home_rp2"].Wins, box.Pitching["home_rp1"].Saves), Is.EqualTo((0, 1, 1)));
            Assert.That((box.Pitching["home_rp1"].GamesStarted, box.Pitching["home_sp"].GamesStarted), Is.EqualTo((0, 1)));
        }

        [Test]
        public void 역전당하면_마지막으로_앞서게_한_점수의_투수끼리_승패가_갈리고_동점_경기는_승패가_없다()
        {
            var path = new List<PitchEvent>
            {
                Pa(1, HalfInning.Top, "home_sp", "away_b1", PlateAppearanceOutcome.InPlay, PlayType.HomeRun, runs: 1),   // 원정 1:0
                Pa(1, HalfInning.Bottom, "away_sp", "home_b1", PlateAppearanceOutcome.InPlay, PlayType.HomeRun, runs: 2), // 내 구단 2:1 역전
                Pa(2, HalfInning.Top, "home_rp1", "away_b2", PlateAppearanceOutcome.InPlay, PlayType.HomeRun, runs: 2),  // 원정 3:2 재역전
            };
            var box = BoxScoreBuilder.Build(null, path, null, null);
            Assert.That((box.WinningPitcher, box.LosingPitcher), Is.EqualTo(("away_sp", "home_rp1")));

            var tie = BoxScoreBuilder.Build(null, path.Take(1).Concat(new[]
            {
                Pa(1, HalfInning.Bottom, "away_sp", "home_b1", PlateAppearanceOutcome.InPlay, PlayType.HomeRun, runs: 1),
            }).ToList(), null, null);
            Assert.That((tie.WinningPitcher, tie.LosingPitcher, tie.SavePitcher), Is.EqualTo(((string)null, (string)null, (string)null)));
            Assert.That(tie.Pitching.Values.Sum(p => p.Wins + p.Losses), Is.Zero);
        }

        // ---------- 박스스코어: 퀵 시뮬레이션 구간 ----------

        [Test]
        public void 퀵_시뮬레이션_구간은_반_이닝의_안타_볼넷_득점과_아웃_수를_그대로_지키며_타순대로_나눈다()
        {
            var book = LeagueRosterBook.Build(Db, null);
            var mine = book.Get(Samsung);
            var lg = book.Get(TestLeague.Lg);
            for (int seed = 1; seed <= 30; seed++)
            {
                var game = FixtureGenerator.Generate(lg, mine, new SeededRandom(seed));
                PlayerData Resolve(string id) => id.StartsWith("home_b") ? mine.AtOrder(int.Parse(id.Substring(6)))
                    : id.StartsWith("away_b") ? lg.AtOrder(int.Parse(id.Substring(6)))
                    : id == "home_sp" ? mine.StartingPitcher : id == "away_sp" ? lg.StartingPitcher : null;
                var box = BoxScoreBuilder.Build(game, null, Resolve, new SeededRandom(BoxScoreBuilder.SeedFor(game)));

                var away = box.Batting.Where(kv => kv.Key.StartsWith("away_")).Select(kv => kv.Value).ToList();
                var home = box.Batting.Where(kv => kv.Key.StartsWith("home_")).Select(kv => kv.Value).ToList();
                Assert.That(away.Sum(b => b.Hits), Is.EqualTo(game.AwayHits), $"seed {seed} 원정 안타");
                Assert.That(home.Sum(b => b.Hits), Is.EqualTo(game.HomeHits), $"seed {seed} 홈 안타");
                Assert.That(away.Sum(b => b.Walks), Is.EqualTo(game.AwayWalks));
                Assert.That(home.Sum(b => b.Walks), Is.EqualTo(game.HomeWalks));
                Assert.That(away.Sum(b => b.RunsBattedIn), Is.EqualTo(game.Fixture.AwayScoreBefore), "타점 합 = 득점");
                Assert.That(home.Sum(b => b.RunsBattedIn), Is.EqualTo(game.Fixture.HomeScoreBefore));
                Assert.That(away.Sum(b => b.HomeRuns), Is.LessThanOrEqualTo(game.Fixture.AwayScoreBefore));
                foreach (var b in away.Concat(home))
                {
                    Assert.That(b.AtBats + b.Walks, Is.EqualTo(b.PlateAppearances), "희생 없음: 타석 = 타수 + 볼넷");
                    Assert.That(b.Doubles + b.Triples + b.HomeRuns, Is.LessThanOrEqualTo(b.Hits));
                }
                Assert.That(box.Pitching["home_sp"].Outs, Is.EqualTo(3 * (FixtureGenerator.SimulatedInnings + 1)), "1~8회초는 내 선발");
                Assert.That(box.Pitching["away_sp"].Outs, Is.EqualTo(3 * FixtureGenerator.SimulatedInnings));
                Assert.That(box.Pitching["home_sp"].EarnedRuns, Is.EqualTo(game.Fixture.AwayScoreBefore));
                Assert.That(home.Sum(b => b.PlateAppearances), Is.EqualTo(21 + game.HomeHits + game.HomeWalks + game.AwayErrors),
                    "아웃 21 + 출루 (안타·볼넷·상대 실책)");
                // 타순은 이어진다: 1번 타자 타석 ≥ 9번 타자 타석
                Assert.That(box.Batting["home_b1"].PlateAppearances, Is.GreaterThanOrEqualTo(box.Batting["home_b9"].PlateAppearances));
                var again = BoxScoreBuilder.Build(game, null, Resolve, new SeededRandom(BoxScoreBuilder.SeedFor(game)));
                Assert.That(again.Batting.Values.Select(b => b.ToString()), Is.EqualTo(box.Batting.Values.Select(b => b.ToString())), "같은 경기는 같은 박스스코어");
            }
        }

        [Test]
        public void 장면_자리를_선수_ID로_바꾸고_더미_자리는_뺀다()
        {
            var box = new BoxScore();
            box.Batter("home_b1").Hits = 2;
            box.Batter("away_b1").Hits = 1;
            box.Pitcher("home_sp").Wins = 1;
            box.WinningPitcher = "home_sp";
            var mapped = box.Remap(id => id.StartsWith("home_") ? "P-" + id : null);
            Assert.That(mapped.Batting.Keys, Is.EqualTo(new[] { "P-home_b1" }));
            Assert.That((mapped.Batting["P-home_b1"].Hits, mapped.Batting["P-home_b1"].Games), Is.EqualTo((2, 1)));
            Assert.That((mapped.WinningPitcher, mapped.Pitching["P-home_sp"].Wins), Is.EqualTo(("P-home_sp", 1)));
        }

        // ---------- 시즌 누적 · 0 나누기 ----------

        [Test]
        public void 기록이_없으면_비율은_0이고_예외없이_대시로_보인다()
        {
            var t = new SeasonStatsTracker();
            var b = t.BatterOf("nobody");
            var p = t.PitcherOf("nobody");
            Assert.That((b.Average, b.OnBase, b.Slugging, b.Ops, b.HasRecord), Is.EqualTo((0.0, 0.0, 0.0, 0.0, false)));
            Assert.That((p.Era, p.Whip, p.Innings, p.HasRecord), Is.EqualTo((0.0, 0.0, 0.0, false)));
            Assert.That((SeasonStatsText.Rate(b.Average, b.AtBats > 0), SeasonStatsText.Era(p), SeasonStatsText.Innings(0)), Is.EqualTo(("-.---", "-.--", "0")));
            Assert.That(SeasonStatsText.Ratio(3, 0), Is.Zero);

            // 볼넷만 고른 타자: 타수 0 → 타율 0, 출루율 1.000
            var box = new BoxScore();
            var line = box.Batter("walker");
            line.PlateAppearances = 2;
            line.Walks = 2;
            var arm = box.Pitcher("nopout");
            arm.EarnedRuns = 3; // 아웃 없이 3자책
            t.Add(box);
            Assert.That((t.BatterOf("walker").Average, t.BatterOf("walker").OnBase), Is.EqualTo((0.0, 1.0)));
            Assert.That((t.PitcherOf("nopout").Era, SeasonStatsText.Era(t.PitcherOf("nopout"))), Is.EqualTo((0.0, "-.--")));
        }

        [Test]
        public void 경기마다_더하고_비율_기록을_누적값으로_계산한다()
        {
            var t = new SeasonStatsTracker();
            for (int g = 0; g < 2; g++)
            {
                var box = new BoxScore();
                var b = box.Batter("bat");
                b.PlateAppearances = 4; b.AtBats = 3; b.Hits = 1; b.Walks = 1; b.HomeRuns = g; b.RunsBattedIn = 1 + g;
                var p = box.Pitcher("arm");
                p.Outs = 25; p.EarnedRuns = 2; p.Strikeouts = 7; p.Wins = g;
                t.Add(box);
            }
            var s = t.BatterOf("bat");
            Assert.That((s.Games, s.PlateAppearances, s.AtBats, s.Hits, s.HomeRuns, s.RunsBattedIn), Is.EqualTo((2, 8, 6, 2, 1, 3)));
            Assert.That(SeasonStatsText.Rate(s.Average, true), Is.EqualTo(".333"));
            Assert.That(SeasonStatsText.Rate(s.OnBase, true), Is.EqualTo(".500"));
            Assert.That(s.Slugging, Is.EqualTo(5.0 / 6).Within(1e-9), "1루타 1 + 홈런 1 = 5루타 / 6타수");
            var a = t.PitcherOf("arm");
            Assert.That((a.Games, a.Outs, a.Wins, a.Strikeouts), Is.EqualTo((2, 50, 1, 14)));
            Assert.That((SeasonStatsText.Innings(a.Outs), SeasonStatsText.Era(a)), Is.EqualTo(("16 2/3", "2.16")));
        }

        // ---------- 결산 · 시즌 넘김 · 저장 ----------

        static BoxScore OneGame()
        {
            var box = new BoxScore();
            var b = box.Batter("WV001");
            b.PlateAppearances = 4; b.AtBats = 4; b.Hits = 2; b.HomeRuns = 1; b.RunsBattedIn = 2;
            var p = box.Pitcher("WV013");
            p.GamesStarted = 1; p.Outs = 21; p.EarnedRuns = 1; p.Strikeouts = 6; p.Wins = 1;
            box.WinningPitcher = "WV013";
            return box;
        }

        [Test]
        public void 한_경기를_결산하면_홈런_타자의_홈런과_승리_투수의_승이_1씩_오른다()
        {
            var s = SeasonState.CreateDummy();
            s.StartNextGame();
            var report = PostGameProcessor.Process(s, new GameScore(1, 5), new SeededRandom(1), null, boxScore: OneGame());
            Assert.That(report.Box, Is.Not.Null);
            Assert.That((s.Stats.BatterOf("WV001").HomeRuns, s.Stats.BatterOf("WV001").Games), Is.EqualTo((1, 1)));
            Assert.That(s.Stats.PitcherOf("WV013").Wins, Is.EqualTo(1));
            s.StartNextGame();
            PostGameProcessor.Process(s, new GameScore(1, 5), new SeededRandom(1), null, boxScore: OneGame());
            Assert.That((s.Stats.BatterOf("WV001").HomeRuns, s.Stats.PitcherOf("WV013").Wins, s.Stats.PitcherOf("WV013").Outs), Is.EqualTo((2, 2, 42)));

            s.StartNextGame();
            PostGameProcessor.Process(s, new GameScore(1, 5), new SeededRandom(1), null);
            Assert.That(s.Stats.BatterOf("WV001").Games, Is.EqualTo(2), "박스스코어가 없는 경기는 성적을 바꾸지 않는다");
        }

        [Test]
        public void 한국시리즈_경기는_시즌_성적에_넣지_않고_시즌을_넘기면_모두_0이_된다()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(27);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                PostGameProcessor.Process(s, s.ScheduleScore(9, 0), random, null, boxScore: OneGame());
            }
            int games = s.Schedule.Count;
            Assert.That(s.Stats.BatterOf("WV001").HomeRuns, Is.EqualTo(games));
            s.BeginPostseasonIfDue();
            Assert.That(s.InPostseason, Is.True, "전승 → 한국시리즈 직행");
            s.StartNextGame();
            var ks = PostGameProcessor.Process(s, s.ScheduleScore(9, 0), random, null, boxScore: OneGame());
            Assert.That(ks.Postseason, Is.True);
            Assert.That(ks.Box, Is.Null);
            Assert.That(s.Stats.BatterOf("WV001").HomeRuns, Is.EqualTo(games), "한국시리즈 홈런은 시즌 기록이 아니다");

            s.TransitionToNextSeason();
            Assert.That(s.Stats.IsEmpty, Is.True);
            Assert.That((s.Stats.BatterOf("WV001").HomeRuns, s.Stats.PitcherOf("WV013").Wins), Is.EqualTo((0, 0)));
        }

        [Test]
        public void 저장하고_불러와도_시즌_성적이_같고_성적이_없는_이전_파일은_0에서_시작한다()
        {
            var s = SeasonState.CreateDummy();
            s.StartNextGame();
            PostGameProcessor.Process(s, new GameScore(1, 5), new SeededRandom(1), null, boxScore: OneGame());
            var json = UnityEngine.JsonUtility.ToJson(s.ToSaveData());
            var loaded = SeasonState.FromSaveData(UnityEngine.JsonUtility.FromJson<SeasonSaveData>(json));
            Assert.That(loaded.Stats.BatterOf("WV001").Line.ToString(), Is.EqualTo(s.Stats.BatterOf("WV001").Line.ToString()));
            Assert.That(loaded.Stats.PitcherOf("WV013").Line.ToString(), Is.EqualTo(s.Stats.PitcherOf("WV013").Line.ToString()));
            Assert.That(loaded.Stats.PitcherOf("WV013").GamesStarted, Is.EqualTo(1));

            var old = json.Replace("\"BattingStats\"", "\"_gone1\"").Replace("\"PitchingStats\"", "\"_gone2\"");
            var legacy = SeasonState.FromSaveData(UnityEngine.JsonUtility.FromJson<SeasonSaveData>(old));
            Assert.That(legacy.Stats.IsEmpty, Is.True);
            var nulls = s.ToSaveData();
            nulls.BattingStats = null;
            nulls.PitchingStats = null;
            Assert.That(SeasonState.FromSaveData(nulls).Stats.IsEmpty, Is.True);
        }

        // ---------- 팀 내 기록 1위 ----------

        [Test]
        public void 팀_내_기록_1위는_규정_타석_이닝을_채운_선수만_비율_부문에_오르고_기록이_없으면_비운다()
        {
            var mine = LeagueRosterBook.Build(Db, null).Get(Samsung);
            var t = new SeasonStatsTracker();
            Assert.That(SeasonLeaders.For(mine, t, 0).All(l => l.Player == null && l.ValueText == "—"), Is.True, "시즌 첫 경기 전");

            var box = new BoxScore();
            void Bat(string id, int pa, int ab, int h, int hr, int rbi)
            {
                var b = box.Batter(id);
                (b.PlateAppearances, b.AtBats, b.Hits, b.HomeRuns, b.RunsBattedIn) = (pa, ab, h, hr, rbi);
            }
            Bat("WV001", 4, 4, 2, 0, 1);
            Bat("WV002", 4, 4, 3, 1, 1);
            Bat("WV003", 1, 1, 1, 1, 3); // 1타수 1안타 — 규정 타석(1경기 × 3.1 → 4) 미달
            box.Pitcher("WV013").Outs = 18;
            box.Pitcher("WV013").EarnedRuns = 3;
            box.Pitcher("WV013").Wins = 1;
            box.Pitcher("WV014").Outs = 2; // 규정 이닝(3아웃) 미달 · 무실점
            box.Pitcher("WV014").Saves = 1;
            box.Pitcher("BW010").Wins = 5; // 남의 구단 선수는 빼고 본다
            t.Add(box);

            var leaders = SeasonLeaders.For(mine, t, 1).ToDictionary(l => l.Category);
            Assert.That((leaders[SeasonLeaderCategory.BattingAverage].Player.Id, leaders[SeasonLeaderCategory.BattingAverage].ValueText), Is.EqualTo(("WV002", ".750")));
            Assert.That((leaders[SeasonLeaderCategory.HomeRuns].Player.Id, leaders[SeasonLeaderCategory.HomeRuns].ValueText), Is.EqualTo(("WV002", "1개")),
                "같으면 타석 많은 쪽");
            Assert.That((leaders[SeasonLeaderCategory.RunsBattedIn].Player.Id, leaders[SeasonLeaderCategory.RunsBattedIn].ValueText), Is.EqualTo(("WV003", "3점")));
            Assert.That((leaders[SeasonLeaderCategory.Wins].Player.Id, leaders[SeasonLeaderCategory.Wins].ValueText), Is.EqualTo(("WV013", "1승")));
            Assert.That((leaders[SeasonLeaderCategory.Era].Player.Id, leaders[SeasonLeaderCategory.Era].ValueText), Is.EqualTo(("WV013", "4.50")));
            Assert.That((leaders[SeasonLeaderCategory.Saves].Player.Id, leaders[SeasonLeaderCategory.Saves].ValueText), Is.EqualTo(("WV014", "1개")));
        }
    }
}
