Shader "Custom/Sand"
{
    Properties
    {
        // 沙子颗粒密度
        _GrainDensity("Grain Density", Range(0.1, 10.0)) = 1.0
        
        // 沙子颜色
        _SandColor("Sand Color", Color) = (0.8, 0.7, 0.6, 1)
        
        // 反光度
        _Specularity("Specularity", Range(0.0, 1.0)) = 0.2
        
        // 粗糙度
        _Roughness("Roughness", Range(0.0, 1.0)) = 0.8
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalRenderPipeline" }
        LOD 300
        
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
            float _GrainDensity;
            float4 _SandColor;
            float _Specularity;
            float _Roughness;
            CBUFFER_END
            
            // 随机噪声函数，用于模拟沙子颗粒
            float random(float2 st) {
                return frac(sin(dot(st.xy, float2(12.9898,78.233))) * 43758.5453);
            }
            
            // 渐变噪声，用于创建自然颗粒感
            float noise(float2 st) {
                float2 i = floor(st);
                float2 f = frac(st);
                
                float a = random(i);
                float b = random(i + float2(1.0, 0.0));
                float c = random(i + float2(0.0, 1.0));
                float d = random(i + float2(1.0, 1.0));
                
                float2 u = f * f * (3.0 - 2.0 * f);
                
                return lerp(a, b, u.x) + (c - a)* u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }
            
            float fbm(float2 st) {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 0.0;
                
                for (int i = 0; i < 4; i++) {
                    value += amplitude * noise(st);
                    st *= 2.0;
                    amplitude *= 0.5;
                }
                return value;
            }
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                
                return output;
            }
            
            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                // 计算世界空间中的自然照光因素
                SurfaceData surfaceData;
                surfaceData.albedo = _SandColor.rgb;
                surfaceData.metallic = 0.0h; // 沙子不反对金属
                surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
                surfaceData.smoothness = 1.0h - _Roughness;
                surfaceData.normalTS = half3(0, 0, 1); // 默认法线
                surfaceData.emission = half3(0.0h, 0.0h, 0.0h);
                surfaceData.occlusion = 1.0h;
                surfaceData.alpha = 1.0h;
                
                // 使用分形噪声生成颗粒效果
                float grainNoise = fbm(input.uv * _GrainDensity * 5.0);
                // 将噪声映射到颜色变化
                half colorVariation = grainNoise * 0.2h + 0.8h; // 在0.8-1.0范围内变化
                surfaceData.albedo *= colorVariation;
                
                // 轻微扰动法线以增加颗粒感
                half normalPerturbation = (grainNoise - 0.5h) * 0.1h * _GrainDensity;
                surfaceData.normalTS.x += normalPerturbation;
                surfaceData.normalTS.y += normalPerturbation;
                // 归一化扰动法线
                surfaceData.normalTS = normalize(surfaceData.normalTS);
                
                // 计算照明
                InputData inputData;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = input.normalWS;
                inputData.viewDirectionWS = GetCameraPositionWS() - input.positionWS;
                inputData.bakedGI = SampleSH(input.normalWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionHCS);
                inputData.staticLightmapUV = 0;
                inputData.dynamicLightmapUV = 0;
                inputData.shadowMask = 1; // 假设无阴影遮罩
                
                BRDFData brdfData;
                InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);
                
                half4 color = UniversalFragmentPBR(inputData, surfaceData.albedo, brdfData.metallic, brdfData.specular, brdfData.smoothness, surfaceData.emission, surfaceData.alpha);
                
                // 添加额外反光度控制
                color.rgb += _Specularity * 0.5h * dot(inputData.viewDirectionWS, input.normalWS);
                
                return color;
            }
            ENDHLSL
        }
        
        // 阴影渲染Pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            
            ZWrite On ZTest LEqual Cull Off
            
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        
        // 深度渲染Pass
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            
            ZWrite On ZTest LEqual Cull Off
            
            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
