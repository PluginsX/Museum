using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
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
        "unity_SpecCube0_HDR", "unity_SpecCube1_HDR","MainTex"
    };

    private GUIStyle activeLayerStyle = new GUIStyle();
    private GUIStyle activeMapStyle = new GUIStyle();
    private GUIStyle defaultLayerStyle = new GUIStyle();
    private GUIStyle defaultMapStyle = new GUIStyle();
    private GUIStyle toggleActiveStyle = new GUIStyle();
    private GUIStyle toggleDisabledStyle = new GUIStyle();

    private GameObject targetObject;
    private Mesh targetMesh;
    private Renderer targetRenderer;// 目标对象渲染器
    private Material originalMaterial;// 目标对象原始材质
    private Material baseMaterial;// 基础参考材质
    private Material previewSingleLayerMaterial; // 用于预览单个图层的简单材质
    private Material paintingMaterial; // 复制基础参考材质，用于绘制
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
    private bool useMaskGrayscale = true; // 灰度遮罩开关
    private float brushSpacing = 0.001f; // 笔刷间距（单位：米）
    private Vector3 lastPaintPosition = Vector3.one * float.MaxValue; // 上次绘制位置
    
    private int selectedLayerIndex = -1;
    private string selectedMapName = "";
    
    private Vector2 scrollPosition;
    private int textureSize = 1024;
    private bool isMouseDragging = false;
    private Vector3 currentCursorPosition; // 当前光标位置
    private bool isCursorVisible = false;  // 光标是否可见

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
            UnityEngine.Debug.Log("已恢复目标对象原始材质");
        }
        // 销毁绘制材质实例
        if (paintingMaterial != null)
        {
            DestroyImmediate(paintingMaterial);
            paintingMaterial = null;
            UnityEngine.Debug.Log("销毁绘制材质实例");
        }
        // 销毁单个参数图层预览材质
        if (previewSingleLayerMaterial != null)
        {
            DestroyImmediate(previewSingleLayerMaterial);
            previewSingleLayerMaterial = null;
            UnityEngine.Debug.Log("销毁单个参数图层预览材质");
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

    // 初始化样式
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
private void PickSelectedObject()
{
    // 检查Hierarchy中是否有选中对象
    if (Selection.activeGameObject == null)
    {
        EditorUtility.DisplayDialog("提示", "请先在Hierarchy中选择一个对象", "确定");
        return;
    }
    
    GameObject selectedObj = Selection.activeGameObject;
    ValidateAndSetTargetObject(selectedObj);
}
    // 设置目标对象
    private void ValidateAndSetTargetObject(GameObject obj)
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
        targetObject = obj;
        targetMesh = meshFilter.sharedMesh;
        targetRenderer = obj.GetComponent<Renderer>();
        originalMaterial = targetRenderer.sharedMaterial;
        UnityEngine.Debug.Log("成功设置目标对象后初始化相关属性");

        // 如果基础参考材质没有上锁
        if(!lockBaseMaterial){
            baseMaterial = originalMaterial;
        }
        // 处理材质
        HandleTargetMaterial();
    }


    private void HandleTargetMaterial()
    {
        UnityEngine.Debug.Log("HandleTargetMaterial()");
        // 如果有渲染器且有材质
        if (targetRenderer != null && targetRenderer.sharedMaterial != null)
        {
            // 检查材质是否有Texture2D属性
            bool hasTextureProperties = false;
            Shader shader = targetRenderer.sharedMaterial.shader;
            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    if (!UnityBuiltinTextures.Contains(propertyName))
                    {
                        hasTextureProperties = true;
                        break;
                    }
                }
            }

            if (hasTextureProperties)
            {
                baseMaterial = targetRenderer.sharedMaterial;
            }
            else
            {
                // 使用URP默认Lit材质
                baseMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }
        }
        else
        {
            // 没有渲染器或材质，使用URP默认Lit材质
            baseMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (targetRenderer != null)
            {
                targetRenderer.sharedMaterial = baseMaterial;
            }
            else
            {
                // 添加渲染器组件
                MeshRenderer renderer = targetObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = baseMaterial;
                targetRenderer = renderer;
            }
        }

        targetShader = baseMaterial.shader;
    }

    private void DrawModeControls()
    {
        //UnityEngine.Debug.Log("DrawModeControls()");
        EditorGUILayout.LabelField("模式控制", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        
        // 进入绘制模式按钮
        string paintModeButtonText = isPaintingMode ? "退出绘制模式" : "进入绘制模式";
        if (GUILayout.Button(paintModeButtonText,isPaintingMode ? toggleActiveStyle : toggleDisabledStyle,GUILayout.Height(50)))
        {
            if (!isPaintingMode)
            {
                // 如果有绘制材质，直接进入绘图模式
                if(paintingMaterial){
                    isPaintingMode = true;

                    // 刷新视图
                    SceneView.RepaintAll();
                }else{
                    EditorUtility.DisplayDialog("错误", "请先创建材质图层再进入绘制模式", "确定");

                    // //首次进入绘图模式
                    // if (targetObject != null && targetMesh != null && baseMaterial != null)
                    // {
                    //     originalMaterial = targetRenderer.sharedMaterial;
                    //     targetShader = baseMaterial.shader;

                    //     // 确保绘制材质存在
                    //     if (paintingMaterial == null)
                    //     {
                    //         // 首次进入绘制模式，初始化绘制材质，
                    //         paintingMaterial = new Material(baseMaterial);
                    //         paintingMaterial.name = "Painting Material";
                    //         // 拷贝原始材质参数
                    //         paintingMaterial.CopyPropertiesFromMaterial(baseMaterial);
                    //         // 将原始参数逐个拷贝到参数图层
                    //         // 待开发。。。
                    //     }

                    //     isPaintingMode = true;
                    //     EnsureValidCollider();
                    //     EnsureLayersHaveTextures();
                    //     //UpdateMaterialPreview();

                    //     // 刷新视图
                    //     SceneView.RepaintAll();
                    //     lastPaintPosition = Vector3.one * float.MaxValue;
                    // }
                    // else
                    // {
                    //     string message = targetObject == null ? "请选择目标对象" :
                    //                     baseMaterial == null ? "请设置基础参考材质" : "目标对象没有Mesh";
                    //     EditorUtility.DisplayDialog("错误", message, "确定");
                    // }
                }
                
            }
            else
            {
                // 退出绘制模式
                isPaintingMode = false;

                SceneView.RepaintAll();
            }
        }
        
        // 合并孤立模式按钮
        string isolateButtonText = isIsolatedMode ? "退出孤立" : "孤立显示";
        if (GUILayout.Button(isolateButtonText,isIsolatedMode ? toggleActiveStyle : toggleDisabledStyle,GUILayout.Height(50)))
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
        
        // 修改目标对象选择行，添加拾取按钮
        GUILayout.BeginHorizontal();
        targetObject = EditorGUILayout.ObjectField("目标对象", targetObject, typeof(GameObject), true) as GameObject;
        
        if (GUILayout.Button("拾取当前选择对象", GUILayout.Width(150)))
        {
            PickSelectedObject();
        }
        GUILayout.EndHorizontal();
    
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
        //UnityEngine.Debug.Log("DrawMaterialReferenceSection()");
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

    // 新建材质
    private void AddNewLayer()
    {
        UnityEngine.Debug.Log("新建图层AddNewLayer()");

        if (baseMaterial == null || baseMaterial.shader == null)
        {
            EditorUtility.DisplayDialog("错误", "请先设置基础参考材质", "确定");
            return;
        }

        

        // 创建绘制材质（复制基础参考材质）
        if (paintingMaterial == null)
        {
            paintingMaterial = new Material(baseMaterial);
            paintingMaterial.name = "Painting Material";
        }

        // 创建单个图层预览材质（简单自发光材质）
        if (previewSingleLayerMaterial == null)
        {
            previewSingleLayerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            previewSingleLayerMaterial.name = "Single Layer Preview Material";
            //配置为自发光显示
            // previewSingleLayerMaterial.SetFloat("_Metallic", 0);
            // previewSingleLayerMaterial.SetFloat("_Roughness", 1);
            // previewSingleLayerMaterial.EnableKeyword("_EMISSION");
            // previewSingleLayerMaterial.SetColor("_EmissionColor", Color.white);
        }

        // 原有图层创建逻辑...
        MaterialLayer newLayer = new MaterialLayer();
        newLayer.layerName = $"M_{materialLayers.Count}";
        newLayer.LayerMaterial = new Material(baseMaterial);

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


    
    private void OnSceneGUI(SceneView sceneView)
    {
        if (!isPaintingMode || targetObject == null || targetMesh == null || 
            selectedLayerIndex < 0 || selectedLayerIndex >= materialLayers.Count ||
            string.IsNullOrEmpty(selectedMapName))
        {
            return; // 非绘制模式或条件不满足时不处理
        }
        
        Event currentEvent = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        
        // 1. 计算笔刷世界大小
        float worldSize = brushSize * Mathf.Max(
            targetMesh.bounds.extents.x, 
            targetMesh.bounds.extents.y, 
            targetMesh.bounds.extents.z);
        
        // 2. 实时获取鼠标射线
        Ray worldRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
        bool isHitTarget = Physics.Raycast(worldRay, out RaycastHit hit) && hit.collider.gameObject == targetObject;
        
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
        if (targetRenderer == null || targetShader == null) return;

        // 先收集所有孤立图层，避免迭代时修改集合
        List<(int layerIndex, TextureMapEntry entry)> isolatedEntries = new List<(int, TextureMapEntry)>();
        foreach (var layer in materialLayers)
        {
            foreach (var entry in layer.textureMaps)
            {
                if (entry.isolateThisMap)
                {
                    isolatedEntries.Add((materialLayers.IndexOf(layer), entry));
                }
            }
        }

        // 处理孤立图层
        bool hasIsolated = isolatedEntries.Count > 0;

        if (hasIsolated)
        {
            var firstIsolated = isolatedEntries[0];
            ApplyIsolatedPreview(firstIsolated.layerIndex, firstIsolated.entry);
        }
        else
        {

            // 没有孤立图层，使用绘制材质
            if (paintingMaterial == null)
            {
                // 首次进入绘制模式，初始化绘制材质，拷贝原始材质参数
                paintingMaterial = new Material(targetShader);
                paintingMaterial.CopyPropertiesFromMaterial(baseMaterial);

                // 将原始参数逐个拷贝到参数图层
                // 待开发。。。

            }

            // 应用所有可见图层
            foreach (var layer in materialLayers.ToList()) // 使用ToList()创建副本避免迭代冲突
            {
                if (!layer.visible) continue;

                foreach (var entry in layer.textureMaps.ToList()) // 使用ToList()创建副本
                {
                    if (entry.isPainting && entry.textureData.paintTexture != null)
                    {
                        paintingMaterial.SetTexture(entry.mapName, entry.textureData.paintTexture);
                    }
                }
            }
            
            targetRenderer.sharedMaterial = paintingMaterial;
        }

        // 强制更新渲染器
        targetRenderer.enabled = false;
        targetRenderer.enabled = true;
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
                    if (EditorUtility.DisplayDialog("确认", $"确定要删除材质？ '{layer.layerName}' 吗?", "是", "否"))
                    {
                        bool wasActive = layer.isActive;
                        materialLayers.RemoveAt(index);
                        // 删除材质
                        
                        if(materialLayers.Count>0){
                            // 如果还有别的材质则以别的材质显示
                            paintingMaterial = materialLayers[0].LayerMaterial;
                            targetRenderer.sharedMaterial = paintingMaterial!=null?paintingMaterial:baseMaterial;
                        }else{
                            // 否则就赋予基础参考材质
                            paintingMaterial = null;
                            targetRenderer.sharedMaterial = baseMaterial;
                        }
                        
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
                
                // if (GUILayout.Button("选择绘制", GUILayout.Width(80)))
                // {
                //     foreach (var l in materialLayers)
                //     {
                //         l.isActive = false;
                //     }
                    
                //     layer.isActive = true;
                //     selectedLayerIndex = index;
                //     if (string.IsNullOrEmpty(selectedMapName) && layer.textureMaps.Count > 0)
                //     {
                //         selectedMapName = layer.textureMaps[0].mapName;
                //     }
                // }
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
    private void ApplyIsolatedPreview(int layerIndex, TextureMapEntry entry)
    {
        if (previewSingleLayerMaterial == null) return;

        // 设置预览材质使用当前图层作为自发光
        if (entry.textureData.paintTexture != null)
        {
            previewSingleLayerMaterial.SetTexture("_BaseMap", entry.textureData.paintTexture);
        }
        else if (entry.textureData.sourceTexture != null)
        {
            previewSingleLayerMaterial.SetTexture("_BaseMap", entry.textureData.sourceTexture);//_EmissionMap
        }

        // 应用到目标对象
        if (targetRenderer != null)
        {
            targetRenderer.sharedMaterial = previewSingleLayerMaterial;
        }
    }

    // 1. 修复DrawLayerParameters中的按钮逻辑，确保状态正确切换
    private void DrawLayerParameters(int layerIndex, MaterialLayer layer)
    {
        if (layer == null) return;
        
        GUILayout.BeginVertical(EditorStyles.helpBox);
        {
            //遍历当前图层的所有参数层
            foreach (var entry in layer.textureMaps)
            {
                // 保存旧的孤立状态用于判断变化
                bool oldIsolateState = entry.isolateThisMap;

                // 定义选中和未选中的样式（选中时用蓝色背景）
                GUIStyle entryStyle = entry.isPainting ? activeMapStyle : defaultMapStyle;

                // 开始横向布局，并获取其矩形范围
                GUILayout.BeginHorizontal(entryStyle);
                {
                    // 以下是原有UI元素（孤立复选框、名称、纹理选择等）
                    GUILayout.Label("孤立", GUILayout.Width(40),GUILayout.Height(50));
                    entry.isolateThisMap = EditorGUILayout.Toggle(entry.isolateThisMap, GUILayout.Width(40),GUILayout.Height(50));

                    // 处理孤立状态变化（保持原有逻辑）
                    if (oldIsolateState != entry.isolateThisMap)
                    {
                        if (entry.isolateThisMap)
                        {
                            // 取消其他所有孤立设置
                            foreach (var e in layer.textureMaps)
                            {
                                if (e != entry)
                                {
                                    e.isolateThisMap = false;
                                }
                            }
                            // 孤立显示该参数图层
                            ApplyIsolatedPreview(layerIndex, entry);
                        }
                        else
                        {
                            // 取消孤立单个图层，显示材质图层完整材质
                            RestoreAllMapsVisibility();
                        }
                    }

                    // 显示参数层名称（去掉前缀下划线）
                    string displayName = entry.mapName.StartsWith("_") ? entry.mapName.Substring(1) : entry.mapName;
                    //EditorGUILayout.LabelField(displayName, GUILayout.Width(150), GUILayout.Height(50));
                    
                    // string buttonText = entry.isPainting ? "绘制结束" : "绘制此图";
                    if (GUILayout.Button(displayName,GUILayout.Width(200), GUILayout.Height(50)))
                    {
                        // 关键修复：切换状态时强制更新选中的参数层
                        //bool true = !entry.isPainting;

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
                        entry.isPainting = true;

                        // 更新选中的图层和参数层
                        selectedLayerIndex = layerIndex;
                        selectedMapName = entry.mapName;
                        //currentEvent.Use(); // 防止事件穿透到其他UI
                        
                        
                    }
                    
                    GUILayout.FlexibleSpace();

                    // 纹理选择框
                    Texture2D newSourceTexture = EditorGUILayout.ObjectField(
                        entry.textureData.sourceTexture,
                        typeof(Texture2D),
                        false,
                        GUILayout.Width(50),  // 固定宽度保持小预览图
                        GUILayout.Height(50)  // 匹配编辑器字段高度
                    ) as Texture2D;

                    if (newSourceTexture != entry.textureData.sourceTexture)
                    {
                        entry.textureData.sourceTexture = newSourceTexture;
                        InitializePaintTextureFromSource(layerIndex, entry.mapName);
                        UpdateMaterialPreview();
                    }

                    // 导出按钮
                    if (GUILayout.Button("导出", GUILayout.Width(50), GUILayout.Height(50)))
                    {
                        ExportTexture(layerIndex, entry.mapName);
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
        if (targetObject == null || previewSingleLayerMaterial == null) return;
        
        foreach (var renderer in targetObject.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == previewSingleLayerMaterial)
                {
                    // 隐藏其他所有纹理
                    foreach (var layer in materialLayers)
                    {
                        foreach (var entry in layer.textureMaps)
                        {
                            if (entry.mapName != mapName)
                            {
                                previewSingleLayerMaterial.SetTexture(entry.mapName, null);
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
        targetRenderer.sharedMaterial = paintingMaterial;
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
            mapEntry = layer.textureMaps.FirstOrDefault(e => e.mapName == selectedMapName);
        }
        
        if (mapEntry == null)
        {
            UnityEngine.Debug.LogWarning("未找到有效的绘制目标参数层");
            return;
        }
        
        // 更新选中的参数层名称（同步状态）
        selectedMapName = mapEntry.mapName;
        
        TextureData textureData = mapEntry.textureData;
        if (textureData.paintTexture == null)
        {
            UnityEngine.Debug.LogWarning("绘制目标纹理为空，已自动初始化");
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
            UnityEngine.Debug.Log("已自动添加MeshCollider以支持UV检测");
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
        
        int brushPixelSize = Mathf.Max(1, (int)(brushSize * texture.width * 0.5f));
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
    
        // 处理所有渲染器组件
        foreach (var renderer in FindObjectsOfType<Renderer>())
        {
            if(renderer.gameObject != targetObject ){
                renderer.enabled = !isolate;
            }
        }
        // 处理所有碰撞组件
        foreach (var collider in FindObjectsOfType<Collider>())
        {
            if(collider.gameObject != targetObject ){
                collider.enabled = !isolate;
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