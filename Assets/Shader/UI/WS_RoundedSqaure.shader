Shader "Museum/UI/roundedSqaure_Billboard"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        // -------------------------- 形状参数 (像素单位) --------------------------
        _RadiusTL ("Top Left Radius", Float) = 20
        _RadiusTR ("Top Right Radius", Float) = 20
        _RadiusBR ("Bottom Right Radius", Float) = 20
        _RadiusBL ("Bottom Left Radius", Float) = 20
        
        // 描边 (向内生长)
        _BorderWidth ("Border Width", Float) = 0
        _BorderColor ("Border Color", Color) = (1,1,1,1)
        
        // -------------------------- 3D 遮挡设置 --------------------------
        // LEqual(4) = 被模型遮挡, Always(8) = 透视显示
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 4 
        
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
            Name "UI_Rounded_DataDriven_Perfect"
            Tags { "LightMode" = "UniversalForward" } 
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            
            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 uv1      : TEXCOORD1; // 接收 HalfSize
            };
            
            struct v2f
            {
                float4 vertex   : SV_POSITION;
                half4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                nointerpolation float2 uiSizeData : TEXCOORD3; 
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            float4 _ClipRect;
            
            float _RadiusTL, _RadiusTR, _RadiusBR, _RadiusBL;
            float _BorderWidth;
            float4 _BorderColor;
            
            inline float UnityGet2DClipping(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position.xy) * step(position.xy, clipRect.zw);
                return inside.x * inside.y;
            }
            
            float RoundedRectSDF(float2 pos, float2 halfSize, float radius)
            {
                radius = min(radius, min(halfSize.x, halfSize.y));
                float2 d = abs(pos) - (halfSize - radius);
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
            }
            
            v2f vert(appdata_t v)
            {
                v2f o;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
                o.worldPosition = float4(vertexInput.positionWS, 1.0);
                o.vertex = vertexInput.positionCS;
                
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                
                // 从 C# 获取尺寸
                float2 size = v.uv1;
                if (length(size) < 0.001) size = float2(50, 50); 
                o.uiSizeData = size;
                
                return o;
            }
            
            half4 frag(v2f i) : SV_Target
            {
                float2 uv = i.texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
                
                // 1. 获取物理尺寸
                float2 halfSize = i.uiSizeData;
                float2 rectSize = halfSize * 2.0;
                
                // 2. 计算像素坐标 (相对于中心)
                float2 pixelPos = (uv - 0.5) * rectSize;
                
                // ----------------------------------------------------
                // 【核心修复】 内缩 (Padding) 逻辑
                // ----------------------------------------------------
                // 计算当前视角下，屏幕 1 像素对应的物理单位大小
                // fwidth(pixelPos.x) = ddx + ddy 的近似值
                float screenPixelSize = fwidth(pixelPos.x);
                
                // 我们需要向内收缩大约 0.5 到 1.0 个屏幕像素的距离
                // 这样抗锯齿产生的“虚边”才能落在 Mesh 网格内部，而不是被切掉
                float padding = max(screenPixelSize, 0.001) * 0.5;
                
                // 使用收缩后的尺寸计算形状
                float2 safeHalfSize = halfSize - padding;
                // ----------------------------------------------------
                
                float isRight = step(0.0, pixelPos.x);
                float isTop = step(0.0, pixelPos.y);
                float topRadius = lerp(_RadiusTL, _RadiusTR, isRight);
                float bottomRadius = lerp(_RadiusBL, _RadiusBR, isRight);
                float cornerRadius = lerp(bottomRadius, topRadius, isTop);
                
                // 确保内缩后圆角不为负
                cornerRadius = max(cornerRadius - padding, 0.0);
                
                // 3. 计算 SDF
                float dist = RoundedRectSDF(pixelPos, safeHalfSize, cornerRadius);
                
                // 4. 颜色与混合
                half4 mainTexColor = tex2D(_MainTex, uv);
                half3 bodyRGB = mainTexColor.rgb * i.color.rgb;
                float bodyAlpha = mainTexColor.a * i.color.a;
                
                half3 borderRGB = _BorderColor.rgb;
                float borderAlpha = _BorderColor.a * i.color.a;
                
                // 抗锯齿阈值 (使用之前的 screenPixelSize)
                float aa = max(screenPixelSize, 0.001); 
                
                // 整体外轮廓
                float shapeAlpha = 1.0 - smoothstep(-aa, aa, dist);
                
                // 独立描边区域判断
                float innerFactor = 1.0 - smoothstep(-aa, aa, dist + _BorderWidth);
                if (_BorderWidth <= 0.0) innerFactor = 1.0;
                
                // 颜色混合 (边缘用描边色，内部用底色)
                half3 finalRGB = lerp(borderRGB, bodyRGB, innerFactor);
                float finalAlpha = lerp(borderAlpha, bodyAlpha, innerFactor);
                
                finalAlpha *= shapeAlpha;
                
                half4 finalColor = half4(finalRGB, finalAlpha);
                
                #ifdef UNITY_UI_CLIP_RECT
                finalColor.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                
                #ifdef UNITY_UI_ALPHACLIP
                clip(finalColor.a - 0.001);
                #endif
                
                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "UI/Default"
}