using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;

/// <summary>
/// ARPG follow camera with a first-person mode: smooth follow, and YOU choose when to turn it —
/// nothing rotates on its own.
///   • Orbit — hold right-click or middle-mouse + drag (yaw + pitch; no stick — it drifted);
///             on a phone, drag one finger across the world
///   • Zoom  — scroll wheel; scrolling all the way IN goes first-person, scrolling out leaves it;
///             on a phone, pinch
///   • View  — V toggles first ↔ third person directly
/// In first person the camera sits at the character's eyes, the body renders shadow-only so it
/// can't block the view, and the player turns to face wherever you look (hold right-click to look).
/// Collision pull-in is OFF by default (it was smashing the camera into the player
/// against walls). Turn it on only if you want it — it's now smoothed and floored so
/// it can never slam onto the character.
/// (Class keeps its old name so existing scenes don't lose the component.)
/// </summary>
public class OrbitCamera3D : MonoBehaviour
{
    /// <summary>True while the camera is in first-person view — other systems (movement facing,
    /// combat swings) read this to behave FPS-style.</summary>
    public static bool FirstPersonActive { get; private set; }

    /// <summary>Cinematic peek (see QuestCam): a world point the camera glides toward, blended by
    /// CinematicWeight (0 = normal follow, 1 = fully on the point). While active it forces the
    /// third-person framing even in first person, so a quest pan always reads clearly.</summary>
    public static Vector3? CinematicPoint;
    public static float CinematicWeight;

    public Transform target;
    public float distance = 16f;
    // minDistance goes right down to eye level so the wheel zooms CONTINUOUSLY into what is
    // effectively first person, with no snap to a separate mode.
    public float minDistance = 0.35f, maxDistance = 26f;

    [Tooltip("Below this ZOOM setting the player body is hidden (still casts shadows), so " +
             "zooming fully in doesn't put the camera inside the model. Compared against the zoom " +
             "you chose with the wheel — NOT the collision-shortened distance, so walking indoors " +
             "never makes you vanish.")]
    public float hideBodyBelowDistance = 1.1f;

    [Tooltip("Hard safety net: if collision jams the camera closer than this the body hides " +
             "regardless of zoom, so you never see through the inside of the head. Keep it well " +
             "under hideBodyBelowDistance.")]
    public float hideBodyHardFloor = 0.5f;
    public float pitch = 52f;          // starting tilt; drag right/middle-mouse to change it
    public float minPitch = 8f, maxPitch = 85f;          // how far you can tilt down/up
    public float yaw = 0f;             // world heading — rotated by input below
    public float followLerp = 10f;
    public float zoomSpeed = 12f;

    [Header("Turning (you choose when)")]
    public float keyRotateSpeed   = 90f;    // Q / E degrees per second
    public float mouseRotateSpeed = 4f;     // middle-mouse drag
    public float stickRotateSpeed = 120f;   // right stick degrees per second
    public float stickDeadzone    = 0.30f;  // ignore resting/jittery stick (and stray axis drift)
    public bool  stickInvertY     = false;  // flip if pushing the right stick up tilts the wrong way

    [Header("Touch (mobile)")]
    public float touchRotateSpeed = 0.12f;  // one- or two-finger drag → orbit (degrees per pixel)
    public float pinchZoomSpeed   = 0.03f;  // two-finger pinch → zoom (distance per pixel)

    [Header("Collision (optional)")]
    public bool avoidClipping = false;      // OFF: no more smashing into the player at walls
    public LayerMask collisionLayers = ~0;
    public float cameraRadius = 0.3f;

    [Header("First person")]
    [Tooltip("OFF (default): zooming all the way in just stops at Min Distance and stays third-person. " +
             "ON: scrolling in past minimum snaps to first person, scrolling out leaves it. " +
             "The toggle key works either way.")]
    public bool zoomIntoFirstPerson = false;

