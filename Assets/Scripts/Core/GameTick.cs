using System;
using System.Collections.Generic;
using UnityEngine;

public class GameTick : MonoBehaviour
{
    public static GameTick Instance { get; private set; }

    public const float TICK_DURATION = 0.6f;

    public static event Action<long> OnTick;

    private float _accumulator;
    private long _tickCount;
    private readonly List<ITickable> _tickables = new();

    void Awake()
    {
        if (Instance != null) { Destroy(this); return; }   // not the object: it carries other managers (see CombatManager)
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        _accumulator += Time.deltaTime;
        while (_accumulator >= TICK_DURATION)
        {
            _accumulator -= TICK_DURATION;
            _tickCount++;
            for (int i = _tickables.Count - 1; i >= 0; i--)
                _tickables[i].OnTick(_tickCount);
            OnTick?.Invoke(_tickCount);
        }
    }

    public float Interpolation => _accumulator / TICK_DURATION;

    /// <summary>The current game-tick count. Lets action code (eat delay, attack
    /// cooldowns) reason about "now" without subscribing to OnTick.</summary>
    public long TickCount => _tickCount;

    public void Register(ITickable t) { if (!_tickables.Contains(t)) _tickables.Add(t); }
    public void Unregister(ITickable t) => _tickables.Remove(t);
}
