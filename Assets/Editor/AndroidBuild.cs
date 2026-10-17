using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DailyFranchise.EditorTools
{
    /// <summary>
    /// Phase 43: 안드로이드(갤럭시 Z 폴드 포함) Alpha APK 빌드.
    /// 메뉴 DailyFranchise/Android, 또는 배치 모드:
    /// Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod DailyFranchise.EditorTools.AndroidBuild.BuildApk
    /// </summary>
    public static class AndroidBuild
    {
        public const string PackageName = "com.jangyunkimsw.dailyfranchise";
        public const string Version = "0.43.0";
        public const int VersionCode = 43;
        /// <summary>Galaxy Z Fold(1세대) 출시 OS가 Android 9(API 28).</summary>
        public const AndroidSdkVersions MinSdk = AndroidSdkVersions.AndroidApiLevel28;
        public const string OutputPath = "Builds/Android/DailyFranchise-alpha.apk";

        [MenuItem("DailyFranchise/Android/Apply Player Settings")]
        public static void ApplySettings()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, PackageName);
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = VersionCode;
            PlayerSettings.Android.minSdkVersion = MinSdk;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // 가로 전용: 왼쪽/오른쪽 가로만 자동 회전.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // 폴더블: 접기·펴기 때 앱을 재시작하지 않고 화면 크기만 바꾼다 (ScreenLetterbox가 다시 맞춘다).
            PlayerSettings.Android.resizeableActivity = true;
            // 카메라 펀치홀·노치 영역은 OS가 비워 두게 해서 레터박스 계산이 안전 영역 안에서만 일어나게 한다.
            PlayerSettings.Android.renderOutsideSafeArea = false;
            // 폴드 커버 화면(약 23.1:9 = 2.57)도 OS 레터박스 없이 전체 화면을 받는다.
            PlayerSettings.Android.maxAspectRatio = 2.7f;
            AssetDatabase.SaveAssets();
        }

        [MenuItem("DailyFranchise/Android/Build Alpha APK")]
        public static void BuildApk()
        {
            ApplySettings();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[AndroidBuild] {summary.result} · {summary.outputPath} · {summary.totalSize / (1024f * 1024f):0.0} MB · {summary.totalTime}");
            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
