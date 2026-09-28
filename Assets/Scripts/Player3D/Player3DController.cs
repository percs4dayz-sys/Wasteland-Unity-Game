using UnityEngine;

/// <summary>
/// 3D character movement — WASD / left stick (camera-relative), plus click-to-move.
/// Works with keyboard+mouse AND Xbox controller out of the box (legacy input axes:
/// Horizontal/Vertical are bound to both keyboard and the left stick by default).
///
/// Combat is real-time click-to-swing (see ActionCombat3D): every attack is a click,
/// gated only by your weapon's cooldown. Skilling (gathering/crafting) stays on the
/// game tick. In first person (V / scroll all the way in) the character faces where
/// the camera looks.
///
/// Controls:
///   Move   — WASD / left stick, or click the ground (ClickToMove3D)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class Player3DController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5.5f;
    public float rotateSpeed = 12f;
    public float gravity = -20f;

    [Header("Water")]
    [Tooltip("Stop the player at the shoreline — an invisible wall along every water edge. Turn off if it ever traps you.")]
    public bool blockWater = true;
    [Tooltip("World Y of the water surface. Terrain below this is 'water'.")]
    public float seaLevel = 50.3f;

    /// <summary>True when the terrain surface under a world position sits below the water line.
    /// Samples whichever terrain tile is under the point (tiled worlds like the Broken Crescent's 3x3),
    /// and takes the water line from the scene's "Sea Level (Water)" plane when there is one — so the
    /// shoreline wall matches the actual sea instead of another scene's number.</summary>
    bool IsOverWater(Vector3 p)
    {
        if (_cc != null) p.y += _cc.center.y - _cc.height * 0.5f;
        return WaterSurface.Blocks(p, WaterLine(), transform);
    }

    float _waterLine; int _waterScene = -1;
    float WaterLine()
    {
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (_waterScene != scene)   // re-read per scene, in case the player survives a scene change
        {
            _waterScene = scene;
            var sea = GameObject.Find("Sea Level (Water)");
            _waterLine = sea != null ? sea.transform.position.y : seaLevel;
        }
        return _waterLine;
    }

    /// <summary>Set by ActionCombat3D while aiming (slows you down).</summary>
    [HideInInspector] public float MoveMultiplier = 1f;

    [Header("Click-to-move")]
    public float arriveDistance = 0.25f;

    [Header("Dodge / slide")]
    public KeyCode dodgeKey = KeyCode.Space;      // pad: B button
    public float dodgeDistance = 4.5f;            // metres covered by one slide
    public float dodgeDuration = 0.35f;           // seconds the slide lasts
    public float dodgeCooldown = 5f;              // seconds after a slide before the next

    [Header("Run")]
    public KeyCode runKey = KeyCode.LeftControl;   // hold to run (Shift is taken by attack-in-place)
    public float runMultiplier = 1.8f;             // run speed = moveSpeed × this
    /// <summary>True while sprinting — read by anything that cares (SFX, stamina later).</summary>
    public bool IsRunning { get; private set; }

    [Header("Twin-stick aim (controller)")]
    public float aimStickDeadzone = 0.30f;        // resting drift reads as zero
    public float aimTurnSpeed = 16f;              // how snappily the character turns to the stick
    public bool aimInvertY = false;               // flip if stick-up aims toward the camera

    public bool IsMoving { get; private set; }
    public bool HasDestination => _dest.HasValue;
    /// <summary>Where the click-to-move path is headed (the minimap puts OSRS's red flag on it).</summary>
    public Vector3? Destination => _dest;

    /// <summary>True during the slide window. Enemy3D checks this — an attack that lands
    /// mid-slide whiffs, so dodging is a real defensive tool, not just movement.</summary>
    public bool IsDodging => Time.time < _dodgeEndAt;

    /// <summary>True while the right stick is deflected — the character is being aimed by the stick
    /// (PoE2 twin-stick style: movement strafes instead of turning you).</summary>
    public bool StickAiming { get; private set; }

    /// <summary>Fires when a slide starts — PlayerAnimator3D plays the slide/dodge clip off this.</summary>
    public event System.Action OnDodged;

    /// <summary>True while the player is resting on the ground. Goes false when the player has
    /// no walkable surface beneath it (fell through the world) — used by PlayerRespawn's
    /// out-of-bounds watchdog to return the player to spawn.</summary>
    public bool IsGrounded { get; private set; }

    CharacterController _cc;
    float _vy;
    Vector3? _dest;
    float _destStop;
    float _dodgeEndAt, _dodgeReadyAt;
    Vector3 _dodgeDir;

    void Awake()
    {
        NormalizeRootScale();
        _cc = GetComponent<CharacterController>();
        // Click-to-move routes round obstacles through the navigator; every world gets one.
        if (GetComponent<BlackwaterNavigator>() == null) gameObject.AddComponent<BlackwaterNavigator>();
        // A scene saved with the original 1.2 s dodge cooldown gets the tuned 5 s without
        // re-touching the inspector (any other hand-set value is respected).
        if (Mathf.Approximately(dodgeCooldown, 1.2f)) dodgeCooldown = 5f;
    }

    /// <summary>
    /// Self-heal for the #1 movement-breaking mistake after a model swap: scaling the player ROOT
    /// (e.g. to 5,5,5) to size the new mesh, instead of scaling the Visual child. The
    /// CharacterController lives on the root, so a scaled root inflates the capsule (radius/height ×5)
    /// into a giant invisible blob that overlaps the terrain and nearby building/node colliders — and
    /// CharacterController.Move then depenetrates it, shoving the player AWAY from wherever you walk
    /// (clicking toward a structure deflects you off it). The ground-clamp math is scale-agnostic too,
    /// so the feet seat wrong and the model clips through the floor.
    ///
    /// Fix: force the root back to unit scale and push the size onto the Visual child, where
    /// PlayerAnimator3D's foot-align then normalises the model to human height anyway. Runs on every
    /// load, so any future swap auto-corrects with zero tribal knowledge.
    /// </summary>
    void NormalizeRootScale()
    {
        Vector3 s = transform.localScale;
        if (Mathf.Approximately(s.x, 1f) && Mathf.Approximately(s.y, 1f) && Mathf.Approximately(s.z, 1f))
            return;

        var vis = transform.Find("Visual");
        if (vis != null) vis.localScale = Vector3.Scale(vis.localScale, s);
        transform.localScale = Vector3.one;

        Debug.Log($"[Player3DController] Player root was scaled {s:F2} — reset to 1 and moved the scale " +
                  "onto the Visual child. A scaled root inflates the CharacterController and breaks movement.");
    }

    /// <summary>Walk to a world point (click-to-move / combat follow). Any direct input cancels it.</summary>
    public void SetDestination(Vector3 point, float stopDistance = -1f)
    {
        _dest = point;
        _destStop = Mathf.Max(stopDistance, arriveDistance);
        GetComponent<BlackwaterNavigator>()?.Plan(point, _destStop);
    }

    public void ClearDestination() { _dest = null; IsMoving = false; }

    void Update()
    {
        if (IsTyping()) { ApplyGravityOnly(); return; }
        // While a menu is focused on a controller, the stick drives the UI — stand still so you don't
        // walk off while navigating. (Mouse/keyboard players never trip this.)
        if (ControllerUI.MenuFocused) { ApplyGravityOnly(); return; }

        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool direct = input.sqrMagnitude > 0.01f;
        if (direct) _dest = null;   // touching the stick always wins over a click path

        // Slide/dodge (Space / B button): a quick burst in your movement direction — facing if
        // standing still. Attacks that land mid-slide miss (see Enemy3D), CoD-style.
        if ((Input.GetKeyDown(dodgeKey) || Input.GetKeyDown(KeyCode.JoystickButton1))
            && Time.time >= _dodgeReadyAt && !IsDodging)
        {
            Vector3 dir = direct ? CameraRelative(input)
                        : _dest.HasValue ? DestinationMove()
                        : transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            _dodgeDir = dir.normalized;
            _dodgeEndAt = Time.time + dodgeDuration;
            _dodgeReadyAt = _dodgeEndAt + dodgeCooldown;
            _dest = null;
            OnDodged?.Invoke();
        }

        Vector3 move = IsDodging ? _dodgeDir
                     : direct    ? CameraRelative(input)
                                 : DestinationMove();
        IsMoving = move.sqrMagnitude > 0.001f;

        // Run: hold Left Ctrl (keyboard) or Left Bumper (controller). Applies to WASD/stick AND
        // click-to-move, so you can sprint to a spot too. Not while sliding (dodge has its own burst).
        IsRunning = IsMoving && !IsDodging &&
                    (Input.GetKey(runKey) || Input.GetKey(KeyCode.JoystickButton4));
        float speed = moveSpeed * MoveMultiplier * (IsRunning ? runMultiplier : 1f);

        Vector3 velocity = IsDodging
            ? _dodgeDir * (dodgeDistance / Mathf.Max(dodgeDuration, 0.05f))
            : move * speed;

        if (_cc.isGrounded && _vy < 0f) _vy = -2f;
        _vy += gravity * Time.deltaTime;
        velocity.y = _vy;

        Vector3 before = transform.position;
        _cc.Move(velocity * Time.deltaTime);

        // Invisible water wall: if that step put us out over water (terrain surface below sea level),
        // cancel the horizontal move so you stop at the shoreline instead of wading in. Works on every
        // lake and ocean edge with no placed colliders. Vertical (gravity/step) is left alone.
        if (blockWater && !IsOverWater(before))
        {
            // Sample the whole step so a fast dodge cannot cross a narrow stream in one frame.
            Vector3 after = transform.position;
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(before, after) / 0.25f));
            for (int i = 1; i <= samples; i++)
                if (IsOverWater(Vector3.Lerp(before, after, i / (float)samples)))
                {
                    transform.position = before;
                    ClearDestination();
                    _dodgeEndAt = Time.time;
                    break;
                }
        }

        // Keep on the ground (Y). When standing still, also undo any slope-induced horizontal
        // drift, so the player stays exactly where it's placed instead of sliding down to the
        // low ground — this game has click/stick movement only, never momentum or sliding.
        bool grounded = ClampToGround();
        IsGrounded = grounded;
        if (grounded && !IsMoving)
        {
            Vector3 after = transform.position;
            transform.position = new Vector3(before.x, after.y, before.z);
        }

        // Facing, in priority order:
        //   1. First person — face where the camera looks, FPS-style.
        //   2. Right stick deflected — PoE2 twin-stick: the stick aims the character (camera-relative),
        //      and movement becomes strafing instead of turning you.
        //   3. Otherwise — face where you're going.
        StickAiming = false;
        if (OrbitCamera3D.FirstPersonActive)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 look = cam.transform.forward; look.y = 0f;
                if (look.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look),
                                                          rotateSpeed * Time.deltaTime);
            }
        }
        else
        {
            Vector2 aim = AimStick();
            if (aim != Vector2.zero)
            {
                StickAiming = true;
                Vector3 dir = CameraRelative(aim);
                if (dir.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir),
                                                          aimTurnSpeed * Time.deltaTime);
            }
            else
            {
                Vector3 face = move;
                face.y = 0f;
                if (face.sqrMagnitude > 0.001f)
                {
                    var want = Quaternion.LookRotation(face);
                    transform.rotation = Quaternion.Slerp(transform.rotation, want, rotateSpeed * Time.deltaTime);
                }
            }
        }
    }

    /// <summary>Right-stick aim vector (x = right, y = away from camera), deadzoned so resting
    /// drift reads as exactly zero. Zero if the axes aren't defined in the Input Manager.</summary>
    Vector2 AimStick()
    {
        float x, y;
        try
        {
            x = Input.GetAxisRaw("RightStickX");
            y = Input.GetAxisRaw("RightStickY");
        }
        catch (System.ArgumentException) { return Vector2.zero; }

        // Stick-up usually reports negative on Unity's joystick Y — flip so up = away from camera.
        y = aimInvertY ? y : -y;
        var v = new Vector2(x, y);
        return v.magnitude > aimStickDeadzone ? v : Vector2.zero;
    }

    Vector3 DestinationMove()
    {
        if (!_dest.HasValue) return Vector3.zero;
        var navigation = GetComponent<BlackwaterNavigator>();
        if (navigation != null)
        {
            Vector3 direction = navigation.Direction(_destStop, out bool arrived);
            if (arrived) _dest = null;
            return direction;
        }
        Vector3 to = _dest.Value - transform.position; to.y = 0f;
        if (to.magnitude <= _destStop) { _dest = null; return Vector3.zero; }
        return to.normalized;
    }

    void ApplyGravityOnly()
    {
        if (_cc.isGrounded && _vy < 0f) _vy = -2f;
        _vy += gravity * Time.deltaTime;
        _cc.Move(new Vector3(0f, _vy, 0f) * Time.deltaTime);
        IsGrounded = ClampToGround();
        IsMoving = false;
    }

    /// <summary>
    /// Keeps the player on the ground surface. The imported world uses a scaled mesh collider
    /// that a CharacterController can tunnel straight through (the player free-falls into the
    /// void at spawn). This game has no jump, so the player should always be on the ground:
    /// each frame we raycast down onto the nearest walkable top and, if the capsule has reached
    /// or sunk below it, pin the capsule bottom back onto the surface. Makes falling-through
    /// impossible regardless of collider quality.
    /// </summary>
    /// <returns>True if the player is resting on the ground (pinned), false if airborne / no ground.</returns>
    bool ClampToGround()
    {
        Vector3 p = transform.position;
        var hits = Physics.RaycastAll(new Ray(new Vector3(p.x, 5000f, p.z), Vector3.down), 20000f,
                                      ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;  // ignore our own capsule
            if (h.normal.y < 0.2f) continue;                          // walkable top, not a wall/underside
            float bottomOffset = _cc.center.y - _cc.height * 0.5f;
            float feet = p.y + bottomOffset;
            // A surface well ABOVE our feet is a rooftop / overhang / upper floor (e.g. a
            // city building's box collider). Skip it and keep looking downward for the real
            // ground, otherwise we get teleported up onto the roof.
            if (h.point.y > feet + 0.5f) continue;
            if (feet <= h.point.y + 0.05f)                            // at or below the surface → pin on top
            {
                transform.position = new Vector3(p.x, h.point.y - bottomOffset + 0.02f, p.z);
                if (_vy < 0f) _vy = -2f;
                return true;
            }
            return false;   // surface is below us — we're falling onto it, not standing on it
        }
        return false;       // no ground beneath
    }

    Vector3 CameraRelative(Vector2 input)
    {
        Vector3 fwd, right;
        var cam = Camera.main;
        if (cam != null) { fwd = cam.transform.forward; right = cam.transform.right; }
        else             { fwd = Vector3.forward;       right = Vector3.right; }
        fwd.y = 0f; right.y = 0f;
        fwd.Normalize(); right.Normalize();
        Vector3 m = fwd * input.y + right * input.x;
        return m.sqrMagnitude > 1f ? m.normalized : m;
    }

    static bool IsTyping() => ChatInput.IsTyping;
}
