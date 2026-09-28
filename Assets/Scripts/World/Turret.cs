using UnityEngine;

/// <summary>
/// A Hearthcraft defensive turret (placed via BuildManager). Each few ticks it picks the nearest
/// living hostile within range and fires for a small random hit. It's a static defense — it grants
/// no combat XP and ignores training dummies.
/// </summary>
public class Turret : MonoBehaviour, IExaminable
{
    public string turretName = "Turret";
    public int   maxHit = 3;
    public float range = 8f;
    public int   ticksPerShot = 2;   // 2 ticks ≈ 1.2s

    int _ticks;

    public string DisplayName => turretName;
    public string ExamineText => $"{turretName} — auto-fires on hostiles within {range:0} m.";

    void OnEnable()  => GameTick.OnTick += OnTick;
    void OnDisable() => GameTick.OnTick -= OnTick;

    void OnTick(long tick)
    {
        _ticks++;
        if (_ticks < ticksPerShot) return;
        _ticks = 0;

        var target = Nearest();
        if (target == null) return;

        // Face the target (cosmetic) and fire.
        Vector3 to = target.transform.position - transform.position; to.y = 0f;
        if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);

        int dmg = Mathf.Max(1, Random.Range(1, maxHit + 1));
        target.TakeDamage(dmg);
        CombatFeedbackUI.ShowWorldSplat(target.transform.position, dmg, true);
    }

    CombatTarget Nearest()
    {
        CombatTarget best = null;
        float bestSq = range * range;
        foreach (var t in Object.FindObjectsByType<CombatTarget>(FindObjectsInactive.Exclude))
        {
            if (t.IsDead || t.isDummy) continue;
            float d = (t.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = t; }
        }
        return best;
    }
}
