using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;

[System.Serializable]
public class MaterialLayer
{
    public string layerName = "New Layer";
    public bool visible = true;
    public bool expanded = false;
    public bool isActive = false;
    public Material LayerMaterial = null;
    [SerializeField] public List<TextureMap> textureMaps = new List<TextureMap>();
}

[System.Serializable]
public class TextureMap
{
    public int index;
    public string mapName;
    public TextureData textureData = new TextureData();
    public bool isPainting = false;
    public bool isolateThisMap = false; // 新增：子属性图层孤立显示
}

[System.Serializable]
public class TextureData
{
    public Texture2D sourceTexture;
    [HideInInspector] public Texture2D paintTexture;
    public bool isModified = false;
}

public enum PaintMode { Brush, Fill }
public enum ProjectionMode { Screen, NormalAligned }
public enum FillTarget { EntireObject, Element, UVIsland }

public class MeshPainter : EditorWindow
{
    static int OpenCount = 0;//工具打开次数

    
    /***
     * 检测是否为内置Texture2D参数
     */
    public static bool IsBuiltInTexture2D(string propertyName)
    {
        // Unity内置Texture2D参数列表
        string[] builtInTexture2Ds = 
        {
            // 标准管线
            "unity_Lightmap", "unity_LightmapInd",
            "unity_ShadowMask", "unity_DynamicLightmap",
            "unity_DynamicDirectionality", "unity_DynamicNormal",
            "_GrabTexture", "_CameraDepthTexture",
            "_CameraDepthNormalsTexture", "_CameraMotionVectorsTexture",
            "_CameraOpaqueTexture", "_PrevCameraOpaqueTexture",
            "_CloudShadowTexture", "_DitherMaskTexture",
            
            // URP专用
            "_ScreenSpaceOcclusionTexture", "_SSRTexture",
            "_SSRTransparentTexture", "_DBufferTexture0",
            "_DBufferTexture1", "_DBufferTexture2",
            "_DBufferTexture3", "_BlitTexture",
            "_BlitScaleBias", "_BlitScaleBiasRt",
            "_SourceTex", "_MainLightShadowmapTexture",
            "_AdditionalLightsShadowmapTexture", "_ShadowmapTexture",
            
            // HDRP专用
            "_AmbientOcclusionTexture", "_DepthPyramidTexture",
            "_AlphaPyramidTexture", "_MotionVectorTexture",
            "_NormalPyramidTexture", "_ColorPyramidTexture",
            "_VolumetricLightingTexture", "_DistortionTexture",
            "_DistortionDepthTexture", "_ExposureTexture",
            
            // 后处理/特效
            "_NoiseTexture", "_RampTexture",
            "_FogTexture", "_HalftonePattern",
            "_VignetteMask", "_Lut2D",
            "_Lut3D", "_UserLut2D",
            
            // 旧版/备用
            "_LightTexture", "_LightTexture0",
            "_LightTextureB0", "_ShadowMapTexture"
        };

        // 检查是否匹配内置参数（不区分大小写）
        return builtInTexture2Ds.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
    }

    /***
     * UI样式定义
     */
    private GUIStyle activeLayerStyle = new GUIStyle();
    private GUIStyle activeMapStyle = new GUIStyle();
    private GUIStyle defaultLayerStyle = new GUIStyle();
    private GUIStyle defaultMapStyle = new GUIStyle();
    private GUIStyle toggleActiveStyle = new GUIStyle();
    private GUIStyle toggleDisabledStyle = new GUIStyle();

    private static GameObject target_Object;// 目标对象
    private static Mesh target_Mesh;//目标对象的Mesh组件
    private static Renderer target_Renderer;// 目标对象渲染器
    private static Material target_OriginalMaterial;// 目标对象原始材质
    private static Shader targetShader;//目标对象的Shader
    private static bool isUsingURP;//是否为UPR管线

    private static Material baseRefMaterial;// 基础参考材质：目标对象原始材质的默认参数值
    private static Material previewMaterial_Isolation; // 用于预览单个图层的简单材质
    private static Material previewMaterial_Final; // 复制基础参考材质，用于绘制

    [SerializeField]
    private bool lockbaseRefMaterial = false; // 锁定基础材质
    [SerializeField]
    private List<MaterialLayer> materialLayers = new List<MaterialLayer>();//材质图层列表

    
    
    private bool isPaintingMode = false;//绘制模式
    private bool isIsolatedMode = false;//孤立模式

    private PaintMode currentPaintMode = PaintMode.Brush;//画笔模式：默认为笔刷
    private ProjectionMode currentProjectionMode = ProjectionMode.Screen;//画笔投影模式：默认为屏幕
    private FillTarget currentFillTarget = FillTarget.EntireObject;//画笔填充对象
    
    private Color brushColor = Color.white;//默认笔刷颜色
    private float brushSize = 0.1f;//默认笔刷尺寸
    private float brushHardness = 0.5f;//默认笔刷硬度
    private Texture2D brushMask;//默认笔刷遮罩
    private bool useMaskGrayscale = true; // 只提取遮罩灰度开关
    private float brushSpacing = 0.001f; // 笔刷间距（单位：米）
    private Vector3 lastPaintPosition = Vector3.one * float.MaxValue; // 上次绘制位置
    
    private int selectedLayerIndex = -1;//当前选中的材质图层
    private int selectedMapIndex = -1;//当前选中的参数图层
    
    private Vector2 scrollPosition;//光标滑动位置

    private int textureSize = 1024;//默认创建纹理尺寸
    private bool isMouseDragging = false;//当前鼠标正在拖拽
    private Vector3 currentCursorPosition; // 当前光标位置
    private bool isCursorVisible = false;  // 光标是否可见
    private bool ExitAutoDestory = false;  //关闭窗口自动清理材质

    // 工具启动函数
    [MenuItem("Window/LNU数字艺术实验室/Mesh Painter")]
    public static void ShowWindow()
    {
        //创建本编辑器窗口类
        GetWindow<MeshPainter>("Mesh Painter");
    }

    // 工具启用
    private void OnEnable()
    {
        //测试
        UnityEngine.Debug.Log($"第{OpenCount++}次打开工具");

        // 检测项目是否为URP管线
        isUsingURP = GraphicsSettings.currentRenderPipeline != null && GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal");

        // 挂载场景内UI内容绘制函数
        SceneView.duringSceneGui += OnSceneGUI;
        
        // 延迟一帧初始化样式，确保EditorStyles可用
        EditorApplication.delayCall += InitializeStyles;
        // 上一次光标绘制位置
        lastPaintPosition = Vector3.one * float.MaxValue; // 新增初始化
    }

    // 窗口关闭
    private void OnDestroy()
    {
        // 重置绘制模式
        isPaintingMode = false;//

        // 恢复原始材质
        if (target_Renderer != null && target_OriginalMaterial != null)
        {
            target_Renderer.sharedMaterial = target_OriginalMaterial;
            UnityEngine.Debug.Log("OnDestroy()  -   恢复目标对象原始材质");
        }
        
        // 退出孤立模式
        if (isIsolatedMode)
        {
            IsolateObject(false);
            isIsolatedMode = false;
            UnityEngine.Debug.Log("OnDestroy()  -   退出孤立模式");
        }

        // 退出窗口时自动清理材质和贴图
        if(ExitAutoDestory){
            // 销毁绘制材质实例
            if (previewMaterial_Final != null)
            {
                DestroyImmediate(previewMaterial_Final);
                previewMaterial_Final = null;
                UnityEngine.Debug.Log("OnDestroy()  -   销毁绘制材质实例");
            }
            // 销毁单个参数图层预览材质
            if (previewMaterial_Isolation != null)
            {
                DestroyImmediate(previewMaterial_Isolation);
                previewMaterial_Isolation = null;
                UnityEngine.Debug.Log("OnDestroy()  -   销毁单个参数图层预览材质");
            }
            // 销毁参数图层
            ClearAllPaintTextures();
        }
        
        
    }

