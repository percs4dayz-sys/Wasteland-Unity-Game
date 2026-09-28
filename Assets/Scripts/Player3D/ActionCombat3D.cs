using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Real-time click-to-swing combat — melee AND ranged work identically: click an enemy
/// (or press F / controller X for the nearest one) and the attack fires immediately,
/// gated only by your weapon's cooldown in real seconds (attackSpeed ticks × 0.6 s, so
/// weapon speeds keep their OSRS-derived pacing). Click out of reach and you walk into
/// range and the swing fires on arrival. No game-tick alignment — that now belongs to
/// skilling only. Works the same in third and first person (in first person the player
/// faces the camera, so your target is right where you're looking).
///
/// Controls:
///   Attack — click an enemy (ClickToMove3D feeds the click here) / F key / X button
///   Cancel — click the ground or press Escape
/// </summary>
[RequireComponent(typeof(Player3DController))]
public class ActionCombat3D : MonoBehaviour
{
    [Header("Range (world meters)")]
    public float meleeReach = 1.9f;   // reach at 1 tile (touching). Each extra tile adds tileMeters.
    public float tileMeters = 2f;     // how much standoff each extra tile of weapon range buys

    [Header("Engage")]
    public KeyCode engageKey = KeyCode.F;
    public float acquireRange = 14f;  // how far F / X can pick a target (then you walk to it)
    public float leashRange = 16f;    // further than this and a queued swing / combat focus drops

    [Header("First-person shooting (hitscan)")]
    [Tooltip("Effective reach of a plain firearm/gauntlet, in metres, before the weapon's own range " +
             "bonus is added. This is how far the crosshair raycast reaches.")]
    public float rangedBaseMeters = 40f;
    [Tooltip("Extra metres of reach per point of the weapon's attackRange above 1.")]
    public float rangedMetersPerTile = 8f;

    [Header("Fission special — Overload Cascade (power gauntlets only)")]
    [Tooltip("Fires the four-hit gauntlet special. OSRS dragon-claws style.")]
    public KeyCode specialKey = KeyCode.G;
    [Tooltip("Special-attack energy this costs, out of 100. 50 means two banked specials at most, " +
             "matching dragon claws. Energy refills over time (CombatManager.specialRegenPerSecond) " +
             "rather than running on a cooldown, so you can save a spec for when it matters.")]
    public float specialEnergyCost = 50f;

    /// <summary>True while holding a ranged weapon on a target — drives the firing-stance animation.</summary>
    public bool IsAiming => Target != null && IsRangedEquipped();

    /// <summary>The enemy you're currently fighting (last one you swung at / queued a swing on).
    /// Read by CompanionManager so your pet assists on it.</summary>
    public CombatTarget Target { get; private set; }

    /// <summary>Fires each time a swing/shot resolves (true = ranged). Drives the player animation.</summary>
    public event System.Action<bool> OnAttackPerformed;

    Player3DController _pc;
    PlayerEntity _pe;
    WeaponMagazine _mag;
    float _readyAt;            // Time.time when the next swing may fire
    CombatTarget _queued;      // enemy the next swing lands on (walking into reach / waiting out cooldown)
    bool _autoFollowing;

    // Milestone-passive state: Bladework L99 first strike, Marksmanship L99 consecutive-hit ramp.
    bool _pendingFirstStrike;
    CombatTarget _streakTarget;
    int _streak;

    void Awake()
    {
        _pc = GetComponent<Player3DController>();
        _pe = GetComponent<PlayerEntity>();
        // The shipped MainWorld3D scene was built before click-to-move existed.
        if (GetComponent<ClickToMove3D>() == null) gameObject.AddComponent<ClickToMove3D>();
        // Magazine/reload layer for ranged weapons — self-added, zero scene setup.
        _mag = GetComponent<WeaponMagazine>();
        if (_mag == null) _mag = gameObject.AddComponent<WeaponMagazine>();
        // First-person viewmodel arms (inert until the FP Arms prefab is baked).
        if (GetComponent<FirstPersonArms>() == null) gameObject.AddComponent<FirstPersonArms>();
        // Floating name + combat-level label over the player's head.
        if (GetComponent<OverheadNameplate>() == null) gameObject.AddComponent<OverheadNameplate>();
    }

