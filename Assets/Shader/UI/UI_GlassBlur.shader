Shader "UI/Custom/UI_GlassDistort"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (0.9, 0.9, 1, 0.7)
        _Distortion ("Distortion Intensity", Range(0, 1)) = 0.1
        
        // Mask组件兼容参数
        _StencilComp ("Stencil Comparison", Float) = 8
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilMask ("Stencil Mask", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    
    SubShader
    {
        Tags
        {
            "Queue"="Transparent+100" // 提高渲染顺序，确保抓取到前方的内容
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "UI"="True"
            // 内置管线注释这行，URP保留
            // "RenderPipeline"="UniversalPipeline"
        }
        
        Stencil
        {
            Ref [_StencilRef]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilMask]
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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            
            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 worldPos : TEXCOORD1;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Distortion; // 扭曲强度
            float4 _ClipRect;
            
            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                o.worldPos = v.vertex;
                
                return o;
            }
            
            
            
            fixed4 frag (v2f i) : SV_Target
            {
                // 1. 应用UI裁剪（Mask兼容）
                float clipAlpha = UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                if (clipAlpha <= 0) discard;
                
                // 2. 玻璃扭曲效果：使用UV坐标添加微小偏移
                float2 distortedUV = i.uv;
                distortedUV += sin(distortedUV * 10) * _Distortion * 0.01;
                
                // 3. 采样UI纹理（应用扭曲）
                fixed4 col = tex2D(_MainTex, distortedUV) * i.color;
                
                // 4. 增强透明度创建玻璃效果
                col.a *= clipAlpha;
                
                return col;
            }
            ENDCG
        }
    }
    FallBack "UI/Default"
}
