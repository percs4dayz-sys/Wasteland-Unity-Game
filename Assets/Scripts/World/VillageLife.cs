using UnityEngine;
using UnityEngine.AI;
using TMPro;

/// <summary>
/// A resident of the south-west harbor village. Wanders, patrols, sits by the fire or runs about
/// depending on <see cref="role"/>, mutters ambient chatter over their head when you're near, and
/// talks when clicked. Residents who run part of a small quest (see SideQuests) give that quest's
/// dialogue first. Walks on the NavMesh when there is one under them; otherwise they just fidget in place.
/// Built by <see cref="VillageLife.Build"/>.
/// </summary>
public class VillagerLife : MonoBehaviour, ITalkableNPC
{
    public enum Role { Wander, Patrol, Fireside, Kid, Drunk }

    public string npcName, examine, questId;
    public Role role;
    public string[] lines, chatter, afterDoneLines;
    public Vector3 center, firePos;
    public float radius = 35f;

    public string DisplayName => npcName;
    public string ExamineText => examine;

    NavMeshAgent _agent;
    Transform _visual;
    TextMeshPro _bubble;
    float _idleUntil, _nextChatter, _bubbleUntil, _pauseUntil, _phase;
    int _patrolIndex;
    bool _walking;

    void Start()
    {
        _visual = transform.Find("Visual");
        _phase = Random.value * 10f;
        _idleUntil = Time.time + Random.Range(0.5f, 4f);
        _nextChatter = Time.time + Random.Range(5f, 20f);

        if (role != Role.Fireside && NavMesh.SamplePosition(transform.position, out var hit, 4f, NavMesh.AllAreas))
        {
            _agent = gameObject.AddComponent<NavMeshAgent>();
            _agent.radius = 0.4f; _agent.height = 1.9f;
            _agent.acceleration = 10f; _agent.angularSpeed = 280f; _agent.stoppingDistance = 0.3f;
            _agent.speed = role == Role.Kid ? 3.2f : role == Role.Drunk ? 0.9f : role == Role.Patrol ? 1.7f : 1.4f;
            _agent.Warp(hit.position);
        }
        if (role == Role.Fireside) FaceFire();
    }

    void Update()
    {
        Animate();
        Chatter();
        if (role == Role.Fireside || Time.time < _pauseUntil) return;

        if (_agent == null)
        {
            // No navmesh here: just look around now and then so they don't read as statues.
            if (Time.time >= _idleUntil)
            {
                transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                _idleUntil = Time.time + Random.Range(3f, 8f);
            }
            return;
        }

        if (_walking)
        {
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.15f)
            {
                _walking = false;
                _idleUntil = Time.time + (role == Role.Kid ? Random.Range(0.5f, 2f) : Random.Range(3f, 9f));
            }
        }
        else if (Time.time >= _idleUntil)
        {
            if (PickDestination(out var dest)) { _agent.SetDestination(dest); _walking = true; }
            else _idleUntil = Time.time + 3f;
        }
    }

    bool PickDestination(out Vector3 dest)
    {
        Vector3 target;
        if (role == Role.Patrol)
        {
            float a = (_patrolIndex++ % 6) / 6f * Mathf.PI * 2f;
            target = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.75f;
        }
        else
        {
            var r = Random.insideUnitCircle * radius * (role == Role.Drunk ? 0.4f : 0.85f);
            target = (Random.value < 0.25f ? firePos : center) + new Vector3(r.x, 0f, r.y);
        }
        if (NavMesh.SamplePosition(target, out var hit, 6f, NavMesh.AllAreas)) { dest = hit.position; return true; }
        dest = default;
        return false;
    }

    void Animate()
    {
        if (_visual == null) return;
        float t = Time.time + _phase;
        if (role == Role.Fireside)
        {
            // Sitting by the fire: lowered and squashed a little, breathing slowly.
            _visual.localPosition = new Vector3(0f, -0.35f + Mathf.Sin(t * 1.4f) * 0.01f, 0f);
            _visual.localScale = new Vector3(1f, 0.75f, 1f);
            return;
        }
        bool moving = _agent != null && _walking && _agent.velocity.sqrMagnitude > 0.1f;
        float bob = moving ? Mathf.Abs(Mathf.Sin(t * (role == Role.Kid ? 12f : 8f))) * 0.09f : Mathf.Sin(t * 1.6f) * 0.012f;
        float sway = role == Role.Drunk ? Mathf.Sin(t * 2.2f) * (moving ? 9f : 4f) : (moving ? Mathf.Sin(t * 8f) * 2.5f : 0f);
        _visual.localPosition = new Vector3(0f, bob, 0f);
        _visual.localRotation = Quaternion.Euler(0f, 0f, sway);
    }

    void FaceFire()
    {
        Vector3 d = firePos - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
    }

    // ── chatter ──────────────────────────────────────────────────────────────
    void Chatter()
    {
        var cam = Camera.main;
        if (_bubble != null)
        {
            if (Time.time >= _bubbleUntil) _bubble.gameObject.SetActive(false);
            else if (cam != null) _bubble.transform.rotation = cam.transform.rotation;
        }
        if (Time.time < _nextChatter || chatter == null || chatter.Length == 0) return;
        _nextChatter = Time.time + Random.Range(12f, 28f);

        var p = PlayerEntity.Instance;
        if (p == null || (p.transform.position - transform.position).sqrMagnitude > 28f * 28f) return;   // only when heard

        if (_bubble == null)
        {
            var go = new GameObject("Bubble");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            _bubble = go.AddComponent<TextMeshPro>();
            _bubble.fontSize = 2.2f;
            _bubble.alignment = TextAlignmentOptions.Center;
            _bubble.textWrappingMode = TextWrappingModes.Normal;
            _bubble.rectTransform.sizeDelta = new Vector2(5f, 2f);
            _bubble.outlineWidth = 0.2f;
            _bubble.outlineColor = Color.black;
            _bubble.color = new Color(1f, 0.95f, 0.8f);
        }
        _bubble.text = chatter[Random.Range(0, chatter.Length)];
        _bubble.gameObject.SetActive(true);
        _bubbleUntil = Time.time + 4.5f;
    }

    // ── talking ──────────────────────────────────────────────────────────────
    public void Interact()
    {
        var p = PlayerEntity.Instance;
        if (p != null)
        {
            Vector3 d = p.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d);
        }
        _pauseUntil = Time.time + 8f;          // stop walking while you're talking to them
        if (_agent != null && _agent.isOnNavMesh) { _agent.ResetPath(); _walking = false; }
        _idleUntil = Time.time + 8f;

        if (SideQuests.TryTalk(npcName, transform.position)) return;
        var dlg = DialogueUI.Instance;
        if (dlg == null) return;
        var pool = !string.IsNullOrEmpty(questId) && p != null && SideQuests.IsDone(p, questId) && afterDoneLines != null && afterDoneLines.Length > 0
            ? afterDoneLines : lines;
        dlg.StartDialogue(npcName, pool[Random.Range(0, pool.Length)]);
    }
}

