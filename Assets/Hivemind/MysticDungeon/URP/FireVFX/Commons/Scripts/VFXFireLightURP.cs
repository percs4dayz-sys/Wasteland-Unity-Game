using UnityEngine;
using UnityEngine.VFX;

[AddComponentMenu("VFX/VFX Fire Light (URP)")]
public class VFXFireLightURP : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign your Fire Visual Effect Graph here.")]
    public VisualEffect vfx;

    [Tooltip("Assign your Light Prefab (with a regular Light component).")]
    public GameObject lightPrefab;

    [Header("Light Placement")]
    public Vector3 offset = new Vector3(0f, 1.5f, 0f);

    [Header("Flicker Settings")]
    [Tooltip("Average brightness (intensity).")]
    public float baseIntensity = 5f;

    [Tooltip("Variation range for intensity flicker.")]
    public float flickerAmount = 2f;

    [Tooltip("Speed of flicker movement.")]
    public float flickerSpeed = 5f;

    [Header("Light Settings")]
    [Tooltip("Fixed light range (radius).")]
    public float lightRange = 12f; // increased slightly to reduce culling

    [Tooltip("Color of the point light.")]
    public Color lightColor = new Color(1f, 0.6f, 0.2f); // warm orange fire tone

    private GameObject spawnedLight;
    private Light urpLight;
    private int onPlayID;

    private void Awake()
    {
        onPlayID = Shader.PropertyToID("OnPlay");
    }

    private void OnEnable()
    {
        if (vfx != null)
            vfx.outputEventReceived += OnVFXEvent;
    }

    private void OnDisable()
    {
        if (vfx != null)
            vfx.outputEventReceived -= OnVFXEvent;
    }

    private void OnVFXEvent(VFXOutputEventArgs args)
    {
        if (args.nameId == onPlayID)
        {
            if (spawnedLight == null && lightPrefab != null)
            {
                spawnedLight = Instantiate(lightPrefab, vfx.transform);
                spawnedLight.transform.localPosition = offset;

                urpLight = spawnedLight.GetComponent<Light>();

                if (urpLight != null)
                {
                    urpLight.color = lightColor;
                    urpLight.range = lightRange;

                    // 🔹 Prevent URP from culling this light even when multiple lights overlap
                    urpLight.renderMode = LightRenderMode.ForcePixel;
                }
            }
        }
    }

    private void Update()
    {
        if (urpLight != null)
        {
            // Smooth flicker using 2D Perlin
            float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, Time.time * 0.33f);
            urpLight.intensity = baseIntensity + noise * flickerAmount;

            urpLight.color = lightColor;
            urpLight.range = lightRange;
        }
    }
}
