using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Turns the enemy PLACEHOLDER MARKERS that WorldContentPlanner drops — the giant MAJOR BOSS
/// cube, the MINIBOSS cubes, and the AGGRO PACK cubes + their cluster dots — into real,
/// fightable <see cref="Enemy3D"/> objects with Kenney visuals. Each one is auto-normalized to
/// a consistent height by <see cref="HeightNormalizer3D"/>: boss biggest, miniboss mid,
/// mobs human-sized — so they're no longer "all over the place".
///
/// Resource / NPC / station markers are left untouched. Identification:
///   • by name  — "MAJOR BOSS" / "MINIBOSS" / "AGGRO PACK" labelled markers
///   • by material — unlabelled cluster dots painted with the aggro material (CMarker_932)
/// Tier (drives stats) is parsed from the marker label ("T3", "Tier 5", …).
///
/// Spawned enemies live under a root "Enemies" group so re-running "Plan Main World Content"
/// (which only rebuilds the "WorldContent" group) won't wipe them. Idempotent: the "Enemies"
/// group is cleared before each conversion, so re-running never stacks duplicates.
///
/// Menu:  Wasteland ▸ Enemies ▸ Replace Enemy Markers with Real Enemies
/// </summary>
public static class EnemyMarkerReplacer
{
    // Mirror WorldContentPlanner's enemy colours (used to spot unlabelled aggro dots).
    static readonly Color cAggro = new Color(0.95f, 0.35f, 0.20f);
    static readonly Color cMini  = new Color(1.00f, 0.32f, 0.26f);

    enum Kind { Mob, Mini, Boss }

    [MenuItem("Wasteland/Enemies/Replace Enemy Markers with Real Enemies")]
    public static void Replace()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Collect targets first — don't mutate the hierarchy while scanning it.
        var targets = new List<(GameObject go, Kind kind, int tier)>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>())
        {
            var go = r.gameObject;
            if (go.GetComponentInParent<Enemy3D>() != null) continue;   // already a real enemy
            if (TryClassify(go, r, out var kind, out int tier))
                targets.Add((go, kind, tier));
        }

        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("Replace Enemy Markers",
                "No enemy markers found in the open scene.\n\nRun 'Wasteland ▸ Plan Main World Content' " +
                "first to lay down the BOSS / MINIBOSS / AGGRO PACK markers, then run this.", "OK");
            return;
        }

        EnemyVisualBuilder.EnsureAssets();   // make sure rigs / controllers / skins exist

        // Fresh "Enemies" group so re-runs don't stack duplicates.
        var oldRoot = GameObject.Find("Enemies");
        if (oldRoot != null) Object.DestroyImmediate(oldRoot);
        var root = new GameObject("Enemies").transform;

        int idx = 0;
        foreach (var (go, kind, tier) in targets)
        {
            var p = go.transform.position;
            SpawnEnemy(NameFor(go, kind), new Vector3(p.x, 0f, p.z), root, idx++, kind, tier);
            Object.DestroyImmediate(go);   // remove the marker (its Label child goes with it)
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root.gameObject;
        EditorGUIUtility.PingObject(root.gameObject);
        EditorUtility.DisplayDialog("Replace Enemy Markers",
            $"Replaced {idx} enemy marker{(idx == 1 ? "" : "s")} with real Enemy3D enemies under the " +
            "'Enemies' group.\n\nPress Play — they idle, chase, and fight, each normalized to a " +
            "consistent height (boss biggest, mobs human-sized). Save the scene (Ctrl+S).", "Nice");
    }

    /// <summary>Is this renderer's GameObject an enemy marker, and if so what kind/tier?</summary>
    static bool TryClassify(GameObject go, MeshRenderer r, out Kind kind, out int tier)
    {
        kind = Kind.Mob; tier = 3;
        string nm = go.name.ToUpperInvariant();

        if (nm.Contains("MINIBOSS")) { kind = Kind.Mini; tier = ParseTier(nm, 5); return true; }
        if (nm.Contains("BOSS"))     { kind = Kind.Boss; tier = ParseTier(nm, 5); return true; }
        if (nm.Contains("AGGRO"))    { kind = Kind.Mob;  tier = ParseTier(nm, 3); return true; }

        // Unlabelled cluster dot: an enemy only if it carries the aggro material colour.
        // (Resource/NPC dots use different colours; miniboss never spawns dots.)
        if (go.name == "(cluster node)" && r.sharedMaterial != null &&
            (Approx(r.sharedMaterial.color, cAggro) || Approx(r.sharedMaterial.color, cMini)))
        {
            kind = Kind.Mob; tier = 3; return true;
        }
        return false;
    }

    static int ParseTier(string upperName, int fallback)
    {
        var m = Regex.Match(upperName, @"(?:TIER\s*|T)(\d)");   // "T3" or "TIER 5"
        return m.Success ? Mathf.Clamp(int.Parse(m.Groups[1].Value), 1, 5) : fallback;
    }

    static string NameFor(GameObject marker, Kind kind)
    {
        if (marker.name == "(cluster node)") return "Aggro Mob";
        string label = marker.name.StartsWith("MARKER - ") ? marker.name.Substring("MARKER - ".Length) : marker.name;
        string prefix = kind == Kind.Boss ? "BOSS" : kind == Kind.Mini ? "MINIBOSS" : "Enemy";
        return $"{prefix} — {label}";
    }

    /// <summary>Build a real Enemy3D (capsule click-collider + Kenney visual) at a marker's spot.</summary>
    static void SpawnEnemy(string name, Vector3 groundPos, Transform parent, int packIndex, Kind kind, int tier)
    {
        var go = World3DBuilder.Capsule(name, groundPos, new Color(0.55f, 0.2f, 0.18f));
        go.transform.SetParent(parent, true);

        var ct = go.AddComponent<CombatTarget>();
        StatsFor(kind, tier, out int hp, out int atk, out int def, out int dmg);
        ct.maxHP = hp; ct.attackLevel = atk; ct.defenceLevel = def; ct.maxDamage = dmg; ct.isAggressive = true;
        go.AddComponent<Enemy3D>();

        EnemyVisualBuilder.AttachVisual(go.transform, packIndex);

        // Boss/miniboss are visibly bigger. Set the authoritative runtime height AND scale the
        // edit-time preview to match, so the Scene view looks right before you press Play.
        float height = kind == Kind.Boss ? 3.6f : kind == Kind.Mini ? 2.6f : 1.8f;
        var vis = go.transform.Find("Visual");
        if (vis != null)
        {
            var norm = vis.GetComponent<HeightNormalizer3D>();
            if (norm != null) norm.targetHeight = height;
            if (!Mathf.Approximately(height, 1.8f)) vis.localScale *= height / 1.8f;   // normalizer makes it exact at runtime
        }
    }

    static void StatsFor(Kind kind, int t, out int hp, out int atk, out int def, out int dmg)
    {
        switch (kind)
        {
            case Kind.Boss: hp = 150 + t * 30; atk = 14 + t * 2; def = 12 + t * 2; dmg = 8 + t; break;
            case Kind.Mini: hp = 60  + t * 15; atk = 8  + t * 2; def = 6  + t * 2; dmg = 4 + t; break;
            default:        hp = 10  + t * 8;  atk = 2  + t * 2; def = 1  + t * 2; dmg = 1 + t; break;
        }
    }

    static bool Approx(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.08f && Mathf.Abs(a.g - b.g) < 0.08f && Mathf.Abs(a.b - b.b) < 0.08f;
}
