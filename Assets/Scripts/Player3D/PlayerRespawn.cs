using UnityEngine;

/// <summary>
/// Player death → respawn. Captures the player's spawn position when the scene loads and,
/// a few seconds after death (long enough for the Die animation to read), returns the player
/// there at full HP and stands them back up. Scene-agnostic — it uses wherever the player
/// started, so it "just works" in MainWorld3D or any other 3D scene with no setup. Auto-attached
/// by PlayerEntity, so existing scenes get it without an editor change.
/// </summary>
[RequireComponent(typeof(PlayerEntity))]
public class PlayerRespawn : MonoBehaviour
{
    [Tooltip("Seconds the death animation plays before you respawn.")]
    public float respawnDelay = 3f;

    [Tooltip("Seconds the player must be in the void (no ground anywhere below) before being " +
             "returned to the scene spawn point.")]
    public float outOfBoundsDelay = 5f;

    [Tooltip("How far below the player we look for ground. If solid ground is within this distance, " +
             "the player is in-bounds (normal roaming). Only a real fall into the void leaves " +
             "nothing this far down. Raise it if your terrain has very deep but legitimate drops.")]
    public float voidFallDistance = 50f;

    PlayerEntity _pe;
    Player3DController _pc;
    [Tooltip("Also count leaving the terrain's X/Z footprint as out of bounds. Without this, only " +
             "FALLING is detected — if you walk off the map onto any stray collider you keep walking " +
             "around in empty space forever, because there's technically ground under you.")]
    public bool useTerrainBounds = true;

    [Tooltip("Metres past the terrain edge you may stray before counting as outside. Keep a little " +
             "slack so standing on the very edge doesn't yank you.")]
    public float boundsMargin = 5f;

    [Tooltip("Metres above the terrain's highest point before you count as out of bounds. Must clear " +
             "your tallest legitimate structure — towers, the crane, rooftops you can stand on.")]
    public float ceilingMargin = 150f;

    [Tooltip("Metres below the terrain's lowest point before you count as out of bounds. Covers " +
             "falling through the world into empty space underneath it.")]
    public float floorMargin = 100f;

    /// <summary>Seconds after scene start during which an out-of-bounds player is rescued almost
    /// immediately rather than after <see cref="outOfBoundsDelay"/> — covers the save being applied
    /// from SaveManager.Update() a frame or two after we start.</summary>
    const float PostLoadWindow = 4f;

    Vector3 _spawnPos;
    Quaternion _spawnRot;
    bool _respawning;
    float _respawnAt;
    float _oobTimer;
    float _sceneStart;

    bool _boundsLogged;   // log the world bounds once, the first time we test them

    /// <summary>Consecutive rescues that landed somewhere still out of bounds. Past this we give up
    /// and report, rather than teleporting in a circle forever.</summary>
    const int MaxRescueLoops = 2;
    int _rescueLoops;

    void Start()
    {
        _pe = GetComponent<PlayerEntity>();
        _pc = GetComponent<Player3DController>();
        // The authored spawn — NOT wherever the player happens to be standing right now. Start()
        // runs after the save has already applied its position, so reading transform.position here
        // would adopt a bad saved position (e.g. Y=2865 in the sky) as the "spawn" and make every
        // recovery teleport straight back into the void.
        _spawnPos = WorldSpawnPoint.Position;
        _spawnRot = WorldSpawnPoint.Rotation(transform.rotation);
        if (_pe != null && _pe.Stats != null) _pe.Stats.OnPlayerDied += HandleDied;

        // New character (no save yet): start at the authored spawn rather than wherever the Player
        // object happens to sit in the scene. A loaded save keeps its own position — untouched.
        if (!SaveManager.SaveExists())
        {
            TeleportToSpawn();
            return;
        }

        _sceneStart = Time.time;
    }

    void OnDestroy()
    {
        if (_pe != null && _pe.Stats != null) _pe.Stats.OnPlayerDied -= HandleDied;
    }

    void HandleDied()
    {
        if (_respawning) return;
        _respawning = true;
        _respawnAt = Time.time + respawnDelay;

        GetComponent<ActionCombat3D>()?.Disengage();         // drop any combat lock
        var pc = GetComponent<Player3DController>();
        if (pc != null) pc.enabled = false;                  // freeze the corpse while it plays out
        HUDController.Emit("<color=#FF6060>You have died.</color> Respawning...");
    }

