using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>World-local graphics quality; the previous pipeline is restored when leaving.</summary>
public class BlackwaterPresentation : MonoBehaviour
{
    public UniversalRenderPipelineAsset pipeline;
    RenderPipelineAsset previous;
    void OnEnable()
    {
        previous=QualitySettings.renderPipeline;
        if(pipeline!=null)QualitySettings.renderPipeline=pipeline;
    }
    void OnDisable()
    {
        if(QualitySettings.renderPipeline==pipeline)QualitySettings.renderPipeline=previous;
    }
}
