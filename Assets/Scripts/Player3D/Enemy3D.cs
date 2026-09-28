using UnityEngine;

/// <summary>
/// Simple 3D hostile: chases the player when close, swings on a cooldown, returns
/// home when you escape. Uses the same Hardening-based defence math as tick combat.
/// Respawns after death.
///
/// Real-time combat feel: every swing TELEGRAPHS — the enemy flashes red and (if its
/// controller has an "Attack" trigger) plays its swing clip, then the hit lands
/// windupSeconds later. Sliding (Space) through the window, or stepping out of range,
/// makes it whiff — that's the dodge counterplay. "Hit"/"Die" triggers play flinch and
/// death clips when the controller ships them. A model with no working Animator gets a
/// CreatureMotion instead (added automatically), which fakes the same beats by moving the
/// whole model: step bob, wind-up and lunge, flinch, fall-and-sink.
/// </summary>
[RequireComponent(typeof(CombatTarget))]
public class Enemy3D : MonoBehaviour
{
    public float aggroRange = 9f;
    public float attackRange = 1.8f;
    public float moveSpeed = 3.2f;
    public float attackCooldown = 1.8f;
    public float respawnSeconds = 10f;

    [Header("Attack telegraph")]
    [Tooltip("Seconds between the swing starting (red flash / attack clip) and the hit landing — the player's dodge window.")]
    public float windupSeconds = 0.4f;
    [Tooltip("How far past attackRange the swing still connects — stops a single backstep from trivially cheesing every hit; a real slide clears it.")]
    public float swingReachBonus = 1.35f;
    public Color telegraphTint = new Color(1f, 0.4f, 0.35f, 1f);
    [Tooltip("Seconds the corpse stays visible playing its death clip (only when the controller has a Die trigger).")]
    public float deathLingerSeconds = 2.2f;

    [Header("Idle wander")]
    public float wanderRadius = 1.5f;            // ~5 ft — gentle pacing around home when not fighting (0 = stand still)
    [Range(0.1f, 1f)] public float wanderSpeedMul = 0.45f;   // mosey, don't sprint, while idling
    public Vector2 wanderPauseRange = new(1.5f, 4f);         // seconds to pause between strolls

    CombatTarget _ct;
    Vector3 _home;
    Vector3 _wanderTarget; bool _haveWanderTarget; float _nextWanderAt;
    float _nextAttackAt, _respawnAt;
    bool _dead;
    bool _provoked;   // passive creatures only fight back once you've hit them
    Renderer[] _renderers;
    Collider[] _colliders;

    Animator _anim;               // rigged model (if one is attached)
    Vector3 _lastPos;
    static readonly int SpeedHash  = Animator.StringToHash("Speed");
    static readonly int AttackHash = Animator.StringToHash("Attack");
    static readonly int HitHash    = Animator.StringToHash("Hit");
    static readonly int DieHash    = Animator.StringToHash("Die");
    bool _animSpeed, _animAttack, _animHit, _animDie;   // which params this controller ships
    CreatureMotion _motion;       // code-driven motion for models with no skeleton

    /// <summary>Swing lifecycle, for CreatureMotion: the wind-up begins (seconds until the hit),
    /// then the blow lands (hit or whiff) or the swing is called off.</summary>
    public event System.Action<float> SwingStarted;
    public event System.Action SwingLanded, SwingCancelled;

    // windup / telegraph state
    bool  _windingUp;
    float _hitLandsAt;
    float _hideCorpseAt;
    MaterialPropertyBlock _mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId     = Shader.PropertyToID("_Color");

