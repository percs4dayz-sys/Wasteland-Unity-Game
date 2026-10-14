// Minimal stand-ins for the UnityEngine surface the data scripts touch. Nothing here runs game logic.
namespace UnityEngine
{
    public class Object { public string name; }
    public class Sprite : Object { }
    public class GameObject : Object { }
    public class MonoBehaviour : Object { }
    public class ScriptableObject : Object { }
    public static class Debug
    {
        public static void Log(object o) { }
        public static void LogWarning(object o) { System.Console.Error.WriteLine("WARN " + o); }
        public static void LogError(object o) { System.Console.Error.WriteLine("ERROR " + o); }
    }
    public static class Mathf
    {
        public static int Clamp(int v, int a, int b) => System.Math.Clamp(v, a, b);
        public static float Clamp(float v, float a, float b) => System.Math.Clamp(v, a, b);
        public static float Clamp01(float v) => System.Math.Clamp(v, 0f, 1f);
        public static int Max(int a, int b) => System.Math.Max(a, b);
        public static float Max(float a, float b) => System.Math.Max(a, b);
        public static int Min(int a, int b) => System.Math.Min(a, b);
        public static float Min(float a, float b) => System.Math.Min(a, b);
        public static int RoundToInt(float f) => (int)System.Math.Round(f);
        public static int FloorToInt(float f) => (int)System.Math.Floor(f);
        public static int CeilToInt(float f) => (int)System.Math.Ceiling(f);
    }
    public static class Random
    {
        static readonly System.Random R = new(1);
        public static int Range(int a, int b) => R.Next(a, b);
        public static float Range(float a, float b) => a + (float)R.NextDouble() * (b - a);
        public static float value => (float)R.NextDouble();
    }
    public static class Resources
    {
        public static T Load<T>(string p) where T : Object => null;
        /// <summary>Root of the Unity Resources folder; set by Program so LoadAll can mirror the real
        /// asset filenames (ItemRegistry derives auto-items from HeldModels filenames).</summary>
        public static string Root;
        public static T[] LoadAll<T>(string p) where T : Object
        {
            if (Root == null || typeof(T) != typeof(GameObject)) return System.Array.Empty<T>();
            string dir = System.IO.Path.Combine(Root, p);
            if (!System.IO.Directory.Exists(dir)) return System.Array.Empty<T>();
            var list = new System.Collections.Generic.List<T>();
            foreach (var f in System.IO.Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".prefab" or ".fbx" or ".obj" or ".glb" or ".gltf" or ".blend")
                    list.Add((T)(Object)new GameObject { name = System.IO.Path.GetFileNameWithoutExtension(f) });
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list.ToArray();
        }
    }
    public enum RuntimeInitializeLoadType { BeforeSceneLoad, AfterSceneLoad }
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } public RuntimeInitializeOnLoadMethodAttribute() { } }
    public class TooltipAttribute : System.Attribute { public TooltipAttribute(string s) { } }
    public class HeaderAttribute : System.Attribute { public HeaderAttribute(string s) { } }
    public class SerializeField : System.Attribute { }
    public class RangeAttribute : System.Attribute { public RangeAttribute(float a, float b) { } }
}

// Gameplay types referenced by helper methods in the data files (never called by the exporter).
public class PlayerStats { public int GetLevel(Skill s) => 1; }
public class PlayerEntity
{
    public Inventory Inventory = new();
    public Equipment Equipment = new();
    public PlayerStats Stats = new();
}
public static class RoxyNPC { public const string FissionGivenFlag = "roxy_fission_given"; }