/// <summary>Flickers a point light and pulses the flame mesh of the village campfire.</summary>
public class CampfireFlicker : MonoBehaviour
{
    public Light fireLight;
    public Transform flame;
    float _seed;

    void Start() => _seed = Random.value * 100f;

    void Update()
    {
        float n = Mathf.PerlinNoise(Time.time * 6f, _seed);
        if (fireLight != null) fireLight.intensity = Mathf.Lerp(1.6f, 3.4f, n);
        if (flame != null) flame.localScale = new Vector3(0.5f + n * 0.15f, 0.7f + n * 0.5f, 0.5f + n * 0.15f);
    }
}

public static class VillageLife
{
    struct Resident
    {
        public string name, examine, quest;
        public VillagerLife.Role role;
        public Color color;
        public string[] lines, chatter, after;
    }

    static readonly Resident[] Roster =
    {
        new Resident { name = "Guard Bex", role = VillagerLife.Role.Patrol, color = new Color(0.3f, 0.35f, 0.45f),
            examine = "Walks the harbor perimeter like it owes them money.",
            lines = new[] { "Nothing's got past me yet. Well. A raccoon. Twice.", "Keep your weapon holstered inside the harbor and we'll get along fine." },
            chatter = new[] { "All quiet.", "Something's moving out past the ridge...", "Still quiet. Suspiciously quiet." } },
        new Resident { name = "Scavenger Nell", role = VillagerLife.Role.Wander, color = new Color(0.55f, 0.4f, 0.3f),
            examine = "Pockets bulging with other people's junk.",
            lines = new[] { "Found a shoe. Just the one. Still thinking about it.", "Everything's worth something if you're desperate enough." },
            chatter = new[] { "Where did I put that spanner?", "Ooh, a bottle cap.", "Mine. That's mine now." } },
        new Resident { name = "Kid Pip", role = VillagerLife.Role.Kid, color = new Color(0.85f, 0.55f, 0.2f),
            examine = "Small, fast, and covered in something you don't want to identify.",
            lines = new[] { "I saw a giant rabbit! Nobody believes me!", "Race you to the fire! ...I already won." },
            chatter = new[] { "Tag!", "I'm not lost, I'm exploring!", "Bet you can't catch me!" } },
        new Resident { name = "Old Marta", role = VillagerLife.Role.Fireside, color = new Color(0.6f, 0.45f, 0.5f), quest = "water",
            examine = "Has sat by this fire longer than the fire has been lit.",
            lines = new[] { "Sit, sit. The fire's free. The water isn't worth drinking.", "In my day the wasteland was quieter. And worse." },
            after = new[] { "Clean water. I'm going to cry. Don't watch.", "Do you know how good nothing tastes?" },
            chatter = new[] { "Hmph.", "Back in my day, the water tasted like water.", "Cold tonight." } },
        new Resident { name = "Tinker Voss", role = VillagerLife.Role.Wander, color = new Color(0.4f, 0.5f, 0.55f), quest = "water",
            examine = "Covered in grease. Holding at least three tools at any time.",
            lines = new[] { "I'm working on something big. Or small. Depends on what's left in the box.", "Everything can be fixed. Some things just take longer." },
            after = new[] { "The filter's holding. I'm as surprised as you are.", "Another job? Give me a minute, I'm still celebrating." },
            chatter = new[] { "Hand me that... no, the other one.", "That should hold. Probably.", "Why is it warm?" } },
        new Resident { name = "Drunk Dell", role = VillagerLife.Role.Drunk, color = new Color(0.5f, 0.3f, 0.3f),
            examine = "Smells like a distillery that lost an argument.",
            lines = new[] { "I'm not drunk. The ground's just... leaning.", "You look like someone who'd buy me a drink. Or just... stand nearby." },
            chatter = new[] { "*hic*", "The floor moved.", "Who put a wall there?" } },
    };

