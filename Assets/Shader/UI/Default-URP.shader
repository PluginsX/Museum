// URP管线下UGUI基础兼容Shader模板（最小化实现）
// 完全兼容Mask、RectMask2D、Sprite图集、颜色叠加等UGUI核心功能
Shader "Custom/UGUI/Default-URP"
{
    Properties
    {
        // 主纹理：PerRendererData标签支持每个Image独立设置纹理（含图集）
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        // 颜色叠加：对应Image的Color属性
        _Color ("Tint", Color) = (1,1,1,1)
        
        // -------------------------- 模板测试参数（Mask遮罩核心） --------------------------
        // 默认值与UGUI原生一致：CompareFunction.Equal (8)
        _StencilComp ("Stencil Comparison", Float) = 8
        // 模板ID：与Mask组件的Stencil ID对应
        _Stencil ("Stencil ID", Float) = 0
        // 默认值：StencilOp.Keep (0)
        _StencilOp ("Stencil Operation", Float) = 0
        // 写入掩码：0-255，默认全写
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        // 读取掩码：0-255，默认全读
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        
        // -------------------------- 深度与混合参数（UGUI原生默认值） --------------------------
        // UGUI默认关闭深度写入
        _ZWrite ("Z Write", Float) = 0
        // 默认值：CompareFunction.LessEqual (8)
        _ZTest ("Z Test", Float) = 8
        // 默认值：BlendMode.SrcAlpha (5)
        _BlendSrc ("Blend Src", Float) = 5
        // 默认值：BlendMode.OneMinusSrcAlpha (10)
        _BlendDst ("Blend Dst", Float) = 10
        
        // 可选：透明度硬裁剪阈值（应对Alpha Cut场景）
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        
        // 可选：颜色掩码（UGUI标准属性）
        _ColorMask ("Color Mask", Float) = 15
    }
    
    SubShader
    {
        Tags
        {
            // UGUI透明渲染队列（与原生UI对齐）
            "Queue" = "Transparent"
            // 忽略投影器（UGUI默认）
            "IgnoreProjector" = "True"
            // 透明渲染类型（后处理/批处理识别）
            "RenderType" = "Transparent"
            // Sprite预览类型（编辑器中显示正确预览）
            "PreviewType" = "Plane"
            // 支持Sprite图集批处理（UGUI必备）
            "CanUseSpriteAtlas" = "True"
            // 标记为URP管线专属（必加）
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
        // 禁用背面裁剪（Sprite可能有双面渲染需求）
        Cull Off
        // 禁用光照（UGUI是2D UI，无需光照）
        Lighting Off
        // 禁用雾效（UGUI默认）
        Fog { Mode Off }
        // 关闭AlphaToMask（避免与透明度混合冲突）
        AlphaToMask Off
        
        Pass
        {
            Name "UGUI-Forward"
            // URP 2D光照模式（无光照开销，适配UGUI）
            Tags { "LightMode" = "Universal2D" }
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 批量编译宏：支持RectMask2D裁剪
            #pragma multi_compile _ UNITY_UI_CLIP_RECT
            // 批量编译宏：支持透明度硬裁剪
            #pragma multi_compile _ UNITY_UI_ALPHACLIP
            
            // -------------------------- 顶点输入结构体（兼容UGUI顶点格式） --------------------------
            struct appdata_t
            {
                // 顶点位置（模型空间）
                float4 vertex   : POSITION;
                // 顶点颜色（UGUI Image的Color传递到这里）
                float4 color    : COLOR;
                // UV坐标
                float2 texcoord : TEXCOORD0;
            };
            
            // -------------------------- 顶点输出结构体 --------------------------
            struct v2f
            {
                // 裁剪空间顶点位置
                float4 vertex   : SV_POSITION;
                // 传递顶点颜色
                fixed4 color    : COLOR;
                // 传递UV坐标
                half2 texcoord  : TEXCOORD0;
                // 世界空间位置（用于RectMask2D裁剪）
                float4 worldPosition : TEXCOORD1;
            };
            
            // -------------------------- 声明属性变量（与Properties对应） --------------------------
            sampler2D _MainTex;
            // 纹理缩放和平移参数
            float4 _MainTex_ST;
            // 颜色叠加参数
            float4 _Color;
            // RectMask2D裁剪矩形参数（UGUI自动传入）
            float4 _ClipRect;
            // 透明度裁剪阈值
            float _Cutoff;
            
            // -------------------------- 手动实现核心工具函数 --------------------------
            // 手动实现TRANSFORM_TEX宏
            #define TRANSFORM_TEX(tex, name) (tex.xy * name##_ST.xy + name##_ST.zw)
            
            // 手动实现模型空间到裁剪空间的转换
            float4 TransformObjectToClipPos(float3 pos)
            {
                // 手动实现MVP矩阵变换，避免使用Unity内置函数
                float4x4 modelMatrix = unity_ObjectToWorld;
                float4x4 viewMatrix = UNITY_MATRIX_V;
                float4x4 projectionMatrix = UNITY_MATRIX_P;
                
                float4 worldPos = mul(modelMatrix, float4(pos, 1.0));
                float4 viewPos = mul(viewMatrix, worldPos);
                float4 clipPos = mul(projectionMatrix, viewPos);
                
                return clipPos;
            }
            
            // RectMask2D裁剪函数（手动实现，避免UnityUI.cginc）
            inline fixed UnityGet2DClipping(float2 position, float4 clipRect)
            {
                #ifdef UNITY_UI_CLIP_RECT
                // 裁剪矩形：x=左, y=下, z=右, w=上
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
                
                // 计算世界空间位置（用于RectMask2D裁剪）
                o.worldPosition = v.vertex;
                // URP专属：模型空间转裁剪空间
                o.vertex = TransformObjectToClipPos(v.vertex.xyz);
                // 计算UV坐标（支持纹理缩放和平移）
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                // 顶点颜色叠加（Image Color * 全局_Color）
                o.color = v.color * _Color;
                
                return o;
            }
            
            // -------------------------- 片元着色器 --------------------------
            fixed4 frag(v2f i) : SV_Target
            {
                // 采样主纹理（Sprite/图集纹理）并叠加颜色
                fixed4 color = tex2D(_MainTex, i.texcoord) * i.color;
                
                // RectMask2D裁剪：剔除区域外的像素透明度
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                
                // 透明度硬裁剪（开启UNITY_UI_ALPHACLIP时生效）
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - _Cutoff);
                #endif
                
                return color;
            }
            ENDCG
        }
    }
    
    // 降级处理：Shader不支持时，使用UGUI原生的UI/Default
    FallBack "UI/Default"
}
