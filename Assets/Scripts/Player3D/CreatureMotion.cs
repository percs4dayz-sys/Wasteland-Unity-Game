using UnityEngine;

/// <summary>
/// "Fake rig" for creature models that have no skeleton. It moves the whole model in code, driven by
/// what Enemy3D is doing: a bob and sway with every step, leaning into turns and idle breathing, then a
/// telegraphed wind-up and lunge on each swing, a flinch when hit, and a fall-and-sink on death.
/// Nothing bends; the body moves as one piece, like a toy walked across a table. The gait sets the
/// flavour: a hound gallops, a tick skitters, the toad squashes, the tracked sentry rumbles and the
/// scrap devil spins.
///
/// At runtime the model child ("Visual") is re-parented under a "MotionPivot" that sits on the model's
/// footprint with the root's axes. Sway and falls then pivot on the feet, and squash-and-stretch follows
/// world up whatever rotation the model was imported with.
///
/// Enemy3D adds one of these to any enemy whose model has no working Animator, so a static enemy never
/// glides about like a statue. Prefabs can carry one with an explicit gait (SeptemberDeliveryBuilder does).
/// </summary>
[DisallowMultipleComponent]
public class CreatureMotion : MonoBehaviour
{
    public enum Gait { Auto, Biped, Heavy, Floater, Quadruped, Crawler, Hopper, Blob, Tracked, Vortex, Shooter }

    [Tooltip("How it moves. Auto picks Biped, Heavy, Quadruped or Crawler from the model's proportions.")]
    public Gait gait = Gait.Auto;
    [Tooltip("Scales every movement: 0 = statue, 1 = default, 2 = cartoonish. Death always plays in full.")]
    [Range(0f, 2f)] public float amount = 1f;

    enum Strike { Lunge, Slam, Rear, Recoil, Puff, Surge, Whirl }
    enum Fall { Topple, FlipOver, Collapse, Deflate, Sag, Unwind }
    enum Fidget { None, Twitch, Peck }
    enum Swing { None, Windup, Strike, Recover, Cancel }

    // Per-gait tuning. Distances are fractions of the model's height; angles are degrees.
    struct Profile
    {
        public float stride;      // stride length → cadence (small legs skitter, big ones plod)
        public float maxCadence;  // steps per second cap; 0 = glides, no steps
        public float bob;         // lift per step
        public float roll;        // side-to-side sway per stride
        public float twist;       // hip twist per stride
        public float rock;        // nose up/down per step (or forward tilt mid-hop)
        public float lean;        // forward lean at full speed
        public float bank;        // lean into turns, degrees per degree-per-second of turning
        public float hover;       // floats this far off the ground
        public float drift;       // slow cloth-like sway, independent of steps
        public float jitter;      // engine rumble
        public float breathe;     // idle scale pulse
        public float spin;        // constant spin, degrees per second
        public bool hop, squash, scan;
        public Fidget fidget;
        public Strike strike;
        public Fall fall;
    }