    void Update()
    {
        // Death respawn: after the death animation has played out, return the player to spawn.
        if (_respawning)
        {
            if (Time.time < _respawnAt) return;
            _respawning = false;
            TeleportToSpawn();
            _pe.Stats.Revive();                              // full HP, clears the dead flag
            GetComponent<PlayerAnimator3D>()?.Revive();      // stand back up
            if (_pc != null) _pc.enabled = true;
            _oobTimer = 0f;
            HUDController.Emit("<color=#80FF80>You wake at your spawn point.</color>");
            return;
        }

        // Out-of-bounds watchdog: only a genuine fall into the void (no ground anywhere below the
        // player) counts. Normal roaming always has terrain underfoot, so walking far never trips
        // this — keying off the frame-by-frame grounded flag was too aggressive.
        bool outside = OutsideWorldBounds();
        if (!HasGroundBelow() || outside)
        {
            _oobTimer += Time.deltaTime;

            // SaveManager applies the saved position from its Update(), i.e. AFTER our Start() ran,
            // so a bad saved position (sky-drop) only becomes visible a frame or two in. For the
            // first few seconds react almost immediately — waiting the full delay there means
            // plummeting thousands of units before the rescue fires.
            bool justLoaded = Time.time - _sceneStart < PostLoadWindow;
            float delay = justLoaded ? 0.25f : outOfBoundsDelay;

            if (_oobTimer >= delay)
            {
                _oobTimer = 0f;

                // Log the actual reason before moving — after the teleport the evidence is gone.
                if (outside)
                    Debug.LogWarning($"[PlayerRespawn] Out of bounds at {transform.position}: " +
                                     $"{WorldBounds.Explain(transform.position, boundsMargin, ceilingMargin, floorMargin)}");
                else
                    Debug.LogWarning($"[PlayerRespawn] No ground within {voidFallDistance}m below " +
                                     $"{transform.position} — treating as a fall into the void.");

                TeleportToSpawn();
                HUDController.Emit(justLoaded
                    ? "<color=#FFD080>Your saved position was outside the world — returned to spawn.</color>"
                    : outside
                        ? "<color=#FFD080>You walked off the edge of the world — returned to spawn.</color>"
                        : "<color=#FFD080>You drifted out of bounds — returned to spawn.</color>");
            }
        }
        else _oobTimer = 0f;
    }

    /// <summary>Public unstuck entry point (e.g. the /stuck chat command): return the player to the
    /// scene spawn point on demand and reset the out-of-bounds timer.</summary>
    public void ReturnToSpawn()
    {
        TeleportToSpawn();
        _oobTimer = 0f;
    }

    /// <summary>True if there's a walkable surface within voidFallDistance below the player. Normal
    /// roaming always has ground underfoot, so only a real fall into the void (e.g. a bad save
    /// position) returns false — that's what the out-of-bounds watchdog keys off.</summary>
    bool HasGroundBelow()
    {
        Vector3 origin = transform.position + Vector3.up * 2f;
        var hits = Physics.RaycastAll(new Ray(origin, Vector3.down), voidFallDistance + 2f,
                                      ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;  // ignore our own colliders
            if (h.normal.y < 0.2f) continue;                          // walls/undersides aren't ground
            return true;
        }
        return false;
    }

    /// <summary>True when the player has left the terrain's X/Z footprint. This is the case
    /// HasGroundBelow can't catch: walk off the map onto any stray collider — a vendor prop, a
    /// leftover ground plane, a water box — and you're technically "grounded" while standing in
    /// empty space, so the fall-based check never trips.</summary>
    bool OutsideWorldBounds()
    {
        if (!useTerrainBounds) return false;

        if (!_boundsLogged)
        {
            _boundsLogged = true;
            Debug.Log($"[PlayerRespawn] World bounds — {WorldBounds.Describe(boundsMargin, ceilingMargin, floorMargin)}");
        }

        return WorldBounds.IsOutside(transform.position, boundsMargin, ceilingMargin, floorMargin);
    }