    public KeyCode toggleViewKey = KeyCode.V;
    public float fpPitchMin = -65f, fpPitchMax = 75f;
    [Tooltip("Eye height as a fraction of the character's rendered height (fallback 1.65 m).")]
    public float eyeHeightFraction = 0.92f;

    [Header("Shooter mode (first-person lock)")]
    [Tooltip("Lock the game to first person — the FPS pivot. V no longer leaves it, the mouse looks " +
             "freely (no need to hold a button), and the cursor locks to screen centre for aiming. " +
             "Off by default: the game boots third-person point-and-click (tick combat); press V to " +
             "toggle first person if you want it.")]
    public bool firstPersonOnly = false;
    [Tooltip("Mouse look sensitivity in first person (degrees per mouse-delta unit).")]
    public float fpMouseSensitivity = 2.2f;
    [Tooltip("Hold to free the mouse cursor so you can click menus with the mouse. Controller players " +
             "navigate menus with the pad and never need this. Menus that expose IsOpen also free it " +
             "automatically.")]
    public KeyCode freeCursorKey = KeyCode.LeftAlt;

    float _dist;
    bool _firstPerson;
    bool _cursorFree;
    float _fpPitch;
    // Renderers we've switched to shadows-only for first person, with their original modes, so the
    // body reappears exactly as it was when you zoom back out. Re-scanned every frame in FP because
    // EquipmentVisuals spawns/destroys held models at any time.
    readonly System.Collections.Generic.Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> _hidden = new();

    void Start()
    {
        // Self-heal the #1 "flat/dull" cause: URP ignores Bloom/Tonemapping/Color grading unless the
        // camera opts into post-processing. This scene shipped with it OFF, so force it on (+ cheap FXAA).
        var cam = GetComponent<Camera>();
        var camData = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
        if (camData != null)
        {
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        }

        // Old scenes serialized bad zoom limits (45 = way too far, then 15 = too tight — "all you
        // see is grass"). Snap the range to the current [~26, 30-cap] without re-touching scenes.
        if (maxDistance < 26f) maxDistance = 26f;
        if (maxDistance > 30f) maxDistance = 30f;
        if (zoomSpeed   < 12f) zoomSpeed   = 12f;
        _dist = Mathf.Clamp(distance, minDistance, maxDistance);

        // Self-heal: scenes whose camera lost its Player reference (the target clears when the
        // player visual is swapped) would otherwise sit frozen. Find the player ourselves.
        if (target == null) target = FindPlayerTarget();

        // Shooter pivot: boot straight into first person and stay there.
        if (firstPersonOnly) SetFirstPerson(true);
    }

    /// <summary>Locate the player's transform so the camera can follow it even when the serialized
    /// Target reference was cleared (e.g. after a visual swap).</summary>
    static Transform FindPlayerTarget()
    {
        var pe = PlayerEntity.Instance != null ? PlayerEntity.Instance : Object.FindAnyObjectByType<PlayerEntity>();
        return pe != null ? pe.transform : null;
    }

