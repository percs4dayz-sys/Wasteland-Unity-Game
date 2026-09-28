using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hands the player the survivor's journal pages (JournalLore) as they progress: entry 1 on
/// reaching the mainland, entries 2 &amp; 5 at their total-level thresholds. Self-building and
/// session-persistent (DontDestroyOnLoad); each page is granted once, tracked by a player flag.
/// </summary>
public class JournalManager : MonoBehaviour
{
    public static JournalManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("JournalManager (auto)").AddComponent<JournalManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    float _nextCheck;

    void Update()
    {
        if (Time.time < _nextCheck) return;   // a light cadence is plenty for level/scene gates
        _nextCheck = Time.time + 1f;

        var p = PlayerEntity.Instance;
        if (p == null) return;

        bool onMainland = SceneManager.GetActiveScene().name.StartsWith("MainWorld");
        int total = p.Stats.TotalLevel();

        foreach (var e in JournalLore.All)
        {
            string flag = "journal_" + e.id;
            if (p.HasFlag(flag)) continue;

            bool due = (e.onMainlandArrival && onMainland) || (e.totalLevelReq > 0 && total >= e.totalLevelReq);
            if (!due) continue;

            if (p.Inventory.IsFull()) continue;        // no room — try again next tick
            if (!p.Inventory.Add(e.id)) continue;
            p.SetFlag(flag);
            HUDController.Emit("<color=#C9A227>[JOURNAL]:</color> A weathered journal page is in your pack — " +
                               $"<color=#FFD24A>{e.itemName}</color>. Click it in your inventory to read.");
        }
    }
}