    void Awake()
    {
        _ct = GetComponent<CombatTarget>();
        _home = transform.position;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _colliders = GetComponentsInChildren<Collider>(true);
        _anim = GetComponentInChildren<Animator>();
        // Note which parameters this model's controller actually has, and only ever drive those —
        // spawner-placed mobs often have a bare Animator (no controller), which would otherwise
        // spam "Animator is not playing an AnimatorController" every frame.
        if (_anim != null && _anim.runtimeAnimatorController != null)
        {
            foreach (var p in _anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float   && p.nameHash == SpeedHash)  _animSpeed  = true;
                if (p.type == AnimatorControllerParameterType.Trigger && p.nameHash == AttackHash) _animAttack = true;
                if (p.type == AnimatorControllerParameterType.Trigger && p.nameHash == HitHash)    _animHit    = true;
                if (p.type == AnimatorControllerParameterType.Trigger && p.nameHash == DieHash)    _animDie    = true;
            }
        }
        else _anim = null;
        // No clips to play → fake them in code, so a static model never glides about like a statue.
        _motion = GetComponent<CreatureMotion>();
        if (_anim == null && _motion == null) _motion = gameObject.AddComponent<CreatureMotion>();
        _lastPos = transform.position;
        _ct.OnDamaged += _ =>
        {
            _provoked = true;
            if (!_dead && _anim != null && _animHit) _anim.SetTrigger(HitHash);   // flinch
        };
        _ct.OnDied += OnDied;
    }

    /// <summary>Re-anchor where this enemy returns to when it loses aggro. Needed for spawner-placed
    /// mobs, whose Awake cached _home before the spawner moved them into position.</summary>
    public void SetHome(Vector3 worldPos) => _home = worldPos;

    /// <summary>True when this creature is actively engaged with the player — an aggressive mob hunting
    /// you, or a passive one you've already hit — and still within aggro range. The companion only
    /// assists on these, so it never starts a fight on its own.</summary>
    public bool IsAggroOnPlayer
    {
        get
        {
            if (_dead || _ct == null || _ct.IsDead) return false;
            var p = PlayerEntity.Instance;
            if (p == null) return false;
            Vector3 to = p.transform.position - transform.position; to.y = 0f;
            if (to.magnitude > aggroRange) return false;
            return _ct.isAggressive || _provoked;
        }
    }

    void OnDied()
    {
        _dead = true;
        _respawnAt = Time.time + respawnSeconds;
        CancelWindup();

        // Golem remains are harvested. FissionGolem owns their visibility and lifetime.
        if (GetComponent<FissionGolem>() != null)
        {
            enabled = false;
            WeaponSkins.RollDrop(_ct);
            return;
        }

        // Colliders off right away (no clicking / bumping the corpse). If there's a death to show —
        // the controller's clip, or CreatureMotion's fall-and-sink — let it play before the body
        // vanishes; otherwise hide instantly as before.
        foreach (var c in _colliders) if (c != null) c.enabled = false;
        if (_anim != null && _animDie)
        {
            _anim.SetTrigger(DieHash);
            _hideCorpseAt = Time.time + deathLingerSeconds;
        }
        else if (_motion != null && _motion.isActiveAndEnabled) _hideCorpseAt = Time.time + deathLingerSeconds;
        else SetRenderersVisible(false);

        // No bones/remains drop any more — Beastmastery trains through beast tasks (see BeastTasks).
        WeaponSkins.RollDrop(_ct);   // rare cosmetic weapon-finish drop (never touches loot economy)
    }

    void SetVisible(bool on)
    {
        SetRenderersVisible(on);
        foreach (var c in _colliders) if (c != null) c.enabled = on;
    }

    void SetRenderersVisible(bool on)
    {
        foreach (var r in _renderers) if (r != null) r.enabled = on;
    }

    /// <summary>Red flash on every renderer while a swing is winding up — the universal telegraph
    /// that works even for models with no attack clip. Per-renderer property block, so shared
    /// materials are never touched.</summary>
    void SetTelegraph(bool on)
    {
        if (on)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetColor(BaseColorId, telegraphTint);   // URP Lit
            _mpb.SetColor(ColorId, telegraphTint);       // legacy/unlit fallbacks
        }
        foreach (var r in _renderers)
            if (r != null) r.SetPropertyBlock(on ? _mpb : null);
    }

    void CancelWindup()
    {
        if (!_windingUp) return;
        _windingUp = false;
        SetTelegraph(false);
        SwingCancelled?.Invoke();
    }

    void Update()
    {
        // Feed the model's idle→run blend from how far we moved last frame.
        if (_anim != null && _anim.isActiveAndEnabled && _animSpeed)
        {
            float spd = (transform.position - _lastPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            _anim.SetFloat(SpeedHash, _dead ? 0f : spd);
            _lastPos = transform.position;
        }

        if (_dead)
        {
            // Death clip finished playing → hide the corpse.
            if (_hideCorpseAt > 0f && Time.time >= _hideCorpseAt)
            {
                SetRenderersVisible(false);
                _hideCorpseAt = 0f;
            }
            if (Time.time >= _respawnAt)
            {
                _dead = false;
                _provoked = false;
                _hideCorpseAt = 0f;
                transform.position = _home;
                _ct.Reset();
                SetVisible(true);
                if (_anim != null) _anim.Rebind();   // out of the Die pose, back to Locomotion
                if (_motion != null) _motion.ResetPose();
            }
            return;
        }

        var player = PlayerEntity.Instance;
        if (player == null) return;

        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        if (dist > aggroRange)
        {
            _provoked = false;   // you got away — it loses interest
            CancelWindup();
            IdleWander();
            return;
        }

        // Passive creatures (isAggressive off) leave you alone until you attack them.
        if (!_ct.isAggressive && !_provoked)
        {
            IdleWander();
            return;
        }

        if (dist > 0.05f) transform.rotation = Quaternion.LookRotation(to);

        // Mid-windup: stand your ground; the swing lands (or whiffs) when the timer hits.
        if (_windingUp)
        {
            if (Time.time >= _hitLandsAt) LandSwing(player);
            return;
        }

        if (dist > attackRange)
        {
            transform.position += to.normalized * moveSpeed * Time.deltaTime;
            return;
        }

        if (Time.time < _nextAttackAt) return;
        StartWindup();
    }

    /// <summary>Begin a telegraphed swing: red flash + attack clip now, damage windupSeconds later.
    /// The cooldown runs from the swing, so the overall attack rhythm matches the old instant hits.</summary>
    void StartWindup()
    {
        _windingUp = true;
        _hitLandsAt = Time.time + windupSeconds;
        _nextAttackAt = _hitLandsAt + attackCooldown;
        if (_anim != null && _animAttack) _anim.SetTrigger(AttackHash);
        SetTelegraph(true);
        SwingStarted?.Invoke(windupSeconds);
    }

    /// <summary>The windup timer expired — connect, unless the player slid or stepped out of the arc.</summary>
    void LandSwing(PlayerEntity player)
    {
        _windingUp = false;
        SetTelegraph(false);
        SwingLanded?.Invoke();   // the blow goes in either way — whether it connects is decided below

        Vector3 to = player.transform.position - transform.position; to.y = 0f;
        if (to.magnitude > attackRange * swingReachBonus)
        {
            CombatFeedbackUI.ShowWorldSplat(player.transform.position, 0, false);   // clean whiff
            return;
        }
        Attack(player);
    }

    /// <summary>Idle behaviour: mosey around home within wanderRadius, pausing between strolls. If the
    /// creature has drifted outside that patch (e.g. it chased you and then lost interest), it walks
    /// back in first. wanderRadius = 0 keeps it rooted to the spot.</summary>
    void IdleWander()
    {
        // Outside the home patch — head back toward home before resuming the gentle pacing.
        Vector3 toHome = _home - transform.position; toHome.y = 0f;
        if (toHome.magnitude > Mathf.Max(wanderRadius, 0.5f))
        {
            transform.position += toHome.normalized * (moveSpeed * 0.6f) * Time.deltaTime;
            FaceMove(toHome);
            _haveWanderTarget = false;
            return;
        }

        if (wanderRadius <= 0.01f) return;   // pinned in place

        // Pick a fresh spot to amble to once the post-stroll pause is up.
        if (!_haveWanderTarget && Time.time >= _nextWanderAt)
        {
            Vector2 r = Random.insideUnitCircle * wanderRadius;
            _wanderTarget = _home + new Vector3(r.x, 0f, r.y);
            _haveWanderTarget = true;
        }

        if (!_haveWanderTarget) return;

        Vector3 to = _wanderTarget - transform.position; to.y = 0f;
        if (to.magnitude < 0.15f)
        {
            _haveWanderTarget = false;
            _nextWanderAt = Time.time + Random.Range(wanderPauseRange.x, wanderPauseRange.y);
            return;
        }
        transform.position += to.normalized * (moveSpeed * wanderSpeedMul) * Time.deltaTime;
        FaceMove(to);
    }

    void FaceMove(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    /// <summary>Brutality L40 stagger: push this enemy's next swing back by some seconds — and
    /// interrupt a swing that's mid-windup, so a well-timed stagger cancels the incoming hit.</summary>
    public void Stagger(float seconds)
    {
        CancelWindup();
        _nextAttackAt = Mathf.Max(_nextAttackAt, Time.time + seconds);
    }

    void Attack(PlayerEntity player)
    {
        // A swing that lands mid-slide whiffs outright — the dodge (Space) is a real defensive
        // tool for the real-time combat, not just movement.
        var pc = player.GetComponent<Player3DController>();
        if (pc != null && pc.IsDodging)
        {
            CombatFeedbackUI.ShowWorldSplat(player.transform.position, 0, false);
            return;
        }

        // Defence roll: Hardening is the defence level; it also drives reduction/negate/reflect below.
        int hardening = player.Stats.GetLevel(Skill.Defence);

        // Hardening L60: very small chance to completely negate the hit.
        if (CombatEffects.NegateHit(hardening))
        {
            CombatFeedbackUI.ShowWorldSplat(player.transform.position, 0, false);
            HUDController.Emit("<color=#80D0FF>[NEGATED]</color> Your Hardening absorbed the blow.");
            return;
        }

        // Hardening IS the defence level — DefenceRoll is (level + 8) * (gearDefence + 64), so
        // pinning the level at 1 made the multiplier 9 instead of 107 at max level and armour
        // barely registered. Gear bonuses are balanced against a real level, per the combat doc.
        int atkRoll = CombatMath.AttackRoll(_ct.attackLevel, 0, 0);
        // Worn armour plus any completed set bonus (Ascendant).
        int equipDef = player.Equipment.TotalDefenceBonus() + GearSets.BonusDefence(player.Equipment) + ModuleEffects.For(player).Defence();
        int defRoll = CombatMath.DefenceRoll(hardening, equipDef, DefenceStanceBonus());
        bool hit    = Random.value <= CombatMath.HitChance(atkRoll, defRoll);

        int dmg = 0;
        if (hit)
        {
            dmg = Mathf.Max(1, Random.Range(0, _ct.maxDamage + 1));   // enemy max hit = its designer-set maxDamage
            // Hardening damage reduction (passive % reduction based on Hardening level).
            float dr = CombatEffects.DamageReduction(hardening);
            if (dr > 0f) dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - dr)));

            // Hardening L99: 8% chance to reflect damage back to the attacker.
            if (CombatEffects.ReflectDamage(hardening))
            {
                _ct.TakeDamage(dmg);
                CombatFeedbackUI.ShowWorldSplat(transform.position, dmg, true);
                HUDController.Emit($"<color=#FF80FF>[REFLECT]</color> Your Hardening reflects {dmg} damage!");
            }

            dmg = ModuleEffects.For(player).Incoming(dmg, _ct, attackRange > 4f);
            if (CompanionManager.Instance != null) dmg = CompanionManager.Instance.AbsorbForPlayer(dmg);
            player.Stats.TakeDamage(dmg);
        }
        CombatFeedbackUI.ShowWorldSplat(player.transform.position, dmg, hit);
    }

    /// <summary>
    /// Hidden defensive-style modifier added to the effective defence level, per the combat doc:
    /// Defensive/Longrange +3, Controlled +1, everything else 0. Small on paper, but it is the
    /// reason to swap stance when you are being hit hard.
    /// </summary>
    static int DefenceStanceBonus()
    {
        var stance = CombatManager.Instance != null ? CombatManager.Instance.Stance : CombatStance.Accurate;
        return stance switch
        {
            CombatStance.Defensive => 3,
            CombatStance.Longrange => 3,
            CombatStance.Controlled => 1,
            _ => 0,
        };
    }
}
