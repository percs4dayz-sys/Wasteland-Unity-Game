using UnityEngine;
using UnityEngine.VFX;

[AddComponentMenu("VFX/VFX Fire Light (URP Burst)")]
public class VFXFireLightURP_Burst : MonoBehaviour
{
    [Header("References")]
    public VisualEffect vfx;          // Fire VFX
    public GameObject lightPrefab;    // Light prefab with regular Light component

    [Header("Light Placement")]
    public Vector3 offset = new Vector3(0f, 1.5f, 0f);

    [Header("Flicker Settings")]
    public float baseIntensity = 5f;
    public float flickerAmount = 2f;
    public float flickerSpeed = 5f;

    [Header("Light Settings")]
    public float lightRange = 12f;
    public Color lightColor = new Color(1f, 0.6f, 0.2f);
    public float lightLifetime = 0.2f;

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
        if (args.nameId != onPlayID || lightPrefab == null) return;

        // Spawn a new light for this event
        GameObject spawnedLight = Instantiate(lightPrefab, vfx.transform);
        spawnedLight.transform.localPosition = offset;

        Light urpLight = spawnedLight.GetComponent<Light>();
        if (urpLight != null)
        {
            urpLight.color = lightColor;
            urpLight.range = lightRange;
            urpLight.renderMode = LightRenderMode.ForcePixel; // reduce culling

            // Start coroutine for flicker and auto-destroy
            StartCoroutine(FadeAndDestroy(urpLight, spawnedLight, lightLifetime));
        }
    }

    private System.Collections.IEnumerator FadeAndDestroy(Light lightComp, GameObject lightGO, float duration)
    {
        float timer = 0f;
        float startIntensity = baseIntensity;

        while (timer < duration)
        {
            timer += Time.deltaTime;

            // Flicker using 2D Perlin
            float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, Time.time * 0.33f);
            lightComp.intensity = Mathf.Lerp(startIntensity + noise * flickerAmount, 0f, timer / duration);

            yield return null;
        }

        Destroy(lightGO);
    }
}
