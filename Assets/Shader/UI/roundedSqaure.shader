// 修复版圆角矩形Shader（URP兼容，完全避免_Time重定义）
// 支持独立控制四个圆角、边框效果、反向模式等
Shader "Custom/roundedSquare"
{
	Properties
	{
		// 主纹理：PerRendererData标签支持每个Image独立设置纹理（含图集）
		[PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
		[MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
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
		
		// Mask专用：是否使用Alpha裁剪（Mask对象通常需要禁用以确保透明像素能写入模板缓冲区）
		[MaterialToggle] _UseUIAlphaClip ("Use Alpha Clip", Float) = 1
		
		
		
		// -------------------------- 圆角相关参数 --------------------------
		// 圆角半径（基于0-1的相对值）
		_Radius ("Radius", Range (0, 0.5)) = 0
		
		// 四个角独立控制
		[MaterialToggle] _TR ("Top Right Corner", Float) = 1
		[MaterialToggle] _BR ("Bottom Right Corner", Float) = 1
		[MaterialToggle] _BL ("Bottom Left Corner", Float) = 1
		[MaterialToggle] _TL ("Top Left Corner", Float) = 1
		
		// 反向模式（用于制作镂空效果）
		[MaterialToggle] _Invert ("Invert", Float) = 0
		
		// 边框宽度（可选）
		_BorderWidth ("Border Width", Range (0, 0.5)) = 0
		_BorderColor ("Border Color", Color) = (0,0,0,1)
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
		ColorMask [_ColorMask]
		
		Pass
		{
			Name "roundedSquare"
			Tags { "LightMode" = "Universal2D" } // URP 2D渲染模式
			
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			// 批量编译宏：支持RectMask2D裁剪
			#pragma multi_compile _ UNITY_UI_CLIP_RECT
			// 批量编译宏：支持透明度硬裁剪
			#pragma multi_compile _ UNITY_UI_ALPHACLIP
			
			// 角控制变量
			uniform half _TR;
			uniform half _BR;
			uniform half _BL;
			uniform half _TL;
			uniform half _Invert;
			uniform half _Radius;
			uniform half _BorderWidth;
			uniform fixed4 _BorderColor;
			
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
			// Mask专用Alpha裁剪控制
			float _UseUIAlphaClip;
			
			// -------------------------- 模板测试参数（与Properties对应） --------------------------
			float _StencilComp;
			float _Stencil;
			float _StencilOp;
			float _StencilWriteMask;
			float _StencilReadMask;
			
			// -------------------------- 深度与混合参数（与Properties对应） --------------------------
			float _ZWrite;
			float _ZTest;
			float _BlendSrc;
			float _BlendDst;
			float _ColorMask;
			
			
			// -------------------------- 手动实现UnityUI.cginc中的函数 --------------------------
			// 2. 替代UnityUI.cginc中的UnityGet2DClipping函数（RectMask2D裁剪核心）
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
			
			// 手动实现UnityPixelSnap函数（简化版）
			float4 UnityPixelSnap(float4 pos)
			{
				// 简单的像素对齐实现，避免使用Unity内置函数
				float2 pixelPos = pos.xy * _ScreenParams.xy;
				pixelPos = round(pixelPos);
				pos.xy = pixelPos / _ScreenParams.xy;
				return pos;
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
				
				// PixelSnap功能（手动实现）
				#ifdef PIXELSNAP_ON
				o.vertex = UnityPixelSnap(o.vertex);
				#endif
				
				o.mask = v.texcoord;
				return o;
			}
			
			// 主片元着色器
			fixed4 frag(v2f IN) : SV_Target
			{
				// 采样纹理
				fixed4 texColor = tex2D(_MainTex, IN.mask);
				fixed4 finalColor = texColor * IN.color;
				
				// UV坐标范围：0-1
				half2 uv = IN.texcoord;
				
				// 默认所有像素都可见
				half show = 1.0;
				
				// 右上角圆角处理
				if (_TR > 0.5)
				{
					// 圆心位置：从角顶点向内偏移半径
					half2 circleCenter = half2(1.0 - _Radius, 1.0 - _Radius);
					// 如果像素在圆角区域内（x >= 圆心x 且 y >= 圆心y）
					half inCornerRegion = step(circleCenter.x, uv.x) * step(circleCenter.y, uv.y);
					if (inCornerRegion > 0.5)
					{
						// 计算到圆心的距离
						half dist = length(uv - circleCenter);
						// 如果距离大于半径，则该像素在圆角外部，应该透明
						show *= step(dist, _Radius);
					}
				}
				
				// 右下角圆角处理
				if (_BR > 0.5)
				{
					half2 circleCenter = half2(1.0 - _Radius, _Radius);
					half inCornerRegion = step(circleCenter.x, uv.x) * step(uv.y, circleCenter.y);
					if (inCornerRegion > 0.5)
					{
						half dist = length(uv - circleCenter);
						show *= step(dist, _Radius);
					}
				}
				
				// 左下角圆角处理
				if (_BL > 0.5)
				{
					half2 circleCenter = half2(_Radius, _Radius);
					half inCornerRegion = step(uv.x, circleCenter.x) * step(uv.y, circleCenter.y);
					if (inCornerRegion > 0.5)
					{
						half dist = length(uv - circleCenter);
						show *= step(dist, _Radius);
					}
				}
				
				// 左上角圆角处理
				if (_TL > 0.5)
				{
					half2 circleCenter = half2(_Radius, 1.0 - _Radius);
					half inCornerRegion = step(uv.x, circleCenter.x) * step(circleCenter.y, uv.y);
					if (inCornerRegion > 0.5)
					{
						half dist = length(uv - circleCenter);
						show *= step(dist, _Radius);
					}
				}
				
				// 反向模式处理
				if (_Invert > 0.5)
				{
					show = 1.0 - show;
				}
				
				// 边框处理（基于圆角形状的内描边）
				if (_BorderWidth > 0)
				{
					// 计算到圆角形状边界的距离
					half distToRoundedEdge = 1.0; // 初始化为最大距离
					
					// 检查是否在圆角区域内，如果是，计算到圆弧的距离
					if (_TR > 0.5 && uv.x >= 1.0 - _Radius && uv.y >= 1.0 - _Radius)
					{
						// 右上角圆角区域
						half2 circleCenter = half2(1.0 - _Radius, 1.0 - _Radius);
						half distToArc = _Radius - length(uv - circleCenter);
						distToRoundedEdge = min(distToRoundedEdge, distToArc);
					}
					else if (_BR > 0.5 && uv.x >= 1.0 - _Radius && uv.y <= _Radius)
					{
						// 右下角圆角区域
						half2 circleCenter = half2(1.0 - _Radius, _Radius);
						half distToArc = _Radius - length(uv - circleCenter);
						distToRoundedEdge = min(distToRoundedEdge, distToArc);
					}
					else if (_BL > 0.5 && uv.x <= _Radius && uv.y <= _Radius)
					{
						// 左下角圆角区域
						half2 circleCenter = half2(_Radius, _Radius);
						half distToArc = _Radius - length(uv - circleCenter);
						distToRoundedEdge = min(distToRoundedEdge, distToArc);
					}
					else if (_TL > 0.5 && uv.x <= _Radius && uv.y >= 1.0 - _Radius)
					{
						// 左上角圆角区域
						half2 circleCenter = half2(_Radius, 1.0 - _Radius);
						half distToArc = _Radius - length(uv - circleCenter);
						distToRoundedEdge = min(distToRoundedEdge, distToArc);
					}
					else
					{
						// 直边区域：计算到最近直边的距离
						half distToTop = 1.0 - uv.y;
						half distToBottom = uv.y;
						half distToLeft = uv.x;
						half distToRight = 1.0 - uv.x;
						
						// 选择最近的直边距离（内侧距离）
						distToRoundedEdge = min(min(distToTop, distToBottom), min(distToLeft, distToRight));
					}
					
					// 内描边：只有当距离小于等于边框宽度时才显示描边
					half isInnerBorder = step(distToRoundedEdge, _BorderWidth);
					
					// 混合边框颜色
					finalColor = lerp(finalColor, _BorderColor, isInnerBorder);
				}
				
				// 应用圆角遮罩
				finalColor.a *= show;
				
				// RectMask2D裁剪
				#ifdef UNITY_UI_CLIP_RECT
				finalColor.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
				#endif
				
				// 透明度硬裁剪
				#ifdef UNITY_UI_ALPHACLIP
				if (_UseUIAlphaClip > 0.5)
				clip(finalColor.a - _Cutoff);
				#endif
				
				return finalColor;
			}
			ENDCG
		}
	}
	
	// 降级处理
	Fallback "UI/Default"
}
