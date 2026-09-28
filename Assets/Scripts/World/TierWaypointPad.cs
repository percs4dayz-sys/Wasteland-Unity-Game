using UnityEngine;

/// <summary>
/// One teleport pad of the tier shortcut network.
///
/// The hub sits on the small detached island west of Tutorial Island. It holds one pad per
/// tier; each pad stays locked until the player has actually opened that tier's TierGate,
/// so the shortcut can never skip you past content you haven't earned. Pads on Tutorial
/// Island and in the Greenbelt point back at the hub, so once the network is unlocked you
/// can hop between any tiers you've already reached instead of walking the whole ring.
///
/// Unlock chain:
///   • TierGate opens          → sets "tier_reached_&lt;N&gt;"
///   • that flag               → unlocks the hub pad for tier N
///   • "tier_reached_2"        → unlocks the hub itself (the pads that travel TO the island)
///
/// Everything persists through PlayerEntity's flag set, so it survives a reload.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TierWaypointPad : MonoBehaviour
{
    [Header("Where it goes")]
    [Tooltip("Destination marker. The player is placed here, keeping their facing.")]
    public Transform destination;

    [Tooltip("Shown in chat on arrival, e.g. 'Frozen Wastes'.")]
    public string destinationName = "Greenbelt";

    [Header("Lock")]
    [Tooltip("Flag required to use this pad. Blank = always open. " +
             "Tier pads use 'tier_reached_<N>'; travel-to-hub pads use 'tier_reached_2'.")]
    public string requiredFlag = "";

    [Tooltip("Message when the pad is still locked.")]
    public string lockedMessage = "The pad is dark. You have not yet set foot in that region.";

    [Header("Feel")]
    public float retriggerCooldown = 2.5f;

    [Tooltip("Optional glow/visual that is only shown once the pad is usable.")]
    public GameObject unlockedVisual;

    [Tooltip("Optional visual shown while the pad is still locked.")]
    public GameObject lockedVisual;

    float _nextAt;
    bool  _lastUnlocked;

    public bool IsUnlocked
    {
        get
        {
            if (string.IsNullOrEmpty(requiredFlag)) return true;
            var pe = PlayerEntity.Instance;
            return pe != null && pe.HasFlag(requiredFlag);
        }
    }

    void Start() { RefreshVisuals(); }

    void Update()
    {
        // The save loads a couple of frames in, so keep the visuals in step with the flags.
        if (IsUnlocked != _lastUnlocked) RefreshVisuals();
    }

    void RefreshVisuals()
    {
        _lastUnlocked = IsUnlocked;
        if (unlockedVisual != null) unlockedVisual.SetActive(_lastUnlocked);
        if (lockedVisual   != null) lockedVisual.SetActive(!_lastUnlocked);
    }

    void OnTriggerEnter(Collider other)
    {
        if (Time.time < _nextAt) return;
        if (other.GetComponentInParent<PlayerEntity>() == null) return;
        _nextAt = Time.time + retriggerCooldown;
        Use();
    }

    public void Use()
    {
        if (!IsUnlocked)
        {
            HUDController.Emit($"<color=#FF6644>[WAYPOINT]</color> {lockedMessage}");
            return;
        }
        if (destination == null)
        {
            Debug.LogWarning($"[TierWaypointPad] {name} has no destination assigned.", this);
            return;
        }

        var player = Object.FindAnyObjectByType<Player3DController>();
        if (player == null) return;

        Teleport(player.gameObject, destination.position);
        HUDController.Emit($"<color=#66DDFF>[WAYPOINT]</color> {destinationName}.");
    }

    /// <summary>Move the player without fighting the CharacterController or its pathing.</summary>
    public static void Teleport(GameObject player, Vector3 to)
    {
        var cc = player.GetComponent<CharacterController>();
        bool had = cc != null && cc.enabled;
        if (had) cc.enabled = false;          // CharacterController overrides transform writes

        player.transform.position = to;

        var rb = player.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic) rb.linearVelocity = Vector3.zero;

        // drop any click-to-move order so the player doesn't immediately walk back
        var pc = player.GetComponent<Player3DController>();
        if (pc != null) pc.ClearDestination();

        if (had) cc.enabled = true;
    }
}
