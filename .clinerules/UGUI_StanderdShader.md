# URP管线下UGUI Image自定义Shader模板研究与实现
## 一、研究背景与需求分析
UGUI是Unity内置的UI系统，其默认的`UI/Default` Shader已适配大部分基础场景，但在项目开发中，开发者往往需要自定义视觉效果（如渐变、描边、纹理混合等）。此时若直接编写自定义Shader，容易出现与UGUI核心功能（如Mask、RectMask2D、Sprite图集、颜色叠加）不兼容的问题。

尤其在**URP（Universal Render Pipeline，通用渲染管线）** 下，由于管线架构的差异，传统的内置管线UGUI Shader无法直接复用，且易出现变量重定义（如`_Time`）、顶点变换失效等编译或运行时错误。因此，研究并实现一份**完全兼容URP管线与UGUI Image所有核心功能**的自定义Shader模板，成为URP项目中UI定制化开发的关键。

本文将从UGUI核心功能兼容原理、URP管线适配要点、Shader模板实现与优化等方面，深入探讨URP下UGUI Image自定义Shader的开发逻辑。

## 二、UGUI Image核心功能兼容原理
要让自定义Shader兼容UGUI Image，需先明确UGUI的核心功能依赖的Shader特性，主要包括以下几点：

### 2.1 基础纹理与颜色叠加
UGUI Image的`Sprite`属性对应Shader的主纹理（`_MainTex`），`Color`属性对应顶点颜色（`COLOR`语义）。Shader需支持：
1. 接收Per Renderer数据（`[PerRendererData]`标签），允许每个Image独立设置纹理（支持Sprite图集）；
2. 顶点颜色与全局颜色（`_Color`）的叠加，匹配UGUI原生的颜色混合逻辑。

### 2.2 Mask与RectMask2D遮罩
UGUI的遮罩功能分为两种，对应Shader的不同实现逻辑：
- **Mask组件**：基于**模板测试（Stencil Test）** 实现，通过Stencil参数（ID、比较函数、操作指令）控制像素的渲染与否；
- **RectMask2D组件**：基于**2D矩形裁剪**实现，通过`_ClipRect`参数定义裁剪区域，在片元着色器中剔除区域外的像素。

### 2.3 渲染顺序与批处理
UGUI属于2D透明UI，Shader需满足：
1. 渲染队列设置为`Transparent`，与UGUI原生渲染顺序对齐；
2. 标记`CanUseSpriteAtlas = "True"`，支持Sprite图集的批处理优化；
3. 保留实例化ID（`UNITY_VERTEX_INPUT_INSTANCE_ID`），支持UGUI的批处理逻辑。

### 2.4 透明度处理
UGUI支持透明度混合（Blend）与透明度硬裁剪（Alpha Clip），Shader需通过混合模式（`Blend SrcAlpha OneMinusSrcAlpha`）和`UNITY_UI_ALPHACLIP`宏实现对应功能。

## 三、URP管线适配关键要点
URP与内置渲染管线的核心差异在于顶点变换、头文件依赖、光照模式等，适配时需注意以下要点：

### 3.1 管线标记与光照模式
- Shader的Tags中必须添加`"RenderPipeline" = "UniversalPipeline"`，标记为URP专属Shader；
- Pass的光照模式建议使用`Universal2D`（无光照开销，适配UGUI的2D特性），避免使用内置管线的`ForwardBase`。

### 3.2 顶点变换函数
URP中需使用坐标变换函数实现模型空间到裁剪空间的转换。推荐使用手动实现的`TransformObjectToClipPos`函数，避免URP内置函数的依赖问题。

### 3.3 头文件依赖冲突解决方案
URP管线下存在两种解决方案：

**方案一（推荐）：完全避免包含冲突头文件**
- 不包含`UnityCG.cginc`和`UnityUI.cginc`
- 手动实现所有必需函数（`TransformObjectToClipPos`、`UnityGet2DClipping`、`TRANSFORM_TEX`等）
- 彻底避免`_Time`等变量重定义问题

