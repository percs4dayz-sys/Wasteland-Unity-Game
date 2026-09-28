using UnityEngine;

public class LanternSwing : MonoBehaviour
{
    [Header("Swing Settings")]
    public float swingAngle = 15f; // Sallanma açýsý
    public float swingSpeed = 2f; // Sallanma hýzý
    public float randomOffsetRange = Mathf.PI * 2f; // Rastgele ofset aralýðý

    private Quaternion initialRotation;
    private float randomOffset;

    void Start()
    {
        // Baþlangýç rotasyonunu kaydedin
        initialRotation = transform.rotation;

        // Her bir lantern için rastgele bir offset oluþturun
        randomOffset = Random.Range(0f, randomOffsetRange);
    }

    void Update()
    {
        // Sallanma hareketi (sinüs dalgasý) - rastgele offset ile
        float swingOffset = Mathf.Sin(Time.time * swingSpeed + randomOffset) * swingAngle;
        transform.rotation = initialRotation * Quaternion.Euler(0, 0, swingOffset);
    }
}
