using UnityEngine;

public class RadarSweep : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 150f;
    [SerializeField] private RectTransform sweepLine;
    [SerializeField] private UnityEngine.UI.Image radarBase;

    void Update()
    {
        if (sweepLine != null)
        {
            // Use localRotation to avoid weird parent-space rotation issues
            sweepLine.localRotation *= Quaternion.Euler(0, 0, -rotationSpeed * Time.deltaTime);
        }
        
        if (radarBase != null)
        {
            float pulse = 0.3f + Mathf.PingPong(Time.time * 0.3f, 0.2f);
            Color c = radarBase.color;
            c.a = pulse;
            radarBase.color = c;
        }
    }
}
