using UnityEngine;

/// <summary>Constrain the existing Enemy3D movement to an authored, grounded boss arena.</summary>
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(CharacterController), typeof(CombatTarget))]
public sealed class BCBossArenaMotion : MonoBehaviour
{
    public float arenaRadius = 14f;
    CharacterController controller;
    CombatTarget target;
    Vector3 home;
    Vector3 previous;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        target = GetComponent<CombatTarget>();
        home = previous = transform.position;
    }

    void LateUpdate()
    {
        if (target.IsDead || !controller.enabled) { previous = transform.position; return; }
        Vector3 wanted = transform.position;
        Vector3 offset = wanted - home;
        offset.y = 0f;
        offset = Vector3.ClampMagnitude(offset, arenaRadius);
        wanted.x = home.x + offset.x;
        wanted.z = home.z + offset.z;
        // Enemy3D computes intent directly; replay it through collision-aware movement.
        controller.enabled = false;
        transform.position = previous;
        controller.enabled = true;
        Vector3 movement = wanted - previous;
        movement.y = -Mathf.Max(.1f, 9.81f * Time.deltaTime);
        controller.Move(movement);
        previous = transform.position;
    }
}
