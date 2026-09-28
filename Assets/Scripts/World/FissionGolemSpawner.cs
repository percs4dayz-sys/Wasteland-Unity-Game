using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// Keeps one living Elemental Golem alive per tier, IN THAT TIER'S PAINTED BIOME, so the geiger
/// counter always has a target in a region that makes sense (the Fire golem in the scorched lands,
/// the Ice golem in the frozen wastes, etc.). Kill one and strip its corpse (FissionGolem) and this
/// spawns a fresh one elsewhere in the same biome after a delay — closing the hunt loop.
///
/// Golem models live at Resources/golems/t{N}/source/... as raw FBX; this builds each into a proper
/// killable boss (collider + CombatTarget + Enemy3D + FissionGolem), normalised to a big ~4.5 m body.
/// Placement uses LivingWorld's biome map; until that's ready (or if a tier's biome isn't found) the
/// tier simply waits, no errors.
/// </summary>
public class FissionGolemSpawner : MonoBehaviour
{
    // tier → (resource path of the golem FBX, element name). Matches the user's imported folders.
    static readonly (int tier, string path, string element)[] Golems =
    {
        (1, "golems/t1/source/Earth_Golem_FBX",  "Earth"),
        (2, "golems/t2/source/Light_Golem_FBX",  "Light"),
        (3, "golems/t3/source/Fire_Golem_FBX",   "Fire"),
        (4, "golems/t4/source/Ice_Golem_FBX",    "Ice"),
        (5, "golems/t5/source/Shadow_Golem_FBX", "Shadow"),
    };

    // per-tier golem combat stats (bosses — tanky, hard-hitting; scale with tier)
    static readonly (int hp, int atk, int def, int dmg, float aggro, float speed)[] Stat =
    {
        (150, 12, 10,  8, 14f, 2.6f),
        (260, 20, 16, 12, 15f, 2.8f),
        (420, 32, 24, 18, 16f, 3.0f),
        (650, 46, 34, 26, 18f, 3.2f),
        (950, 64, 48, 38, 20f, 3.4f),
    };

    const float BodyHeight = 4.5f;   // golems are big
    const float RespawnDelay = 10f;

    float _nextScan;
    readonly float[] _respawnAt = new float[6];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("MainWorld")) return;
        var go = new GameObject("FissionGolemSpawner (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<FissionGolemSpawner>();
    }

    void Update()
    {
        if (Time.time < _nextScan) return;
        _nextScan = Time.time + 2f;
        if (LivingWorld.Instance == null || !LivingWorld.Instance.Ready) return;   // biome map not ready yet

        foreach (var g in Golems)
        {
            bool exists = FissionGolem.Active.Any(x => x != null && x.tier == g.tier);
            if (exists) { _respawnAt[g.tier] = 0f; continue; }

            if (_respawnAt[g.tier] > 0f)
            {
                if (Time.time >= _respawnAt[g.tier]) { TrySpawn(g); _respawnAt[g.tier] = 0f; }
            }
            else _respawnAt[g.tier] = Time.time + RespawnDelay;
        }
    }

    void TrySpawn((int tier, string path, string element) g)
    {
        if (!LivingWorld.Instance.FindTierSpot(g.tier, out Vector3 pos)) return;   // biome not found this pass; retry next scan

        var prefab = Resources.Load<GameObject>(g.path);
        if (prefab == null) { Debug.LogWarning($"[FissionGolemSpawner] Missing {g.path}"); return; }

        var go = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.value * 360f, 0f));
        go.name = $"{g.element} Golem (T{g.tier})";
        LivingWorld.Sanitize(go);   // strip the FBX's imported camera/audio/lights (was hijacking the view)
        NormalizeHeight(go, BodyHeight);
        SeatOnGround(go, pos.y);    // pivot isn't always at the feet — sit its base on the ground

        // Collider for clicks/combat.
        if (go.GetComponentInChildren<Collider>() == null)
        {
            var cc = go.AddComponent<CapsuleCollider>();
            cc.height = 4.5f; cc.radius = 1.2f; cc.center = new Vector3(0f, 2.25f, 0f);
        }

        var s = Stat[g.tier - 1];
        var ct = go.GetComponent<CombatTarget>() ?? go.AddComponent<CombatTarget>();
        ct.maxHP = s.hp; ct.attackLevel = s.atk; ct.defenceLevel = s.def; ct.maxDamage = s.dmg;
        ct.isAggressive = true; ct.tier = g.tier;
        ct.isBoss = g.tier == 5; ct.isMiniBoss = g.tier < 5;

        var e = go.GetComponent<Enemy3D>() ?? go.AddComponent<Enemy3D>();
        e.aggroRange = s.aggro; e.moveSpeed = s.speed; e.attackCooldown = 2.6f; e.respawnSeconds = 9999f;

        var fg = go.GetComponent<FissionGolem>() ?? go.AddComponent<FissionGolem>();
        fg.tier = g.tier;

        Debug.Log($"[FissionGolemSpawner] {g.element} golem (T{g.tier}) spawned in its biome at {pos}.");
    }

    /// <summary>Scale a raw FBX so its rendered height matches <paramref name="target"/> metres.</summary>
    static void NormalizeHeight(GameObject go, float target)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        if (b.size.y > 0.01f) go.transform.localScale *= target / b.size.y;
    }

    /// <summary>Lift the model so its lowest rendered point rests on <paramref name="groundY"/> —
    /// fixes models whose pivot is at the centre (they sink half-underground/underwater).</summary>
    static void SeatOnGround(GameObject go, float groundY)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        go.transform.position += Vector3.up * (groundY - b.min.y);
    }
}