    void LateUpdate()
    {
        // Always lock onto the live player. The serialized Target reference gets wiped (or left
        // pointing at a stale object) whenever the player or its visual is swapped while editing —
        // which is what kept stranding the camera. Re-acquiring the live player every frame makes
        // that impossible: it no longer matters how often or how the reference clears.
        var live = FindPlayerTarget();
        if (live != null) target = live;
        if (target == null) return;

        UpdateCursorLock();

        // V toggles first ↔ third person (ignored while typing in chat). Disabled when locked to
        // first person — the shooter has no third-person mode to switch to.
        if (!firstPersonOnly && !ChatInput.IsTyping && Input.GetKeyDown(toggleViewKey))
            SetFirstPerson(!_firstPerson);

        // Don't zoom the camera when the wheel is being used to scroll a UI panel
        // (music list, inventory, etc.) — only zoom when the pointer is over the world.
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        float scroll = overUI ? 0f : Input.GetAxis("Mouse ScrollWheel");
        if (!_firstPerson && scroll != 0f)
        {
            // Scrolling IN while already at minimum zoom crosses into first person — but only
            // if that's wanted. With zoomIntoFirstPerson off, zooming just stops at minDistance
            // and first person is reachable solely via the toggle key.
            if (zoomIntoFirstPerson && scroll > 0f && distance <= minDistance + 0.01f)
                SetFirstPerson(true);
            else
                distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
        }
        else if (_firstPerson && scroll < 0f && zoomIntoFirstPerson && !firstPersonOnly)
        {
            // Scrolling OUT leaves first person; SetFirstPerson restores the zoom you had before.
            // Only when scroll owns the transition — otherwise the toggle key is the only way out.
            // Never in the shooter lock.
            SetFirstPerson(false);
        }

        // Shooter free-look: in the locked first-person view the mouse aims directly, no button held,
        // as long as the cursor is captured (it's released while a menu is open or Alt is held).
        bool fpFreeLook = firstPersonOnly && _firstPerson && !_cursorFree && !Application.isMobilePlatform;
        if (fpFreeLook)
        {
            yaw     += Input.GetAxis("Mouse X") * fpMouseSensitivity;
            _fpPitch -= Input.GetAxis("Mouse Y") * fpMouseSensitivity;
            _fpPitch  = Mathf.Clamp(_fpPitch, fpPitchMin, fpPitchMax);
        }

        // Free orbit (yaw + pitch) while holding right-click or middle-mouse (the scroll-wheel button).
        // Q/E were removed (E is the interact key). Skipped on mobile: a two-finger touch emulates
        // the right mouse button and pumps the Mouse X/Y axes with junk, which made the camera spin
        // wildly — there, the touch block below orbits. Skipped during shooter free-look (above owns it).
        if (!fpFreeLook && !Application.isMobilePlatform && (Input.GetMouseButton(1) || Input.GetMouseButton(2)))
        {
            yaw += Input.GetAxis("Mouse X") * mouseRotateSpeed;
            if (_firstPerson)
            {
                _fpPitch -= Input.GetAxis("Mouse Y") * mouseRotateSpeed;
                _fpPitch  = Mathf.Clamp(_fpPitch, fpPitchMin, fpPitchMax);
            }
            else
            {
                pitch -= Input.GetAxis("Mouse Y") * mouseRotateSpeed;
                pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
        }

        // Controller right stick — PoE2-style split:
        //   • THIRD person: the stick AIMS THE CHARACTER (Player3DController reads it), and this
        //     isometric camera deliberately stays put — twin-stick feel, the camera never fights you.
        //   • FIRST person: camera yaw IS your facing, so here the stick looks around.
        if (_firstPerson)
        {
            float rsx = DeadzonedAxis("RightStickX");
            float rsy = DeadzonedAxis("RightStickY");
            if (rsx != 0f) yaw += rsx * stickRotateSpeed * Time.deltaTime;
            if (rsy != 0f)
            {
                float v = rsy * stickRotateSpeed * Time.deltaTime * (stickInvertY ? -1f : 1f);
                _fpPitch = Mathf.Clamp(_fpPitch + v, fpPitchMin, fpPitchMax);
            }
        }

        // Mobile, OSRS style: dragging one finger across the world rotates the view as if you'd grabbed
        // the ground (TouchInput only hands over real drags — taps and long-presses never turn it).
        // (No touches on desktop, so this and the block below are inert there.)
        Vector2 drag = TouchInput.WorldDragDelta;
        if (drag != Vector2.zero)
        {
            yaw -= drag.x * touchRotateSpeed;
            if (_firstPerson) _fpPitch = Mathf.Clamp(_fpPitch - drag.y * touchRotateSpeed, fpPitchMin, fpPitchMax);
            else              pitch    = Mathf.Clamp(pitch - drag.y * touchRotateSpeed, minPitch, maxPitch);
        }

        // Two fingers: pinch zooms, and dragging both together orbits the same way one finger does.
        if (Input.touchCount >= 2)
        {
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);

            // Both fingers moving together (average pan) → orbit. A pure pinch nets ~zero average, so
            // the two gestures stay separable.
            Vector2 avgDelta = (t0.deltaPosition + t1.deltaPosition) * 0.5f;
            yaw   -= avgDelta.x * touchRotateSpeed;
            pitch -= avgDelta.y * touchRotateSpeed;
            pitch  = Mathf.Clamp(pitch, minPitch, maxPitch);

            // Spread/squeeze the finger gap → zoom in/out (the scroll wheel doesn't exist on a phone).
            float prevGap = ((t0.position - t0.deltaPosition) - (t1.position - t1.deltaPosition)).magnitude;
            float gap     = (t0.position - t1.position).magnitude;
            distance = Mathf.Clamp(distance - (gap - prevGap) * pinchZoomSpeed, minDistance, maxDistance);
        }

        // The character's rendered bounds feed both the third-person focus height and the
        // first-person eye height, so any model scale gets framed correctly.
        // IMPORTANT: only renderers near the player count. A held-item model spawned on a broken
        // hand bone can end up hundreds of metres away — blindly encapsulating it dragged the
        // camera focus out to the midpoint ("camera moves to the object you equip"). Anything
        // beyond a few metres from the root is ignored for framing.
        Bounds vb = default; bool hasBounds = false;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            Bounds b = r.bounds;
            if ((b.center - target.position).sqrMagnitude > 16f) continue;   // > 4 m off the root: not the body
            if (!hasBounds) { vb = b; hasBounds = true; } else vb.Encapsulate(b);
        }
        if (hasBounds && vb.size.y <= 0.01f) hasBounds = false;

