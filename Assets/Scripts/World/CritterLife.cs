using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ambient critters: keeps a small population of harmless wildlife alive around the player so the
/// wasteland feels inhabited. Self-bootstrapping like the other managers — nothing to place in a scene.
///
/// v1 species: feral wolves (Resources/wolf.fbx + Resources/WolfAnimator, which is already a
/// Speed-driven locomotion controller — the same pattern the player uses). The species table is
/// data, so spiders/etc. can be added once their rigs are verified.
///
/// Design constraints, deliberate:
///   • Critters are SCENERY. No Enemy3D, no CombatTarget, no health — they cannot be attacked and
///     cannot fight. Hunting them is a later feature.
///   • Cheap by construction: population is capped, each brain makes decisions on a staggered
///     low-frequency tick (not per-frame), animators cull when off-screen, and far critters despawn.
///   • Spawns happen OUT OF VIEW when possible (behind the camera, or beyond a distance floor), so
///     animals never pop into existence in front of the player.
///
/// Behaviour: Graze (stand around) → Wander (amble to a nearby point) → Flee (player got close;
/// sprint away until comfortable, then settle). That tiny loop plus distance-based turnover reads as
/// "wildlife" from a distance, which is all ambience needs.
/// </summary>
public class CritterLife : MonoBehaviour
{
    // ── population tuning ─────────────────────────────────────────────────
    const int   MaxCritters   = 6;     // live at once, world-feel vs cost
    const float SpawnMin      = 28f;   // spawn ring around the player (m)
    const float SpawnMax      = 46f;
    const float DespawnAt     = 60f;   // beyond this, quietly recycled
    const float SpawnEvery    = 4f;    // seconds between top-up attempts

    // ── species table ─────────────────────────────────────────────────────
    /// <summary>
    /// How a species' Animator is driven. SpeedParam feeds a "Speed" float into a locomotion blend
    /// tree (the wolf/player pattern). CrossFade plays named states directly — for vendor packs whose
    /// controller is just a bag of disconnected demo states with no parameters (the dog pack ships
    /// exactly that), where a float would drive nothing.
    /// </summary>
    public enum Drive { SpeedParam, CrossFade }

