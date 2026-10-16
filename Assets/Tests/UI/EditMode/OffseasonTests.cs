using System;
using System.Linq;
using DailyFranchise.App;
using DailyFranchise.Sim;
using DailyFranchise.Sim.Data;
using NUnit.Framework;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 27: 시즌 연도, 정규시즌 종료, 오프시즌 결산과 이월.</summary>
    public class OffseasonTests
    {
        /// <summary>더미 시즌 10경기를 모두 치른다 (내 구단 홀수 경기 승리, 짝수 경기 패배).</summary>
        static SeasonState FinishedSeason()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(27);
            while (!s.IsSeasonFinished)
            {
                s.StartNextGame();
                bool win = s.NextIndex % 2 == 0;
                s.FinishCurrentGame(s.ScheduleScore(win ? 5 : 2, win ? 1 : 4), random);
            }
            return s;
        }

        [Test]
        public void 새_시즌은_2026년이고_제목은_연도와_다음_경기_달이다()
        {
            var s = SeasonState.CreateDummy();
            Assert.That(s.Year, Is.EqualTo(SeasonState.DefaultYear).And.EqualTo(2026));
            Assert.That(s.SeasonTitle, Is.EqualTo("2026 시즌 · 5월"));
            Assert.That(s.IsSeasonFinished, Is.False);
        }

        [Test]
        public void 일정을_모두_치르면_IsSeasonFinished가_참이고_제목이_정규시즌_종료로_바뀐다()
        {
            var s = SeasonState.CreateDummy();
            var random = new SeededRandom(1);
            for (int i = 0; i < s.Schedule.Count; i++)
            {
                Assert.That(s.IsSeasonFinished, Is.False, $"{i}경기 뒤");
                s.StartNextGame();
                s.FinishCurrentGame(new GameScore(1, 2), random);
            }
            Assert.That(s.IsSeasonFinished, Is.True);
            Assert.That(s.NextIndex, Is.EqualTo(s.Schedule.Count));
            Assert.That(s.SeasonTitle, Is.EqualTo("2026 시즌 · 정규시즌 종료"));
            Assert.Throws<InvalidOperationException>(() => s.StartNextGame(), "일정 종료 뒤 경기 진입 차단");
        }

        [Test]
        public void 정규시즌이_남았거나_경기_중이면_결산할_수_없다()
        {
            var s = SeasonState.CreateDummy();
            Assert.Throws<InvalidOperationException>(() => s.TransitionToNextSeason());
            s.StartNextGame();
            Assert.Throws<InvalidOperationException>(() => s.TransitionToNextSeason());
            Assert.That(s.Year, Is.EqualTo(2026));
        }

        [Test]
        public void 결산하면_연도가_오르고_전적_득실점_순위변동_L10이_초기화된다()
        {
            var s = FinishedSeason();
            Assert.That(s.MyStanding.GamesPlayed, Is.GreaterThan(0));
            var teams = s.Standings.Select(r => r.Team).ToList();

            var summary = s.TransitionToNextSeason();

            Assert.That(s.Year, Is.EqualTo(2027));
            Assert.That(s.SeasonTitle, Is.EqualTo("2027 시즌 · 5월"));
            Assert.That(s.IsSeasonFinished, Is.False);
            Assert.That(s.NextIndex, Is.Zero);
            Assert.That(s.GamesPlayed, Is.Zero);
            Assert.That(s.InProgress, Is.Null);
            Assert.That(s.LastFinished, Is.Null);
            Assert.That(s.LastDayResults, Is.Empty);
            Assert.That(s.MyRecentForm().Games, Is.Zero, "L10 비움");
            Assert.That(s.Standings.Select(r => r.Team), Is.EqualTo(teams), "지난 시즌 최종 순서 유지");
            foreach (var row in s.Standings)
            {
                Assert.That((row.Wins, row.Losses, row.Draws), Is.EqualTo((0, 0, 0)), row.Team);
                Assert.That((row.RunsScored, row.RunsAllowed, row.PreviousRank), Is.EqualTo((0, 0, 0)), row.Team);
                Assert.That(row.Rank, Is.EqualTo(1), "전원 동률 공동 1위");
                Assert.That(row.RankChangeText, Is.Empty);
            }

            Assert.That(summary.Year, Is.EqualTo(2026));
            Assert.That(summary.Wins + summary.Losses + summary.Draws, Is.EqualTo(23 + 18 + 10), "더미 시작 41경기 + 10경기");
            Assert.That(summary.Text, Does.StartWith($"2026 시즌 최종 {summary.Rank}위 · {summary.Wins}승 {summary.Losses}패"));
        }

        [Test]
        public void 새_일정은_같은_대진을_다음_해_같은_월일로_옮기고_모두_예정이다()
        {
            var s = FinishedSeason();
            var before = s.Schedule.Select(g => (g.Number, g.AwayTeam, g.HomeTeam, g.Stadium)).ToList();
            s.TransitionToNextSeason();

            Assert.That(s.Schedule.Select(g => (g.Number, g.AwayTeam, g.HomeTeam, g.Stadium)), Is.EqualTo(before));
            Assert.That(s.Schedule.All(g => g.Status == GameStatus.Scheduled && g.Score == null), Is.True);
            Assert.That(s.Schedule[0].Date, Is.EqualTo("5.15 (토)"), "2027-05-15는 토요일");
            Assert.That(s.Schedule[9].Date, Is.EqualTo("5.26 (수)"));
            Assert.That(s.NextGame, Is.SameAs(s.Schedule[0]));

            // 새 시즌도 그대로 치를 수 있다.
            s.StartNextGame();
            s.FinishCurrentGame(s.ScheduleScore(3, 1), new SeededRandom(2));
            Assert.That(s.MyStanding.Wins, Is.EqualTo(1));
        }

        [Test]
        public void 다음_해_날짜는_월일을_지키고_요일만_다시_붙인다()
        {
            Assert.That(SeasonState.NextYearDate("5.15 (금)", 2026), Is.EqualTo("5.15 (토)"));
            Assert.That(SeasonState.NextYearDate("3.28 (토)", 2026), Is.EqualTo("3.28 (일)"));
            Assert.That(SeasonState.NextYearDate("2.29 (목)", 2028), Is.EqualTo("2.28 (수)"), "평년은 2.28");
            Assert.That(SeasonState.NextYearDate("경기일 미정", 2026), Is.EqualTo("경기일 미정"));
            Assert.That(SeasonState.NextYearDate("2.30", 2026), Is.EqualTo("2.30"), "없는 날짜는 그대로");
            // 여러 해 넘겨도 날짜가 흘러가지 않는다.
            string date = "5.15 (금)";
            for (int year = 2026; year < 2036; year++) date = SeasonState.NextYearDate(date, year);
            Assert.That(date, Does.StartWith("5.15 ("));
        }

        [Test]
        public void 예산_팬만족도_TP_팬덤슬롯_이적기록_라인업은_이월된다()
        {
            var s = FinishedSeason();
            s.SetFinance(7_250_000_000L, 66);
            s.SetTrust(80, 40, 0, 0);
            s.SetUnlockedSlots(FandomSlot.Uniform | FandomSlot.Ceremony);
            s.AddSigning(new SigningRecord("p1", "LG 트윈스", s.MyTeam, 300_000_000L), isBatter: false);
            s.AddSigning(new SigningRecord("p2", s.MyTeam, LeagueRosterBook.FreeAgentTeam, 100_000_000L), isBatter: false);
            var signings = s.Signings.ToList();
            var lineup = s.MyLineup;

            s.TransitionToNextSeason();

            Assert.That((s.Budget, s.FanApproval), Is.EqualTo((7_250_000_000L, 66)));
            Assert.That(s.TrustPoints, Is.EqualTo(80), "TP 이월 (이월 상한 100 이하)");
            Assert.That(s.TrustEarnedThisSeason, Is.Zero, "시즌 적립 한도는 새로");
            Assert.That(s.UnlockedSlots, Is.EqualTo(FandomSlot.Uniform | FandomSlot.Ceremony));
            Assert.That(s.Signings, Is.EqualTo(signings), "영입·방출 기록 = 선수단·FA 시장 그대로");
            Assert.That(s.MyLineup, Is.SameAs(lineup));
        }

        [Test]
        public void TP는_기존_시즌_마감_규칙대로_100까지만_이월한다()
        {
            var s = FinishedSeason();
            s.SetTrust(180, 0, 0, 0);
            s.TransitionToNextSeason();
            Assert.That(s.TrustPoints, Is.EqualTo(FrontOfficeTrust.CarryOver));
        }

        [Test]
        public void 전년도_신뢰_대출은_절반이_새_시즌_샐러리캡에서_빠진다()
        {
            var s = FinishedSeason();
            s.SetTrust(150, 0, 0, 0);
            Assert.That(s.BorrowCap(100), Is.EqualTo(200_000_000L));
            Assert.That(s.SalaryCap, Is.EqualTo(6_200_000_000L));

            var summary = s.TransitionToNextSeason();

            Assert.That(summary.CapFlex, Is.EqualTo(200_000_000L));
            Assert.That((s.CapRepay, s.CapFlex), Is.EqualTo((100_000_000L, 0L)));
            Assert.That(s.SalaryCap, Is.EqualTo(5_900_000_000L), "캡 기준 60억 − 상환 1억");
        }

        [Test]
        public void 컨디션은_보통_미만만_보통으로_올리고_선발_로테이션은_1번부터()
        {
            var s = FinishedSeason();
            s.SetCondition("tired", PlayerCondition.Min);
            s.SetCondition("bad", 2);
            s.SetCondition("great", PlayerCondition.Max);
            s.SetLastStarter(s.MyTeam, "ace");

            s.TransitionToNextSeason();

            Assert.That(s.ConditionOf("tired"), Is.EqualTo(PlayerCondition.Default));
            Assert.That(s.ConditionOf("bad"), Is.EqualTo(PlayerCondition.Default));
            Assert.That(s.ConditionOf("great"), Is.EqualTo(PlayerCondition.Max));
            Assert.That(s.Conditions.Values.All(c => c >= PlayerCondition.Default), Is.True);
            Assert.That(s.LastStarterOf(s.MyTeam), Is.Null);
        }

        [Test]
        public void 연도는_저장되고_결산_뒤에도_불러온_시즌이_같다()
        {
            var s = FinishedSeason();
            s.TransitionToNextSeason();
            var data = s.ToSaveData();
            Assert.That(data.Year, Is.EqualTo(2027));
            Assert.That(data.NextIndex, Is.Zero);

            var loaded = SaveManager.FromJson(SaveManager.ToJson(s));
            Assert.That(loaded.Year, Is.EqualTo(2027));
            Assert.That(loaded.SeasonTitle, Is.EqualTo("2027 시즌 · 5월"));
            Assert.That(loaded.Schedule.Select(g => g.Date), Is.EqualTo(s.Schedule.Select(g => g.Date)));
            Assert.That(loaded.Standings.All(r => r.GamesPlayed == 0), Is.True);
        }

        [Test]
        public void 끝난_시즌을_저장하고_불러와도_결산할_수_있다()
        {
            var loaded = SaveManager.FromJson(SaveManager.ToJson(FinishedSeason()));
            Assert.That(loaded.IsSeasonFinished, Is.True);
            loaded.TransitionToNextSeason();
            Assert.That(loaded.Year, Is.EqualTo(2027));
        }

        [Test]
        public void 연도가_없는_이전_저장_파일은_제목에서_연도를_읽고_없으면_2026()
        {
            var data = SeasonState.CreateDummy().ToSaveData();
            data.Year = 0;
            data.SeasonTitle = "2025 시즌 · 5월";
            Assert.That(SeasonState.FromSaveData(data).Year, Is.EqualTo(2025));
            data.SeasonTitle = "시즌";
            Assert.That(SeasonState.FromSaveData(data).Year, Is.EqualTo(2026));
            data.SeasonTitle = null;
            Assert.That(SeasonState.FromSaveData(data).Year, Is.EqualTo(2026));
        }

        [Test]
        public void 제목_연도_읽기()
        {
            Assert.That(SeasonState.YearOf("2026 시즌 · 5월"), Is.EqualTo(2026));
            Assert.That(SeasonState.YearOf("2031"), Is.EqualTo(2031));
            Assert.That(SeasonState.YearOf("20261 시즌"), Is.Null);
            Assert.That(SeasonState.YearOf("t"), Is.Null);
            Assert.That(SeasonState.YearOf(null), Is.Null);
        }

        [Test]
        public void 결산_버튼_문구는_다음_연도를_가리킨다()
        {
            var s = FinishedSeason();
            Assert.That(LobbyController.OffseasonButtonText(s), Is.EqualTo("시즌 결산 및 2027 시즌 준비"));
            s.TransitionToNextSeason();
            Assert.That(LobbyController.OffseasonButtonText(s), Is.EqualTo("시즌 결산 및 2028 시즌 준비"));
        }
    }
}
