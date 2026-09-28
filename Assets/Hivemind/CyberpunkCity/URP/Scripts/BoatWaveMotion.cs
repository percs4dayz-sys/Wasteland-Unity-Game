using UnityEngine;

public class BoatWaveMotion : MonoBehaviour
{
    [Header("Wave Settings")]
    public float amplitude = 0.5f; // Dalgalarýn yüksekliði
    public float frequency = 1.0f; // Dalgalarýn hýzý

    [Header("Tilt Settings")]
    public float tiltAngleSide = 5f; // Saða sola eðilme açýsý
    public float tiltSpeedSide = 1.0f; // Saða sola eðilme hýzý
    public float tiltAngleForward = 5f; // Ýleri geri eðilme açýsý
    public float tiltSpeedForward = 1.0f; // Ýleri geri eðilme hýzý

    [Header("Random Offset Settings")]
    public float randomOffsetRange = Mathf.PI * 2f; // Rastgele ofset aralýðý

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private float randomWaveOffset;
    private float randomTiltSideOffset;
    private float randomTiltForwardOffset;

    void Start()
    {
        // Baþlangýç pozisyonu ve rotasyonu kaydedilir
        initialPosition = transform.position;
        initialRotation = transform.rotation;

        // Her bir bot için rastgele ofset oluþturulur
        randomWaveOffset = Random.Range(0f, randomOffsetRange);
        randomTiltSideOffset = Random.Range(0f, randomOffsetRange);
        randomTiltForwardOffset = Random.Range(0f, randomOffsetRange);
    }

    void Update()
    {
        // Yükseklik hareketi (sinüs dalgasý) - rastgele offset ile
        float waveOffset = Mathf.Sin(Time.time * frequency + randomWaveOffset) * amplitude;
        transform.position = new Vector3(initialPosition.x, initialPosition.y + waveOffset, initialPosition.z);

        // Eðilme hareketi (yanal ve ileri-geri sallanma) - rastgele offset ile
        float tiltSideOffset = Mathf.Sin(Time.time * tiltSpeedSide + randomTiltSideOffset) * tiltAngleSide; // Saða sola eðilme
        float tiltForwardOffset = Mathf.Cos(Time.time * tiltSpeedForward + randomTiltForwardOffset) * tiltAngleForward; // Ýleri geri eðilme
        transform.rotation = initialRotation * Quaternion.Euler(tiltForwardOffset, 0, tiltSideOffset);
    }
}
