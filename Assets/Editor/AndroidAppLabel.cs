using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;

/// <summary>
/// The phone shows the game as "Wastelanders" under its icon. Every other platform takes its name from
/// Player Settings ▸ Product Name, and changing that would move the PC save folder (it's part of
/// persistentDataPath), so only the Android build's app label is renamed, after Unity writes the Gradle project.
/// </summary>
class AndroidAppLabel : IPostGenerateGradleAndroidProject
{
    public const string Label = "Wastelanders";

    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        var root = Directory.GetParent(unityLibraryPath).FullName;
        foreach (var module in new[] { "launcher", "unityLibrary" })
        {
            var strings = Path.Combine(root, module, "src", "main", "res", "values", "strings.xml");
            if (!File.Exists(strings)) continue;
            var xml = File.ReadAllText(strings);
            var renamed = Regex.Replace(xml, "(<string name=\"app_name\">)[^<]*(</string>)", "${1}" + Label + "${2}");
            if (renamed != xml) File.WriteAllText(strings, renamed);
        }
    }
}
