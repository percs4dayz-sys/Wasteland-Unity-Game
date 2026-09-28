using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

#if UNITY_RENDER_PIPELINE_HIGH_DEFINITION
using UnityEngine.Rendering.HighDefinition;
#endif

namespace RealisticBlood.VFX
{
    [ExecuteInEditMode]
    public class PeriodicallyPlayVFX : MonoBehaviour
    {
        public float loopDuration = 3.0f;
        public float fadeSpeed = 0.5f;
        public float fadeFactor = 1.0f;

        private float timer = 0f;
        private float timerFade = 0f;
        private float opacity = 0f;
        private float dissolve = 0f;

        private VisualEffect[] vfxComponents;
        private ParticleSystem[] particleSystems;

#if UNITY_RENDER_PIPELINE_HIGH_DEFINITION
        private DecalProjector[] decalProjectors;
#endif

        void Start()
        {
            RefreshComponents();
            PlayVFX();
        }

        void Update()
        {
            RefreshComponents();

            timer += Time.deltaTime;
            if (timer >= loopDuration)
            {
                timer = 0f;
                timerFade = 0f;
                PlayVFX();
            }

            timerFade += Time.deltaTime * fadeSpeed;
            opacity = Mathf.Clamp01(timerFade);

            if (opacity > 0.5f)
                opacity = 1.0f - opacity;

            dissolve = Mathf.Clamp01(1.0f - opacity * fadeFactor);

#if UNITY_RENDER_PIPELINE_HIGH_DEFINITION
            // HDRP decal projector dissolve
            if (decalProjectors != null)
            {
                foreach (var decal in decalProjectors)
                {
                    if (decal != null && decal.material != null && decal.material.HasProperty("_Dissolve"))
                    {
                        decal.material.SetFloat("_Dissolve", dissolve);
                    }
                }
            }
#endif

            // Fade particle systems (only at runtime to avoid leaking materials)
            if (Application.isPlaying && particleSystems != null)
            {
                foreach (var ps in particleSystems)
                {
                    if (ps != null)
                    {
                        var renderer = ps.GetComponent<ParticleSystemRenderer>();
                        if (renderer != null && renderer.material != null && renderer.material.HasProperty("_Dissolve"))
                        {
                            renderer.material.SetFloat("_Dissolve", dissolve);
                        }
                    }
                }
            }
        }

        void PlayVFX()
        {
            if (vfxComponents != null)
            {
                foreach (var vfx in vfxComponents)
                {
                    if (vfx != null)
                        vfx.Play();
                }
            }

            if (particleSystems != null)
            {
                foreach (var ps in particleSystems)
                {
                    if (ps != null)
                        ps.Play(true);
                }
            }
        }

        void RefreshComponents()
        {
            vfxComponents = GetComponentsInChildren<VisualEffect>(true);
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);

#if UNITY_RENDER_PIPELINE_HIGH_DEFINITION
            decalProjectors = GetComponentsInChildren<DecalProjector>(true);
#endif
        }
    }
}
