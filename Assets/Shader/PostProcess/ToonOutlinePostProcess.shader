Shader "Custom/URP/ToonOutlinePostProcess"
{
    Properties
    {
        _MainTex ("Source Texture", 2D) = "white" {} // URP自动传入的原屏幕纹理
        _OutlineColor ("描边颜色", Color) = (0,0,0,1)
        _OutlineThickness ("描边厚度", Float) = 1.0
        _DepthSensitivity ("深度敏感度", Float) = 1.0
        _NormalSensitivity ("法线敏感度", Float) = 1.0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "RenderPipeline"="UniversalPipeline" 
            "Queue"="Transparent" 
        }
        LOD 100
        
        // 后处理必须的设置：关闭深度写入/测试，关闭裁剪
        ZTest Always 
        ZWrite Off 
        Cull Off
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0 // 适配低版本GPU
            
            // URP核心头文件
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SceneDepth.hlsl"
            
            // 顶点输入
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };
            
            // 顶点输出
            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float2 uvOffsetX    : TEXCOORD1; // X方向采样偏移
                float2 uvOffsetY    : TEXCOORD2; // Y方向采样偏移
            };
            
            // 材质参数
            CBUFFER_START(UnityPerMaterial)
            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // 纹理像素大小（自动赋值）
            float4 _OutlineColor;
            float _OutlineThickness;
            float _DepthSensitivity;
            float _NormalSensitivity;
            CBUFFER_END
            
            // 深度纹理采样
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            // 法线纹理采样（GBuffer2）
            TEXTURE2D(_CameraGBufferTexture2);
            SAMPLER(sampler_CameraGBufferTexture2);
            
            // 顶点着色器：计算采样偏移
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                
                // 计算基于屏幕分辨率的采样偏移（适配不同分辨率）
                float2 offset = _OutlineThickness * _MainTex_TexelSize.xy;
                output.uvOffsetX = input.uv + float2(offset.x, 0);
                output.uvOffsetY = input.uv + float2(0, offset.y);
                return output;
            }
            
            // 边缘检测：深度+法线
            float GetEdgeStrength(float2 uv)
            {
                // 1. 深度边缘检测
                float depth = SampleSceneDepth(uv);
                float depthX = SampleSceneDepth(uv + float2(_MainTex_TexelSize.x * _OutlineThickness, 0));
                float depthY = SampleSceneDepth(uv + float2(0, _MainTex_TexelSize.y * _OutlineThickness));
                float depthEdge = abs(depth - depthX) + abs(depth - depthY);
                depthEdge *= _DepthSensitivity;
                
                // 2. 法线边缘检测
                float3 normal = SAMPLE_TEXTURE2D(_CameraGBufferTexture2, sampler_CameraGBufferTexture2, uv).xyz * 2 - 1;
                float3 normalX = SAMPLE_TEXTURE2D(_CameraGBufferTexture2, sampler_CameraGBufferTexture2, uv + float2(_MainTex_TexelSize.x * _OutlineThickness, 0)).xyz * 2 - 1;
                float3 normalY = SAMPLE_TEXTURE2D(_CameraGBufferTexture2, sampler_CameraGBufferTexture2, uv + float2(0, _MainTex_TexelSize.y * _OutlineThickness)).xyz * 2 - 1;
                float normalEdge = (1 - dot(normal, normalX)) + (1 - dot(normal, normalY));
                normalEdge *= _NormalSensitivity;
                
                // 合并边缘强度
                return saturate(depthEdge + normalEdge);
            }
            
            // 片元着色器：叠加描边
            half4 frag(Varyings input) : SV_Target
            {
                // 采样原屏幕纹理
                half4 srcColor = SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv);
                // 计算边缘强度
                float edge = GetEdgeStrength(input.uv);
                // 叠加描边颜色
                half4 finalColor = lerp(srcColor, _OutlineColor, edge * _OutlineColor.a);
                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack Off
}