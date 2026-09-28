using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Labelled MARKER layout for the main world as CONCENTRIC TIER RINGS: spawn dead-centre (Tier 1,
/// safe — bank + every crafting station + home), progressively harder & richer outward (Tier 2→4),
/// bosses in the four corners (3 minibosses + 1 major boss) with Tier-5 nodes.
///
/// Resources spawn in RANDOM GROUPS of 3-5, several groups per ring. NPC packs sit in every ring.
/// From Ring 3 out, aggressive mob packs guard territory. Scales off one knob — EDGE (half-extent);
/// keep it in sync with WorldBlockoutTool.HALF.
///
/// Non-destructive — only manages its own "WorldContent" group (re-runnable; markers have NO
/// colliders). Drop real art on each, then delete the marker (or the whole group).
///
/// Menu:  Wasteland ▸ Plan Main World Content   /   Clear Main World Content Markers
/// </summary>
public static class WorldContentPlanner
{
    // Map half-extent. MUST match WorldBlockoutTool.HALF. Raise both to grow the whole world.
    const float EDGE = 300f;

    static readonly Color cHome    = new Color(0.35f, 0.85f, 0.40f);
    static readonly Color cExcav   = new Color(0.55f, 0.62f, 0.72f);  // metal/scrap
    static readonly Color cSalv    = new Color(0.55f, 0.40f, 0.24f);  // wood
    static readonly Color cScav    = new Color(0.30f, 0.70f, 0.82f);  // fishing
    static readonly Color cStation = new Color(1.00f, 0.60f, 0.20f);
    static readonly Color cBank    = new Color(0.35f, 0.70f, 1.00f);
    static readonly Color cNpc     = new Color(1.00f, 0.85f, 0.30f);
    static readonly Color cAggro   = new Color(0.95f, 0.35f, 0.20f);  // territorial mob pack
    static readonly Color cMini    = new Color(1.00f, 0.32f, 0.26f);
    static readonly Color cBoss    = new Color(0.92f, 0.22f, 0.72f);

    [MenuItem("Wasteland/Archived/Plan Main World Content", false, 9000)]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var oldRoot = GameObject.Find("WorldContent");
        if (oldRoot != null) Object.DestroyImmediate(oldRoot);
        var root = new GameObject("WorldContent").transform;

        float rT2 = EDGE * 0.33f, rT3 = EDGE * 0.63f, rT4 = EDGE * 0.87f, rT5 = EDGE * 0.80f, corner = EDGE * 0.95f;

        // ── TIER 1 — centre hub: spawn/home, bank, every station, starter node groups ──
        var t1 = Group("Tier1_Hub", root);
        Marker(t1, "SPAWN / HOME (Tier 1)", Vector3.zero, cHome, 1.8f);
        Marker(t1, "BANK", P(9, 45), cBank);
        Marker(t1, "FURNACE - Hearthcraft (smelt scrap->bars)", P(9, 135), cStation);
        Marker(t1, "GUNSMITHING BENCH - Tinkering (gear/guns/ammo)", P(9, 225), cStation);
        Marker(t1, "COOKING FIRE - Sustenance (cook food)", P(9, 315), cStation);
        Marker(t1, "NPC: Shopkeeper", P(15, 90), cNpc);
        Marker(t1, "NPC: Quartermaster", P(15, 270), cNpc);
        Cluster(t1, "EXCAVATION T1 - Junk Piles (Iron Bar)",        P(26, 30),  cExcav);
        Cluster(t1, "SALVAGING T1 - Dead Scrub/Brush (Rough Lumber)",P(26, 150), cSalv);
        Cluster(t1, "SCAVENGING T1 - Stagnant Puddle [WATER]",      P(26, 270), cScav);

        // ── TIER 2 ring — node GROUPS (x2 each skill), an NPC pack ──
        Ring(root, "Tier2_Ring", rT2, 2, false,
             "Abandoned Car (Steel Bar)", "Irradiated Thicket (Treated Lumber)", "Toxic Runoff [WATER]");

        // ── TIER 3 ring — node groups + NPC pack + TERRITORIAL aggro packs ──
        Ring(root, "Tier3_Ring", rT3, 3, true,
             "Tech Dumpster (Wiring/Alloy Bar)", "Fossilized Deadwood (Hardened Lumber)", "Irradiated River [WATER]");

        // ── TIER 4 ring — node groups + 2nd bank + furnace + NPC pack + aggro packs ──
        var t4 = Ring(root, "Tier4_Ring", rT4, 4, true,
             "Downed Drone (Titanium Bar)", "Petrified Forest (Petrified Lumber)", "Contaminated Lake [WATER]");
        Marker(t4, "BANK (Tier 4)", P(rT4, 20), cBank);
        Marker(t4, "FURNACE / skilling station (Tier 4)", P(rT4, 200), cStation);

        // ── TIER 5 — the four corners: bosses, T5 node groups, guard packs ──
        var t5 = Group("Tier5_Corners", root);
        Marker(t5, "MAJOR BOSS - ENDGAME (Tier 5)", new Vector3( corner, 0,  corner), cBoss, 3.0f);
        Marker(t5, "MINIBOSS (Tier 5)",             new Vector3(-corner, 0,  corner), cMini, 2.2f);
        Marker(t5, "MINIBOSS (Tier 5)",             new Vector3(-corner, 0, -corner), cMini, 2.2f);
        Marker(t5, "MINIBOSS (Tier 5)",             new Vector3( corner, 0, -corner), cMini, 2.2f);
        Cluster(t5, "EXCAVATION T5 - Abandoned Robotics (Cyber-Steel)", new Vector3( rT5, 0,  rT5), cExcav);
        Cluster(t5, "SALVAGING T5 - Ancient Flora (Prime Lumber)",      new Vector3(-rT5, 0,  rT5), cSalv);
        Cluster(t5, "SCAVENGING T5 - Flooded Ruins [WATER]",            new Vector3(-rT5, 0, -rT5), cScav);
        Pack(t5, "AGGRO PACK T5 - territorial (guards Major Boss)", new Vector3(corner * 0.78f, 0, corner * 0.78f), cAggro, 3, 5);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root.gameObject;
        EditorGUIUtility.PingObject(root.gameObject);

        EditorUtility.DisplayDialog("Main World Content Planned",
            $"Added a populated concentric layout ('WorldContent') across a {EDGE * 2:0}x{EDGE * 2:0} m map:\n\n" +
            "• Resources spawn in RANDOM GROUPS of 3-5, two groups per skill per ring.\n" +
            "• NPC packs in every ring; TERRITORIAL aggro mob packs from Ring 3 out + guarding the bosses.\n" +
            "• Tier 1 centre (home/bank/stations) → Tier 5 corners (Major Boss NE + 3 minibosses).\n\n" +
            "Markers have no colliders. Re-run to re-randomise the groupings. Drop art, delete markers, " +
            "put the player spawn on the centre HOME marker. SAVE (Ctrl+S).",
            "Got it");
    }

    // Builds one ring's three skill node-groups (x2 each), an NPC pack, and (if aggressive) two
    // territorial mob packs. Returns the group so the caller can add ring-specific extras.
    static Transform Ring(Transform parent, string name, float r, int tier, bool aggressive,
                          string excavName, string salvName, string scavName)
    {
        var g = Group(name, parent);
        // two groups of each skill, spread around the ring
        Cluster(g, $"EXCAVATION T{tier} - {excavName}", P(r, 40),  cExcav);
        Cluster(g, $"EXCAVATION T{tier} - {excavName}", P(r, 220), cExcav);
        Cluster(g, $"SALVAGING T{tier} - {salvName}",   P(r, 160), cSalv);
        Cluster(g, $"SALVAGING T{tier} - {salvName}",   P(r, 340), cSalv);
        Cluster(g, $"SCAVENGING T{tier} - {scavName}",  P(r, 100), cScav);
        Cluster(g, $"SCAVENGING T{tier} - {scavName}",  P(r, 280), cScav);

        Pack(g, $"NPC PACK (Tier {tier})", P(r, 0), cNpc, 2, 4);

        if (aggressive)
        {
            Pack(g, $"AGGRO PACK T{tier} - territorial", P(r, 70),  cAggro, 3, 5);
            Pack(g, $"AGGRO PACK T{tier} - territorial", P(r, 250), cAggro, 3, 5);
        }
        return g;
    }

    [MenuItem("Wasteland/Archived/Clear Main World Content Markers", false, 9000)]
    public static void Clear()
    {
        var old = GameObject.Find("WorldContent");
        if (old == null) return;
        Object.DestroyImmediate(old);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // ── helpers ──
    static Vector3 P(float radius, float angleDeg)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent);
        return t;
    }

    // A random group of min..max items around a centre: one labelled representative + plain dots.
    static void Cluster(Transform parent, string label, Vector3 center, Color color, int min = 3, int max = 5)
    {
        int count = Random.Range(min, max + 1);
        Marker(parent, $"{label}  (group x{count})", center, color);
        for (int i = 1; i < count; i++)
            Dot(parent, center + new Vector3(Random.Range(-3.5f, 3.5f), 0f, Random.Range(-3.5f, 3.5f)), color);
    }

    // NPC / mob packs use the same grouping (slightly smaller).
    static void Pack(Transform parent, string label, Vector3 center, Color color, int min, int max)
        => Cluster(parent, label, center, color, min, max);

    static void Marker(Transform parent, string label, Vector3 pos, Color color, float scale = 1f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "MARKER - " + label;
        go.transform.SetParent(parent);
        go.transform.position = pos + Vector3.up * (0.6f * scale);
        go.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f) * scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = World3DBuilder.MakeMat("CMarker_" + ColorTag(color), color);

        var txt = new GameObject("Label", typeof(TextMesh), typeof(MeshRenderer));
        txt.transform.SetParent(go.transform);
        txt.transform.localPosition = new Vector3(0, 1.6f, 0);
        txt.transform.localScale = Vector3.one * (0.5f / scale);
        var tm = txt.GetComponent<TextMesh>();
        tm.text = label; tm.fontSize = 64; tm.characterSize = 0.12f;
        tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center;
        tm.color = color;
    }

    // Plain unlabelled cube — a filler node within a cluster.
    static void Dot(Transform parent, Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "(cluster node)";
        go.transform.SetParent(parent);
        go.transform.position = pos + Vector3.up * 0.5f;
        go.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = World3DBuilder.MakeMat("CMarker_" + ColorTag(color), color);
    }

    static string ColorTag(Color c) =>
        $"{Mathf.RoundToInt(c.r * 9)}{Mathf.RoundToInt(c.g * 9)}{Mathf.RoundToInt(c.b * 9)}";
}
