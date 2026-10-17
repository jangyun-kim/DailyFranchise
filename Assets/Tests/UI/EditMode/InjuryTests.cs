using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 36: 부상 — 컨디션별 확률(위험 5% · 지침 1% · 그 밖 0.1%), 결장 3~10경기, 퓨처스 강등과 빈자리(김헌곤 룰·퓨처스 콜업),
    /// 경기마다 회복 카운트, 회복 뒤 콜업, 부상 중 1군 등록·트레이드·방출 차단, 뉴스, 저장/불러오기·이전 파일 호환.
    /// 2개 구단 가상 선수단 (삼성 = 내 구단 · 선발 타순 WV001~009 · 벤치 WV010~012 · 로테이션 WV013·017~020 · 불펜 WV014~016,
    /// 퓨처스는 신인으로 채운다).
    /// </summary>
    public class InjuryTests
    {
        static readonly MasterDatabase Db = TestLeague.TwoTeams;
        const string Samsung = TestLeague.Samsung;
        const string Lg = TestLeague.Lg;
        const string Shortstop = "p_rookie_2026_ss";
        const string Catcher = "p_rookie_2026_c";
        const string Arm = "p_rookie_2026_p";
        /// <summary>도경민 — 선발 2번 유격수.</summary>
        const string LineupShortstop = "WV002";

        static LeagueRosterBook Book(SeasonState s) => LeagueRosterBook.Build(Db, s.Signings, s.Progression, s.Rookies);

        static TeamRoster Mine(SeasonState s) => Book(s).Get(Samsung);

        static SeasonState WithFutures()
        {
            var s = SeasonState.CreateDummy();
            s.AddRookie(new PlayerData { Id = Shortstop, Name = "한유망", Team = Samsung, Role = RosterRole.Bench, Order = 1, Position = "SS", Age = 19, War = 0.40, Year = 2026 });
            s.AddRookie(new PlayerData { Id = Catcher, Name = "백포수", Team = Samsung, Role = RosterRole.Bench, Order = 2, Position = "C", Age = 20, War = 0.10, Year = 2026 });
            s.AddRookie(new PlayerData { Id = Arm, Name = "강속구", Team = Samsung, Role = RosterRole.Bullpen, Order = 3, Position = "불펜", Age = 21, War = 0.30, Year = 2026 });
            return s;
        }

        /// <summary>한 선수만 타석에 선 출장 기록 (부상 판정 대상이 한 명 — 스크립트 난수가 그 선수에게만 쓰인다).</summary>
        static GameParticipation OnlyBatter(string id)
        {
            var g = new GameParticipation();
            g.AddBatter(id);
            return g;
        }

        /// <summary>경기 하나를 치른다 (장부는 경기 전 것).</summary>
        static PostGameReport Play(SeasonState s, GameParticipation game = null, IRandomSource injury = null)
        {
            s.StartNextGame();
            return PostGameProcessor.Process(s, new GameScore(2, 5), new SeededRandom(1), null, game,
                game == null ? null : new ScriptedRandom(0.99), injuryRandom: injury, book: Book(s));
        }

        // ---------- 확률 ----------

        [Test]
        public void 부상_확률은_경기_전_컨디션이_위험이면_5퍼센트_지침이면_1퍼센트_그밖은_0점1퍼센트()
        {
            Assert.That(InjuryRules.ChanceFor(PlayerCondition.Exhausted), Is.EqualTo(0.05));
            Assert.That(InjuryRules.ChanceFor(PlayerCondition.Tired), Is.EqualTo(0.01));
            foreach (int c in new[] { PlayerCondition.Normal, PlayerCondition.Good, PlayerCondition.Excellent })
                Assert.That(InjuryRules.ChanceFor(c), Is.EqualTo(0.001), PlayerCondition.Label(c));
            Assert.That(InjuryRules.ChanceFor(0), Is.EqualTo(0.05), "범위 밖은 1~5로 맞춘다");

            // 문턱: 난수 < 확률이면 부상
            int Hurt(int condition, double roll) =>
                InjuryRules.Roll(new[] { "x" }, _ => condition, new ScriptedRandom(roll, 0.0)).Count;
            Assert.That((Hurt(1, 0.049), Hurt(1, 0.05), Hurt(2, 0.0099), Hurt(2, 0.01), Hurt(3, 0.0009), Hurt(3, 0.001)),
                Is.EqualTo((1, 0, 1, 0, 1, 0)));
        }

        [Test]
        public void 많이_굴리면_위험_선수의_부상_비율은_5퍼센트_가까이_나오고_결장은_3에서_10경기()
        {
            var random = new SeededRandom(36);
            const int trials = 100000;
            var ids = Enumerable.Range(0, trials).Select(i => "p" + i.ToString("D6")).ToList();
            var hurt = InjuryRules.Roll(ids, _ => PlayerCondition.Exhausted, random);
            Assert.That(hurt.Count / (double)trials, Is.EqualTo(0.05).Within(0.003));
            Assert.That(hurt.Min(h => h.games), Is.EqualTo(InjuryRules.MinGames));
            Assert.That(hurt.Max(h => h.games), Is.EqualTo(InjuryRules.MaxGames));
            var tired = InjuryRules.Roll(ids, _ => PlayerCondition.Tired, new SeededRandom(37));
            Assert.That(tired.Count / (double)trials, Is.EqualTo(0.01).Within(0.0015));
        }

        // ---------- 경기 결산 ----------

        [Test]
        public void 위험_컨디션으로_출장한_선발_타자가_다치면_퓨처스로_내려가고_김헌곤_룰로_같은_위치가_메운다()
        {
            var s = WithFutures();
            s.SetCondition(LineupShortstop, PlayerCondition.Exhausted);
            var before = Mine(s);
            Assert.That(before.AtOrder(2).Id, Is.EqualTo(LineupShortstop));

            // 0.04 < 5% → 부상, 0.5 → 3 + (int)(0.5 × 8) = 7경기
            var report = Play(s, OnlyBatter(LineupShortstop), new ScriptedRandom(0.04, 0.5));

            Assert.That(report.Injuries.Select(i => (i.PlayerId, i.Team, i.Name, i.Games)),
                Is.EqualTo(new[] { (LineupShortstop, Samsung, "도경민", 7) }));
            Assert.That(s.InjuryGamesOf(LineupShortstop), Is.EqualTo(7));
            var record = s.Signings.Last();
            Assert.That((record.Mark, record.PlayerId, record.FromTeam, record.ToTeam, record.IsTransfer), Is.EqualTo((InjuryMark.Injured, LineupShortstop, Samsung, Samsung, false)));

            var after = Mine(s);
            Assert.That(after.Reserves.Select(p => p.Id), Is.EqualTo(new[] { Catcher, Arm, LineupShortstop }), "부상 선수는 퓨처스 끝");
            var filler = after.AtOrder(2);
            Assert.That((filler.Id, filler.Position, filler.Role), Is.EqualTo((Shortstop, "SS", RosterRole.Lineup)), "퓨처스 같은 위치(유격수)가 그 타순으로");
            Assert.That(after.Bench.Select(p => p.Id), Is.EqualTo(before.Bench.Select(p => p.Id)), "벤치는 그대로");
            Assert.That(RosterLimits.GameReadinessProblem(after), Is.Null, "다음 경기를 그대로 치를 수 있다");
            Assert.That(Book(s).Injured, Is.EquivalentTo(new[] { LineupShortstop }));

            var news = report.News.Last();
            Assert.That((news.Kind, news.Text), Is.EqualTo((NewsKind.Injury, "[부상] 삼성 라이온즈 도경민, 경기 중 부상으로 7경기 결장!")));
            Assert.That(s.RecentNews.Any(n => n.Kind == NewsKind.Injury), Is.True);
        }

        [Test]
        public void 컨디션이_좋으면_같은_난수에도_다치지_않고_출장하지_않은_선수는_판정하지_않는다()
        {
            var s = WithFutures();
            var report = Play(s, OnlyBatter(LineupShortstop), new ScriptedRandom(0.04, 0.5));
            Assert.That(report.Injuries, Is.Empty, "보통이면 0.1% — 0.04는 다치지 않는다");

            var bench = new GameParticipation();
            bench.AddRosterPlayer("WV010"); // 선수단에만 있고 출장하지 않음
            var zero = new ScriptedRandom(0.0);
            Assert.That(Play(s, bench, zero).Injuries, Is.Empty);
            Assert.That(zero.Draws, Is.Zero, "출장하지 않은 선수에게는 난수를 쓰지 않는다");
        }

        [Test]
        public void 부상_난수가_없으면_부상은_나지_않고_컨디션_결과는_그대로다()
        {
            var a = WithFutures();
            var b = WithFutures();
            var game = OnlyBatter(LineupShortstop);
            a.SetCondition(LineupShortstop, 1);
            b.SetCondition(LineupShortstop, 1);
            var plain = Play(a, game);
            var hurt = Play(b, game, new ScriptedRandom(0.0, 0.0));
            Assert.That(plain.Injuries, Is.Empty);
            Assert.That(hurt.Injuries.Single().Games, Is.EqualTo(InjuryRules.MinGames));
            Assert.That(hurt.Conditions.Select(c => c.ToString()), Is.EqualTo(plain.Conditions.Select(c => c.ToString())),
                "부상 판정은 전용 난수 — 컨디션 결과를 바꾸지 않는다");
            Assert.That(hurt.DayResults.Select(r => r.Score), Is.EqualTo(plain.DayResults.Select(r => r.Score)), "타 구단 경기도 그대로");
        }

        [Test]
        public void 타_구단_출장_선수도_다치면_그_구단_퓨처스로_내려가고_뉴스가_난다()
        {
            var s = WithFutures();
            var lg = Book(s).Get(Lg);
            string theirs = lg.Lineup[0].Id;
            s.SetCondition(theirs, PlayerCondition.Exhausted);
            var report = Play(s, OnlyBatter(theirs), new ScriptedRandom(0.0, 0.0));
            Assert.That(report.Injuries.Single().Team, Is.EqualTo(Lg));
            var after = Book(s).Get(Lg);
            Assert.That(after.Reserves.Any(p => p.Id == theirs) && after.Lineup.All(p => p.Id != theirs), Is.True);
            Assert.That(RosterLimits.GameReadinessProblem(after), Is.Null, "빈 타순은 김헌곤 룰(없으면 대체 선수)로 메운다");
            Assert.That(report.News.Last().Text, Does.StartWith("[부상] LG 트윈스 "));
        }

        // ---------- 빈자리 ----------

        [Test]
        public void 벤치_불펜_로테이션_선수가_다치면_퓨처스에서_같은_종류가_올라와_정원을_지킨다()
        {
            var s = WithFutures();
            int count = RosterLimits.CountOf(Mine(s));

            s.Injure(Samsung, "WV011", 4); // 벤치 타자 → 퓨처스 타자(포수 제외 첫 타자 = 유격수)가 벤치 끝으로
            var r = Mine(s);
            Assert.That(r.Bench.Select(p => p.Id), Is.EqualTo(new[] { "WV010", "WV012", Shortstop }));
            Assert.That(r.Bench.Select(p => p.Order), Is.EqualTo(new[] { 1, 2, 3 }));

            s.Injure(Samsung, "WV013", 5); // 선발 로테이션 1번 → 퓨처스 투수가 그 순번을 잇는다
            r = Mine(s);
            Assert.That((r.Starters[0].Id, r.Starters[0].Order, r.Starters[0].Role), Is.EqualTo((Arm, 1, RosterRole.Starter)));

            s.Injure(Samsung, "WV015", 3); // 불펜 → 퓨처스에 남은 투수가 없다 → 한 자리 빈다
            r = Mine(s);
            Assert.That(r.Bullpen.Select(p => p.Id), Is.EqualTo(new[] { "WV014", "WV016" }));
            Assert.That(RosterLimits.CountOf(r), Is.EqualTo(count - 1), "올릴 투수가 없으면 1군이 한 명 준다");
            Assert.That(r.Reserves.Select(p => p.Id), Is.EqualTo(new[] { Catcher, "WV011", "WV013", "WV015" }));
            Assert.That(RosterLimits.GameReadinessProblem(r), Is.Null);
        }

        [Test]
        public void 퓨처스의_부상_선수는_김헌곤_룰로도_올라오지_않는다()
        {
            var s = WithFutures();
            s.Injure(Samsung, Shortstop, 6); // 퓨처스 유격수가 먼저 다친다 (자리는 그대로)
            Assert.That(Mine(s).Reserves.Select(p => p.Id), Is.EqualTo(new[] { Shortstop, Catcher, Arm }));
            s.Injure(Samsung, LineupShortstop, 3);
            var slot = Mine(s).AtOrder(2);
            Assert.That(slot.Id, Is.Not.EqualTo(Shortstop), "부상 중인 유격수는 빈 타순에 오르지 않는다");
            Assert.That((LeagueRosterBook.IsReplacement(slot), slot.Position), Is.EqualTo((true, "SS")), "맞는 선수가 없으면 대체 선수");
        }

        [Test]
        public void 사용자_라인업_카드의_부상_선수_자리도_김헌곤_룰로_고쳐진다()
        {
            var s = WithFutures();
            var card = LineupCard.FromRoster(Mine(s));
            Assert.That(card.TrySwap(LineupSpot.Batting(2), LineupSpot.Batting(5)), Is.True); // 도경민을 5번으로
            s.MyLineup = card;
            s.Injure(Samsung, LineupShortstop, 3);
            var repaired = s.MyLineup.Repair(Mine(s));
            Assert.That(repaired, Is.Not.Null);
            Assert.That(repaired.Batting[4], Is.EqualTo(new LineupSlot(Shortstop, "SS")), "카드의 5번 유격수 자리를 퓨처스에서 올라온 유격수가");
            Assert.That(repaired.Batting[1].PlayerId, Is.EqualTo("WV005"), "나머지 타순은 그대로");
        }

        // ---------- 회복 ----------

        [Test]
        public void 경기마다_남은_경기가_하나씩_줄고_0이_되면_회복해_퓨처스에서_다시_1군_등록할_수_있다()
        {
            var s = WithFutures();
            s.SetCondition(LineupShortstop, PlayerCondition.Exhausted);
            Play(s, OnlyBatter(LineupShortstop), new ScriptedRandom(0.0, 0.0)); // 3경기
            Assert.That(s.InjuryGamesOf(LineupShortstop), Is.EqualTo(3));
            Assert.That(RosterMoves.Partners(Mine(s), LineupShortstop, s), Is.Empty, "부상 중에는 맞바꿀 상대가 없다");
            Assert.That(RosterMoves.Check(s, Mine(s), "WV010", LineupShortstop), Is.EqualTo(RosterMoveBlock.Injured));
            Assert.That(RosterMoves.Partners(Mine(s), "WV010", s).Select(p => p.Id), Has.No.Member(LineupShortstop), "벤치에서 봐도 상대로 나오지 않는다");

            Assert.That(Play(s).Recoveries, Is.Empty);
            Assert.That(s.InjuryGamesOf(LineupShortstop), Is.EqualTo(2));
            Play(s);
            Assert.That(s.InjuryGamesOf(LineupShortstop), Is.EqualTo(1));
            var last = Play(s);
            Assert.That(last.Recoveries.Select(r => (r.PlayerId, r.Name)), Is.EqualTo(new[] { (LineupShortstop, "도경민") }));
            Assert.That((s.IsInjured(LineupShortstop), s.ConditionOf(LineupShortstop)), Is.EqualTo((false, PlayerCondition.Normal)), "회복하면 보통");
            Assert.That(s.Signings.Last().Mark, Is.EqualTo(InjuryMark.Recovered));
            Assert.That(last.News.Last().Text, Is.EqualTo("[복귀] 삼성 라이온즈 도경민, 부상 털고 퓨처스 복귀!"));

            var my = Mine(s);
            Assert.That(my.Reserves.Any(p => p.Id == LineupShortstop), Is.True, "회복해도 퓨처스에서 대기");
            Assert.That(Book(s).Injured, Is.Empty);
            Assert.That(RosterMoves.Partners(my, LineupShortstop, s).Select(p => p.Id), Is.EqualTo(new[] { "WV010", "WV011", "WV012" }));
            Assert.That(RosterMoves.Apply(s, my, "WV010", LineupShortstop).Moved, Is.True, "1군 등록");
            Assert.That(Mine(s).Bench[0].Id, Is.EqualTo(LineupShortstop));
        }

        [Test]
        public void 부상_중에는_트레이드와_방출이_막힌다()
        {
            var s = WithFutures();
            s.Injure(Samsung, "WV011", 4);
            var book = Book(s);
            string theirs = book.Get(Lg).Lineup[0].Id;
            Assert.That(TradeDesk.Quote(s, book, "WV011", theirs).Block, Is.EqualTo(TradeBlock.Injured), "내 부상 선수를 보낼 수 없다");
            Assert.That(StoveLeague.CheckRelease(s, book, "WV011"), Is.EqualTo(ReleaseBlock.Injured));
            Assert.That(StoveLeague.ReleaseBlockText(ReleaseBlock.Injured), Does.Contain("부상"));

            s.Injure(Lg, theirs, 5);
            book = Book(s);
            Assert.That(TradeDesk.Quote(s, book, "WV010", theirs).Block, Is.EqualTo(TradeBlock.Injured), "타 구단 부상 선수도 받을 수 없다");
            Assert.That(TradeDesk.BlockText(TradeBlock.Injured), Does.Contain("부상"));
        }

        [Test]
        public void 이미_다친_선수를_또_다치게_하면_긴_쪽만_남고_기록은_하나다()
        {
            var s = WithFutures();
            s.Injure(Samsung, "WV011", 4);
            s.Injure(Samsung, "WV011", 2);
            Assert.That(s.InjuryGamesOf("WV011"), Is.EqualTo(4));
            s.Injure(Samsung, "WV011", 9);
            Assert.That(s.InjuryGamesOf("WV011"), Is.EqualTo(9));
            Assert.That(s.Signings.Count(r => r.IsInjuryMark), Is.EqualTo(1));
        }

        [Test]
        public void 부상_강등과_회복은_페이롤_1군_규칙을_따르고_연봉_기록을_바꾸지_않는다()
        {
            var s = WithFutures();
            long payroll = StoveLeague.Payroll(Mine(s), s.Signings);
            s.Injure(Samsung, "WV011", 4); // 피준서(WAR −0.50) 퓨처스 · 한유망(WAR 0.40) 벤치
            Assert.That(StoveLeague.Payroll(Mine(s), s.Signings),
                Is.EqualTo(payroll - FrontOfficeTrust.SalaryOf(-0.50) + FrontOfficeTrust.SalaryOf(0.40)));
            var p = Book(s).Find(Shortstop).player;
            Assert.That(StoveLeague.SalaryOf(p, Samsung, s.Signings), Is.EqualTo(FrontOfficeTrust.SalaryOf(0.40)), "부상 기록은 계약이 아니다");
        }

        // ---------- 저장 ----------

        [Test]
        public void 저장하고_불러와도_부상과_남은_경기와_선수단이_같다()
        {
            var s = WithFutures();
            s.Injure(Samsung, LineupShortstop, 6);
            s.Injure(Samsung, "WV015", 3);
            var data = s.ToSaveData();
            Assert.That(data.Injuries.Select(i => (i.Id, i.Team, i.Games)), Is.EqualTo(new[] { (LineupShortstop, Samsung, 6), ("WV015", Samsung, 3) }));
            Assert.That(data.Signings.Select(x => x.Injury), Is.EqualTo(new[] { 1, 1 }));
            var json = UnityEngine.JsonUtility.ToJson(data);
            var loaded = SeasonState.FromSaveData(UnityEngine.JsonUtility.FromJson<SeasonSaveData>(json));
            Assert.That((loaded.InjuryGamesOf(LineupShortstop), loaded.InjuryGamesOf("WV015")), Is.EqualTo((6, 3)));
            Assert.That(loaded.Signings.Select(x => x.Mark), Is.EqualTo(new[] { InjuryMark.Injured, InjuryMark.Injured }));
            Assert.That(Mine(loaded).Reserves.Select(p => p.Id), Is.EqualTo(Mine(s).Reserves.Select(p => p.Id)));
            Assert.That(Mine(loaded).Lineup.Select(p => p.Id), Is.EqualTo(Mine(s).Lineup.Select(p => p.Id)));
        }

        [Test]
        public void 부상_데이터가_없는_이전_저장_파일은_전원_건강하다()
        {
            var data = WithFutures().ToSaveData();
            data.Injuries = null;
            var loaded = SeasonState.FromSaveData(data);
            Assert.That(loaded.Injuries, Is.Empty);
            Assert.That(loaded.IsInjured(LineupShortstop), Is.False);

            // JSON에 필드 자체가 없는 파일 (Phase 35 형식)
            var json = UnityEngine.JsonUtility.ToJson(WithFutures().ToSaveData()).Replace("\"Injuries\":[],", "");
            Assert.That(json, Does.Not.Contain("Injuries"));
            Assert.That(SeasonState.FromSaveData(UnityEngine.JsonUtility.FromJson<SeasonSaveData>(json)).Injuries, Is.Empty);
        }

        [Test]
        public void 시즌을_넘기면_부상은_모두_낫고_선수는_퓨처스에서_대기한다()
        {
            var s = WithFutures();
            s.Injure(Samsung, "WV011", 9);
            var random = new SeededRandom(27);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(5, 1), random); // 결산 없이 일정만 (남은 경기는 줄지 않는다)
            }
            Assert.That(s.IsInjured("WV011"), Is.True);
            s.TransitionToNextSeason();
            Assert.That(s.Injuries, Is.Empty);
            Assert.That(s.Signings.Last().Mark, Is.EqualTo(InjuryMark.Recovered));
            Assert.That(Mine(s).Reserves.Any(p => p.Id == "WV011"), Is.True);
        }
    }
}
