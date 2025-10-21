Shader "Hidden/PBRPainter/BrushStamp"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; // source
            float4 _BrushColor;
            float2 _CenterUV;
            float _RadiusUV;
            float _Hardness; // 0..1
            sampler2D _AlphaTex;
            float _UseAlphaTex;
            float4 _ChannelMask; // 1 where paint applies per channel (RGBA)

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (uint id : SV_VertexID)
            {
                v2f o;
                o.uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(o.uv * 2 - 1, 0, 1);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 baseCol = tex2D(_MainTex, i.uv);
                float d = distance(i.uv, _CenterUV) / max(_RadiusUV, 1e-6);
                float t = saturate(1.0 - d);
                float expv = lerp(2.0, 16.0, saturate(_Hardness));
                float soft = pow(t, expv);
                float aMask = 1.0;
                if (_UseAlphaTex > 0.5)
                {
                    float2 local = (i.uv - _CenterUV) / max(_RadiusUV, 1e-6);
                    float2 uv = local * 0.5 + 0.5; // map to [0,1]
                    aMask = tex2D(_AlphaTex, uv).r;
                }
                float a = saturate(soft * aMask * _BrushColor.a);

                float4 brush = float4(_BrushColor.rgb, 1.0);
                float4 res = baseCol;
                float4 delta = (brush - baseCol);
                res += delta * a * _ChannelMask;
                // Union alpha with stamp alpha
                res.a = saturate(1.0 - (1.0 - baseCol.a) * (1.0 - a));
                return res;
            }
            ENDCG
        }
    }
}
