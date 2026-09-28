using UnityEngine;

/// <summary>
/// Gentle shimmer for a fishing-spot marker — pulses scale and slowly spins so it reads as a
/// rippling spot on the water (OSRS-style). Purely cosmetic; put it on the ripple disc child.
/// </summary>
public class FishingShimmer : MonoBehaviour
{
    public float pulseAmount = 0.15f;
    public float pulseSpeed  = 2.2f;
    public float spinSpeed   = 25f;

    Vector3 _base;

    void Start() => _base = transform.localScale;

    void Update()
    {
        float s = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        transform.localScale = new Vector3(_base.x * s, _base.y, _base.z * s);
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
    }
}
