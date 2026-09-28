using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click: finds all 4 boss models in the current scene and rigs them with
/// CombatTarget + Enemy3D + boss-tier stats. Each boss gets progressively tougher
/// stats matching the wasteland-corner theme.
///
/// Menu: Wasteland ▸ Bosses ▸ Set Up All 4 Bosses
/// </summary>
public static class BossSetupTool
{
    // Names to search for (partial match, case-insensitive).
    static readonly (string match, string displayName, int hp, int atk, int def, int dmg, float scale)[] BossDefs =
    {
        ("greyface",   "Greyface the Hollow",     400,  90, 70, 30, 1.6f),
        ("lostwarrior","Gorek the Butcher",        550, 110, 85, 40, 1.7f),
        ("miyu",       "Miyu the Unchained",       700, 130, 100, 50, 1.8f),
        ("percival",   "Percival the Calamity",    900, 160, 120, 65, 2.0f),
    };

    [MenuItem("Wasteland/Bosses/Set Up All 4 Bosses")]
    public static void SetUpAllBosses()
    {
        var scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Boss Setup", "Save your scene first (Ctrl+S).", "OK");
            return;
        }

        int found = 0;
        foreach (var def in BossDefs)
        {
            var go = FindByName(def.match);
            if (go == null)
            {
                Debug.LogWarning($"[BossSetup] No GameObject matching '{def.match}' found in scene.");
                continue;
            }

            Undo.RegisterFullObjectHierarchyUndo(go, $"Set up boss: {def.displayName}");
            SetUpBoss(go, def);
            found++;
        }

        EditorSceneManager.MarkSceneDirty(scene);

        EditorUtility.DisplayDialog("Boss Setup",
            $"Set up {found}/4 bosses.\n\n" +
            (found == 4
                ? "All 4 are ready — CombatTarget (isBoss) + Enemy3D + boss-tier stats attached."
                : "Some bosses weren't found. Make sure the models are in the scene with their original names."),
            "Nice");
    }

    static void SetUpBoss(GameObject go, (string match, string displayName, int hp, int atk, int def, int dmg, float scale) def)
    {
        go.name = def.displayName;

        // ── collider ───────────────────────────────────────────────────
        var col = go.GetComponent<Collider>();
        if (col == null)
        {
            var cap = Undo.AddComponent<CapsuleCollider>(go);
            cap.height = 2.2f * def.scale;
            cap.radius = 0.55f * def.scale;
            cap.center = new Vector3(0f, 1.1f * def.scale, 0f);
        }

        // ── combat target (the stat block + death hooks) ───────────────
        var ct = go.GetComponent<CombatTarget>();
        if (ct == null) ct = Undo.AddComponent<CombatTarget>(go);
        ct.maxHP = def.hp;
        ct.attackLevel = def.atk;
        ct.defenceLevel = def.def;
        ct.maxDamage = def.dmg;
        ct.isBoss = true;
        ct.isAggressive = true;
        ct.isDummy = false;

        // ── AI (chase + attack the player) ─────────────────────────────
        if (go.GetComponent<Enemy3D>() == null)
            Undo.AddComponent<Enemy3D>(go);

        // ── scale the model up to boss size ────────────────────────────
        go.transform.localScale = Vector3.one * def.scale;

        // ── clean up stray lights/cameras from the FBX import ──────────
        foreach (var cam in go.GetComponentsInChildren<Camera>(true))
            if (cam != null) Undo.DestroyObjectImmediate(cam.gameObject);
        foreach (var lt in go.GetComponentsInChildren<Light>(true))
            if (lt != null) Undo.DestroyObjectImmediate(lt.gameObject);
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true))
            if (al != null) Undo.DestroyObjectImmediate(al);

        // ── ensure renderers stay visible at distance ──────────────────
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;

        Debug.Log($"[BossSetup] {def.displayName} — {def.hp} HP, {def.atk} ATK, {def.def} DEF, {def.dmg} DMG, ×{def.scale}");
    }

    static GameObject FindByName(string partial)
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name.IndexOf(partial, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return go;
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.IndexOf(partial, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return child.gameObject;
            }
        }
        return null;
    }
}
