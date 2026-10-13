using System.Collections.Generic;
using DailyFranchise.Sim;

namespace DailyFranchise.UI
{
    /// <summary>
    /// PitchEvent를 화면 문구·스타일 클래스로 옮기는 표시 전용 매핑.
    /// 판정은 하지 않는다: Result / Outcome / IsStrikeByRule 값을 그대로 읽기만 한다.
    /// </summary>
    public static class PitchPresentation
    {
        public static string ResultText(PitchEvent e)
        {
            switch (e.Outcome)
            {
                case PlateAppearanceOutcome.Strikeout: return "STRIKE OUT";
                case PlateAppearanceOutcome.Walk: return "WALK";
            }
            switch (e.Result)
            {
                case PitchResult.Ball: return "BALL";
                case PitchResult.CalledStrike:
                case PitchResult.SwingingStrike: return "STRIKE";
                case PitchResult.Foul: return "FOUL";
                default: return "IN PLAY";
            }
        }

        public static string ResultClass(PitchEvent e)
        {
            switch (e.Outcome)
            {
                case PlateAppearanceOutcome.Strikeout: return "res--strikeout";
                case PlateAppearanceOutcome.Walk: return "res--walk";
            }
            switch (e.Result)
            {
                case PitchResult.Ball: return "res--ball";
                case PitchResult.CalledStrike:
                case PitchResult.SwingingStrike: return "res--strike";
                case PitchResult.Foul: return "res--foul";
                default: return "res--inplay";
            }
        }

        public static readonly string[] AllResultClasses =
        {
            "res--strike", "res--ball", "res--foul", "res--inplay", "res--strikeout", "res--walk",
        };

        /// <summary>스윙 여부 보조 표기 (루킹/헛스윙).</summary>
        public static string SwingNote(PitchEvent e)
        {
            switch (e.Result)
            {
                case PitchResult.CalledStrike: return "루킹";
                case PitchResult.SwingingStrike: return "헛스윙";
                default: return null;
            }
        }

        public static string ZoneNote(PitchEvent e) => e.IsStrikeByRule ? "존 안" : "존 밖";

        public static string HalfText(PitchEvent e) =>
            $"{e.Inning}회{(e.HalfInning == HalfInning.Top ? "초" : "말")}";

        public static string OutsText(int outs) => outs == 0 ? "무사" : $"{outs}사";
    }

    /// <summary>
    /// 고정 이벤트 목록에서 화면에 필요한 파생 값을 계산하는 읽기 전용 도우미.
    /// 이벤트 목록을 변경하지 않는다.
    /// </summary>
    public static class ScenarioReadModel
    {
        /// <summary>index 투구가 속한 타석의 첫 투구 인덱스.</summary>
        public static int PlateAppearanceStart(IReadOnlyList<PitchEvent> events, int index)
        {
            int i = index;
            while (i > 0 && events[i - 1].Outcome == PlateAppearanceOutcome.None) i--;
            return i;
        }

        /// <summary>장면 시작부터 index 투구까지, 해당 공격(초=원정, 말=홈)이 올린 득점 합.</summary>
        public static int RunsThrough(IReadOnlyList<PitchEvent> events, int index, HalfInning battingHalf)
        {
            int runs = 0;
            for (int i = 0; i <= index; i++)
                if (events[i].HalfInning == battingHalf) runs += events[i].RunsScored;
            return runs;
        }

        /// <summary>장면 시작부터 index 투구까지, 특정 반 이닝에 올린 득점 합.</summary>
        public static int RunsInHalfThrough(IReadOnlyList<PitchEvent> events, int index, int inning, HalfInning half)
        {
            int runs = 0;
            for (int i = 0; i <= index; i++)
                if (events[i].Inning == inning && events[i].HalfInning == half) runs += events[i].RunsScored;
            return runs;
        }

        /// <summary>장면 시작부터 index 투구까지 해당 투수가 던진 공 수.</summary>
        public static int PitcherPitchesThrough(IReadOnlyList<PitchEvent> events, int index)
        {
            int n = 0;
            string pitcher = events[index].PitcherId;
            for (int i = 0; i <= index; i++)
                if (events[i].PitcherId == pitcher) n++;
            return n;
        }
    }
}
