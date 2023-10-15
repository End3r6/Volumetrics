using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

using System.Collections.Generic;

public enum Quality : int
{
    Low = 0,
    Medium,
    High
}

[System.Serializable]
public class RadialBlurLightScatteringSettings
{
    [Header("Properties")]
    public Quality quality;
    
    [Space(10)]

    public Color tint;

    [Space(10)]

    [Range(0.1f, 1f)]
    public float resolutionScale = 0.5f;

    [Range(0.0f, 1f)]
    public float intensity = 1.0f;

    [Range(0.0f, 1f)]
    public float blurWidth = 0.85f;
}

public class RadialBlurLightScattering : ScriptableRendererFeature
{
    class RadialBlurLightScatteringPass : ScriptableRenderPass
    {
        private readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();

        private FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque);

        private readonly RenderTargetHandle occluders = RenderTargetHandle.CameraTarget;

        private readonly Color tint;
        private readonly Quality quality;
        private readonly float resolutionScale;
        private readonly float intensity;
        private readonly float blurWidth;

        private readonly Material occludersMaterial;
        private readonly Material radialBlurMaterial;

        private RenderTargetIdentifier cameraColorTargetIdent;

        public RadialBlurLightScatteringPass(RadialBlurLightScatteringSettings settings)
        {
            occluders.Init("_OccludersMap");
            tint = settings.tint;
            quality = settings.quality;

            resolutionScale = settings.resolutionScale;
            intensity = settings.intensity;
            blurWidth = settings.blurWidth;

            occludersMaterial = new Material(Shader.Find("Hidden/Worlds End/UnlitColor"));

            radialBlurMaterial = new Material(Shader.Find("Hidden/Worlds End/RadialBlur"));

            shaderTagIdList.Add(new ShaderTagId("UniversalForward"));
            shaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));
            shaderTagIdList.Add(new ShaderTagId("LightweightForward"));
            shaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
        }

        public void SetCameraColorTarget(RenderTargetIdentifier cameraColorTargetIdent)
        {
            this.cameraColorTargetIdent = cameraColorTargetIdent;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor cameraTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;

            cameraTextureDescriptor.depthBufferBits = 0;

            cameraTextureDescriptor.width = Mathf.RoundToInt(cameraTextureDescriptor.width * resolutionScale);
            cameraTextureDescriptor.height = Mathf.RoundToInt(cameraTextureDescriptor.height * resolutionScale);

            cmd.GetTemporaryRT(occluders.id, cameraTextureDescriptor, FilterMode.Bilinear);

            ConfigureTarget(occluders.Identifier());
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!occludersMaterial || !radialBlurMaterial )
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, new ProfilingSampler("RadialBlurLightScattering")))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                
                Camera camera = renderingData.cameraData.camera;
                context.DrawSkybox(camera);

                DrawingSettings drawSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, SortingCriteria.CommonOpaque);
                drawSettings.overrideMaterial = occludersMaterial;

                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings);

                Vector3 sunDirectionWorldSpace = RenderSettings.sun.transform.forward;
                Vector3 cameraPositionWorldSpace = camera.transform.position;

                Vector3 sunPositionWorldSpace = cameraPositionWorldSpace + sunDirectionWorldSpace;
                Vector3 sunPositionViewportSpace = camera.WorldToViewportPoint(sunPositionWorldSpace);

                radialBlurMaterial.SetVector("_Center", new Vector4(sunPositionViewportSpace.x, sunPositionViewportSpace.y, 0, 0));

                radialBlurMaterial.SetColor("_Tint", tint);
                radialBlurMaterial.SetFloat("_Samples", (float)quality);
                radialBlurMaterial.SetFloat("_Intensity", intensity);
                radialBlurMaterial.SetFloat("_BlurWidth", blurWidth);

                Blit(cmd, occluders.Identifier(), cameraColorTargetIdent, radialBlurMaterial);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(occluders.id);
        }
    }

    RadialBlurLightScatteringPass m_ScriptablePass;

    public RadialBlurLightScatteringSettings settings = new RadialBlurLightScatteringSettings();

    public override void Create()
    {
        m_ScriptablePass = new RadialBlurLightScatteringPass(settings);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);
        m_ScriptablePass.SetCameraColorTarget(renderer.cameraColorTarget);
    }
}