    void Update()
    {
        if (_pe == null || _pe.Stats == null) return;
        if (ChatInput.IsTyping) return;
        if (ControllerUI.MenuFocused) return;   // controller is navigating a menu — X/RT shouldn't attack

        // ── First-person shooter: point the crosshair, pull the trigger ──
        // Left mouse (held) or the right trigger fires a hitscan shot straight down the camera's
        // forward. You damage whatever the reticle is on — no target lock. Everything is gated by
        // the weapon's cooldown, so fire rate is still the gun's stat. In third person the old
        // click-to-engage / twin-stick paths take over instead.
        bool fps = OrbitCamera3D.FirstPersonActive;
        if (fps)
        {
            if (FireHeld() && !PointerOverUI())
                FireCrosshair();
        }
        else
        {
            if (Input.GetKeyDown(engageKey) || Input.GetKeyDown(KeyCode.JoystickButton2)) // X button
                AttackNearest();

            // Right trigger (pad): attack toward wherever you're facing — the twin-stick companion to
            // the right-stick aim. Hold it to keep attacking on the weapon's cooldown.
            if (RightTriggerHeld())
                AttackInPlace(transform.position + transform.forward * 2f);
        }

        if (Input.GetKeyDown(specialKey) || (!Input.GetKey(KeyCode.JoystickButton4) && Input.GetKeyDown(KeyCode.JoystickButton3))) // Y button
            TrySpecial();

        if (Input.GetKeyDown(KeyCode.Escape))
            Disengage("You break off the attack.");

        // Combat focus fades when the fight is over or you've left it behind.
        if (Target != null && (Target.IsDead || TooFar(Target)))
            Target = null;

        TickQueuedSwing();
    }

    // ── attack requests ──────────────────────────────────────────────────
    /// <summary>Attack this enemy: swing as soon as you're in reach and off cooldown (walking into
    /// range first if needed). Called by ClickToMove3D for every enemy click. (Keeps its old name so
    /// existing callers don't change.)</summary>
    public void Engage(CombatTarget t)
    {
        if (t == null || t.IsDead) return;
        bool isNew = t != Target && t != _queued;
        _queued = t;
        if (CombatFeedbackUI.Instance != null) CombatFeedbackUI.Instance.FocusTarget = t;
        if (isNew)
        {
            _pendingFirstStrike = true;   // arm the Bladework L99 opening hit
            _streakTarget = null; _streak = 0;
            Msg($"You attack the {t.DisplayName}.");
        }
    }

    public void Disengage(string message = null)
    {
        bool hadFight = Target != null || _queued != null;
        Target = null;
        _queued = null;
        _streakTarget = null; _streak = 0; _pendingFirstStrike = false;
        if (_autoFollowing) { _pc.ClearDestination(); _autoFollowing = false; }
        if (hadFight && !string.IsNullOrEmpty(message)) Msg(message);
    }

    /// <summary>Eating mid-fight delays your next attack by up to <paramref name="ticks"/> ticks'
    /// worth of seconds. No effect if you aren't fighting, or if your weapon is already off
    /// cooldown (a ready weapon takes no added delay).</summary>
    public void DelayAttackAfterEating(int ticks)
    {
        if (Target == null && _queued == null) return;
        if (_readyAt <= Time.time) return;        // weapon ready → eating adds no delay
        _readyAt = Mathf.Max(_readyAt, Time.time + ModuleEffects.For(_pe).EatDelay(ticks) * GameTick.TICK_DURATION);
    }

    /// <summary>PoE-style attack-in-place (hold Shift + click): plant your feet, face the cursor,
    /// and swing/fire that way — landing on the nearest enemy within reach in that arc, or whiffing
    /// cleanly (animation + cooldown, no damage) if nothing's there. Never moves the player.</summary>
    public void AttackInPlace(Vector3 aimPoint)
    {
        if (_pe == null || _pe.Stats == null) return;

        _queued = null;                                  // stand your ground — cancel any walk-to-attack
        SkillingManager.Instance?.StopGathering();       // swinging at something ends the gather
        if (_autoFollowing) { _pc.ClearDestination(); _autoFollowing = false; }
        FaceToward(aimPoint);

        if (Time.time < _readyAt) return;

        bool ranged = IsRangedEquipped();
        if (ranged)
        {
            if (_pe.Equipment.GetItemId("Ammo") == null || _pe.Equipment.AmmoQuantity <= 0)
            {
                Msg("Out of ammo — load rounds into your Ammo slot.");
                return;
            }
            if (_mag != null)
            {
                if (_mag.IsReloading) return;
                if (_mag.Loaded <= 0) { _mag.TryStartReload(auto: true); return; }
            }
        }

        var target = BestTargetInArc(aimPoint, ReachMeters());
        if (target != null)
        {
            Target = target;
            if (CombatFeedbackUI.Instance != null) CombatFeedbackUI.Instance.FocusTarget = target;
            ResolveHit(target, ranged);
        }
        else
        {
            OnAttackPerformed?.Invoke(ranged);           // clean whiff — swing the weapon anyway
        }
        if (ranged && _mag != null) _mag.NoteShotFired();
        _readyAt = Time.time + CurrentSpeedTicks() * GameTick.TICK_DURATION * ModuleEffects.For(_pe).CooldownMultiplier();
    }