    public struct Species
    {
        public string modelResource;      // under a Resources folder
        public string controllerResource; // optional — null keeps whatever the prefab carries
        public Drive  drive;
        public string idleState, moveState, fleeState;   // CrossFade mode only
        public bool   moveInPlace;        // true: "wander" plays moveState without translating
                                          // (for packs with no straight-walk clip, e.g. the free
                                          // dog pack only has turns — walking would moonwalk)
        public bool   buildGenericAvatar; // true: model imported via glTF (no avatar) — build a generic
                                          // avatar from its hierarchy at spawn so the clips can play.
        public bool   flier;              // true: on flee it takes off — plays fleeState (a flight clip),
                                          // climbs into the air and leaves the ground until it despawns.
        public float  walkSpeed, fleeSpeed, scaleMin, scaleMax;
    }
    static readonly Species[] Table =
    {
        // ── Wolf: BENCHED. Its WolfAnimator blend tree ships with all clips NULL (empty), so it
        // spawns with zero animation and just slides. The only wolf clips we own live in
        // Assets/Art/newshit/BeastDog/source/WOLF_DEMO.fbx (walk + howl, no idle/run). To restore it,
        // wire those clips into WolfAnimator's blend tree (thr 0/1.5/4) and uncomment this entry.
        // new Species { modelResource = "wolf", controllerResource = "WolfAnimator",
        //               drive = Drive.SpeedParam,
        //               walkSpeed = 1.6f, fleeSpeed = 5.5f, scaleMin = 0.8f, scaleMax = 1.15f },

        // Cottontail. Real idle + slow-walk clips (Speed blend). Model is ~0.4 m natural, kept
        // near-scale. No run clip, so flee speed is modest to keep the walk cycle from moonwalking.
        new Species { modelResource = "Wildlife/Rabbit/RABBIT_DEMO", controllerResource = "Wildlife/RabbitAnimator",
                      drive = Drive.SpeedParam,
                      walkSpeed = 1.1f, fleeSpeed = 2.2f, scaleMin = 1.0f, scaleMax = 1.4f },

        // Rhinoceros. Only one (long, ~12 s) baked clip, so it grazes in place and lumbers off only
        // when fleeing. Model is ~6x oversized, scaled down to roughly real bulk. If its one clip
        // turns out to be a walk (not an idle), flip moveInPlace to false so it translates while it plays.
        new Species { modelResource = "Wildlife/Rhino/Rhino", controllerResource = "Wildlife/RhinoAnimator",
                      drive = Drive.CrossFade,
                      idleState = "Move", moveState = "Move", fleeState = "Move",
                      moveInPlace = true,
                      walkSpeed = 1.0f, fleeSpeed = 2.5f, scaleMin = 0.15f, scaleMax = 0.20f },

        // Feral stray. Free pack has no straight-walk clip, so "wander" is the playful-idle in place;
        // it still bolts properly on Run Loop. Materials are HDRP-authored — run the existing
        // "Wasteland/Fix All Materials (URP)" once after import or the dog renders pink.
        new Species { modelResource = "RSG_DogsPack/HDRP/Prefabs/P_GermanShepherd",
                      controllerResource = null,
                      drive = Drive.CrossFade,
                      idleState = "1 type_Idle Breathing",
                      moveState = "1 type_Idle_Playing",
                      fleeState = "1 type_Run Loop",
                      moveInPlace = true,
                      walkSpeed = 1.4f, fleeSpeed = 6f, scaleMin = 0.9f, scaleMax = 1.05f },

        // Wild boar. Breathing/idle + a fast-walk (Speed blend). Natural ~2 m size. Walk clip reads
        // at a brisk pace, so wander speed is kept up to match it.
        new Species { modelResource = "Wildlife/Boar/BOAR_DEMO", controllerResource = "Wildlife/BoarAnimator",
                      drive = Drive.SpeedParam,
                      walkSpeed = 2.2f, fleeSpeed = 3.2f, scaleMin = 1.0f, scaleMax = 1.3f },

        // Bald eagle. 111-clip pack; struts on the ground (Idle_Ground / Walk) then TAKES OFF when
        // startled — flier=true climbs it into the air on the Fly clip and it leaves. Model is ~100x
        // oversized, scaled way down to real wingspan. Fast flee so the takeoff has punch.
        new Species { modelResource = "Wildlife/BaldEagle/BaldEaglex", controllerResource = "Wildlife/BaldEagleAnimator",
                      drive = Drive.CrossFade,
                      idleState = "Idle", moveState = "Walk", fleeState = "Fly",
                      moveInPlace = false, flier = true,
                      walkSpeed = 0.8f, fleeSpeed = 7.0f, scaleMin = 0.014f, scaleMax = 0.018f },

        // Raccoon (glTF). One baked clip → grazes in place, bolts on flee. Needs a runtime generic
        // avatar (glTF import carries none). Model is oversized, scaled to ~0.6 m.
        new Species { modelResource = "Wildlife/Raccoon/Raccoon", controllerResource = "Wildlife/RaccoonAnimator",
                      drive = Drive.CrossFade, idleState = "Move", moveState = "Move", fleeState = "Move",
                      moveInPlace = true, buildGenericAvatar = true,
                      walkSpeed = 1.0f, fleeSpeed = 3.0f, scaleMin = 0.14f, scaleMax = 0.18f },

        // Green tree frog (glTF). One baked clip, sits and animates in place; only hops off on flee.
        // Model is ~12 cm natural — bumped up a touch so it reads at distance.
        new Species { modelResource = "Wildlife/Frog/Frog", controllerResource = "Wildlife/FrogAnimator",
                      drive = Drive.CrossFade, idleState = "Move", moveState = "Move", fleeState = "Move",
                      moveInPlace = true, buildGenericAvatar = true,
                      walkSpeed = 0.6f, fleeSpeed = 2.0f, scaleMin = 2.0f, scaleMax = 3.0f },
    };

    Transform _player;
    readonly List<Critter> _critters = new();
    float _nextSpawnAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("CritterLife (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<CritterLife>();
    }

