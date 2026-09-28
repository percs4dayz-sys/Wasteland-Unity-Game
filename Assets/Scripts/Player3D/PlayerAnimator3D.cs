using UnityEngine;

/// <summary>
/// Drives the player model's Animator from the existing 3D systems — no per-frame
/// glue needed in those scripts. Lives on the player ROOT (next to Player3DController);
/// finds the Animator on the visual child and feeds it:
///   • Speed       — measured planar movement (blend Idle/Walk/Run)
///   • Aiming      — from ActionCombat3D
///   • Gathering / GatherType — from SkillingManager (wood/scrap vs fishing)
///   • AttackMelee / Shoot — when a swing or shot resolves
///   • Hit / Die   — from PlayerStats events
///
/// If the Animator has no controller, it loads Resources/PlayerAnimator at runtime,
/// so a freshly-built scene "just works" once you've generated that controller.
/// </summary>
[RequireComponent(typeof(Player3DController))]
public class PlayerAnimator3D : MonoBehaviour
{
    static readonly int P_Speed      = Animator.StringToHash("Speed");
    static readonly int P_Aiming     = Animator.StringToHash("Aiming");
    static readonly int P_Gathering  = Animator.StringToHash("Gathering");
    static readonly int P_GatherType = Animator.StringToHash("GatherType");
    static readonly int P_AttackMelee= Animator.StringToHash("AttackMelee");
    static readonly int P_Shoot      = Animator.StringToHash("Shoot");
    static readonly int P_Hit        = Animator.StringToHash("Hit");
    static readonly int P_Die        = Animator.StringToHash("Die");
    static readonly int P_Celebrate  = Animator.StringToHash("Celebrate");
    static readonly int P_Dodge      = Animator.StringToHash("Dodge");
    static readonly int P_Reload     = Animator.StringToHash("Reload");

    Animator _anim;
    ActionCombat3D _combat;
    Player3DController _pc;
    WeaponMagazine _magazine;
    PlayerEntity _pe;
    Vector3 _lastPos;
    int _lastHp = -1;
    int _lastGatherType = -1;

    [Tooltip("Keeps the player sized to match the world and plants his feet, continuously. Only a " +
             "clearly-wrong size (>12% off target) is corrected, so a good scale is left alone.")]
    [SerializeField] bool autoResizeVisual = true;

    [Tooltip("Absolute fallback height (metres) when no world characters are found to match.")]
    [SerializeField] float targetHeight = 1.8f;

    [Tooltip("Size the player to match the scene's other characters (NPCs/enemies) instead of Target " +
             "Height. Only needed for a world authored ABOVE human scale — leave OFF for a normal, " +
             "human-scale map (ON can accidentally match an oversized boss). Falls back to Target Height.")]
    [SerializeField] bool matchWorldCharacters = false;

    Transform _vis;
    bool _footAligned;
    bool _visRotHealed;   // logged once when the Visual's stray local rotation is squared to the root
    int _alignTick;
    int _nextHeightCheckFrame;
    bool _sizedOnce;   // height rescue is one-shot — see LateUpdate

    void Awake()
    {
        _combat = GetComponent<ActionCombat3D>();
        _pc = GetComponent<Player3DController>();
        _pe = GetComponent<PlayerEntity>();

        // Self-heal: if the player root somehow got named "Visual" (e.g. from a model swap),
        // rename it back to "Player" so the child-finding logic works correctly.
        if (transform.name == "Visual" && GetComponent<Player3DController>() != null)
        {
            transform.name = "Player";
            Debug.LogWarning("[PlayerAnimator3D] Player root was named 'Visual' — renamed to 'Player'.");
        }
    }

