Shader "Universal Render Pipeline/Sand"
{
    Properties
    {
        // 沙子颗粒密度
        _GrainDensity("Grain Density", Range(0.1, 2.0)) = 0.8
        
        // 沙子颜色
        _BaseColor("Base Color", Color) = (0.8, 0.7, 0.6, 1)
        
        // 主纹理
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        
        // 反光度
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.2
        
        // 金属度
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        
        // 法线贴图
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        
        // 自发光贴图
        _EmissionColor("Emission", Color) = (0, 0, 0)
        
        // 透明度模式
        [Enum(LitAlpha,0, Premultiply,1, Additive,2)] _Surface("__surface", Float) = 0
        
        // 遮罩模式
        [ShowAsFloat] _Cutoff("__cutoff", Float) = 0.5
    }
    
    SubShader
    {
        Tags{"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True"}
        LOD 300
        
        Pass
        {
            Name "ForwardLit"
            Tags{"LightMode" = "UniversalForward"}
            
            Blend[_SrcBlend] OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZTest LEqual
            ZWrite[_ZWrite]
            
            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5
            
            // 定义自定义材质属性变量
            float _GrainDensity;
            
            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSS
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local _SPECULAR_SETUP
            #pragma shader_feature_local _CLEARCOLORMASK
            
            // -------------------------------------
            // Universal Pipeline keywords
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
            
            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            
            #pragma vertex LitPassVertex
            #pragma fragment LitSandPassFragment
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            
            // 噪声函数定义
            float random(float2 st) {
                return frac(sin(dot(st.xy, float2(12.9898,78.233))) * 43758.5453);
            }
            
            float noise(float2 st) {
                float2 i = floor(st);
                float2 f = frac(st);
                float a = random(i);
                float b = random(i + float2(1, 0));
                float c = random(i + float2(0, 1));
                float d = random(i + float2(1, 1));
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }
            
            float fbm(float2 st) {
                float value = 0;
                float amplitude = 0.5;
                for (int i = 0; i < 3; i++) {
                    value += amplitude * noise(st + _Time.y * 0.1);
                    st *= 2;
                    amplitude *= 0.5;
                }
                return value;
            }
            
            // 自定义片段着色器，以添加沙子效果
            half4 LitSandPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // 获取基本表面数据
                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);
                
                // 应用沙子噪声效果
                float noiseValue = fbm(input.uv * _GrainDensity * 3.0);
                float colorVariation = noiseValue * 0.3 + 0.85;
                surfaceData.albedo *= colorVariation;
                
                // 轻微扰动法线
                float normalPerturbation = (noiseValue - 0.5) * 0.2;
                surfaceData.normalTS.x += normalPerturbation;
                surfaceData.normalTS.y += normalPerturbation;
                
                surfaceData.occlusion *= colorVariation;
                
                
                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                
                #ifdef _DBUFFER
                ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
                #endif
                
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = surfaceData.alpha;
                
                return color;
            }
            
            ENDHLSL
        }
        
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            
            ZWrite On ZTest LEqual Cull Back
            ColorMask 0
            
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowCasterVertex
            #pragma fragment ShadowCasterFragment
            
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _EMISSION
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            
            float3 _LightDirection;
            float3 _LightPosition;
            
            struct AttributesShadow
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct VaryingsShadow
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                float3 viewDirectionWS : TEXCOORD1;
            };
            
            VaryingsShadow ShadowCasterVertex(AttributesShadow input)
            {
                VaryingsShadow output = (VaryingsShadow)0;
                UNITY_SETUP_INSTANCE_ID(input);
                
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                
                output.viewDirectionWS = GetWorldSpaceViewDir(mul(unity_ObjectToWorld, input.positionOS).xyz);
                return output;
            }
            
            void ShadowCasterFragment(VaryingsShadow input)
            {
                #ifdef _ALPHATEST_ON
                half alpha = tex2D(_BaseMap, input.uv).a;
                Alpha(alpha, _BaseColor, _Cutoff);
                #endif
                
                // Apply shadow caster operations
            }
            
            ENDHLSL
        }
        
        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode" = "DepthOnly"}
            
            ZWrite On
            ColorMask 0
            Cull[_Cull]
            
            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        
        Pass
        {
            Name "DepthNormalsOnly"
            Tags {"LightMode" = "DepthNormalsOnly"}
            
            ZWrite On
            Cull[_Cull]
            
            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA
            #pragma shader_feature_local _NORMALMAP
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 texcoord : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };
            
            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            
            float4 DepthNormalsFragment(Varyings input) : SV_TARGET
            {
                return float4(input.normalWS * 0.5 + 0.5, 1.0);
            }
            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    // 使用适当的后备着色器代替默认的VertexLit
    //CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.LitShader"
}