        bool cinematic = CinematicPoint.HasValue && CinematicWeight > 0.001f;

        if (_firstPerson && !cinematic)
        {
            // Re-hide every frame: held-item models spawn/despawn at any time and must never pop
            // into view an inch from the lens.
            HideBodyForFirstPerson();

            float eyeY = hasBounds ? vb.min.y + vb.size.y * eyeHeightFraction
                                   : target.position.y + 1.65f;
            var fpRot = Quaternion.Euler(_fpPitch, yaw, 0f);
            // Snap, don't lerp — a lagging first-person camera reads as motion sickness.
            transform.position = new Vector3(target.position.x, eyeY, target.position.z);
            transform.rotation = fpRot;
            return;
        }

        var rot = Quaternion.Euler(pitch, yaw, 0f);
        // Aim at ~65% up the model's ACTUAL rendered height, not a fixed 1.2 m — so a hand-scaled
        // model (e.g. one you sized to 1000 because it imports microscopic) still gets framed at the
        // chest/head instead of the camera staring at its feet. Falls back to 1.2 m if no renderers.
        Vector3 focus = hasBounds
            ? new Vector3(target.position.x, vb.min.y + vb.size.y * 0.65f, target.position.z)
            : target.position + Vector3.up * 1.2f;

        // Quest peek: glide the focus toward the point of interest and back (QuestCam ramps the weight).
        if (cinematic)
            focus = Vector3.Lerp(focus, CinematicPoint.Value, CinematicWeight);

        float wantDist = distance;
        if (avoidClipping &&
            Physics.SphereCast(focus, cameraRadius, -(rot * Vector3.forward), out var hit, distance,
                               collisionLayers, QueryTriggerInteraction.Ignore))
            wantDist = Mathf.Max(minDistance * 0.5f, hit.distance);   // gentle floor — never on the player

        _dist = Mathf.Lerp(_dist, wantDist, 8f * Time.deltaTime);     // smooth so it can't snap

        // Zoom is continuous all the way down to eye level — no snapping into a first-person
        // MODE. Once the camera is close enough that it would be looking through the model,
        // hide the body (ShadowsOnly, so it still casts a shadow) and restore it on the way
        // back out. Same trick first person uses, without the mode switch.
        // Gate on `distance` (the zoom YOU chose with the wheel), never on `_dist` (which collision
        // above may have collapsed). Otherwise walking into a building shoves the camera in and
        // silently turns the player invisible — reads exactly like the character was deleted.
        // `hideBodyHardFloor` is the one case collision still hides you: the camera is genuinely
        // inside the model, where the alternative is rendering the inside of the skull.
        if (distance <= hideBodyBelowDistance || _dist <= hideBodyHardFloor) HideBodyForFirstPerson();
        else if (_hidden.Count > 0) RestoreBody();

