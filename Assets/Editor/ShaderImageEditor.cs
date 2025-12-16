using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace Museum.Component.UGUI
{
    [CustomEditor(typeof(ShaderImage))]
    public class ShaderImageEditor : Editor
    {
        private ShaderImage shaderImage;
        private SerializedProperty targetShaderProperty;
        private SerializedProperty imageProperty;

        // 序列化属性
        private SerializedProperty floatPropertiesProperty;
        private SerializedProperty colorPropertiesProperty;
        private SerializedProperty vectorPropertiesProperty;
        private SerializedProperty texturePropertiesProperty;

        // 用于检测材质参数变化的哈希值
        private int lastMaterialHash = 0;

        // 用于跟踪同步状态
        private bool lastSyncState = true;

        // 折叠面板状态
        private bool showStoredProperties = false;

        private void RefreshSerializedState(bool requestRepaint = false)
        {
            if (shaderImage == null)
            {
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();
            lastSyncState = shaderImage.AreMaterialPropertiesInSync();

            if (requestRepaint)
            {
                Repaint();
            }
        }


        private void OnEnable()
        {
            shaderImage = (ShaderImage)target;

            // 获取序列化属性
            targetShaderProperty = serializedObject.FindProperty("targetShader");
            imageProperty = serializedObject.FindProperty("image");
            floatPropertiesProperty = serializedObject.FindProperty("floatProperties");
            colorPropertiesProperty = serializedObject.FindProperty("colorProperties");
            vectorPropertiesProperty = serializedObject.FindProperty("vectorProperties");
            texturePropertiesProperty = serializedObject.FindProperty("textureProperties");

            // 添加播放模式变化的监听
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // 添加编辑器更新回调，持续检查参数变化
            EditorApplication.update += OnEditorUpdate;

            RefreshSerializedState();
        }


        private void OnDisable()
        {
            // 移除监听，避免内存泄漏
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();

            // Shader/Material选择字段
            EditorGUILayout.BeginHorizontal();
            GUIContent shaderMaterialLabel = new GUIContent("Shader/Material", "支持拖入Shader或Material文件");
            Object currentObject = targetShaderProperty.objectReferenceValue;
            Object newShaderOrMaterial = EditorGUILayout.ObjectField(shaderMaterialLabel, currentObject, typeof(Object), false);
            
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                newShaderOrMaterial = null;
            }
            EditorGUILayout.EndHorizontal();
            
            if (newShaderOrMaterial != currentObject)
            {
                if (newShaderOrMaterial != null)
                {
                    if (newShaderOrMaterial is Shader)
                    {
                        targetShaderProperty.objectReferenceValue = newShaderOrMaterial;
                        serializedObject.ApplyModifiedProperties();
                        shaderImage.UpdateShader();
                    }
                    else if (newShaderOrMaterial is Material)
                    {
                        Material material = (Material)newShaderOrMaterial;
                        targetShaderProperty.objectReferenceValue = material.shader;
                        serializedObject.ApplyModifiedProperties();
                        shaderImage.UpdateShader();
                    }
                }
                else
                {
                    targetShaderProperty.objectReferenceValue = null;
                    serializedObject.ApplyModifiedProperties();
                    shaderImage.UpdateShader();
                }
                
                return;
            }

            EditorGUILayout.Space();

            // 显示所有材质参数（区分默认值和修改过的值）
            if (shaderImage.TargetShader != null)
            {
                // 参数保存状态指示器 - 移到前面
                bool isInSync = shaderImage.AreMaterialPropertiesInSync();
                lastSyncState = isInSync;
                EditorGUILayout.BeginHorizontal();
                if (isInSync)

                {
                    EditorGUILayout.HelpBox("✓ 参数已保存", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("⚠ 参数未保存", MessageType.Warning);
                }

                // 保存按钮 - 根据状态控制可用性，与HelpBox高度一致
                GUI.enabled = !isInSync; // 已保存时按钮不可用
                if (GUILayout.Button("保存参数", GUILayout.Width(80), GUILayout.Height(38)))
                {
                    shaderImage.ForceSaveAllCurrentParameters();

                    if (Application.isPlaying)
                    {
                        ShaderImageRuntimeSaveTracker.CaptureRuntimeState(shaderImage);
                    }

                    RefreshSerializedState(true);
                }


                GUI.enabled = true; // 恢复GUI状态

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                var allProperties = shaderImage.GetAllShaderProperties();
                int modifiedCount = shaderImage.FloatProperties.Count +
                                  shaderImage.ColorProperties.Count +
                                  shaderImage.VectorProperties.Count +
                                  shaderImage.TextureProperties.Count;

                if (allProperties.Count > 0)
                {
                    EditorGUILayout.Space();
                    showStoredProperties = EditorGUILayout.Foldout(showStoredProperties,
                        $"材质参数 ({modifiedCount}/{allProperties.Count} 已修改)", true);

                    if (showStoredProperties)
                    {
                        EditorGUI.indentLevel++;

                        // 按类型分组显示参数
                        DisplayPropertiesByType(allProperties, "Float/Range", ShaderPropertyType.Float, ShaderPropertyType.Range);
                        DisplayPropertiesByType(allProperties, "Color", ShaderPropertyType.Color);
                        DisplayPropertiesByType(allProperties, "Vector", ShaderPropertyType.Vector);
                        DisplayPropertiesByType(allProperties, "Texture", ShaderPropertyType.Texture);

                        EditorGUI.indentLevel--;

                        if (modifiedCount > 0)
                        {
                            EditorGUILayout.HelpBox("M按钮：标记参数为修改状态\nR按钮：恢复默认值并取消标记\n灰色参数：未标记修改，黑色参数：已标记修改\n只有标记为修改的参数会被保存和应用。", MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("M按钮：标记参数为修改状态\nR按钮：恢复默认值并取消标记\n灰色参数：未标记修改，黑色参数：已标记修改\n目前没有标记为修改的参数。", MessageType.Info);
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Shader没有可编辑的参数", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("请先选择一个Shader", MessageType.Info);
            }

            // 检查材质实例参数是否变化（用于默认材质界面修改的参数保存）
            CheckMaterialPropertiesChanges();

            serializedObject.ApplyModifiedProperties();
        }

        // 检查材质实例参数是否变化
        private bool CheckMaterialPropertiesChanges()
        {
            if (shaderImage.MaterialInstance == null) return false;

            // 计算材质参数的哈希值
            int currentHash = CalculateMaterialPropertiesHash(shaderImage.MaterialInstance);

            // 如果哈希值变化，说明材质参数被修改
            if (currentHash != lastMaterialHash)
            {
                lastMaterialHash = currentHash;

                if (!Application.isPlaying)
                {
                    // 只在编辑器模式下更新标记和保存值
                    CheckAndUpdatePropertyMarks();
                    shaderImage.UpdateMaterialPropertiesList();
                    EditorUtility.SetDirty(shaderImage);
                }

                // 总是重绘UI
                Repaint();
                return true;
            }

            return false;
        }


        // 检查并更新属性标记状态
        private void CheckAndUpdatePropertyMarks()
        {
            // 只在编辑器模式下自动标记参数，在运行时不自动标记（需要用户手动保存）
            if (Application.isPlaying) return;

            if (shaderImage.MaterialInstance == null || shaderImage.TargetShader == null) return;

            var allProperties = shaderImage.GetAllShaderProperties();

            foreach (var property in allProperties)
            {
                bool isCurrentlyModified = shaderImage.IsPropertyModified(property.name, property.type);
                bool isMarkedModified = shaderImage.IsPropertyMarkedModified(property.name);

                // 如果参数值发生了变化且当前没有被标记，自动标记为修改
                if (isCurrentlyModified && !isMarkedModified)
                {
                    shaderImage.MarkPropertyAsModified(property.name);
                }
            }
        }

        // 计算材质参数的哈希值
        private int CalculateMaterialPropertiesHash(Material material)
        {
            int hash = 0;
            
            if (material == null || material.shader == null) return hash;

            int propertyCount = material.shader.GetPropertyCount();
            
            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = material.shader.GetPropertyName(i);
                ShaderPropertyType propertyType = material.shader.GetPropertyType(i);

                if (material.HasProperty(propertyName))
                {
                    switch (propertyType)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            hash ^= material.GetFloat(propertyName).GetHashCode();
                            break;
                        case ShaderPropertyType.Color:
                            Color color = material.GetColor(propertyName);
                            hash ^= color.GetHashCode();
                            break;
                        case ShaderPropertyType.Vector:
                            Vector4 vector = material.GetVector(propertyName);
                            hash ^= vector.GetHashCode();
                            break;
                        case ShaderPropertyType.Texture:
                            Texture texture = material.GetTexture(propertyName);
                            if (texture != null)
                                hash ^= texture.GetInstanceID();
                            break;
                    }
                }
            }
            
            return hash;
        }

        // 编辑器更新回调，持续检查参数变化
        private void OnEditorUpdate()
        {
            bool hasMaterialChange = CheckMaterialPropertiesChanges();
            if (hasMaterialChange && !Application.isPlaying)
            {
                RefreshSerializedState();
            }

            // 在运行时也定期检查同步状态
            if (Application.isPlaying && shaderImage != null)
            {
                // 每帧检查一次同步状态，如果不同步则重绘UI
                bool currentSyncState = shaderImage.AreMaterialPropertiesInSync();
                if (currentSyncState != lastSyncState)
                {
                    lastSyncState = currentSyncState;
                    Repaint();
                }
            }
        }


        // 播放模式变化监听
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 重置材质哈希，避免模式切换时的误检测
            lastMaterialHash = 0;

            // 在退出播放模式后，重新创建材质实例并应用保存的参数
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                shaderImage.UpdateShader();

                // 强制刷新Image组件，确保最新参数立即生效
                Image imageComponent = shaderImage.GetComponent<Image>();
                if (imageComponent != null && shaderImage.MaterialInstance != null)
                {
                    imageComponent.SetMaterialDirty();
                    // 强制重新应用材质，确保绘制正确
                    imageComponent.material = shaderImage.MaterialInstance;
                }

                RefreshSerializedState(true);
            }

        }

        // 按类型显示属性
        private void DisplayPropertiesByType(List<(string name, ShaderPropertyType type, object defaultValue, object currentValue)> allProperties,
                                           string label, params ShaderPropertyType[] propertyTypes)
        {
            var filteredProperties = allProperties.Where(p => propertyTypes.Contains(p.type)).ToList();

            if (filteredProperties.Count == 0) return;

            EditorGUILayout.LabelField($"{label} 属性:", EditorStyles.boldLabel);

            foreach (var property in filteredProperties)
            {
                bool isMarkedModified = shaderImage.IsPropertyMarkedModified(property.name);
                GUIStyle labelStyle = new GUIStyle(EditorStyles.label);

                // 未标记修改的参数用灰色
                if (!isMarkedModified)
                {
                    labelStyle.normal.textColor = Color.gray;
                }

                EditorGUILayout.BeginHorizontal();

                // 根据属性类型显示不同的编辑器控件
                switch (property.type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        if (shaderImage.MaterialInstance != null && shaderImage.MaterialInstance.HasProperty(property.name))
                        {
                            float currentValue = shaderImage.MaterialInstance.GetFloat(property.name);

                            // 只有标记为修改的参数才能编辑
                            bool wasEnabled = GUI.enabled;
                            GUI.enabled = isMarkedModified;

                            // 自定义布局支持拖拽参数名调整值
                            EditorGUILayout.BeginHorizontal();

                            // 参数名标签，支持拖拽调整值
                            Rect labelRect = GUILayoutUtility.GetRect(new GUIContent(property.name), labelStyle, GUILayout.Width(120));
                            EditorGUI.LabelField(labelRect, property.name, labelStyle);

                            // 处理拖拽事件
                            if (isMarkedModified && Event.current.type == EventType.MouseDrag && labelRect.Contains(Event.current.mousePosition))
                            {
                                float dragDelta = Event.current.delta.x * 0.01f; // 调整灵敏度
                                currentValue += dragDelta;
                                shaderImage.MaterialInstance.SetFloat(property.name, currentValue);
                                shaderImage.UpdateMaterialPropertiesList();
                                EditorUtility.SetDirty(shaderImage);
                                ((Image)shaderImage.GetComponent(typeof(Image))).SetMaterialDirty();

                                // 运行时参数修改通知，触发保存状态更新
                                if (Application.isPlaying)
                                {
                                    shaderImage.NotifyRuntimeParameterChanged(property.name);
                                }

                                Event.current.Use();
                                GUI.changed = true;
                            }

                            // 数值输入字段
                            float newValue = EditorGUILayout.FloatField(currentValue);
                            if (isMarkedModified && newValue != currentValue)
                            {
                                shaderImage.MaterialInstance.SetFloat(property.name, newValue);
                                shaderImage.UpdateMaterialPropertiesList();
                                EditorUtility.SetDirty(shaderImage);
                                ((Image)shaderImage.GetComponent(typeof(Image))).SetMaterialDirty();

                                // 运行时参数修改通知，触发保存状态更新
                                if (Application.isPlaying)
                                {
                                    shaderImage.NotifyRuntimeParameterChanged(property.name);
                                }
                            }

                            EditorGUILayout.EndHorizontal();

                            GUI.enabled = wasEnabled;
                        }
                        break;

                    case ShaderPropertyType.Color:
                        if (shaderImage.MaterialInstance != null && shaderImage.MaterialInstance.HasProperty(property.name))
                        {
                            Color currentValue = shaderImage.MaterialInstance.GetColor(property.name);

                            // 显示参数名
                            EditorGUILayout.LabelField(property.name, labelStyle, GUILayout.Width(120));

                            // 只有标记为修改的参数才能编辑
                            bool wasEnabled = GUI.enabled;
                            GUI.enabled = isMarkedModified;

                            Color newValue = EditorGUILayout.ColorField(currentValue);
                            if (isMarkedModified && newValue != currentValue)
                            {
                                shaderImage.MaterialInstance.SetColor(property.name, newValue);
                                shaderImage.UpdateMaterialPropertiesList();
                                EditorUtility.SetDirty(shaderImage);
                                ((Image)shaderImage.GetComponent(typeof(Image))).SetMaterialDirty();

                                // 运行时参数修改通知，触发保存状态更新
                                if (Application.isPlaying)
                                {
                                    shaderImage.NotifyRuntimeParameterChanged(property.name);
                                }
                            }

                            GUI.enabled = wasEnabled;
                        }
                        break;

                    case ShaderPropertyType.Vector:
                        if (shaderImage.MaterialInstance != null && shaderImage.MaterialInstance.HasProperty(property.name))
                        {
                            Vector4 currentValue = shaderImage.MaterialInstance.GetVector(property.name);

                            // 显示参数名
                            EditorGUILayout.LabelField(property.name, labelStyle, GUILayout.Width(120));

                            // 只有标记为修改的参数才能编辑
                            bool wasEnabled = GUI.enabled;
                            GUI.enabled = isMarkedModified;

                            Vector4 newValue = EditorGUILayout.Vector4Field("", currentValue);
                            if (isMarkedModified && newValue != currentValue)
                            {
                                shaderImage.MaterialInstance.SetVector(property.name, newValue);
                                shaderImage.UpdateMaterialPropertiesList();
                                EditorUtility.SetDirty(shaderImage);
                                ((Image)shaderImage.GetComponent(typeof(Image))).SetMaterialDirty();

                                // 运行时参数修改通知，触发保存状态更新
                                if (Application.isPlaying)
                                {
                                    shaderImage.NotifyRuntimeParameterChanged(property.name);
                                }
                            }

                            GUI.enabled = wasEnabled;
                        }
                        break;

                    case ShaderPropertyType.Texture:
                        if (shaderImage.MaterialInstance != null && shaderImage.MaterialInstance.HasProperty(property.name))
                        {
                            Texture currentValue = shaderImage.MaterialInstance.GetTexture(property.name);

                            // 显示参数名
                            EditorGUILayout.LabelField(property.name, labelStyle, GUILayout.Width(120));

                            // 只有标记为修改的参数才能编辑
                            bool wasEnabled = GUI.enabled;
                            GUI.enabled = isMarkedModified;

                            Texture newValue = (Texture)EditorGUILayout.ObjectField(currentValue, typeof(Texture), false);
                            if (isMarkedModified && newValue != currentValue)
                            {
                                shaderImage.MaterialInstance.SetTexture(property.name, newValue);
                                shaderImage.UpdateMaterialPropertiesList();
                                EditorUtility.SetDirty(shaderImage);
                                ((Image)shaderImage.GetComponent(typeof(Image))).SetMaterialDirty();

                                // 运行时参数修改通知，触发保存状态更新
                                if (Application.isPlaying)
                                {
                                    shaderImage.NotifyRuntimeParameterChanged(property.name);
                                }
                            }

                            GUI.enabled = wasEnabled;
                        }
                        break;
                }

                // M/R按钮
                if (isMarkedModified)
                {
                    if (GUILayout.Button("R", GUILayout.Width(20)))
                    {
                        shaderImage.UnmarkPropertyAsModified(property.name);
                    }
                }
                else
                {
                    if (GUILayout.Button("M", GUILayout.Width(20)))
                    {
                        shaderImage.MarkPropertyAsModified(property.name);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
        }
    }

    internal static class ShaderImageRuntimeSaveTracker
    {
        private class ShaderImageSavedState
        {
            public Shader Shader;
            public List<ShaderImage.FloatProperty> FloatProperties;
            public List<ShaderImage.ColorProperty> ColorProperties;
            public List<ShaderImage.VectorProperty> VectorProperties;
            public List<ShaderImage.TextureProperty> TextureProperties;
            public List<string> MarkedPropertyNames;

            public ShaderImageSavedState(ShaderImage source)
            {
                Shader = source.TargetShader;
                FloatProperties = CloneFloatProperties(source.FloatProperties);
                ColorProperties = CloneColorProperties(source.ColorProperties);
                VectorProperties = CloneVectorProperties(source.VectorProperties);
                TextureProperties = CloneTextureProperties(source.TextureProperties);
                MarkedPropertyNames = new List<string>(source.GetMarkedPropertyNamesSnapshot());
            }

            public void ApplyTo(ShaderImage target)
            {
                target.OverrideSavedParameters(
                    Shader,
                    FloatProperties,
                    ColorProperties,
                    VectorProperties,
                    TextureProperties,
                    MarkedPropertyNames);
            }

            private static List<ShaderImage.FloatProperty> CloneFloatProperties(List<ShaderImage.FloatProperty> source)
            {
                List<ShaderImage.FloatProperty> result = new List<ShaderImage.FloatProperty>();
                if (source == null)
                {
                    return result;
                }

                foreach (var property in source)
                {
                    if (property == null || string.IsNullOrEmpty(property.propertyName))
                    {
                        continue;
                    }

                    result.Add(new ShaderImage.FloatProperty
                    {
                        propertyName = property.propertyName,
                        value = property.value
                    });
                }

                return result;
            }

            private static List<ShaderImage.ColorProperty> CloneColorProperties(List<ShaderImage.ColorProperty> source)
            {
                List<ShaderImage.ColorProperty> result = new List<ShaderImage.ColorProperty>();
                if (source == null)
                {
                    return result;
                }

                foreach (var property in source)
                {
                    if (property == null || string.IsNullOrEmpty(property.propertyName))
                    {
                        continue;
                    }

                    result.Add(new ShaderImage.ColorProperty
                    {
                        propertyName = property.propertyName,
                        value = property.value
                    });
                }

                return result;
            }

            private static List<ShaderImage.VectorProperty> CloneVectorProperties(List<ShaderImage.VectorProperty> source)
            {
                List<ShaderImage.VectorProperty> result = new List<ShaderImage.VectorProperty>();
                if (source == null)
                {
                    return result;
                }

                foreach (var property in source)
                {
                    if (property == null || string.IsNullOrEmpty(property.propertyName))
                    {
                        continue;
                    }

                    result.Add(new ShaderImage.VectorProperty
                    {
                        propertyName = property.propertyName,
                        value = property.value
                    });
                }

                return result;
            }

            private static List<ShaderImage.TextureProperty> CloneTextureProperties(List<ShaderImage.TextureProperty> source)
            {
                List<ShaderImage.TextureProperty> result = new List<ShaderImage.TextureProperty>();
                if (source == null)
                {
                    return result;
                }

                foreach (var property in source)
                {
                    if (property == null || string.IsNullOrEmpty(property.propertyName))
                    {
                        continue;
                    }

                    result.Add(new ShaderImage.TextureProperty
                    {
                        propertyName = property.propertyName,
                        value = property.value
                    });
                }

                return result;
            }
        }

        private static readonly Dictionary<string, ShaderImageSavedState> PendingStates = new Dictionary<string, ShaderImageSavedState>();
        private static bool _initialized;

        public static void CaptureRuntimeState(ShaderImage runtimeInstance)
        {
            EnsureInitialized();

            if (!Application.isPlaying || runtimeInstance == null)
            {
                return;
            }

            string id = runtimeInstance.PersistentId;
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            PendingStates[id] = new ShaderImageSavedState(runtimeInstance);
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            EditorApplication.playModeStateChanged += ApplyPendingStates;
        }

        private static void ApplyPendingStates(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || PendingStates.Count == 0)
            {
                return;
            }

            var shaderImages = Object.FindObjectsOfType<ShaderImage>(true);
            foreach (var instance in shaderImages)
            {
                if (instance == null)
                {
                    continue;
                }

                string id = instance.PersistentId;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (PendingStates.TryGetValue(id, out var savedState))
                {
                    savedState.ApplyTo(instance);
                    EditorUtility.SetDirty(instance);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                }
            }

            PendingStates.Clear();
            AssetDatabase.SaveAssets();
        }
    }
}
