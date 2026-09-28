using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene transitions + a debug warp. Press F12 to jump straight to the main world
/// (handy for worldbuilding without grinding the tutorial). The Portal uses the same
/// loader when you step through it. Auto-spawned; no setup.
/// </summary>
public class SceneWarp : MonoBehaviour
{
    /// <summary>Name of the main-world scene (must exist & be in Build Settings).</summary>
    public const string MainWorldScene = "MainWorld";
    /// <summary>Tutorial island scene — F12 toggles back to it from the mainland.</summary>
    public const string TutorialScene = "TutorialIsland3D";
    public const KeyCode WarpKey = KeyCode.F12;
    public const KeyCode TestLevelsKey = KeyCode.F11;   // debug: jump all skills to level 5
    public const KeyCode RangedKitKey = KeyCode.F10;    // debug: grant + equip a Pipe Pistol & ammo
    public const int TestLevel = 5;

    // Set just before a warp; the next sceneLoaded seats the player on the ground at its authored
    // spawn point so a warp never drops you into the void / through the map.
    static bool _seatOnNextLoad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Object.FindAnyObjectByType<SceneWarp>() != null) return;
        var go = new GameObject("SceneWarp (auto)");
        go.AddComponent<SceneWarp>();
        DontDestroyOnLoad(go);
    }

    void OnEnable()  => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_seatOnNextLoad) return;
        _seatOnNextLoad = false;
        SeatPlayerOnGround();
    }

    /// <summary>Drop the player straight down onto the nearest walkable surface at its authored spawn
    /// XZ. Runs right after a warp (before PlayerRespawn caches its spawn), so the destination spawn
    /// point is always grounded instead of floating in space.</summary>
    static void SeatPlayerOnGround()
    {
        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc == null) return;
        var t  = pc.transform;
        var cc = pc.GetComponent<CharacterController>();
        Vector3 p = t.position;

        var hits = Physics.RaycastAll(new Ray(new Vector3(p.x, 5000f, p.z), Vector3.down), 20000f,
                                      ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(t)) continue;   // skip our own capsule
            if (h.normal.y < 0.2f) continue;                   // walkable top, not a wall/underside
            float bottomOffset = cc != null ? cc.center.y - cc.height * 0.5f : 0f;
            bool wasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;                // move without the controller fighting it
            t.position = new Vector3(p.x, h.point.y - bottomOffset + 0.05f, p.z);
            if (cc != null) cc.enabled = wasEnabled;
            return;
        }
    }

    void Update()
    {
#if !UNITY_ANDROID
        // F12 tutorial<->mainland warp is a desktop-only debug shortcut; excluded from Android builds.
        if (Input.GetKeyDown(WarpKey)) ToggleWorld();
#endif
        if (Input.GetKeyDown(TestLevelsKey)) GrantTestLevels();
        if (Input.GetKeyDown(RangedKitKey)) GrantRangedKit();
    }

    /// <summary>Debug F12: warp to the mainland from anywhere. (The tutorial island is retired —
    /// if it's still in Build Settings this bounces to it for archaeology, otherwise it's a no-op
    /// on the mainland.)</summary>
    static void ToggleWorld()
    {
        bool onMainland = SceneManager.GetActiveScene().name.StartsWith(MainWorldScene);
        if (onMainland) WarpToTutorial();   // safely refuses when the island scene no longer exists
        else            WarpToMainWorld();
    }

    /// <summary>Loads the tutorial island scene if it still exists in Build Settings; otherwise just
    /// says so. Kept only as a debug convenience — nothing in the game flow depends on it.</summary>
    public static void WarpToTutorial()
    {
        if (Application.CanStreamedLevelBeLoaded(TutorialScene))
        {
            HUDController.Instance?.AddChatLine("<color=#88DDFF>[WARP]:</color> Back to the tutorial island...");
            _seatOnNextLoad = true;
            SceneManager.LoadScene(TutorialScene);
        }
        else
        {
            HUDController.Instance?.AddChatLine(
                "<color=#88DDFF>[WARP]:</color> The tutorial island is gone — you're already home.");
        }
    }

    /// <summary>Debug: hand over a Pipe Pistol + 100 Scrap Rounds, already equipped, for testing ranged.</summary>
    static void GrantRangedKit()
    {
        var p = PlayerEntity.Instance;
        if (p == null) return;

        if (!p.Inventory.Contains(12)) p.Inventory.Add(12); // Pipe Pistol
        p.Equip(12);

        p.Inventory.Add(13, 100);                           // Scrap Rounds
        p.Equip(13);                                        // loads the whole stack into the Ammo slot

        HUDController.Instance?.AddChatLine("<color=#88DDFF>[DEBUG]:</color> Granted Pipe Pistol + 100 Scrap Rounds (equipped).");
    }

    /// <summary>Debug: instantly raise every skill to a testing level (opens the portal).</summary>
    static void GrantTestLevels()
    {
        var p = PlayerEntity.Instance;
        if (p == null) return;
        int target = XPTable.XPForLevel(TestLevel);
        foreach (Skill s in System.Enum.GetValues(typeof(Skill)))
        {
            int cur = p.Stats.GetXP(s);
            if (target > cur) p.Stats.AddXP(s, target - cur);   // fires level-up so the portal/HUD refresh
        }
        HUDController.Instance?.AddChatLine($"<color=#88DDFF>[DEBUG]:</color> All skills set to level {TestLevel}.");
    }

    /// <summary>Loads the 3D world if it exists, else the 2D MainWorld, else explains setup.</summary>
    public static void WarpToMainWorld()
    {
        string target =
            Application.CanStreamedLevelBeLoaded(CharacterSelectUI.StartScene) ? CharacterSelectUI.StartScene :
            Application.CanStreamedLevelBeLoaded("MainWorld3D") ? "MainWorld3D" :
            Application.CanStreamedLevelBeLoaded(MainWorldScene) ? MainWorldScene : null;

        if (target != null)
        {
            HUDController.Instance?.AddChatLine("<color=#88DDFF>[WARP]:</color> Stepping into the wasteland...");
            _seatOnNextLoad = true;
            SceneManager.LoadScene(target);
        }
        else
        {
            string msg = "No main world scene yet. Run 'Wasteland > Build 3D World (New Scene)' in the editor, " +
                         "or create a 'MainWorld' scene and add it in Build Settings.";
            HUDController.Instance?.AddChatLine($"<color=#FF8080>[WARP]:</color> {msg}");
            Debug.LogWarning($"[SceneWarp] {msg}");
        }
    }
}
