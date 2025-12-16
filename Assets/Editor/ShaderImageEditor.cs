using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Museum.Component.UGUI
{
    [CustomEditor(typeof(ShaderImage))]
    public class ShaderImageEditor : Editor
    {
        private ShaderImage shaderImage;
        private SerializedProperty targetShaderProperty;
        private SerializedProperty imageProperty;

        // 用于检测材质参数变化的哈希值
        private int lastMaterialHash = 0;

        private void OnEnable()
        {
            shaderImage = (ShaderImage)target;

            // 获取序列化属性
            targetShaderProperty = serializedObject.FindProperty("targetShader");
            imageProperty = serializedObject.FindProperty("image");

            // 添加播放模式变化的监听
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // 添加编辑器更新回调，持续检查参数变化
            EditorApplication.update += OnEditorUpdate;
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

            // 使用默认材质界面的提示
            if (shaderImage.TargetShader != null)
            {
                EditorGUILayout.HelpBox("使用默认材质界面编辑参数：选择GameObject，在Inspector底部点击\"Open Material Editor\"按钮或展开\"Material\"折叠面板", MessageType.Info);
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
        private void CheckMaterialPropertiesChanges()
        {
            if (shaderImage.MaterialInstance == null) return;

            // 计算材质参数的哈希值
            int currentHash = CalculateMaterialPropertiesHash(shaderImage.MaterialInstance);

            // 如果哈希值变化，说明材质参数被修改
            if (currentHash != lastMaterialHash)
            {
                lastMaterialHash = currentHash;
                
                // 触发ShaderImage组件更新材质参数列表
                shaderImage.UpdateMaterialPropertiesList();
                
                // 标记组件为脏，确保参数变化被保存
                EditorUtility.SetDirty(shaderImage);
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
            CheckMaterialPropertiesChanges();
        }

        // 播放模式变化监听
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 在进入播放模式前，保存当前参数
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (shaderImage.MaterialInstance != null)
                {
                    shaderImage.UpdateMaterialPropertiesList();
                    // 强制保存组件的序列化数据
                    EditorUtility.SetDirty(shaderImage);
                    AssetDatabase.SaveAssets();
                }
            }
            // 在退出播放模式后，恢复保存的参数
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (shaderImage.MaterialInstance != null)
                {
                    shaderImage.ApplyMaterialProperties();
                }
            }
        }
    }
}
