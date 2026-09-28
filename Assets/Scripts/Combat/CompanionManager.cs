using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Beastmastery — your companion.
///
/// You choose ONE of three eggs from Old Mara. Carry it and it hatches into a
/// beast that grows with your Beastmastery level (stages at 30 and 60):
///   • Mottled Egg  → Mutant Pup → Mutant Wolf → ALPHA WOLF   (savage melee, Frenzy)
///   • Heavy Egg    → Hatchling  → Shellback  → COLOSSAL SHELLBACK
///                    (slow tank; carries extra inventory slots for you: +2/+4/+6)
///   • Speckled Egg → Rad-Chick  → Razorbeak  → STORM RAZORBEAK
///                    (spotter: passive +10/15/20% ranged damage for YOU while it's out,
///                     plus its own heavy dives)
///
/// The beast follows you, attacks whatever you're fighting (2D tick combat or the
/// 3D target lock), can be scattered, and runs on stamina. (Beastmastery trains Slayer-style:
/// kills of your beast task's creature count only while the beast is out — see BeastTasks.)
/// Summon/dismiss with B, special with H. Self-building; no inspector setup.
/// </summary>
public class CompanionManager : MonoBehaviour
{
    public static CompanionManager Instance { get; private set; }

    [Header("Tuning")]
    public KeyCode summonKey  = KeyCode.B;          // pad: RB
    public KeyCode frenzyKey  = KeyCode.H;          // pad: Y
    public float frenzyCooldown    = 20f;  // seconds
    public float regroupSeconds    = 12f;  // downtime after being scattered
    public float incubateSeconds = 120f;   // carry an egg this long and it hatches

    // ── species lines ────────────────────────────────────────────────────
    class SpeciesDef
    {
        public string key, verb, specialName, hatchMsg;
        public int eggId, attackEveryTicks;
        public float dmgMult, hpMult, specialMult;
        public string[] stageNames;
        public float[] stageScale;
        public int[] carrySlots;
        public string[] modelNames;   // 3D model per stage (Resources/). null/empty → 2D sprite billboard.
        public string controllerName; // animator controller in Resources for a rigged model. null = static.
    }

    static readonly SpeciesDef[] Defs =
    {
        new SpeciesDef { key="wolf", eggId=50, attackEveryTicks=3, dmgMult=1f, hpMult=1f,
            specialName="Frenzy", specialMult=3f, verb="mauls",
            stageNames=new[]{"Mutant Pup","Mutant Wolf","Alpha Wolf"},
            stageScale=new[]{1f,1.2f,1.45f}, carrySlots=new[]{0,0,0},
            modelNames=new[]{"wolf","wolf","wolf"}, controllerName="WolfAnimator",
            hatchMsg="The egg cracks open — a scrawny mutant pup blinks up at you!" },
        new SpeciesDef { key="turtle", eggId=51, attackEveryTicks=5, dmgMult=0.6f, hpMult=2f,
            specialName="Shell Slam", specialMult=2.5f, verb="slams",
            stageNames=new[]{"Shellback Hatchling","Shellback","Colossal Shellback"},
            stageScale=new[]{0.85f,1.4f,2f}, carrySlots=new[]{2,4,6},
            modelNames=new[]{"otherneededassets/beastmastertortoise","otherneededassets/beastmastertortoise","otherneededassets/beastmastertortoise"},
            hatchMsg="The heavy egg splits — a tiny shellback pokes its head out and yawns." },
        new SpeciesDef { key="bird", eggId=52, attackEveryTicks=4, dmgMult=1.6f, hpMult=0.75f,
            specialName="Dive Bomb", specialMult=3f, verb="rakes",
            stageNames=new[]{"Rad-Chick","Razorbeak","Storm Razorbeak"},
            stageScale=new[]{0.7f,1f,1.3f}, carrySlots=new[]{0,0,0},
            modelNames=new[]{"otherneededassets/beastmasterbird","otherneededassets/beastmasterbird","otherneededassets/beastmasterbird"},
            hatchMsg="The speckled egg bursts — a scrappy rad-chick shrieks its first war cry!" },
    };

    // incubation
    float _incubation;
    bool _isIncubating;

    // runtime state
    GameObject _go;
    SpriteRenderer _sr;
    static Sprite _packSprite;
    static bool _customSprite;

    // 3D-model body (e.g. the dog). Falls back to the sprite path when a species has no models.
    bool   _isModel;
    int    _builtStage = -1;       // species/stage the current body was built for (rebuild on change)
    string _builtKey = "";
    static Material _modelMat;      // shared fallback material for untextured .obj models
    const float ModelBaseHeight = 0.7f;   // a stage-1 dog ≈ 0.7 m tall; stageScale grows from here
    public float modelYawOffset = 0f;     // correct a model whose forward isn't +Z (try 90/180/270 if it faces wrong)
    Animator _modelAnimator;              // set when the body is a rigged model with a controller
    bool    _useSpeedParam;               // body runs a controller with a "Speed" blend (the wolf)
    Vector3 _lastModelPos;
    static readonly int _speedHash = Animator.StringToHash("Speed");

    // Models with clips but no controller of ours (the glb bird / tortoise): an idle ↔ move blend
    // played straight onto the Animator, looped by hand since imported clips may not be set to loop.
    PlayableGraph _clipGraph;
    AnimationMixerPlayable _clipMixer;
    AnimationClipPlayable _idlePlayable, _movePlayable;
    bool  _hasMovePlayable;
    float _clipSpeed;
    const float MoveBlendSpeed = 1.5f;    // m/s at which the move clip fully takes over from idle

