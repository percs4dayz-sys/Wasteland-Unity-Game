using System;
using UnityEngine;

public class SkillingManager : MonoBehaviour
{
    public static SkillingManager Instance { get; private set; }

    private ResourceNode _activeNode;
    private PlayerEntity _player;
    private float _ticksSinceGather; // retains fractional progress from upgraded tools

    public bool IsGathering => _activeNode != null;
    public string ActiveNodeName => _activeNode != null ? _activeNode.nodeType.ToString() : "";
    public ResourceNode ActiveNode => _activeNode;

    public event Action<string> OnMessage;

    void Awake() => Instance = this;

    void Start()
    {
        _player = PlayerEntity.Instance;
        GameTick.OnTick += Tick;
    }

    void OnDestroy() => GameTick.OnTick -= Tick;

    public void StartGathering(ResourceNode node)
    {
        if (node.IsDepleted) { OnMessage?.Invoke("Nothing left to gather here."); return; }
        _activeNode = node;
        _ticksSinceGather = 0;
        OnMessage?.Invoke("You begin gathering...");
    }

    public void StopGathering() => _activeNode = null;

    private void Tick(long tickCount)
    {
        if (_activeNode == null) return;
        if (_player == null) _player = PlayerEntity.Instance;
        if (_player == null) return;

        if (_activeNode.IsDepleted)
        {
            OnMessage?.Invoke("The node is depleted.");
            StopGathering();
            return;
        }

        _ticksSinceGather += GatheringTools.CycleSpeed(_player, _activeNode.requiredToolId);
        if (_ticksSinceGather < _activeNode.CurrentTicksPerCycle(_player)) return;
        _ticksSinceGather -= _activeNode.CurrentTicksPerCycle(_player);

        var (result, msg) = _activeNode.TryGatherCycle(_player);
        if (result == GatherCycleResult.Stop)
        {
            if (msg != null) OnMessage?.Invoke(msg);
            StopGathering();
            return;
        }

        if (msg != null) OnMessage?.Invoke(msg);   // Caught — NoCatch stays silent (no chat spam)

        if (_activeNode != null && _activeNode.IsDepleted)
            StopGathering();
    }
}
