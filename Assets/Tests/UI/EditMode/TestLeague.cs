using System.IO;
using DailyFranchise.App.Data;
using UnityEngine;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// 테스트용 선수 DB (Phase 26.5).
    /// - <see cref="Real"/>: 런타임과 같은 실제 KBO 데이터 (Resources: Teams.csv + SampleRoster_Export.csv, 삼성 1개 구단).
    /// - <see cref="TwoTeams"/>: 다구단 규칙(이적·대체 선수·케미스트리 상성) 검증용 2개 구단 가상 선수단
    ///   (Assets/Tests/Fixtures/TestLeague_TwoTeams.csv — Resources 밖이라 런타임에서는 읽지 않는다).
    /// </summary>
    public static class TestLeague
    {
        public const string Samsung = "삼성 라이온즈";
        public const string Lg = "LG 트윈스";

        static MasterDatabase _real;
        static MasterDatabase _twoTeams;

        public static string FixturePath => Path.Combine(Application.dataPath, "Tests", "Fixtures", "TestLeague_TwoTeams.csv");

        public static string TeamsCsv => Resources.Load<TextAsset>(MasterDatabase.TeamsResource).text;

        public static MasterDatabase Real => _real ??= MasterDatabase.LoadFromResources();

        public static MasterDatabase TwoTeams => _twoTeams ??= MasterDatabase.FromCsv(TeamsCsv, File.ReadAllText(FixturePath));
    }
}
