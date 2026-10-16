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
    /// Phase 30: 오프시즌 신인 드래프트(가상 루키 생성·퓨처스 주입·저장·뉴스)와 포지션 매칭 빈자리 규칙("김헌곤 룰" —
    /// 강민호(C)가 빠지면 벤치 1번 김헌곤(LF)이 아니라 포수가 들어간다).
    /// </summary>
    public class RookieDraftTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;
        const string Samsung = "삼성 라이온즈";

        static string IdOf(string name) => Real.Players.First(p => p.Name == name).Id;

        static SeasonState FinishedSeason()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(30);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(3, 2), random);
            }
            return s;
        }

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Real, s.Signings, s.Progression, s.Rookies);

        static void Retire(SeasonState s, params string[] names)
        {
            foreach (var n in names) s.SetProgression(new PlayerProgression(IdOf(n), 0, 0, true, 2027));
        }

        static PlayerData CatcherSlot(TeamRoster r) => r.Lineup.Single(p => p.Position == "C");

        sealed class Lookup : IPlayerLookup
        {
            readonly Dictionary<string, PlayerData> _players;
            public Lookup(params PlayerData[] players) => _players = players.ToDictionary(p => p.Id);
            public PlayerData FindPlayer(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;
        }

        // ---------- 신인 생성 ----------

        [Test]
        public void 신인은_ID_이름_나이_기록이_규칙대로이고_시드가_같으면_같은_선수다()
        {
            var a = RookieGenerator.Create(Samsung, 2027, 1, "C", 30, new SeededRandom(5));
            var b = RookieGenerator.Create(Samsung, 2027, 1, "C", 30, new SeededRandom(5));
            Assert.That(a.Id, Does.Match("^p_rookie_2027_[0-9a-f]{32}$"));
            Assert.That((a.Id, a.Name, a.Age, a.BattingAverage), Is.EqualTo((b.Id, b.Name, b.Age, b.BattingAverage)), "결정적");
            Assert.That(a.Name.Length, Is.EqualTo(3));
            Assert.That((a.Team, a.Position, a.Role, a.Order, a.Memo, a.Year), Is.EqualTo((Samsung, "C", RosterRole.Bench, 30, "2027 신인 1R", 2027)));
            Assert.That(RookieGenerator.IsRookie(a), Is.True);
            Assert.That(RookieGenerator.IsRookie(Real.Players[0]), Is.False);

            var random = new SeededRandom(11);
            var names = new HashSet<string>();
            for (int i = 0; i < 300; i++)
            {
                int round = 1 + i % 5;
                bool pitcher = i % 2 == 0;
                var p = RookieGenerator.Create(Samsung, 2027, round, pitcher ? "P" : "SS", i + 1, random, names);
                Assert.That(p.Age, Is.InRange(18, 22), p.Name);
                Assert.That(p.War, Is.InRange(-0.5, 0.5), "리그 평균 이하 WAR");
                if (pitcher)
                {
                    Assert.That((p.Role, p.IsPitcher), Is.EqualTo((RosterRole.Bullpen, true)));
                    Assert.That(p.Era, Is.InRange(4.8, 6.9));
                    Assert.That(p.Era, Is.GreaterThan(MatchupCalculator.LeagueEra), "리그 평균자책보다 나쁘다");
                    Assert.That(p.OpponentAverage, Is.GreaterThan(MatchupCalculator.LeagueAverage));
                    Assert.That((p.Fip > 0, p.Whip > 0, p.HomeRunsPer9 > 0, p.InningsPitched > 0), Is.EqualTo((true, true, true, true)));
                }
                else
                {
                    Assert.That(p.BattingAverage, Is.InRange(0.19, 0.266));
                    Assert.That(p.BattingAverage, Is.LessThan(MatchupCalculator.LeagueAverage), "리그 평균 타율 이하");
                    Assert.That(p.OnBasePercentage, Is.GreaterThan(p.BattingAverage));
                    Assert.That(p.Ops, Is.EqualTo(p.OnBasePercentage + p.RecordedSlugging).Within(0.0015));
                    Assert.That(p.WrcPlus, Is.LessThan(100));
                }
            }
            Assert.That(names.Count, Is.GreaterThan(280), "리그 안 이름은 겹치지 않게 고른다");
        }

        [Test]
        public void 라운드가_빠를수록_기록이_좋다()
        {
            var random = new SeededRandom(3);
            double Avg(int round) => Enumerable.Range(0, 200)
                .Select(_ => RookieGenerator.Create(Samsung, 2027, round, "CF", 1, random).BattingAverage).Average();
            Assert.That(Avg(1), Is.GreaterThan(Avg(2)));
            Assert.That(Avg(2), Is.GreaterThan(Avg(4)));
        }

        [Test]
        public void 신인도_확률_계산기가_기존_선수와_구분_없이_계산하고_유망주_커브로_자란다()
        {
            var rookie = RookieGenerator.Create(Samsung, 2027, 1, "SS", 1, new SeededRandom(8));
            var pitcher = Real.GetRoster(Samsung).Starters[0];
            var calc = new MatchupCalculator(new Lookup(rookie, pitcher));
            double p = calc.HitProbability(rookie.Id, pitcher.Id, default);
            Assert.That(p, Is.EqualTo(MatchupCalculator.Combine(rookie, pitcher)).Within(0.05), "기존 선수와 같은 경로");
            Assert.That(p, Is.GreaterThan(0).And.LessThan(MatchupCalculator.Combine(MatchupCalculator.AverageBatter, pitcher)), "평균 타자보다 낮다");
            Assert.That(AgingCurve.StageOf(rookie.Age + 1), Is.EqualTo(AgeStage.Prospect), "다음 오프시즌에도 유망주 성장 구간");
        }

        // ---------- 오프시즌 드래프트 ----------

        [Test]
        public void 오프시즌_결산_뒤_구단_퓨처스에_18에서_22세_신인_3에서_5명이_들어간다()
        {
            var s = FinishedSeason();
            var summary = s.TransitionToNextSeason(Book(s), new SeededRandom(2027));

            var draft = summary.Draft;
            Assert.That(draft.Picks.Count, Is.InRange(RookieDraft.MinPicks, RookieDraft.MaxPicks), "실제 데이터는 삼성 1개 구단");
            Assert.That(draft.MyPicks.Select(p => p.Round), Is.EqualTo(Enumerable.Range(1, draft.Picks.Count)));
            Assert.That(s.Rookies.Select(r => r.Id), Is.EqualTo(draft.Picks.Select(p => p.Player.Id)));
            Assert.That(s.Rookies.All(r => r.Team == Samsung && r.Age >= 18 && r.Age <= 22 && r.Id.StartsWith("p_rookie_2027_")), Is.True,
                "새 시즌 2027 신인");
            Assert.That(Real.Players.Any(RookieGenerator.IsRookie), Is.False, "마스터 DB는 그대로");

            var after = Book(s).Get(Samsung);
            Assert.That(after.Reserves.Where(RookieGenerator.IsRookie).Select(p => p.Id), Is.EquivalentTo(s.Rookies.Select(r => r.Id)),
                "신인은 퓨처스로 (1군 26명 정원·페이롤에 넣지 않는다)");
            Assert.That(RosterLimits.CountOf(after), Is.LessThanOrEqualTo(RosterLimits.MaxRoster));
            Assert.That(summary.Text, Does.EndWith($"신인 {draft.Picks.Count}명"));

            var news = s.LatestNews(1).Single();
            Assert.That(news.Kind, Is.EqualTo(NewsKind.Draft));
            Assert.That(news.Text, Does.StartWith("[드래프트] 삼성 라이온즈, 1라운드에서 특급 유망주 " + draft.Picks[0].Player.Name + "(")
                .And.EndWith(") 지명!"));
        }

        [Test]
        public void 첫_지명은_구단에서_가장_부족한_포지션이다()
        {
            // 삼성 유격수는 이재현 1명 (목표 2명) — 가장 부족
            var s = FinishedSeason();
            var draft = s.TransitionToNextSeason(Book(s), new SeededRandom(1)).Draft;
            bool shortstopRetired = s.ProgressionOf(IdOf("이재현"))?.Retired == true;
            if (!shortstopRetired) Assert.That(draft.Picks[0].Player.Position, Is.EqualTo("SS"));

            var depth = RookieDraft.TargetDepth.Keys.ToDictionary(k => k, k => RookieDraft.TargetDepth[k]);
            depth["C"] = 0;
            Assert.That(RookieDraft.MostNeeded(depth, new SeededRandom(0)), Is.EqualTo("C"));
        }

        [Test]
        public void 신인은_다음_시즌에도_장부에_남고_한_살_먹으며_다시_지명이_쌓인다()
        {
            var s = FinishedSeason();
            s.TransitionToNextSeason(Book(s), new SeededRandom(4));
            var first = s.Rookies.ToList();
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(1, 0), new SeededRandom(9));
            }
            var aging = s.TransitionToNextSeason(Book(s), new SeededRandom(5));
            Assert.That(s.Rookies.Take(first.Count), Is.EqualTo(first), "지난 신인은 그대로");
            Assert.That(s.Rookies.Count, Is.GreaterThanOrEqualTo(first.Count + RookieDraft.MinPicks));
            var book = Book(s).Get(Samsung);
            foreach (var r in first.Where(r => s.ProgressionOf(r.Id)?.Retired != true))
            {
                var now = book.Reserves.Concat(LeagueRosterBook.AllOf(book)).Single(p => p.Id == r.Id);
                Assert.That(now.Age, Is.EqualTo(r.Age + 1), r.Name);
                Assert.That(s.ProgressionOf(r.Id), Is.Not.Null);
            }
            Assert.That(aging.Draft.Picks.All(p => p.Player.Id.StartsWith("p_rookie_2028_")), Is.True);
        }

        [Test]
        public void 신인은_저장_파일에_기록_전체가_남고_이전_파일은_신인_없이_읽힌다()
        {
            var s = FinishedSeason();
            s.TransitionToNextSeason(Book(s), new SeededRandom(6));
            string json = JsonUtility.ToJson(s.ToSaveData());
            var loaded = SeasonState.FromSaveData(JsonUtility.FromJson<SeasonSaveData>(json));
            Assert.That(loaded.Rookies.Count, Is.EqualTo(s.Rookies.Count));
            for (int i = 0; i < s.Rookies.Count; i++)
            {
                PlayerData a = s.Rookies[i], b = loaded.Rookies[i];
                Assert.That((b.Id, b.Name, b.Team, b.Position, b.Role, b.Order, b.Age, b.Throws, b.Bats, b.Memo),
                    Is.EqualTo((a.Id, a.Name, a.Team, a.Position, a.Role, a.Order, a.Age, a.Throws, a.Bats, a.Memo)));
                Assert.That((b.BattingAverage, b.OnBasePercentage, b.Ops, b.War, b.Era, b.OpponentAverage, b.HomeRunsPer9, b.Fip, b.Whip),
                    Is.EqualTo((a.BattingAverage, a.OnBasePercentage, a.Ops, a.War, a.Era, a.OpponentAverage, a.HomeRunsPer9, a.Fip, a.Whip)));
            }
            Assert.That(Book(loaded).Get(Samsung).Reserves.Count(RookieGenerator.IsRookie), Is.EqualTo(s.Rookies.Count));

            var old = JsonUtility.FromJson<SeasonSaveData>(json.Replace("\"Rookies\"", "\"Rookies_old\""));
            Assert.That(SeasonState.FromSaveData(old).Rookies, Is.Empty, "Phase 30 이전 파일");
            var dup = s.ToSaveData();
            dup.Rookies.Add(s.Rookies[0]);
            dup.Rookies.Add(new PlayerData { Id = "", Team = Samsung });
            Assert.That(SeasonState.FromSaveData(dup).Rookies.Count, Is.EqualTo(s.Rookies.Count), "중복·빈 ID는 건너뛴다");
        }

        // ---------- 포지션 매칭 (김헌곤 룰) ----------

        [Test]
        public void 포지션_적합도_포수는_포수만_내야끼리_외야끼리_지명타자는_누구나()
        {
            Assert.That(PositionMatcher.FitOf("C", "C"), Is.EqualTo(PositionFit.Exact));
            Assert.That(PositionMatcher.FitOf("C", "LF"), Is.EqualTo(PositionFit.None), "포수 자리에 외야수 금지");
            Assert.That(PositionMatcher.FitOf("C", "1B"), Is.EqualTo(PositionFit.None));
            Assert.That(PositionMatcher.FitOf("SS", "2B"), Is.EqualTo(PositionFit.Compatible));
            Assert.That(PositionMatcher.FitOf("SS", "CF"), Is.EqualTo(PositionFit.None));
            Assert.That(PositionMatcher.FitOf("RF", "LF"), Is.EqualTo(PositionFit.Compatible));
            Assert.That(PositionMatcher.FitOf("DH", "1B"), Is.EqualTo(PositionFit.Compatible));
            Assert.That(PositionMatcher.FitOf("DH", "C"), Is.EqualTo(PositionFit.None), "포수는 다른 자리에 대신 서지 않는다");
            Assert.That(PositionMatcher.FitOf("1B", "C"), Is.EqualTo(PositionFit.None));
            Assert.That(PositionMatcher.Label("C"), Is.EqualTo("포수"));
        }

        [Test]
        public void 강민호가_은퇴하면_김헌곤이_아니라_벤치_포수_이병헌이_포수_자리에_선다()
        {
            var db = Real.GetRoster(Samsung);
            Assert.That(CatcherSlot(db).Name, Is.EqualTo("강민호"));
            Assert.That(db.Bench[0].Name, Is.EqualTo("김헌곤"), "예전 규칙이면 벤치 1번 김헌곤(LF)이 포수로 섰다");

            var s = SeasonState.CreateDummy();
            Retire(s, "강민호");
            var r = Book(s).Get(Samsung);
            var c = CatcherSlot(r);
            Assert.That((c.Name, c.Order, c.Role), Is.EqualTo(("이병헌", CatcherSlot(db).Order, RosterRole.Lineup)));
            Assert.That(r.Lineup.Any(p => p.Name == "김헌곤"), Is.False);
            Assert.That(r.Bench.Select(p => p.Name), Does.Contain("김헌곤").And.Not.Contain("이병헌"));
            Assert.That(RosterLimits.GameReadinessProblem(r), Is.Null);
        }

        [Test]
        public void 벤치에_포수가_없으면_퓨처스_포수를_올리고_구단에_포수가_없으면_대체_포수를_세운다()
        {
            var s = SeasonState.CreateDummy();
            Retire(s, "강민호", "이병헌");
            var r = Book(s).Get(Samsung);
            var c = CatcherSlot(r);
            Assert.That((c.Name, c.Role), Is.EqualTo(("김재성", RosterRole.Lineup)), "퓨처스 첫 포수");
            Assert.That(r.Reserves.Any(p => p.Name == "김재성"), Is.False, "퓨처스에서 빠진다");

            Retire(s, "김재성", "김민수", "김도환");
            r = Book(s).Get(Samsung);
            c = CatcherSlot(r);
            Assert.That((c.Id.StartsWith(LeagueRosterBook.ReplacementPrefix), c.Name, c.Position), Is.EqualTo((true, "대체 선수", "C")),
                "구단에 포수가 한 명도 없을 때만");
            Assert.That(c.BattingAverage, Is.EqualTo(MatchupCalculator.LeagueAverage), "리그 평균 기록");
            Assert.That(r.Lineup.Concat(r.Bench).Where(p => p.Position == "LF").Select(p => p.Name), Does.Contain("김헌곤"));

            // 신인 포수가 퓨처스에 있으면 대체 선수 대신 신인
            var rookie = RookieGenerator.Create(Samsung, 2027, 1, "C", 99, new SeededRandom(1));
            s.AddRookie(rookie);
            c = CatcherSlot(Book(s).Get(Samsung));
            Assert.That((c.Id, c.Role, c.Order), Is.EqualTo((rookie.Id, RosterRole.Lineup, CatcherSlot(Real.GetRoster(Samsung)).Order)));
        }

        [Test]
        public void 같은_위치가_없으면_같은_수비군으로_메운다()
        {
            // 유격수 이재현은 구단 유일한 SS — 벤치 첫 내야수(2B 안주형)가 SS 자리로
            var s = SeasonState.CreateDummy();
            Retire(s, "이재현");
            var ss = Book(s).Get(Samsung).Lineup.Single(p => p.Position == "SS");
            Assert.That(ss.Name, Is.EqualTo("안주형"));
        }

        [Test]
        public void 주전_포수를_방출하면_카드의_다른_타순은_그대로_두고_포수_자리만_포수로_채운다()
        {
            var s = SeasonState.CreateDummy();
            var card = LineupCard.FromRoster(Book(s).Get(Samsung));
            Assert.That(card.TrySwap(LineupSpot.Batting(1), LineupSpot.Batting(5)), Is.True, "강민호를 1번으로");
            s.MyLineup = card;
            Assert.That(card.Batting[0], Is.EqualTo(new LineupSlot(IdOf("강민호"), "C")));

            Assert.That(StoveLeague.CheckRelease(s, Book(s), IdOf("강민호")), Is.EqualTo(ReleaseBlock.None));
            var o = StoveLeague.Release(s, Book(s), IdOf("강민호"));
            Assert.That(o.Released, Is.True);
            var roster = Book(s).Get(Samsung);
            Assert.That(s.MyLineup.Matches(roster), Is.False, "장부가 바뀌어 카드가 어긋난다");

            var repaired = s.MyLineup.Repair(roster);
            Assert.That(repaired.Batting[0], Is.EqualTo(new LineupSlot(IdOf("이병헌"), "C")), "1번 포수 자리에 포수");
            Assert.That(repaired.Batting.Skip(1), Is.EqualTo(card.Batting.Skip(1)), "나머지 타순 그대로");
            Assert.That(repaired.Bench, Does.Contain(IdOf("김헌곤")).And.Not.Contain(IdOf("이병헌")));
            Assert.That(repaired.Matches(roster), Is.True);
            var applied = repaired.ApplyTo(roster);
            Assert.That(applied.Lineup.Where(p => p.Position == "C").Select(p => p.Name), Is.EqualTo(new[] { "이병헌" }));
        }

        [Test]
        public void 카드의_빈_포수_자리를_맞는_선수로_못_채우면_고치지_않는다()
        {
            var roster = Real.GetRoster(Samsung);
            var card = LineupCard.FromRoster(roster);
            var catcher = CatcherSlot(roster);
            Assert.That(card.Repair(roster).Batting, Is.EqualTo(card.Batting), "맞는 카드는 그대로");
            Assert.That(card.Repair(null), Is.Null);

            // 포수가 빠지고 벤치는 김헌곤(LF)뿐인 선수단
            var benchOnlyOutfield = new TeamRoster(Samsung, roster.Lineup.Where(p => p.Id != catcher.Id).ToList(),
                new[] { roster.Bench[0] }, roster.Starters, roster.Bullpen);
            Assert.That(card.Repair(benchOnlyOutfield), Is.Null, "포수 자리에 김헌곤(LF)을 세우지 않는다");
        }
    }
}