    /// <summary>Nearest living enemy within <paramref name="range"/> metres inside a ~100° arc
    /// toward the aim point — a swing, not a snipe, so facing roughly at the enemy is enough.</summary>
    CombatTarget BestTargetInArc(Vector3 aimPoint, float range)
    {
        Vector3 dir = aimPoint - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        dir.Normalize();

        CombatTarget best = null;
        float bestDist = range * range;
        foreach (var t in Object.FindObjectsByType<CombatTarget>(FindObjectsInactive.Exclude))
        {
            if (t.IsDead) continue;   // dummies stay valid — shift-swinging at the training dummy works
            Vector3 to = t.transform.position - transform.position; to.y = 0f;
            float d = to.sqrMagnitude;
            if (d > bestDist) continue;
            if (d > 0.01f && Vector3.Dot(dir, to.normalized) < 0.64f) continue;   // ≈ ±50° arc
            bestDist = d; best = t;
        }
        return best;
    }

    /// <summary>Swing at the nearest living enemy within acquire range (F key / X button).</summary>
    void AttackNearest()
    {
        CombatTarget best = null;
        float bestDist = acquireRange * acquireRange;
        foreach (var t in Object.FindObjectsByType<CombatTarget>(FindObjectsInactive.Exclude))
        {
            if (t.IsDead) continue;
            float d = (t.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = t; }
        }

        if (best == null) { Msg("Nothing to attack nearby."); return; }
        Engage(best);
    }

    // ── first-person hitscan fire ────────────────────────────────────────
    /// <summary>True while the fire control is held: left mouse OR the right trigger. Held-fire is
    /// gated by the weapon cooldown in <see cref="FireCrosshair"/>, so this can return true every
    /// frame without machine-gunning a slow weapon.</summary>
    static bool FireHeld() => Input.GetMouseButton(0) || RightTriggerHeld();

    static bool PointerOverUI() =>
        UnityEngine.EventSystems.EventSystem.current != null &&
        UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    /// <summary>How far the crosshair ray reaches: the weapon's range (in tiles) mapped to metres.</summary>
    float RangedReachMeters()
    {
        var w = _pe.Equipment.GetItem("Weapon");
        int tiles = (w != null && w.attackRange > 0) ? w.attackRange : 1;
        return rangedBaseMeters + Mathf.Max(0, tiles - 1) * rangedMetersPerTile;
    }

