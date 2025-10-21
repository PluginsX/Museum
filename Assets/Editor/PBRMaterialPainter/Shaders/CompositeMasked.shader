Shader "Hidden/PBRPainter/CompositeMasked"
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

            sampler2D _UnderTex;
            sampler2D _MaskTex;
            float4 _Color; // target color (RGB) and alpha (intensity)
            float4 _ChannelMask; // RGBA selector

            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert (uint id : SV_VertexID)
            {
                v2f o; o.uv = float2((id << 1) & 2, id & 2); o.pos = float4(o.uv * 2 - 1, 0, 1); return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 u = tex2D(_UnderTex, i.uv);
                float m = tex2D(_MaskTex, i.uv).r * _Color.a;
                float4 target = float4(_Color.rgb, 1.0);
                float4 res = u + (target - u) * (m * _ChannelMask);
                res.a = saturate(1.0 - (1.0 - u.a) * (1.0 - m));
                return res;
            }
            ENDCG
        }
    }
}
