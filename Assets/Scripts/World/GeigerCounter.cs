using UnityEngine;

/// <summary>
/// The geiger counter. While the player is carrying a Geiger Counter item, it locks onto the nearest
/// fission golem (living target to kill, or a corpse still worth stripping) and tells you where it is:
/// a bearing, a distance, and a click rate that speeds up the closer you get. Farm one out, and it
/// re-points to the next.
///
/// Self-bootstrapping like the other managers — nothing to place. Readout is IMGUI for now (functional
/// first pass; swap to the project's Canvas/TMP HUD once the loop feels right).
/// </summary>
public class GeigerCounter : MonoBehaviour
{
    const float ScanEvery = 0.4f;     // re-find the nearest target this often (cheap)
    const float MaxTickHz = 8f;       // click rate when almost on top of it
    const float MinTickHz = 0.6f;     // click rate at the edge of detection
    const float DetectRange = 400f;   // how far the counter can sense a core

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("GeigerCounter (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<GeigerCounter>();
    }

    Transform _player;
    PlayerEntity _pe;
    FissionGolem _target;
    float _nextScan;
    float _clickTimer;
    bool _hasCounter;
    float _dist;

    void Update()
    {
        if (_player == null)
        {
            _pe = FindAnyObjectByType<PlayerEntity>();
            if (_pe == null) return;
            _player = _pe.transform;
        }

        // Only works while the geiger is on you.
        _hasCounter = _pe != null && _pe.Inventory != null
            && (_pe.Inventory.Contains(FissionItems.GeigerCounter)
                || _pe.Equipment.GetItemId("Tool") == FissionItems.GeigerCounter);
        if (!_hasCounter) { _target = null; return; }

        if (Time.time >= _nextScan) { _nextScan = Time.time + ScanEvery; _target = Nearest(); }
        if (_target == null) return;

        _dist = Vector3.Distance(_player.position, _target.Position);

        // Click faster the closer you are (audio hook is TODO — this drives the cadence + the HUD dot).
        float prox = 1f - Mathf.Clamp01(_dist / DetectRange);
        float hz = Mathf.Lerp(MinTickHz, MaxTickHz, prox * prox);
        _clickTimer -= Time.deltaTime;
        if (_clickTimer <= 0f)
        {
            _clickTimer = 1f / Mathf.Max(hz, 0.01f);
            // AudioSource.PlayClipAtPoint(clickSfx, _player.position);  // wire an SFX later
        }
    }

    FissionGolem Nearest()
    {
        FissionGolem best = null; float bestSq = DetectRange * DetectRange;
        foreach (var g in FissionGolem.Active)
        {
            if (g == null) continue;
            float sq = (g.Position - _player.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = g; }
        }
        return best;
    }

    void OnGUI()
    {
        if (!_hasCounter) return;

        var style = new GUIStyle(GUI.skin.box)
        { fontSize = 14, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 8, 8) };
        var rect = new Rect(12, Screen.height - 92, 260, 78);

        if (_target == null)
        {
            GUI.Box(rect, "  ☢ GEIGER COUNTER\n  …silent. No cores in range.", style);
            return;
        }

        // Bearing relative to where the camera faces, so "left/right/ahead" makes sense on screen.
        Vector3 to = _target.Position - _player.position; to.y = 0f;
        Vector3 camFwd = Camera.main != null ? Camera.main.transform.forward : Vector3.forward; camFwd.y = 0f;
        float signed = Vector3.SignedAngle(camFwd.normalized, to.normalized, Vector3.up);
        string arrow = Mathf.Abs(signed) < 22f ? "▲ ahead"
                     : signed > 0 ? $"▶ right {Mathf.Abs(signed):F0}°"
                     : $"◀ left {Mathf.Abs(signed):F0}°";
        string state = _target.IsCorpse ? "CORPSE — strip it" : $"T{_target.tier} ELEMENTAL — kill it";
        int bars = Mathf.RoundToInt(Mathf.Clamp01(1f - _dist / DetectRange) * 10f);

        GUI.Box(rect,
            $"  ☢ GEIGER  ·  {state}\n" +
            $"  {arrow}   {_dist:F0} m\n" +
            $"  [{new string('|', bars)}{new string('.', 10 - bars)}]", style);
    }
}
