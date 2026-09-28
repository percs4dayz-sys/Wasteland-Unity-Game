using UnityEngine;

/// <summary>
/// Tier progression gate — sits at the bridge chokepoints between concentric world rings.
/// Checks player combat level and kill count before allowing passage. Once cleared,
/// persists permanently via player flags.
///
/// Place at N/S/E/W bridge mouths where the terrain gulley forms the ring boundary.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class TierGate : MonoBehaviour
{
    [Header("Progression")]
    [Tooltip("Which tier this gate leads INTO (2-5). Tier 1 has no gate — it's the starting hub.")]
    [Range(2, 5)] public int targetTier = 2;

    [Header("Boss requirement — the ONLY way gates open")]
    [Tooltip("Drag this tier's boss here. Killing it ONCE opens the gate permanently, even if the " +
             "boss later respawns. This is the only thing that gates area progression — there are " +
             "no level or kill requirements anywhere. If you can kill it, you can pass.")]
    public CombatTarget requiredBoss;

    [Tooltip("Shown while the boss is still alive. Overrides Locked Message when a boss is set.")]
    public string bossAliveMessage = "The gate will not move. Something in this land still holds it shut.";

    [Tooltip("Shown the moment the boss dies and the gate releases.")]
    public string bossDefeatedMessage = "A shudder runs through the gate. Whatever held it is dead.";

    [Tooltip("Unique ID. Persists as flags 'tiergate_<id>' and 'bossdown_<id>'.")]
    public string gateId = "t2_north";

    [Header("Feedback")]
    [Tooltip("Message shown when the gate opens.")]
    public string unlockMessage = "The oppressive presence recedes. The path is clear.";

    [Header("Visual")]
    public GameObject fogVisual;   // assign the fog particle wall or glow barrier

    [Header("Sliding doors (optional)")]
    [Tooltip("Left door panel. Model the gate CLOSED — this is the closed pose.")]
    public Transform leftDoor;

    [Tooltip("Right door panel. Model the gate CLOSED — this is the closed pose.")]
    public Transform rightDoor;

    [Tooltip("How far each panel slides apart, in metres, along the gate's local X.")]
    public float doorSlideDistance = 6f;

    [Tooltip("Seconds the doors take to grind open.")]
    public float slideDuration = 3.5f;

    // closed poses captured at Awake; open poses derived from them
    Vector3 _leftClosed, _rightClosed, _leftOpen, _rightOpen;
    bool _sliding;
    float _slideT;

    BoxCollider _trigger;
    bool _opened;
    bool _subscribed;

    string Flag => "tiergate_" + gateId;

    /// <summary>Set the first time the boss dies. Survives the boss respawning, and the save.</summary>
    string BossFlag => "bossdown_" + gateId;

    /// <summary>Tier-level flag, set by ANY gate into this tier. The waypoint pads key off this.</summary>
    public string TierFlag => "tier_reached_" + targetTier;

    /// <summary>True once this tier's boss has been beaten — the first time is the only time.</summary>
    public bool BossDefeated =>
        requiredBoss == null || (PlayerEntity.Instance != null && PlayerEntity.Instance.HasFlag(BossFlag));

    void Awake()
    {
        _trigger = GetComponent<BoxCollider>();
        _trigger.isTrigger = true;

        // The authored pose IS the closed pose; open is that slid apart along local X.
        if (leftDoor != null)
        {
            _leftClosed = leftDoor.localPosition;
            _leftOpen = _leftClosed + Vector3.left * doorSlideDistance;
        }
        if (rightDoor != null)
        {
            _rightClosed = rightDoor.localPosition;
            _rightOpen = _rightClosed + Vector3.right * doorSlideDistance;
        }
    }

    void SlideDoors(float k)
    {
        if (leftDoor != null) leftDoor.localPosition = Vector3.Lerp(_leftClosed, _leftOpen, k);
        if (rightDoor != null) rightDoor.localPosition = Vector3.Lerp(_rightClosed, _rightOpen, k);
    }

    void Start()
    {
        if (PlayerEntity.Instance != null && PlayerEntity.Instance.HasFlag(Flag))
        {
            // Saves written before the waypoint network existed only have the per-gate flag.
            PlayerEntity.Instance.SetFlag(TierFlag);
            OpenInstant();
        }
        TrySubscribe();
    }

    void Update()
    {
        // The boss may spawn a frame or two after this does, so keep trying until it exists.
        if (!_subscribed) TrySubscribe();

        if (_sliding)
        {
            _slideT += Time.deltaTime / Mathf.Max(0.01f, slideDuration);
            SlideDoors(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_slideT)));
            if (_slideT >= 1f) _sliding = false;
        }
    }

    void OnDisable()
    {
        if (requiredBoss != null) requiredBoss.OnDied -= HandleBossDied;
        _subscribed = false;
    }

    void TrySubscribe()
    {
        if (_subscribed || requiredBoss == null) return;
        requiredBoss.OnDied += HandleBossDied;
        _subscribed = true;
    }

    /// <summary>
    /// Boss died. Record it permanently — the gate is now passable whether or not the player
    /// is standing here, and whether or not the boss respawns later.
    /// </summary>
    void HandleBossDied()
    {
        var pe = PlayerEntity.Instance;
        if (pe == null) return;
        if (pe.HasFlag(BossFlag)) return;          // already banked; only the first kill counts

        pe.SetFlag(BossFlag);
        if (!string.IsNullOrEmpty(bossDefeatedMessage))
            HUDController.Emit($"<color=#FFD060>[TIER {targetTier} GATE]</color> {bossDefeatedMessage}");
    }

    void OnTriggerEnter(Collider other)
    {
        if (_opened) return;
        var player = other.GetComponentInParent<PlayerEntity>();
        if (player == null) return;
        TryOpen(player);
    }

    /// <summary>
    /// The boss is the ONLY lock. Beat it once and the gate is yours for good. There is deliberately
    /// no level requirement and no kill count — if you are good enough to kill the boss, you are
    /// good enough to move on. (Gear has its own skill requirements; that is a separate system and
    /// never gates an area.)
    /// </summary>
    public void TryOpen(PlayerEntity player)
    {
        if (_opened) return;

        if (requiredBoss == null)
        {
            // No boss wired up. Never trap a player behind a scene-setup mistake — open it and warn
            // the developer instead, so a misconfigured gate is a bug report and not a dead end.
            Debug.LogWarning($"[TierGate] '{gateId}' (tier {targetTier}) has no Required Boss assigned. " +
                             "Opening it so nobody gets stuck — assign this tier's boss to make it a real gate.");
            Open(player);
            return;
        }

        if (BossDefeated) { Open(player); return; }

        HUDController.Emit(
            $"<color=#FF6644>[TIER {targetTier} GATE]</color> {bossAliveMessage}\n" +
            $"  • Defeat {BossName()} to open this gate");
    }

    /// <summary>CombatTarget.DisplayName is just "Hostile", so use the object's name — you'll
    /// have named the boss GameObject something meaningful.</summary>
    string BossName() => requiredBoss == null ? "this tier's boss" : requiredBoss.gameObject.name;

    void Open(PlayerEntity player)
    {
        _opened = true;
        player.SetFlag(Flag);
        player.SetFlag(TierFlag);   // unlocks this tier's pad on the waypoint island
        HUDController.Emit($"<color=#44FF66>[TIER {targetTier} GATE]</color> {unlockMessage}");

        if (fogVisual != null)
            fogVisual.SetActive(false);

        // grind the doors apart, if any are wired up
        if (leftDoor != null || rightDoor != null) { _sliding = true; _slideT = 0f; }

        _trigger.enabled = false;
    }

    /// <summary>Already open from a previous session — snap, don't animate.</summary>
    void OpenInstant()
    {
        _opened = true;
        if (fogVisual != null) fogVisual.SetActive(false);
        if (_trigger != null) _trigger.enabled = false;
        SlideDoors(1f);
    }

}
