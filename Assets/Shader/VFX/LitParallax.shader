Shader "Custom/URP/LitParallax"
{
    Properties
    {
        [Header(Surface Options)]
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1.0

        [NoScaleOffset] _MetallicGlossMap("Metallic(R) Smoothness(A)", 2D) = "white" {}
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5

        [NoScaleOffset] _OcclusionMap("Occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0

        [Header(Parallax Settings)]
        _ParallaxMap("Height Map (R: Height)", 2D) = "black" {}
        _ParallaxStrength("Parallax Strength", Range(0.0, 0.1)) = 0.02
        _ParallaxSteps("Parallax Steps (Quality)", Range(1, 50)) = 10
        [Toggle] _InvertHeightMap("Invert Height Map", Float) = 0

        [Header(Advanced)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2 // Back
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "Geometry" 
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // ---------------------------------------------------------
            // URP 关键字
            // ---------------------------------------------------------
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog // 增加雾效支持

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
                float2 lightmapUV   : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 tangentWS    : TEXCOORD2;
                float3 bitangentWS  : TEXCOORD3;
                float2 uv           : TEXCOORD4;
                float3 viewDirTS    : TEXCOORD5;
                float  fogFactor    : TEXCOORD6;
                
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 7); 
            };

            TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_ParallaxMap);        SAMPLER(sampler_ParallaxMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                half _Metallic;
                half _Smoothness;
                half _OcclusionStrength;
                half _ParallaxStrength;
                float _ParallaxSteps;
                half _InvertHeightMap;
            CBUFFER_END

            // --- POM 算法 ---
            inline float ApplyHeightInvert(float heightValue)
            {
                return lerp(heightValue, 1.0 - heightValue, _InvertHeightMap);
            }

            float2 ParallaxMapping(float2 uv, float3 viewDirTS)
            {
                float minSteps = _ParallaxSteps * 0.5;
                float maxSteps = _ParallaxSteps;
                float numSteps = lerp(maxSteps, minSteps, viewDirTS.z);
                
                float stepSize = 1.0 / numSteps;
                // 保护除零
                float2 p = viewDirTS.xy / (viewDirTS.z + 0.0001) * _ParallaxStrength; 
                float2 deltaTexCoord = p / numSteps;

                float2 currentTexCoord = uv;
                float currentLayerHeight = 0.0;
                
                // 读取高度图 (这里取 R 通道)
                float currentHeightMapValue = ApplyHeightInvert(SAMPLE_TEXTURE2D(_ParallaxMap, sampler_ParallaxMap, currentTexCoord).r);

                UNITY_UNROLL
                for(int i = 0; i < 50; i++)
                {
                    if (i >= numSteps || currentLayerHeight >= currentHeightMapValue) 
                        break;

                    currentTexCoord -= deltaTexCoord;
                    currentLayerHeight += stepSize;
                    currentHeightMapValue = ApplyHeightInvert(SAMPLE_TEXTURE2D(_ParallaxMap, sampler_ParallaxMap, currentTexCoord).r);
                }

                // 线性插值平滑
                float2 prevTexCoord = currentTexCoord + deltaTexCoord;
                float afterHeight  = currentHeightMapValue - currentLayerHeight;
                float beforeHeight = ApplyHeightInvert(SAMPLE_TEXTURE2D(_ParallaxMap, sampler_ParallaxMap, prevTexCoord).r) - (currentLayerHeight - stepSize);
                
                float weight = afterHeight / (afterHeight - beforeHeight);
                float2 finalTexCoord = prevTexCoord * weight + currentTexCoord * (1.0 - weight);

                return finalTexCoord;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = normalInput.tangentWS;
                output.bitangentWS = normalInput.bitangentWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                float3 viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                output.viewDirTS.x = dot(viewDirWS, output.tangentWS);
                output.viewDirTS.y = dot(viewDirWS, output.bitangentWS);
                output.viewDirTS.z = dot(viewDirWS, output.normalWS);
                output.viewDirTS = normalize(output.viewDirTS);

                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. POM 偏移
                float3 viewDirTS = normalize(input.viewDirTS);
                float2 parallaxUV = ParallaxMapping(input.uv, viewDirTS);

                // 2. 采样贴图
                half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, parallaxUV);
                half4 metallicSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, parallaxUV);
                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, parallaxUV);
                half occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, parallaxUV).r;

                // 3. 填充 SurfaceData
                // 【关键修复】：必须初始化为 0，防止 "not completely initialized" 错误
                SurfaceData surfaceData = (SurfaceData)0;
                
                surfaceData.albedo = albedoSample.rgb * _BaseColor.rgb;
                surfaceData.alpha = albedoSample.a * _BaseColor.a;
                surfaceData.metallic = metallicSample.r * _Metallic;
                surfaceData.smoothness = metallicSample.a * _Smoothness;
                surfaceData.normalTS = UnpackNormalScale(normalSample, _BumpScale);
                surfaceData.occlusion = lerp(1.0, occlusion, _OcclusionStrength);
                
                // 即使是 Metallic 流程，specular 字段也必须赋值（设为0即可）
                surfaceData.specular = 0; 
                surfaceData.emission = 0;
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;

                // 4. 重建 InputData
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                
                float3 viewDirWS = GetWorldSpaceViewDir(input.positionWS);
                inputData.viewDirectionWS = viewDirWS;
                
                // 重建世界空间法线
                float3 normalWS = TransformTangentToWorld(surfaceData.normalTS, half3x3(input.tangentWS, input.bitangentWS, input.normalWS));
                inputData.normalWS = NormalizeNormalPerPixel(normalWS);
                
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);

                // 5. 光照计算
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                
                // 雾效混合
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.uv = input.uv;
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}