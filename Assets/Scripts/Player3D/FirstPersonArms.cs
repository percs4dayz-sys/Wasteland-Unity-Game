using UnityEngine;

/// <summary>
/// First-person viewmodel: shows the baked pistol arms (Resources/FPArms/PistolArms — built by
/// Wasteland ▸ Player ▸ Build FP Arms) under the camera while first person is active with a
/// ranged weapon equipped. Fire and Reload animations play off the real combat events
/// (ActionCombat3D.OnAttackPerformed / WeaponMagazine.OnReloadStarted), so the viewmodel can
/// never desync from the actual shots. The body itself is already hidden shadows-only in FP by
/// OrbitCamera3D. Self-added by ActionCombat3D; does nothing until the prefab exists.
/// </summary>
public class FirstPersonArms : MonoBehaviour
{
    [Tooltip("Viewmodel offset from the camera (right, down, forward).")]
    public Vector3 localOffset = new(0.10f, -0.20f, 0.30f);
    public Vector3 localEuler = Vector3.zero;
    public float viewmodelScale = 1f;

    static readonly int FireHash   = Animator.StringToHash("Fire");
    static readonly int ReloadHash = Animator.StringToHash("Reload");

    PlayerEntity _pe;
    ActionCombat3D _combat;
    WeaponMagazine _mag;
    GameObject _instance;
    Animator _anim;
    bool _hasFire, _hasReload;
    bool _prefabMissing;   // don't hammer Resources.Load every frame when it isn't baked yet

    void Start()
    {
        _pe = GetComponent<PlayerEntity>();
        _combat = GetComponent<ActionCombat3D>();
        _mag = GetComponent<WeaponMagazine>();
        if (_combat != null) _combat.OnAttackPerformed += HandleAttack;
        if (_mag != null) _mag.OnReloadStarted += HandleReload;
    }

    void OnDestroy()
    {
        if (_combat != null) _combat.OnAttackPerformed -= HandleAttack;
        if (_mag != null) _mag.OnReloadStarted -= HandleReload;
    }

    bool RangedEquipped()
    {
        var w = _pe != null ? _pe.Equipment.GetItem("Weapon") : null;
        return w != null && w.weaponStyle == WeaponStyle.Ranged;
    }

    void LateUpdate()
    {
        bool want = OrbitCamera3D.FirstPersonActive && RangedEquipped() && Camera.main != null;
        if (want && _instance == null && !_prefabMissing) BuildInstance();
        if (_instance != null && _instance.activeSelf != want) _instance.SetActive(want);

        // Keep the tuning fields live so offset/scale tweaks in the Inspector apply immediately.
        if (_instance != null && want)
        {
            _instance.transform.localPosition = localOffset;
            _instance.transform.localRotation = Quaternion.Euler(localEuler);
            _instance.transform.localScale = Vector3.one * viewmodelScale;
        }
    }

    void BuildInstance()
    {
        var prefab = Resources.Load<GameObject>("FPArms/PistolArms");
        if (prefab == null)
        {
            _prefabMissing = true;   // bake it via Wasteland ▸ Player ▸ Build FP Arms (pistol)
            return;
        }

        // Parented to the camera so it follows with zero lag regardless of script order.
        _instance = Instantiate(prefab, Camera.main.transform);
        _instance.name = "FP Arms";
        // A viewmodel sits closer than the default 0.3 near plane — pull it in so hands don't get
        // sliced open by the clip plane.
        if (Camera.main.nearClipPlane > 0.1f) Camera.main.nearClipPlane = 0.1f;
        _anim = _instance.GetComponentInChildren<Animator>();
        if (_anim != null && _anim.runtimeAnimatorController != null)
        {
            foreach (var p in _anim.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Trigger) continue;
                if (p.nameHash == FireHash) _hasFire = true;
                if (p.nameHash == ReloadHash) _hasReload = true;
            }
        }
    }

    void HandleAttack(bool ranged)
    {
        if (ranged && _hasFire && _instance != null && _instance.activeSelf) _anim.SetTrigger(FireHash);
    }

    void HandleReload()
    {
        if (_hasReload && _instance != null && _instance.activeSelf) _anim.SetTrigger(ReloadHash);
    }
}
