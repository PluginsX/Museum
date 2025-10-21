using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[System.Serializable]
public class MaterialLayer
{
    public string layerName = "New Layer";
    public bool visible = true;
    public bool expanded = false;
    public bool isActive = false;
    [SerializeField] public List<TextureMapEntry> textureMaps = new List<TextureMapEntry>();
}

[System.Serializable]
public class TextureMapEntry
{
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

public class PBRPainterWindow : EditorWindow
{
    private static readonly List<string> UnityBuiltinTextures = new List<string>
    {
        "unity_Lightmaps", "unity_LightmapsInd", "unity_ShadowMasks",
        "unity_ReflectionProbes", "unity_SpecCube0", "unity_SpecCube1",
        "unity_SpecCube0_HDR", "unity_SpecCube1_HDR"
    };

    private GUIStyle activeLayerStyle = new GUIStyle();
    private GUIStyle activeMapStyle = new GUIStyle();
    private GUIStyle defaultLayerStyle = new GUIStyle();
    private GUIStyle defaultMapStyle = new GUIStyle();

    private GameObject targetObject;
    private Mesh targetMesh;
    private Renderer targetRenderer;
    private Material originalMaterial;
    private Material previewMaterialInstance; // 用于保存当前预览材质实例
    
    private Material baseMaterial;
    [SerializeField] private bool lockBaseMaterial = false; // 新增：锁定基础材质标志
    [SerializeField] private List<MaterialLayer> materialLayers = new List<MaterialLayer>();
    private Shader targetShader;
    private bool isUsingURP;
    
    private bool isPaintingMode = false;
    private bool isIsolatedMode = false;
    private PaintMode currentPaintMode = PaintMode.Brush;
    private ProjectionMode currentProjectionMode = ProjectionMode.Screen;
    private FillTarget currentFillTarget = FillTarget.EntireObject;
    
    private Color brushColor = Color.white;
    private float brushSize = 0.1f;
    private float brushHardness = 1.0f;
    private Texture2D brushMask;
    private float brushSpacing = 0.1f; // 笔刷间距（单位：米）
    private Vector3 lastPaintPosition = Vector3.one * float.MaxValue; // 上次绘制位置
    
    private int selectedLayerIndex = -1;
    private string selectedMapName = "";
    
    private Vector2 scrollPosition;
    private int textureSize = 1024;
    private bool isMouseDragging = false;

    [MenuItem("Window/LNU数字艺术实验室/PBR Painter")]
    public static void ShowWindow()
    {
        GetWindow<PBRPainterWindow>("PBR Painter");
    }

    private void OnEnable()
    {
        isUsingURP = GraphicsSettings.currentRenderPipeline != null && 
                    GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal");
        SceneView.duringSceneGui += OnSceneGUI;
        
        // 延迟一帧初始化样式，确保EditorStyles可用
        EditorApplication.delayCall += InitializeStyles;
        lastPaintPosition = Vector3.one * float.MaxValue; // 新增初始化
    }

    // 窗口关闭时自动清理
    private void OnDestroy()
    {
        // 恢复原始材质
        if (targetRenderer != null && originalMaterial != null)
        {
            targetRenderer.sharedMaterial = originalMaterial;
        }
        
        // 销毁预览材质实例
        if (previewMaterialInstance != null)
        {
            DestroyImmediate(previewMaterialInstance);
            previewMaterialInstance = null;
        }
        
        // 退出孤立模式
        if (isIsolatedMode)
        {
            IsolateObject(false);
            isIsolatedMode = false;
        }
        
        // 重置绘制模式
        isPaintingMode = false;
    }

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
        defaultMapStyle = new GUIStyle(EditorStyles.label);
        
