using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Optional point-and-click layer for the 3D world (OSRS muscle memory):
///   • Click the ground         — walk there (hold the button to steer); routes around obstacles
///   • Click an enemy           — swing at it (walks into reach first; real-time weapon cooldown)
///   • Shift OR right-mouse held + left-click — "look & fight": plant your feet and swing toward
///                                the cursor (hit whatever's in the arc) while you orbit the camera
///   • Click a node/bank/forge  — walk over and use it
///   • Right-click (no drag)    — "Choose Option" menu: every action for everything under the cursor,
///                                Walk here, Examine, Cancel. A right-DRAG still orbits the camera.
/// Phones (OSRS mobile, gestures from TouchInput): tap = the left-click action, press and hold = the
/// Choose Option menu, drag = rotate the camera, pinch = zoom.
/// WASD / stick input always wins: touching the stick cancels the click path.
/// </summary>
[RequireComponent(typeof(Player3DController))]
public class ClickToMove3D : MonoBehaviour
{
    public float interactRange = 2.6f;
    public float useReach = 3.4f;      // "close enough to use it" — generous so a big NPC/node collider
                                       // that stops you a couple metres out can't block the interaction
    public float holdSteerInterval = 0.15f;
    public float clickAssist = 1.6f;   // forgiveness radius: click NEAR an NPC/node and it still registers
    public LayerMask clickMask = ~0;

    // Cap legacy scene values as well as new components. A world-space 1.6 m assist
    // captured neighbouring objects even when the finger clearly tapped the ground.
    float AssistRadius => Mathf.Min(clickAssist, Application.isMobilePlatform ? 0.15f : 0.35f);
    float UseReach => Mathf.Min(useReach, 2f);
    float ApproachStop => Mathf.Min(interactRange * 0.9f, UseReach - 0.2f);

    /// <summary>How close to a solid resource node's near surface the player works it from. The generic
    /// use reach left you swinging a pickaxe or hatchet at thin air almost two metres short of the node,
    /// so gathering walks right up to it first.</summary>
    public const float GatherReach = 1.1f;
    const float GatherStop = GatherReach - 0.25f;

    /// <summary>True when this is a node you gather by standing right against it (not a fishing spot,
    /// whose interaction point on the bank is already where you stand).</summary>
    public static bool IsSolidNode(Component c) => c is ResourceNode node && node.interactionPoint == null;
    float StopFor(Component c) => IsSolidNode(c) ? GatherStop : ApproachStop;
    float ReachFor(Component c) => IsSolidNode(c) ? GatherReach : UseReach;

    Player3DController _pc;
    ActionCombat3D _combat;
    Interactor3D _interactor;
    Component _pendingUse;     // the node/bank/station we're walking toward
    float _pendingBest; float _pendingStallAt;   // closest we've got to it, and when that last improved
    float _nextSteerAt;
    Vector3 _rmbDownPos; float _rmbDownAt; bool _rmbCanMenu;   // right-click vs right-drag (camera orbit)

    void Awake()
    {
        _pc = GetComponent<Player3DController>();
        _combat = GetComponent<ActionCombat3D>();
        _interactor = GetComponent<Interactor3D>();
    }

    void OnEnable()
    {
        TouchInput.WorldTap += OnWorldTap;
        TouchInput.WorldLongPress += OnWorldLongPress;
    }

    void OnDisable()
    {
        TouchInput.WorldTap -= OnWorldTap;
        TouchInput.WorldLongPress -= OnWorldLongPress;
    }

    // Phones: TouchInput has already told taps, holds and camera drags apart, and skips touches on UI or
    // on an open menu. A tap is the left-click action; a hold opens Choose Option.
    void OnWorldTap(Vector2 screenPos)
    {
        if (Application.isMobilePlatform && AcceptsWorldInput()) HandleClick(true, screenPos);
    }

    void OnWorldLongPress(Vector2 screenPos)
    {
        if (Application.isMobilePlatform && AcceptsWorldInput()) OpenMenu(screenPos);
    }

    bool AcceptsWorldInput()
    {
        if (ChatInput.IsTyping) return false;
        // First-person shooter mode: the mouse aims and left-click fires (ActionCombat3D). Click-to-
        // move / click-to-interact belong to third person only; interaction in FP is the E key.
        if (OrbitCamera3D.FirstPersonActive) return false;
        // While placing a deployable, left-click belongs to the build system, not movement.
        if (BuildManager.Instance != null && BuildManager.Instance.IsPlacing) return false;

        // Swallow input briefly after the scene loads. Pressing the editor Play button (or a
        // scene load) can register as a frame-1 click in the game, which otherwise fires
        // click-to-move and sends the player walking to whatever node is under the cursor at
        // spawn — making it look like the player "teleports to the middle" on spawn.
        return Time.timeSinceLevelLoad >= 0.3f;
    }