    static Profile For(Gait g)
    {
        var p = new Profile
        {
            stride = 0.75f, maxCadence = 3.2f, bob = 0.03f, roll = 4f, twist = 4f, rock = 1.5f, lean = 7f,
            bank = 0.03f, breathe = 0.012f, strike = Strike.Lunge, fall = Fall.Topple,
        };
        switch (g)
        {
            case Gait.Heavy:        // brutes, mechs, golems: slow, lumbering, overhead slam
                p.stride = 0.6f; p.maxCadence = 2f; p.bob = 0.035f; p.roll = 6f; p.twist = 5f; p.rock = 2f;
                p.lean = 5f; p.breathe = 0.01f; p.strike = Strike.Slam; break;
            case Gait.Floater:      // wraiths: glide above the ground, swoop in, fold away on death
                p.maxCadence = 0f; p.bob = 0f; p.roll = 0f; p.twist = 0f; p.rock = 0f; p.lean = 12f; p.bank = 0.05f;
                p.hover = 0.04f; p.drift = 3f; p.breathe = 0.015f; p.strike = Strike.Surge; p.fall = Fall.Collapse; break;
            case Gait.Quadruped:    // hounds, beasts: galloping rock, rear back then pounce
                p.stride = 0.95f; p.maxCadence = 3f; p.bob = 0.04f; p.roll = 2f; p.twist = 2f; p.rock = 5f;
                p.lean = 3f; p.strike = Strike.Rear; break;
            case Gait.Crawler:      // ticks, spiders: fast skitter, idle twitches, dies legs-up
                p.stride = 0.35f; p.maxCadence = 7f; p.bob = 0.025f; p.roll = 3f; p.twist = 5f; p.rock = 1.5f;
                p.lean = 2f; p.breathe = 0.01f; p.fidget = Fidget.Twitch; p.strike = Strike.Rear; p.fall = Fall.FlipOver; break;
            case Gait.Hopper:       // birds: hop along, peck at the ground when idle
                p.stride = 0.55f; p.maxCadence = 3.5f; p.bob = 0.12f; p.roll = 3f; p.twist = 0f; p.rock = 8f;
                p.lean = 6f; p.hop = true; p.fidget = Fidget.Peck; break;
            case Gait.Blob:         // the toad: squash-and-stretch hops, puffs up to strike, deflates on death
                p.stride = 0.5f; p.maxCadence = 2.2f; p.bob = 0.1f; p.roll = 2f; p.twist = 0f; p.rock = 3f;
                p.lean = 2f; p.hop = true; p.squash = true; p.breathe = 0.025f; p.strike = Strike.Puff; p.fall = Fall.Deflate; break;
            case Gait.Tracked:      // treads: no steps, engine rumble, scans when idle, recoils when firing
                p.maxCadence = 0f; p.bob = 0f; p.roll = 0f; p.twist = 0f; p.rock = 0f; p.lean = 0f; p.bank = 0.01f;
                p.jitter = 0.004f; p.breathe = 0f; p.scan = true; p.strike = Strike.Recoil; p.fall = Fall.Sag; break;
            case Gait.Vortex:       // the scrap tornado: spins, wobbles, winds down and collapses on death
                p.maxCadence = 0f; p.bob = 0f; p.roll = 0f; p.twist = 0f; p.rock = 0f; p.lean = 10f; p.bank = 0.02f;
                p.hover = 0.03f; p.breathe = 0.02f; p.spin = 540f; p.strike = Strike.Whirl; p.fall = Fall.Unwind; break;
            case Gait.Shooter:      // crouched gunners: shuffling steps, recoil instead of a lunge
                p.bob = 0.02f; p.roll = 3f; p.twist = 2f; p.lean = 4f; p.strike = Strike.Recoil; break;
        }
        return p;
    }

    /// <summary>Offsets from the rest pose: angles in degrees, distances as fractions of height,
    /// scale as offsets from 1 (vertical, and horizontal on both axes).</summary>
    struct Pose
    {
        public float pitch, yaw, roll, x, y, z, sy, sxz;

        public static Pose operator +(Pose a, Pose b) => new Pose
        {
            pitch = a.pitch + b.pitch, yaw = a.yaw + b.yaw, roll = a.roll + b.roll,
            x = a.x + b.x, y = a.y + b.y, z = a.z + b.z, sy = a.sy + b.sy, sxz = a.sxz + b.sxz,
        };
        public static Pose operator *(Pose a, float k) => new Pose
        {
            pitch = a.pitch * k, yaw = a.yaw * k, roll = a.roll * k,
            x = a.x * k, y = a.y * k, z = a.z * k, sy = a.sy * k, sxz = a.sxz * k,
        };
        public static Pose Lerp(Pose a, Pose b, float t) => a + (b + a * -1f) * t;
    }

    Transform _pivot;
    Vector3 _rest;
    Enemy3D _enemy;
    CombatTarget _ct;
    Renderer _probe;
    Profile _p;
    float _h = 1.8f, _w = 1f, _l = 1f, _refSpeed = 3.2f, _windup = 0.4f;

    Vector3 _lastPos;
    float _lastYaw, _speed, _prevSpeed, _accel, _turn;
    float _phase, _t, _seed, _spin, _nextSetupTry;

