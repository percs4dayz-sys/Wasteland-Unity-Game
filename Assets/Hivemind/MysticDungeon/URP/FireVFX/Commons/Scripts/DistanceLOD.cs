using UnityEngine;
using UnityEngine.VFX;

public class VFXDistanceLOD : MonoBehaviour
{
    public VisualEffect vfx;
    private Transform cam;

    void Start()
    {
        cam = Camera.main.transform;
    }

    void Update()
    {
        if (!cam || !vfx) return;

        float dist = Vector3.Distance(cam.position, vfx.transform.position);
        vfx.SetFloat("CameraDistance", dist);
        // Debug.Log("Distance: " + dist);
    }
}
