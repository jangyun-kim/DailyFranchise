using System;
using System.IO;
using UnityEngine;

namespace DailyFranchise.App
{
    public enum SeasonLoadStatus
    {
        /// <summary>저장 파일을 불러왔다.</summary>
        Loaded,
        /// <summary>저장 파일이 없어 새 시즌을 시작했다.</summary>
        NewSeason,
        /// <summary>저장 파일이 손상돼 새 시즌으로 덮어썼다.</summary>
        RecoveredFromCorrupt,
    }

    /// <summary>
    /// 시즌 상태를 로컬 JSON 파일(JsonUtility)로 저장·불러오기 한다. 파일 I/O는 이 클래스만 다룬다.
    /// 손상된 파일은 크래시 없이 경고를 남기고 새 시즌으로 덮어쓴다.
    /// </summary>
    public sealed class SaveManager
    {
        public const string FileName = "season_save.json";

        /// <summary>기본 저장 경로: Application.persistentDataPath/season_save.json.</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        readonly Action<string> _warn;

        /// <param name="filePath">저장 파일 경로. 테스트는 임시 경로를 넣는다.</param>
        /// <param name="warn">경고 출력. null이면 Debug.LogWarning.</param>
        public SaveManager(string filePath, Action<string> warn = null)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("저장 경로가 비어 있다.", nameof(filePath));
            FilePath = filePath;
            _warn = warn ?? Debug.LogWarning;
        }

        public string FilePath { get; }
        public bool HasSave => File.Exists(FilePath);

        public static string ToJson(SeasonState season) => JsonUtility.ToJson(season.ToSaveData(), prettyPrint: true);

        /// <summary>JSON을 시즌으로 되살린다. 형식이 깨졌거나 내용이 맞지 않으면 <see cref="FormatException"/>.</summary>
        public static SeasonState FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("저장 파일이 비어 있다.");
            SeasonSaveData data;
            try
            {
                data = JsonUtility.FromJson<SeasonSaveData>(json);
            }
            catch (Exception e) when (!(e is FormatException))
            {
                throw new FormatException($"JSON 해석 실패: {e.Message}", e);
            }
            return SeasonState.FromSaveData(data);
        }

        /// <summary>
        /// 저장한다. 임시 파일에 다 쓴 뒤 바꿔 끼워, 쓰는 도중 꺼져도 기존 저장이 반쯤 덮이지 않게 한다.
        /// </summary>
        /// <returns>저장했으면 true. 디스크 오류는 경고만 남기고 false.</returns>
        public bool Save(SeasonState season)
        {
            if (season == null) throw new ArgumentNullException(nameof(season));
            string json = ToJson(season);
            string temp = FilePath + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(temp, json);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _warn($"[SaveManager] 시즌 저장 실패 ({FilePath}): {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 저장 파일이 있으면 불러오고, 없으면 새 시즌을 만든다.
        /// 파일을 읽지 못하거나 손상됐으면 경고를 남기고 새 시즌으로 파일을 덮어쓴다.
        /// </summary>
        public SeasonState LoadOrCreate(Func<SeasonState> createNew, out SeasonLoadStatus status)
        {
            if (createNew == null) throw new ArgumentNullException(nameof(createNew));
            if (!HasSave)
            {
                status = SeasonLoadStatus.NewSeason;
                return createNew();
            }

            try
            {
                var season = FromJson(File.ReadAllText(FilePath));
                status = SeasonLoadStatus.Loaded;
                return season;
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
            {
                _warn($"[SaveManager] 저장 파일이 손상돼 새 시즌을 시작합니다 ({FilePath}): {e.Message}");
                var fresh = createNew();
                Save(fresh);
                status = SeasonLoadStatus.RecoveredFromCorrupt;
                return fresh;
            }
        }
    }
}
