Shader "Custom/UGUI/URP_WorldPos_Visualizer_Fixed"
{
    Properties
    {
        // -------------------------- 核心属性 --------------------------
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        // -------------------------- 可视化控制参数 --------------------------
        [Header(Visualization Settings)]
        // 用于缩放世界坐标数值，避免颜色过曝。
        // 建议值：0.001 ~ 0.01 (取决于分辨率)
        _PosScale ("Position Scale (RGB)", Vector) = (0.005, 0.005, 0.005, 1)
        
        // 偏移量，用于调整颜色起始点
        _PosOffset ("Position Offset", Vector) = (0, 0, 0, 0)
        
        // 勾选此项使用 frac() 函数，使颜色产生循环条纹效果，方便观察坐标变化
        [Toggle] _UseFrac ("Use Frac (Loop Color)", Float) = 0
        
        // -------------------------- UGUI 标准属性 (Mask/Stencil) --------------------------
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        
        _ColorMask ("Color Mask", Float) = 15
        
        // RectMask2D 裁剪区域 (Unity自动传入)
        [HideInInspector] _ClipRect ("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)
        
        // 深度与混合
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendSrc ("Blend Src", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendDst ("Blend Dst", Float) = 10 // OneMinusSrcAlpha
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 4 // LEqual
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0 // Off
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZWrite ("Z Write", Float) = 0 // Off
        
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        
        Cull [_Cull]
        Lighting Off
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend [_BlendSrc] [_BlendDst]
        ColorMask [_ColorMask]
        
        Pass
        {
            Name "WorldPosVisualizer"
            Tags { "LightMode" = "Universal2D" }
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            
            // 编译变体
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            
            // -------------------------------------------------
            // 结构体定义 (已移除 Instancing 宏以修复报错)
            // -------------------------------------------------
            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };
            
            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1; // 传递世界坐标
            };
            
            // -------------------------------------------------
            // 变量声明
            // -------------------------------------------------
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;
            
            // 自定义变量
            float4 _PosScale;
            float4 _PosOffset;
            float _UseFrac;
            
            // -------------------------------------------------
            // 辅助函数 (手动实现以完全脱离 UnityCG.cginc)
            // -------------------------------------------------
            
            // 手动计算 MVP 变换
            float4 TransformObjectToClipPos(float3 pos)
            {
                // unity_ObjectToWorld 等矩阵是 Unity 内置自动绑定的，不需要头文件也能访问
                float4x4 modelMatrix = unity_ObjectToWorld;
                float4x4 viewMatrix = UNITY_MATRIX_V;
                float4x4 projectionMatrix = UNITY_MATRIX_P;
                
                float4 worldPos = mul(modelMatrix, float4(pos, 1.0));
                float4 viewPos = mul(viewMatrix, worldPos);
                return mul(projectionMatrix, viewPos);
            }
            
            // 手动实现 RectMask2D 裁剪检测
            inline float UnityGet2DClipping(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position.xy) * step(position.xy, clipRect.zw);
                return inside.x * inside.y;
            }
            
            // -------------------------------------------------
            // 顶点着色器
            // -------------------------------------------------
            v2f vert(appdata_t v)
            {
                v2f o;
                // 移除了 UNITY_SETUP_INSTANCE_ID(v);
                
                // 1. 计算世界坐标
                // 直接使用内置变量 unity_ObjectToWorld
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.worldPosition = worldPos;
                
                // 2. 转换到裁剪空间 (使用手动函数)
                o.vertex = TransformObjectToClipPos(v.vertex.xyz);
                
                // 3. 传递 UV 和 顶点颜色
                o.texcoord = v.texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
                o.color = v.color * _Color;
                
                return o;
            }
            
            // -------------------------------------------------
            // 片元着色器
            // -------------------------------------------------
            fixed4 frag(v2f i) : SV_Target
            {
                // 采样原始纹理
                half4 texColor = tex2D(_MainTex, i.texcoord);
                
                // 1. 获取世界坐标并应用偏移
                float3 wPos = i.worldPosition.xyz + _PosOffset.xyz;
                
                // 2. 缩放坐标映射为颜色
                float3 visualizationColor = wPos * _PosScale.xyz;
                
                // 3. 可选：循环条纹效果
                if (_UseFrac > 0.5)
                {
                    visualizationColor = frac(abs(visualizationColor));
                }
                
                // 4. 组合最终颜色
                half4 finalColor;
                finalColor.rgb = visualizationColor; 
                finalColor.a = texColor.a * i.color.a;
                
                // RectMask2D 裁剪
                #ifdef UNITY_UI_CLIP_RECT
                finalColor.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                
                // Alpha Clip
                #ifdef UNITY_UI_ALPHACLIP
                clip(finalColor.a - 0.001);
                #endif
                
                return finalColor;
            }
            ENDCG
        }
    }
    
    FallBack "UI/Default"
}