    /// <summary>
    /// The BODY's Animator — never just the first Animator found. First-found breaks whenever anything
    /// else carrying an Animator (first-person arms, a nameplate rig, a leftover model) sits earlier
    /// in child order: that rig gets spammed with Speed/Aiming/Gathering it doesn't have ("Parameter
    /// 'Hash …' does not exist", forever) while the real character stands frozen, driven by nobody.
    /// Preference order: the child named "Visual", else any humanoid Animator with a skinned mesh,
    /// else first-found as a last resort.
    /// </summary>
    Animator ResolveBodyAnimator()
    {
        var named = transform.Find("Visual");
        if (named != null)
        {
            var a = named.GetComponentInChildren<Animator>(true);
            if (a != null) return a;
        }
        foreach (var a in GetComponentsInChildren<Animator>(true))
            if (a.avatar != null && a.avatar.isHuman &&
                a.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                return a;
        return GetComponentInChildren<Animator>();
    }

    void Start()
    {
        _anim = ResolveBodyAnimator();
        if (_anim != null)
        {
            // Always assign the canonical controller (rebuilt by PlayerAnimatorBuilder) — not just when
            // it's missing. The model prefab can ship with a STALE baked controller that otherwise wins
            // and keeps playing the OLD animations even after a rebuild, so force ours on top.
            if (forceCanonicalController)
            {
                var rc = Resources.Load<RuntimeAnimatorController>("PlayerAnimator");
                if (rc != null && _anim.runtimeAnimatorController != rc)
                    _anim.runtimeAnimatorController = rc;
            }
            else Debug.LogWarning("[PlayerAnimator3D] forceCanonicalController is OFF — keeping " +
                                  $"'{(_anim.runtimeAnimatorController != null ? _anim.runtimeAnimatorController.name : "none")}'. " +
                                  "Bisection only; turn back ON for normal play.");
        }
        // Movement is driven by Player3DController — never by the clips. Some imported rigs ship
        // with Apply Root Motion ON, which makes the Mixamo locomotion/turn clips drag the model in
        // a circle off the capsule. Force it off so the mesh stays centred on the player.
        if (_anim != null) _anim.applyRootMotion = false;

        // Visual normalisation (rescale + foot-align) happens once in LateUpdate, after the Animator
        // has posed the rig. Doing it here in Start measured bind-pose bounds through the bone the
        // mesh hangs off, which blew the model up and flung the Visual into the sky.

        if (_combat != null) _combat.OnAttackPerformed += HandleAttack;
        if (_pc != null) _pc.OnDodged += HandleDodge;
        _magazine = GetComponent<WeaponMagazine>();   // added at runtime by ActionCombat3D.Awake
        if (_magazine != null) _magazine.OnReloadStarted += HandleReload;
        if (_pe != null && _pe.Stats != null)
        {
            _pe.Stats.OnHPChanged   += HandleHpChanged;
            _pe.Stats.OnPlayerDied  += HandleDied;
            _pe.Stats.OnLevelUp     += HandleLevelUp;
            _lastHp = _pe.Stats.CurrentHP;
        }
        _lastPos = transform.position;
    }

    void OnDestroy()
    {
        if (_combat != null) _combat.OnAttackPerformed -= HandleAttack;
        if (_pc != null) _pc.OnDodged -= HandleDodge;
        if (_magazine != null) _magazine.OnReloadStarted -= HandleReload;
        if (_pe != null && _pe.Stats != null)
        {
            _pe.Stats.OnHPChanged  -= HandleHpChanged;
            _pe.Stats.OnPlayerDied -= HandleDied;
            _pe.Stats.OnLevelUp    -= HandleLevelUp;
        }
    }

    // ── temporary animation diagnostic ───────────────────────────────────
    // Is the SKELETON moving? The Animator inspector can't answer that. Samples the hips each second
    // for the first few seconds and reports what the state machine thinks it's doing. Delete once the
    // animation question is settled.
    [SerializeField] bool logAnimationProbe = true;

    [Tooltip("ON: force Resources/PlayerAnimator onto the model every time, overwriting whatever " +
             "controller is assigned in the Inspector. Turn OFF to test a different controller " +
             "in-game without losing movement — otherwise your choice is silently replaced on Play.")]
    [SerializeField] bool forceCanonicalController = true;
    Quaternion _probeLastHips;
    float _probeNext;
    int _probeCount;

    /// <summary>Where the probe writes. A file rather than the Console so the whole picture can be read
    /// in one go instead of hunted for among a few hundred log lines.</summary>
    static string ProbePath => System.IO.Path.Combine(Application.persistentDataPath, "anim_probe.log");

    void AnimationProbe()
    {
        if (!logAnimationProbe || _probeCount >= 8) return;
        if (Time.time < _probeNext) return;
        _probeNext = Time.time + 1f;
        _probeCount++;

        var sb = new System.Text.StringBuilder();

        if (_probeCount == 1)
        {
            try { System.IO.File.WriteAllText(ProbePath, $"=== anim probe {System.DateTime.Now} ===\n"); }
            catch { /* diagnostics must never break the game */ }

            sb.AppendLine($"player root      : {transform.name} @ {transform.position}");
            sb.AppendLine($"visual (_vis)    : {(_vis != null ? _vis.name : "NULL")}");
            sb.AppendLine($"animator (_anim) : {(_anim != null ? _anim.gameObject.name : "NULL")}");
            if (_anim != null)
            {
                sb.AppendLine($"  controller     : {(_anim.runtimeAnimatorController != null ? _anim.runtimeAnimatorController.name : "NONE")}");
                sb.AppendLine($"  avatar         : {(_anim.avatar != null ? _anim.avatar.name : "NONE")} " +
                              $"valid={_anim.avatar != null && _anim.avatar.isValid} isHuman={_anim.isHuman}");
                sb.AppendLine($"  layers         : {_anim.layerCount}, params {_anim.parameterCount}");
            }
        }

        if (_anim == null) { Append(sb); return; }

        var hips = _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.Hips) : null;
        var lFoot = _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.LeftFoot) : null;

