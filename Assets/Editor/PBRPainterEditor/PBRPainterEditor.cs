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
        InitializeStyles();
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
            // 绘制笔刷预览球体（旧版本稳定逻辑）
            float worldSize = brushSize * Mathf.Max(
                targetMesh.bounds.extents.x, 
                targetMesh.bounds.extents.y, 
                targetMesh.bounds.extents.z);
            Handles.SphereHandleCap(0, hit.point, Quaternion.identity, worldSize, EventType.Repaint);
            
            // 处理鼠标事件进行绘制（旧版本稳定逻辑）
            if (!currentEvent.alt && !currentEvent.control && !currentEvent.shift)
            {
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
                {
                    isMouseDragging = true;
                    PaintOnTexture(hit);
                    currentEvent.Use();
                    sceneView.Repaint();
                }
                else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && isMouseDragging)
                {
                    PaintOnTexture(hit);
                    currentEvent.Use();
                    sceneView.Repaint();
                }
            }
            else
            {
                isMouseDragging = false;
            }
        }
        
        // 鼠标抬起时重置拖拽状态
        if (currentEvent.type == EventType.MouseUp)
        {
            isMouseDragging = false;
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
                    // 左侧元素 - 靠左排列
                    GUILayout.Label("孤立", GUILayout.Width(40)); // 固定文字宽度
                    entry.isolateThisMap = EditorGUILayout.Toggle(entry.isolateThisMap, GUILayout.Width(20)); // 仅复选框宽度
                    
                    string displayName = entry.mapName.StartsWith("_") ? entry.mapName.Substring(1) : entry.mapName;
                    EditorGUILayout.LabelField($"{displayName}", mapStyle, GUILayout.Width(150));
                    
                    // 弹性空间分隔左右
                    GUILayout.FlexibleSpace();
                    
                    // 右侧元素 - 靠右排列
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
                        entry.isPainting = !entry.isPainting;
                        
                        if (entry.isPainting)
                        {
                            foreach (var l in materialLayers)
                            {
                                foreach (var e in l.textureMaps)
                                {
                                    if (l != layer || e != entry)
                                    {
                                        e.isPainting = false;
                                    }
                                }
                            }
                            layer.isActive = true;
                            selectedLayerIndex = layerIndex;
                            selectedMapName = entry.mapName;
                            
                            // 处理孤立显示
                            if (entry.isolateThisMap)
                            {
                                IsolateMapLayer(layerIndex, entry.mapName);
                            }
                            else
                            {
                                RestoreAllMapsVisibility();
                            }
                        }
                        
                        UpdateMaterialPreview();
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
        brushMask = EditorGUILayout.ObjectField("画笔遮罩", brushMask, typeof(Texture2D), false) as Texture2D;
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
        var mapEntry = layer.textureMaps.FirstOrDefault(e => e.mapName == selectedMapName);
        if (mapEntry == null)
            return;
            
        TextureData textureData = mapEntry.textureData;
        if (textureData.paintTexture == null)
        {
            Debug.LogWarning("绘制目标纹理为空，已自动初始化");
            InitializePaintTexture(layer, selectedMapName);
            return;
        }
        
        // 旧版本稳定UV获取逻辑：直接使用hit.textureCoord（已验证可正常映射）
        Vector2 uv = hit.textureCoord;
        // 计算像素坐标（旧版本边界处理逻辑）
        int pixelX = Mathf.Clamp((int)(uv.x * textureData.paintTexture.width), 0, textureData.paintTexture.width - 1);
        int pixelY = Mathf.Clamp((int)(uv.y * textureData.paintTexture.height), 0, textureData.paintTexture.height - 1);
        
        // 调试输出（可选保留）
        Debug.Log($"绘制位置: UV({uv.x:F2},{uv.y:F2}) 像素({pixelX},{pixelY})");
        
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

    // ======================== 核心修改3：替换为旧版本可正常绘制的PaintWithBrush ========================
    private void PaintWithBrush(Texture2D texture, int centerX, int centerY)
    {
        // 旧版本稳定笔刷计算逻辑：直接基于纹理宽度计算笔刷像素大小
        int brushPixelSize = Mathf.Max(1, (int)(brushSize * texture.width));
        // 获取笔刷遮罩（旧版本逻辑：优先使用自定义遮罩，否则创建默认圆形遮罩）
        Texture2D mask = brushMask ?? CreateDefaultBrushMask(brushPixelSize, brushHardness);
        
        // 遍历遮罩像素并绘制到纹理（旧版本逐像素绘制逻辑）
        for (int y = 0; y < mask.height; y++)
        {
            for (int x = 0; x < mask.width; x++)
            {
                // 计算目标纹理上的像素位置
                int targetX = centerX - mask.width / 2 + x;
                int targetY = centerY - mask.height / 2 + y;
                
                // 确保像素位置在纹理范围内
                if (targetX >= 0 && targetX < texture.width && targetY >= 0 && targetY < texture.height)
                {
                    // 计算混合因子（结合遮罩alpha和画笔颜色alpha）
                    float alpha = mask.GetPixel(x, y).a * brushColor.a;
                    
                    if (alpha > 0)
                    {
                        // 混合原始颜色和画笔颜色（旧版本稳定插值逻辑）
                        Color originalColor = texture.GetPixel(targetX, targetY);
                        Color newColor = Color.Lerp(originalColor, brushColor, alpha);
                        texture.SetPixel(targetX, targetY, newColor);
                    }
                }
            }
        }
        
        // 应用纹理修改（关键步骤，确保绘制生效）
        texture.Apply();
    }

    // 2. 添加遮罩缩放辅助方法（保留，避免自定义遮罩尺寸异常）
    private Texture2D ResizeMask(Texture2D source, int targetSize)
    {
        Texture2D resized = new Texture2D(targetSize, targetSize, TextureFormat.Alpha8, false);
        RenderTexture rt = RenderTexture.GetTemporary(targetSize, targetSize);
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