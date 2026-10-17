using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace DailyFranchise.UI.Tests
{
    /// <summary>Phase 43: 어떤 화면 비율에서도 1920×1080 레이아웃을 16:9 그대로(찌그러짐 없이) 가운데에 둔다.</summary>
    public class ScreenLetterboxTests
    {
        // 갤럭시 Z 폴드 커버(가로 약 23:9) · 메인(약 5:4) · 일반 20:9 폰 · 16:9 · 4:3 태블릿
        static readonly Vector2[] Screens =
        {
            new Vector2(2316, 904), new Vector2(2176, 1812), new Vector2(2400, 1080),
            new Vector2(1920, 1080), new Vector2(2048, 1536), new Vector2(1280, 720),
        };

        [Test]
        public void 모든_화면에서_게임_영역은_16대9이고_화면_안_가운데에_있다()
        {
            foreach (var screen in Screens)
            {
                var r = ScreenLetterbox.ContentPixels(screen);
                Assert.That(r.width / r.height, Is.EqualTo(16f / 9f).Within(1e-4f), $"{screen}: 비율 유지");
                Assert.That(r.xMin, Is.GreaterThanOrEqualTo(-0.01f), $"{screen}");
                Assert.That(r.yMin, Is.GreaterThanOrEqualTo(-0.01f), $"{screen}");
                Assert.That(r.xMax, Is.LessThanOrEqualTo(screen.x + 0.01f), $"{screen}");
                Assert.That(r.yMax, Is.LessThanOrEqualTo(screen.y + 0.01f), $"{screen}");
                Assert.That(r.xMin, Is.EqualTo(screen.x - r.xMax).Within(0.01f), $"{screen}: 좌우 여백 같음");
                Assert.That(r.yMin, Is.EqualTo(screen.y - r.yMax).Within(0.01f), $"{screen}: 상하 여백 같음");
                // 한쪽 축은 꽉 찬다 (가능한 가장 크게).
                bool fillsWidth = Mathf.Abs(r.width - screen.x) < 0.01f;
                bool fillsHeight = Mathf.Abs(r.height - screen.y) < 0.01f;
                Assert.That(fillsWidth || fillsHeight, Is.True, $"{screen}: 최대 배율");
            }
        }

        [Test]
        public void 커버_화면은_좌우_필러박스_메인_화면은_상하_레터박스()
        {
            var cover = ScreenLetterbox.ContentPixels(new Vector2(2316, 904));
            Assert.That(cover.height, Is.EqualTo(904f).Within(0.01f));
            Assert.That(cover.x, Is.GreaterThan(0f));
            Assert.That(cover.y, Is.EqualTo(0f).Within(0.01f));

            var main = ScreenLetterbox.ContentPixels(new Vector2(2176, 1812));
            Assert.That(main.width, Is.EqualTo(2176f).Within(0.01f));
            Assert.That(main.y, Is.GreaterThan(0f));
            Assert.That(main.x, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void 패널이_기준보다_크면_가운데_정확히_기준이면_원점()
        {
            Assert.That(ScreenLetterbox.Fit(new Vector2(1920, 1080)), Is.EqualTo(new Rect(0, 0, 1920, 1080)));
            Assert.That(ScreenLetterbox.Fit(new Vector2(2920, 1080)), Is.EqualTo(new Rect(500, 0, 1920, 1080)));
            Assert.That(ScreenLetterbox.Fit(new Vector2(1920, 1480)), Is.EqualTo(new Rect(0, 200, 1920, 1080)));
            Assert.That(ScreenLetterbox.Scale(new Vector2(3840, 2160)), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void 패널_설정은_기준_해상도_1920x1080_Expand_모드다()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/GameScreenPanelSettings.asset");
            Assert.That(ps, Is.Not.Null);
            Assert.That(ps.scaleMode, Is.EqualTo(PanelScaleMode.ScaleWithScreenSize));
            Assert.That(ps.screenMatchMode, Is.EqualTo(PanelScreenMatchMode.Expand), "늘이거나 잘리지 않게 배율 = min(가로비, 세로비)");
            Assert.That(ps.referenceResolution, Is.EqualTo(new Vector2Int(1920, 1080)));
        }

        [Test]
        public void 안드로이드_플레이어_설정은_가로_전용_고유_패키지_폴드_최소_API다()
        {
            Assert.That(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android), Is.EqualTo("com.jangyunkimsw.dailyfranchise"));
            Assert.That(PlayerSettings.allowedAutorotateToPortrait, Is.False);
            Assert.That(PlayerSettings.allowedAutorotateToPortraitUpsideDown, Is.False);
            Assert.That(PlayerSettings.allowedAutorotateToLandscapeLeft, Is.True);
            Assert.That(PlayerSettings.allowedAutorotateToLandscapeRight, Is.True);
            Assert.That(PlayerSettings.Android.minSdkVersion, Is.EqualTo(AndroidSdkVersions.AndroidApiLevel28));
            Assert.That(PlayerSettings.Android.resizeableActivity, Is.True, "폴더블 접기·펴기에 재시작 없이 대응");
        }
    }
}
