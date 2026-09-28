using UnityEngine;

/// <summary>
/// A Hearthcraft AFK station (placed via BuildManager). Runs on the game tick in one of two modes,
/// cycled by clicking it:
///   • Resource — passively produces a raw material into your pack (+ a little XP).
///   • Process  — consumes a material for a bigger chunk of XP (no item out).
/// A station with no produce item is Process-only; one with no consume item is Resource-only.
/// Default mode is Off so a fresh station never surprises you by filling your pack.
/// </summary>
public class AFKStation : MonoBehaviour, IExaminable
{
    public string stationName = "AFK Station";
    public Skill skill;
    public int produceItemId;          // resource-mode output (0 = none)
    public int consumeItemId;          // process-mode input  (0 = none)
    public int ticksPerAction = 5;     // 5 ticks ≈ 3s
    public int resourceXp = 4;         // XP per produced item
    public int processXp = 20;         // XP per consumed item

    public enum Mode { Off, Resource, Process }
    public Mode mode = Mode.Off;

    int _ticks;

    public string DisplayName => stationName;
    public string ExamineText => $"{stationName} — currently {mode}. Walk up and click to switch mode.";

    void OnEnable()  => GameTick.OnTick += OnTick;
    void OnDisable() => GameTick.OnTick -= OnTick;

    void OnTick(long tick)
    {
        if (mode == Mode.Off) return;
        var p = PlayerEntity.Instance;
        if (p == null) return;

        _ticks++;
        if (_ticks < ticksPerAction) return;
        _ticks = 0;

        if (mode == Mode.Resource)
        {
            if (produceItemId <= 0) return;
            if (p.Inventory.IsFull()) { Msg($"{stationName}: your pack is full."); return; }
            p.Inventory.Add(produceItemId);
            p.Stats.AddXP(skill, resourceXp);
        }
        else // Process
        {
            if (consumeItemId <= 0) return;
            if (!p.Inventory.Contains(consumeItemId)) { Msg($"{stationName}: out of materials."); return; }
            p.Inventory.Remove(consumeItemId);
            p.Stats.AddXP(skill, processXp);
        }
    }

    /// <summary>Cycle Off → Resource → Process → Off (skipping modes this station can't do).
    /// Called by the 3D click-to-move / press-E interaction paths (Interactor3D).</summary>
    public void Use()
    {
        bool canProduce = produceItemId > 0;
        bool canProcess = consumeItemId > 0;
        mode = mode switch
        {
            Mode.Off      => canProduce ? Mode.Resource : (canProcess ? Mode.Process : Mode.Off),
            Mode.Resource => canProcess ? Mode.Process : Mode.Off,
            _             => Mode.Off
        };
        Msg($"{stationName} set to <color=#FFD24A>{mode}</color>.");
    }

    static void Msg(string m) => HUDController.Emit("<color=#9FE0C0>[BUILD]:</color> " + m);
}