    void Update()
    {
        // The player can arrive late (scene flow goes through CharacterSelect) — keep looking.
        if (_player == null)
        {
            var pe = FindAnyObjectByType<PlayerEntity>();
            if (pe == null) return;
            _player = pe.transform;
        }

        // Recycle the far and the dead-scene ones.
        for (int i = _critters.Count - 1; i >= 0; i--)
        {
            var c = _critters[i];
            if (c == null || c.gameObject == null) { _critters.RemoveAt(i); continue; }
            if ((c.transform.position - _player.position).sqrMagnitude > DespawnAt * DespawnAt)
            {
                Destroy(c.gameObject);
                _critters.RemoveAt(i);
            }
        }

        // Top up, at most one per interval so a fresh area fills gradually rather than all at once.
        if (_critters.Count < MaxCritters && Time.time >= _nextSpawnAt)
        {
            _nextSpawnAt = Time.time + SpawnEvery;
            TrySpawnOne();
        }
    }

    void TrySpawnOne()
    {
        var species = Table[Random.Range(0, Table.Length)];

        // A few placement attempts per call; give up quietly if the area is all cliffs/void.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 dir = new Vector3(dir2.x, 0f, dir2.y);

            // Prefer behind the camera so nothing pops in on screen. If every attempt is in view,
            // the last ones are allowed anyway — better a distant pop than no wildlife.
            var cam = Camera.main;
            if (attempt < 4 && cam != null && Vector3.Dot(cam.transform.forward, dir) > 0.15f) continue;

            float dist = Random.Range(SpawnMin, SpawnMax);
            Vector3 probe = _player.position + dir * dist + Vector3.up * 40f;
            if (!Physics.Raycast(probe, Vector3.down, out var hit, 120f,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                continue;
            if (hit.normal.y < 0.7f) continue;        // too steep — goats are a later species
            if (hit.point.y <= 50.7f) continue;       // on/under the water — wildlife stays on dry land

            Spawn(species, hit.point);
            return;
        }
    }

    void Spawn(Species s, Vector3 pos)
    {
        var model = Resources.Load<GameObject>(s.modelResource);
        if (model == null)
        {
            Debug.LogWarning($"[CritterLife] Resources/{s.modelResource} not found — species disabled.");
            enabled = false;
            return;
        }

        var go = Instantiate(model, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        go.name = $"Critter_{s.modelResource}";
        go.transform.localScale = Vector3.one * Random.Range(s.scaleMin, s.scaleMax);

        var anim = go.GetComponentInChildren<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        // glTF models arrive with no avatar; a Generic Animator won't deform the mesh without one.
        // Build one from the instantiated hierarchy so its baked clips can play.
        if (s.buildGenericAvatar && anim.avatar == null)
        {
            var built = AvatarBuilder.BuildGenericAvatar(anim.gameObject, "");
            if (built != null && built.isValid) anim.avatar = built;
        }
        if (!string.IsNullOrEmpty(s.controllerResource))
        {
            var rc = Resources.Load<RuntimeAnimatorController>(s.controllerResource);
            if (rc != null) anim.runtimeAnimatorController = rc;
        }
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;   // free when off-screen

        var brain = go.AddComponent<Critter>();
        brain.Init(_player, s);
        _critters.Add(brain);
    }
}

/// <summary>
/// One critter's brain. Decisions run on a low-frequency staggered tick; only actual movement runs
/// per-frame, and only while moving. Animation is a single Speed float into the species' locomotion
/// controller — identical to how the player is driven, so any Speed-param controller works.
/// </summary>
public class Critter : MonoBehaviour
{
    const float PanicRadius = 9f;    // player closer than this → flee
    const float CalmRadius  = 20f;   // fled farther than this → settle
    const float DecideEvery = 0.4f;  // seconds between think-ticks (staggered per critter)

    static readonly int P_Speed = Animator.StringToHash("Speed");

    Transform _player;
    Animator _anim;
    CritterLife.Species _species;

    enum State { Graze, Wander, Flee }
    State _state = State.Graze;
    State _animState = (State)(-1);  // last state pushed to a CrossFade animator
    Vector3 _target;
    float _nextThink;
    float _stateUntil;
    float _speed;                    // current, eased toward desired

    public void Init(Transform player, CritterLife.Species species)
    {
        _player = player;
        _species = species;
        _anim = GetComponentInChildren<Animator>();
        _nextThink = Time.time + Random.value * DecideEvery;   // stagger the herd's brains
        _stateUntil = Time.time + Random.Range(1f, 4f);
    }

    void Update()
    {
        if (_player == null) return;

        if (Time.time >= _nextThink)
        {
            _nextThink = Time.time + DecideEvery;
            Think();
        }

        // Move only when there's somewhere to be. A moveInPlace species doesn't translate while
        // wandering (its pack has no walk clip) — it only covers ground when fleeing.
        bool wanderMoves = !_species.moveInPlace;
        float desired = _state == State.Flee ? _species.fleeSpeed
                      : _state == State.Wander && wanderMoves ? _species.walkSpeed
                      : 0f;
        _speed = Mathf.MoveTowards(_speed, desired, 8f * Time.deltaTime);

        bool flyingOff = _species.flier && _state == State.Flee;
        if (_speed > 0.05f)
        {
            Vector3 to = _target - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.04f)
            {
                var look = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 6f * Time.deltaTime);
                transform.position += transform.forward * (_speed * Time.deltaTime);
                // A flier climbs into the air as it escapes; everything else hugs the ground.
                if (flyingOff) ClimbAway();
                else SnapToGround();
            }
        }

        if (_anim == null) return;
        if (_species.drive == CritterLife.Drive.SpeedParam)
        {
            _anim.SetFloat(P_Speed, _speed);
        }
        else if (_state != _animState)   // CrossFade: push a named state only on change
        {
            _animState = _state;
            string st = _state == State.Flee ? _species.fleeState
                      : _state == State.Wander ? _species.moveState
                      : _species.idleState;
            if (!string.IsNullOrEmpty(st)) _anim.CrossFadeInFixedTime(st, 0.2f);
        }
    }

