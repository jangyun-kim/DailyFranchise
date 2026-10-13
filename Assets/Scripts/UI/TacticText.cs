using DailyFranchise.Sim;

namespace DailyFranchise.UI
{
    /// <summary>작전 버튼·설명 문구. 확률 계산 없이 표시용 설명만 담는다.</summary>
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
    }
}