        activeLayerStyle = new GUIStyle(defaultLayerStyle);
        activeLayerStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.4f, 0.6f, 0.3f));
        
        activeMapStyle = new GUIStyle(defaultMapStyle);
        activeMapStyle.normal.background = MakeTex(2, 2, new Color(0.3f, 0.6f, 0.9f, 0.4f));
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

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

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

    private void DrawModeControls()
    {
        EditorGUILayout.LabelField("模式控制", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        
        // 合并绘制模式按钮
        string paintModeButtonText = isPaintingMode ? "退出绘制模式" : "进入绘制模式";
        if (GUILayout.Button(paintModeButtonText))
        {
            if (!isPaintingMode)
            {
                // 进入绘制模式
                if (targetObject != null && targetMesh != null && baseMaterial != null)
                {
                    originalMaterial = targetRenderer.sharedMaterial;
                    targetShader = baseMaterial.shader;
                    isPaintingMode = true;
                    EnsureValidCollider(); 
                    EnsureLayersHaveTextures();
                    UpdateMaterialPreview();
                    SceneView.RepaintAll();
                    lastPaintPosition = Vector3.one * float.MaxValue; // 新增重置
                }
                else
                {
                    string message = targetObject == null ? "请选择目标对象" : 
                                     baseMaterial == null ? "请设置基础参考材质" : "目标对象没有Mesh";
                    EditorUtility.DisplayDialog("错误", message, "确定");
                }
            }
            else
            {
                // 退出绘制模式
                isPaintingMode = false;
                foreach (var layer in materialLayers)
                {
                    layer.isActive = false;
                    foreach (var entry in layer.textureMaps)
                    {
                        entry.isPainting = false;
                    }
                }
                
                // 恢复原始材质
                if (targetRenderer != null && originalMaterial != null)
                {
                    targetRenderer.sharedMaterial = originalMaterial;
                }
                
                // 销毁预览材质实例
                if (previewMaterialInstance != null)
                {
                    DestroyImmediate(previewMaterialInstance);
                    previewMaterialInstance = null;
                }
                
                SceneView.RepaintAll();
            }
        }
        
        // 合并孤立模式按钮
        string isolateButtonText = isIsolatedMode ? "退出孤立" : "孤立显示";
        if (GUILayout.Button(isolateButtonText))
        {
            if (targetObject != null)
            {
                isIsolatedMode = !isIsolatedMode;
                IsolateObject(isIsolatedMode);
            }
            else
            {
                EditorUtility.DisplayDialog("错误", "请先选择目标对象", "确定");
            }
        }
        
        GUILayout.EndHorizontal();
        
        targetObject = EditorGUILayout.ObjectField("目标对象", targetObject, typeof(GameObject), true) as GameObject;
        
        if (targetObject != null)
        {
            targetMesh = targetObject.GetComponent<MeshFilter>()?.sharedMesh;
            targetRenderer = targetObject.GetComponent<Renderer>();
            
            // 当目标对象变更且有渲染器时，更新基础参考材质（如果未锁定）
            if (targetRenderer != null && !lockBaseMaterial)
            {
                if (baseMaterial == null)
                {
                    baseMaterial = targetRenderer.sharedMaterial;
                }
                else if (originalMaterial == null || originalMaterial == targetRenderer.sharedMaterial)
                {
                    baseMaterial = targetRenderer.sharedMaterial;
                }
            }
        }
        else
        {
            targetMesh = null;
            targetRenderer = null;
            if (originalMaterial == null)
            {
                baseMaterial = null;
            }
        }
    }

    private void DrawMaterialReferenceSection()
    {
        EditorGUILayout.LabelField("材质参考", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        
        // 修复基础参考材质锁定复选框
        EditorGUILayout.LabelField("基础参考材质", GUILayout.Width(100));
        lockBaseMaterial = EditorGUILayout.Toggle(lockBaseMaterial, GUILayout.Width(20));
        EditorGUILayout.LabelField("锁定", GUILayout.Width(30));
        
        // 只有未锁定时才允许修改材质
        if (!lockBaseMaterial)
        {
            Material newBaseMaterial = EditorGUILayout.ObjectField(baseMaterial, typeof(Material), false) as Material;
            if (newBaseMaterial != baseMaterial)
            {
                baseMaterial = newBaseMaterial;
                if (baseMaterial != null)
                {
                    targetShader = baseMaterial.shader;
                }
            }
        }
        else
        {
            // 锁定状态下显示材质但不可编辑
            GUI.enabled = false;
            EditorGUILayout.ObjectField(baseMaterial, typeof(Material), false);
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
            
        if (baseMaterial != null)
        {
            if (baseMaterial.HasProperty("_BaseMap") && 
                baseMaterial.HasProperty("_Metallic") && 
                baseMaterial.HasProperty("_Roughness"))
            {
                return true;
            }
        }
        
        return false;
    }

    private void AddNewLayer()
    {
        if (baseMaterial == null || baseMaterial.shader == null)
        {
            EditorUtility.DisplayDialog("错误", "请先设置基础参考材质", "确定");
            return;
        }
        
        MaterialLayer newLayer = new MaterialLayer();
        newLayer.layerName = $"Layer {materialLayers.Count}";
        
        for (int i = 0; i < ShaderUtil.GetPropertyCount(baseMaterial.shader); i++)
        {
            if (ShaderUtil.GetPropertyType(baseMaterial.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
            {
                string propertyName = ShaderUtil.GetPropertyName(baseMaterial.shader, i);
                
                if (!UnityBuiltinTextures.Contains(propertyName) && 
                    !newLayer.textureMaps.Any(e => e.mapName == propertyName))
                {
                    newLayer.textureMaps.Add(new TextureMapEntry { mapName = propertyName });
                }
            }
        }
        
        if (newLayer.textureMaps.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "基础材质没有可绘制的Texture2D属性（已过滤系统内置属性）", "确定");
            return;
        }
        
        materialLayers.Add(newLayer);
        selectedLayerIndex = materialLayers.Count - 1;
        selectedMapName = newLayer.textureMaps[0].mapName;
    }

    // ======================== 核心修改1：替换为旧版本可正常绘制的OnSceneGUI ========================
    private void OnSceneGUI(SceneView sceneView)
    {
        if (!isPaintingMode || targetObject == null || targetMesh == null || 
            selectedLayerIndex < 0 || selectedLayerIndex >= materialLayers.Count ||
            string.IsNullOrEmpty(selectedMapName))
        {
            return;
        }
        
        Event currentEvent = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        
        Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.5f);
        Ray worldRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
        
        // 检测射线是否击中目标物体
        if (Physics.Raycast(worldRay, out RaycastHit hit) && hit.collider.gameObject == targetObject)
        {
            // 绘制笔刷预览球体
            float worldSize = brushSize * Mathf.Max(
                targetMesh.bounds.extents.x, 
                targetMesh.bounds.extents.y, 
                targetMesh.bounds.extents.z);
            Handles.SphereHandleCap(0, hit.point, Quaternion.identity, worldSize, EventType.Repaint);
            
            // 处理鼠标事件进行绘制，添加间距判断
            if (!currentEvent.alt && !currentEvent.control && !currentEvent.shift)
            {
                // 计算当前位置与上次绘制位置的距离
                float distanceFromLast = Vector3.Distance(hit.point, lastPaintPosition);
                
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
                {
                    isMouseDragging = true;
                    PaintOnTexture(hit);
                    lastPaintPosition = hit.point; // 更新上次绘制位置
                    currentEvent.Use();
                    sceneView.Repaint();
                }
                else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && isMouseDragging)
                {
                    // 只有当距离超过设定的间距或间距为0时才绘制
                    if (distanceFromLast >= brushSpacing || brushSpacing <= 0)
                    {
                        PaintOnTexture(hit);
                        lastPaintPosition = hit.point; // 更新上次绘制位置
                        currentEvent.Use();
                        sceneView.Repaint();
                    }
                }
            }
            else
            {
                isMouseDragging = false;
            }
        }
        
        // 鼠标抬起时重置拖拽状态和上次位置
        if (currentEvent.type == EventType.MouseUp)
        {
            isMouseDragging = false;
            lastPaintPosition = Vector3.one * float.MaxValue; // 重置为初始值
        }
    }

    // 增强 UpdateMaterialPreview 方法的可靠性
    private void UpdateMaterialPreview()
    {
        if (targetRenderer == null || targetShader == null) return;
        
        // 强制刷新预览材质
        if (previewMaterialInstance == null)
        {
            previewMaterialInstance = new Material(targetShader);
        }
        previewMaterialInstance.CopyPropertiesFromMaterial(baseMaterial);
        
        // 确保绘制的纹理被正确赋值
        //bool hasActivePaintingMap = false;
        foreach (var layer in materialLayers)
        {
            foreach (var entry in layer.textureMaps)
            {
                if (entry.isPainting && entry.textureData.paintTexture != null)
                {
                    previewMaterialInstance.SetTexture(entry.mapName, entry.textureData.paintTexture);
                    //hasActivePaintingMap = true;
                }
            }
        }
        
        // 强制更新渲染器
        targetRenderer.sharedMaterial = previewMaterialInstance;
        targetRenderer.enabled = false;
        targetRenderer.enabled = true; // 触发重新渲染
        
        // 刷新场景视图
        SceneView.RepaintAll();
    }

    private void ShowOnlyActiveMap(Material material)
    {
        foreach (var layer in materialLayers)
        {
            foreach (var entry in layer.textureMaps)
            {
                if (entry.isPainting && entry.textureData.paintTexture != null)
                {
                    material.SetTexture(entry.mapName, entry.textureData.paintTexture);
                    
                    if (isUsingURP)
                    {
                        if (entry.mapName == "_BaseMap")
                            material.SetColor("_BaseColor", Color.white);
                        else if (entry.mapName == "_EmissionMap")
                            material.SetColor("_EmissionColor", Color.white);
                        else if (entry.mapName == "_MetallicMap")
                            material.SetFloat("_Metallic", 1);
                        else if (entry.mapName == "_RoughnessMap")
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
            
            foreach (var entry in layer.textureMaps)
            {
                if (UnityBuiltinTextures.Contains(entry.mapName))
                    continue;
                    
                if (entry.textureData.paintTexture != null)
                {
                    material.SetTexture(entry.mapName, entry.textureData.paintTexture);
                    
                    if (isUsingURP)
                    {
                        if (entry.mapName == "_BaseMap")
                            material.SetColor("_BaseColor", Color.white);
                        else if (entry.mapName == "_EmissionMap")
                            material.SetColor("_EmissionColor", Color.white);
                    }
                    else
                    {
                        if (entry.mapName == "_BaseColorMap")
                            material.SetColor("_BaseColor", Color.white);
                        else if (entry.mapName == "_EmissionMap")
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
                DrawLayer(i, materialLayers[i]);
            }
        }
        
        EditorGUILayout.EndScrollView();
    }

    private void DrawLayer(int index, MaterialLayer layer)
    {
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
                    ExportLayerAsMaterial(index);
                }
                
                if (GUILayout.Button("删除", GUILayout.Width(50)))
                {
                    if (EditorUtility.DisplayDialog("确认", $"确定要删除图层 '{layer.layerName}' 吗?", "是", "否"))
                    {
                        bool wasActive = layer.isActive;
                        materialLayers.RemoveAt(index);
                        
                        if (wasActive)
                        {
                            UpdateMaterialPreview();
                        }
                        
                        if (selectedLayerIndex == index)
                        {
                            selectedLayerIndex = -1;
                            selectedMapName = "";
                        }
                        else if (selectedLayerIndex > index)
                        {
                            selectedLayerIndex--;
                        }
                        
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        return;
                    }
                }
                
                if (GUILayout.Button("选择绘制", GUILayout.Width(80)))
                {
                    foreach (var l in materialLayers)
                    {
                        l.isActive = false;
                    }
                    
                    layer.isActive = true;
                    selectedLayerIndex = index;
                    if (string.IsNullOrEmpty(selectedMapName) && layer.textureMaps.Count > 0)
                    {
                        selectedMapName = layer.textureMaps[0].mapName;
                    }
                }
            }
            GUILayout.EndHorizontal();
            
            if (layer.expanded)
            {
                DrawLayerParameters(index, layer);
            }
        }
        GUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    // 1. 修复DrawLayerParameters中的按钮逻辑，确保状态正确切换
    private void DrawLayerParameters(int layerIndex, MaterialLayer layer)
    {
        if (layer == null) return;
        
        GUILayout.BeginVertical(EditorStyles.helpBox);
        {
            foreach (var entry in layer.textureMaps)
            {
                GUIStyle mapStyle = entry.isPainting ? activeMapStyle : defaultMapStyle;
                
                GUILayout.BeginHorizontal();
                {
                    // 左侧元素
                    GUILayout.Label("孤立", GUILayout.Width(40));
                    entry.isolateThisMap = EditorGUILayout.Toggle(entry.isolateThisMap, GUILayout.Width(20));
                    
                    string displayName = entry.mapName.StartsWith("_") ? entry.mapName.Substring(1) : entry.mapName;
                    EditorGUILayout.LabelField($"{displayName}", mapStyle, GUILayout.Width(150));
                    
                    GUILayout.FlexibleSpace();
                    
                    // 右侧元素
                    Texture2D newSourceTexture = EditorGUILayout.ObjectField(
                        entry.textureData.sourceTexture, 
                        typeof(Texture2D), 
                        false,
                        GUILayout.MinWidth(100), 
                        GUILayout.MaxWidth(300)) as Texture2D;
                    
                    if (newSourceTexture != entry.textureData.sourceTexture)
                    {
                        entry.textureData.sourceTexture = newSourceTexture;
                        InitializePaintTextureFromSource(layerIndex, entry.mapName);
                        UpdateMaterialPreview();
                    }
                    
                    if (GUILayout.Button("导出", GUILayout.Width(60)))
                    {
                        ExportTexture(layerIndex, entry.mapName);
                    }
                    
                    string buttonText = entry.isPainting ? "绘制结束" : "绘制此图";
                    if (GUILayout.Button(buttonText, GUILayout.Width(80)))
                    {
                        // 关键修复：切换状态时强制更新选中的参数层
                        bool newState = !entry.isPainting;
                        
                        // 先重置所有参数层的绘制状态
                        foreach (var l in materialLayers)
                        {
                            foreach (var e in l.textureMaps)
                            {
                                e.isPainting = false;
                            }
                        }
                        
                        // 设置当前参数层状态
                        entry.isPainting = newState;
                        
                        // 更新选中的图层和参数层
                        selectedLayerIndex = layerIndex;
                        selectedMapName = entry.mapName;
                        
                        // 激活当前材质层
                        foreach (var l in materialLayers)
                        {
                            l.isActive = false;
                        }
                        layer.isActive = newState;
                        
                        // 处理孤立显示
                        if (newState && entry.isolateThisMap)
                        {
                            IsolateMapLayer(layerIndex, entry.mapName);
                        }
                        else
                        {
                            RestoreAllMapsVisibility();
                        }
                        
                        UpdateMaterialPreview();
                        // 强制刷新场景视图
                        SceneView.RepaintAll();
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndVertical();
    }

    // 新增：孤立显示指定纹理图层
    private void IsolateMapLayer(int layerIndex, string mapName)
    {
        if (targetObject == null || previewMaterialInstance == null) return;
        
        foreach (var renderer in targetObject.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == previewMaterialInstance)
                {
                    // 隐藏其他所有纹理
                    foreach (var layer in materialLayers)
                    {
                        foreach (var entry in layer.textureMaps)
                        {
                            if (entry.mapName != mapName)
                            {
                                previewMaterialInstance.SetTexture(entry.mapName, null);
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
        brushHardness = EditorGUILayout.Slider("画笔硬度", brushHardness, 0.01f, 1.0f);
        brushColor.a = EditorGUILayout.Slider("不透明度", brushColor.a, 0.01f, 1.0f);
        brushSpacing = EditorGUILayout.Slider("笔刷间距", brushSpacing, 0.0f, 1.0f); 
        brushMask = EditorGUILayout.ObjectField("笔刷遮罩", brushMask, typeof(Texture2D), false) as Texture2D;
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
            mapEntry = layer.textureMaps.FirstOrDefault(e => e.mapName == selectedMapName);
        }
        
        if (mapEntry == null)
        {
            Debug.LogWarning("未找到有效的绘制目标参数层");
            return;
        }
        
        // 更新选中的参数层名称（同步状态）
        selectedMapName = mapEntry.mapName;
        
        TextureData textureData = mapEntry.textureData;
        if (textureData.paintTexture == null)
        {
            Debug.LogWarning("绘制目标纹理为空，已自动初始化");
            InitializePaintTexture(layer, selectedMapName);
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
        if (targetObject == null) return;
        // 检查是否有MeshCollider
        MeshCollider meshCollider = targetObject.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            // 没有则添加
            meshCollider = targetObject.AddComponent<MeshCollider>();
            Debug.Log("已自动添加MeshCollider以支持UV检测");
        }
        // 确保碰撞体使用正确的网格
        if (meshCollider.sharedMesh != targetMesh)
        {
            meshCollider.sharedMesh = targetMesh;
        }
        // 确保碰撞体是凸面体（如果需要）
        if (!meshCollider.convex && targetObject.GetComponent<Rigidbody>() != null)
        {
            meshCollider.convex = true;
        }
    }

    // 4. 添加缺失的ScaleTexture方法
    private Texture2D ScaleTexture(Texture2D source, int width, int height)
    {
        Texture2D scaled = new Texture2D(width, height, source.format, false);
        Color[] sourcePixels = source.GetPixels();
        Color[] scaledPixels = new Color[width * height];
        
        float xRatio = (float)source.width / width;
        float yRatio = (float)source.height / height;
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceX = Mathf.Min((int)(x * xRatio), source.width - 1);
                int sourceY = Mathf.Min((int)(y * yRatio), source.height - 1);
                scaledPixels[y * width + x] = sourcePixels[sourceY * source.width + sourceX];
            }
        }
        
        scaled.SetPixels(scaledPixels);
        scaled.Apply();
        return scaled;
    }
    
    // 3. 完善PaintWithBrush方法（确保完整实现）
    private void PaintWithBrush(Texture2D texture, int centerX, int centerY)
    {
        if (texture == null) return;
        
        int brushPixelSize = Mathf.Max(1, (int)(brushSize * texture.width * 0.5f));
        Texture2D mask = brushMask;
        
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
                        Color newColor = Color.Lerp(originalColor, brushColor, alpha);
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
        Debug.Log($"填充三角形 {triangleIndex}");
    }

    private void PaintUVIsland(Texture2D texture, Vector2 uv, Color color)
    {
        Debug.Log($"填充UV岛 at {uv}");
    }

    // 旧版本默认笔刷遮罩创建逻辑（保留，确保圆形遮罩正常生成）
    private Texture2D CreateDefaultBrushMask(int size, float hardness)
    {
        Texture2D mask = new Texture2D(size, size, TextureFormat.Alpha8, false);
        mask.filterMode = FilterMode.Bilinear;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x / (float)size - 0.5f;
                float dy = y / (float)size - 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy) * 2.0f;
                float falloff = Mathf.Lerp(0.1f, 1.0f, hardness);
                float alpha = Mathf.Clamp01(1.0f - Mathf.Pow(distance, falloff));
                mask.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        
        mask.Apply();
        return mask;
    }

    private void EnsureLayersHaveTextures()
    {
        foreach (var layer in materialLayers)
        {
            foreach (var entry in layer.textureMaps)
            {
                if (entry.textureData.paintTexture == null)
                {
                    InitializePaintTexture(layer, entry.mapName);
                }
            }
        }
    }

    private void InitializePaintTexture(MaterialLayer layer, string mapName)
    {
        var entry = layer.textureMaps.FirstOrDefault(e => e.mapName == mapName);
        if (entry == null) return;
        
        Texture2D newTexture = new Texture2D(textureSize, textureSize, TextureFormat.ARGB32, false);
        newTexture.filterMode = FilterMode.Bilinear;
        newTexture.wrapMode = TextureWrapMode.Repeat;
        newTexture.hideFlags = HideFlags.HideAndDontSave; // 确保临时纹理不会被保存
        
        if (entry.textureData.sourceTexture != null)
        {
            Graphics.CopyTexture(entry.textureData.sourceTexture, newTexture);
        }
        else
        {
            Color defaultColor = GetDefaultMapColor(mapName);
            Color[] pixels = new Color[textureSize * textureSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = defaultColor;
            }
            newTexture.SetPixels(pixels);
        }
        
        newTexture.Apply();
        entry.textureData.paintTexture = newTexture;
    }

    private void InitializePaintTextureFromSource(int layerIndex, string mapName)
    {
        if (layerIndex < 0 || layerIndex >= materialLayers.Count) return;
        
        MaterialLayer layer = materialLayers[layerIndex];
        var entry = layer.textureMaps.FirstOrDefault(e => e.mapName == mapName);
        if (entry == null) return;
        
        if (entry.textureData.sourceTexture != null)
        {
            Texture2D source = entry.textureData.sourceTexture;
            Texture2D newTexture = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            newTexture.filterMode = source.filterMode;
            newTexture.wrapMode = source.wrapMode;
            newTexture.hideFlags = HideFlags.HideAndDontSave;
            
            Graphics.CopyTexture(source, newTexture);
            newTexture.Apply();
            
            entry.textureData.paintTexture = newTexture;
            entry.textureData.isModified = true;
        }
        else if (entry.textureData.paintTexture != null)
        {
            Color defaultColor = GetDefaultMapColor(mapName);
            Color[] pixels = new Color[entry.textureData.paintTexture.width * entry.textureData.paintTexture.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = defaultColor;
            }
            entry.textureData.paintTexture.SetPixels(pixels);
            entry.textureData.paintTexture.Apply();
        }
    }

    private Color GetDefaultMapColor(string mapName)
    {
        if (mapName.Contains("BaseColor") || mapName.Contains("BaseMap") || mapName.Contains("Albedo"))
            return Color.white;
        else if (mapName.Contains("Metallic"))
            return Color.black;
        else if (mapName.Contains("Roughness"))
            return new Color(0.5f, 0.5f, 0.5f);
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
                selectedMapName = "";
                return;
            }
            
            if (!layer.textureMaps.Any(e => e.mapName == selectedMapName))
            {
                if (layer.textureMaps.Count > 0)
                    selectedMapName = layer.textureMaps[0].mapName;
                else
                    selectedMapName = "";
            }
        }
    }

    private void IsolateObject(bool isolate)
    {
        if (targetObject == null) return;
        
        foreach (var renderer in FindObjectsOfType<Renderer>())
        {
            if (renderer.gameObject != targetObject && !renderer.gameObject.transform.IsChildOf(targetObject.transform))
            {
                renderer.enabled = !isolate;
            }
        }
    }

    private void ExportTexture(int layerIndex, string mapName)
    {
        if (layerIndex < 0 || layerIndex >= materialLayers.Count) return;
        
        MaterialLayer layer = materialLayers[layerIndex];
        var entry = layer.textureMaps.FirstOrDefault(e => e.mapName == mapName);
        if (entry == null || entry.textureData.paintTexture == null) return;
        
        string defaultName = $"{layer.layerName}_{mapName}.png";
        string path = EditorUtility.SaveFilePanelInProject("导出纹理", defaultName, "png", "请选择保存纹理的路径");
        
        if (!string.IsNullOrEmpty(path))
        {
            byte[] bytes = entry.textureData.paintTexture.EncodeToPNG();
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
            newMaterial.CopyPropertiesFromMaterial(baseMaterial);
            MergeLayersToMaterial(newMaterial);
            AssetDatabase.CreateAsset(newMaterial, path);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"材质已导出到:\n{path}", "确定");
        }
    }

    private void ExportMergedMaterial()
    {
        if (baseMaterial == null || targetShader == null) return;
        
        string defaultName = "MergedMaterial.mat";
        string path = EditorUtility.SaveFilePanelInProject("导出合并材质", defaultName, "mat", "请选择保存材质的路径");
        
        if (!string.IsNullOrEmpty(path))
        {
            Material mergedMaterial = new Material(targetShader);
            mergedMaterial.CopyPropertiesFromMaterial(baseMaterial);
            MergeLayersToMaterial(mergedMaterial);
            AssetDatabase.CreateAsset(mergedMaterial, path);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"合并材质已导出到:\n{path}", "确定");
        }
    }
}