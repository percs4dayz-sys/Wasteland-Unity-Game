using UnityEngine;

/// <summary>
/// A door or gate leaf that swings open as the player walks up and closes again once they've gone.
/// The imported buildings ship every door shut, which walled off each house. This keeps the look
/// without the block. It swings away from the player, so a door never opens into their face.
///
/// The hinge comes from the mesh: whichever end of the leaf sits near its pivot (Synty doors pivot on
/// the hinge), else one end. The leaf's colliders are off whenever it isn't fully shut, so a moving door
/// can never shove the player. The navmesh bake ignores door leaves, so routes go through doorways.
///
/// Added to door leaves by Wasteland ▸ Broken Crescent ▸ Repair Doorways (BCDoorwayRepair).
/// </summary>
[DisallowMultipleComponent]
public class SwingDoor : MonoBehaviour
{
    public float openDistance = 3.4f;    // start swinging when the player is this close to the doorway
    public float closeDistance = 7f;     // swing shut once they're this far away...
    public float closeDelay = 2f;        // ...for this long
    public float swingSeconds = 0.35f;
    public float openAngle = 95f;

    Vector3 _closedPos, _hinge, _mid, _normal, _free;   // world space, captured in the closed pose
    Quaternion _closedRot;
    Collider[] _cols;
    float _t, _sign = 1f, _farSince = -1f, _nextCheck;
    bool _wantOpen, _ready;

    void Start() => Capture();

    void Capture()
    {
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { enabled = false; return; }
        _cols = GetComponentsInChildren<Collider>();
        _closedPos = transform.position;
        _closedRot = transform.rotation;

        // The leaf's box in its own frame: the longer horizontal axis is its width.
        var toLocal = transform.worldToLocalMatrix;
        Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
        foreach (var r in rs)
        {
            var b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = toLocal.MultiplyPoint3x4(c);
                mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
            }
        }
        Vector3 centre = (mn + mx) * 0.5f, ext = (mx - mn) * 0.5f;
        bool alongX = ext.x >= ext.z;
        Vector3 axis = alongX ? Vector3.right : Vector3.forward;
        float half = alongX ? ext.x : ext.z;
        float pivotOnAxis = alongX ? -centre.x : -centre.z;             // where the pivot sits along the width
        float hingeSide = Mathf.Abs(pivotOnAxis) > half * 0.25f ? Mathf.Sign(pivotOnAxis) : -1f;

        Vector3 hingeLocal = centre + axis * (hingeSide * half);
        Vector3 freeLocal = centre - axis * (hingeSide * half);
        _hinge = transform.TransformPoint(hingeLocal);
        _free = transform.TransformPoint(freeLocal);
        _mid = transform.TransformPoint(centre);
        _normal = transform.TransformDirection(alongX ? Vector3.forward : Vector3.right);
        _normal.y = 0f; _normal.Normalize();
        _ready = true;
    }

    void Update()
    {
        if (!_ready) return;
        var player = PlayerEntity.Instance;
        if (player != null && Time.time >= _nextCheck)
        {
            _nextCheck = Time.time + 0.1f;
            Vector3 d = player.transform.position - _mid; d.y = 0f;
            float dist = d.magnitude;
            if (dist <= openDistance)
            {
                if (!_wantOpen && _t <= 0.001f)
                {
                    // Swing so the free end moves away from the side the player is on.
                    float side = Mathf.Sign(Vector3.Dot(d, _normal));
                    Vector3 arm = _free - _hinge; arm.y = 0f;
                    Vector3 swung = Quaternion.AngleAxis(90f, Vector3.up) * arm;
                    _sign = Vector3.Dot(swung, _normal) * side > 0f ? -1f : 1f;
                }
                _wantOpen = true;
                _farSince = -1f;
            }
            else if (dist >= closeDistance)
            {
                if (_farSince < 0f) _farSince = Time.time;
                else if (Time.time - _farSince >= closeDelay) _wantOpen = false;
            }
            else _farSince = -1f;
        }

        float target = _wantOpen ? 1f : 0f;
        if (Mathf.Approximately(_t, target)) return;
        _t = Mathf.MoveTowards(_t, target, Time.deltaTime / Mathf.Max(0.05f, swingSeconds));
        float angle = _sign * openAngle * Mathf.SmoothStep(0f, 1f, _t);
        var rot = Quaternion.AngleAxis(angle, Vector3.up);
        transform.SetPositionAndRotation(_hinge + rot * (_closedPos - _hinge), rot * _closedRot);
        SetSolid(_t <= 0.001f);
    }

    void SetSolid(bool on)
    {
        if (_cols == null) return;
        foreach (var c in _cols) if (c != null && c.enabled != on) c.enabled = on;
    }
}