    Swing _swing; float _swingT; Pose _from;
    float _flinch, _flinchVel;
    bool _dying, _fallBack; float _deathT, _side = 1f;
    float _fidgetAt, _fidgetT = 9f, _fidgetDir = 1f;

    float StrikeTime  => _p.strike == Strike.Slam ? 0.14f : 0.1f;
    float RecoverTime => _p.strike == Strike.Slam ? 0.5f : 0.35f;
    const float CancelTime = 0.25f;

    void Awake()
    {
        _enemy = GetComponent<Enemy3D>();
        _ct = GetComponent<CombatTarget>();
        if (_enemy != null)
        {
            _refSpeed = Mathf.Max(0.5f, _enemy.moveSpeed);
            _windup = Mathf.Max(0.05f, _enemy.windupSeconds);
            _enemy.SwingStarted += OnSwingStarted;
            _enemy.SwingLanded += OnSwingLanded;
            _enemy.SwingCancelled += OnSwingCancelled;
        }
        if (_ct != null)
        {
            _ct.OnDamaged += OnDamaged;
            _ct.OnDied += OnDied;
        }
        _seed = Random.value * 100f;          // so a pack doesn't breathe and step in lockstep
        _fidgetAt = Random.Range(1f, 4f);
        _lastPos = transform.position;
        _lastYaw = transform.eulerAngles.y;
    }

    void Start() => Setup();

    void OnDestroy()
    {
        if (_enemy != null)
        {
            _enemy.SwingStarted -= OnSwingStarted;
            _enemy.SwingLanded -= OnSwingLanded;
            _enemy.SwingCancelled -= OnSwingCancelled;
        }
        if (_ct != null)
        {
            _ct.OnDamaged -= OnDamaged;
            _ct.OnDied -= OnDied;
        }
    }

    /// <summary>Back to the rest pose — Enemy3D calls this when the creature respawns.</summary>
    public void ResetPose()
    {
        _dying = false; _deathT = 0f;
        _swing = Swing.None; _flinch = _flinchVel = 0f;
        _speed = _prevSpeed = _accel = _turn = 0f;
        _lastPos = transform.position;
        _lastYaw = transform.eulerAngles.y;
        if (_pivot != null)
        {
            _pivot.localPosition = _rest;
            _pivot.localRotation = Quaternion.identity;
            _pivot.localScale = Vector3.one;
        }
    }

    // ── set-up ───────────────────────────────────────────────────────────

    bool Setup()
    {
        if (_pivot != null) return true;
        Transform vis = transform.Find("Visual");
        if (vis == null)
            foreach (Transform child in transform)
                if (child.GetComponentInChildren<Renderer>(true) != null) { vis = child; break; }
        if (vis == null) return false;
        if (!Measure(vis.GetComponentsInChildren<Renderer>(true), out var mn, out var mx)) return false;

        _h = Mathf.Max(0.2f, mx.y - mn.y);
        _w = Mathf.Max(0.1f, mx.x - mn.x);
        _l = Mathf.Max(0.1f, mx.z - mn.z);
        if (gait == Gait.Auto) gait = Guess(_h, _w, _l);
        _p = For(gait);

        _pivot = new GameObject("MotionPivot").transform;
        _pivot.SetParent(transform, false);
        _rest = new Vector3((mn.x + mx.x) * 0.5f, mn.y, (mn.z + mx.z) * 0.5f);
        _pivot.localPosition = _rest;
        vis.SetParent(_pivot, true);
        return true;
    }

