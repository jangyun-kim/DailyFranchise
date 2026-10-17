using System.Collections.Generic;
using DailyFranchise.App;
using DailyFranchise.Sim.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>
    /// Phase 38.5: 선수 사진 로더 — 파일 이름 규칙({id}_face / {id}_live), 캐시, 실루엣 Fallback, UI 요소 배경 바인딩.
    /// 로더를 가짜로 바꿔 사진 폴더(.gitignore로 저장소 밖)가 없어도 돈다. 실제 사진 확인은 사진이 있을 때만.
    /// </summary>
    public class PlayerImageTests
    {
        const string Gu = "p_삼성_2024_구자욱";
        readonly List<string> _requested = new List<string>();
        readonly List<Object> _created = new List<Object>();
        Dictionary<string, Sprite> _files;

        Sprite MakeSprite(string name)
        {
            var texture = new Texture2D(4, 5) { name = name };
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 5), Vector2.zero);
            sprite.name = name;
            _created.Add(texture);
            _created.Add(sprite);
            return sprite;
        }

        [SetUp]
        public void SetUp()
        {
            _requested.Clear();
            _created.Clear();
            _files = new Dictionary<string, Sprite>
            {
                [ImageResourceManager.PlayerFolder + Gu + "_face"] = MakeSprite("gu_face"),
                [ImageResourceManager.PlayerFolder + Gu + "_live"] = MakeSprite("gu_live"),
                [ImageResourceManager.PlayerFolder + "p_삼성_2024_강민호_face"] = MakeSprite("kang_face"),
            };
            ImageResourceManager.Loader = path =>
            {
                _requested.Add(path);
                return _files.TryGetValue(path, out var s) ? s : null;
            };
        }

        [TearDown]
        public void TearDown()
        {
            ImageResourceManager.Loader = null;
            foreach (var o in _created) Object.DestroyImmediate(o);
        }

        [Test]
        public void 파일_경로는_Resources_Images_Players_아래_선수ID_face_live다()
        {
            Assert.That(ImageResourceManager.PathFor(Gu, PlayerImageKind.Face), Is.EqualTo("Images/Players/p_삼성_2024_구자욱_face"));
            Assert.That(ImageResourceManager.PathFor(Gu, PlayerImageKind.Live), Is.EqualTo("Images/Players/p_삼성_2024_구자욱_live"));
        }

        [Test]
        public void 사진이_있으면_그_사진을_돌려주고_한_번_읽은_사진은_다시_읽지_않는다()
        {
            Assert.That(ImageResourceManager.Face(Gu).name, Is.EqualTo("gu_face"));
            Assert.That(ImageResourceManager.Live(Gu).name, Is.EqualTo("gu_live"));
            int loads = _requested.Count;
            for (int i = 0; i < 50; i++)
            {
                ImageResourceManager.Face(Gu);
                ImageResourceManager.Live(Gu);
            }
            Assert.That(_requested.Count, Is.EqualTo(loads), "타석마다 다시 불러오지 않는다 (캐시)");
            Assert.That(_requested, Is.Unique);
        }

        [Test]
        public void 액션_사진이_없으면_증명사진_둘_다_없으면_실루엣이고_없는_파일도_다시_찾지_않는다()
        {
            Assert.That(ImageResourceManager.Live("p_삼성_2024_강민호").name, Is.EqualTo("kang_face"), "Live → Face");
            var old = ImageResourceManager.Face("p_삼성_1999_가상선수");
            Assert.That(old, Is.Not.Null, "크래시 대신 실루엣");
            Assert.That(ImageResourceManager.IsSilhouette(old), Is.True);
            Assert.That(ImageResourceManager.IsSilhouette(ImageResourceManager.Live("p_삼성_1999_가상선수")), Is.True);
            Assert.That(ImageResourceManager.IsSilhouette(ImageResourceManager.Face(null)), Is.True, "선수 없음(더미 자리)");
            Assert.That(ImageResourceManager.IsSilhouette(ImageResourceManager.Face("")), Is.True);
            Assert.That(ImageResourceManager.Has("p_삼성_1999_가상선수", PlayerImageKind.Face), Is.False);
            Assert.That(ImageResourceManager.Has(Gu, PlayerImageKind.Live), Is.True);

            int loads = _requested.Count;
            ImageResourceManager.Face("p_삼성_1999_가상선수");
            ImageResourceManager.Live("p_삼성_1999_가상선수");
            Assert.That(_requested.Count, Is.EqualTo(loads), "없는 사진도 캐시");
        }

        [Test]
        public void 로더가_예외를_던져도_실루엣으로_버틴다()
        {
            ImageResourceManager.Loader = path => throw new System.IO.IOException("깨진 파일");
            Assert.That(ImageResourceManager.IsSilhouette(ImageResourceManager.Live(Gu)), Is.True);
        }

        [Test]
        public void 실루엣은_Resources_UI_PlayerSilhouette이고_없어도_코드로_그린_실루엣이_나온다()
        {
            ImageResourceManager.Loader = null; // 실제 Resources
            var silhouette = ImageResourceManager.Silhouette;
            Assert.That(silhouette, Is.Not.Null);
            Assert.That(silhouette.texture, Is.Not.Null);
            Assert.That(Resources.Load<Texture2D>(ImageResourceManager.SilhouettePath), Is.Not.Null, "저장소에 들어 있는 기본 실루엣");
        }

        [Test]
        public void 요소에_사진을_걸면_배경이_바뀌고_실루엣이면_표시_클래스가_붙는다()
        {
            var el = new VisualElement();
            var sprite = ImageResourceManager.Apply(el, Gu, PlayerImageKind.Live);
            Assert.That(el.style.backgroundImage.value.sprite, Is.SameAs(sprite));
            Assert.That(sprite.name, Is.EqualTo("gu_live"));
            Assert.That(el.ClassListContains("player-photo--silhouette"), Is.False);

            ImageResourceManager.Apply(el, "p_없음", PlayerImageKind.Live);
            Assert.That(el.style.backgroundImage.value.sprite, Is.SameAs(ImageResourceManager.Silhouette));
            Assert.That(el.ClassListContains("player-photo--silhouette"), Is.True);
            Assert.That(ImageResourceManager.Apply(null, Gu, PlayerImageKind.Face), Is.Null, "요소가 없으면 무시");
        }

        [Test]
        public void 로비_얼굴_칸은_선수_사진을_걸고_선수가_없으면_실루엣()
        {
            var cell = LobbyController.FaceCell(new PlayerData { Id = Gu, Name = "구자욱" });
            Assert.That(cell.name, Is.EqualTo("face-" + Gu));
            Assert.That(cell.ClassListContains("col-face"), Is.True);
            Assert.That(cell.style.backgroundImage.value.sprite.name, Is.EqualTo("gu_face"));
            Assert.That(cell.pickingMode, Is.EqualTo(PickingMode.Ignore), "행 클릭(선수 상세)을 가로채지 않는다");
            var empty = LobbyController.FaceCell(null);
            Assert.That(empty.ClassListContains("player-photo--silhouette"), Is.True);
        }

        [Test]
        public void 실제_사진_폴더가_있으면_2024년_구자욱_증명사진을_불러온다()
        {
            ImageResourceManager.Loader = null;
            Assume.That(Resources.LoadAll<Texture2D>(ImageResourceManager.PathFor(Gu, PlayerImageKind.Face)).Length
                        + Resources.LoadAll<Sprite>(ImageResourceManager.PathFor(Gu, PlayerImageKind.Face)).Length,
                Is.GreaterThan(0), "사진 폴더는 .gitignore — 로컬에 있을 때만 확인");
            var face = ImageResourceManager.Face(Gu);
            Assert.That(ImageResourceManager.IsSilhouette(face), Is.False);
            Assert.That(face.rect.width, Is.GreaterThan(100));
            Assert.That(ImageResourceManager.IsSilhouette(ImageResourceManager.Live(Gu)), Is.False, "액션 사진이 없으면 증명사진");
        }
    }
}
