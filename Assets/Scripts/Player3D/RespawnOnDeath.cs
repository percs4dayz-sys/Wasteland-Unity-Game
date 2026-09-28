using UnityEngine;

/// <summary>For training dummies: quietly resets HP a few seconds after "death".</summary>
[RequireComponent(typeof(CombatTarget))]
public class RespawnOnDeath : MonoBehaviour
{
    public float delay = 3f;

    CombatTarget _ct;
    float _at;
    bool _dead;

    void Awake()
    {
        _ct = GetComponent<CombatTarget>();
        _ct.OnDied += () => { _dead = true; _at = Time.time + delay; };
    }

    void Update()
    {
        if (_dead && Time.time >= _at) { _dead = false; _ct.Reset(); }
    }
}
