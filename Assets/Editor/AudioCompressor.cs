using UnityEditor;
using UnityEngine;
using System.Text;

/// <summary>
/// One-click audio size cut for mobile. Everything under Resources/Music + Resources/SFX ships in the
/// build (Resources always does), and uncompressed WAV there is the single biggest bloat. This sets
/// every clip to Vorbis with a sane quality, music to streaming (decoded on the fly, tiny memory) and
/// short SFX to compressed-in-memory, with an Android override so the device build is small.
///
/// Safe & reversible — it only changes import settings, never moves or deletes assets. Re-run anytime.
///   Wasteland ▸ Build ▸ Compress Audio for Mobile
/// </summary>
public static class AudioCompressor
{
    [MenuItem("Wasteland/Build/Compress Audio for Mobile")]
    public static void Compress()
    {
        var log = new StringBuilder();
        int done = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not AudioImporter ai) continue;

            // Music streams (low memory); everything else loads compressed in memory.
            bool isMusic = path.Replace('\\', '/').Contains("/Music/");

            var s = ai.defaultSampleSettings;
            s.loadType          = isMusic ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality           = isMusic ? 0.4f : 0.5f;
            ai.defaultSampleSettings = s;

            // Android override (same settings, explicitly applied for the player build).
            var a = s;
            ai.SetOverrideSampleSettings("Android", a);

            ai.forceToMono = false;
            ai.loadInBackground = isMusic;

            ai.SaveAndReimport();
            log.AppendLine($"  {(isMusic ? "stream" : "mem")}  {path}");
            done++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[AudioCompressor] Re-encoded {done} clip(s) to Vorbis.\n{log}");
        EditorUtility.DisplayDialog("Compress Audio for Mobile",
            $"Re-encoded {done} audio clip(s) under Assets/Resources to Vorbis.\n\n" +
            "Music = streaming, SFX = compressed in memory. This should shrink the ~580 MB of audio " +
            "down to tens of MB. Build again to see the smaller APK.", "Nice");
    }
}