    // 初始化UI样式
    private void InitializeStyles()
    {
        // 安全检查：确保EditorStyles.helpBox已初始化
        if (EditorStyles.helpBox == null)
        {
            // 如果仍未初始化，再延迟一帧
            EditorApplication.delayCall += InitializeStyles;
            return;
        }

        defaultLayerStyle = new GUIStyle(EditorStyles.helpBox);
        defaultMapStyle = new GUIStyle(defaultLayerStyle);
        
        activeLayerStyle = new GUIStyle(defaultLayerStyle);
        activeLayerStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.4f, 0.6f, 0.3f));
        
        activeMapStyle = new GUIStyle(defaultMapStyle);
        activeMapStyle.normal.background = MakeTex(2, 2, new Color(0.3f, 0.6f, 0.9f, 0.4f));
    
        // 禁用状态样式（黑底白字，居中）
        toggleDisabledStyle = new GUIStyle(defaultLayerStyle);
        toggleDisabledStyle.normal.background = MakeTex(2, 2, Color.black); // 黑色背景
        toggleDisabledStyle.active.background = MakeTex(2, 2, Color.black);
        toggleDisabledStyle.focused.background = MakeTex(2, 2, Color.black);
        toggleDisabledStyle.normal.textColor = Color.white; // 白色文字
        toggleDisabledStyle.fontSize = 20;
        toggleDisabledStyle.alignment = TextAnchor.MiddleCenter; // 文字居中
        toggleDisabledStyle.padding = new RectOffset(8, 8, 2, 2); // 内边距调整

        // 激活状态样式（蓝底白字，居中）
        toggleActiveStyle = new GUIStyle(defaultLayerStyle);
        toggleActiveStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.4f, 0.8f)); // 蓝色背景
        toggleActiveStyle.active.background = MakeTex(2, 2, new Color(0.15f, 0.3f, 0.6f)); // 点击时稍暗
        toggleActiveStyle.focused.background = MakeTex(2, 2, new Color(0.25f, 0.5f, 0.9f)); // 聚焦时稍亮
        toggleActiveStyle.normal.textColor = Color.white; // 白色文字
        toggleDisabledStyle.fontSize = 20;
        toggleActiveStyle.alignment = TextAnchor.MiddleCenter; // 文字居中
        toggleActiveStyle.padding = new RectOffset(8, 8, 2, 2); // 与禁用样式保持一致内边距
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    // 禁用时
    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // UI内容创建
    private void OnGUI()
    {
        if (activeLayerStyle == null || activeMapStyle == null)
        {
            InitializeStyles();
        }

        try
        {
            DrawModeControls();
            EditorGUILayout.Space();
            DrawMaterialReferenceSection();
            EditorGUILayout.Space();
            DrawLayersSection();
            EditorGUILayout.Space();
            DrawProjectionOptions();
            EditorGUILayout.Space();
            DrawPaintingOptions();
            
            ValidateLayerData();
        }
        catch (System.Exception e)
        {
            EditorGUILayout.HelpBox($"UI错误: {e.Message}", MessageType.Error);
        }
    }

    // 拾取对象
    private void PickSelectedObject()
    {
        // 检查Hierarchy中是否有选中对象
        if (Selection.activeGameObject == null)
        {
            EditorUtility.DisplayDialog("提示", "请先在Hierarchy中选择一个对象", "确定");
            return;
        }
        
        GameObject selectedObj = Selection.activeGameObject;
        ValidateAndSettarget_Object(selectedObj);
    }

    // 预处理目标对象碰撞组件
    private void ValidateAndSettarget_Object(GameObject obj)
    {
        // 检查是否有MeshFilter组件
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            EditorUtility.DisplayDialog("错误", "所选对象不支持绘制", "确定");
            return;
        }

        // 禁用其他碰撞体，确保只有MeshCollider
        Collider[] colliders = obj.GetComponents<Collider>();
        foreach (var collider in colliders)
        {
            if (!(collider is MeshCollider))
            {
                collider.enabled = false;
            }
        }

        // 确保有MeshCollider
        MeshCollider meshCollider = obj.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = obj.AddComponent<MeshCollider>();
        }
        meshCollider.sharedMesh = meshFilter.sharedMesh;

        // 成功设置目标对象后初始化相关属性
        target_Object = obj;
        target_Mesh = meshFilter.sharedMesh;
        target_Renderer = obj.GetComponent<Renderer>();
        target_OriginalMaterial = target_Renderer.sharedMaterial;
        UnityEngine.Debug.Log("成功设置目标对象后初始化相关属性");

        // 如果基础参考材质没有上锁
        if(!lockbaseRefMaterial){
            baseRefMaterial = target_OriginalMaterial;
        }
        // 处理材质
        HandleTargetMaterial();
    }


    // 预处理目标对象渲染组件
    private void HandleTargetMaterial()
    {
        UnityEngine.Debug.Log("HandleTargetMaterial()");
        // 如果有渲染器且有材质
        if (target_Renderer != null && target_Renderer.sharedMaterial != null)
        {
            // 检查材质是否有Texture2D属性
            bool hasTextureProperties = false;
            Shader shader = target_Renderer.sharedMaterial.shader;
            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    if (!IsBuiltInTexture2D(propertyName))//IsBuiltInTexture2D(propertyName)
                    {
                        hasTextureProperties = true;
                        break;
                    }
                }
            }

            if (hasTextureProperties)
            {
                baseRefMaterial = target_Renderer.sharedMaterial;
            }
            else
            {
                // 使用URP默认Lit材质
                baseRefMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }
        }
        else
        {
            // 没有渲染器或材质，使用URP默认Lit材质
            baseRefMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (target_Renderer != null)
            {
                target_Renderer.sharedMaterial = baseRefMaterial;
            }
            else
            {
                // 添加渲染器组件
                MeshRenderer renderer = target_Object.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = baseRefMaterial;
                target_Renderer = renderer;
            }
        }

        targetShader = baseRefMaterial.shader;
    }

    // 绘制模式控制
    private void DrawModeControls()
    {
        //UnityEngine.Debug.Log("DrawModeControls()");
        EditorGUILayout.LabelField("模式控制", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        {
            // 进入绘制模式按钮
            string paintModeButtonText = isPaintingMode ? "退出绘制模式" : "进入绘制模式";
            if (GUILayout.Button(paintModeButtonText, isPaintingMode ? toggleActiveStyle : toggleDisabledStyle, GUILayout.Height(50)))
            {
                if (!isPaintingMode)
                {
                    // 如果有绘制材质，直接进入绘图模式
                    if (previewMaterial_Final && materialLayers.Count > 0)
                    {
                        isPaintingMode = true;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("错误", "请先创建材质图层再进入绘制模式", "确定");
                        GUILayout.EndHorizontal();
                        return;
                    }
                }
                else
                {
                    // 退出绘制模式
                    isPaintingMode = false;
                }
                SceneView.RepaintAll();
            }

            // 合并孤立模式按钮
            string isolateButtonText = isIsolatedMode ? "退出孤立" : "孤立显示";
            if (GUILayout.Button(isolateButtonText, isIsolatedMode ? toggleActiveStyle : toggleDisabledStyle, GUILayout.Height(50)))
            {
                if (target_Object != null)
                {
                    isIsolatedMode = !isIsolatedMode;
                    IsolateObject(isIsolatedMode);
                }
                else
                {
                    EditorUtility.DisplayDialog("错误", "请先选择目标对象", "确定");
                }
            }
        }
        GUILayout.EndHorizontal();
        
        // 修改目标对象选择行，添加拾取按钮
        GUILayout.BeginHorizontal();
        target_Object = EditorGUILayout.ObjectField("目标对象", target_Object, typeof(GameObject), true) as GameObject;

        EditorGUILayout.LabelField("关闭窗口自动销毁", GUILayout.Width(100));
        ExitAutoDestory = EditorGUILayout.Toggle(ExitAutoDestory, GUILayout.Width(20));

        if (GUILayout.Button("拾取当前选择对象", GUILayout.Width(150)))
        {
            PickSelectedObject();
        }
        GUILayout.EndHorizontal();
    
        if (target_Object != null)
        {
            target_Mesh = target_Object.GetComponent<MeshFilter>()?.sharedMesh;
            target_Renderer = target_Object.GetComponent<Renderer>();
            
            // 当目标对象变更且有渲染器时，更新基础参考材质（如果未锁定）
            if (target_Renderer != null && !lockbaseRefMaterial)
            {
                if (baseRefMaterial == null)
                {
                    baseRefMaterial = target_Renderer.sharedMaterial;
                }
                else if (target_OriginalMaterial == null || target_OriginalMaterial == target_Renderer.sharedMaterial)
                {
                    baseRefMaterial = target_Renderer.sharedMaterial;
                }
            }
        }
        else
        {
            target_Mesh = null;
            target_Renderer = null;
            if (target_OriginalMaterial == null)
            {
                baseRefMaterial = null;
            }
        }
    }

    private void DrawMaterialReferenceSection()
    {
        //UnityEngine.Debug.Log("DrawMaterialReferenceSection()");
        EditorGUILayout.LabelField("材质参考", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        
        // 修复基础参考材质锁定复选框
        EditorGUILayout.LabelField("基础参考材质", GUILayout.Width(100));
        
        EditorGUILayout.LabelField("锁定", GUILayout.Width(30));
        lockbaseRefMaterial = EditorGUILayout.Toggle(lockbaseRefMaterial, GUILayout.Width(20));
        
        // 只有未锁定时才允许修改材质
        if (!lockbaseRefMaterial)
        {
            Material newbaseRefMaterial = EditorGUILayout.ObjectField(baseRefMaterial, typeof(Material), false) as Material;
            if (newbaseRefMaterial != baseRefMaterial)
            {
                baseRefMaterial = newbaseRefMaterial;
                if (baseRefMaterial != null)
                {
                    targetShader = baseRefMaterial.shader;
                }
            }
        }
        else
        {
            // 锁定状态下显示材质但不可编辑
            GUI.enabled = false;
            EditorGUILayout.ObjectField(baseRefMaterial, typeof(Material), false);
            GUI.enabled = true;
        }
        // 新增：纹理大小设置
        EditorGUILayout.LabelField("纹理尺寸", GUILayout.Width(60));
        int newTextureSize = EditorGUILayout.IntField(textureSize, GUILayout.Width(80));
        // 确保纹理尺寸为2的幂且不小于32
        if (newTextureSize != textureSize)
        {
            newTextureSize = Mathf.Max(32, newTextureSize);
            newTextureSize = Mathf.NextPowerOfTwo(newTextureSize);
            textureSize = newTextureSize;
        }
        if (GUILayout.Button("合并所有材质层并导出"))
        {
            ExportMergedMaterial();
        }
        
        GUILayout.EndHorizontal();
        
        if (GUILayout.Button("添加新材质层"))
        {
            AddNewLayer();
        }
    }

    private bool IsShaderURPCompatible(Shader shader)
    {
        if (shader == null) return false;
        
        if (shader.name.Contains("Universal") || 
            shader.name.Contains("URP") ||
            shader.name.Contains("UniversalRenderPipeline"))
            return true;
            
        if (baseRefMaterial != null)
        {
            if (baseRefMaterial.HasProperty("_BaseMap") && 
                baseRefMaterial.HasProperty("_Metallic") && 
                baseRefMaterial.HasProperty("_Roughness"))
            {
                return true;
            }
        }
        
        return false;
    }

    // 新建材质
    private void AddNewLayer()
    {
        UnityEngine.Debug.Log("新建图层AddNewLayer()");

        //无法新建材质
        if (baseRefMaterial == null || baseRefMaterial.shader == null)
        {
            EditorUtility.DisplayDialog("错误", "请先设置基础参考材质", "确定");
            return;
        }

        //先新建参数图层列表
        List<TextureMap> textureMaps = new List<TextureMap>();
        
        //参数图层序号
        int mapIndex=-1;

        //检测基础参考材质有有无非内置的Texture2D参数，并加入参数图层列表
        for (int i = 0; i < ShaderUtil.GetPropertyCount(baseRefMaterial.shader); i++)
        {
            if (ShaderUtil.GetPropertyType(baseRefMaterial.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
            {
                string propertyName = ShaderUtil.GetPropertyName(baseRefMaterial.shader, i);

                if (!IsBuiltInTexture2D(propertyName) && !textureMaps.Any(e => e.mapName == propertyName))
                {
                    //参数图层序号++
                    mapIndex++;
                    TextureMap newmMap = new TextureMap();
                    newmMap.mapName = propertyName;
                    newmMap.index = mapIndex;
                    InitializePaintTexture(newmMap);
                    textureMaps.Add(newmMap);
                }
            }
        }
        // 判断shader有无非内置纹理输入参数
        if (textureMaps.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "基础材质没有可绘制的Texture2D属性（已过滤系统内置属性）", "确定");
            return;
        }
        
        //新建材质图层
        MaterialLayer NewLayer = new MaterialLayer();
        NewLayer.layerName = "MaterialLayer";
        NewLayer.visible = true;//是否可见
        NewLayer.expanded = false;//UI是否展开
        NewLayer.isActive = false;//激活该层
        NewLayer.LayerMaterial = new Material(baseRefMaterial);//拷贝基本参考材质新建材质
        NewLayer.textureMaps = textureMaps;

        // 添加进材质列表
        materialLayers.Add(NewLayer);

        //如果没有预览材质先创建创建预览材质
        if(previewMaterial_Final==null){
            previewMaterial_Final = new Material(baseRefMaterial);
        }
        // 获取当前材质数量
        int LayersCount = materialLayers.Count;
        // 如果材质列表只有一个材质
        if(LayersCount==1){
            // 直接设为预览图层
            previewMaterial_Final = materialLayers[0].LayerMaterial;
            previewMaterial_Final.name = materialLayers[0].layerName;
            materialLayers[0].visible = true;
            materialLayers[0].isActive = true;
            // 直接选中第一层
            selectedLayerIndex = 0;
        }
        // 如果有多个材质
        else if(LayersCount > 1){
            // 从后往前遍历所有材质层
            for(int i = LayersCount-1 ; i > -1 ; i--){
                //如果该材质为可见
                if(materialLayers[i].visible){
                    //设为预览材质
                    previewMaterial_Final = materialLayers[i].LayerMaterial;
                    // 选择该层
                    selectedLayerIndex = i;
                }
            }
        }

        // 创建单个图层预览材质（简单自发光材质）
        if (previewMaterial_Isolation == null)
        {
            previewMaterial_Isolation = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            previewMaterial_Isolation.name = "Single Layer Preview Material";
        }
    }


    // 场景内UI
    private void OnSceneGUI(SceneView sceneView)
    {
        if (!isPaintingMode || target_Object == null || target_Mesh == null || 
            selectedLayerIndex < 0 || selectedLayerIndex >= materialLayers.Count ||
            selectedMapIndex == -1)
        {
            return; // 非绘制模式或条件不满足时不处理
        }
        
        Event currentEvent = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        
        // 1. 计算笔刷世界大小
        float worldSize = brushSize * Mathf.Max(
            target_Mesh.bounds.extents.x, 
            target_Mesh.bounds.extents.y, 
            target_Mesh.bounds.extents.z);
        
        // 2. 实时获取鼠标射线
        Ray worldRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
        bool isHitTarget = Physics.Raycast(worldRay, out RaycastHit hit) && hit.collider.gameObject == target_Object;
        
        // 3. 始终绘制光标（改进：即使未命中目标也显示光标在射线方向上）
        Vector3 cursorPosition = isHitTarget ? hit.point : worldRay.origin + worldRay.direction * 10f;
        Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, isHitTarget ? 0.3f : 0.1f);
        Handles.SphereHandleCap(0, cursorPosition, Quaternion.identity, worldSize, EventType.Repaint);
        
        // 4. 处理绘制逻辑（改进：允许从任意位置开始拖动，命中模型时再绘制）
        if (!currentEvent.alt && !currentEvent.control && !currentEvent.shift)
        {
            // 鼠标按下时开始拖动（无论是否在模型上）
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                isMouseDragging = true;
                // 如果初始点击就在模型上，直接绘制
                if (isHitTarget)
                {
                    PaintOnTexture(hit);
                    lastPaintPosition = hit.point;
                }
                currentEvent.Use();
                sceneView.Repaint();
            }
            // 鼠标拖动时，只要命中模型就绘制
            else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && isMouseDragging)
            {
                if (isHitTarget)
                {
                    float distanceFromLast = Vector3.Distance(hit.point, lastPaintPosition);
                    if (distanceFromLast >= brushSpacing || brushSpacing <= 0 || lastPaintPosition == Vector3.one * float.MaxValue)
                    {
                        PaintOnTexture(hit);
                        lastPaintPosition = hit.point;
                        currentEvent.Use();
                        sceneView.Repaint();
                    }
                }
            }
        }
        else
        {
            isMouseDragging = false;
        }
        
        // 5. 鼠标抬起时重置状态
        if (currentEvent.type == EventType.MouseUp)
        {
            isMouseDragging = false;
            lastPaintPosition = Vector3.one * float.MaxValue;
        }
        
        // 6. 强制重绘确保光标实时更新
        if (isPaintingMode)
        {
            sceneView.Repaint();
        }
    }

    private void UpdateMaterialPreview()
    {
        //UnityEngine.Debug.Log("UpdateMaterialPreview()");
        if (target_Renderer == null || targetShader == null) return;

        // 处理孤立图层
        if (isIsolatedMode)
        {
            ApplyIsolatedPreview();
        }
        else
        {
            // 没有孤立图层，使用绘制材质
            if (previewMaterial_Final == null)
            {
                // 首次进入绘制模式，初始化绘制材质，拷贝原始材质参数
                previewMaterial_Final = new Material(targetShader);
                previewMaterial_Final.CopyPropertiesFromMaterial(baseRefMaterial);

                // 将原始参数逐个拷贝到参数图层
                // 待开发。。。

            }

            // 应用所有可见图层
            foreach (var layer in materialLayers.ToList()) // 使用ToList()创建副本避免迭代冲突
            {
                // if (!layer.visible) continue;

                foreach (var Map in layer.textureMaps.ToList()) // 使用ToList()创建副本
                {
                    if (Map.isPainting && Map.textureData.paintTexture != null)
                    {
                        previewMaterial_Final.SetTexture(Map.mapName, Map.textureData.paintTexture);
                    }
                }
            }
            
            target_Renderer.sharedMaterial = previewMaterial_Final;
        }

        // 强制更新渲染器
        target_Renderer.enabled = false;
        target_Renderer.enabled = true;
        SceneView.RepaintAll();
    }


    private void ShowOnlyActiveMap(Material material)
    {
        foreach (var layer in materialLayers)
        {
            foreach (var Map in layer.textureMaps)
            {
                if (Map.isPainting && Map.textureData.paintTexture != null)
                {
                    material.SetTexture(Map.mapName, Map.textureData.paintTexture);
                    
                    if (isUsingURP)
                    {
                        if (Map.mapName == "_BaseMap")
                            material.SetColor("_BaseColor", Color.white);
                        else if (Map.mapName == "_EmissionMap")
                            material.SetColor("_EmissionColor", Color.white);
                        else if (Map.mapName == "_MetallicMap")
                            material.SetFloat("_Metallic", 1);
                        else if (Map.mapName == "_RoughnessMap")
                            material.SetFloat("_Roughness", 1);
                    }
                }
            }
        }
    }

    private void MergeLayersToMaterial(Material material)
    {
        foreach (var layer in materialLayers)
        {
            if (!layer.visible) continue;
            
            foreach (var Map in layer.textureMaps)
            {
                if (IsBuiltInTexture2D(Map.mapName))
                    continue;
                    
                if (Map.textureData.paintTexture != null)
                {
                    material.SetTexture(Map.mapName, Map.textureData.paintTexture);
                    
                    if (isUsingURP)
                    {
                        if (Map.mapName == "_BaseMap")
                            material.SetColor("_BaseColor", Color.white);
                        else if (Map.mapName == "_EmissionMap")
                            material.SetColor("_EmissionColor", Color.white);
                    }
                    else
                    {
                        if (Map.mapName == "_BaseColorMap")
                            material.SetColor("_BaseColor", Color.white);
                        else if (Map.mapName == "_EmissionMap")
                            material.SetColor("_EmissionColor", Color.white);
                    }
                }
            }
        }
    }

    private void DrawLayersSection()
    {
        EditorGUILayout.LabelField("材质图层", EditorStyles.boldLabel);
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        int layerCount = materialLayers.Count;
        for (int i = layerCount - 1; i >= 0; i--)
        {
            if (i < materialLayers.Count)
            {
                DrawLayer(i);
            }
        }
        
        EditorGUILayout.EndScrollView();
    }
    /// <summary>
    /// 清除所有内存中的绘制纹理
    /// </summary>
    public void ClearAllPaintTextures()
    {
        if (materialLayers == null) return;
        
        int textureCount = 0;
        
        foreach (var layer in materialLayers)
        {
            if (layer?.textureMaps == null) continue;
            
            foreach (var Map in layer.textureMaps)
            {
                if (Map?.textureData != null)
                {
                    // 销毁绘制纹理
                    if (Map.textureData.paintTexture != null)
                    {
                        DestroyImmediate(Map.textureData.paintTexture);
                        Map.textureData.paintTexture = null;
                        textureCount++;
                    }
                    
                    // 重置修改状态
                    Map.textureData.isModified = false;
                }
            }
        }
        
        UnityEngine.Debug.Log($"ClearAllPaintTextures() - 已清理 {textureCount} 个绘制纹理");
        
        // 刷新材质预览
        UpdateMaterialPreview();
        
        // 强制垃圾回收
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
    }

    // 按材质列表序号删除单个材质层
    private void DelateLayer(int index){
        MaterialLayer layer = materialLayers[index];
        if (EditorUtility.DisplayDialog("确认", $"确定要删除材质 '{layer.layerName}' 吗?", "是", "否"))
        {
            // 记录该层的激活状态
            bool wasActive = layer.isActive;
            // 删除材质
            materialLayers.RemoveAt(index);
            
            if(materialLayers.Count>0){

                // 如果还有别的材质则以别的材质显示
                previewMaterial_Final = materialLayers[0].LayerMaterial;
                target_Renderer.sharedMaterial = previewMaterial_Final != null ? previewMaterial_Final : baseRefMaterial;
                
            }else{

                // 否则就恢复原始材质
                if (target_Renderer != null && target_OriginalMaterial != null)
                {
                    target_Renderer.sharedMaterial = target_OriginalMaterial;
                    UnityEngine.Debug.Log("已恢复目标对象原始材质");
                }
                // 销毁绘制材质实例
                if (previewMaterial_Final != null)
                {
                    DestroyImmediate(previewMaterial_Final);
                    previewMaterial_Final = null;
                    UnityEngine.Debug.Log("销毁绘制材质实例");
                }
                // 销毁单个参数图层预览材质
                if (previewMaterial_Isolation != null)
                {
                    DestroyImmediate(previewMaterial_Isolation);
                    previewMaterial_Isolation = null;
                    UnityEngine.Debug.Log("销毁单个参数图层预览材质");
                }

                // 销毁参数图层
                ClearAllPaintTextures();

            }
            
            if (wasActive)
            {
                UpdateMaterialPreview();
            }
            
            if (selectedLayerIndex == index)
            {
                selectedLayerIndex = -1;
                selectedMapIndex = -1;
            }
            else if (selectedLayerIndex > index)
            {
                selectedLayerIndex--;
            }
            
        }
    }

    // 绘制材质图层UI
    private void DrawLayer(int index)
    {
        MaterialLayer layer = materialLayers[index]; 
        if (layer == null) return;
        
        GUIStyle layerStyle = layer.isActive ? activeLayerStyle : defaultLayerStyle;
        
        GUILayout.BeginVertical(layerStyle);
        {
            GUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField($"[{index}]", GUILayout.Width(30));
                layer.layerName = EditorGUILayout.TextField(layer.layerName);
                layer.visible = GUILayout.Toggle(layer.visible, layer.visible ? "显示" : "隐藏", GUILayout.Width(50));

                layer.expanded = GUILayout.Toggle(layer.expanded, layer.expanded ? "折叠" : "展开", GUILayout.Width(50));
                
                if (GUILayout.Button("导出材质", GUILayout.Width(80)))
                {
                    //导出材质
                    ExportLayerAsMaterial(index);
                }
                
                // 删除材质图层
                if (GUILayout.Button("删除", GUILayout.Width(50)))
                {
                    // 删除材质
                    DelateLayer(index);
                }
            }
            GUILayout.EndHorizontal();
            
            if (layer.expanded)
            {
                // 绘制参数图层
                DrawLayerParameters(index, layer);
            }
        }
        GUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    // 孤立显示当前选中的参数图层
    private void ApplyIsolatedPreview()
    {
        if (previewMaterial_Isolation == null||selectedMapIndex==-1) return;

        TextureMap Map = materialLayers[selectedLayerIndex].textureMaps[selectedMapIndex];
        // 设置预览材质使用当前图层作为自发光
        if (Map.textureData.paintTexture != null)
        {
            previewMaterial_Isolation.SetTexture("_BaseMap", Map.textureData.paintTexture);
        }
        else if (Map.textureData.sourceTexture != null)
        {
            previewMaterial_Isolation.SetTexture("_BaseMap", Map.textureData.sourceTexture);//_EmissionMap
        }

        // 应用到目标对象
        if (target_Renderer != null)
        {
            target_Renderer.sharedMaterial = previewMaterial_Isolation;
        }
    }

    // 1. 修复DrawLayerParameters中的按钮逻辑，确保状态正确切换
    private void DrawLayerParameters(int layerIndex, MaterialLayer layer)
    {
        if (layer == null) return;
        
        GUILayout.BeginVertical(EditorStyles.helpBox);
        {
            //遍历当前图层的所有参数层
            foreach (var Map in layer.textureMaps)
            {
                // 保存旧的孤立状态用于判断变化
                bool oldIsolateState = Map.isolateThisMap;

                // 定义选中和未选中的样式（选中时用蓝色背景）
                GUIStyle entryStyle = Map.isPainting ? activeMapStyle : defaultMapStyle;

                // 开始横向布局，并获取其矩形范围
                GUILayout.BeginHorizontal(entryStyle);
                {
                    // 以下是原有UI元素（孤立复选框、名称、纹理选择等）
                    GUILayout.Label("孤立", GUILayout.Width(40),GUILayout.Height(50));
                    Map.isolateThisMap = EditorGUILayout.Toggle(Map.isolateThisMap, GUILayout.Width(40),GUILayout.Height(50));

                    // 处理孤立状态变化（保持原有逻辑）
                    if (oldIsolateState != Map.isolateThisMap)
                    {
                        if (Map.isolateThisMap)
                        {
                            // 取消其他所有孤立设置
                            foreach (var e in layer.textureMaps)
                            {
                                if (e != Map)
                                {
                                    e.isolateThisMap = false;
                                }
                            }
                            // 孤立显示该参数图层
                            ApplyIsolatedPreview();
                        }
                        else
                        {
                            // 取消孤立单个图层，显示材质图层完整材质
                            RestoreAllMapsVisibility();
                        }
                    }

                    // 显示参数层名称（去掉前缀下划线）
                    string displayName = Map.mapName.StartsWith("_") ? Map.mapName.Substring(1) : Map.mapName;
                    //EditorGUILayout.LabelField(displayName, GUILayout.Width(150), GUILayout.Height(50));
                    
                    // string buttonText = Map.isPainting ? "绘制结束" : "绘制此图";
                    if (GUILayout.Button(displayName,GUILayout.Width(200), GUILayout.Height(50)))
                    {
                        // 关键修复：切换状态时强制更新选中的参数层
                        //bool true = !Map.isPainting;

                        //先重置所有参数层的绘制状态
                        foreach (var l in materialLayers)
                        {
                            foreach (var e in l.textureMaps)
                            {
                                e.isPainting = false;
                            }
                        }
                        // 激活当前材质层
                        foreach (var l in materialLayers)
                        {
                            l.isActive = false;
                        }
                        layer.isActive = true;


                        // 设置当前参数层状态
                        Map.isPainting = true;

                        // 更新选中的图层和参数层
                        selectedLayerIndex = layerIndex;
                        // 马上回来
                        selectedMapIndex = 0;
                        //currentEvent.Use(); // 防止事件穿透到其他UI
                        
                        
                    }
                    
                    GUILayout.FlexibleSpace();

                    // 纹理选择框
                    Texture2D newSourceTexture = EditorGUILayout.ObjectField(
                        Map.textureData.sourceTexture,
                        typeof(Texture2D),
                        false,
                        GUILayout.Width(50),  // 固定宽度保持小预览图
                        GUILayout.Height(50)  // 匹配编辑器字段高度
                    ) as Texture2D;

                    if (newSourceTexture != Map.textureData.sourceTexture)
                    {
                        Map.textureData.sourceTexture = newSourceTexture;
                        InitializePaintTextureFromSource(layerIndex, Map.mapName);
                        UpdateMaterialPreview();
                    }

                    // 导出按钮
                    if (GUILayout.Button("导出", GUILayout.Width(50), GUILayout.Height(50)))
                    {
                        ExportTexture(layerIndex, Map.mapName);
                    }


                } 
                GUILayout.EndHorizontal(); // 结束横向布局
            }
        }
        GUILayout.EndVertical();
    }

    // 新增：孤立显示指定纹理图层
    private void IsolateMapLayer(int layerIndex, string mapName)
    {
        if (target_Object == null || previewMaterial_Isolation == null) return;
        
        foreach (var renderer in target_Object.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == previewMaterial_Isolation)
                {
                    // 隐藏其他所有纹理
                    foreach (var layer in materialLayers)
                    {
                        foreach (var Map in layer.textureMaps)
                        {
                            if (Map.mapName != mapName)
                            {
                                previewMaterial_Isolation.SetTexture(Map.mapName, null);
                            }
                        }
                    }
                }
            }
        }
    }

    // 新增：恢复所有纹理显示
    private void RestoreAllMapsVisibility()
    {
        target_Renderer.sharedMaterial = previewMaterial_Final;
        UpdateMaterialPreview();
        
    }

    private void DrawProjectionOptions()
    {
        EditorGUILayout.LabelField("投影模式", EditorStyles.boldLabel);
        currentProjectionMode = (ProjectionMode)EditorGUILayout.EnumPopup(currentProjectionMode);
    }

    private void DrawPaintingOptions()
    {
        EditorGUILayout.LabelField("绘制模式", EditorStyles.boldLabel);
        currentPaintMode = (PaintMode)EditorGUILayout.EnumPopup(currentPaintMode);
        
        switch (currentPaintMode)
        {
            case PaintMode.Brush:
                DrawBrushSettings();
                break;
            case PaintMode.Fill:
                DrawFillSettings();
                break;
        }
    }

    private void DrawBrushSettings()
    {
        GUILayout.BeginVertical("box");
        brushColor = EditorGUILayout.ColorField("画笔颜色", brushColor);
        brushSize = EditorGUILayout.Slider("画笔尺寸", brushSize, 0.01f, 1.0f);
        brushHardness = EditorGUILayout.Slider("画笔硬度", brushHardness, 0.0f, 1.0f);
        brushColor.a = EditorGUILayout.Slider("不透明度", brushColor.a, 0.01f, 1.0f);
        brushSpacing = EditorGUILayout.Slider("笔刷间距", brushSpacing, 0.0f, 1.0f);

        // 新增灰度复选框（调整布局顺序）
        GUILayout.BeginHorizontal();
        // 1. "笔刷遮罩"文字（靠左）
        EditorGUILayout.LabelField("笔刷遮罩", GUILayout.Width(60));
        // 弹性空间分隔左右元素
        GUILayout.FlexibleSpace();
        // 2. "灰度"文字（靠左）
        EditorGUILayout.LabelField("灰度", GUILayout.Width(30));
        // 3. 灰度复选框（靠右）
        useMaskGrayscale = EditorGUILayout.Toggle(useMaskGrayscale, GUILayout.Width(15));
        // 4. Texture2D引用（靠右，保持小预览图样式）
        brushMask = EditorGUILayout.ObjectField(
            brushMask, 
            typeof(Texture2D), 
            false, 
            GUILayout.Width(60),  // 固定宽度保持小预览图
            GUILayout.Height(60)  // 匹配编辑器字段高度
        ) as Texture2D;
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    private void DrawFillSettings()
    {
        GUILayout.BeginVertical("box");
        brushColor = EditorGUILayout.ColorField("填充颜色", brushColor);
        currentFillTarget = (FillTarget)EditorGUILayout.EnumPopup("填充目标", currentFillTarget);
        GUILayout.EndVertical();
    }

    // ======================== 核心修改2：替换为旧版本可正常绘制的PaintOnTexture ========================
    private void PaintOnTexture(RaycastHit hit)
    {
        if (selectedLayerIndex < 0 || selectedLayerIndex >= materialLayers.Count)
            return;
            
        MaterialLayer layer = materialLayers[selectedLayerIndex];
        // 关键修复：优先使用isPainting为true的参数层作为绘制目标
        var mapEntry = layer.textureMaps.FirstOrDefault(e => e.isPainting);
        // 备选方案：如果没有激活的，使用选中的名称
        if (mapEntry == null)
        {
            mapEntry = layer.textureMaps.FirstOrDefault(e => e.index == selectedMapIndex);
        }
        
        if (mapEntry == null)
        {
            UnityEngine.Debug.LogWarning("未找到有效的绘制目标参数层");
            return;
        }
        
        // 更新选中的参数层名称（同步状态）
        selectedMapIndex = mapEntry.index;
        
        TextureData textureData = mapEntry.textureData;
        if (textureData.paintTexture == null)
        {
            UnityEngine.Debug.LogWarning("绘制目标纹理为空，已自动初始化");
            InitializePaintTexture(layer.textureMaps[selectedMapIndex]);
            return;
        }

        Vector2 uv = hit.textureCoord;
        int pixelX = Mathf.Clamp((int)(uv.x * textureData.paintTexture.width), 0, textureData.paintTexture.width - 1);
        int pixelY = Mathf.Clamp((int)(uv.y * textureData.paintTexture.height), 0, textureData.paintTexture.height - 1);
        
        if (currentPaintMode == PaintMode.Brush)
        {
            PaintWithBrush(textureData.paintTexture, pixelX, pixelY);
        }
        else if (currentPaintMode == PaintMode.Fill)
        {
            PaintWithFill(textureData.paintTexture, hit.triangleIndex, uv);
        }
        
        textureData.isModified = true;
        UpdateMaterialPreview();
    }

    // 添加碰撞体检查和自动修复（在进入绘制模式时调用）
    private void EnsureValidCollider()
    {
        if (target_Object == null) return;
        // 检查是否有MeshCollider
        MeshCollider meshCollider = target_Object.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            // 没有则添加
            meshCollider = target_Object.AddComponent<MeshCollider>();
            UnityEngine.Debug.Log("已自动添加MeshCollider以支持UV检测");
        }
        // 确保碰撞体使用正确的网格
        if (meshCollider.sharedMesh != target_Mesh)
        {
            meshCollider.sharedMesh = target_Mesh;
        }
        // 确保碰撞体是凸面体（如果需要）
        if (!meshCollider.convex && target_Object.GetComponent<Rigidbody>() != null)
        {
            meshCollider.convex = true;
        }
    }

    private Texture2D ScaleTexture(Texture2D source, int width, int height)
    {
        // 确保源纹理可读
        Texture2D readableSource = GetReadableTexture(source);
        if (readableSource == null)
        {
            return CreateFallbackMask();
        }

        // 1. 尺寸相同：直接用Graphics.CopyTexture（高效）
        if (readableSource.width == width && readableSource.height == height)
        {
            Texture2D scaled = new Texture2D(width, height, readableSource.format, false);
            scaled.filterMode = readableSource.filterMode;
            scaled.wrapMode = readableSource.wrapMode;
            
            // 同尺寸可直接复制（无内存尺寸问题）
            Graphics.CopyTexture(
                source,          // 源纹理
                0,               // 源纹理的mipmap级别
                0,               // 源纹理的数组索引（单张纹理为0）
                scaled,          // 目标纹理
                0,               // 目标纹理的mipmap级别
                0                // 目标纹理的数组索引
            );
            
            scaled.Apply();
            
            // 清理临时纹理
            if (readableSource != source)
            {
                DestroyImmediate(readableSource);
            }
            return scaled;
        }
        // 2. 尺寸不同：用双线性插值手动缩放
        else
        {
            Texture2D scaled = new Texture2D(width, height, readableSource.format, false);
            scaled.filterMode = FilterMode.Bilinear;
            scaled.wrapMode = TextureWrapMode.Clamp;

            Color[] sourcePixels = readableSource.GetPixels();
            Color[] scaledPixels = new Color[width * height];
            
            float xRatio = (float)readableSource.width / width;
            float yRatio = (float)readableSource.height / height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // 双线性插值计算（保留平滑过渡）
                    float sourceX = x * xRatio;
                    float sourceY = y * yRatio;

                    int x1 = Mathf.Clamp((int)sourceX, 0, readableSource.width - 2);
                    int y1 = Mathf.Clamp((int)sourceY, 0, readableSource.height - 2);
                    int x2 = x1 + 1;
                    int y2 = y1 + 1;

                    float u = sourceX - x1;
                    float v = sourceY - y1;

                    Color pixel11 = sourcePixels[y1 * readableSource.width + x1];
                    Color pixel12 = sourcePixels[y2 * readableSource.width + x1];
                    Color pixel21 = sourcePixels[y1 * readableSource.width + x2];
                    Color pixel22 = sourcePixels[y2 * readableSource.width + x2];

                    Color row1 = Color.Lerp(pixel11, pixel21, u);
                    Color row2 = Color.Lerp(pixel12, pixel22, u);
                    scaledPixels[y * width + x] = Color.Lerp(row1, row2, v);
                }
            }

            scaled.SetPixels(scaledPixels);
            scaled.Apply();

            // 清理临时纹理
            if (readableSource != source)
            {
                DestroyImmediate(readableSource);
            }
            return scaled;
        }
    }
    // 添加在类中，用于读取纹理原始数据
    private Texture2D GetReadableTexture(Texture2D sourceTexture)
    {
        if (sourceTexture == null) return null;

        // 如果纹理已可读，直接返回
        if (sourceTexture.isReadable)
        {
            return sourceTexture;
        }

        // 对于不可读纹理，通过AssetDatabase读取原始文件
        string assetPath = AssetDatabase.GetAssetPath(sourceTexture);
        if (string.IsNullOrEmpty(assetPath))
        {
            UnityEngine.Debug.LogWarning("无法获取纹理资源路径，可能是内置资源");
            return CreateFallbackMask(); // 返回默认圆形遮罩
        }

        // 读取纹理文件原始数据
        byte[] fileData = File.ReadAllBytes(assetPath);
        Texture2D tempTexture = new Texture2D(2, 2);
        
        // 加载原始数据到临时纹理（无需设置可读）
        if (ImageConversion.LoadImage(tempTexture, fileData))
        {
            tempTexture.filterMode = sourceTexture.filterMode;
            tempTexture.wrapMode = TextureWrapMode.Clamp;
            return tempTexture;
        }
        else
        {
            UnityEngine.Debug.LogWarning("无法加载纹理数据：" + assetPath);
            return CreateFallbackMask(); // 返回默认圆形遮罩
        }
    }

    // 当纹理无法读取时的 fallback
    private Texture2D CreateFallbackMask()
    {
        Texture2D mask = new Texture2D(64, 64, TextureFormat.Alpha8, false);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float dx = x / 63f - 0.5f;
                float dy = y / 63f - 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float alpha = Mathf.Clamp01(1 - dist);
                mask.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        mask.Apply();
        return mask;
    }
    // 3. 完善PaintWithBrush方法（确保完整实现）
    private void PaintWithBrush(Texture2D texture, int centerX, int centerY)
    {
        if (texture == null) return;
        
        int brushPixelSize = Mathf.Max(1, (int)(brushSize * texture.width * 0.16f));
        //Texture2D mask = brushMask;
        Texture2D mask = null;
        if (brushMask != null)
        {
            // 使用新方法获取可读纹理（无需手动设置）
            mask = GetReadableTexture(brushMask);
        }
        else
        {
            // 无遮罩时使用默认圆形遮罩
            mask = CreateDefaultBrushMask(brushPixelSize, brushHardness);
        }

        // 缩放遮罩到当前画笔尺寸（原有逻辑保留）
        if (mask.width != brushPixelSize || mask.height != brushPixelSize)
        {
            mask = ScaleTexture(mask, brushPixelSize, brushPixelSize);
        }
        
        // 创建或缩放遮罩
        if (mask == null)
        {
            mask = CreateDefaultBrushMask(brushPixelSize, brushHardness);
        }
        else if (mask.width != brushPixelSize || mask.height != brushPixelSize)
        {
            mask = ScaleTexture(mask, brushPixelSize, brushPixelSize);
        }
        
        // 锁定纹理以提高性能
        texture.SetPixels32(texture.GetPixels32());
        
        // 绘制笔刷
        for (int y = 0; y < mask.height; y++)
        {
            for (int x = 0; x < mask.width; x++)
            {
                int targetX = centerX - mask.width / 2 + x;
                int targetY = centerY - mask.height / 2 + y;
                
                if (targetX >= 0 && targetX < texture.width && targetY >= 0 && targetY < texture.height)
                {
                    float alpha = mask.GetPixel(x, y).a * brushColor.a;
                    if (alpha > 0)
                    {
                        Color originalColor = texture.GetPixel(targetX, targetY);
                        Color newColor;

                        if (brushMask != null)
                        {
                            // 获取遮罩像素
                            Color maskPixel = mask.GetPixel(x, y);
                            if (useMaskGrayscale)
                            {
                                // 只使用灰度信息（亮度），结合画笔颜色
                                float grayscale = maskPixel.grayscale; // 计算灰度值
                                //  新透明度 = 遮罩灰度 * 遮罩Alpha * 笔刷透明度
                                float NewAlpha = grayscale * maskPixel.a * brushColor.a;
                                newColor = Color.Lerp(originalColor, brushColor, NewAlpha);
                            }
                            else
                            {
                                // 新透明度 = 遮罩透明度 * 笔刷透明度
                                float NewAlpha = maskPixel.a * brushColor.a;
                                // 使用遮罩本身颜色，忽略画笔颜色
                                newColor = Color.Lerp(originalColor, maskPixel, NewAlpha);
                            }
                        }
                        else
                        {
                            // 无遮罩时使用默认逻辑
                            newColor = Color.Lerp(originalColor, brushColor, alpha);
                        }
                        texture.SetPixel(targetX, targetY, newColor);
                    }
                    
                }
            }
        }
        
        texture.Apply();
    }


    // 2. 添加遮罩缩放辅助方法（保留，避免自定义遮罩尺寸异常）
    private Texture2D ResizeMask(Texture2D source, int targetSize)
    {
        Texture2D resized = new Texture2D(targetSize, targetSize, TextureFormat.Alpha8, false);
        resized.filterMode = FilterMode.Bilinear; // 双线性过滤，缩放后边缘更平滑
        
        RenderTexture rt = RenderTexture.GetTemporary(targetSize, targetSize);
        rt.filterMode = FilterMode.Bilinear;
        Graphics.Blit(source, rt);
        
        RenderTexture.active = rt;
        resized.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
        resized.Apply();
        
        RenderTexture.ReleaseTemporary(rt);
        return resized;
    }

    private void PaintWithFill(Texture2D texture, int triangleIndex, Vector2 uv)
    {
        switch (currentFillTarget)
        {
            case FillTarget.EntireObject:
                Color[] pixels = new Color[texture.width * texture.height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = brushColor;
                }
                texture.SetPixels(pixels);
                break;
                
            case FillTarget.Element:
                PaintTriangle(texture, triangleIndex, brushColor);
                break;
                
            case FillTarget.UVIsland:
                PaintUVIsland(texture, uv, brushColor);
                break;
        }
        
        texture.Apply();
    }

    private void PaintTriangle(Texture2D texture, int triangleIndex, Color color)
    {
        UnityEngine.Debug.Log($"填充三角形 {triangleIndex}");
    }

    private void PaintUVIsland(Texture2D texture, Vector2 uv, Color color)
    {
        UnityEngine.Debug.Log($"填充UV岛 at {uv}");
    }

    // 旧版本默认笔刷遮罩创建逻辑（保留，确保圆形遮罩正常生成）
    private Texture2D CreateDefaultBrushMask(int size, float hardness)
    {
        Texture2D mask = new Texture2D(size, size, TextureFormat.Alpha8, false);
        mask.filterMode = FilterMode.Bilinear;

        float Radius = size * 0.5f;
        float Radius_Iner = Radius * hardness;
        float Padding_Width = Radius - Radius_Iner;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - size * 0.5f;
                float dy = y - size * 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float Delta = distance - Radius_Iner;
                float alpha = Delta <= 0 ? 1 : distance >= Radius ? 0 : (Padding_Width - Delta) / Padding_Width;
                mask.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        
        mask.Apply();
        return mask;
    }

    private void EnsureLayersHaveTextures()
    {
        foreach (MaterialLayer layer in materialLayers)
        {
            foreach (TextureMap Map in layer.textureMaps)
            {
                if (Map.textureData.paintTexture == null)
                {
                    InitializePaintTexture(Map);
                }
            }
        }
    }

    // 初始化纹理
    private void InitializePaintTexture(TextureMap Map)
    {
        if (Map == null) return;
        
        Texture2D newTexture = new Texture2D(textureSize, textureSize, TextureFormat.ARGB32, false);
        newTexture.filterMode = FilterMode.Bilinear;
        newTexture.wrapMode = TextureWrapMode.Repeat;
        newTexture.hideFlags = HideFlags.HideAndDontSave; // 确保临时纹理不会被保存
        
        if (Map.textureData.sourceTexture != null)
        {
            Graphics.CopyTexture(Map.textureData.sourceTexture, newTexture);
        }
        else
        {
            Color defaultColor = GetDefaultMapColor(Map.mapName);
            Color[] pixels = new Color[textureSize * textureSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = defaultColor;
            }
            newTexture.SetPixels(pixels);
        }
        
        newTexture.Apply();
        Map.textureData.paintTexture = newTexture;
    }

    private void InitializePaintTextureFromSource(int layerIndex, string mapName)
    {
        if (layerIndex < 0 || layerIndex >= materialLayers.Count) return;
        
        MaterialLayer layer = materialLayers[layerIndex];
        var Map = layer.textureMaps.FirstOrDefault(e => e.mapName == mapName);
        if (Map == null) return;
        
        if (Map.textureData.sourceTexture != null)
        {
            Texture2D source = Map.textureData.sourceTexture;
            Texture2D newTexture = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            newTexture.filterMode = source.filterMode;
            newTexture.wrapMode = source.wrapMode;
            newTexture.hideFlags = HideFlags.HideAndDontSave;
            
            Graphics.CopyTexture(source, newTexture);
            newTexture.Apply();
            
            Map.textureData.paintTexture = newTexture;
            Map.textureData.isModified = true;
        }
        else if (Map.textureData.paintTexture != null)
        {
            Color defaultColor = GetDefaultMapColor(mapName);
            Color[] pixels = new Color[Map.textureData.paintTexture.width * Map.textureData.paintTexture.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = defaultColor;
            }
            Map.textureData.paintTexture.SetPixels(pixels);
            Map.textureData.paintTexture.Apply();
        }
    }

    private Color GetDefaultMapColor(string mapName)
    {
        if (mapName.Contains("BaseColor") || mapName.Contains("BaseMap") || mapName.Contains("Albedo"))
            return Color.white;
        else if (mapName.Contains("Metallic"))
            return Color.black;
        else if (mapName.Contains("Roughness"))
            return Color.black;
        else if (mapName.Contains("Occlusion"))
            return Color.white;
        else if (mapName.Contains("Emission"))
            return Color.black;
        else if (mapName.Contains("Opacity") || mapName.Contains("Alpha"))
            return Color.white;
        return Color.white;
    }

    private void ValidateLayerData()
    {
        if (materialLayers == null)
            materialLayers = new List<MaterialLayer>();
            
        if (selectedLayerIndex >= materialLayers.Count)
            selectedLayerIndex = -1;
            
        if (selectedLayerIndex != -1)
        {
            var layer = materialLayers[selectedLayerIndex];
            if (layer == null)
            {
                selectedLayerIndex = -1;
                selectedMapIndex = -1;
                return;
            }
            
            if (!layer.textureMaps.Any(e => e.index == selectedMapIndex))
            {
                if (layer.textureMaps.Count > 0)
                    selectedMapIndex = layer.textureMaps[0].index;
                else
                    selectedMapIndex = -1;
            }
        }
    }

    private void IsolateObject(bool isolate)
    {
        if (target_Object == null) return;
    
        // 处理所有渲染器组件
        foreach (var renderer in FindObjectsOfType<Renderer>())
        {
            if(renderer.gameObject != target_Object ){
                renderer.enabled = !isolate;
            }
        }
        // 处理所有碰撞组件
        foreach (var collider in FindObjectsOfType<Collider>())
        {
            if(collider.gameObject != target_Object ){
                collider.enabled = !isolate;
            }
        }
    }

    private void ExportTexture(int layerIndex, string mapName)
    {
        if (layerIndex < 0 || layerIndex >= materialLayers.Count) return;
        
        MaterialLayer layer = materialLayers[layerIndex];
        var Map = layer.textureMaps.FirstOrDefault(e => e.mapName == mapName);
        if (Map == null || Map.textureData.paintTexture == null) return;
        
        string defaultName = $"{layer.layerName}_{mapName}.png";
        string path = EditorUtility.SaveFilePanelInProject("导出纹理", defaultName, "png", "请选择保存纹理的路径");
        
        if (!string.IsNullOrEmpty(path))
        {
            byte[] bytes = Map.textureData.paintTexture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"纹理已导出到:\n{path}", "确定");
        }
    }

    private void ExportLayerAsMaterial(int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= materialLayers.Count || targetShader == null) return;
        
        MaterialLayer layer = materialLayers[layerIndex];
        string defaultName = $"{layer.layerName}.mat";
        string path = EditorUtility.SaveFilePanelInProject("导出材质", defaultName, "mat", "请选择保存材质的路径");
        
        if (!string.IsNullOrEmpty(path))
        {
            Material newMaterial = new Material(targetShader);
            newMaterial.CopyPropertiesFromMaterial(baseRefMaterial);
            MergeLayersToMaterial(newMaterial);
            AssetDatabase.CreateAsset(newMaterial, path);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"材质已导出到:\n{path}", "确定");
        }
    }

    private void ExportMergedMaterial()
    {
        if (baseRefMaterial == null || targetShader == null) return;
        
        string defaultName = "MergedMaterial.mat";
        string path = EditorUtility.SaveFilePanelInProject("导出合并材质", defaultName, "mat", "请选择保存材质的路径");
        
        if (!string.IsNullOrEmpty(path))
        {
            Material mergedMaterial = new Material(targetShader);
            mergedMaterial.CopyPropertiesFromMaterial(baseRefMaterial);
            MergeLayersToMaterial(mergedMaterial);
            AssetDatabase.CreateAsset(mergedMaterial, path);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"合并材质已导出到:\n{path}", "确定");
        }
    }
}