    /// <summary>Nearest living enemy the centre ray passes through, within range. Non-enemy colliders
    /// are skipped rather than blocking, so a bit of foliage in front of an enemy never eats your
    /// shot. (True line-of-sight occlusion can be layered on later if shooting through thin walls
    /// becomes a problem.)</summary>
    CombatTarget RaycastTarget(Ray ray, float range)
    {
        var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;   // our own capsule / held model
            var ct = h.collider.GetComponentInParent<CombatTarget>();
            if (ct != null && !ct.IsDead) return ct;
        }
        return null;
    }

    /// <summary>
    /// The FPS trigger pull. Raycasts from the camera straight ahead; if the reticle is on a living
    /// enemy within weapon range it lands a guaranteed hit (aim IS the accuracy — see ResolveHit's
    /// hitscan path), otherwise it fires a clean miss. Ammo, magazine/reload and the weapon cooldown
    /// all behave exactly as the click combat, so guns and gauntlets keep every stat that makes them
    /// differ. Bare-handed / melee weapons simply can't fire.
    /// </summary>
    void FireCrosshair()
    {
        if (_pe == null || _pe.Stats == null) return;
        if (Time.time < _readyAt) return;                 // still on the weapon's cooldown

        bool ranged = IsRangedEquipped();
        if (!ranged) { return; }                          // no melee weapons in the shooter build

        // Ammo + magazine gates, same as AttackInPlace.
        if (_pe.Equipment.GetItemId("Ammo") == null || _pe.Equipment.AmmoQuantity <= 0)
        {
            Msg("Out of ammo — load rounds into your Ammo slot.");
            return;
        }
        if (_mag != null)
        {
            if (_mag.IsReloading) return;
            if (_mag.Loaded <= 0) { _mag.TryStartReload(auto: true); return; }
        }

        var cam = Camera.main;
        if (cam == null) return;

        bool fission = IsFissionEquipped();
        var target = RaycastTarget(new Ray(cam.transform.position, cam.transform.forward), RangedReachMeters());
        if (target != null)
        {
            Target = target;
            if (CombatFeedbackUI.Instance != null) CombatFeedbackUI.Instance.FocusTarget = target;
            ResolveHit(target, ranged, hitscan: true);    // ResolveHit spends the round on a hit
        }
        else
        {
            OnAttackPerformed?.Invoke(ranged);            // muzzle flash / shot anim on a clean miss
            ConsumeShot(fission);                         // a pulled trigger still spends the round
        }

        if (_mag != null) _mag.NoteShotFired();
        _readyAt = Time.time + CurrentSpeedTicks() * GameTick.TICK_DURATION * ModuleEffects.For(_pe).CooldownMultiplier();
    }

    // ── the swing, resolved in real time ─────────────────────────────────
    void TickQueuedSwing()
    {
        if (_queued == null)
        {
            if (_autoFollowing) { _pc.ClearDestination(); _autoFollowing = false; }
            return;
        }
        if (_queued.IsDead) { _queued = null; return; }

        bool ranged = IsRangedEquipped();
        float range = ReachMeters();

        Vector3 to = _queued.transform.position - transform.position; to.y = 0f;
        float dist = to.magnitude;

        if (dist > leashRange) { Disengage("You break off the attack."); return; }

        if (dist > range)
        {
            // Out of reach: walk in (WASD/stick clears the destination — that cancels the errand).
            if (_autoFollowing && !_pc.HasDestination) { _queued = null; _autoFollowing = false; return; }
            _pc.SetDestination(_queued.transform.position, range * 0.9f);
            _autoFollowing = true;
            return;
        }
        if (_autoFollowing) { _pc.ClearDestination(); _autoFollowing = false; }

        if (Time.time < _readyAt) return;   // clicked mid-cooldown — the swing fires the moment it's up

        if (ranged)
        {
            if (_pe.Equipment.GetItemId("Ammo") == null || _pe.Equipment.AmmoQuantity <= 0)
            {
                Msg("Out of ammo — load rounds into your Ammo slot.");
                _queued = null;
                return;
            }
            if (_mag != null)
            {
                if (_mag.IsReloading) return;                       // queued shot fires when it's done
                if (_mag.Loaded <= 0) { _mag.TryStartReload(auto: true); return; }
            }
        }

        FaceToward(_queued.transform.position);
        var target = _queued;
        _queued = null;
        Target = target;                    // combat focus: pet assist, aiming anim, feedback UI
        ResolveHit(target, ranged);
        if (ranged && _mag != null) _mag.NoteShotFired();   // every trigger pull spends a mag round
        _readyAt = Time.time + CurrentSpeedTicks() * GameTick.TICK_DURATION * ModuleEffects.For(_pe).CooldownMultiplier();
    }

    /// <summary>Right trigger past half-pull. Zero (never held) if the "RightTrigger" axis isn't
    /// defined in the Input Manager — this project defines it.</summary>
    static bool RightTriggerHeld()
    {
        try { return Input.GetAxisRaw("RightTrigger") > 0.5f; }
        catch (System.ArgumentException) { return false; }
    }

    bool TooFar(CombatTarget t)
    {
        Vector3 to = t.transform.position - transform.position; to.y = 0f;
        return to.magnitude > leashRange;
    }

    /// <summary>Attack range in meters: weapon distance (+Longrange stance), scaled to the 3D world.</summary>
    float ReachMeters()
    {
        var weapon = _pe.Equipment.GetItem("Weapon");
        int tiles = (weapon != null && weapon.attackRange > 0) ? weapon.attackRange : 1;
        return meleeReach + Mathf.Max(0, tiles - 1) * tileMeters;
    }

    static CombatStance CurrentStance() =>
        CombatManager.Instance != null ? CombatManager.Instance.Stance : CombatStance.Accurate;

    /// <summary>Weapon cadence in ticks — kept as the balance unit (× TICK_DURATION for seconds).</summary>
    int CurrentSpeedTicks()
    {
        var stance = CurrentStance();
        var weapon = _pe.Equipment.GetItem("Weapon");
        int t = (weapon != null && weapon.attackSpeed > 0) ? weapon.attackSpeed : 3;
        if (stance == CombatStance.Rapid) t = Mathf.Max(2, t - 1);        // faster swings
        if (stance == CombatStance.Aggressive) t += 1;                    // slower, harder hits
        return t;
    }

    /// <summary>Style of the equipped weapon; bare hands count as melee.</summary>
    WeaponStyle EquippedStyle()
    {
        var w = _pe.Equipment.GetItem("Weapon");
        return w != null ? w.weaponStyle : WeaponStyle.Melee;
    }

    /// <summary>
    /// True for anything fired from a distance and fed by the Ammo slot — guns AND power gauntlets.
    /// Callers use this for aiming, the shoot animation and the out-of-ammo gate, all of which apply
    /// equally to both. ResolveHit separates the two for the actual accuracy/damage/XP math.
    /// </summary>
    bool IsRangedEquipped()
    {
        var s = EquippedStyle();
        return s == WeaponStyle.Ranged || s == WeaponStyle.Fission;
    }

    /// <summary>Power gauntlets specifically — the Fission style.</summary>
    bool IsFissionEquipped() => EquippedStyle() == WeaponStyle.Fission;

    /// <summary>Same accuracy/damage/XP math as the tick combat — power comes from levels & gear,
    /// plus the SkillGuide milestone passives (see CombatEffects). When <paramref name="hitscan"/> is
    /// true (the first-person crosshair path) the accuracy roll is SKIPPED: your aim already decided
    /// the hit, so landing the ray on the enemy always deals damage. Level and gear then decide how
    /// MUCH, exactly as before — so which gun you carry and which core you loaded still matter.</summary>
    void ResolveHit(CombatTarget target, bool ranged, bool hitscan = false)
    {
        OnAttackPerformed?.Invoke(ranged);   // play the swing/shot animation
        if (CombatFeedbackUI.Instance != null) CombatFeedbackUI.Instance.FocusTarget = target;

        var stance = CurrentStance();
        bool fission  = IsFissionEquipped();   // gauntlets: a sub-case of `ranged`
        int melee     = _pe.Stats.GetLevel(Skill.Attack);      // accuracy
        int brutality = _pe.Stats.GetLevel(Skill.Strength);    // raw power
        int hardening = _pe.Stats.GetLevel(Skill.Defence);     // defence
        int marks     = _pe.Stats.GetLevel(Skill.Marksmanship);
        int fissionLv = _pe.Stats.GetLevel(Skill.Fission);

        var style = fission ? WeaponStyle.Fission
                  : ranged  ? WeaponStyle.Ranged
                            : WeaponStyle.Melee;

        // Accuracy: the style's own stat + weapon attack bonus + stance option. For Fission this is
        // the ENTIRE contribution of the skill — level buys accuracy and nothing else.
        int atkLvl  = fission ? fissionLv : ranged ? marks : melee;
        // Style-aware: ranged armour's accuracy only helps shooting, melee plate only helps swinging.
        int equipAcc = _pe.Equipment.TotalAttackBonusFor(style) + ModuleEffects.For(_pe).Accuracy(target, style);
        int atkRoll = CombatMath.AttackRoll(atkLvl, equipAcc, AccuracyStance(stance));

        // Melee L60: chance to bypass the target's defence entirely (melee only).
        bool bypass = !ranged && CombatEffects.ArmorBypass(melee);
        int defRoll = bypass ? 0 : CombatMath.DefenceRoll(ModuleEffects.For(_pe).TargetDefence(target), 0, 0);

        // Hitscan (FPS crosshair): landing the ray IS the hit — no accuracy roll. Otherwise roll it.
        bool hit = hitscan || bypass || Random.value <= CombatMath.HitChance(atkRoll, defRoll);
        if (!hit)
        {
            ModuleEffects.For(_pe).Miss(target);
            if (ranged) ConsumeShot(fission);
            CombatFeedbackUI.ShowWorldSplat(target.transform.position, 0, false);
            _streakTarget = null; _streak = 0;   // a miss breaks the ranged consecutive-hit ramp
            return;
        }

        int maxHit;
        if (fission)
        {
            // Fission does NOT use the strength formula at all. The loaded core's rating IS the max
            // hit, flat — no level term, no gear term. That is what makes a tier-1 core devastating
            // at low level and keeps the whole power curve in the ammo you had to grind for.
            maxHit = _pe.Equipment.AmmoStrengthBonus();
            if (maxHit <= 0) maxHit = 1;   // an un-rated core still does something
        }
        else
        {
            // OSRS-style max hit: Brutality level (raw strength) + weapon's strength bonus + stance.
            int strLvl   = ranged ? marks : brutality;
            int strBonus = _pe.Equipment.TotalStrengthBonus();
            if (ranged) strBonus += _pe.Equipment.AmmoStrengthBonus();   // OSRS: ammo supplies ranged strength
            maxHit = CombatMath.MaxHit(strLvl, strBonus, StrengthStance(stance));
        }

        // Full Ascendant set: the whole point of the drop chase. Style-matched only — there is no
        // Fission armour line yet, so a gauntlet build simply gets no set bonus.
        float setMul = GearSets.DamageMultiplier(_pe.Equipment, style);
        if (setMul > 1f) maxHit = Mathf.RoundToInt(maxHit * setMul);

        float dmgF   = Mathf.Max(1, CombatMath.RollDamage(maxHit));

        // ── milestone damage multipliers ──
        string tag = null;
        // Headshot / consecutive-hit / companion perks are MARKSMANSHIP milestones — they key off
        // the marks level, so they must not fire for gauntlets even though those are "ranged".
        if (ranged && !fission)
        {
            // Razorbeak companion perk: the bird spots targets — passive ranged damage multiplier.
            if (CompanionManager.Instance != null)
                dmgF *= CompanionManager.Instance.MarksmanshipDamageMult;
            // Marksmanship L40 headshot.
            if (CombatEffects.Headshot(marks)) { dmgF *= CombatEffects.HeadshotMult; tag = "HEADSHOT"; }
            // Marksmanship L99 consecutive-hit ramp on the same target.
            if (_streakTarget == target) _streak++; else { _streakTarget = target; _streak = 0; }
            dmgF *= CombatEffects.ConsecutiveMultiplier(marks, _streak);
        }
        else if (!fission)
        {
            // MELEE only — these are Brutality milestones. Fission gets neither branch: its power
            // is the core, and it has no milestone passives of its own yet.
            // Brutality L99 opening strike.
            float fs = CombatEffects.FirstStrikeMultiplier(brutality, _pendingFirstStrike);
            if (fs > 1f) tag = "FIRST STRIKE";
            dmgF *= fs;
            // Brutality L20 power attack.
            if (CombatEffects.PowerAttack(brutality)) { dmgF *= CombatEffects.PowerAttackMult; tag ??= "POWER"; }
            // Brutality L99 boss bonus.
            dmgF *= CombatEffects.BossMultiplier(brutality, target.isBoss);
            // Hardening damage reduction: higher Hardening = less damage taken (applied as self-buff,
            // not on the enemy — the player's defence mitigates incoming damage elsewhere).
        }
        else
        {
            // FISSION: Meltdown. Flat chance to deal DOUBLE the loaded core's max hit outright —
            // not a multiplier on the roll, a straight 2x max. It is the only thing Fission levels
            // give besides accuracy, so unlike the other styles' passives it is live from level 1.
            if (CombatEffects.Meltdown(fissionLv))
            {
                dmgF = maxHit * CombatEffects.MeltdownMultiplier;
                tag  = "MELTDOWN";
            }
        }
        _pendingFirstStrike = false;

        int dmg = ModuleEffects.For(_pe).Outgoing(target, Mathf.Max(1, Mathf.RoundToInt(dmgF)), style);
        dmg = Mathf.Min(dmg, target.CurrentHP);

        if (ranged) ConsumeShot(fission);
        target.TakeDamage(dmg);               // hitsplat comes via the feedback UI's OnDamaged hook
        AwardXP(dmg, ranged, fission, stance);

        // Brutality L40 stagger: delay the enemy's next swing.
        if (!ranged && CombatEffects.Stagger(brutality))
            target.GetComponent<Enemy3D>()?.Stagger(CombatEffects.StaggerSeconds);

        // Brutality L80 cleave: splash to nearby enemies.
        if (!ranged && CombatEffects.Cleave(brutality))
            CleaveNearby(target, Mathf.Max(1, Mathf.RoundToInt(dmg * CombatEffects.CleaveFraction)));

        if (tag != null) Msg($"<color=#FF9030>{tag}!</color>");
        if (target.IsDead) Msg($"{target.DisplayName} is defeated.");
    }

    // ── Fission special: Overload Cascade ────────────────────────────────────
    // Four blasts in a single action, built like OSRS dragon claws: the accuracy roll walks DOWN the
    // four hits, and the first one that connects sets the damage from a high-floor band, with each
    // later hit halving. Front-loaded, so a lucky first roll is the big one and a late one fizzles.
    //
    // Its total damage is deliberately close to four ordinary shots — the special buys BURST and
    // accuracy, not damage-per-core. Special ENERGY is what stops it simply replacing the basic
    // attack, since it costs the same four cores either way.
    const int   SpecialHits         = 4;
    const float SpecialAccuracyMult = 1.5f;   // "high accuracy" is the spec's whole identity

    /// <summary>True when there is enough energy banked to overload. Read by the UI button.</summary>
    public bool SpecialReady =>
        CombatManager.Instance != null && CombatManager.Instance.SpecialEnergy + 0.001f >= specialEnergyCost;

    public bool HasWeaponSpecial => _pe != null && IsFissionEquipped();
    public string SpecialAttackName => "Overload Cascade";
    public string SpecialBlockReason
    {
        get
        {
            if (_pe == null || _pe.Stats == null || _pe.Stats.IsDead) return "Unavailable while down";
            if (!HasWeaponSpecial) return "Equip Power Gauntlets";
            if (!SpecialReady) return "Recharging";
            var ammo = _pe.Equipment.GetItem("Ammo");
            if (ammo == null || ammo.id < FissionItems.RefinedCore(1) || ammo.id > FissionItems.RefinedCore(5) || _pe.Equipment.AmmoQuantity < SpecialHits)
                return "Load 4 fission cores";
            if (Target == null || Target.IsDead) return "Select an enemy";
            if (TooFar(Target)) return "Move closer to target";
            return null;
        }
    }

    /// <summary>Fire the special. Public so the COMBAT panel's button can call it, not just the key.</summary>
    public void TrySpecial()
    {
        string blocked = SpecialBlockReason;
        if (blocked != null) { Msg(blocked + "."); return; }
        if (!IsFissionEquipped()) { Msg("Only the Power Gauntlets can overload."); return; }

        var cm = CombatManager.Instance;
        if (cm == null) return;
        if (cm.SpecialEnergy + 0.001f < specialEnergyCost)
        {
            Msg($"Not enough special energy ({cm.SpecialEnergy:F0}% / {specialEnergyCost:F0}%).");
            return;
        }

        var target = Target;
        if (target == null || target.IsDead) { Msg("No target — engage something first."); return; }
        if (TooFar(target)) { Msg("Too far away to overload."); return; }

        if (_pe.Equipment.AmmoQuantity < SpecialHits)
        {
            Msg($"Overload needs {SpecialHits} cores loaded — you have {_pe.Equipment.AmmoQuantity}.");
            return;
        }

        int maxHit = _pe.Equipment.AmmoStrengthBonus();
        if (maxHit <= 0) maxHit = 1;

        // Energy goes FIRST, before any cores are burned. Every reason to bail — no target, out of
        // range, too few cores — has already been checked above, so this is the last gate; taking it
        // before the cores means a failure here can never leave you paying cores for nothing.
        if (!cm.TrySpendSpecial(specialEnergyCost)) return;

        // Cores are spent UP FRONT. Unlike an ordinary shot (where a miss costs nothing) this is a
        // committed action, so a bad cascade still burns the cores. The gauntlets' save chance
        // applies to each core individually.
        var gauntlets = _pe.Equipment.GetItem("Weapon");
        float save = gauntlets != null ? gauntlets.ammoSaveChance : 0f;
        int recovered = 0;
        for (int i = 0; i < SpecialHits; i++)
        {
            if (save > 0f && Random.value < save) { recovered++; continue; }
            _pe.Equipment.ConsumeAmmo(1);
        }

        FaceToward(target.transform.position);
        OnAttackPerformed?.Invoke(true);
        if (CombatFeedbackUI.Instance != null) CombatFeedbackUI.Instance.FocusTarget = target;

        var stance   = CurrentStance();
        int fissionLv = _pe.Stats.GetLevel(Skill.Fission);
        int equipAcc  = _pe.Equipment.TotalAttackBonusFor(WeaponStyle.Fission);
        int atkRoll   = Mathf.RoundToInt(
            CombatMath.AttackRoll(fissionLv, equipAcc, AccuracyStance(stance)) * SpecialAccuracyMult);
        int defRoll   = CombatMath.DefenceRoll(ModuleEffects.For(_pe).TargetDefence(target), 0, 0);
        float chance  = CombatMath.HitChance(atkRoll, defRoll);

        // Meltdown can fire on the cascade too — one roll for the whole ability. Because every hit
        // is derived from the first one that lands, a proc scales the ENTIRE burst, not one blast.
        // That is the "delete the boss" moment.
        bool meltdown = CombatEffects.Meltdown(fissionLv);

        Msg((meltdown ? "<color=#FF6030>MELTDOWN OVERLOAD CASCADE!!</color>" : "<color=#7FE7FF>OVERLOAD CASCADE!</color>") +
            (recovered > 0 ? $" <color=#7FE7FF>({recovered} core{(recovered == 1 ? "" : "s")} recovered)</color>" : ""));

        int[] hits = CascadeDamage(maxHit, chance, meltdown);
        int total = 0;
        foreach (int d in hits)
        {
            if (d <= 0) { CombatFeedbackUI.ShowWorldSplat(target.transform.position, 0, false); continue; }
            target.TakeDamage(d);
            CombatFeedbackUI.ShowWorldSplat(target.transform.position, d, true);
            total += d;
            if (target.IsDead) break;
        }

        if (total > 0) AwardXP(total, ranged: true, fission: true, stance: stance);
        if (target.IsDead) Msg($"{target.DisplayName} is defeated.");
    }

    /// <summary>
    /// Dragon-claws damage pattern. Rolls accuracy for each hit in turn; the FIRST success picks a
    /// damage band (earlier successes roll higher) and every hit after it is half the one before,
    /// with the final hit mirroring the third rather than halving again. Whiffing all four still
    /// chips for 1, the same consolation claws give.
    ///
    /// When <paramref name="meltdown"/> procs, the first landing hit deals a flat 2x the core's max
    /// instead of rolling its band — and since the rest of the cascade halves down from it, the
    /// whole burst scales with it.
    /// </summary>
    static int[] CascadeDamage(int maxHit, float hitChance, bool meltdown)
    {
        var d = new int[SpecialHits];

        for (int i = 0; i < SpecialHits; i++)
        {
            if (Random.value > hitChance) continue;   // this hit missed — try the next one down

            // Bands run ABOVE a normal shot's 0..max roll — an overload is meant to overshoot the
            // core's ordinary rating. Sized so the whole cascade averages slightly more than the
            // four ordinary shots the same four cores would buy: the special must not be a damage
            // LOSS, or its cooldown and core cost make it strictly worse than just shooting.
            int lo, hi;
            switch (i)
            {
                case 0:  lo = maxHit * 5 / 8; hi = maxHit * 10 / 8; break;   // best case
                case 1:  lo = maxHit * 4 / 8; hi = maxHit *  9 / 8; break;
                case 2:  lo = maxHit * 3 / 8; hi = maxHit *  8 / 8; break;
                default: lo = maxHit * 3 / 8; hi = maxHit * 12 / 8; break;   // last-gasp, swingy
            }
            d[i] = meltdown
                 ? maxHit * CombatEffects.MeltdownMultiplier          // flat 2x max, no roll
                 : Random.Range(Mathf.Max(1, lo), Mathf.Max(2, hi) + 1);

            // Halve down the remaining hits; the last one mirrors its predecessor.
            if (i + 1 < SpecialHits) d[i + 1] = Mathf.Max(1, d[i] / 2);
            if (i + 2 < SpecialHits) d[i + 2] = Mathf.Max(1, d[i + 1] / 2);
            if (i + 3 < SpecialHits) d[i + 3] = d[i + 2];
            return d;
        }

        d[SpecialHits - 1] = 1;   // total whiff
        return d;
    }

    /// <summary>Brutality L80 cleave: deal splash damage to enemies near the primary target.</summary>
    void CleaveNearby(CombatTarget primary, int dmg)
    {
        float r2 = CombatEffects.CleaveRadius * CombatEffects.CleaveRadius;
        foreach (var t in Object.FindObjectsByType<CombatTarget>(FindObjectsInactive.Exclude))
        {
            if (t == primary || t.IsDead) continue;
            if ((t.transform.position - primary.transform.position).sqrMagnitude > r2) continue;
            t.TakeDamage(dmg);
            CombatFeedbackUI.ShowWorldSplat(t.transform.position, dmg, true);
        }
    }

    // Attack-option contributions (no XP split): Precise (Accurate) → +accuracy; Powerful
    // (Aggressive) → +damage; Rapid → faster swings (handled in CurrentSpeedTicks). Each is a small
    // tradeoff on the same swing; all train the one style stat.
    static int AccuracyStance(CombatStance s) => s == CombatStance.Accurate ? 3 : 0;
    static int StrengthStance(CombatStance s) => s == CombatStance.Aggressive ? 3 : 0;

    // Combat XP: 4 XP per damage to the primary skill, plus 1.333 XP to HP (Endurance).
    // Melee:  Accurate/Rapid → Melee,  Aggressive → Brutality,  Defensive → Hardening
    // Ranged: Precise/Powerful/Rapid → Marksmanship,  Defensive → Marksmanship + Hardening
    // Fractions carried so small hits don't lose XP to rounding.
    const float XP_PER_DAMAGE    = 4f;
    const float HP_XP_PER_DAMAGE = 4f / 3f;   // 1.333
    readonly Dictionary<Skill, float> _xpRemainder = new();

    /// <summary>
    /// Spend the shot. Gauntlets have a chance to recover the core instead of burning it — fission
    /// cores are the scarcest ammo in the game (one Elemental Golem yields 3-15 raw), so a strict
    /// one-core-per-shot rule would make the style unplayable however strong each blast is.
    /// </summary>
    void ConsumeShot(bool fission)
    {
        if (fission)
        {
            var w = _pe.Equipment.GetItem("Weapon");
            float save = w != null ? w.ammoSaveChance : 0f;
            if (save > 0f && Random.value < save)
            {
                Msg("<color=#7FE7FF>The gauntlets recover the core.</color>");
                return;
            }
        }
        _pe.Equipment.ConsumeAmmo(1);
    }

    void AwardXP(int damage, bool ranged, bool fission, CombatStance stance)
    {
        if (fission)
        {
            // Gauntlets train Fission, never Marksmanship, even though they fire at range.
            GiveXP(Skill.Fission, damage * XP_PER_DAMAGE);
        }
        else if (ranged)
        {
            GiveXP(Skill.Marksmanship, damage * XP_PER_DAMAGE);
        }
        else
        {
            // Melee is live again (tick-combat return). Each stance trains its own skill, matching
            // CombatManager.StanceSkill: Precise/Rapid → Attack (accuracy), Powerful → Strength,
            // Defensive → Defence. This is what makes the melee skills worth levelling.
            GiveXP(CombatManager.StanceSkill(stance, CombatStyle.Melee), damage * XP_PER_DAMAGE);
        }
        GiveXP(Skill.Endurance, damage * HP_XP_PER_DAMAGE);   // HP always trains, either style
    }

    /// <summary>Adds fractional XP, carrying the remainder so repeated small awards (e.g. 1.333 per
    /// hit) accumulate to the exact OSRS total instead of being floored away on every hit.</summary>
    void GiveXP(Skill skill, float amount)
    {
        if (amount <= 0f) return;
        float carried = amount + (_xpRemainder.TryGetValue(skill, out var r) ? r : 0f);
        int whole = Mathf.FloorToInt(carried);
        _xpRemainder[skill] = carried - whole;
        if (whole > 0) _pe.Stats.AddXP(skill, whole);
    }

    void FaceToward(Vector3 pos)
    {
        Vector3 d = pos - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d);
    }

    static void Msg(string m) => HUDController.Emit("<color=#FFD24A>[COMBAT]:</color> " + m);
}