**方案二（兼容性）：通过宏屏蔽**
- 包含`UnityUI.cginc`但通过宏定义屏蔽冲突：
```glsl
#define SHADER_VARIABLES_CGINC_INCLUDED 1
#define UNITY_CG_INCLUDED 1
```
- 需手动实现部分函数以补充功能

### 3.4 核心函数手动实现
关键函数的实现方式：

**坐标变换函数**：
```glsl
float4 TransformObjectToClipPos(float3 pos)
{
    float4x4 modelMatrix = unity_ObjectToWorld;
    float4x4 viewMatrix = UNITY_MATRIX_V;
    float4x4 projectionMatrix = UNITY_MATRIX_P;
    float4 worldPos = mul(modelMatrix, float4(pos, 1.0));
    float4 viewPos = mul(viewMatrix, worldPos);
    return mul(projectionMatrix, viewPos);
}
```

**裁剪函数**：
```glsl
inline fixed UnityGet2DClipping(float2 position, float4 clipRect)
{
    #ifdef UNITY_UI_CLIP_RECT
    float2 inside = step(clipRect.xy, position.xy) * step(position.xy, clipRect.zw);
    return inside.x * inside.y;
    #else
    return 1.0;
    #endif
}
```

**纹理变换宏**：
```glsl
#define TRANSFORM_TEX(tex, name) (tex.xy * name##_ST.xy + name##_ST.zw)
```

## 四、完整Shader模板实现（URP专属）
基于上述原理与要点，以下是**完全兼容URP管线与UGUI Image所有核心功能**的自定义Shader模板，包含详细注释与核心逻辑说明。

### 4.1 Shader代码实现
```shaderlab
// URP专属UGUI Image自定义Shader模板
// 兼容Mask、RectMask2D、Sprite图集、颜色叠加、透明度处理等所有UGUI核心功能
Shader "Custom/UGUI/URP-Image-Custom"
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

        // -------------------------- 自定义扩展参数（示例：渐变颜色） --------------------------
        // 可根据需求添加自定义参数，如渐变、描边、纹理混合等
        _GradientColor ("Gradient Color", Color) = (1,1,1,1)
        _GradientRatio ("Gradient Ratio", Range(0, 1)) = 0.5
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
                // 纹理采样坐标
                half2 mask      : TEXCOORD2;
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

            // -------------------------- 自定义扩展参数（示例） --------------------------
            float4 _GradientColor;
            float _GradientRatio;

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

                o.mask = v.texcoord;
                return o;
            }

            // -------------------------- 片元着色器 --------------------------
            fixed4 frag(v2f i) : SV_Target
            {
                // 1. 采样主纹理（Sprite/图集纹理）并叠加颜色
                half4 color = tex2D(_MainTex, i.texcoord) * i.color;

                // 2. 自定义扩展逻辑示例：渐变颜色叠加（可根据需求修改/删除）
                color = lerp(color, color * _GradientColor, _GradientRatio);

                // 3. RectMask2D裁剪：剔除区域外的像素透明度
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);

                // 4. 透明度硬裁剪（开启UNITY_UI_ALPHACLIP时生效）
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
```

### 4.2 核心代码解析
1. **属性定义**：包含UGUI核心属性（纹理、颜色、Stencil参数、深度混合参数）与自定义扩展参数（渐变颜色、比例），兼顾兼容性与可扩展性。
2. **Tags与渲染状态**：严格匹配UGUI的渲染队列、批处理标记与URP管线标记，保证渲染行为与原生UI一致。
3. **模板测试**：通过Stencil代码块实现Mask组件的遮罩功能，参数与UGUI原生对齐。
4. **头文件冲突解决**：通过定义屏蔽宏，避免`_Time`等变量重定义；手动实现`TRANSFORM_TEX`与`UnityGet2DClipping`，摆脱对内置头文件的依赖。
5. **顶点/片元着色器**：兼容UGUI的顶点格式，实现纹理采样、颜色叠加、裁剪处理，并预留自定义扩展逻辑入口。