        float moved = hips != null ? Quaternion.Angle(_probeLastHips, hips.localRotation) : -1f;
        if (hips != null) _probeLastHips = hips.localRotation;

        var st = _anim.GetCurrentAnimatorStateInfo(0);
        var clips = _anim.GetCurrentAnimatorClipInfo(0);
        string clip = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "none";

        sb.AppendLine(
            $"[{_probeCount}] clip='{clip}' clips={clips.Length} t={st.normalizedTime:F2} " +
            $"stateHash={st.shortNameHash} speedParam={_anim.GetFloat(P_Speed):F2} " +
            $"hipsMoved={moved:F2} " +
            $"visY={(_vis != null ? _vis.position.y.ToString("F3") : "-")} " +
            $"visLocalY={(_vis != null ? _vis.localPosition.y.ToString("F3") : "-")} " +
            $"rootY={transform.position.y:F3} " +
            $"hipsY={(hips != null ? hips.position.y.ToString("F3") : "-")} " +
            $"footY={(lFoot != null ? lFoot.position.y.ToString("F3") : "-")} " +
            $"scale={(_vis != null ? _vis.localScale.x.ToString("F3") : "-")} " +
            $"aligned={_footAligned} sized={_sizedOnce}");

        Append(sb);
    }

    static void Append(System.Text.StringBuilder sb)
    {
        try { System.IO.File.AppendAllText(ProbePath, sb.ToString()); }
        catch { /* ignore */ }
    }

    void Update()
    {
        AnimationProbe();

        if (_anim == null || _anim.runtimeAnimatorController == null) return;

        // Measured speed keeps the blend honest whether you moved by stick, WASD, or click-to-move.
        Vector3 d = transform.position - _lastPos; d.y = 0f;
        _lastPos = transform.position;
        float speed = d.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);

        // Gate on the controller's INTENT to move. Raw position delta is polluted at rest: gravity
        // calls _cc.Move every frame and on any non-flat ground the capsule micro-slides/jitters as it
        // settles, so measured speed never quite reaches 0 — the blend tree then hovers just above the
        // idle threshold forever, churning the feet instead of releasing to idle. Player3DController
        // knows when it is actually driving movement (false the instant DestinationMove arrives), and
        // that signal is immune to the jitter. When it says "not moving", force speed to 0.
        if (_pc != null && !_pc.IsMoving) speed = 0f;

        _anim.SetFloat(P_Speed, speed, 0.10f, Time.deltaTime);

        _anim.SetBool(P_Aiming, _combat != null && _combat.IsAiming);

        var sk = SkillingManager.Instance;
        bool gathering = sk != null && sk.IsGathering;
        _anim.SetBool(P_Gathering, gathering);
        if (gathering)
        {
            var node = sk.ActiveNode;
            // Gather clip per skill (GatherType): Woodcutting → chop (0), Fishing → cast (1),
            // Scrapping → mining/dig (2). Keyed on the node's skill.
            int type = node == null ? 0 : node.skill switch
            {
                Skill.Fishing   => 1,   // fishing cast clip
                Skill.Scrapping => 2,   // mining/digging clip
                _               => 0    // Woodcutting (and anything else) → chop
            };
            _anim.SetInteger(P_GatherType, type);
            // The controller enters gathering from locomotion. When a player switches
            // directly between nodes there may be no idle frame to select the new state.
            if (_lastGatherType != type)
            {
                string state = type == 1 ? "GatherFish" : type == 2 ? "GatherMine" : "GatherWood";
                if (_anim.HasState(0, Animator.StringToHash(state))) _anim.CrossFadeInFixedTime(state, 0.1f);
            }
            _lastGatherType = type;
        }
        else _lastGatherType = -1;
    }

    // Keep the visual at human height, continuously. The old approach measured the POSED humanoid
    // skeleton a few frames in — but with a hand-scaled model (the Crimson FBX imports microscopic and
    // is scaled ×1000 in the scene) that posed measurement could read a wildly wrong span and shrink
    // the player to a speck every session. Now we measure the AUTHORED mesh bounds of the body mesh
    // (pose-independent, valid every frame, consistent at any transform scale) and only correct when
    // the rendered height is genuinely off — so a correct hand-set scale is respected, a microscopic
    // or ballooned one heals itself, and the check re-runs forever so nothing can shrink him again.
    void LateUpdate()
    {
        // Keep the working pose aimed at the resource after movement / aim scripts run.
        var node = SkillingManager.Instance != null ? SkillingManager.Instance.ActiveNode : null;
        if (node != null && _pc != null && !_pc.IsMoving)
        {
            Vector3 toward = node.transform.position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(toward);
        }
        // Self-heal guard (every frame): keep the model glued to the player ROOT. Rigs imported with
        // Apply Root Motion ON drag the mesh off the capsule as you walk — the Visual drifted hundreds
        // of units from the root (seen as local x ≈ -367), so the camera, correctly following the root,
        // looked like it abandoned the character. Force root motion off and zero any horizontal drift
        // so the model can never separate from the player again.
        if (_vis == null) _vis = ResolveVisual();
        if (_vis != null)
        {
            if (_anim == null) _anim = _vis.GetComponentInChildren<Animator>();
            if (_anim != null && _anim.applyRootMotion) _anim.applyRootMotion = false;
            var lp = _vis.localPosition;
            if (lp.x != 0f || lp.z != 0f) _vis.localPosition = new Vector3(0f, lp.y, 0f);

            // Self-heal: the model must face where the ROOT faces — the controller turns the root toward
            // your movement, and the mesh rides it. A stray local rotation on the Visual (a hand-nudged
            // gizmo, or a swap tool that parented the model at an angle — this scene shipped with ~267° on
            // Y) twists the body off-axis, so you appear turned 90° from the camera and crab-walk away
            // from your clicks. Keep the Visual square to the root; turns are posed through the bones by
            // the animator, never through this transform, so forcing identity here is always safe.
            if (_vis.localRotation != Quaternion.identity)
            {
                if (!_visRotHealed)
                {
                    Debug.Log("[PlayerAnimator3D] Visual had a local rotation offset — squared it back to " +
                              "the root so the character faces where it moves (was crab-walking 90°).");
                    _visRotHealed = true;
                }
                _vis.localRotation = Quaternion.identity;
            }

            // Self-heal: strip any colliders on the Visual child — they confuse the interactor's
            // Physics.OverlapSphere and make it seem like you're near a resource node when you're
            // actually across the map (the Visual's collider is at the root's position, but the
            // mesh may have drifted or the collider is huge).
            foreach (var col in _vis.GetComponentsInChildren<Collider>(true))
            {
                if (col != null && col.gameObject != gameObject)
                {
                    Debug.LogWarning($"[PlayerAnimator3D] Stripped stray collider '{col.GetType().Name}' " +
                                     $"from Visual child '{col.name}' — it was causing false proximity.");
                    Destroy(col);
                }
            }
        }
        if (_vis == null) return;

        // Opt-out: a model you've hand-scaled and want left alone entirely.
        if (!autoResizeVisual) return;

        // Height rescue — ONE-SHOT. An animating character's rendered height swings a lot with pose
        // (idle vs attack vs crouch), so a tight, continuous check oscillates: it'd read 1.3 m mid-swing,
        // scale up, then read 2.5 m the next pose and scale back down, forever (that constant rescaling
        // also threw the hand bones out of range). So: correct ONCE, and only when the size is CLEARLY
        // broken (less than half or more than double the target — a speck or a giant), never for normal
        // pose variance. A model that's already roughly right is left completely alone.
        // Never size off a rig that isn't being driven. A model with no controller (or no avatar) sits
        // in an unposed/collapsed state whose bounds read far too small, so f = target/h comes out huge
        // and the one-shot rescale locks in a giant permanently. Wait until it's actually animating.
        bool animatorDriving = _anim != null
                            && _anim.runtimeAnimatorController != null
                            && _anim.avatar != null;

        if (!_sizedOnce && animatorDriving && Time.frameCount >= _nextHeightCheckFrame)
        {
            float h = MeasureWorldHeight();
            float target = ResolveTargetHeight();
            if (h > 0.05f && target > 1e-3f)          // valid measurement (bounds have posed)
            {
                float f = target / h;
                if (f < 0.5f || f > 2f)               // only rescue a clearly-wrong size
                {
                    _vis.localScale *= f;
                    _footAligned = false; _alignTick = 0;
                    Debug.Log($"[PlayerAnimator3D] Player measured {h:F2} m — one-shot rescale ×{f:F3} toward {target:F2} m.");
                }
                _sizedOnce = true;                    // done — never fight the animation again
            }
            else _nextHeightCheckFrame = Time.frameCount + 5;   // bounds not ready yet → retry soon
        }

        if (_footAligned) return;
        if (_alignTick++ < 2) return;                       // give the Animator a couple frames to pose
        FootAlign();

        // Keep skinned meshes from frustum-culling themselves to nothing.
        foreach (var smr in _vis.GetComponentsInChildren<SkinnedMeshRenderer>())
            smr.updateWhenOffscreen = true;

        _footAligned = true;
    }

    /// <summary>Character height in metres = the TRUE on-screen size: the world-space AABB of the
    /// posed body mesh (the biggest skinned mesh). This is the only measure that can't lie — on this
    /// rig the bone span AND the authored mesh bounds both reported ~9.6× too large (17 m for a model
    /// that's correct at ~1.8 m), because the skeleton/mesh are authored in a mismatched unit. Rendered
    /// bounds always match what you see, so sizing off them converges and holds.
    ///
    /// Returns 0 when bounds aren't valid yet (the collapsed pre-pose frames) so the watchdog just
    /// waits instead of ballooning the model to fill a near-zero measurement.</summary>
    /// <summary>Target rendered height: match the scene's other characters (NPCs first — they're
    /// placed at the intended in-world human size — then a real enemy), so the player fits a world
    /// authored above human scale. Falls back to the absolute targetHeight when nothing suitable is
    /// loaded yet. Recomputed each tick (cheap) so it self-heals as the scene populates.</summary>
    float ResolveTargetHeight()
    {
        if (matchWorldCharacters)
        {
            var roxy = Object.FindAnyObjectByType<RoxyNPC>();
            float h = roxy != null ? ExternalCharacterHeight(roxy.gameObject) : 0f;
            if (h > 0.2f) return h;

            foreach (var ct in Object.FindObjectsByType<CombatTarget>(FindObjectsInactive.Exclude))
            {
                if (ct.isDummy) continue;
                h = ExternalCharacterHeight(ct.gameObject);
                if (h > 0.2f) return h;
            }
        }
        return targetHeight;
    }

    /// <summary>Rendered world height of another character (its combined renderer bounds).</summary>
    static float ExternalCharacterHeight(GameObject go)
    {
        Bounds b = default; bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return has ? b.size.y : 0f;
    }

    float MeasureWorldHeight()
    {
        // Union of every skinned mesh, NOT just the biggest one. Picking the largest-vertex-count mesh
        // assumed a single body mesh spanning head→foot — true for a one-piece FBX, false for a Sidekick
        // character, which is one SkinnedMeshRenderer per body part. There the biggest part is a torso,
        // so the player measured 0.78 m and got rescaled ×2.3 into a giant.
        Bounds body = default; bool hasBody = false;
        foreach (var smr in _vis.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.updateWhenOffscreen = true;   // keep bounds live even when the camera isn't looking
            if (smr.sharedMesh == null) continue;
            if (!hasBody) { body = smr.bounds; hasBody = true; } else body.Encapsulate(smr.bounds);
        }
        if (hasBody)
        {
            float h = body.size.y;                        // world AABB of the CURRENT pose
            return h > 0.05f ? h : 0f;                    // <5 cm = not posed yet → skip this tick
        }

        // Static / no skinned mesh: the combined renderer bounds.
        Bounds total = default; bool has = false;
        foreach (var r in _vis.GetComponentsInChildren<MeshRenderer>())
        {
            if (!has) { total = r.bounds; has = true; } else total.Encapsulate(r.bounds);
        }
        float sh = has ? total.size.y : 0f;
        return sh > 0.05f ? sh : 0f;
    }

    /// <summary>Drop the model so its feet sit on the capsule bottom. Uses the posed foot bones on a
    /// humanoid (sole ≈ 9 cm below the ankle), else the rendered bounds bottom.</summary>
    void FootAlign()
    {
        var cc = GetComponent<CharacterController>();
        float capsuleBottom = transform.position.y + (cc != null ? cc.center.y - cc.height * 0.5f : 0f);

        if (_anim != null && _anim.isHuman)
        {
            var lFoot = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rFoot = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
            if (lFoot != null || rFoot != null)
            {
                float footY = Mathf.Min(lFoot != null ? lFoot.position.y : float.MaxValue,
                                        rFoot != null ? rFoot.position.y : float.MaxValue);
                _vis.position += Vector3.up * (capsuleBottom + 0.09f - footY);
                return;
            }
        }

        var rends = _vis.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        _vis.position += Vector3.up * (capsuleBottom + 0.02f - b.min.y);
    }

    /// <summary>Re-bind to whichever model is under the player right now, and put the canonical
    /// controller on it. ArmourVisuals replaces the entire character object whenever the loadout
    /// changes, but the Animator cached in Start() is only ever re-resolved when it's null — so
    /// without this the old, discarded model keeps being driven while the freshly built Sidekick
    /// character stands frozen in its bind pose (arms straight out to the sides).</summary>
    /// <param name="model">The model to drive. Pass the object you just built — searching for it by
    /// name or by "first Animator in the hierarchy" picks whichever leftover model happens to sit
    /// earlier in the children, which is how a stale "Visual" kept getting animated while the real
    /// character stood frozen beside it.</param>
    public void RebindVisual(GameObject model = null)
    {
        _vis  = model != null ? model.transform : null;
        _anim = model != null ? model.GetComponentInChildren<Animator>()
                              : ResolveBodyAnimator();

        if (_anim != null)
        {
            if (forceCanonicalController)
            {
                var rc = Resources.Load<RuntimeAnimatorController>("PlayerAnimator");
                if (rc != null && _anim.runtimeAnimatorController != rc)
                    _anim.runtimeAnimatorController = rc;
            }
            _anim.applyRootMotion = false;
        }

        // A swapped-in model has to be sized and foot-aligned again from scratch.
        _footAligned = false;
        _sizedOnce   = false;

        // Whether the humanoid avatar actually RESOLVED against this hierarchy. The Animator inspector
        // can't tell you this — its "Muscles: 130" describes the clip, and reads the same whether the
        // avatar bound or not. If isHuman is false or Hips is null, the clip plays into nothing.
        if (_anim != null)
        {
            var hips = _anim.avatar != null && _anim.isHuman
                     ? _anim.GetBoneTransform(HumanBodyBones.Hips)
                     : null;
            Debug.Log($"[PlayerAnimator3D] Rebound to '{_anim.gameObject.name}' — " +
                      $"avatar '{(_anim.avatar != null ? _anim.avatar.name : "none")}' " +
                      $"(valid {_anim.avatar != null && _anim.avatar.isValid}, " +
                      $"human {_anim.avatar != null && _anim.avatar.isHuman}), " +
                      $"isHuman {_anim.isHuman}, hips '{(hips != null ? hips.name : "NOT RESOLVED")}'.");
        }
    }

    /// <summary>The model child to normalise. Prefers a child literally named "Visual", but falls back
    /// to whichever direct child of the player root actually carries the Animator — so a freshly
    /// swapped-in model (e.g. an FBX dragged under the player but not renamed "Visual") still gets
    /// foot-aligned and driven instead of T-posing waist-deep in the ground.</summary>
    Transform ResolveVisual()
    {
        var named = transform.Find("Visual");
        if (named != null) return named;
        if (_anim == null) _anim = ResolveBodyAnimator();
        if (_anim != null)
        {
            var t = _anim.transform;
            while (t.parent != null && t.parent != transform) t = t.parent;   // climb to the player's direct child
            return t;
        }
        return null;
    }

    void HandleAttack(bool ranged) => _anim?.SetTrigger(ranged ? P_Shoot : P_AttackMelee);

    // Dodge/Reload states may not exist in an older built controller — fire these triggers only if
    // the parameter exists, so there's no console warning until the controller is rebuilt.
    void HandleDodge()  => SetTriggerIfPresent(P_Dodge);
    void HandleReload() => SetTriggerIfPresent(P_Reload);

    void SetTriggerIfPresent(int hash)
    {
        if (_anim == null || _anim.runtimeAnimatorController == null) return;
        foreach (var p in _anim.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.nameHash == hash)
            {
                _anim.SetTrigger(hash);
                return;
            }
    }

    void HandleHpChanged(int current, int max)
    {
        if (current < _lastHp && current > 0) _anim?.SetTrigger(P_Hit);
        _lastHp = current;
    }

    void HandleDied() => _anim?.SetTrigger(P_Die);

    // Ding! A quick fist-pump celebration whenever a skill levels up. The Celebrate state cancels on
    // movement and is overridden by the next attack, so it never locks you up mid-fight.
    void HandleLevelUp(Skill skill, int newLevel) => _anim?.SetTrigger(P_Celebrate);

    /// <summary>Stand the player back up after a respawn: force the animator out of the Die state
    /// back into Locomotion. Called by PlayerRespawn once HP is restored.</summary>
    public void Revive()
    {
        if (_pe != null && _pe.Stats != null) _lastHp = _pe.Stats.CurrentHP;
        if (_anim != null && _anim.runtimeAnimatorController != null)
        {
            _anim.ResetTrigger(P_Die);
            _anim.Play("Locomotion", 0, 0f);   // immediate exit from the (exit-less) Die state
        }
    }

}
