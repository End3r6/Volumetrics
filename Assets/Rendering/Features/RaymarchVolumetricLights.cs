using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RaymarchVolumetricLights : ScriptableRendererFeature
{
    public enum DownSample 
    { 
        off = 1, 
        half = 2, 
        third = 3, 
        quarter = 4 
    };

    [System.Serializable]
    public class Settings
    {
        public DownSample downsampling = DownSample.off;
        public enum Stage { raymarch, gaussianBlur, full };

        [System.Serializable]
        public class GaussBlur
        {
            public float amount;
            public float samples;
        }

        [Space(10)]
        [Header("Debug")]
        public Stage stage = Stage.full;

        [Space(10)]
        [Header("Apperance")]
        public Color tint = Color.white;
        public float intensity = 1;
        public float scattering = 0;

        [Space(10)]
        [Header("Performance")]
        public float steps = 24;
        public float maxDistance=75;
        public float jitter = 250;

        [Space(10)]
        [Header("Post Sampling")]
        public GaussBlur gaussBlur = new GaussBlur();

        [Space(10)]
        [Header("Initialization")]
        public Shader shader;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public Settings settings = new Settings();

    class Pass : ScriptableRenderPass
    {
        public Settings settings;
        private RenderTargetIdentifier source;
        RenderTargetHandle tempTexture;
        RenderTargetHandle lowResDepthRT;
        RenderTargetHandle temptexture3;

        Material material;

        private string profilerTag;

        public void Setup(RenderTargetIdentifier source)
        {
            this.source = source;
        }

        public Pass(string profilerTag)
        {
            this.profilerTag = profilerTag;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            var original = cameraTextureDescriptor;
            int divider = (int)settings.downsampling;

            if (Camera.current != null) //This is necessary so it uses the proper resolution in the scene window
            {
                cameraTextureDescriptor.width = (int)Camera.current.pixelRect.width / divider;
                cameraTextureDescriptor.height = (int)Camera.current.pixelRect.height / divider;
                original.width = (int)Camera.current.pixelRect.width;
                original.height = (int)Camera.current.pixelRect.height;
            }
            else //regular game window
            {
                cameraTextureDescriptor.width /= divider;
                cameraTextureDescriptor.height /= divider;
            }

            //R8 has noticeable banding
            cameraTextureDescriptor.colorFormat = RenderTextureFormat.ARGB32;

            //we dont need to resolve AA in every single Blit
            cameraTextureDescriptor.msaaSamples = 1;

            //we need to assing a different id for every render texture
            lowResDepthRT.id = 1;
            temptexture3.id = 2;

            cmd.GetTemporaryRT(tempTexture.id, cameraTextureDescriptor);
            ConfigureTarget(tempTexture.Identifier());

            cmd.GetTemporaryRT(lowResDepthRT.id, cameraTextureDescriptor);
            ConfigureTarget(lowResDepthRT.Identifier());

            cmd.GetTemporaryRT(temptexture3.id, original);
            ConfigureTarget(temptexture3.Identifier());
            
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
            cmd.Clear();

            //it is very important that if something fails our code still calls
            //CommandBufferPool.Release(cmd) or we will have a HUGE memory leak
            try
            {
                material = new Material(settings.shader);
                
                material.SetFloat("_Scattering", settings.scattering);
                material.SetFloat("_Steps", settings.steps);
                material.SetFloat("_JitterVolumetric", settings.jitter);
                material.SetFloat("_MaxDistance", settings.maxDistance);
                material.SetFloat("_Intensity", settings.intensity);
                material.SetFloat("_GaussSamples", settings.gaussBlur.samples);
                material.SetFloat("_GaussAmount", settings.gaussBlur.amount);
                material.SetColor("_Tint", settings.tint);

                //this is a debug feature which will let us see the process until any given point
                switch (settings.stage)
                {
                    case Settings.Stage.raymarch:
                        cmd.Blit(source, tempTexture.Identifier());
                        cmd.Blit(tempTexture.Identifier(), source, material, 0);
                        break;
                    case Settings.Stage.gaussianBlur:
                        cmd.Blit(source, tempTexture.Identifier(), material, 0);
                        cmd.Blit(tempTexture.Identifier(), lowResDepthRT.Identifier(), material, 1);
                        cmd.Blit(lowResDepthRT.Identifier(), source, material, 2);
                        break;
                    default:

                        //raymarch
                        cmd.Blit(source, tempTexture.Identifier(), material, 0);

                        //bilateral blu X, we use the lowresdepth render texture for other things too, it is just a name
                        cmd.Blit(tempTexture.Identifier(), lowResDepthRT.Identifier(), material, 1);

                        //bilateral blur Y
                        cmd.Blit(lowResDepthRT.Identifier(), tempTexture.Identifier(), material, 2);

                        //save it in a global texture
                        cmd.SetGlobalTexture("_volumetricTexture", tempTexture.Identifier());

                        //downsample depth
                        cmd.Blit(source, lowResDepthRT.Identifier(), material, 4);
                        cmd.SetGlobalTexture("_LowResDepth", lowResDepthRT.Identifier());

                        //upsample and composite
                        cmd.Blit(source, temptexture3.Identifier(), material, 3);
                        cmd.Blit(temptexture3.Identifier(), source);
                        break;
                }

                context.ExecuteCommandBuffer(cmd);
            }
            catch
            {
                Debug.LogError("Error");
            }

            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }

    Pass pass;
    RenderTargetHandle renderTextureHandle;
    public override void Create()
    {
        pass = new Pass("Volumetric Light");
        name = "Volumetric Light";
        pass.settings = settings;
        pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraColorTargetIdent = renderer.cameraColorTarget;
        pass.Setup(cameraColorTargetIdent);
        renderer.EnqueuePass(pass);
    }
}