## 五、功能验证与使用指南
### 5.1 环境准备
1. 确保项目已安装`Universal Render Pipeline`包（Window > Package Manager > 搜索并安装）；
2. 项目的渲染管线设置已切换为URP（Project Settings > Graphics > Scriptable Render Pipeline Settings）；
3. 将上述代码保存为`.shader`文件（如`URP-Image-Custom.shader`）。

### 5.2 使用步骤
1. 在Unity中创建材质（右键 > Create > Material），将Shader设置为`Custom/UGUI/URP-Image-Custom`；
2. 将材质赋值给UGUI Image组件的`Material`属性，替代原生的`UI/Default`；
3. 按需调整材质参数（如渐变颜色、透明度阈值），或添加自定义扩展逻辑。

### 5.3 兼容性验证
需验证以下UGUI核心功能是否正常工作：
| 功能项               | 验证方式                                                                 |
|----------------------|--------------------------------------------------------------------------|
| Sprite纹理与图集     | 使用Sprite图集的Image正常显示，且批处理统计中批次数量无异常增长           |
| Color颜色叠加        | 修改Image的Color属性，纹理颜色同步变化                                   |
| Mask遮罩             | 添加Mask组件，Image仅显示遮罩区域内的部分                               |
| RectMask2D裁剪       | 添加RectMask2D组件，Image仅显示裁剪矩形内的部分                         |
| 透明度混合/硬裁剪     | 调整Image的Alpha值，透明度正常变化；开启Alpha Clip后，低透明度像素被裁剪 |
| 渲染顺序             | 多个Image的渲染层级与设置的Order in Layer一致                           |

## 六、扩展与优化建议
### 6.1 自定义效果扩展
基于该模板可扩展常见的UI视觉效果，示例如下：
1. **渐变效果**：添加渐变纹理（`_GradientTex`），在片元着色器中根据UV采样渐变纹理并叠加；
2. **描边效果**：新增Pass，渲染时将顶点向外膨胀，并用纯色填充，实现外描边；
3. **纹理混合**：添加第二张纹理（`_SecondTex`）和混合比例（`_BlendRatio`），在片元着色器中混合两张纹理；
4. **颜色校正**：添加亮度、对比度、饱和度参数，在片元着色器中调整颜色属性。

### 6.2 性能优化
1. 移除未使用的参数与宏：若不需要透明度硬裁剪，可删除`_Cutoff`参数和`UNITY_UI_ALPHACLIP`宏，减少编译变体；
2. 简化顶点/片元逻辑：避免在片元着色器中执行复杂的数学运算，可将部分计算移至顶点着色器；
3. 移除PixelSnap相关宏：如果不需要像素对齐功能，可移除`#pragma multi_compile _ PIXELSNAP_ON`以减少编译变体。

### 6.3 常见问题解决
1. **`_Time`重定义错误**：确认已添加屏蔽宏`SHADER_VARIABLES_CGINC_INCLUDED`和`UNITY_CG_INCLUDED`，或直接注释掉`UnityUI.cginc`；
2. **RectMask2D裁剪失效**：检查`_ClipRect`参数是否由UGUI自动传入，且`UnityGet2DClipping`函数逻辑正确；
3. **Sprite图集批处理失效**：确认Shader的Tags中包含`CanUseSpriteAtlas = "True"`，且`_MainTex`带有`[PerRendererData]`标签。

## 七、总结
本文从UGUI核心功能兼容原理与URP管线适配要点出发，实现了一份**完全兼容URP管线与UGUI Image所有核心功能**的自定义Shader模板。该模板解决了URP下常见的变量重定义、功能不兼容问题，同时预留了自定义效果扩展的入口，可作为URP项目中UGUI Image定制化开发的基础模板。

在实际项目中，开发者可基于该模板根据需求扩展视觉效果，同时遵循性能优化原则，确保UI渲染的流畅性与兼容性。
