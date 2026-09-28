using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists the player to disk (Application.persistentDataPath/wasteland_save.json).
/// Auto-spawns so it works in EVERY scene. Survives scene changes, autosaves every few seconds +
/// on quit, and loads the save into the player as soon as one appears in the scene.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    /// <summary>Canonical save path — static so callers (splash screen, dev tools) can check for a
    /// save before the SaveManager instance has spawned.</summary>
    public static string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileNameFor(
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));

    /// <summary>Separate worlds keep separate saves, so playing one never moves or overwrites a
    /// character in another.</summary>
    static string SaveFileNameFor(string sceneName)
    {
        if (sceneName.StartsWith(BlackwaterAtlas.SceneName, System.StringComparison.Ordinal))
            return "wasteland_blackwater_save.json";
        if (sceneName.StartsWith("Overworld_BrokenCrescent", System.StringComparison.Ordinal))
            return "wasteland_brokencrescent_save.json";
        return "wasteland_save.json";
    }

    /// <summary>True when a save file is on disk. Use this to tell a "new game" from a "continue".</summary>
    public static bool SaveExists() => File.Exists(SaveFilePath);

    /// <summary>Is there a save for that world? (SaveExists asks about the active scene, which on the title
    /// screen is the title screen itself.)</summary>
    public static bool SaveExistsFor(string sceneName) =>
        File.Exists(Path.Combine(Application.persistentDataPath, SaveFileNameFor(sceneName)));

    private string SavePath => SaveFilePath;
    private float _autosaveTimer;
    private const float AUTOSAVE_INTERVAL = 3f;
    private PlayerEntity _loadedFor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("SaveManager (auto)").AddComponent<SaveManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        var player = PlayerEntity.Instance;

        // Load the save into a freshly-arrived player.
        if (player != null && player != _loadedFor)
        {
            _loadedFor = player;
            Load()?.ApplyTo(player);
        }

        if (player == null) return;
        _autosaveTimer += Time.deltaTime;
        if (_autosaveTimer >= AUTOSAVE_INTERVAL) { _autosaveTimer = 0f; Save(); }
    }

    void OnApplicationQuit() => Save();

    // Phones rarely get a real quit — the app is backgrounded, then killed. Save the moment that happens.
    void OnApplicationPause(bool paused) { if (paused) Save(); }

    public bool HasSave() => File.Exists(SavePath);

    public SaveData Load()
    {
        if (!HasSave()) return null;
        try
        {
            return JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        }
        catch
        {
            File.Delete(SavePath);
            return null;
        }
    }

    public void Save()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;
        try
        {
            string json = JsonUtility.ToJson(SaveData.Capture(player), true);
            string tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(tmp, SavePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] Save failed: {e.Message}");
        }
    }

    public void DeleteSave()
    {
        if (HasSave()) File.Delete(SavePath);
    }

    // Called from character select, whose active scene uses a different save filename.
    // Archive the destination world's previous run before starting at its authored spawn.
    public static void StartNewRun(string sceneName)
    {
        string path = Path.Combine(Application.persistentDataPath, SaveFileNameFor(sceneName));
        if (File.Exists(path))
            File.Move(path, path + ".before-new-character-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak");
        if (Instance != null)
        {
            Instance._loadedFor = null;
            Instance._autosaveTimer = 0f;
        }
    }
}
