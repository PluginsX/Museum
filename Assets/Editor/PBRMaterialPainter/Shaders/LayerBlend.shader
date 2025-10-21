Shader "Hidden/PBRPainter/LayerBlend"
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
            sampler2D _LayerTex;

            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert (uint id : SV_VertexID)
            {
                v2f o; o.uv = float2((id << 1) & 2, id & 2); o.pos = float4(o.uv * 2 - 1, 0, 1); return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 u = tex2D(_UnderTex, i.uv);
                float4 l = tex2D(_LayerTex, i.uv);
                float a = saturate(l.a);
                float3 rgb = lerp(u.rgb, l.rgb, a);
                float alpha = saturate(1.0 - (1.0 - u.a) * (1.0 - a));
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
}