    // Directional beast art (loaded from Assets/Resources/). Left falls back to flipped right.
    Sprite _pupDown, _pupUp, _pupRight, _pupLeft;
    bool _pupLoaded, _hasPup;
    string _pupFacing = "down";

    bool  _alive;
    int   _hp, _maxHp;
    int   _ticksSinceAttack;
    float _frenzyReadyAt;
    float _regroupAt;

    // Stamina (the Beastmastery downside): drains while the beast is out (faster in
    // combat), regenerates while resting — 2× when you're below half HP. Empty = rest.
    float _stamina = 100f;
    bool  _exhausted;
    const float MaxStamina = 100f;

    // status UI
    GameObject _statusPanel;
    Image _hpFill;
    TMP_Text _statusText;

    public bool IsActive => _alive && _go != null;
    public bool HasSpecialAttack => HasCompanion;
    public string SpecialAttackName => CurrentDef().specialName;
    public float SpecialCooldownRemaining => Mathf.Max(0f, _frenzyReadyAt - Time.time);
    public string SpecialBlockReason
    {
        get
        {
            var player = PlayerEntity.Instance;
            if (player == null || player.Stats == null || player.Stats.IsDead) return "Unavailable while down";
            if (!IsActive) return "Summon your beast [B]";
            if (SpecialCooldownRemaining > 0f) return $"Ready in {Mathf.CeilToInt(SpecialCooldownRemaining)}s";
            var target = AssistTarget();
            if (target == null || target.IsDead) return "Select an enemy";
            if ((_go.transform.position - target.transform.position).sqrMagnitude > (PackReach + 1f) * (PackReach + 1f))
                return "Beast approaching target";
            return null;
        }
    }
    public float StaminaPercent => Mathf.Clamp01(_stamina / MaxStamina);
    public int BeastLevel => Level;

    /// <summary>Razorbeak line perk: the bird spots targets for you — a passive multiplier on YOUR
    /// ranged (Marksmanship) damage while it's out: +10% / +15% / +20% by growth stage.
    /// ActionCombat3D applies this to every ranged hit.</summary>
    public float MarksmanshipDamageMult =>
        IsActive && CurrentDef().key == "bird" ? 1.10f + 0.05f * Stage : 1f;

