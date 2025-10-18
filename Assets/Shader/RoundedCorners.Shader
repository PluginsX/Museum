Shader "UI/RoundedCornersBorder"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        [Header(Rounded Corners)]
        _Radius_TL ("Top Left Radius", Float) = 40.0
        _Radius_TR ("Top Right Radius", Float) = 40.0
        _Radius_BL ("Bottom Left Radius", Float) = 40.0
        _Radius_BR ("Bottom Right Radius", Float) = 40.0
        
        [Header(Border Settings)]
        _BorderWidth ("Border Width", Float) = 5.0
        _BorderColor ("Border Color", Color) = (1,1,1,1)
        
        [Header(Stencil)]
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        
        _ColorMask ("Color Mask", Float) = 15
        
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "RoundedCorners"
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 localPos : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            
            float _Radius_TL;
            float _Radius_TR;
            float _Radius_BL;
            float _Radius_BR;
            float _BorderWidth;
            fixed4 _BorderColor;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.localPos = v.vertex.xy;
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                OUT.worldPosition = v.vertex;
                
                return OUT;
            }

            float roundedCornerDistance(float2 pos, float2 size, float4 radii)
            {
                // 确定当前点所在的象限
                float2 quadrant = sign(pos);
                float radius = 0;
                
                // 根据象限选择对应的半径
                if (quadrant.x > 0 && quadrant.y > 0) radius = radii.x; // 第一象限 (TR)
                else if (quadrant.x < 0 && quadrant.y > 0) radius = radii.y; // 第二象限 (TL)
                else if (quadrant.x > 0 && quadrant.y < 0) radius = radii.z; // 第四象限 (BR)
                else radius = radii.w; // 第三象限 (BL)
                
                // 计算到圆角的距离
                float2 cornerCenter = (size - radius) * quadrant;
                float2 cornerToPoint = pos - cornerCenter;
                float distance = length(cornerToPoint) - radius;
                
                return distance;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 计算实际尺寸和半径
                float2 size = abs(IN.localPos * 2.0);
                float4 radii = float4(_Radius_TR, _Radius_TL, _Radius_BR, _Radius_BL);
                
                // 限制半径不超过最小尺寸
                radii = min(radii, min(size.x, size.y).xxxx);
                
                // 计算到最近圆角的距离
                float distance = roundedCornerDistance(IN.localPos, size, radii);
                
                // 主纹理采样
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                
                // 计算透明度
                float alpha = 1.0 - smoothstep(0.0, 1.0, distance); // 圆角区域透明
                
                // 计算边框
                float borderAlpha = 0.0;
                if (_BorderWidth > 0.01)
                {
                    borderAlpha = smoothstep(_BorderWidth + 0.5, _BorderWidth - 0.5, abs(distance));
                    borderAlpha *= _BorderColor.a;
                }
                
                // 组合效果
                color.a = min(alpha, borderAlpha);
                color.rgb = lerp(color.rgb, _BorderColor.rgb, borderAlpha);
                
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                
                return color;
            }
            ENDCG
        }
    }
    
    CustomEditor "UIRoundedCornersEditor"
}