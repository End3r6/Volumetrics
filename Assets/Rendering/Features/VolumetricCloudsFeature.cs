using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricCloudsFeature : ScriptableRendererFeature
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
        [System.Serializable]
        public class Container
        {
            public Vector3 pos;
            public Vector3 scale;
        }

        [System.Serializable]
        public class RayMarch
        {
            [Min(1)]
            public float steps;

            [Min(0)]
            public float jitter;
        }

        [System.Serializable]
        public class Shadows
        {
            public bool enabled;
            public bool receiveDetail;
            
            [Min(1)]
            public float steps = 3;

            [Min(0)]
            public float jitter = 2;
        }
        

        [System.Serializable]
        public class Lighting
        {
            [Min(0)]
            public float lightDensSteps;

            [Min(0)]
            public float lightAbsorption;

            [Min(0)]
            public float cloudLightAbsorption;

            [Range(0, 1)]
            public float darknessThreshold;

            [HideInInspector]
            public Vector4 phaseParams;

            [Header("Phase Params")]

            [Range(0, 1)]
            public float backScattering = 0.915f;
            [Range(0, 1)]
            public float frontScattering = 0.867f;
            [Range(0, 1)]
            public float baseBrightness = 1f;
            [Range(0, 1)]
            public float phaseIntensity = 0.43f;

            public Shadows shadows = new Shadows();
        }

        [System.Serializable]
        public class Shape
        {
            [Header("Base")]
            public Vector3 cloudOffset;
            public Vector3 detailOffset;

            [Header("Shape")]
            public Texture3D shapeNoise;
            public Texture3D detailTexture;
            public Texture3D weatherMap;

            public float cloudScale;
            public float detailScale;
            public float weatherMapScale;

            [Header("Movement")]
            public float detailSpeed;
            public float baseSpeed;
            
            [Header("Density")]
            public float densMultiplier;
            [Range(-5, 10)]
            public float densOffset;
            
        }

        [System.Serializable]
        public class Weights
        {
            public float shapeWeight;
            public float detailWeight;
            public float weatherWeight;
        }

        public DownSample downsampling = DownSample.off;

        public Container container = new Container();

        public RayMarch raymarch = new RayMarch();

        public Shape shape = new Shape();

        public Weights weights = new Weights();

        public Lighting lighting = new Lighting();

        [Space(10)]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    class Pass : ScriptableRenderPass
    {
        public Settings settings;

        private RenderTargetIdentifier source;

        RenderTargetHandle tempTexture;

        RenderTargetHandle cloudTexture;

        private Material material;

        private string passName;

        public void Setup(RenderTargetIdentifier source)
        {
            this.source = source;
            material = new Material(Shader.Find("Hidden/Worlds End/VolumetricClouds"));
        }

        public Pass(string passName)
        {
            this.passName = passName;
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
            cloudTexture.id = 1;

            cmd.GetTemporaryRT(tempTexture.id, cameraTextureDescriptor);
            ConfigureTarget(tempTexture.Identifier());

            cmd.GetTemporaryRT(cloudTexture.id, cameraTextureDescriptor);
            ConfigureTarget(cloudTexture.Identifier());
            
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(passName);
            cmd.Clear();

            //it is very important that if something fails our code still calls 
            //CommandBufferPool.Release(cmd) or we will have a HUGE memory leak
            try
            {

                settings.lighting.phaseParams.x = settings.lighting.backScattering;
                settings.lighting.phaseParams.y = settings.lighting.frontScattering;
                settings.lighting.phaseParams.z = settings.lighting.baseBrightness;
                settings.lighting.phaseParams.w = settings.lighting.phaseIntensity;

                #region Material Settings                
                material.SetVector("_BoundsMin", settings.container.pos - settings.container.scale / 2);
                material.SetVector("_BoundsMax", settings.container.pos + settings.container.scale / 2);

                material.SetTexture("_ShapeNoise", settings.shape.shapeNoise);
                material.SetTexture("_DetailNoiseTex", settings.shape.detailTexture);
                material.SetTexture("_WeatherMap", settings.shape.weatherMap);

                material.SetVector("_ShapeOffset", settings.shape.cloudOffset);
                material.SetVector("_DetailOffset", settings.shape.detailOffset);

                material.SetFloat("_ShapeScale", settings.shape.cloudScale);
                material.SetFloat("_DetailScale", settings.shape.detailScale);
                material.SetFloat("weatherScale", settings.shape.weatherMapScale);

                material.SetFloat("_BaseSpeed", settings.shape.baseSpeed);
                material.SetFloat("_DetailSpeed", settings.shape.detailSpeed);

                material.SetFloat("_DensMultiplier", settings.shape.densMultiplier);
                material.SetFloat("_DensOffset", settings.shape.densOffset);

                material.SetFloat("_NumSteps", settings.raymarch.steps);
                material.SetFloat("_JitterValue", settings.raymarch.jitter);

                material.SetFloat("shapeWeight", settings.weights.shapeWeight);
                material.SetFloat("detailWeight", settings.weights.detailWeight);
                material.SetFloat("weatherWeight", settings.weights.weatherWeight);

                material.SetVector("phaseParams", settings.lighting.phaseParams);

                material.SetFloat("_LightDensSteps", settings.lighting.lightDensSteps);
                material.SetFloat("_LightAbsorption", settings.lighting.lightAbsorption);
                material.SetFloat("_CloudLightAbsorption", settings.lighting.cloudLightAbsorption);
                material.SetFloat("_DarknessThreshold", settings.lighting.darknessThreshold);

                material.SetFloat("_ShadowJitter", settings.lighting.shadows.jitter);
                material.SetFloat("_ShadowSteps", settings.lighting.shadows.steps);
                material.SetInt("_ReceiveDetal", settings.lighting.shadows.receiveDetail ? 1 : 0);
                material.SetInt("_ShadowsEnabled", settings.lighting.shadows.enabled ? 1 : 0);

                #endregion

                //Cloud draw
                cmd.Blit(source, cloudTexture.Identifier(), material, 0);

                //Redraw to source
                cmd.Blit(cloudTexture.Identifier(), source);

                context.ExecuteCommandBuffer(cmd);
            }
            catch
            {
                Debug.LogError($"An issue has occured in {passName}");
            }

            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }

    Pass pass;

    public Settings settings = new Settings();

    RenderTargetHandle renderTextureHandle;

    public override void Create()
    {
        pass = new Pass("Volumetric Clouds");
        pass.settings = settings;

        pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(pass);

        // var cameraColorTargetIdent = renderer.cameraColorTarget;
        pass.Setup(renderer.cameraColorTarget);
    }
}