    int Level => PlayerEntity.Instance != null ? PlayerEntity.Instance.Stats.GetLevel(Skill.Beastmastery) : 1;
    int Stage => Level >= 60 ? 2 : Level >= 30 ? 1 : 0;
    int MaxHpForLevel => Mathf.RoundToInt((15 + Level) * CurrentDef().hpMult);
    int MaxHit        => Mathf.Max(1, Mathf.RoundToInt((1 + Level * 0.2f) * CurrentDef().dmgMult));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("CompanionManager (auto)").AddComponent<CompanionManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // survive scene changes (warp to 3D world etc.)
        BuildStatusUI();
    }

    void OnEnable()  => GameTick.OnTick += OnTick;
    void OnDisable() => GameTick.OnTick -= OnTick;

    bool HasCompanion => PlayerEntity.Instance != null && PlayerEntity.Instance.HasFlag("pup_hatched");

    /// <summary>The species line chosen at the egg (persisted via flags). Wolf default.</summary>
    SpeciesDef CurrentDef()
    {
        var p = PlayerEntity.Instance;
        if (p != null)
            foreach (var d in Defs)
                if (p.HasFlag("species_" + d.key)) return d;
        return Defs[0];   // pre-rework saves (pup, no species flag) behave as the wolf line
    }

    /// <summary>Display name: the growth-stage name for the chosen line.</summary>
    string BeastName() => CurrentDef().stageNames[Stage];

    void Update()
    {
        TickIncubation();
        var owner = PlayerEntity.Instance;
        if (owner == null || owner.Stats == null) return;
        if (owner.Stats.CurrentHP <= 0) { _exhausted=false; _regroupAt=0; Dismiss(null); SyncCarryPerk(); UpdateStatusUI(); return; }

        // RB (Button 5) to Summon
        if ((Input.GetKeyDown(summonKey) || Input.GetKeyDown(KeyCode.JoystickButton5)) && !IsTyping()) Toggle();
        // Y Button (Button 3) for the special
        if ((Input.GetKeyDown(frenzyKey) || (Input.GetKey(KeyCode.JoystickButton4) && Input.GetKeyDown(KeyCode.JoystickButton3))) && !IsTyping()) TriggerFrenzy();
        // Debug: F9 hatches the wolf and cycles its growth stage (pup → adolescent → adult).
        if (Input.GetKeyDown(KeyCode.F9) && !IsTyping()) DebugCycleDog();

        FollowPlayer();
        HandleRegroup();
        UpdateStamina();
        SyncCarryPerk();
        UpdateStatusUI();
    }

    /// <summary>The shellback's saddlebag: extra inventory slots while it's out.</summary>
    void SyncCarryPerk()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;
        int want = IsActive ? CurrentDef().carrySlots[Stage] : 0;
        if (player.Inventory.BonusSlots == want) return;

        bool gained = want > player.Inventory.BonusSlots;
        player.Inventory.BonusSlots = want;
        if (want > 0 && gained) Msg($"Your {BeastName().ToLower()} shoulders your extra gear (+{want} pack slots).");
        else if (want == 0)     Msg("Your saddlebag slots are carried off with your beast.");
    }

    /// <summary>Carrying any companion egg slowly incubates it. When it hatches, your beast arrives.</summary>
    void TickIncubation()
    {
        var player = PlayerEntity.Instance;
        if (player == null || HasCompanion) return;

        var def = IncubatingDef(player);
        if (def == null)
        {
            _incubation = 0f;
            _isIncubating = false;
            return;
        }

        float before = _incubation;
        _incubation += Time.deltaTime;

        if (before < incubateSeconds * 0.5f && _incubation >= incubateSeconds * 0.5f)
            Msg("The egg twitches in your pack...");

        _isIncubating = true;
        if (_incubation >= incubateSeconds)
        {
            player.Inventory.Remove(def.eggId, 1);
            player.SetFlag("pup_hatched");
            player.SetFlag("species_" + def.key);
            _isIncubating = false;
            Msg($"<color=#80FF80>{def.hatchMsg}</color>");
            _stamina = MaxStamina;
            _pupLoaded = false;
            if (_go != null) { Destroy(_go); _go = null; }
            Summon();
        }
    }

    static SpeciesDef IncubatingDef(PlayerEntity player)
    {
        foreach (var d in Defs)
            if (player.Inventory.Contains(d.eggId)) return d;
        return null;
    }

    // ── summon / dismiss ─────────────────────────────────────────────────
    public void Toggle()
    {
        if (_alive) { _exhausted = false; Dismiss("You whistle your beast down."); }
        else Summon();
    }

    public void Summon()
    {
        if (IsActive) return;
        if (Time.time < _regroupAt) { Msg("Your beast is regrouping."); return; }
        var player = PlayerEntity.Instance;
        if (player == null) return;
        if (!HasCompanion)
        {
            Msg(IncubatingDef(player) != null
                ? "Your egg hasn't hatched yet — keep it warm in your pack."
                : "You have no beast companion. Ask Roxy about her Beastmastery lesson.");
            return;
        }
        if (_stamina < 20f)
        {
            Msg("Your beast is too worn out — let it rest a moment.");
            return;
        }

        EnsureBody();
        ApplyBodyScale();
        _maxHp = MaxHpForLevel;
        _hp = _maxHp;
        _alive = true;
        _go.SetActive(true);
        _go.transform.position = FollowGoal(player);
        Msg($"Your {BeastName().ToLower()} falls in at your side!");
        if (CurrentDef().key == "bird")
            Msg($"Its keen eyes mark your targets (+{Mathf.RoundToInt((MarksmanshipDamageMult - 1f) * 100)}% ranged damage).");
    }

    void Dismiss(string message)
    {
        _alive = false;
        if (_go != null) _go.SetActive(false);
        if (!string.IsNullOrEmpty(message)) Msg(message);
    }

    // ── per-tick combat ──────────────────────────────────────────────────
    /// <summary>What the beast should fight: the enemy you're locked onto if any; otherwise the
    /// nearest mob ALREADY ENGAGED with you (you hit it, or it's hunting you), so the pack keeps
    /// fighting while you kite but NEVER starts a fight on its own.</summary>
    static CombatTarget AssistTarget()
    {
        var pe = PlayerEntity.Instance;
        if (pe == null) return null;

        var ac = pe.GetComponent<ActionCombat3D>();
        if (ac != null && ac.Target != null && !ac.Target.IsDead) return ac.Target;

        CombatTarget best = null;
        float bestSq = AssistRange * AssistRange;
        foreach (var t in Object.FindObjectsByType<CombatTarget>(FindObjectsInactive.Exclude))
        {
            if (t.IsDead || t.isDummy) continue;
            var e = t.GetComponent<Enemy3D>();
            if (e == null || !e.IsAggroOnPlayer) continue;   // only join fights already underway
            float d = (t.transform.position - pe.transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = t; }
        }
        return best;
    }

    void OnTick(long tickCount)
    {
        if (!IsActive) return;

        var def = CurrentDef();
        var target = AssistTarget();
        if (target == null || target.IsDead) { _ticksSinceAttack = def.attackEveryTicks; return; }

        // It has to reach the enemy first (it charges in via FollowPlayer) — no biting from across the map.
        if (_go != null)
        {
            Vector3 d = target.transform.position - _go.transform.position; d.y = 0f;
            float bite = PackReach + 0.7f;
            if (d.sqrMagnitude > bite * bite) return;   // still closing the distance
        }

        _ticksSinceAttack++;
        if (_ticksSinceAttack < def.attackEveryTicks) return;
        _ticksSinceAttack = 0;

        Strike(target, MaxHit, "");
    }

    void Strike(CombatTarget target, int maxHit, string prefix)
    {
        int dmg = Mathf.Max(1, Random.Range(1, maxHit + 1));
        target.TakeDamage(dmg);

        // NOTE: the beast's own hits give no XP. Beastmastery is trained by beast-task kills made
        // while it's out with you (see BeastTasks) — by either of you.
        var def = CurrentDef();
        Msg($"{prefix}Your {BeastName().ToLower()} {def.verb} the {target.name} for {dmg}.");

        if (target.IsDead)
            Msg($"Your {BeastName().ToLower()} helps bring down the {target.name}!");
    }

    public void TriggerFrenzy()
    {
        string blocked = SpecialBlockReason;
        if (blocked != null) { Msg(blocked + "."); return; }
        var def = CurrentDef();
        if (!IsActive) { Msg("You have no beast to command."); return; }
        if (Time.time < _frenzyReadyAt)
        {
            Msg($"{def.specialName} not ready ({Mathf.CeilToInt(_frenzyReadyAt - Time.time)}s).");
            return;
        }
        var target = AssistTarget();
        if (target == null || target.IsDead)
        {
            Msg($"Nothing to unleash {def.specialName} on.");
            return;
        }

        if ((_go.transform.position - target.transform.position).sqrMagnitude > (PackReach + 1f) * (PackReach + 1f))
        { Msg("Your beast must reach the target first."); return; }
        _frenzyReadyAt = Time.time + frenzyCooldown;
        Strike(target, Mathf.Max(2, Mathf.RoundToInt(MaxHit * def.specialMult)),
               $"<color=#FF7A3C>{def.specialName.ToUpper()}!</color> ");
    }

    // ── taking damage / recovery ─────────────────────────────────────────

    /// <summary>The shellback shields its owner; nearby companions can be scattered by shared damage.</summary>
    public int AbsorbForPlayer(int damage)
    {
        var player=PlayerEntity.Instance;
        if(damage<=0 || !IsActive || player==null || (_go.transform.position-player.transform.position).sqrMagnitude>64f)
            return damage;
        float fraction=CurrentDef().key=="turtle"?0.35f:0.10f;
        int shared=Mathf.Min(_hp,Mathf.FloorToInt(damage*fraction));
        if(shared>0)TakePackDamage(shared);
        return damage-shared;
    }

    public void TakePackDamage(int amount)
    {
        if (!IsActive) return;
        _hp = Mathf.Max(0, _hp - amount);
        if (_hp <= 0)
        {
            _alive = false;
            if (_go != null) _go.SetActive(false);
            _regroupAt = Time.time + regroupSeconds;
            Msg($"<color=#FF6060>Your {BeastName().ToLower()} is overwhelmed and flees!</color>");
        }
    }

    void HandleRegroup()
    {
        // Auto-regroup after being scattered (only if a body exists, i.e. it was summoned).
        if (_alive || _go == null) return;
        if (_regroupAt <= 0f) return;                 // dismissed on purpose, not scattered
        if (Time.time >= _regroupAt)
        {
            _regroupAt = 0f;
            Summon();
            if (IsActive) Msg($"<color=#80FF80>Your {BeastName().ToLower()} returns, ready to fight again.</color>");
            else _exhausted = true; // too tired to regroup now — returns once rested
        }
    }

    void UpdateStamina()
    {
        var player = PlayerEntity.Instance;
        bool lowHp = player != null && player.Stats.MaxHP > 0 &&
                     player.Stats.CurrentHP <= player.Stats.MaxHP * 0.5f;
        bool fighting = AssistTarget() != null;

        if (IsActive)
        {
            _stamina -= (fighting ? 6f : 1.5f) * Time.deltaTime;
            if (_stamina <= 0f)
            {
                _stamina = 0f;
                _exhausted = true;
                _regroupAt = 0f; // a rest, not an HP-scatter
                Dismiss($"<color=#FF9030>Your {BeastName().ToLower()} is exhausted and slinks off to rest.</color>");
            }
        }
        else
        {
            _stamina = Mathf.Min(MaxStamina, _stamina + 4f * (lowHp ? 2f : 1f) * Time.deltaTime);
            // An exhausted beast returns on its own once rested (manual dismiss stays down).
            if (_exhausted && _stamina >= MaxStamina * 0.8f)
            {
                _exhausted = false;
                Summon();
                if (IsActive) Msg($"<color=#80FF80>Your {BeastName().ToLower()} is rested and rejoins you.</color>");
            }
        }
    }

    // ── visuals ──────────────────────────────────────────────────────────
    Vector3 FollowGoal(PlayerEntity player)
    {
        if (!GameMode.Is3D) return player.transform.position + new Vector3(-0.6f, -0.3f, 0f);
        // 3D: a ground-standing model sits at the player's feet; a billboard sprite floats mid-height.
        float y = _isModel ? 0f : 0.6f;
        return player.transform.position + new Vector3(-0.7f, y, -0.7f);
    }

    // ── combat positioning ────────────────────────────────────────────────
    const float PackReach   = 1.8f;   // how close the beast must be to bite the enemy
    const float AssistRange = 14f;    // how far from you it will go hunt a hostile when you have no locked target

    /// <summary>Where the beast wants to stand while fighting: just within striking distance of the
    /// target, on the side nearest you so it doesn't pile on top of your shot.</summary>
    Vector3 CombatGoal(PlayerEntity player, CombatTarget target)
    {
        Vector3 tp = target.transform.position;
        Vector3 dir = player.transform.position - tp; dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
        Vector3 g = tp + dir * PackReach;
        g.y = tp.y + (_isModel ? 0f : 0.6f);
        return g;
    }

    void FollowPlayer()
    {
        if (!IsActive) return;
        var player = PlayerEntity.Instance;
        if (player == null) return;

        // Grew into the next stage (or species changed)? Swap the body, then carry on.
        if (_builtStage != Stage || _builtKey != CurrentDef().key)
        {
            EnsureBody();
            ApplyBodyScale();
            _go.transform.position = FollowGoal(player);
        }

        Vector3 pos  = _go.transform.position;
        var target = AssistTarget();
        bool fighting = target != null && !target.IsDead;

        // Fighting → charge in and stand within striking distance of the enemy; otherwise heel at your side.
        Vector3 goal = fighting ? CombatGoal(player, target) : FollowGoal(player);
        _go.transform.position = Vector3.Lerp(pos, goal, Time.deltaTime * (fighting ? 8f : 6f));

        // Heading for the sprite frame; face the enemy while fighting.
        Vector3 dir = fighting ? target.transform.position - pos : goal - pos;

        if (_isModel)
        {
            // Drive the locomotion blend (idle/walk/run) from how fast the body is actually moving.
            float speed = (_go.transform.position - _lastModelPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            _lastModelPos = _go.transform.position;
            if (_useSpeedParam && _modelAnimator != null)
                _modelAnimator.SetFloat(_speedHash, speed, 0.12f, Time.deltaTime);
            else UpdateClipAnimation(speed);

            // Face the enemy while fighting; otherwise face the way you're facing.
            Vector3 faceDir = fighting
                ? target.transform.position - _go.transform.position
                : player.transform.forward;
            faceDir.y = 0f;
            if (faceDir.sqrMagnitude > 0.0004f)
            {
                Quaternion want = Quaternion.LookRotation(faceDir) * Quaternion.Euler(0f, modelYawOffset, 0f);
                _go.transform.rotation = Quaternion.Slerp(_go.transform.rotation, want, Time.deltaTime * 8f);
            }
            return;
        }

        // Billboard sprite: face the camera, pick the directional frame from ground motion.
        if (GameMode.Is3D)
        {
            if (Camera.main != null)
                _go.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
            dir = new Vector3(dir.x, dir.z, 0f);
        }
        UpdatePupSprite(dir);
    }

    void UpdatePupSprite(Vector3 dir)
    {
        if (!_hasPup) return; // placeholder blob: nothing to switch

        if (dir.sqrMagnitude > 0.0004f)
        {
            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)) _pupFacing = dir.x > 0 ? "right" : "left";
            else                                      _pupFacing = dir.y > 0 ? "up" : "down";
        }

        bool flip = false;
        Sprite s;
        switch (_pupFacing)
        {
            case "up":    s = _pupUp ?? _pupDown; break;
            case "right": s = _pupRight ?? _pupDown; break;
            case "left":
                if (_pupLeft != null) s = _pupLeft;
                else { s = _pupRight ?? _pupDown; flip = _pupRight != null; }
                break;
            default:      s = _pupDown; break;
        }
        if (s != null) _sr.sprite = s;
        _sr.flipX = flip;
    }

    void EnsureBody()
    {
        var def = CurrentDef();
        // Reuse the current body only if it already matches this species + growth stage.
        if (_go != null && _builtStage == Stage && _builtKey == def.key) return;
        if (_go != null) { Destroy(_go); _go = null; _sr = null; _modelAnimator = null; }
        StopClipAnimation();
        _useSpeedParam = false;

        _go = new GameObject("Companion");
        _builtStage = Stage;
        _builtKey = def.key;

        var modelPrefab = LoadStageModel(def, Stage);
        if (modelPrefab != null)
        {
            _isModel = true;
            // Scale an unanimated parent: imported animation scale tracks must not undo sizing.
            var sizeRoot = new GameObject("ModelSize");
            sizeRoot.transform.SetParent(_go.transform, false);
            var mesh = Instantiate(modelPrefab, sizeRoot.transform);
            mesh.name = "Model";
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
            StripStrayComponents(mesh);   // FBX-embedded Light/Camera would hijack the scene's lighting
            EnsureModelMaterials(mesh);

            // Rigged model with a controller (e.g. the wolf) → animate it instead of sliding.
            // Otherwise play the model's own clips (the glb bird / tortoise carry theirs inside).
            _modelAnimator = mesh.GetComponentInChildren<Animator>();
            var rc = string.IsNullOrEmpty(def.controllerName) ? null
                   : Resources.Load<RuntimeAnimatorController>(def.controllerName);
            if (_modelAnimator != null && rc != null)
            {
                _modelAnimator.applyRootMotion = false;
                _modelAnimator.runtimeAnimatorController = rc;
                _useSpeedParam = true;
                _modelAnimator.Update(0f);
            }
            else StartClipAnimation(mesh, def, Stage);
            NormalizeModelHeight(sizeRoot, 1f);
            _lastModelPos = _go.transform.position;
            return;
        }

        // No 3D model for this species → directional sprite / placeholder blob (2D & fallback).
        _isModel = false;
        LoadPupSprites();
        _sr = _go.AddComponent<SpriteRenderer>();
        _sr.sprite = _hasPup ? (_pupDown ?? _pupRight ?? _pupUp ?? _pupLeft) : GetPackSprite();
        _sr.sortingOrder = 50;
        _sr.color = (_hasPup || _customSprite) ? Color.white : new Color(0.55f, 0.5f, 0.45f, 1f);
    }

    /// <summary>Size the body by growth stage (the "big ass turtle" / alpha-wolf knob).</summary>
    void ApplyBodyScale()
    {
        if (_go == null) return;
        float stage = CurrentDef().stageScale[Stage];
        if (_isModel)
        {
            // Model child is pre-normalized to ~1 m, so just scale the root by stage.
            _go.transform.localScale = Vector3.one * (ModelBaseHeight * stage);
            return;
        }
        if (_sr == null) return;
        float h = _sr.sprite != null ? _sr.sprite.bounds.size.y : 1f;
        float k = h > 0.001f ? 0.95f / h : 0.8f;
        _go.transform.localScale = Vector3.one * (k * stage);
    }

    // ── 3D-model helpers ──────────────────────────────────────────────────
    static GameObject LoadStageModel(SpeciesDef def, int stage)
    {
        if (def.modelNames == null || stage < 0 || stage >= def.modelNames.Length) return null;
        string n = def.modelNames[stage];
        return string.IsNullOrEmpty(n) ? null : Resources.Load<GameObject>(n);
    }

    /// <summary>Animates a model that has clips but no controller of ours: picks an idle and a
    /// move clip by name and blends between them by speed. No clips → the model stays posed.</summary>
    void StartClipAnimation(GameObject mesh, SpeciesDef def, int stage)
    {
        AnimationClip idle = null, move = null, first = null;
        foreach (var c in ModelClips(mesh, def, stage))
        {
            if (c == null || c.legacy || c.length <= 0f) continue;
            string n = c.name.ToLowerInvariant();
            bool isMove = n.Contains("walk") || n.Contains("run") || n.Contains("fly") || n.Contains("move")
                       || n.Contains("swim") || n.Contains("crawl") || n.Contains("trot") || n.Contains("gallop");
            if (isMove) { if (move == null) move = c; }
            else if (n.Contains("idle") || n.Contains("stand") || n.Contains("breath")) { if (idle == null) idle = c; }
            else if (first == null) first = c;
        }
        idle = idle ?? first ?? move;
        if (idle == null) return;   // model has no usable animation
        move = move ?? idle;

        if (_modelAnimator == null) _modelAnimator = mesh.AddComponent<Animator>();
        _modelAnimator.applyRootMotion = false;
        _modelAnimator.runtimeAnimatorController = null;   // our graph drives it, not an importer default
        _modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        _clipGraph = PlayableGraph.Create("CompanionClips");
        _clipGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        _clipMixer = AnimationMixerPlayable.Create(_clipGraph, 2);
        _idlePlayable = AnimationClipPlayable.Create(_clipGraph, idle);
        _clipGraph.Connect(_idlePlayable, 0, _clipMixer, 0);
        _hasMovePlayable = move != idle;
        if (_hasMovePlayable)
        {
            _movePlayable = AnimationClipPlayable.Create(_clipGraph, move);
            _clipGraph.Connect(_movePlayable, 0, _clipMixer, 1);
        }
        _clipMixer.SetInputWeight(0, 1f);
        _clipMixer.SetInputWeight(1, 0f);
        AnimationPlayableOutput.Create(_clipGraph, "Companion", _modelAnimator).SetSourcePlayable(_clipMixer);
        _clipSpeed = 0f;
        _clipGraph.Play();
        _clipGraph.Evaluate(0f);
    }

    /// <summary>The clips a model came with: its importer-made controller's, else the asset's sub-assets.</summary>
    AnimationClip[] ModelClips(GameObject mesh, SpeciesDef def, int stage)
    {
        var anim = mesh.GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            var clips = anim.runtimeAnimatorController.animationClips;
            if (clips != null && clips.Length > 0) return clips;
        }
        return Resources.LoadAll<AnimationClip>(def.modelNames[stage]);
    }

    void UpdateClipAnimation(float speed)
    {
        if (!_clipGraph.IsValid()) return;
        _clipSpeed = Mathf.Lerp(_clipSpeed, speed, Time.deltaTime * 8f);
        if (_hasMovePlayable)
        {
            float w = Mathf.Clamp01(_clipSpeed / MoveBlendSpeed);
            _clipMixer.SetInputWeight(0, 1f - w);
            _clipMixer.SetInputWeight(1, w);
            LoopPlayable(_movePlayable);
        }
        LoopPlayable(_idlePlayable);
    }

    /// <summary>Wrap a clip back to its start so a clip imported without "Loop Time" keeps cycling.</summary>
    static void LoopPlayable(AnimationClipPlayable p)
    {
        if (!p.IsValid()) return;
        float len = p.GetAnimationClip().length;
        double t = p.GetTime();
        if (len > 0f && t >= len) p.SetTime(t % len);
    }

    void StopClipAnimation()
    {
        if (_clipGraph.IsValid()) _clipGraph.Destroy();
        _hasMovePlayable = false;
    }

    void OnDestroy() => StopClipAnimation();

    /// <summary>Imported FBX models often carry the authoring scene's Light/Camera/AudioListener
    /// (wolf.fbx has importLights/importCameras on). Instantiating those injects a stray light/camera
    /// that hijacks the game's lighting and view — the companion "turned the whole map white". Strip
    /// them so the model is nothing but its mesh.</summary>
    static void StripStrayComponents(GameObject mesh)
    {
        foreach (var l in mesh.GetComponentsInChildren<Light>(true))         Destroy(l);
        foreach (var c in mesh.GetComponentsInChildren<Camera>(true))        Destroy(c);
        foreach (var a in mesh.GetComponentsInChildren<AudioListener>(true)) Destroy(a);
    }

    /// <summary>An untextured model renders magenta/white under URP — give it a visible fallback
    /// material. Covers SkinnedMeshRenderer too (the wolf is rigged), not just MeshRenderer.</summary>
    static void EnsureModelMaterials(GameObject mesh)
    {
        foreach (var r in mesh.GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            var m = r.sharedMaterial;
            if (m == null || m.shader == null || m.name.StartsWith("Default-"))
                r.sharedMaterial = ModelMat();
        }
    }

    static Material ModelMat()
    {
        if (_modelMat != null) return _modelMat;
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        bool isURP = sh != null;
        if (sh == null) sh = Shader.Find("Standard");

        _modelMat = new Material(sh) { name = "CompanionFallback" };
        var brown = new Color(0.5f, 0.42f, 0.34f);
        _modelMat.color = brown;
        if (isURP) _modelMat.SetColor("_BaseColor", brown);

        return _modelMat;
    }

    static void NormalizeModelHeight(GameObject go, float wantHeight)
    {
        // Include axis conversion, bone pose and separate body parts. Local mesh Y is not
        // necessarily world up (many imported animals are authored Z-up).
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            Mesh baked = null;
            Mesh mesh;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                baked = new Mesh();
                smr.BakeMesh(baked, true);
                mesh = baked;
            }
            else mesh = r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
            if (mesh == null) continue;
            if (baked != null)
            {
                // A rotated bounding box contains empty space. Measuring its corners can
                // overestimate a quadruped's height several times; use the posed vertices.
                foreach (Vector3 vertex in baked.vertices)
                {
                    float y = r.transform.TransformPoint(vertex).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
                if (Application.isPlaying) Destroy(baked);
                else DestroyImmediate(baked);
                continue;
            }
            Bounds bounds = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float y = r.transform.TransformPoint(corner).y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }
        float h = maxY - minY;
        if (h < 1e-5f || float.IsInfinity(h) || float.IsNaN(h)) return;
        go.transform.localScale *= wantHeight / h;
    }

    /// <summary>Debug (F9): hatch the wolf and step its growth stage so you can see all three models.</summary>
    void DebugCycleDog()
    {
        var p = PlayerEntity.Instance;
        if (p == null) return;
        p.SetFlag("pup_hatched");
        p.SetFlag("species_wolf");

        int lvl = p.Stats.GetLevel(Skill.Beastmastery);
        int target = !IsActive ? Mathf.Max(1, lvl)            // first press: summon at current (usually pup)
                   : lvl < 30 ? 30                            // → adolescent
                   : lvl < 60 ? 60                            // → adult
                   : 1;                                       // → back to pup
        p.Stats.SetXP(Skill.Beastmastery, XPTable.XPForLevel(target));

        _stamina = MaxStamina;
        _builtStage = -1;          // force a body rebuild for the (possibly new) stage
        Summon();
        Msg($"<color=#88DDFF>[DEBUG]</color> Dog at Beastmastery {target} — {CurrentDef().stageNames[Stage]}.");
    }

    /// <summary>Loads directional art for the current species, falling back to "pup" art.</summary>
    void LoadPupSprites()
    {
        if (_pupLoaded) return;
        _pupLoaded = true;

        string species = CurrentDef().key;

        _pupDown  = FirstSprite($"{species} walking forward") ?? FirstSprite($"{species} down") ?? FirstSprite($"{species} forward");
        _pupUp    = FirstSprite($"{species} north")           ?? FirstSprite($"{species} up");
        _pupRight = FirstSprite($"{species} right");
        _pupLeft  = FirstSprite($"{species} left");

        _hasPup = _pupDown != null || _pupUp != null || _pupRight != null || _pupLeft != null;

        // No art for this species yet? Reuse the pup set so SOMETHING walks beside you.
        if (!_hasPup && species != "pup")
        {
            _pupDown  = FirstSprite("pup walking forward") ?? FirstSprite("pup down") ?? FirstSprite("pup forward");
            _pupUp    = FirstSprite("pup north")           ?? FirstSprite("pup up");
            _pupRight = FirstSprite("pup right");
            _pupLeft  = FirstSprite("pup left");
            _hasPup = _pupDown != null || _pupUp != null || _pupRight != null || _pupLeft != null;
        }

        if (_pupDown == null) _pupDown = _pupRight ?? _pupUp ?? _pupLeft; // ensure a default frame
    }

    static Sprite FirstSprite(string resourceName)
    {
        var s = Resources.Load<Sprite>(resourceName);
        if (s != null) return s;
        var all = Resources.LoadAll<Sprite>(resourceName);
        return (all != null && all.Length > 0) ? all[0] : null;
    }

    /// <summary>Placeholder round sprite generated at runtime so the beast is visible
    /// before you assign real art.</summary>
    static Sprite GetPackSprite()
    {
        if (_packSprite != null) return _packSprite;

        // Prefer a real sprite the user dropped at Assets/Resources/pack_pup.png
        // (works whether it's a single sprite or a sliced sheet — we take the first frame).
        var loaded = Resources.Load<Sprite>("pack_pup");
        if (loaded == null)
        {
            var all = Resources.LoadAll<Sprite>("pack_pup");
            if (all != null && all.Length > 0) loaded = all[0];
        }
        if (loaded != null) { _packSprite = loaded; _customSprite = true; return _packSprite; }

        // Fallback: generated placeholder blob.
        const int R = 24;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Vector2 c = new Vector2(R / 2f, R / 2f);
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                tex.SetPixel(x, y, d <= R / 2f - 1 ? Color.white : new Color(0, 0, 0, 0));
            }
        tex.Apply();
        _packSprite = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), R);
        return _packSprite;
    }

    // ── status bar (self-built) ──────────────────────────────────────────
    void BuildStatusUI()
    {
        var canvasGo = new GameObject("CompanionCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 460;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var panel = new GameObject("PackStatus", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
        prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0, 90);
        prt.sizeDelta = new Vector2(320, 46);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.10f, 0.10f, 0.09f, 0.9f);
        panelImg.raycastTarget = true;               // tappable: tap = summon/dismiss, hold = special
        panel.AddComponent<CompanionPanelTap>();
        _statusPanel = panel;

        // HP bar background
        var barBg = new GameObject("HPBg", typeof(RectTransform), typeof(Image));
        barBg.transform.SetParent(prt, false);
        var bgrt = barBg.GetComponent<RectTransform>();
        bgrt.anchorMin = new Vector2(0, 0); bgrt.anchorMax = new Vector2(1, 0);
        bgrt.pivot = new Vector2(0.5f, 0f);
        bgrt.anchoredPosition = new Vector2(0, 6);
        bgrt.sizeDelta = new Vector2(-16, 12);
        var barBgImg = barBg.GetComponent<Image>();
        barBgImg.color = new Color(0.05f, 0.05f, 0.05f, 1f);
        barBgImg.raycastTarget = false;              // let taps fall through to the panel handler

        var fill = new GameObject("HPFill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bgrt, false);
        var frt = fill.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(1, 1);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        _hpFill = fill.GetComponent<Image>();
        _hpFill.raycastTarget = false;
        _hpFill.color = new Color(0.6f, 0.85f, 0.4f, 1f);
        _hpFill.type = Image.Type.Filled;
        _hpFill.fillMethod = Image.FillMethod.Horizontal;
        _hpFill.fillAmount = 1f;

        var txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(prt, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0.4f); trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(8, 0); trt.offsetMax = new Vector2(-8, 0);
        _statusText = txtGo.AddComponent<TextMeshProUGUI>();
        _statusText.fontSize = 15; _statusText.color = UITheme.Text;
        _statusText.alignment = TextAlignmentOptions.Left; _statusText.raycastTarget = false;
        _statusText.textWrappingMode = TextWrappingModes.NoWrap;      // one line — never spills onto the bar
        _statusText.overflowMode = TextOverflowModes.Ellipsis;

        _statusPanel.SetActive(false);
    }

    void UpdateStatusUI()
    {
        if (_statusPanel == null) return;

        // Also show a standing reminder whenever you OWN a beast that simply isn't out right now —
        // it's not in your inventory, so otherwise there's no cue you have a companion at all.
        bool show = IsActive || _regroupAt > 0f || _isIncubating || HasCompanion;
        _statusPanel.SetActive(show);
        if (!show) return;

        if (_isIncubating)
        {
            if (_hpFill != null) _hpFill.fillAmount = _incubation / incubateSeconds;
            if (_statusText != null) _statusText.text = $"EGG INCUBATING... {Mathf.FloorToInt((_incubation/incubateSeconds)*100)}%";
        }
        else if (IsActive)
        {
            var def = CurrentDef();
            if (_hpFill != null) _hpFill.fillAmount = _maxHp > 0 ? (float)_hp / _maxHp : 0f;
            float fcd = Mathf.Max(0, _frenzyReadyAt - Time.time);
            string special = fcd <= 0f
                ? $"<color=#FFD24A>{def.specialName} READY (H / LB+Y / hold)</color>"
                : $"{def.specialName} {Mathf.CeilToInt(fcd)}s";
            if (_statusText != null) _statusText.text = $"{BeastName().ToUpper()}  {_hp}/{_maxHp}   {special}";
        }
        else if (_regroupAt > 0f)
        {
            if (_hpFill != null) _hpFill.fillAmount = 0f;
            if (_statusText != null)
                _statusText.text = $"<color=#FF6060>Beast fled</color> — returns {Mathf.CeilToInt(Mathf.Max(0, _regroupAt - Time.time))}s";
        }
        else   // owned but resting / dismissed — gentle "you have a pet" reminder
        {
            if (_hpFill != null) _hpFill.fillAmount = StaminaPercent;
            if (_statusText != null)
            {
                bool rested = _stamina >= 20f;
                _statusText.text = rested
                    ? $"<color=#9FE0C0>{BeastName().ToUpper()} resting</color> — tap to summon (B)"
                    : $"<color=#FF9030>{BeastName().ToUpper()} worn out</color> — {Mathf.FloorToInt(StaminaPercent * 100)}% rested";
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────
    static void Msg(string m) => HUDController.Emit("<color=#C9A227>[BEAST]:</color> " + m);

    static bool IsTyping() => ChatInput.IsTyping;
}

/// <summary>Makes the bottom pet bar tappable on mobile: tap = summon/dismiss (the B key),
/// hold-tap = trigger the pack special (the H key). On desktop, clicks work the same way.</summary>
public class CompanionPanelTap : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData e)
    {
        var cm = CompanionManager.Instance;
        if (cm == null) return;
        if (e.button == PointerEventData.InputButton.Right)
            cm.TriggerFrenzy();                 // hold-tap on mobile / right-click on desktop
        else if (!TouchInput.SuppressClick)     // ignore the tap that tails a long-press
            cm.Toggle();
    }
}
