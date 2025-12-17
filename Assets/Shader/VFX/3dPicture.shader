Shader "Custom/URP/Pseudo3DParticle"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Color (RGB) Alpha (A)", 2D) = "white" {}
        _HighMap("Height Map (R: Height)", 2D) = "black" {}
        
        [Header(Parallax Settings)]
        _OffsetX("Offset X", Float) = 0
        _OffsetY("Offset Y", Float) = 0
        _ParallaxScale("Parallax Intensity", Range(0, 1)) = 0.1
        
        [Header(System Settings)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10 // OneMinusSrcAlpha
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 0 // Off
        _ZWrite("ZWrite", Float) = 0 // Off
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
            "PreviewType" = "Plane"
        }

        Blend [_SrcBlend] [_DstBlend]
        Cull [_Cull]
        ZWrite [_ZWrite]

        Pass
        {
            Name "Pseudo3D"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // 启用 GPU Instancing 支持（对粒子系统优化至关重要）
            #pragma multi_compile_instancing
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // 粒子系统传入的顶点颜色
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 纹理定义
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HighMap);
            SAMPLER(sampler_HighMap);

            // 变量定义 (CBUFFER 用于 SRP Batcher 优化)
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _OffsetX;
                float _OffsetY;
                float _ParallaxScale;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // 转换顶点位置到裁剪空间
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                
                // 应用 Tiling 和 Offset
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                
                // 传递顶点颜色（粒子系统的颜色和透明度控制）
                output.color = input.color;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 1. 采样高度图 (通常存储在 R 通道)
                // 这里使用原始 UV 采样高度，因为我们需要知道当前像素的"高度"
                half height = SAMPLE_TEXTURE2D(_HighMap, sampler_HighMap, input.uv).r;

                // 2. 计算偏移量
                // 原理：UV Offset = 输入偏移量 * 高度 * 强度系数
                // 高度越大(1.0)，偏移越多；高度越小(0.0)，偏移越少。
                // 这里的负号 "-" 取决于你的贴图移动方向，通常视差是反向移动背景
                float2 parallaxOffset = float2(_OffsetX, _OffsetY) * height * _ParallaxScale;

                // 3. 应用偏移到 UV
                float2 finalUV = input.uv + parallaxOffset;

                // 4. 采样基础颜色贴图
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);

                // 5. 混合粒子顶点颜色 (支持粒子系统的淡入淡出和染色)
                return baseColor * input.color;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}