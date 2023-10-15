Shader "Hidden/Worlds End/VolumetricClouds"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;

                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;

                float2 uv : TEXCOORD0;
            };

            Texture3D<float4> _ShapeNoise;
            SamplerState sampler_ShapeNoise;

            float3 _ShapeOffset;
            float _ShapeScale;
            float shapeWeight;

            Texture3D<float4> _DetailNoiseTex;
            SamplerState sampler_DetailNoiseTex;

            float detailWeight;
            float3 _DetailOffset;
            float _DetailScale;

            Texture3D<float4> _WeatherMap;
            SamplerState sampler_WeatherMap;
            float weatherScale;
            float weatherWeight;

            float3 _BoundsMin, _BoundsMax;
            
            float _BaseSpeed;
            float _DetailSpeed;

            float4 phaseParams;

            float _DensThreshold;
            float _DensOffset;
            float _DensMultiplier;

            int _NumSteps;
            float _JitterValue;

            int _LightDensSteps;

            float _LightAbsorption;
            float _CloudLightAbsorption;
            float _DarknessThreshold;

            float _ShadowJitter;
            float _ShadowSteps;
            int _ReceiveDetal;
            int _ShadowsEnabled;

            TEXTURE2D(_CloudShadowMap);
            SAMPLER(sampler_CloudShadowMap);

            float2 RayBoxDist(float3 boundMin, float3 boundMax, float3 rayOrg, float3 rayDir)
            {
                float3 t0 = (boundMin - rayOrg) / rayDir;
                float3 t1 = (boundMax - rayOrg) / rayDir;

                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                float3 dstA = max(max(tmin.x, tmin.y), tmin.z);
                float3 dstB = min(tmax.x, min(tmax.y, tmax.z));

                float dstToBox = max(0, dstA);
                float dstInsideBox = max(0, dstB - dstToBox);

                return float2(dstToBox, dstInsideBox);
            }

            float remap(float v, float minOld, float maxOld, float minNew, float maxNew) 
            {
                return minNew + (v-minOld) * (maxNew - minNew) / (maxOld-minOld);
            }

            float SampleDensity(float3 rayPos, int useDetail) 
            {
                // Constants:
                const int mipLevel = 0;
                const float baseScale = 1 / 1000.0;
                const float offsetSpeed = 1 / 100.0;

                // Calculate texture sample positions
                float time = _Time.y;

                float3 size = _BoundsMax - _BoundsMin;
                float3 boundsCentre = (_BoundsMin + _BoundsMax) * .5;
                
                float3 uvw = (size * .5 + rayPos) * baseScale * _ShapeScale;
                float3 shapeSamplePos = uvw + _ShapeOffset + float3(time, time * 0.1, time * 0.2) * _BaseSpeed;

                // Calculate falloff at along x/z edges of the cloud container
                const float containerEdgeFadeDst = 50;
                float dstFromEdgeX = min(containerEdgeFadeDst, min(rayPos.x - _BoundsMin.x, _BoundsMax.x - rayPos.x));
                float dstFromEdgeZ = min(containerEdgeFadeDst, min(rayPos.z - _BoundsMin.z, _BoundsMax.z - rayPos.z));
                float edgeWeight = min(dstFromEdgeZ, dstFromEdgeX) / containerEdgeFadeDst;

                // Calculate base shape density
                float4 shapeNoise = _ShapeNoise.SampleLevel(sampler_ShapeNoise, shapeSamplePos, mipLevel);
                float baseShapeDensity = shapeNoise + _DensOffset * .1;

                float3 weatherSamplePos = uvw * weatherScale;
                float4 weatherMap = _WeatherMap.SampleLevel(sampler_WeatherMap, weatherSamplePos, mipLevel);

                // Save sampling from detail tex if shape density <= 0
                if (baseShapeDensity > 0) {
                    // Sample detail noise
                    float3 detailSamplePos = 1;
                    float4 detailNoise = 1;

                    if(useDetail == 1)
                    {
                        detailSamplePos = uvw * _DetailScale + _DetailOffset + float3(time*.4,-time,time*0.1) * _DetailSpeed;
                        detailNoise = _DetailNoiseTex.SampleLevel(sampler_DetailNoiseTex, detailSamplePos, mipLevel);
                    }

                    // Subtract detail noise from base shape (weighted by inverse density so that edges get eroded more than centre)
                    float cloudDensity = (baseShapeDensity * shapeWeight) - 1 - (detailNoise * detailWeight) - (weatherMap * weatherWeight);
    
                    return (cloudDensity) * _DensMultiplier * 0.1 * edgeWeight;
                }
                return 0;
            }

            real3 GetWorldPos(real2 uv)
            {
                #if UNITY_REVERSED_Z
                    real depth = SampleSceneDepth(uv);
                #else
                    // Adjust z to match NDC for OpenGL
                    real depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif
                return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
            }

            float hg(float a, float g) 
            {
                float g2 = g * g;
                return (1-g2) / (4*3.1415*pow(1+g2-2*g*(a), 1.5));
            }

            float phase(float a) 
            {
                float blend = .5;
                float hgBlend = hg(a, phaseParams.x) * (1 - blend) + hg(a, phaseParams.y) * blend;
                return phaseParams.z + hgBlend * phaseParams.w;
            }

            float Lightmarch(float3 position) 
            {
                float3 dirToLight = _MainLightPosition.xyz;
                float dstInsideBox = RayBoxDist(_BoundsMin, _BoundsMax, position, dirToLight).y;
                
                float stepSize = dstInsideBox / _LightDensSteps;
                float totalDensity = 0;

                for (int s = 0; s < _LightDensSteps; s ++) 
                {
                    position += dirToLight * stepSize;
                    totalDensity += max(0, SampleDensity(position, 1) * stepSize);
                }

                float transmittance = exp(-totalDensity * _LightAbsorption);
                return _DarknessThreshold + transmittance * (1 - _DarknessThreshold);
            }

            float random01( real2 p )
            {
                return frac(sin(dot(p, real2(41, 289)))*45758.5453 ); 
            }

            float GetShadowMap(float2 uv)
            {
                float3 lightPos = _MainLightPosition;
                float3 rayStartPos = GetWorldPos(uv);
                float3 rayVector = lightPos - rayStartPos;
                float3 rayDir = lightPos;
                float rayLength = length(rayVector);

                //Box stuff
                float2 boxInfo = RayBoxDist(_BoundsMin, _BoundsMax, rayStartPos, rayDir);
                float dstToBox = boxInfo.x;
                float dstInsideBox = boxInfo.y;

                //Depth
                float nonlin_depth = SampleSceneDepth(uv);
                float depth = LinearEyeDepth(nonlin_depth, _ZBufferParams.y) * (dstToBox + dstInsideBox);

                //Distance
                float stepLength = dstInsideBox / _ShadowSteps;
                float dstLimit = min(depth - dstToBox, dstInsideBox);

                float randomOffset = random01(uv) * stepLength * _ShadowJitter;
                float dstTravelled = randomOffset;

                //Shadow Stuff
                float shadowAtten = 1;
                
                while(dstTravelled < dstLimit)
                {
                    float3 samplePoint = rayStartPos + rayDir * (dstToBox + dstTravelled);
                    float density = SampleDensity(samplePoint, _ReceiveDetal);
                    
                    // if it is in light
                    if(density > 0)
                    {                 
                        shadowAtten *= exp(-density * stepLength * _CloudLightAbsorption);

                        if(shadowAtten < 0.01)
                        {
                            break;
                        }
                    }
                    dstTravelled += stepLength;
                }

                return shadowAtten;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformWorldToHClip(v.vertex.xyz);
                o.uv = v.uv;

                return o;
            }

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float3 frag (v2f i) : SV_Target
            {
                //Ray stuff
                float3 worldPos = GetWorldPos(i.uv);
                float3 rayStartPos = _WorldSpaceCameraPos;
                float3 rayVector = worldPos - rayStartPos;
                float3 rayDir = normalize(rayVector);
                float rayLength = length(rayVector);

                //Depth
                float nonlin_depth = SampleSceneDepth(i.uv);
                float depth = LinearEyeDepth(nonlin_depth, _ZBufferParams.y) * rayLength;

                //Box stuff
                float2 boxInfo = RayBoxDist(_BoundsMin, _BoundsMax, rayStartPos, rayDir);
                float dstToBox = boxInfo.x;
                float dstInsideBox = boxInfo.y;

                float cosAngle = dot(rayDir, _MainLightPosition.xyz);
                float phaseVal = phase(cosAngle);

                //Distance Stuff
                float stepLength = dstInsideBox / _NumSteps;
                float dstLimit = min(depth - dstToBox, dstInsideBox);
                
                float randomOffset = random01(i.uv) * stepLength * _JitterValue / 100;

                float dstTravelled = randomOffset;

                float transmittance = 1;
                float3 lightEnergy = 0;
                while(dstTravelled < dstLimit)
                {
                    float3 samplePoint = rayStartPos + rayDir * (dstToBox + dstTravelled);
                    float density = SampleDensity(samplePoint, 1);
                    
                    // if it is in light
                    if(density > 0)
                    {    
                        float lighTransmittance = Lightmarch(samplePoint);
                        lightEnergy += density * stepLength * transmittance * lighTransmittance * phaseVal;                   
                        transmittance *= exp(-density * stepLength * _CloudLightAbsorption);

                        if(transmittance < 0.01)
                        {
                            break;
                        }
                    }
                    dstTravelled += stepLength;
                }

                float3 cloudShadows = 1;
                if(_ShadowsEnabled)
                {
                    cloudShadows = GetShadowMap(i.uv) * 0.5 + 0.5;
                }
                
                float3 background = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                float3 cloudColor = lightEnergy * _MainLightColor;
                float3 col = background * cloudShadows * transmittance + cloudColor;
                
                return col;
            }
            ENDHLSL
        }
    }
}