    /// <summary>Campfire, and the residents. Everything is parented under <paramref name="root"/>.</summary>
    public static void Build(Transform root, Vector3 center, float radius)
    {
        Vector3 fire = BuildCampfire(root, center);

        foreach (var r in Roster)
        {
            var go = SideQuestActors.BuildPerson(r.name, r.color, r.role == VillagerLife.Role.Kid ? 1.3f : 1.9f);
            go.transform.SetParent(root, true);
            Vector3 pos = r.role == VillagerLife.Role.Fireside
                ? WorldAnchors.Ground(fire + new Vector3(2.2f, 0f, 0.6f))
                : WorldAnchors.VillageSpot(center, radius, r.name, 0.15f, 0.7f);
            go.transform.position = pos;

            var v = go.AddComponent<VillagerLife>();
            v.npcName = r.name; v.examine = r.examine; v.role = r.role; v.questId = r.quest;
            v.lines = r.lines; v.chatter = r.chatter; v.afterDoneLines = r.after;
            v.center = center; v.firePos = fire; v.radius = radius;
        }
    }

    static Vector3 BuildCampfire(Transform root, Vector3 center)
    {
        Vector3 pos = WorldAnchors.Ground(center + new Vector3(4f, 0f, -3f));
        var fire = new GameObject("Campfire");
        fire.transform.SetParent(root, true);
        fire.transform.position = pos;

        // A ring of logs, a glowing flame, and a light that flickers.
        for (int i = 0; i < 5; i++)
        {
            var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            log.name = "Log";
            Object.Destroy(log.GetComponent<Collider>());
            log.transform.SetParent(fire.transform, false);
            float a = i / 5f * Mathf.PI * 2f;
            log.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, 0.12f, Mathf.Sin(a) * 0.45f);
            log.transform.localRotation = Quaternion.Euler(80f, -a * Mathf.Rad2Deg, 0f);
            log.transform.localScale = new Vector3(0.14f, 0.5f, 0.14f);
            log.GetComponent<Renderer>().material.color = new Color(0.25f, 0.15f, 0.08f);
        }

        var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flame.name = "Flame";
        Object.Destroy(flame.GetComponent<Collider>());
        flame.transform.SetParent(fire.transform, false);
        flame.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        var rend = flame.GetComponent<Renderer>();
        rend.material.color = new Color(1f, 0.55f, 0.1f);
        rend.material.EnableKeyword("_EMISSION");
        rend.material.SetColor("_EmissionColor", new Color(2.5f, 1.0f, 0.15f));
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(fire.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.25f);
        light.range = 14f;
        light.intensity = 2.5f;

        var src = fire.AddComponent<AudioSource>();
        src.clip = ProceduralAudio.Crackle();
        src.loop = true;
        src.spatialBlend = 1f;
        src.minDistance = 2f; src.maxDistance = 22f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.volume = 0.5f;
        src.Play();

        var fl = fire.AddComponent<CampfireFlicker>();
        fl.fireLight = light; fl.flame = flame.transform;
        return pos;
    }
}
