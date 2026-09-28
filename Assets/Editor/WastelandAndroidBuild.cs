using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// One-click phone builds. Builds the scenes enabled in Build Settings (character select + Broken
/// Crescent) into F:/UnityProjects/Builds/Wasteland.apk.
///   • Build And Run installs and launches it on the phone plugged in over USB (USB debugging on).
///   • Build APK just writes the file, to copy onto a phone by hand.
/// The build starts on the next editor tick, so it can be kicked off from tools and scripts too.
/// The result is logged with a "[WASTELAND BUILD]" prefix.
/// The data is LZ4HC-compressed: an APK can't go past 4 GB, and the phone installs it faster.
/// See also AndroidTexturePolicy and AndroidLodStrip, which keep the phone build small.
/// </summary>
public static class WastelandAndroidBuild
{
    public const string ApkPath = "F:/UnityProjects/Builds/Wasteland.apk";
    static bool _queued, _run;

    [MenuItem("Wasteland/Build/Android: Build And Run")]
    public static void BuildAndRunMenu() => Queue(true);

    [MenuItem("Wasteland/Build/Android: Build APK")]
    public static void BuildApkMenu() => Queue(false);

    public static void Queue(bool run)
    {
        if (_queued) return;
        _queued = true; _run = run;
        EditorApplication.update += Tick;
        Debug.Log($"[WASTELAND BUILD] queued ({(run ? "build and run" : "APK only")})");
    }

    static void Tick()
    {
        EditorApplication.update -= Tick;
        _queued = false;
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.LogError("[WASTELAND BUILD] switch the project to Android first (File ▸ Build Profiles).");
            return;
        }
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = (_run ? BuildOptions.AutoRunPlayer : BuildOptions.None) | BuildOptions.CompressWithLz4HC,
        });
        var s = report.summary;
        string msg = $"[WASTELAND BUILD] {s.result}: {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalWarnings} warnings, took {s.totalTime:hh\\:mm\\:ss} → {ApkPath}";
        if (s.result == BuildResult.Succeeded) Debug.Log(msg); else Debug.LogError(msg);
    }
}
