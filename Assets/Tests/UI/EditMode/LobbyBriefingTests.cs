using System.Linq;
using DailyFranchise.App;
using DailyFranchise.App.Data;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 31: 로비 홈 대시보드 뷰모델 — 오늘의 결정(컨디션 경고·결산·경기 불가), 프리뷰 문구, 선수 상세 문구, 팀 테마.</summary>
    public class LobbyBriefingTests
    {
        static readonly MasterDatabase Real = TestLeague.Real;
        const string Samsung = "삼성 라이온즈";

        static TeamRoster Mine => Real.GetRoster(Samsung);

        [Test]
        public void 경고가_없으면_결정_항목도_없다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(LobbyBriefing.Decisions(s, Mine, Mine.Starters[0], null), Is.Empty);
            Assert.That(LobbyBriefing.Decisions(s, null, null, null), Is.Empty, "선수 DB 전");
        }

        [Test]
        public void 지친_선발_타자는_위험_먼저_경고하고_같은_수비군의_컨디션_좋은_벤치를_추천한다()
        {
            var s = SeasonState.CreateDummy();
            var third = Mine.AtOrder(3);   // 김영웅 3B
            var catcher = Mine.AtOrder(5); // 강민호 C
            s.SetCondition(third.Id, PlayerCondition.Tired);
            s.SetCondition(catcher.Id, PlayerCondition.Exhausted);
            s.SetCondition(Mine.AtOrder(1).Id, PlayerCondition.Good); // 경고 아님

            var items = LobbyBriefing.Decisions(s, Mine, Mine.Starters[0], null);
            Assert.That(items.Select(i => i.PlayerId), Is.EqualTo(new[] { catcher.Id, third.Id }), "위험(1) 먼저");
            Assert.That(items[0].Severity, Is.EqualTo(BriefingSeverity.Danger));
            Assert.That(items[0].Title, Is.EqualTo("5번 강민호 (C) · 컨디션 위험"));
            Assert.That(items[0].Detail, Is.EqualTo("추천 교체: 벤치 이병헌 (C · 보통)"), "포수 자리는 포수 추천");
            Assert.That(items[1].Severity, Is.EqualTo(BriefingSeverity.Caution));
            Assert.That(items[1].Detail, Is.EqualTo("추천 교체: 벤치 안주형 (2B · 보통)"), "3B → 같은 내야 첫 벤치");
            Assert.That(items[1].Key, Is.EqualTo("cond-" + third.Id));

            s.SetCondition(Mine.Bench[0].Id, PlayerCondition.Excellent); // 김헌곤 LF 최상 — 내야가 아니라 3B 추천은 그대로
            Assert.That(LobbyBriefing.Decisions(s, Mine, null, null)[1].Detail, Does.Contain("안주형"));
        }

        [Test]
        public void 경기_불가_선수단과_결산_필요와_지친_오늘_선발도_결정이다()
        {
            var s = SeasonState.CreateDummy();
            var items = LobbyBriefing.Decisions(s, Mine, Mine.Starters[0], "선발 투수가 없습니다.");
            Assert.That((items.Single().Key, items.Single().Severity, items.Single().Detail),
                Is.EqualTo(("roster", BriefingSeverity.Danger, "선발 투수가 없습니다.")));

            s.SetCondition(Mine.Starters[0].Id, PlayerCondition.Tired);
            var starter = LobbyBriefing.Decisions(s, Mine, Mine.Starters[0], null).Single();
            Assert.That((starter.PlayerId, starter.Title), Is.EqualTo((Mine.Starters[0].Id, $"오늘 선발 {Mine.Starters[0].Name} · 컨디션 지침")));

            var random = new SeededRandom(1);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                s.FinishCurrentGame(s.ScheduleScore(2, 1), random);
            }
            var done = LobbyBriefing.Decisions(s, null, null, null).Single();
            Assert.That((done.Key, done.Severity, done.Title), Is.EqualTo(("offseason", BriefingSeverity.Info, "2026 정규시즌 종료")));
        }

        [Test]
        public void 프리뷰_문구()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(LobbyBriefing.StandingText(s.StandingOf("KIA 타이거즈")), Is.EqualTo("1위 · 26승 15패 1무"));
            Assert.That(LobbyBriefing.StandingText(s.StandingOf(Samsung)), Is.EqualTo("3위 · 23승 18패"));
            Assert.That(LobbyBriefing.StandingText(null), Is.EqualTo("순위 없음"));
            Assert.That(LobbyBriefing.StarterLine(Mine.Starters[1]), Is.EqualTo("ERA 3.66 · FIP 4.34"), "원태인");
            Assert.That(LobbyBriefing.StarterLine(null), Is.EqualTo("선발 정보 없음"));
            Assert.That(LobbyBriefing.StarterLine(new PlayerData { Role = RosterRole.Starter }), Is.EqualTo("기록 없음"));
            Assert.That(LobbyBriefing.GameDayText(s), Is.EqualTo("다음 경기 5.15 (금)"));

            Assert.That(LobbyBriefing.HeadToHeadText(s, "LG 트윈스"), Is.EqualTo("시즌 첫 맞대결"));
            s.StartNextGame();
            s.FinishCurrentGame(s.ScheduleScore(5, 2), new SeededRandom(3)); // 경기 1: LG
            Assert.That(LobbyBriefing.HeadToHeadText(s, "LG 트윈스"), Is.EqualTo("시즌 상대 전적 1승 0패"));
        }

        [Test]
        public void 팀_테마와_엠블럼()
        {
            Assert.That(LobbyBriefing.TeamThemeClass(Samsung), Is.EqualTo("team--samsung"));
            Assert.That(SeasonState.KboStadiums.Keys.Select(LobbyBriefing.TeamThemeClass).Distinct().Count(), Is.EqualTo(10), "10개 구단 모두 테마");
            Assert.That(LobbyBriefing.TeamThemeClass("모르는 구단"), Is.EqualTo("team--default"));
            Assert.That(LobbyBriefing.TeamMark(Samsung), Is.EqualTo("삼성"));
            Assert.That(LobbyBriefing.TeamMark(null), Is.EqualTo("—"));
        }

        [Test]
        public void 선수_상세_기록_타일과_컨디션_안내()
        {
            var batter = Mine.AtOrder(1); // 구자욱
            Assert.That(PlayerDetailView.StatTiles(batter).Select(t => t.label), Is.EqualTo(new[] { "타율", "출루율", "OPS", "홈런", "도루", "WAR" }));
            Assert.That(PlayerDetailView.StatTiles(batter)[0].value, Is.EqualTo(".343"));
            Assert.That(PlayerDetailView.StatTiles(batter)[2].value, Is.EqualTo("1.044"));
            var pitcher = Mine.Starters[0];
            Assert.That(PlayerDetailView.StatTiles(pitcher).Select(t => t.label), Is.EqualTo(new[] { "ERA", "FIP", "WHIP", "승-패", "세이브·홀드", "WAR" }));

            Assert.That(PlayerDetailView.ConditionNote(batter, PlayerCondition.Tired), Is.EqualTo("벤치에서 한 경기 쉬면 보통으로 회복합니다."));
            Assert.That(PlayerDetailView.ConditionNote(batter, PlayerCondition.Exhausted), Does.Contain("휴식이 필요합니다"));
            Assert.That(PlayerDetailView.ConditionNote(pitcher, PlayerCondition.Tired), Does.StartWith("보통 미만이면 선발 순번을 건너뜁니다"));
        }
    }
}