    void Update()
    {
        if (!AcceptsWorldInput()) return;

        if (!Application.isMobilePlatform)
        {
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            TrackRightClick(overUI);
            // The click that dismisses the menu (or picks from it) is the menu's, never a walk.
            if (!overUI && !ContextMenuUI.Blocking)
            {
                // "Look & fight" mode: while you HOLD right-click (or middle) to orbit the camera —
                // or hold Shift — left-click attacks TOWARD the cursor instead of moving. So you can
                // freely rotate the view and keep swinging. Held button repeats on the weapon cooldown.
                bool lookMode = Input.GetMouseButton(1) || Input.GetMouseButton(2)
                             || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (lookMode)
                {
                    if (Input.GetMouseButton(0)) AttackInPlaceAtCursor();
                }
                else if (Input.GetMouseButtonDown(0)) HandleClick(true, Input.mousePosition);
                else if (Input.GetMouseButton(0) && Time.time >= _nextSteerAt) HandleClick(false, Input.mousePosition);
            }
        }

        TickPendingUse();
    }

    /// <summary>Where is the cursor pointing in the world? Prefers a physics hit; falls back to the
    /// ray crossing the player's height plane so aiming at the sky/void still gives a direction.</summary>
    void AttackInPlaceAtCursor()
    {
        var cam = Camera.main;
        if (cam == null || _combat == null) return;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        Vector3 aim;
        if (Physics.Raycast(ray, out var hit, 500f, clickMask)) aim = hit.point;
        else
        {
            var plane = new Plane(Vector3.up, transform.position);
            aim = plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position + transform.forward;
        }
        _combat.AttackInPlace(aim);
    }

    void HandleClick(bool firstClick, Vector3 screenPos)
    {
        _nextSteerAt = Time.time + holdSteerInterval;

        // Hold-to-steer must NOT cancel a walk-to-interact errand. Clicking an NPC/node and then
        // holding the button even a fraction of a second was re-running this as a ground-move and
        // wiping _pendingUse — so the player walked over but never interacted (E still worked).
        if (!firstClick && _pendingUse != null) return;

        var cam = Camera.main;
        if (cam == null) return;
        if (!FirstHit(cam.ScreenPointToRay(screenPos), out var hit, out Component direct)) return;

        // What did we click? The collider directly under the cursor, else the nearest clickable
        // within a small radius of the hit point — so a far/small NPC or node doesn't need a
        // pixel-perfect click. (Steering with a held button only moves; it never interacts.)
        Component clicked = firstClick ? (direct ?? NearestClickable(hit.point)) : null;

        if (clicked is CombatTarget enemy)
        {
            if (!enemy.IsDead) { _pendingUse = null; _combat?.Engage(enemy); Feedback(screenPos, true); }
            return;
        }
        if (clicked != null)
        {
            BeginUse(clicked);
            Feedback(screenPos, true);
            return;
        }

        // Plain ground click → walk there (and drop any combat lock).
        if (firstClick) { _combat?.Disengage(); Feedback(screenPos, false); }
        _pendingUse = null;
        _pc.SetDestination(hit.point);
    }

    /// <summary>OSRS click feedback: a yellow cross for walking, a red one for doing something to something
    /// (plus a vibration tick on phones, if Settings ▸ Controls ▸ Vibrate on interaction is on).</summary>
    static void Feedback(Vector2 screenPos, bool interaction)
    {
        ClickCross.Show(screenPos, interaction);
        if (interaction && Application.isMobilePlatform && TouchSettings.VibrateOnInteraction) TouchSettings.Haptic();
    }

    static Component DirectClickable(Collider col) => WorldInteractables.Resolve(col);

