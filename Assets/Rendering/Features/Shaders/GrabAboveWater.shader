Shader "Hidden/Worlds End/GrabAboveWater"
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            real3 GetWorldPos(real2 uv)
            {
                #if UNITY_REVERSED_Z
                    real depth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
                #else
                    // Adjust z to match NDC for OpenGL
                    real depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv));
                #endif
                return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
            }

            v2f vert (appdata v)
            {
                v2f o;

                float3 positionWS = TransformObjectToWorld(v.vertex.xyz);

                o.vertex = float4(positionWS, 1);
                o.uv = v.uv;

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = (0, 0, 0, 1);

                float3 worldPos = GetWorldPos(i.uv);

                if(worldPos.y >= 0)
                {
                    col = 1;
                }
                else
                {
                    col = 0;
                }

                return col;
            }
            ENDHLSL
        }
    }
}
