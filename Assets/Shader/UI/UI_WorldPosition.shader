Shader "Custom/UGUI/URP_WorldPos_Visualizer_Rounded_UV"
{
    Properties
    {
        // -------------------------- 核心属性 --------------------------
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        // -------------------------- 圆角控制 (无需脚本) --------------------------
        // 圆角半径 (像素单位/世界单位)
        _Radius ("Corner Radius (Pixels)", Float) = 20
        
        // -------------------------- 可视化控制参数 --------------------------
        [Header(Visualization Settings)]
        _PosScale ("Position Scale (RGB)", Vector) = (0.005, 0.005, 0.005, 1)
        _PosOffset ("Position Offset", Vector) = (0, 0, 0, 0)
        [Toggle] _UseFrac ("Use Frac (Loop Color)", Float) = 0
        
        // -------------------------- UGUI 标准属性 --------------------------
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        
        _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _ClipRect ("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)
        
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendSrc ("Blend Src", Float) = 5 
        [Enum(UnityEngine.Rendering.BlendMode)] _BlendDst ("Blend Dst", Float) = 10 
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 4 
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0 
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZWrite ("Z Write", Float) = 0 
        
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
            Name "WorldPosVisualizer_Rounded_UV"
            Tags { "LightMode" = "Universal2D" }
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0 // 需要3.0以支持更好的导数计算
            
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            
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
                float4 worldPosition : TEXCOORD1;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;
            
            float4 _PosScale;
            float4 _PosOffset;
            float _UseFrac;
            float _Radius; // 像素单位
            
            float4 TransformObjectToClipPos(float3 pos)
            {
                float4x4 modelMatrix = unity_ObjectToWorld;
                float4x4 viewMatrix = UNITY_MATRIX_V;
                float4x4 projectionMatrix = UNITY_MATRIX_P;
                float4 worldPos = mul(modelMatrix, float4(pos, 1.0));
                float4 viewPos = mul(viewMatrix, worldPos);
                return mul(projectionMatrix, viewPos);
            }
            
            inline float UnityGet2DClipping(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position.xy) * step(position.xy, clipRect.zw);
                return inside.x * inside.y;
            }
            
            // SDF 矩形计算
            float RoundedRectSDF(float2 pos, float2 halfSize, float radius)
            {
                radius = min(radius, min(halfSize.x, halfSize.y));
                float2 d = abs(pos) - (halfSize - radius);
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
            }
            
            v2f vert(appdata_t v)
            {
                v2f o;
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.worldPosition = worldPos;
                o.vertex = TransformObjectToClipPos(v.vertex.xyz);
                o.texcoord = v.texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
                o.color = v.color * _Color;
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                half4 texColor = tex2D(_MainTex, i.texcoord);
                
                // 1. 世界坐标可视化
                float3 wPos = i.worldPosition.xyz + _PosOffset.xyz;
                float3 visualizationColor = wPos * _PosScale.xyz;
                
                if (_UseFrac > 0.5)
                visualizationColor = frac(abs(visualizationColor));
                
                half4 finalColor;
                finalColor.rgb = visualizationColor; 
                finalColor.a = texColor.a * i.color.a;
                
                // ----------------------------------------------------
                // 2. 无脚本圆角逻辑 (基于 UV 导数)
                // ----------------------------------------------------
                
                // 计算 UV 的变化率 (ddx, ddy) 和 世界坐标的变化率
                // fwidth = abs(ddx) + abs(ddy)
                float2 uvDeriv = fwidth(i.texcoord);
                float2 posDeriv = float2(length(fwidth(i.worldPosition.x)), length(fwidth(i.worldPosition.y)));
                
                // 避免除以0 (极少数情况下可能发生)
                uvDeriv = max(uvDeriv, 0.00001);
                
                // 计算 UI 实际尺寸 (Size = dPos / dUV)
                // 假设 UV X轴 对应 世界 X轴 (常规 UI 布局)
                float2 rectSize = posDeriv / uvDeriv;
                
                // 基于 UV 计算像素位置
                // (i.texcoord - 0.5) 将 UV 中心移到 (0,0)
                // * rectSize 将其放大到实际世界尺寸
                float2 pixelPos = (i.texcoord - 0.5) * rectSize;
                
                float2 halfSize = rectSize * 0.5;
                
                // 计算 SDF
                if (_Radius > 0)
                {
                    float dist = RoundedRectSDF(pixelPos, halfSize, _Radius);
                    float alphaMask = 1.0 - smoothstep(-0.5, 0.5, dist);
                    finalColor.a *= alphaMask;
                }
                
                // ----------------------------------------------------
                
                #ifdef UNITY_UI_CLIP_RECT
                finalColor.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                
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