        Vector3 finalPos = focus - rot * Vector3.forward * _dist;
        transform.position = Vector3.Lerp(transform.position, finalPos, followLerp * Time.deltaTime);
        transform.rotation = rot;
    }

    /// <summary>
    /// Capture the cursor for mouse-look in the shooter, and release it when the player needs to click
    /// UI — while typing, while a known menu panel is open, while the controller is driving a menu, or
    /// while the free-cursor key (Alt) is held. Only active in the first-person lock.
    /// </summary>
    void UpdateCursorLock()
    {
        if (!firstPersonOnly) { _cursorFree = true; return; }

        bool wantFree = ChatInput.IsTyping
                     || Input.GetKey(freeCursorKey)
                     || AnyMenuOpen();

        _cursorFree = wantFree;
        Cursor.lockState = wantFree ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = wantFree;
    }

    /// <summary>True if a mouse-driven panel is up, so the cursor should be freed. Covers the panels
    /// that expose a static IsOpen; anything else is reachable by holding the free-cursor key.</summary>
    static bool AnyMenuOpen()
    {
        if (ControllerUI.MenuFocused) return true;
        if (SkillsPanelUI.Instance != null && SkillsPanelUI.Instance.IsOpen) return true;
        if (CombatStyleUI.Instance != null && CombatStyleUI.Instance.IsOpen) return true;
        return false;
    }

    /// <summary>Right-stick axis with the deadzone removed and the remaining range rescaled to 0–1,
    /// so a drifting/resting stick reads exactly 0 but deliberate input ramps smoothly. Returns 0
    /// if the axis isn't defined in the Input Manager (defensive — this project defines both).</summary>
    float DeadzonedAxis(string axis)
    {
        float v;
        try { v = Input.GetAxisRaw(axis); }
        catch (System.ArgumentException) { return 0f; }
        float a = Mathf.Abs(v);
        if (a <= stickDeadzone) return 0f;
        return Mathf.Sign(v) * (a - stickDeadzone) / (1f - stickDeadzone);
    }

    // Scene reload / camera swap while in first person: put the body back and clear the static
    // flag so movement facing and the next camera start from a clean third-person state.
    void OnDisable()
    {
        if (_firstPerson) RestoreBody();
        _firstPerson = false;
        FirstPersonActive = false;
        // Never leave the cursor captured behind us (scene unload / component disable / stop play).
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    float _preFpDistance = -1f;

    void SetFirstPerson(bool on)
    {
        if (_firstPerson == on) return;
        _firstPerson = on;
        FirstPersonActive = on;
        if (on)
        {
            _preFpDistance = distance;   // remember the third-person framing to come back to
            _fpPitch = 0f;   // level gaze on entry — the orbit pitch (often 52°) would stare at the floor
            HUDController.Emit("<color=#9AD1FF>[VIEW]:</color> First person — scroll out or press V to go back.");
        }
        else
        {
            RestoreBody();
            // Come back to exactly the view you left — not slammed to minimum zoom.
            if (_preFpDistance > 0f) { distance = _preFpDistance; _dist = _preFpDistance; }
        }
    }

    /// <summary>Switch every renderer on the character (body + held items) to shadows-only so the
    /// model never blocks the first-person view but still casts its shadow. Original modes are
    /// remembered per renderer and restored on exit.</summary>
    void HideBodyForFirstPerson()
    {
        if (target == null) return;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (r == null || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
            if (!_hidden.ContainsKey(r)) _hidden[r] = r.shadowCastingMode;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
    }

    void RestoreBody()
    {
        foreach (var kv in _hidden)
            if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
        _hidden.Clear();
    }
}