    /// <summary>The model's box in root space (so a spawn yaw doesn't skew it), and the biggest
    /// renderer as the visibility probe. Effects (particles, trails) don't count as body.</summary>
    bool Measure(Renderer[] rs, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        float biggest = -1f;
        var toRoot = transform.worldToLocalMatrix;
        foreach (var r in rs)
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            Transform space = r is SkinnedMeshRenderer smr && smr.rootBone != null ? smr.rootBone : r.transform;
            var m = toRoot * space.localToWorldMatrix;
            var lb = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = lb.center + Vector3.Scale(lb.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var p = m.MultiplyPoint3x4(corner);
                mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
            }
            float vol = r.bounds.size.sqrMagnitude;
            if (vol > biggest) { biggest = vol; _probe = r; }
        }
        var size = mx - mn;
        return biggest >= 0f && size.y > 0.01f && size.y < 1000f;
    }

    /// <summary>Long and low reads as four legs (flat and very long as a crawler); tall reads as a
    /// heavy; everything else walks on two legs.</summary>
    static Gait Guess(float h, float w, float l)
    {
        if (l > h * 1.4f && l >= w) return h < l * 0.45f ? Gait.Crawler : Gait.Quadruped;
        return h > 2.8f ? Gait.Heavy : Gait.Biped;
    }

    // ── events from Enemy3D / CombatTarget ───────────────────────────────

    void OnSwingStarted(float windup)
    {
        if (_dying) return;
        _from = SwingPose();
        _windup = Mathf.Max(0.05f, windup);
        _swing = Swing.Windup; _swingT = 0f;
    }

    void OnSwingLanded()
    {
        if (_dying) return;
        _from = SwingPose();
        _swing = Swing.Strike; _swingT = 0f;
    }

    void OnSwingCancelled()
    {
        if (_dying || _swing == Swing.None) return;
        _from = SwingPose();
        _swing = Swing.Cancel; _swingT = 0f;
    }

    void OnDamaged(int _)
    {
        if (_dying || (_ct != null && _ct.IsDead)) return;
        _flinchVel += 30f;   // a kick into the spring below; rapid fire stacks up to the clamp
    }

    void OnDied()
    {
        _dying = true; _deathT = 0f;
        _swing = Swing.None; _flinch = _flinchVel = 0f;
        _side = Random.value < 0.5f ? -1f : 1f;
        bool upright = gait == Gait.Biped || gait == Gait.Heavy || gait == Gait.Shooter || gait == Gait.Hopper;
        _fallBack = upright && Random.value < 0.5f;   // two-legged things go over backwards or sideways
    }

    // ── per frame ────────────────────────────────────────────────────────

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (_pivot == null)
        {
            if (Time.time < _nextSetupTry) return;
            if (!Setup()) { _nextSetupTry = Time.time + 2f; return; }
        }
        _t += dt;

        // What Enemy3D did to the root this frame.
        Vector3 pos = transform.position, d = pos - _lastPos;
        d.y = 0f; _lastPos = pos;
        float raw = d.magnitude / dt;
        if (raw > _refSpeed * 4f + 4f) raw = 0f;   // a teleport (respawn, spawner placement), not a sprint
        float yaw = transform.eulerAngles.y, yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
        _lastYaw = yaw;
        _speed = Damp(_speed, raw, 8f, dt);
        _accel = Damp(_accel, (_speed - _prevSpeed) / dt, 5f, dt);
        _prevSpeed = _speed;
        _turn = Damp(_turn, Mathf.Clamp(yawRate, -400f, 400f), 6f, dt);

        float rel = Mathf.Clamp01(_speed / _refSpeed);
        float amp = Mathf.Sqrt(rel);                           // wandering ≈ 0.7, chasing = 1
        float walk = Mathf.SmoothStep(0f, 1f, rel / 0.15f);    // 0 standing → 1 on the move
        bool fighting = _enemy != null && _enemy.IsAggroOnPlayer;

        if (_p.maxCadence > 0f)
            _phase = Mathf.Repeat(_phase + Mathf.PI * Mathf.Min(_speed / (_p.stride * _h), _p.maxCadence) * dt, 2f * Mathf.PI * 64f);
        float spinRate = _p.spin * (1f + 0.5f * walk) * amount;
        if (_swing == Swing.Windup || _swing == Swing.Strike) spinRate *= 2.5f;
        if (_dying) spinRate *= Mathf.Clamp01(1f - _deathT / 1.4f);
        _spin = Mathf.Repeat(_spin + spinRate * dt, 360f);

        AdvanceSwing(dt);
        AdvanceFlinch(dt);
        AdvanceFidget(dt, walk, fighting);
        if (_dying) _deathT += dt;

        // Nobody can see it: keep the clocks running, skip the pose.
        if (!_dying && _probe != null && !_probe.isVisible) return;

        Pose pose = (Locomotion(amp, walk, fighting) + SwingPose() + FlinchPose()) * amount;
        float pivotY = 0f;
        if (_dying) pose = pose * Mathf.Clamp01(1f - _deathT / 0.3f) + DeathPose(ref pivotY);
        Apply(pose, pivotY);
    }

    void Apply(Pose p, float pivotY)
    {
        var r = Quaternion.Euler(p.pitch, p.yaw, p.roll) * Quaternion.Euler(0f, _spin, 0f);
        var sc = new Vector3(Mathf.Max(0.05f, 1f + p.sxz), Mathf.Max(0.05f, 1f + p.sy), Mathf.Max(0.05f, 1f + p.sxz));
        var pv = new Vector3(0f, pivotY * _h, 0f);   // rotate and scale about this point (feet by default)
        _pivot.localPosition = _rest + pv - r * Vector3.Scale(sc, pv) + new Vector3(p.x, p.y, p.z) * _h;
        _pivot.localRotation = r;
        _pivot.localScale = sc;
    }

    Pose Locomotion(float amp, float walk, bool fighting)
    {
        var p = new Pose();
        float s = Mathf.Sin(_phase), s2 = Mathf.Sin(2f * _phase);
        float g = amp * walk;

        if (_p.hop)
        {
            float u = Mathf.Repeat(_phase / Mathf.PI, 1f);   // progress through one hop
            float air = 4f * u * (1f - u);
            p.y = _p.bob * air * g;
            p.pitch = _p.rock * air * g;                     // tipped forward in the air
            if (_p.squash)
            {
                float land = Mathf.Pow(1f - air, 6f);
                p.sy = (0.07f * air - 0.1f * land) * g;      // stretch in the air, squash on landing
                p.sxz = -0.5f * p.sy;
            }
        }
        else
        {
            p.y = _p.bob * Mathf.Abs(s) * g;                 // lifts once per step
            p.pitch = _p.rock * s2 * g;
        }
        p.roll += _p.roll * s * g;                           // rocks side to side once per stride
        p.yaw = _p.twist * s * g;
        if (_p.roll > 0f) p.x = 0.012f * s * g;

        // Lean forward with speed and while speeding up (treads rock back instead), and into turns.
        float accLean = Mathf.Clamp(_accel * 0.8f, -5f, 5f);
        p.pitch += _p.lean * g + (_p.scan ? -accLean : accLean);
        p.roll += Mathf.Clamp(-_turn * _p.bank, -12f, 12f);

        // Idle life.
        float breath = Mathf.Sin(_t * (2f * Mathf.PI / 3f) + _seed) * _p.breathe * (1f - 0.6f * walk);
        p.sy += breath;
        p.sxz -= 0.5f * breath;
        if (_p.hover > 0f) p.y += _p.hover * (0.6f + 0.4f * Mathf.Sin(_t * 2.6f + _seed));
        if (_p.drift > 0f)
        {
            p.roll += _p.drift * Mathf.Sin(_t * 2.2f + _seed * 2f);
            p.pitch += 0.4f * _p.drift * Mathf.Sin(_t * 1.7f + _seed);
        }
        if (_p.jitter > 0f)
        {
            float rumble = 0.35f + 0.65f * walk;
            p.y += _p.jitter * (Mathf.PerlinNoise(_t * 18f, _seed) - 0.5f) * 2f * rumble;
            p.roll += 0.8f * (Mathf.PerlinNoise(_seed, _t * 15f) - 0.5f) * 2f * rumble;
            p.pitch += 0.5f * (Mathf.PerlinNoise(_t * 13f, _seed + 7f) - 0.5f) * 2f * rumble;
        }
        if (_p.scan && !fighting) p.yaw += 10f * Mathf.Sin(_t * (2f * Mathf.PI / 6f) + _seed) * (1f - walk);
        if (_p.spin > 0f)
        {
            p.pitch += 4f * Mathf.Sin(_t * 4.4f);            // tornado wobble
            p.roll += 4f * Mathf.Cos(_t * 4.4f);
        }

        float f = _fidgetT < 0.3f ? Mathf.Sin(_fidgetT / 0.3f * Mathf.PI) : 0f;
        if (_p.fidget == Fidget.Twitch) p.yaw += _fidgetDir * 9f * f;
        else if (_p.fidget == Fidget.Peck) p.pitch += 16f * f;
        return p;
    }

    void AdvanceFidget(float dt, float walk, bool fighting)
    {
        if (_p.fidget == Fidget.None) return;
        _fidgetT += dt;
        if (_t < _fidgetAt) return;
        _fidgetAt = _t + Random.Range(1.5f, 4.5f);
        if (!fighting && walk < 0.3f && !_dying) { _fidgetT = 0f; _fidgetDir = Random.value < 0.5f ? -1f : 1f; }
    }

    // ── attacks ──────────────────────────────────────────────────────────

    Pose WindupPose()
    {
        switch (_p.strike)
        {
            case Strike.Lunge:  return new Pose { pitch = -8f, z = -0.05f, y = -0.02f };
            case Strike.Slam:   return new Pose { pitch = -14f, z = -0.04f, y = 0.03f };
            case Strike.Rear:   return new Pose { pitch = -16f, z = -0.06f, y = 0.03f };
            case Strike.Recoil: return new Pose { pitch = 3f, y = -0.015f };
            case Strike.Puff:   return new Pose { pitch = -5f, sy = 0.12f, sxz = 0.12f };
            case Strike.Surge:  return new Pose { pitch = -10f, y = 0.08f };
            default:            return new Pose { y = 0.03f };   // Whirl: rises and spins up
        }
    }

    Pose StrikePose()
    {
        switch (_p.strike)
        {
            case Strike.Lunge:  return new Pose { pitch = 12f, z = 0.18f };
            case Strike.Slam:   return new Pose { pitch = 20f, z = 0.08f, y = -0.03f };
            case Strike.Rear:   return new Pose { pitch = 10f, z = 0.28f };
            case Strike.Recoil: return new Pose { pitch = -10f, z = -0.1f };
            case Strike.Puff:   return new Pose { pitch = 8f, z = 0.12f, sy = -0.08f, sxz = 0.05f };
            case Strike.Surge:  return new Pose { pitch = 16f, z = 0.26f, y = 0.02f };
            default:            return new Pose { pitch = 12f, z = 0.25f };
        }
    }

    Pose SwingPose()
    {
        switch (_swing)
        {
            case Swing.Windup:
            {
                float k = _swingT / _windup;
                var p = Pose.Lerp(_from, WindupPose(), Mathf.SmoothStep(0f, 1f, k));
                p.roll += Mathf.Sin(_t * 55f) * 1.5f * Mathf.Clamp01((k - 0.55f) / 0.45f);   // trembles just before it goes
                return p;
            }
            case Swing.Strike:  return Pose.Lerp(_from, StrikePose(), 1f - Sq(1f - Mathf.Clamp01(_swingT / StrikeTime)));
            case Swing.Recover: return Pose.Lerp(StrikePose(), default, Mathf.SmoothStep(0f, 1f, _swingT / RecoverTime));
            case Swing.Cancel:  return Pose.Lerp(_from, default, Mathf.SmoothStep(0f, 1f, _swingT / CancelTime));
            default:            return default;
        }
    }

    void AdvanceSwing(float dt)
    {
        if (_swing == Swing.None) return;
        _swingT += dt;
        if (_swing == Swing.Strike && _swingT >= StrikeTime) { _swing = Swing.Recover; _swingT = 0f; }
        else if (_swing == Swing.Recover && _swingT >= RecoverTime) _swing = Swing.None;
        else if (_swing == Swing.Cancel && _swingT >= CancelTime) _swing = Swing.None;
        // A wind-up holds at full stretch until Enemy3D says the blow landed or was called off.
    }

    // ── hits ─────────────────────────────────────────────────────────────

    void AdvanceFlinch(float dt)
    {
        if (_dying) { _flinch = _flinchVel = 0f; return; }
        // Stiff, slightly under-damped spring: knocked back, a small wobble, settled in ~0.3 s.
        _flinchVel += (-220f * _flinch - 22f * _flinchVel) * dt;
        _flinch = Mathf.Clamp(_flinch + _flinchVel * dt, -1.5f, 1.5f);
    }

    Pose FlinchPose()
    {
        float f = _flinch;
        if (Mathf.Abs(f) < 0.001f) return default;
        var p = new Pose
        {
            pitch = -10f * f, z = -0.05f * f,
            yaw = 5f * f * Mathf.Sin(_t * 47f), roll = 4f * f * Mathf.Sin(_t * 39f + 1f),
        };
        if (_p.squash) { p.sy = -0.1f * f; p.sxz = 0.05f * f; }
        return p;
    }

    // ── death ────────────────────────────────────────────────────────────
    // Every fall ends fully below ground by ~2.1 s, inside Enemy3D's deathLingerSeconds (2.2).

    Pose DeathPose(ref float pivotY)
    {
        var p = new Pose();
        float t = _deathT;
        switch (_p.fall)
        {
            case Fall.Topple:
            {
                float u = Mathf.Clamp01(t / 0.5f);
                float ang = 90f * u * u;                                                              // gravity: slow start, fast finish
                if (t > 0.5f) ang -= 7f * Mathf.Sin(Mathf.Clamp01((t - 0.5f) / 0.25f) * Mathf.PI);   // thud and settle
                float thick = (_fallBack ? _l : _w) / _h;                                             // what it ends up lying on
                if (_fallBack) p.pitch = -ang; else p.roll = _side * ang;
                p.y = 0.5f * thick * Mathf.Sin(ang * Mathf.Deg2Rad) - Sink(t, 1.0f, 1.1f) * (thick + 0.1f);
                break;
            }
            case Fall.FlipOver:
            {
                pivotY = 0.5f;                                                                        // flips about its middle
                float u = Mathf.Clamp01(t / 0.45f);
                p.roll = _side * 180f * Mathf.SmoothStep(0f, 1f, u);
                p.y = 0.35f * Mathf.Sin(u * Mathf.PI) - Sink(t, 1.2f, 0.9f) * 1.1f;
                if (t > 0.45f && t < 1.2f) p.pitch = 5f * Mathf.Sin(t * 38f) * (1.2f - t);           // legs-up twitching
                break;
            }
            case Fall.Collapse:
            {
                float u = Mathf.SmoothStep(0f, 1f, t / 1.2f);
                p.sy = -0.85f * u; p.sxz = 0.2f * u; p.yaw = 120f * u;
                p.y = -0.1f * u - Sink(t, 1.2f, 0.8f) * 0.3f;
                break;
            }
            case Fall.Deflate:
            {
                float u = Mathf.Clamp01(t / 0.7f), e = 1f - (1f - u) * (1f - u) * (1f - u);
                p.sy = -0.6f * e + 0.08f * Mathf.Sin(u * Mathf.PI * 3f) * (1f - u);
                p.sxz = 0.3f * e;
                p.y = -Sink(t, 1.1f, 0.9f) * 0.5f;
                break;
            }
            case Fall.Sag:
            {
                float e = Sq(Mathf.Clamp01(t / 0.4f));
                p.pitch = 10f * e; p.roll = 6f * _side * e;
                p.y = -0.04f * e - Sink(t, 0.9f, 1.2f) * 1.05f;
                break;
            }
            case Fall.Unwind:
            {
                float u = Mathf.SmoothStep(0f, 1f, t / 1.4f);
                p.sy = -0.9f * u; p.sxz = 0.4f * u;
                p.y = -Sink(t, 1.4f, 0.7f) * 0.2f;
                break;
            }
        }
        return p;
    }

    static float Sink(float t, float start, float duration) => Mathf.SmoothStep(0f, 1f, (t - start) / duration);
    static float Sq(float x) => x * x;
    static float Damp(float a, float b, float rate, float dt) => Mathf.Lerp(a, b, 1f - Mathf.Exp(-rate * dt));
}
