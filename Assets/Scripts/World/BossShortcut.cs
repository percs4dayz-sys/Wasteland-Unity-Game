using UnityEngine;

/// <summary>
/// Opens a shortcut (a drawbridge, gate, ramp, lift, etc.) when a boss dies, then keeps it open
/// forever via a saved flag. Clearing the final tier can drop a bridge straight back toward your
/// base — and it stays dropped after you reload, because the flag rides along in the save.
///
/// Setup (no tribal knowledge needed):
///   1. Place the bridge where it should end up when OPEN (its final, walkable spot). Give it a
///      Collider so the player can walk across it.
///   2. Add this component to that bridge object.
///   3. Drag the boss (anything with a CombatTarget) into "Boss".
///   4. Set the CLOSED pose as an offset from where you placed it:
///        • Raised Euler Offset X = -85  → a drawbridge standing upright, swings down flat, or
///        • Raised Position Offset Y = 12 → a slab that drops in from above, etc.
/// It starts closed, animates open when the boss dies, and is already open on load if the boss was
/// beaten in a previous session.
/// </summary>
public class BossShortcut : MonoBehaviour
{
    [Header("What unlocks it")]
    [Tooltip("The boss whose death opens the shortcut.")]
    public CombatTarget boss;

    [Tooltip("Save flag that remembers it's open. Leave blank to auto-name it from this object.")]
    public string unlockFlag = "";

    [Header("What moves")]
    [Tooltip("The object that moves (the bridge). Defaults to this object if left empty.")]
    public Transform bridge;

    [Tooltip("CLOSED rotation, as a degrees offset from the placed (open) rotation. " +
             "e.g. X = -85 for an upright drawbridge that swings down flat.")]
    public Vector3 raisedEulerOffset = new Vector3(-85f, 0f, 0f);

    [Tooltip("CLOSED position, as an offset from the placed (open) position. " +
             "e.g. Y = 12 for a slab that drops in from above.")]
    public Vector3 raisedPositionOffset = Vector3.zero;

    [Header("Feel")]
    public float dropDuration = 2f;
    [Tooltip("Shown in chat when the shortcut opens. Leave blank for none.")]
    public string openMessage = "A path has opened back toward your base.";

    Vector3 _openPos, _closedPos;
    Quaternion _openRot, _closedRot;
    bool _open;        // currently resting in the open pose
    bool _animating;
    float _t;
    bool _subscribed;

    void Awake()
    {
        if (bridge == null) bridge = transform;
        if (string.IsNullOrEmpty(unlockFlag)) unlockFlag = "shortcut_" + gameObject.name;

        // The placed pose IS the open pose; the closed pose is that plus the offsets.
        _openPos = bridge.localPosition;
        _openRot = bridge.localRotation;
        _closedPos = _openPos + raisedPositionOffset;
        _closedRot = _openRot * Quaternion.Euler(raisedEulerOffset);

        // Start closed; Update snaps it open if it was already unlocked in a prior session.
        bridge.localPosition = _closedPos;
        bridge.localRotation = _closedRot;
    }

    void OnEnable() { TrySubscribe(); }
    void OnDisable()
    {
        if (boss != null) boss.OnDied -= HandleBossDied;
        _subscribed = false;
    }

    void TrySubscribe()
    {
        if (_subscribed || boss == null) return;
        boss.OnDied += HandleBossDied;
        _subscribed = true;
    }

    void Update()
    {
        TrySubscribe();   // boss may spawn a frame or two after this does

        // The save loads a couple of frames into the scene, so keep checking until the flag shows
        // up — then snap open instantly (it was opened in a previous session, no animation needed).
        if (!_open && !_animating)
        {
            var pe = PlayerEntity.Instance;
            if (pe != null && pe.HasFlag(unlockFlag)) SnapOpen();
        }

        if (_animating)
        {
            _t += Time.deltaTime / Mathf.Max(0.01f, dropDuration);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
            bridge.localPosition = Vector3.Lerp(_closedPos, _openPos, k);
            bridge.localRotation = Quaternion.Slerp(_closedRot, _openRot, k);
            if (_t >= 1f) { _animating = false; _open = true; }
        }
    }

    void HandleBossDied()
    {
        var pe = PlayerEntity.Instance;
        if (pe != null) pe.SetFlag(unlockFlag);   // remembered, and persisted by the autosave
        if (_open || _animating) return;
        _animating = true; _t = 0f;
        if (!string.IsNullOrEmpty(openMessage))
            HUDController.Emit("<color=#80FF80>" + openMessage + "</color>");
    }

    void SnapOpen()
    {
        bridge.localPosition = _openPos;
        bridge.localRotation = _openRot;
        _open = true;
        _animating = false;
    }
}
