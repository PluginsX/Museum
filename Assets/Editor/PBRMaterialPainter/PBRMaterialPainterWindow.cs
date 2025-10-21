#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PBRMaterialPainterTool
{
    public class PBRMaterialPainterWindow : EditorWindow
    {
        private static PBRMaterialPainterWindow _instance;

        [MenuItem("Tools/PBR Material Painter")] 
        public static void Open()
        {
            _instance = GetWindow<PBRMaterialPainterWindow>("PBR Painter");
            _instance.minSize = new Vector2(420, 360);
        }

        private Material _referenceMaterial;
        private readonly List<ShaderTextureProperty> _mappedProps = new List<ShaderTextureProperty>();
        private readonly List<MaterialLayer> _layers = new List<MaterialLayer>();

        private int _textureSize = 2048;

        private bool _isPaintingMode = false;
        private bool _isIsolationMode = false;
        private GameObject _targetObject;
        private MeshCollider _runtimeCollider;

        private int _activeLayerIndex = -1;
        private ParameterSemantic? _activeSemantic = null;

        private enum ProjectionMode { ScreenProject, NormalAligned }
        private ProjectionMode _projectionMode = ProjectionMode.ScreenProject;

        private enum PaintMode { Brush, Fill }
        private PaintMode _paintMode = PaintMode.Brush;

        // Brush params
        private Color _brushColor = Color.white;
        private int _brushSizePx = 64; // diameter in pixels
        private float _brushHardness = 0.6f; // 0..1
        private Texture2D _brushAlpha;

        // Fill params
        private Color _fillColor = Color.white;
        private enum FillTarget { Object, Element /*, UVIsland*/ }
        private FillTarget _fillTarget = FillTarget.Object;

        private Vector2 _scroll;
        // Cached UV per screen pixel at cursor for screen-projection brush sizing
        private float _lastUvPerScreenPixel = -1f;

        private void OnEnable()
        {
            _instance = this;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            ExitPaintModeInternal();
            foreach (var l in _layers) l.Dispose();
            _layers.Clear();
            PainterMaterials.Cleanup();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            DrawModeControls();

            EditorGUILayout.Space();
            DrawMaterialListArea();

            EditorGUILayout.Space();
            DrawLayerList();

            EditorGUILayout.Space();
            DrawProjectionAndPaintModes();
        }

        private void DrawModeControls()
        {
            GUILayout.Label("模式控制", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_isPaintingMode))
                {
                    if (GUILayout.Button("进入绘制模式"))
                    {
                        EnterPaintMode();
                    }
                }
                using (new EditorGUI.DisabledScope(!_isPaintingMode))
                {
                    if (GUILayout.Button("退出绘制模式"))
                    {
                        ExitPaintMode();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_isPaintingMode || _targetObject == null || _isIsolationMode))
                {
                    if (GUILayout.Button("孤立显示绘制对象"))
                    {
                        ToggleIsolation(true);
                    }
                }
                using (new EditorGUI.DisabledScope(!_isPaintingMode || !_isIsolationMode))
                {
                    if (GUILayout.Button("退出孤立模式"))
                    {
                        ToggleIsolation(false);
                    }
                }
            }
        }

        private void DrawMaterialListArea()
        {
            GUILayout.Label("材质列表", EditorStyles.boldLabel);
            _referenceMaterial = (Material)EditorGUILayout.ObjectField("基本参考材质", _referenceMaterial, typeof(Material), false);
            if (GUILayout.Button("合并所有材质层并导出材质"))
            {
                ExportMergedMaterial();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _textureSize = EditorGUILayout.IntPopup("工作纹理分辨率", _textureSize, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
                if (GUILayout.Button("新建材质层", GUILayout.Width(120)))
                {
                    AddNewLayer();
                }
            }
        }

        private void DrawLayerList()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_referenceMaterial != null)
            {
                UpdateMappedProperties();
                EnsureAllLayersHaveParameters();
            }

            for (int i = _layers.Count - 1; i >= 0; i--) // display top-first
            {
                var layer = _layers[i];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"层序号-{i}", GUILayout.Width(80));
                        layer.layerName = EditorGUILayout.TextField(layer.layerName ?? $"Layer {i}");
                        layer.isVisible = GUILayout.Toggle(layer.isVisible, "显隐", "Button", GUILayout.Width(60));
                        layer.isFoldout = GUILayout.Toggle(layer.isFoldout, "展开参数层", "Button", GUILayout.Width(100));

                        using (new EditorGUI.DisabledScope(_referenceMaterial == null))
                        {
                            if (GUILayout.Button("导出材质", GUILayout.Width(90)))
                            {
                                ExportLayerAsMaterial(i);
                            }
                        }

                        if (GUILayout.Button("删除", GUILayout.Width(60)))
                        {
                            RemoveLayerAt(i);
                            continue;
                        }
                    }

                    if (layer.isFoldout)
                    {
                        foreach (var prop in _mappedProps)
                        {
                            if (!layer.parameters.TryGetValue(prop.semantic, out var param)) continue;

                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Space(12);
                                GUILayout.Label(SemanticLabel(prop.semantic), GUILayout.Width(140));

                                EditorGUI.BeginChangeCheck();
                                var newTex = (Texture2D)EditorGUILayout.ObjectField(param.baseTexture, typeof(Texture2D), false);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    param.baseTexture = newTex;
                                }

                                if (GUILayout.Button("导入为基础", GUILayout.Width(90)))
                                {
                                    param.ImportBaseFromTexture(param.baseTexture);
                                }

                                if (GUILayout.Button("导出Texture2D", GUILayout.Width(120)))
                                {
                                    ExportParameterTexture(param, prop.semantic);
                                }

                                bool isActiveTarget = (_activeLayerIndex == i && _activeSemantic.HasValue && _activeSemantic.Value == prop.semantic);
                                var setActiveLabel = isActiveTarget ? "当前绘制" : "设为绘制";
                                if (GUILayout.Button(setActiveLabel, GUILayout.Width(80)))
                                {
                                    _activeLayerIndex = i;
                                    _activeSemantic = prop.semantic;
                                }
                            }
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawProjectionAndPaintModes()
        {
            GUILayout.Label("投影模式", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _projectionMode = (ProjectionMode)GUILayout.Toolbar((int)_projectionMode, new[] { "屏幕投影", "对齐法线" });
            }

            GUILayout.Label("绘制模式", EditorStyles.boldLabel);
            _paintMode = (PaintMode)GUILayout.Toolbar((int)_paintMode, new[] { "画笔模式", "填充模式" });

            if (_paintMode == PaintMode.Brush)
            {
                EditorGUILayout.Space();
                GUILayout.Label("画笔参数", EditorStyles.boldLabel);
                _brushColor = EditorGUILayout.ColorField("颜色", _brushColor);
                _brushSizePx = EditorGUILayout.IntSlider("画笔尺寸(px)", _brushSizePx, 1, Mathf.Max(16, _textureSize));
                _brushHardness = EditorGUILayout.Slider("软硬", _brushHardness, 0f, 1f);
                _brushAlpha = (Texture2D)EditorGUILayout.ObjectField("画笔Alpha遮罩", _brushAlpha, typeof(Texture2D), false);
            }
            else
            {
                EditorGUILayout.Space();
                GUILayout.Label("填充参数", EditorStyles.boldLabel);
                _fillColor = EditorGUILayout.ColorField("颜色", _fillColor);
                _fillTarget = (FillTarget)EditorGUILayout.EnumPopup("填充目标", _fillTarget);
                EditorGUILayout.HelpBox("当前版本支持: 填充到对象。元素/UV岛填充将于后续版本提供。", MessageType.Info);
            }

            if (_activeLayerIndex >= 0 && _activeLayerIndex < _layers.Count && _activeSemantic.HasValue)
            {
                GUILayout.Label($"当前绘制目标: 层{_activeLayerIndex} - {SemanticLabel(_activeSemantic.Value)}", EditorStyles.helpBox);
            }
            else
            {
                EditorGUILayout.HelpBox("请选择一个层与参数作为绘制目标。", MessageType.Warning);
            }
        }

        private void EnterPaintMode()
        {
            if (Selection.activeGameObject == null)
            {
                EditorUtility.DisplayDialog("提示", "请选择一个场景中的Mesh对象后再进入绘制模式。", "确定");
                return;
            }

            _targetObject = Selection.activeGameObject;
            var mf = _targetObject.GetComponent<MeshFilter>();
            var mr = _targetObject.GetComponent<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("提示", "所选对象必须包含MeshFilter与MeshRenderer。", "确定");
                _targetObject = null;
                return;
            }

            _runtimeCollider = _targetObject.GetComponent<MeshCollider>();
            if (_runtimeCollider == null)
            {
                _runtimeCollider = _targetObject.AddComponent<MeshCollider>();
                _runtimeCollider.sharedMesh = mf.sharedMesh;
            }

            _isPaintingMode = true;
            SceneView.RepaintAll();
        }

        private void ExitPaintMode()
        {
            ToggleIsolation(false);
            ExitPaintModeInternal();
        }

        private void ExitPaintModeInternal()
        {
            _isPaintingMode = false;
            if (_runtimeCollider != null)
            {
                try
                {
                    DestroyImmediate(_runtimeCollider);
                }
                catch { /* ignore */ }
                _runtimeCollider = null;
            }
            _targetObject = null;
            SceneView.RepaintAll();
        }

        private void ToggleIsolation(bool enable)
        {
#if UNITY_2019_1_OR_NEWER
            if (_targetObject == null) return;
            if (enable)
            {
                SceneVisibilityManager.instance.Isolate(_targetObject, true);
                _isIsolationMode = true;
            }
            else
            {
                SceneVisibilityManager.instance.ExitIsolation();
                _isIsolationMode = false;
            }
#else
            EditorUtility.DisplayDialog("提示", "当前Unity版本不支持编辑器孤立显示API。", "确定");
#endif
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_isPaintingMode) return;
            if (_activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count || !_activeSemantic.HasValue) return;
            if (_targetObject == null || _runtimeCollider == null) return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            // Restrict painting to the selected object's collider
            RaycastHit? matchedHit = TryRaycastToSelected(ray);

            if (matchedHit.HasValue)
            {
                var hit = matchedHit.Value;
                // Approximate UV scale per one screen pixel around cursor for screen-projection sizing
                _lastUvPerScreenPixel = -1f;
                var rightRay = HandleUtility.GUIPointToWorldRay(e.mousePosition + Vector2.right);
                var upRay = HandleUtility.GUIPointToWorldRay(e.mousePosition + Vector2.up);
                var rightHit = TryRaycastToSelected(rightRay);
                var upHit = TryRaycastToSelected(upRay);
                if (rightHit.HasValue)
                {
                    _lastUvPerScreenPixel = Vector2.Distance(hit.textureCoord, rightHit.Value.textureCoord);
                }
                if (upHit.HasValue)
                {
                    float v = Vector2.Distance(hit.textureCoord, upHit.Value.textureCoord);
                    _lastUvPerScreenPixel = _lastUvPerScreenPixel < 0 ? v : (0.5f * (_lastUvPerScreenPixel + v));
                }

                // Draw brush indicator
                Handles.color = new Color(1, 0.5f, 0, 1);
                Handles.DrawWireDisc(hit.point, hit.normal, HandleUtility.GetHandleSize(hit.point) * 0.03f);

                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
                {
                    if (_paintMode == PaintMode.Brush)
                        DoBrushPaint(hit);
                    else
                        DoFillPaint(hit);
                    e.Use();
                }
            }
        }

        private RaycastHit? TryRaycastToSelected(Ray ray)
        {
            RaycastHit? matched = null;
            var hits = Physics.RaycastAll(ray, float.MaxValue);
            float minDist = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider == _runtimeCollider && h.distance < minDist)
                {
                    matched = h;
                    minDist = h.distance;
                }
            }
            return matched;
        }

        private void DoBrushPaint(RaycastHit hit)
        {
            if (_activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count || !_activeSemantic.HasValue) return;
            var layer = _layers[_activeLayerIndex];
            if (!layer.parameters.TryGetValue(_activeSemantic.Value, out var param)) return;

            float uvRadius;
            if (_projectionMode == ProjectionMode.ScreenProject && _lastUvPerScreenPixel > 0)
            {
                uvRadius = Mathf.Max(1, _brushSizePx) * 0.5f * _lastUvPerScreenPixel;
            }
            else
            {
                uvRadius = Mathf.Max(1, _brushSizePx) * 0.5f / Mathf.Max(1, param.textureSize);
            }

            var channelMask = ExportUtils.ChannelMaskFor(_activeSemantic.Value);
            var brushColor = ExportUtils.NormalizeBrushColorForSemantic(_brushColor, _activeSemantic.Value);
            param.StampBrushAtUV(hit.textureCoord, brushColor, uvRadius, _brushHardness, _brushAlpha, channelMask);
            SceneView.RepaintAll();
        }

        private void DoFillPaint(RaycastHit hit)
        {
            if (_activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count || !_activeSemantic.HasValue) return;
            var layer = _layers[_activeLayerIndex];
            if (!layer.parameters.TryGetValue(_activeSemantic.Value, out var param)) return;

            var channelMask = ExportUtils.ChannelMaskFor(_activeSemantic.Value);
            var color = ExportUtils.NormalizeBrushColorForSemantic(_fillColor, _activeSemantic.Value);

            switch (_fillTarget)
            {
                case FillTarget.Object:
                    param.FillWhole(color, channelMask);
                    break;
                case FillTarget.Element:
                    // Future work: element-only fill
                    param.FillWhole(color, channelMask);
                    break;
                default:
                    param.FillWhole(color, channelMask);
                    break;
            }
            SceneView.RepaintAll();
        }

        private void AddNewLayer()
        {
            if (_referenceMaterial == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个参考材质。", "确定");
                return;
            }

            UpdateMappedProperties();
            var layer = new MaterialLayer { layerName = $"Layer {_layers.Count}", isVisible = true };
            layer.EnsureParameters(_mappedProps, _textureSize);
            _layers.Add(layer);

            if (_activeLayerIndex < 0)
            {
                _activeLayerIndex = _layers.Count - 1;
                _activeSemantic = _mappedProps.Count > 0 ? _mappedProps[0].semantic : (ParameterSemantic?)null;
            }
        }

        private void RemoveLayerAt(int index)
        {
            if (index < 0 || index >= _layers.Count) return;
            _layers[index].Dispose();
            _layers.RemoveAt(index);
            if (_activeLayerIndex >= _layers.Count)
            {
                _activeLayerIndex = _layers.Count - 1;
            }
        }

        private void UpdateMappedProperties()
        {
            _mappedProps.Clear();
            if (_referenceMaterial != null)
            {
                _mappedProps.AddRange(ShaderPropertyDiscovery.DiscoverTextureProperties(_referenceMaterial));
            }
        }

        private void EnsureAllLayersHaveParameters()
        {
            foreach (var l in _layers)
            {
                l.EnsureParameters(_mappedProps, _textureSize);
            }
        }

        private string SemanticLabel(ParameterSemantic s)
        {
            switch (s)
            {
                case ParameterSemantic.BaseColor: return "BaseColor层(固有色层)";
                case ParameterSemantic.Roughness: return "Roughness层(粗糙度层)";
                case ParameterSemantic.Metallic: return "Metallic层(金属度层)";
                case ParameterSemantic.Occlusion: return "Occlusion层(环境光遮蔽层)";
                case ParameterSemantic.Emission: return "Emission层(自发光层)";
                case ParameterSemantic.Opacity: return "Opacity层(透明度层)";
                default: return s.ToString();
            }
        }

        private void ExportParameterTexture(PaintedParameterLayer param, ParameterSemantic semantic)
        {
            string defaultName = $"{semantic}.png";
            string path = EditorUtility.SaveFilePanelInProject("导出参数纹理", defaultName, "png", "选择导出路径");
            if (string.IsNullOrEmpty(path)) return;

            bool isColor = ExportUtils.IsColorSemantic(semantic);
            var tex = param.ToTexture2D(isColor);
            ExportUtils.ExportTextureToAsset(tex, path, isColor);
            DestroyImmediate(tex);
        }

        private void ExportLayerAsMaterial(int layerIndex)
        {
            if (_referenceMaterial == null) return;
            if (layerIndex < 0 || layerIndex >= _layers.Count) return;

            string matPath = EditorUtility.SaveFilePanelInProject("导出材质(当前层)", $"Layer_{layerIndex}.mat", "mat", "选择材质导出路径");
            if (string.IsNullOrEmpty(matPath)) return;

            string folder = Path.GetDirectoryName(matPath).Replace("\\", "/");
            string baseName = Path.GetFileNameWithoutExtension(matPath);

            var layer = _layers[layerIndex];
            var newMat = new Material(_referenceMaterial.shader);

            foreach (var prop in _mappedProps)
            {
                if (!layer.parameters.TryGetValue(prop.semantic, out var param)) continue;
                param.textureSize = _textureSize;
                param.EnsureInitialized();

                var tex2d = param.ToTexture2D(ExportUtils.IsColorSemantic(prop.semantic));
                string texAssetPath = Path.Combine(folder, $"{baseName}_{prop.semantic}.png").Replace("\\", "/");
                ExportUtils.ExportTextureToAsset(tex2d, texAssetPath, ExportUtils.IsColorSemantic(prop.semantic));
                DestroyImmediate(tex2d);

                newMat.SetTexture(prop.propertyName, AssetDatabase.LoadAssetAtPath<Texture2D>(texAssetPath));
            }

            AssetDatabase.CreateAsset(newMat, matPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void ExportMergedMaterial()
        {
            if (_referenceMaterial == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个参考材质。", "确定");
                return;
            }

            string matPath = EditorUtility.SaveFilePanelInProject("合并并导出材质", "PaintedMaterial.mat", "mat", "选择材质导出路径");
            if (string.IsNullOrEmpty(matPath)) return;

            string folder = Path.GetDirectoryName(matPath).Replace("\\", "/");
            string baseName = Path.GetFileNameWithoutExtension(matPath);

            var newMat = new Material(_referenceMaterial.shader);

            foreach (var prop in _mappedProps)
            {
                using (var composed = ComposeSemantic(prop.semantic))
                {
                    var tex2d = RTToTexture2D(composed, ExportUtils.IsColorSemantic(prop.semantic));
                    string texAssetPath = Path.Combine(folder, $"{baseName}_{prop.semantic}.png").Replace("\\", "/");
                    ExportUtils.ExportTextureToAsset(tex2d, texAssetPath, ExportUtils.IsColorSemantic(prop.semantic));
                    DestroyImmediate(tex2d);

                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(texAssetPath);
                    newMat.SetTexture(prop.propertyName, t);
                }
            }

            AssetDatabase.CreateAsset(newMat, matPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private RenderTexture ComposeSemantic(ParameterSemantic semantic)
        {
            foreach (var l in _layers) l.EnsureParameters(_mappedProps, _textureSize);
            return LayerCompositor.Compose(_layers, semantic, _textureSize);
        }

        private static Texture2D RTToTexture2D(RenderTexture rt, bool sRGB)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, !sRGB);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            return tex;
        }
    }
}
#endif
