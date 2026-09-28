using UnityEngine;

/// <summary>
/// Tags a mob created by EnemySpawner so the spawner can count/manage its population, and cleans the
/// body up a few seconds after it dies (the spawner then replenishes the population elsewhere).
/// Spawner-owned mobs don't use Enemy3D's home respawn — that's set to effectively never.
/// </summary>
[RequireComponent(typeof(CombatTarget))]
public class SpawnedEnemy : MonoBehaviour
{
    public float corpseSeconds = 3f;

    CombatTarget _ct;

    void Awake()
    {
        _ct = GetComponent<CombatTarget>();
        if (_ct != null) _ct.OnDied += OnDied;
    }

    void OnDestroy()
    {
        if (_ct != null) _ct.OnDied -= OnDied;
    }

    void OnDied() => Destroy(gameObject, corpseSeconds);
}
