Shader "EcoQuest/VegetationGrassCutout"
{
    Properties
    {
        [NoScaleOffset]_BaseTexture("Base Texture", 2D) = "white" {}
        _TopColor("Top Color", Color) = (0, 0.349, 0.002, 1)
        _GroundColor("Ground Color", Color) = (0.029, 0.192, 0.06, 1)
        _Gradient("Gradient", Range(0, 10)) = 1.55
        _LeavesThickness("Leaves Thickness", Range(0.05, 0.95)) = 0.5
        _WindMovement("Wind Movement", Range(0, 10)) = 0.5
        _WindDensity("Wind Density", Range(0, 5)) = 3.3
        _WindStrength("Wind Strength", Range(0, 1)) = 0.3
        [ToggleOff] _ReceiveShadows("Receive Shadows", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite On
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseTexture);
        SAMPLER(sampler_BaseTexture);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseTexture_ST;
            half4 _TopColor;
            half4 _GroundColor;
            half _Gradient;
            half _LeavesThickness;
            half _WindMovement;
            half _WindDensity;
            half _WindStrength;
            half _ReceiveShadows;
        CBUFFER_END

        float3 ApplyGrassWindOS(float3 posOS)
        {
            float3 originWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            float phase = _Time.y * (_WindMovement * 2.4h + 0.5h) + (originWS.x + posOS.x) * _WindDensity + (originWS.z + posOS.z) * (_WindDensity * 0.7h);
            float sway = sin(phase) * (saturate(posOS.y * 1.5h) * _WindStrength * 0.08h);
            posOS.x += sway;
            posOS.z += sway * 0.35h;
            return posOS;
        }

        // Preserve distant mipmapped grass blade coverage (fragment-only) so thin blades don't vanish into dots.
        half4 SampleAndClipGrass(float2 uv)
        {
            half4 tex = SAMPLE_TEXTURE2D(_BaseTexture, sampler_BaseTexture, uv);
            float2 dx = ddx(uv * 256.0);
            float2 dy = ddy(uv * 256.0);
            float rho = max(dot(dx, dx), dot(dy, dy));
            half mipBoost = (half)saturate(0.5 * log2(max(rho, 1.0)) * 0.16);
            half cutoff = max(0.22h, (1.0h - _LeavesThickness) - mipBoost * 0.25h);
            clip(tex.a - cutoff);
            return tex;
        }
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertForward
            #pragma fragment FragForward
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                half3 normalWS     : TEXCOORD2;
                half3 vertexSH     : TEXCOORD3;
                float fogFactor    : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings VertForward(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posOS = ApplyGrassWindOS(IN.positionOS.xyz);
                VertexPositionInputs vtxInput = GetVertexPositionInputs(posOS);
                OUT.positionCS = vtxInput.positionCS;
                OUT.positionWS = vtxInput.positionWS;
                OUT.uv = IN.uv;
                OUT.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(IN.normalOS));
                OUT.vertexSH = SampleSHVertex(OUT.normalWS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 FragForward(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 tex = SampleAndClipGrass(IN.uv);
                half tipMask = saturate(IN.uv.y * _Gradient);

                // Use vibrant base grass texture RGB with subtle root-to-tip depth gradient matching Polytope look.
                half3 albedo = tex.rgb * lerp(0.76h, 1.16h, tipMask);

                // Keep upward sky normal on both sides of double-sided blades so backfaces stay sunlit, not black.
                half3 normalWS = NormalizeNormalPerPixel(IN.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = saturate(dot(normalWS, mainLight.direction) * 0.45h + 0.55h);
                half atten = mainLight.distanceAttenuation * lerp(0.32h, 1.0h, mainLight.shadowAttenuation);
                half3 ambient = SampleSHPixel(IN.vertexSH, normalWS);

                half3 viewDirWS = SafeNormalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                half transVdotL = pow(saturate(dot(viewDirWS, -(mainLight.direction + normalWS * 0.5h))), 2.0h);
                half transTerm = (transVdotL * 0.85h + 0.16h) * lerp(0.35h, 1.0h, mainLight.shadowAttenuation) * tipMask;

                half3 color = albedo * (ambient + mainLight.color * (ndotl * atten + transTerm));
                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertDepth
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings VertDepth(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 posOS = ApplyGrassWindOS(IN.positionOS.xyz);
                OUT.positionCS = TransformObjectToHClip(posOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half FragDepth(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                SampleAndClipGrass(IN.uv);
                return IN.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertDepthNormals
            #pragma fragment FragDepthNormals
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3 normalWS    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings VertDepthNormals(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 posOS = ApplyGrassWindOS(IN.positionOS.xyz);
                OUT.positionCS = TransformObjectToHClip(posOS);
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 FragDepthNormals(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                SampleAndClipGrass(IN.uv);
                return half4(NormalizeNormalPerPixel(IN.normalWS), 0.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertShadow
            #pragma fragment FragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings VertShadow(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posWS = TransformObjectToWorld(ApplyGrassWindOS(IN.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirWS = normalize(_LightPosition - posWS);
                #else
                    float3 lightDirWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normalWS, lightDirWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 FragShadow(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                SampleAndClipGrass(IN.uv);
                return 0;
            }
            ENDHLSL
        }
    }
}