    /// <summary>What a click/tap lands on: the nearest thing you can use, pick up or attack, else the first solid
    /// surface. Invisible trigger volumes that belong to nothing (streaming/border/zone triggers) are see-through —
    /// the ray used to stop on them, so a tap there picked nothing and walked nowhere.</summary>
    bool FirstHit(Ray ray, out RaycastHit hit, out Component direct)
    {
        hit = default; direct = null;
        var hits = Physics.RaycastAll(ray, 500f, clickMask, QueryTriggerInteraction.Collide);
        if (hits.Length == 0) return false;
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            var c = DirectClickable(h.collider);
            if (c != null)
            {
                if (!WorldInteractables.RayHitsVisual(c, ray)) continue;
                hit = h; direct = c; return true;
            }
            if (!h.collider.isTrigger) { hit = h; return true; }   // the ground (or a wall) under the finger
        }
        return false;
    }

    // ── right-click "Choose Option" menu ─────────────────────────────────

    /// <summary>A right-click is a press and release without dragging (a drag orbits the camera) and
    /// without a left-click in between (that's look &amp; fight). It opens the menu on release.</summary>
    void TrackRightClick(bool overUI)
    {
        if (Input.GetMouseButtonDown(1))
        {
            _rmbDownPos = Input.mousePosition;
            _rmbDownAt = Time.unscaledTime;
            _rmbCanMenu = !overUI;
        }
        if (!_rmbCanMenu) return;
        if (Input.GetMouseButton(1) && ((Input.mousePosition - _rmbDownPos).sqrMagnitude > 64f || Input.GetMouseButton(0)))
            _rmbCanMenu = false;
        if (Input.GetMouseButtonUp(1))
        {
            _rmbCanMenu = false;
            if (Time.unscaledTime - _rmbDownAt <= 0.5f) OpenMenu(Input.mousePosition);
        }
    }

    /// <summary>OSRS order: each thing's main action, Walk here, each thing's Examine, Cancel. There is
    /// always a menu, even over bare ground.</summary>
    void OpenMenu(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null || ContextMenuUI.Instance == null) return;
        var things = WorldInteractables.UnderCursor(cam.ScreenPointToRay(screenPos), AssistRadius, out var point, out bool hitGround);

        var options = new List<(string, System.Action)>();
        foreach (var c in things)
        {
            var target = c;
            options.Add(($"{WorldInteractables.Verb(c)} {WorldInteractables.Coloured(c)}", () => { Feedback(screenPos, true); UseFromMenu(target); }));
        }
        if (hitGround)
        {
            var p = point;
            options.Add(("Walk here", () => { Feedback(screenPos, false); WalkTo(p); }));
        }
        foreach (var c in things)
        {
            var target = c;
            options.Add(($"Examine {WorldInteractables.Coloured(c)}", () =>
                HUDController.Emit($"<color=#88DDFF>[EXAMINE]:</color> {WorldInteractables.Examine(target)}")));
        }
        options.Add(("Cancel", null));
        ContextMenuUI.Instance.Open(screenPos, "Choose Option", options);
    }

    /// <summary>Do a thing's main action: attack an enemy, or walk over and use anything else.</summary>
    public void UseFromMenu(Component c)
    {
        if (c == null) return;
        if (c is CombatTarget enemy)
        {
            if (!enemy.IsDead) { _pendingUse = null; _combat?.Engage(enemy); }
            return;
        }
        BeginUse(c);
    }

    /// <summary>Walk over to a thing and use it on arrival.</summary>
    void BeginUse(Component c)
    {
        _pendingUse = c;
        _pendingBest = float.PositiveInfinity;
        _pendingStallAt = Time.time;
        _combat?.Disengage();
        _pc.SetDestination(UsePosition(c), StopFor(c));
    }

    /// <summary>Walk to a point (drops combat and any errand). The minimap's tap-to-walk uses it too.</summary>
    public void WalkTo(Vector3 point)
    {
        _combat?.Disengage();
        _pendingUse = null;
        _pc.SetDestination(point);
    }

    Component NearestClickable(Vector3 p)
    {
        Component best = null;
        float bestSq = AssistRadius * AssistRadius;
        foreach (var col in Physics.OverlapSphere(p, AssistRadius, clickMask, QueryTriggerInteraction.Collide))
        {
            var c = DirectClickable(col);
            if (c == null) continue;
            float d = (c.transform.position - p).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = c; }
        }
        return best;
    }

    void TickPendingUse()
    {
        if (_pendingUse == null) return;
        if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
            Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f || _pc.IsDodging)
        {
            _pendingUse = null;
            return;
        }

        Vector3 to = UsePosition(_pendingUse) - transform.position; to.y = 0f;
        float distance = to.magnitude;
        if (distance < _pendingBest - 0.02f) { _pendingBest = distance; _pendingStallAt = Time.time; }

        // Close enough — or, for a node, as close as the ground allows: the walk ended, or the player has
        // been pressed against its collider without getting any nearer, within the usual use reach.
        bool stalled = !_pc.HasDestination || Time.time - _pendingStallAt > 0.3f;
        if (distance <= ReachFor(_pendingUse) || (stalled && distance <= UseReach))
        {
            var use = _pendingUse;
            _pendingUse = null;
            _pc.ClearDestination();
            _interactor?.InteractWith(use);
        }
        else if (!_pc.HasDestination)
        {
            // A failed path must not silently use something up to 8.5 metres away.
            // Fishing already supplies an explicit reachable bank interaction point.
            _pendingUse = null;
            HUDController.Emit("I can't reach that from here. Try approaching from another side.");
        }
    }

    public static Vector3 UsePosition(Component target)
    {
        if (target is ResourceNode node && node.interactionPoint != null) return node.InteractionPosition;
        var player = PlayerEntity.Instance;
        if (player == null || target is GroundItem || target is ITalkableNPC) return target.transform.position;

        // Walk to the near side of solid nodes / stations, not their inaccessible centre.
        // Clamp collision bounds to the visible object so an oversized box cannot grant remote use.
        Bounds visual = default;
        bool hasVisual = false;
        foreach (var renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
            if (!hasVisual) { visual = renderer.bounds; hasVisual = true; }
            else visual.Encapsulate(renderer.bounds);
        }
        Vector3 from = player.transform.position;
        Vector3 best = target.transform.position;
        float bestDistance = float.PositiveInfinity;
        foreach (var collider in target.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || WorldInteractables.Resolve(collider) != target) continue;
            Bounds bounds = collider.bounds;
            if (hasVisual)
            {
                if (!bounds.Intersects(visual)) continue;
                bounds.SetMinMax(Vector3.Max(bounds.min, visual.min), Vector3.Min(bounds.max, visual.max));
            }
            Vector3 point = bounds.ClosestPoint(from);
            point.y = target.transform.position.y;
            float distance = (point - from).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = point; }
        }
        return best;
    }
}
