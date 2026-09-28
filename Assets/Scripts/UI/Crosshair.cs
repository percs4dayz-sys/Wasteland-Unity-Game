using UnityEngine;

/// <summary>
/// Minimal centre-screen crosshair for the first-person shooter. Auto-spawned, zero scene setup, and
/// only drawn while the camera is in first person (<see cref="OrbitCamera3D.FirstPersonActive"/>).
/// The reticle turns red for a moment whenever the centre ray is on a living enemy, so you get target
/// feedback without any lock-on — pure aim.
/// </summary>
public class Crosshair : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<Crosshair>() != null) return;
        var go = new GameObject("Crosshair (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<Crosshair>();
    }

    static readonly Color C_Idle = new Color(1f, 1f, 1f, 0.75f);
    static readonly Color C_Hot  = new Color(1f, 0.3f, 0.25f, 0.95f);

    Texture2D _tex;
    float _hotUntil;

    void Awake()
    {
        _tex = new Texture2D(1, 1);
        _tex.SetPixel(0, 0, Color.white);
        _tex.Apply();
        _tex.hideFlags = HideFlags.HideAndDontSave;
    }

    void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
    }

    void Update()
    {
        if (!OrbitCamera3D.FirstPersonActive) return;
        var cam = Camera.main;
        if (cam == null) return;

        // Cheap on-target feedback: is a living enemy under the centre ray?
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            var ct = hit.collider.GetComponentInParent<CombatTarget>();
            if (ct != null && !ct.IsDead) _hotUntil = Time.time + 0.06f;
        }
    }

    void OnGUI()
    {
        if (!OrbitCamera3D.FirstPersonActive || _tex == null) return;

        GUI.color = Time.time < _hotUntil ? C_Hot : C_Idle;

        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        const float len = 10f, thick = 2f, gap = 5f;

        GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy - gap - len, thick, len), _tex); // up
        GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy + gap,       thick, len), _tex); // down
        GUI.DrawTexture(new Rect(cx - gap - len, cy - thick * 0.5f, len, thick), _tex); // left
        GUI.DrawTexture(new Rect(cx + gap,       cy - thick * 0.5f, len, thick), _tex); // right
        GUI.DrawTexture(new Rect(cx - 1f, cy - 1f, 2f, 2f), _tex);                       // centre dot

        GUI.color = Color.white;
    }
}