    void Think()
    {
        float playerDist = Vector3.Distance(transform.position, _player.position);

        // Fear overrides everything.
        if (playerDist < PanicRadius && _state != State.Flee)
        {
            _state = State.Flee;
            PickFleeTarget();
            return;
        }

        switch (_state)
        {
            case State.Flee:
                // A flier never settles back down — once airborne it keeps flying off (and is recycled
                // by distance). A ground animal calms and grazes again once it's put space between them.
                if (_species.flier) PickFleeTarget();
                else if (playerDist > CalmRadius) { _state = State.Graze; _stateUntil = Time.time + Random.Range(2f, 5f); }
                else if ((_target - transform.position).sqrMagnitude < 1f) PickFleeTarget();
                break;

            case State.Graze:
                if (Time.time >= _stateUntil)
                {
                    _state = State.Wander;
                    Vector2 r = Random.insideUnitCircle * 12f;
                    _target = transform.position + new Vector3(r.x, 0f, r.y);
                }
                break;

            case State.Wander:
                if ((_target - transform.position).sqrMagnitude < 1.2f || Time.time >= _stateUntil + 12f)
                {
                    _state = State.Graze;
                    _stateUntil = Time.time + Random.Range(2f, 6f);
                }
                break;
        }
    }

    void PickFleeTarget()
    {
        // Directly away from the player, with jitter so a pack scatters instead of forming a line.
        Vector3 away = (transform.position - _player.position); away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
        away.y = 0f;
        Vector3 jitter = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized;
        _target = transform.position + jitter * 18f;
    }

    void SnapToGround()
    {
        if (Physics.Raycast(transform.position + Vector3.up * 3f, Vector3.down, out var hit, 30f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
    }

    const float FlyAltitude = 18f;   // metres a flier climbs above the ground below it
    const float ClimbSpeed  = 5f;    // metres/sec of ascent while taking off

    // Take off: rise toward a ceiling ~FlyAltitude above whatever ground is currently beneath, and
    // pitch the nose up a touch while climbing so it reads as flight rather than a hovering slide.
    void ClimbAway()
    {
        float groundY = transform.position.y;
        if (Physics.Raycast(transform.position + Vector3.up * 3f, Vector3.down, out var hit, 250f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            groundY = hit.point.y;
        float ceiling = groundY + FlyAltitude;
        if (transform.position.y < ceiling)
        {
            float y = Mathf.MoveTowards(transform.position.y, ceiling, ClimbSpeed * Time.deltaTime);
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
            // ease a gentle upward pitch onto the current (horizontal) heading
            Vector3 fwd = transform.forward; fwd.y = 0.35f;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                                 Quaternion.LookRotation(fwd.normalized, Vector3.up), 3f * Time.deltaTime);
        }
    }
}
