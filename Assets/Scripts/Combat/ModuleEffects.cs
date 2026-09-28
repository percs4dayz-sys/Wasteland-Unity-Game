using UnityEngine;
using System.Collections;
using E = ModuleCatalog.Effect;

/// <summary>Per-player module combat state. Proc damage never recursively procs more modules.</summary>
public class ModuleEffects : MonoBehaviour
{
    PlayerEntity _player;
    CombatTarget _target;
    int _chain, _misses, _charge;
    float _lastHit, _reactiveUntil, _killUntil, _servoUntil, _lastIncoming = -100f;
    float _shield, _lastRegen, _nextPickup;
    Equipment _bound;
    sealed class BleedState { public int damage, stacks = 1, ticks = 3; }
    readonly System.Collections.Generic.Dictionary<CombatTarget, BleedState> _bleeds = new();
    string _gearSignature;

    public static ModuleEffects For(PlayerEntity p)
    {
        if (p == null) return null;
        var fx=p.GetComponent<ModuleEffects>();
        if(fx == null) fx=p.gameObject.AddComponent<ModuleEffects>();
        fx._player=p;
        return fx;
    }
    int P(E e) => ModuleCatalog.Power(_player?.Equipment,e);
    void Update()
    {
        if (_player == null) _player = GetComponent<PlayerEntity>();
        if (_player == null || _player.Stats == null) return;
        if (_bound != _player.Equipment) {
            if (_bound != null) _bound.OnChanged -= GearChanged;
            _bound = _player.Equipment; _bound.OnChanged += GearChanged;
        }
        Regenerate();
        if (_player.Stats.CurrentHP <= 0) { ResetCombat(); return; }
        int magnet=P(E.Magnet);
        if(magnet == 0 || Time.time < _nextPickup) return;
        _nextPickup=Time.time+0.4f;
        // Copy: pickup destroys a drop at end of frame, modifying the registry.
        foreach(var loot in GroundItem.All.ToArray())
            if(loot != null && ItemRegistry.Get(loot.itemId)?.type == ItemType.Resource &&
                (loot.transform.position-transform.position).sqrMagnitude <= 4f*magnet*magnet)
                loot.TryPickUp(silentFull:true);
    }
    void OnDestroy() { if (_bound != null) _bound.OnChanged -= GearChanged; }
    void GearChanged()
    {
        string signature="";
        foreach(var slot in Equipment.Slots)
            if(slot!="Ammo" && slot!="Tool") signature+=slot+_bound.GetItemId(slot)+":"+string.Join(",",_bound.GetModules(slot))+";";
        if(signature==_gearSignature)return; // Spending ammo is also an equipment change.
        _gearSignature=signature;_shield=Mathf.Min(_shield,ShieldCapacity());ResetCombat();
    }
    void ResetCombat() { _chain=0;_misses=0;_charge=0;_target=null;_reactiveUntil=0;_servoUntil=0;_killUntil=0; }
    void Track(CombatTarget target)
    {
        float window=P(E.Combo)>=2 ? 10f : 6f;
        if(target != _target || Time.time-_lastHit>window) {_chain=0;_charge=0;_misses=0;_target=target;}
    }
    public int Accuracy(CombatTarget target, WeaponStyle style)
    {
        Track(target);
        return _misses*5*P(E.MissAdapt)+(style==WeaponStyle.Ranged ? 4*P(E.Targeting):0);
    }
    public int Defence() => Time.time < _reactiveUntil ? 8*P(E.Reactive) : 0;
    public int TargetDefence(CombatTarget target)
    {
        Track(target);
        return Mathf.Max(0,Mathf.RoundToInt(target.defenceLevel*(1f-Mathf.Min(0.4f,_chain*0.02f*P(E.Resonance)))));
    }
    public void Miss(CombatTarget target) { Track(target); _misses=Mathf.Min(3,_misses+1); _chain=0; _lastHit=Time.time; }
    public float CooldownMultiplier()
    {
        float reduction=0.03f*P(E.Processor)+0.03f*P(E.Recovery)*(Time.time<_killUntil?2:1);
        reduction+=Mathf.Min(_chain,5)*(0.02f*P(E.Overclock)+(P(E.Adaptive)>=2?0.03f:P(E.Adaptive)*0.02f));
        if(Time.time<_servoUntil && P(E.Servo)>0) reduction+=0.08f;
        int loose=P(E.HasteProc);
        if(loose>0 && Random.value < 0.05f+0.05f*loose) reduction+=0.20f;
        return Mathf.Clamp(1f-reduction,0.5f,1f);
    }
    public int EatDelay(int ticks) => Mathf.Max(1,ticks-P(E.EatRecovery));
    public int Outgoing(CombatTarget target, int damage, WeaponStyle style)
    {
        if(damage<=0) return damage;
        Track(target);
        int priorMisses=_misses;
        _misses=0;_chain=Mathf.Min(8,_chain+1);_lastHit=Time.time;
        int capped=Mathf.Min(5,_chain), combo=P(E.Combo);
        float bonus=0.04f*P(E.Power)+0.01f*P(E.Scanner)*capped;
        if(combo>0) bonus+=(combo>=2?0.03f:0.02f)*Mathf.Min(_chain,combo>=2?8:5);
        if(P(E.Adaptive)>=2) bonus+=0.02f*capped;
        if(P(E.Tactical)>=2) bonus+=0.01f*capped;
        if(P(E.MissAdapt)>=2) bonus+=0.03f*priorMisses;
        if(P(E.Reactive)>=2 && Time.time<_reactiveUntil) bonus+=0.05f;
        if(P(E.Emergency)>=2 && _player.Stats.CurrentHP < _player.Stats.MaxHP*0.3f) bonus+=0.1f;
        if(Random.value < 0.05f*P(E.Critical)) bonus+=0.5f;
        if(style==WeaponStyle.Ranged && P(E.Targeting)>=2 && Random.value<0.05f) bonus+=0.5f;
        if(Random.value < 0.05f*P(E.DoubleStrike)) bonus+=0.4f;
        if(Random.value < 0.08f*P(E.Burst)) bonus+=0.5f;
        int cap=P(E.Capacitor);
        if(cap>0 && ++_charge >= (cap>=2?3:5)) {_charge=0;bonus+=0.3f;}
        int result=Mathf.Max(1,Mathf.RoundToInt(damage*(1f+bonus)));
        int actual=Mathf.Min(result,target.CurrentHP);
        if(P(E.Leech)>0 && actual>0) _player.Stats.Heal(Mathf.Max(1,Mathf.FloorToInt(actual*0.03f*P(E.Leech))));
        int serrated=P(E.Serrated);
        if(Random.value < 0.05f*P(E.Bleed)+0.10f*serrated)
            AddBleed(target,Mathf.Max(1,Mathf.RoundToInt(damage*0.10f)),serrated>=2?2:1);
        if(P(E.Servo)>0) _servoUntil=Time.time+(P(E.Servo)>=2?6f:3f);
        if(result>=target.CurrentHP) _killUntil=Time.time+5f;
        return result;
    }
    void AddBleed(CombatTarget target,int damage,int maxStacks)
    {
        if (_bleeds.TryGetValue(target,out var current)) {
            current.damage=Mathf.Max(current.damage,damage);current.stacks=Mathf.Min(maxStacks,current.stacks+1);current.ticks=3;
            return;
        }
        var state=new BleedState {damage=damage};_bleeds[target]=state;
        StartCoroutine(Bleed(target,state));
    }
    IEnumerator Bleed(CombatTarget target,BleedState state)
    {
        while(state.ticks>0) {
            yield return new WaitForSeconds(GameTick.TICK_DURATION);
            if(target==null || target.IsDead || !target.gameObject.activeInHierarchy) break;
            target.TakeDamage(state.damage*state.stacks);state.ticks--;
        }
        _bleeds.Remove(target);
    }
    float ShieldCapacity() => 6*P(E.Barrier)+10*P(E.Nano);
    void Regenerate()
    {
        float elapsed=Mathf.Max(0,Time.time-_lastRegen);
        _lastRegen=Time.time;
        _shield=Mathf.Min(ShieldCapacity(),_shield+elapsed*P(E.Nano));
        int barrier=P(E.Barrier);
        if(barrier>0 && Time.time-_lastIncoming >= (barrier>=2?8f:12f))
            _shield=Mathf.Max(_shield,6*barrier);
    }
    public int Incoming(int damage,CombatTarget attacker,bool ranged)
    {
        if(damage<=0)return 0;
        Regenerate();
        _lastIncoming=Time.time;
        int original=damage;
        if(Random.value<0.05f*P(E.Block)) {
            if(P(E.Block)>=2 && attacker!=null && !attacker.IsDead) attacker.TakeDamage(Mathf.Max(1,original/4));
            return 0;
        }
        if(ranged && P(E.Ballistic)>=2 && Random.value<0.05f)return 0;
        float reduction=0.03f*P(E.Plates);
        if(damage>=10) reduction+=0.10f*P(E.HeavyGuard);
        if(ranged) reduction+=0.10f*P(E.Ballistic);
        if(_player.Stats.CurrentHP < _player.Stats.MaxHP*0.3f) reduction+=0.10f*P(E.Emergency);
        damage=Mathf.Max(1,Mathf.RoundToInt(damage*(1f-Mathf.Min(0.6f,reduction))));
        int absorbed=Mathf.Min(damage,Mathf.FloorToInt(_shield));
        _shield-=absorbed;damage-=absorbed;
        _reactiveUntil=Time.time+4f;
        if(attacker!=null && !attacker.IsDead) {
            int reflect=P(E.Reflect)>0?Mathf.Max(1,Mathf.FloorToInt(original*0.05f*P(E.Reflect))):0;
            if(Random.value<0.08f*P(E.Deflect))reflect+=Mathf.Max(1,original/2);
            if(reflect>0)attacker.TakeDamage(reflect);
        }
        return damage;
    }
}