    /// <summary>Teleport the player back to the scene spawn point, snapped onto the ground surface so
    /// they never land half-buried. Toggle the CharacterController off around the move so it doesn't
    /// fight (or revert) the position change.</summary>
    void TeleportToSpawn()
    {
        Vector3 from = transform.position;

        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        Vector3 landed = GroundedSpawn(cc);
        transform.position = landed;
        transform.rotation = _spawnRot;
        if (cc != null) cc.enabled = true;

        // Prove what actually happened. "It says it moved me but nothing changed" has two very
        // different causes and they need opposite fixes: either the spawn point resolves to where
        // you already are (the marker was set while you were stuck), or the move is being undone by
        // something else a frame later. This distinguishes them.
        float moved = Vector3.Distance(from, transform.position);
        Debug.Log($"[PlayerRespawn] Teleport: {from} → {transform.position}  (moved {moved:F2}m)\n" +
                  $"  authored spawn {_spawnPos} | marker in scene: {WorldSpawnPoint.HasMarker}" +
                  (WorldSpawnPoint.HasMarker ? $" at {WorldSpawnPoint.Position}" : " (using hardcoded fallback)"));
        // Starting a new game already at a valid spawn is normal, not a failed rescue.
        if (moved < 0.5f && !HasGroundBelow())
            Debug.LogError("[PlayerRespawn] The spawn point is essentially WHERE YOU ALREADY ARE — " +
                           "the marker was almost certainly placed while the player was stuck, so every " +
                           "rescue returns you to the same bad spot. Move the marker itself to solid ground.");

        // If the spawn point ITSELF fails the bounds test, rescuing to it just re-triggers the
        // watchdog forever — you get a wall of "returned to spawn" messages and no information.
        // Say so once, plainly, and stop rescuing rather than spamming.
        string why = WorldBounds.Explain(landed, boundsMargin, ceilingMargin, floorMargin);
        if (!string.IsNullOrEmpty(why))
        {
            _rescueLoops++;
            if (_rescueLoops >= MaxRescueLoops && useTerrainBounds)
            {
                useTerrainBounds = false;   // stop the loop; the watchdog can't help here
                Debug.LogError(
                    $"[PlayerRespawn] THE SPAWN POINT IS ITSELF OUT OF BOUNDS — {why}.\n" +
                    $"  Spawn resolved to {landed} (authored {_spawnPos}, " +
                    $"marker in scene: {WorldSpawnPoint.HasMarker}).\n" +
                    $"  World bounds: {WorldBounds.Describe(boundsMargin, ceilingMargin, floorMargin)}\n" +
                    "  Bounds checking is now OFF for this session so it stops looping. Fix it with " +
                    "Wasteland > Fix > Set World Spawn Point Here on solid ground, then save the scene.");
            }
        }
        else _rescueLoops = 0;
    }

    /// <summary>The spawn point with its Y snapped so the capsule's feet rest on top of the highest
    /// walkable surface at the spawn's X/Z. Casting from far above and taking the first walkable hit
    /// avoids dropping the player into the ground (which ClampToGround then mistakes for a rooftop and
    /// refuses to lift them out of — causing a respawn loop).</summary>
    Vector3 GroundedSpawn(CharacterController cc)
    {
        Vector3 p = _spawnPos;
        var hits = Physics.RaycastAll(new Ray(new Vector3(p.x, p.y + 5000f, p.z), Vector3.down), 20000f,
                                      ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;   // ignore our own colliders
            if (h.normal.y < 0.2f) continue;                           // walls/undersides aren't ground

            // Ignore anything floating outside the world. We cast from far overhead to find the
            // highest real surface, which means a stray collider parked in the sky is the FIRST
            // thing we hit — and we'd happily "snap to ground" thousands of metres up. That is
            // exactly how a spawn point authored at y=157 resolved to y=2865.
            if (WorldBounds.IsOutside(h.point, boundsMargin, ceilingMargin, floorMargin))
            {
                Debug.LogWarning($"[PlayerRespawn] Ignoring sky/void collider '{h.collider.name}' at " +
                                 $"{h.point} while grounding the spawn — it is outside the world.");
                continue;
            }

            float bottomOffset = cc != null ? cc.center.y - cc.height * 0.5f : 0f;
            return new Vector3(p.x, h.point.y - bottomOffset + 0.02f, p.z);
        }

        // No collider hit. That does NOT mean there's no ground — a TerrainCollider can be missing
        // or disabled, and a raycast is blind to terrain without one. Sample the terrain heightmap
        // directly, which needs no collider at all. Without this we returned the raw spawn point and
        // left the player hovering at whatever Y it happened to carry.
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            var all = FindObjectsByType<Terrain>();
            if (all.Length > 0) terrain = all[0];
        }
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 tp = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            bool over = p.x >= tp.x && p.x <= tp.x + size.x && p.z >= tp.z && p.z <= tp.z + size.z;
            if (over)
            {
                float y = terrain.SampleHeight(p) + tp.y;
                float bottomOffset = cc != null ? cc.center.y - cc.height * 0.5f : 0f;
                Debug.LogWarning($"[PlayerRespawn] No collider at spawn ({p.x:F1}, {p.z:F1}) — used the " +
                                 $"terrain heightmap instead (y={y:F2}). Check that the Terrain has an " +
                                 "enabled TerrainCollider.");
                return new Vector3(p.x, y - bottomOffset + 0.02f, p.z);
            }
            Debug.LogError($"[PlayerRespawn] Spawn ({p.x:F1}, {p.z:F1}) is OUTSIDE the terrain " +
                           $"(X {tp.x:F0}→{tp.x + size.x:F0}, Z {tp.z:F0}→{tp.z + size.z:F0}). " +
                           "Run Wasteland ▸ Fix ▸ Set World Spawn Point Here on a spot that's on the map.");
        }

        return p;   // genuinely nothing to stand on — leave the spawn as authored
    }
}
