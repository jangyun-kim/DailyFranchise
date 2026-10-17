using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI
{
    /// <summary>선수 사진 종류: 증명사진(로비 목록 썸네일) · 액션 사진(경기 화면 카드).</summary>
    public enum PlayerImageKind
    {
        Face,
        Live,
    }

    /// <summary>
    /// 선수 사진 불러오기 (Phase 38.5). 파이썬 백엔드가 배경을 지워 <c>Resources/Images/Players/</c>에 넣은
    /// <c>{선수 ID}_face.png</c>·<c>{선수 ID}_live.png</c>를 <see cref="Resources.Load{T}(string)"/>로 읽는다 (예: p_삼성_2024_구자욱_face).
    /// - 캐시: 한 번 읽은 Sprite는 경로별 사전에 둔다. 없는 파일도 "없음"으로 기억해 타석마다 다시 찾지 않는다.
    /// - Fallback: 액션 사진이 없으면 증명사진, 그것도 없으면(사진 없는 과거 연도·더미 선수) 기본 실루엣
    ///   (<c>Resources/UI/PlayerSilhouette</c>, 그 파일마저 없으면 코드로 그린 실루엣). 어떤 경우에도 null을 돌려주지 않는다.
    /// - 선수 사진 폴더는 라이선스 문제로 저장소에 넣지 않는다 (.gitignore) — 사진이 없는 체크아웃에서도 실루엣으로 동작한다.
    /// </summary>
    public static class ImageResourceManager
    {
        public const string PlayerFolder = "Images/Players/";
        public const string SilhouettePath = "UI/PlayerSilhouette";
        public const string FaceSuffix = "_face";
        public const string LiveSuffix = "_live";

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static Func<string, Sprite> _loader = LoadFromResources;
        static Sprite _silhouette;

        /// <summary>실제로 리소스를 찾은 횟수 (캐시 확인용).</summary>
        public static int LoadCount { get; private set; }

        /// <summary>
        /// 경로 → Sprite 로더 (테스트용으로 바꿀 수 있다). null을 넣으면 기본 로더(<see cref="Resources"/>)로 돌아간다.
        /// 바꾸면 캐시(실루엣 포함)를 비운다.
        /// </summary>
        public static Func<string, Sprite> Loader
        {
            get => _loader;
            set
            {
                _loader = value ?? LoadFromResources;
                _silhouette = null;
                ClearCache();
            }
        }

        /// <summary>Resources 경로 (확장자 없음): "Images/Players/p_삼성_2024_구자욱_face".</summary>
        public static string PathFor(string playerId, PlayerImageKind kind) =>
            PlayerFolder + playerId + (kind == PlayerImageKind.Live ? LiveSuffix : FaceSuffix);

        /// <summary>그 선수의 그 사진이 있는지 (Fallback 없이).</summary>
        public static bool Has(string playerId, PlayerImageKind kind) => Find(playerId, kind) != null;

        /// <summary>증명사진. 없으면 실루엣.</summary>
        public static Sprite Face(string playerId) => Find(playerId, PlayerImageKind.Face) ?? Silhouette;

        /// <summary>액션 사진. 없으면 증명사진, 그것도 없으면 실루엣.</summary>
        public static Sprite Live(string playerId) =>
            Find(playerId, PlayerImageKind.Live) ?? Find(playerId, PlayerImageKind.Face) ?? Silhouette;

        public static Sprite For(string playerId, PlayerImageKind kind) => kind == PlayerImageKind.Live ? Live(playerId) : Face(playerId);

        /// <summary>기본 실루엣 (리소스가 없으면 코드로 그린 회색 실루엣).</summary>
        public static Sprite Silhouette
        {
            get
            {
                if (_silhouette != null) return _silhouette;
                _silhouette = SafeLoad(SilhouettePath) ?? DrawSilhouette();
                return _silhouette;
            }
        }

        public static bool IsSilhouette(Sprite sprite) => sprite != null && sprite == Silhouette;

        /// <summary>
        /// 요소 배경에 사진을 건다. 같은 사진이면 다시 걸지 않는다. 실루엣이면 <c>player-photo--silhouette</c> 클래스를 붙인다.
        /// </summary>
        public static Sprite Apply(VisualElement element, string playerId, PlayerImageKind kind)
        {
            if (element == null) return null;
            var sprite = For(playerId, kind);
            if (element.userData as Sprite != sprite)
            {
                element.style.backgroundImage = new StyleBackground(sprite);
                element.userData = sprite;
            }
            element.EnableInClassList("player-photo--silhouette", IsSilhouette(sprite));
            return sprite;
        }

        public static void ClearCache()
        {
            Cache.Clear();
            LoadCount = 0;
        }

        static Sprite Find(string playerId, PlayerImageKind kind)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            string path = PathFor(playerId, kind);
            if (Cache.TryGetValue(path, out var cached)) return cached;
            var sprite = SafeLoad(path);
            Cache[path] = sprite;
            return sprite;
        }

        static Sprite SafeLoad(string path)
        {
            LoadCount++;
            try
            {
                return _loader(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ImageResourceManager] {path} 불러오기 실패: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sprite로 읽고, 스프라이트 모드가 Multiple이면 첫 조각, 텍스처로만 들어왔으면 Sprite를 만든다.
        /// </summary>
        static Sprite LoadFromResources(string path)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null) return sprite;
            var parts = Resources.LoadAll<Sprite>(path);
            if (parts != null && parts.Length > 0) return parts[0];
            var texture = Resources.Load<Texture2D>(path);
            return texture == null ? null
                : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0f));
        }

        /// <summary>코드로 그리는 실루엣 (머리 원 + 어깨 반원, 128×160 투명 배경).</summary>
        static Sprite DrawSilhouette()
        {
            const int w = 128, h = 160;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "PlayerSilhouette (generated)" };
            var clear = new Color(0, 0, 0, 0);
            var fill = new Color(0.55f, 0.6f, 0.68f, 0.85f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float hx = x - w / 2f, hy = y - 108f;
                bool head = hx * hx + hy * hy <= 30f * 30f;
                float sx = (x - w / 2f) / 58f, sy = y / 70f;
                bool shoulders = y <= 70 && sx * sx + (sy - 1f) * (sy - 1f) <= 1f;
                texture.SetPixel(x, y, head || shoulders ? fill : clear);
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0f));
        }
    }
}
