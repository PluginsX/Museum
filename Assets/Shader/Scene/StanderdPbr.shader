// Unity URP Standard PBR Shader (Complete Fixed Version)
// Compatible with Unity 2022.3+, URP, and WebGL
Shader "Museum/Scene/StandardPBR"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Color Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        
        // 默认值 "bump" 保证无贴图时法线平坦
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0, 2)) = 1
        
        // ARM Map: R=Occlusion, G=Roughness, B=Metallic
        // 默认 white (1,1,1) 意味着：无AO，最大粗糙，最大金属(需要配合滑杆控制)
        _MaskMap ("ARM Map (AO/Roughness/Metallic)", 2D) = "white" {}
        
        // 核心 PBR 参数 (现在起作用了)
        _Metallic ("Metallic", Range(0.0, 1.0)) = 1.0
        _Roughness ("Roughness", Range(0.0, 1.0)) = 1.0
        _Occlusion ("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        
        [Header(Advanced Options)]
        [Toggle(_INVERT_ROUGHNESS)] _InvertRoughness ("Invert Roughness", Float) = 0
        [Toggle(_USE_OPENGL_NORMAL)] _UseOpenGLNormal ("Use OpenGL Normal (Invert Y)", Float) = 0
        
        [Header(Multipliers)]
        _OcclusionMultiplier ("Occlusion Multiplier", Range(0, 2)) = 1
        _RoughnessMultiplier ("Roughness Multiplier", Range(0, 2)) = 1
        _MetallicMultiplier ("Metallic Multiplier", Range(0, 2)) = 1
        
        [Header(Contrast)]
        _OcclusionContrast ("Occlusion Contrast", Range(0.5, 2)) = 1
        _RoughnessContrast ("Roughness Contrast", Range(0.5, 2)) = 1
        _MetallicContrast ("Metallic Contrast", Range(0.5, 2)) = 1
        
        [Header(Emission)]
        _EmissionColor ("Emission Color", Color) = (0, 0, 0)
        
        [Header(Render Settings)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2 // 2=Back, 0=Off(Double Sided)
        [Enum(LitAlpha,0, Premultiply,1, Additive,2)] _Surface("__surface", Float) = 0
        [ShowAsFloat] _Cutoff("__cutoff", Float) = 0.5
    }
    
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline" 
            "UniversalMaterialType" = "Lit" 
            "IgnoreProjector" = "True"
        }
        LOD 300
        
        Cull [_Cull]
        
        Pass
        {
            Name "ForwardLit"
            Tags{"LightMode" = "UniversalForward"}
            
            Blend Off
            ZTest LEqual
            ZWrite On
            
            HLSLPROGRAM
            #pragma target 2.0
            
            // --------------------------------------------------------
            // 核心修复：强制开启 NormalMap 宏
            // 必须在 include LitInput 之前定义，确保切线空间被计算
            #define _NORMALMAP 1
            // --------------------------------------------------------
            
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            
            #pragma shader_feature_local _INVERT_ROUGHNESS
            #pragma shader_feature_local _USE_OPENGL_NORMAL
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fragment _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            
            #pragma vertex LitPassVertex
            #pragma fragment LitStandardPBRPassFragment
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            
            // WebGL 兼容：将自定义变量放入 CBUFFER
            CBUFFER_START(UnityPerMaterial_Custom)
            float4 _MaskMap_ST;
            float _Roughness;
            float _Occlusion;
            float _OcclusionMultiplier;
            float _RoughnessMultiplier;
            float _MetallicMultiplier;
            float _OcclusionContrast;
            float _RoughnessContrast;
            float _MetallicContrast;
            float _Cull;
            CBUFFER_END
            
            TEXTURE2D(_MaskMap);
            SAMPLER(sampler_MaskMap);
            
            float ApplyContrast(float value, float contrast)
            {
                return (value - 0.5) * contrast + 0.5;
            }
            
            half4 LitStandardPBRPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                float2 uv = input.uv;
                
                // 1. 基础色
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                
                // 2. 法线 (因为定义了宏，这里必定执行)
                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uv);
                half3 normalTS = UnpackNormalScale(normalSample, _BumpScale);
                
                #ifdef _USE_OPENGL_NORMAL
                normalTS.y *= -1.0;
                #endif
                
                // 3. ARM 贴图与参数混合
                // 默认贴图为 White (1,1,1)
                half4 maskSample = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
                
                // --- 逻辑修复 ---
                // 使用乘法逻辑：最终值 = 贴图值 * 滑杆值 * 乘数
                // 这样当没贴图时(1.0)，滑杆值直接生效。
                
                // 粗糙度 G通道
                half roughness = maskSample.g * _Roughness * _RoughnessMultiplier;
                
                // 金属度 B通道
                half metallic = maskSample.b * _Metallic * _MetallicMultiplier;
                
                // AO R通道 (AO通常作为强度混合，而非直接乘法)
                // 贴图R值越小越黑。Lerp(1.0, map.r, strength)
                half aoStrength = _Occlusion * _OcclusionMultiplier;
                half occlusion = lerp(1.0, maskSample.r, aoStrength);
                
                // 4. 对比度调整
                roughness = saturate(ApplyContrast(roughness, _RoughnessContrast));
                metallic = saturate(ApplyContrast(metallic, _MetallicContrast));
                occlusion = saturate(ApplyContrast(occlusion, _OcclusionContrast));
                
                #ifdef _INVERT_ROUGHNESS
                roughness = 1.0 - roughness;
                #endif
                
                // 确保范围安全
                roughness = saturate(roughness);
                metallic = saturate(metallic);
                occlusion = saturate(occlusion);
                half smoothness = 1.0 - roughness;
                
                // 5. 填充 SurfaceData
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseColor.rgb;
                surfaceData.alpha = 1.0; 
                surfaceData.metallic = metallic;
                surfaceData.smoothness = smoothness;
                surfaceData.occlusion = occlusion;
                surfaceData.normalTS = normalTS; 
                surfaceData.emission = _EmissionColor.rgb;
                
                // 6. 计算输入数据 (TBN矩阵等)
                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                
                #ifdef _DBUFFER
                ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
                #endif
                
                // 7. PBR 光照计算
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                
                return color;
            }
            
            ENDHLSL
        }
        
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            
            ZWrite On ZTest LEqual Cull [_Cull]
            ColorMask 0
            
            HLSLPROGRAM
            #pragma target 2.0
            
            // 修复：使用正确的函数名 ShadowPassVertex
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            
            ENDHLSL
        }
        
        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode" = "DepthOnly"}
            
            ZWrite On
            ColorMask 0
            Cull [_Cull]
            
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        
        Pass
        {
            Name "DepthNormalsOnly"
            Tags {"LightMode" = "DepthNormalsOnly"}
            
            ZWrite On
            Cull [_Cull]
            
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            
            // 修复：DepthNormal Pass 同样需要强制开启 NormalMap 宏
            #define _NORMALMAP 1
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}