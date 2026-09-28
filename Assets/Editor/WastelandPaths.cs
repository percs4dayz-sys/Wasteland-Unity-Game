using System.IO;
using UnityEditor;

/// <summary>
/// Central place for where the Wasteland-made assets live. Supports the "organize" move:
/// every lookup prefers the new _Wasteland location but falls back to the old Art/Generated3D
/// location, so the tools keep working whether or not you've run "Organize My Files" yet.
/// </summary>
public static class WastelandPaths
{
    public const string Root      = "Assets/_Wasteland";
    public const string Models    = Root + "/Models";
    public const string Materials = Root + "/Materials";
    public const string Shaders   = Root + "/Shaders";
    public const string Textures  = Root + "/Textures";

    public const string Legacy = "Assets/Art/Generated3D";

    /// <summary>Returns the new path if that file exists, else the legacy path if THAT exists, else the new path.</summary>
    public static string Resolve(string preferred, string legacy)
        => File.Exists(preferred) ? preferred : (File.Exists(legacy) ? legacy : preferred);

    /// <summary>Creates a nested Assets/... folder (and any missing parents) if it doesn't exist.</summary>
    public static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf   = Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
