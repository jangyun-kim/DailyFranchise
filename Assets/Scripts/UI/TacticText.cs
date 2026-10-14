using DailyFranchise.Sim;
using DailyFranchise.Sim.Fixtures;

namespace DailyFranchise.UI
{
    /// <summary>작전 버튼·설명 문구. 확률 판정은 BranchPoint가 하고, 여기서는 표시만 한다.</summary>
    public static class TacticText
    {
        public static string Label(TacticKind t)
        {
            switch (t)
            {
                case TacticKind.Default: return "기본 진행";
                case TacticKind.SwingAway: return "강공 지시";
                case TacticKind.SqueezeBunt: return "스퀴즈 번트";
                default: return t.ToString();
            }
        }

        /// <summary>선택 설명 칸: 실행 조건 / 성공 이득 / 실패 위험.</summary>
        public static string Detail(TacticKind t)
        {
            switch (t)
            {
                case TacticKind.Default:
                    return "기본 진행 · 개입 없이 타자에게 맡깁니다.\n이득: 작전 노출 없음 · 위험: 내야 땅볼 병살";
                case TacticKind.SwingAway:
                    return "강공 지시 · 조건: 볼카운트 1-1, 타자 컨택 양호\n이득: 장타 시 역전 · 위험: 땅볼 병살로 이닝 종료";
                case TacticKind.SqueezeBunt:
                    return "스퀴즈 번트 · 조건: 3루 주자, 2아웃 전\n이득: 확실한 동점 · 위험: 번트 실패 시 3루 주자 아웃";
                default:
                    return Label(t);
            }
        }

        /// <summary>미리보기용 성공 확률 줄.</summary>
        public static string Odds(TacticBranch branch) =>
            branch.IsGamble
                ? $"성공 확률 {System.Math.Round(branch.SuccessRate * 100)}% · 실패 {System.Math.Round((1 - branch.SuccessRate) * 100)}%"
                : "확률 판정 없음 · 원래 흐름대로 진행";

        /// <summary>작전 결과 한 단어. 기본 진행은 판정이 없으므로 "개입 없음".</summary>
        public static string OutcomeText(TacticKind t, TacticOutcome outcome) =>
            t == TacticKind.Default ? "개입 없음" : outcome == TacticOutcome.Success ? "성공" : "실패";

        /// <summary>작전 결과가 나온 뒤 설명 칸 마지막 줄.</summary>
        public static string OutcomeLine(TacticKind t, TacticOutcome outcome) =>
            t == TacticKind.Default ? "결과 · 개입 없이 진행"
                : outcome == TacticOutcome.Success ? $"결과 · {Label(t)} 성공" : $"결과 · {Label(t)} 실패";
    }
}
