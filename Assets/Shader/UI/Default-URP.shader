// URP管线下UGUI Mask兼容测试Shader
// 最终最终版：解决_Time重定义 + 未识别宏 + _StencilComp未声明
Shader "Custom/UGUI/Default-URP-Test"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        // -------------------------- 模板测试参数（Mask遮罩核心） --------------------------
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        
        // -------------------------- 深度与混合参数（UGUI原生默认值） --------------------------
        _ZWrite ("Z Write", Float) = 0
        _ZTest ("Z Test", Float) = 8
        _BlendSrc ("Blend Src", Float) = 5
        _BlendDst ("Blend Dst", Float) = 10
        
        // 可选：透明度硬裁剪阈值（应对Alpha Cut场景）
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        
        // 颜色掩码（UGUI标准属性，控制颜色写入）
        _ColorMask ("Color Mask", Float) = 15
    }
    
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        // 模板测试逻辑（Mask组件核心实现）
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        
        // 混合模式、深度设置（与UGUI原生行为对齐）
        Blend [_BlendSrc] [_BlendDst]
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Cull Off
        Lighting Off
        Fog { Mode Off }
        AlphaToMask Off
        ColorMask [_ColorMask] // 应用颜色掩码，控制颜色写入
        
        Pass
        {
            Name "UGUI-Forward"
            Tags { "LightMode" = "Universal2D" }
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNITY_UI_CLIP_RECT
            #pragma multi_compile _ UNITY_UI_ALPHACLIP
            
            // ==============================================
            // 核心：全量宏屏蔽（阻止所有内置头文件的包含，解决_Time重定义）
            // ==============================================
            #define SHADER_VARIABLES_CGINC_INCLUDED 1
            #define UNITY_CG_INCLUDED 1
            #define UNITY_UI_CGINC_INCLUDED 1
            #define TIME_CGINC_INCLUDED 1
            #define HLSLSUPPORT_CGINC_INCLUDED 1
            #define UNITY_SHADER_VARIABLES_CBUFFER 1
            
            // ==============================================
            // 手动实现UI顶点变换函数（替代URP的TransformObjectToHClip）
            // ==============================================
            float4 UnityUI_ObjectToClipPos(float3 pos)
            {
                // UGUI正交投影的MVP变换（仅使用Unity自动提供的矩阵，无外部依赖）
                float4 worldPos = mul(unity_ObjectToWorld, float4(pos, 1.0));
                float4 clipPos = mul(unity_MatrixVP, worldPos);
                return clipPos;
            }
            
            // -------------------------- 顶点输入结构体 --------------------------
            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };
            
            // -------------------------- 顶点输出结构体 --------------------------
            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                half2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };
            
            // -------------------------- 声明属性变量（关键：补充Stencil相关变量） --------------------------
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _ClipRect;
            float _Cutoff;
            
            // ===== 新增：声明所有Stencil相关变量（与Properties一一对应） =====
            float _StencilComp;
            float _Stencil;
            float _StencilOp;
            float _StencilWriteMask;
            float _StencilReadMask;
            
            // ===== 新增：声明深度/混合相关变量（若需要在着色器中使用，可选） =====
            float _ZWrite;
            float _ZTest;
            float _BlendSrc;
            float _BlendDst;
            float _ColorMask;
            
            // -------------------------- 手动实现工具函数 --------------------------
            #define TRANSFORM_TEX(tex, name) (tex.xy * name##_ST.xy + name##_ST.zw)
            
            inline fixed UnityGet2DClipping(float2 position, float4 clipRect)
            {
                #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(clipRect.xy, position.xy) * step(position.xy, clipRect.zw);
                return inside.x * inside.y;
                #else
                return 1.0;
                #endif
            }
            
            // -------------------------- 顶点着色器 --------------------------
            v2f vert(appdata_t v)
            {
                v2f o;
                
                o.worldPosition = v.vertex;
                o.vertex = UnityUI_ObjectToClipPos(v.vertex.xyz); // 手动变换函数
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                
                return o;
            }
            
            // -------------------------- 片元着色器 --------------------------
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.texcoord) * i.color;
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                
                // Stencil枚举值定义
                #define STENCIL_COMP_ALWAYS 0
                #define STENCIL_OP_KEEP 0
                
                // 现在可以正常访问_StencilComp和_StencilOp了
                bool isMaskObject = (_StencilComp < 0.5) && (_StencilOp > 0.5);
                
                if (isMaskObject)
                {
                    // Mask对象：忽略Alpha裁剪
                }
                else
                {
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - _Cutoff);
                    #endif
                }
                
                return color;
            }
            ENDCG
        }
    }
    
    // 可选：使用URP的空Shader作为FallBack（无依赖）
    // FallBack "Hidden/Universal Render Pipeline/Empty"
}