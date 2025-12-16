using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;


namespace Museum.Component.UGUI
{
    [RequireComponent(typeof(Image))]
    public class ShaderImage : MonoBehaviour, ISerializationCallbackReceiver
    {

        [SerializeField]
        private Shader targetShader;

        [SerializeField]
        private Image image;

        // 直接显示所有材质参数，不进行过滤
        [System.Serializable]
        public class FloatProperty
        {
            public string propertyName;
            public float value;
        }

        [System.Serializable]
        public class ColorProperty
        {
            public string propertyName;
            public Color value;
        }

        [System.Serializable]
        public class VectorProperty
        {
            public string propertyName;
            public Vector4 value;
        }

        [System.Serializable]
        public class TextureProperty
        {
            public string propertyName;
            public Texture value;
        }

        [SerializeField]
        private List<FloatProperty> floatProperties = new List<FloatProperty>();

        [SerializeField]
        private List<ColorProperty> colorProperties = new List<ColorProperty>();

        [SerializeField]
        private List<VectorProperty> vectorProperties = new List<VectorProperty>();

        [SerializeField]
        private List<TextureProperty> textureProperties = new List<TextureProperty>();

        // 标记为修改的参数名列表（用户显式标记的）
        [SerializeField]
        private List<string> serializedMarkedProperties = new List<string>();

        [SerializeField, HideInInspector]
        private string persistentId;

        private HashSet<string> markedModifiedProperties = new HashSet<string>();

        private Material materialInstance;


        public void OnBeforeSerialize()
        {
            EnsurePersistentId();
            SyncSerializedMarksFromHash();
        }

        public void OnAfterDeserialize()
        {
            markedModifiedProperties = serializedMarkedProperties != null
                ? new HashSet<string>(serializedMarkedProperties)
                : new HashSet<string>();

            EnsurePersistentId();
        }


        private void EnsurePersistentId()
        {
            if (string.IsNullOrEmpty(persistentId))
            {
                persistentId = Guid.NewGuid().ToString("N");
            }
        }

        private void EnsureMarkedPropertiesCache()
        {
            if (serializedMarkedProperties == null)
            {
                serializedMarkedProperties = new List<string>();
            }

            if (markedModifiedProperties == null)
            {
                markedModifiedProperties = new HashSet<string>(serializedMarkedProperties);
            }
        }

        private void SyncSerializedMarksFromHash()
        {
            EnsureMarkedPropertiesCache();
            serializedMarkedProperties.Clear();
            serializedMarkedProperties.AddRange(markedModifiedProperties);
        }


        void OnValidate()
        {
            EnsureMarkedPropertiesCache();
            EnsurePersistentId();

            if (image == null)
            {
                image = GetComponent<Image>();
            }

            UpdateShader();
        }

        void Awake()
        {
            EnsureMarkedPropertiesCache();
            EnsurePersistentId();

            if (image == null)
            {
                image = GetComponent<Image>();
            }

            // 在游戏启动时，确保材质实例使用最新保存的参数
            UpdateShader();
        }



        void OnEnable()
        {
            EnsureMarkedPropertiesCache();
            EnsurePersistentId();

            if (materialInstance != null)
            {
                image.material = materialInstance;
            }
        }



        void OnDestroy()
        {
            if (materialInstance != null)
            {
                // 在销毁材质实例前，更新参数列表以保存最新的参数值
                UpdateMaterialPropertiesList();
                
                if (Application.isPlaying)
                {
                    Destroy(materialInstance);
                }
                else
                {
                    DestroyImmediate(materialInstance);
                }
            }
        }

        public void UpdateShader()
        {
            EnsureMarkedPropertiesCache();

            if (targetShader == null || image == null)
            {
                return;
            }


            if (materialInstance == null || materialInstance.shader != targetShader)
            {
                if (materialInstance != null)
                {
                    // 在替换材质实例前，保存当前参数
                    UpdateMaterialPropertiesList();

                    if (Application.isPlaying)
                    {
                        Destroy(materialInstance);
                    }
                    else
                    {
                        DestroyImmediate(materialInstance);
                    }
                }

                materialInstance = new Material(targetShader);

                image.material = materialInstance;

                // 新创建材质实例时，应用已保存的参数
                ApplyMaterialProperties();
            }
            else
            {
                // 材质实例存在且Shader相同，确保参数正确应用
                ApplyMaterialProperties();
            }
        }

        // 获取Shader参数的默认值
        private Dictionary<string, object> GetDefaultPropertyValues()
        {
            Dictionary<string, object> defaultValues = new Dictionary<string, object>();

            if (targetShader == null) return defaultValues;

            // 创建一个临时的Material来获取默认值
            Material tempMaterial = new Material(targetShader);

            int propertyCount = targetShader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = targetShader.GetPropertyName(i);
                ShaderPropertyType propertyType = targetShader.GetPropertyType(i);

                if (tempMaterial.HasProperty(propertyName))
                {
                    switch (propertyType)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            defaultValues[propertyName] = tempMaterial.GetFloat(propertyName);
                            break;
                        case ShaderPropertyType.Color:
                            defaultValues[propertyName] = tempMaterial.GetColor(propertyName);
                            break;
                        case ShaderPropertyType.Vector:
                            defaultValues[propertyName] = tempMaterial.GetVector(propertyName);
                            break;
                        case ShaderPropertyType.Texture:
                            defaultValues[propertyName] = tempMaterial.GetTexture(propertyName);
                            break;
                    }
                }
            }

            // 清理临时材质
            if (Application.isPlaying)
            {
                Destroy(tempMaterial);
            }
            else
            {
                DestroyImmediate(tempMaterial);
            }

            return defaultValues;
        }

        public void UpdateMaterialPropertiesList()
        {
            EnsureMarkedPropertiesCache();

            if (materialInstance == null)
            {
                return;
            }


            floatProperties.Clear();
            colorProperties.Clear();
            vectorProperties.Clear();
            textureProperties.Clear();

            // 只保存标记为修改的参数
            foreach (string propertyName in markedModifiedProperties)
            {
                if (materialInstance.HasProperty(propertyName))
                {
                    ShaderPropertyType propertyType = GetPropertyType(propertyName);

                    switch (propertyType)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            floatProperties.Add(new FloatProperty { propertyName = propertyName, value = materialInstance.GetFloat(propertyName) });
                            break;
                        case ShaderPropertyType.Color:
                            colorProperties.Add(new ColorProperty { propertyName = propertyName, value = materialInstance.GetColor(propertyName) });
                            break;
                        case ShaderPropertyType.Vector:
                            vectorProperties.Add(new VectorProperty { propertyName = propertyName, value = materialInstance.GetVector(propertyName) });
                            break;
                        case ShaderPropertyType.Texture:
                            textureProperties.Add(new TextureProperty { propertyName = propertyName, value = materialInstance.GetTexture(propertyName) });
                            break;
                    }
                }
            }
        }

        // 获取属性类型
        private ShaderPropertyType GetPropertyType(string propertyName)
        {
            if (targetShader == null) return ShaderPropertyType.Float;

            int propertyCount = targetShader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                if (targetShader.GetPropertyName(i) == propertyName)
                {
                    return targetShader.GetPropertyType(i);
                }
            }

            return ShaderPropertyType.Float;
        }

        // 标记参数为修改状态
        public void MarkPropertyAsModified(string propertyName)
        {
            EnsureMarkedPropertiesCache();

            if (markedModifiedProperties.Add(propertyName))
            {
                UpdateMaterialPropertiesList();
                SyncSerializedMarksFromHash();
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }


        // 取消标记参数为修改状态并恢复默认值
        public void UnmarkPropertyAsModified(string propertyName)
        {
            EnsureMarkedPropertiesCache();

            if (!markedModifiedProperties.Contains(propertyName))
            {
                return;
            }

            markedModifiedProperties.Remove(propertyName);
            SyncSerializedMarksFromHash();

            // 恢复默认值
            if (materialInstance != null && materialInstance.HasProperty(propertyName))
            {
                Dictionary<string, object> defaultValues = GetDefaultPropertyValues();
                if (defaultValues.ContainsKey(propertyName))
                {
                    ShaderPropertyType propertyType = GetPropertyType(propertyName);

                    switch (propertyType)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            materialInstance.SetFloat(propertyName, (float)defaultValues[propertyName]);
                            break;
                        case ShaderPropertyType.Color:
                            materialInstance.SetColor(propertyName, (Color)defaultValues[propertyName]);
                            break;
                        case ShaderPropertyType.Vector:
                            materialInstance.SetVector(propertyName, (Vector4)defaultValues[propertyName]);
                            break;
                        case ShaderPropertyType.Texture:
                            materialInstance.SetTexture(propertyName, (Texture)defaultValues[propertyName]);
                            break;
                    }

                    ((Image)GetComponent(typeof(Image))).SetMaterialDirty();
                }
            }

            UpdateMaterialPropertiesList();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }


        // 检查参数是否被标记为修改
        public bool IsPropertyMarkedModified(string propertyName)
        {
            EnsureMarkedPropertiesCache();
            return markedModifiedProperties.Contains(propertyName);
        }


        // 检查当前材质参数是否与保存的参数同步
        public bool AreMaterialPropertiesInSync()
        {
            EnsureMarkedPropertiesCache();

            if (materialInstance == null) return true;


            // 获取所有Shader参数
            var allProperties = GetAllShaderProperties();

            // 检查是否有任何参数与默认值不同但未被标记为修改
            foreach (var property in allProperties)
            {
                bool isCurrentlyModified = IsPropertyModified(property.name, property.type);
                bool isMarkedModified = IsPropertyMarkedModified(property.name);

            // 检查未标记但修改的参数（在编辑器和运行时都有可能）
            if (isCurrentlyModified && !isMarkedModified)
            {
                return false;
            }

                // 如果参数被标记为修改，检查当前值是否与保存值匹配
                if (isMarkedModified)
                {
                    bool foundInSaved = false;

                    switch (property.type)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            float savedFloat = floatProperties.Find(p => p.propertyName == property.name)?.value ?? 0f;
                            float currentFloat = materialInstance.GetFloat(property.name);
                            if (Mathf.Approximately(savedFloat, currentFloat))
                                foundInSaved = true;
                            break;
                        case ShaderPropertyType.Color:
                            Color savedColor = colorProperties.Find(p => p.propertyName == property.name)?.value ?? Color.white;
                            Color currentColor = materialInstance.GetColor(property.name);
                            if (savedColor == currentColor)
                                foundInSaved = true;
                            break;
                        case ShaderPropertyType.Vector:
                            Vector4 savedVector = vectorProperties.Find(p => p.propertyName == property.name)?.value ?? Vector4.zero;
                            Vector4 currentVector = materialInstance.GetVector(property.name);
                            if (savedVector == currentVector)
                                foundInSaved = true;
                            break;
                        case ShaderPropertyType.Texture:
                            Texture savedTexture = textureProperties.Find(p => p.propertyName == property.name)?.value;
                            Texture currentTexture = materialInstance.GetTexture(property.name);
                            if (savedTexture == currentTexture)
                                foundInSaved = true;
                            break;
                    }

                    if (!foundInSaved)
                        return false; // 有参数不同步
                }
            }

            return true; // 所有参数都同步
        }

        // 强制保存当前所有参数（用于运行时保存）
        public void ForceSaveAllCurrentParameters()
        {
            EnsureMarkedPropertiesCache();

            if (materialInstance == null) return;

            // 标记所有当前非默认值的参数为修改状态
            var allProperties = GetAllShaderProperties();
            foreach (var property in allProperties)
            {
                if (IsPropertyModified(property.name, property.type))
                {
                    markedModifiedProperties.Add(property.name);
                }
            }

            SyncSerializedMarksFromHash();

            // 保存所有标记的参数
            UpdateMaterialPropertiesList();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            // 在运行时保存时，立即保存资源以确保数据持久化
            if (Application.isPlaying)
            {
                UnityEditor.AssetDatabase.SaveAssets();
            }
#endif
        }


        // 获取所有Shader参数的信息（用于Editor显示）
        public List<(string name, ShaderPropertyType type, object defaultValue, object currentValue)> GetAllShaderProperties()
        {
            List<(string, ShaderPropertyType, object, object)> properties = new List<(string, ShaderPropertyType, object, object)>();

            if (targetShader == null) return properties;

            Dictionary<string, object> defaultValues = GetDefaultPropertyValues();

            int propertyCount = targetShader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = targetShader.GetPropertyName(i);
                ShaderPropertyType propertyType = targetShader.GetPropertyType(i);

                object defaultValue = defaultValues.ContainsKey(propertyName) ? defaultValues[propertyName] : null;
                object currentValue = null;

                if (materialInstance != null && materialInstance.HasProperty(propertyName))
                {
                    switch (propertyType)
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            currentValue = materialInstance.GetFloat(propertyName);
                            break;
                        case ShaderPropertyType.Color:
                            currentValue = materialInstance.GetColor(propertyName);
                            break;
                        case ShaderPropertyType.Vector:
                            currentValue = materialInstance.GetVector(propertyName);
                            break;
                        case ShaderPropertyType.Texture:
                            currentValue = materialInstance.GetTexture(propertyName);
                            break;
                    }
                }

                properties.Add((propertyName, propertyType, defaultValue, currentValue));
            }

            return properties;
        }

        // 检查参数是否被用户修改过
        public bool IsPropertyModified(string propertyName, ShaderPropertyType propertyType)
        {
            if (targetShader == null) return false;

            Dictionary<string, object> defaultValues = GetDefaultPropertyValues();
            if (!defaultValues.ContainsKey(propertyName)) return false;

            if (materialInstance == null || !materialInstance.HasProperty(propertyName)) return false;

            switch (propertyType)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return materialInstance.GetFloat(propertyName) != (float)defaultValues[propertyName];
                case ShaderPropertyType.Color:
                    return materialInstance.GetColor(propertyName) != (Color)defaultValues[propertyName];
                case ShaderPropertyType.Vector:
                    return materialInstance.GetVector(propertyName) != (Vector4)defaultValues[propertyName];
                case ShaderPropertyType.Texture:
                    return materialInstance.GetTexture(propertyName) != (Texture)defaultValues[propertyName];
                default:
                    return false;
            }
        }

        public void ApplyMaterialProperties()
        {
            if (materialInstance == null)
            {
                return;
            }

            bool hasChanged = false;

            foreach (FloatProperty property in floatProperties)
            {
                float currentValue = materialInstance.GetFloat(property.propertyName);
                if (currentValue != property.value)
                {
                    materialInstance.SetFloat(property.propertyName, property.value);
                    hasChanged = true;
                }
            }

            foreach (ColorProperty property in colorProperties)
            {
                Color currentValue = materialInstance.GetColor(property.propertyName);
                if (currentValue != property.value)
                {
                    materialInstance.SetColor(property.propertyName, property.value);
                    hasChanged = true;
                }
            }

            foreach (VectorProperty property in vectorProperties)
            {
                Vector4 currentValue = materialInstance.GetVector(property.propertyName);
                if (currentValue != property.value)
                {
                    materialInstance.SetVector(property.propertyName, property.value);
                    hasChanged = true;
                }
            }

            foreach (TextureProperty property in textureProperties)
            {
                Texture currentValue = materialInstance.GetTexture(property.propertyName);
                if (currentValue != property.value)
                {
                    materialInstance.SetTexture(property.propertyName, property.value);
                    hasChanged = true;
                }
            }

            if (hasChanged)
            {
                image.SetMaterialDirty();
            }
        }

        // 运行时参数修改通知（用于更新保存状态）
        public void NotifyRuntimeParameterChanged(string propertyName)
        {
            // 在运行时参数被修改时调用此方法来更新状态
            // 这个方法会在Editor中被调用来更新UI状态
        }

        public string PersistentId
        {
            get
            {
                EnsurePersistentId();
                return persistentId;
            }
        }

        public Shader TargetShader
        {
            get { return targetShader; }
            set
            {
                if (targetShader != value)

                {
                    // Shader改变时，清空之前保存的参数和标记
                    floatProperties.Clear();
                    colorProperties.Clear();
                    vectorProperties.Clear();
                    textureProperties.Clear();
                    markedModifiedProperties.Clear();
                    serializedMarkedProperties.Clear();
                }

                targetShader = value;
                UpdateShader();
            }
        }


        public Material MaterialInstance
        {
            get { return materialInstance; }
        }

        // 公共属性用于Editor访问
        public List<FloatProperty> FloatProperties => floatProperties;
        public List<ColorProperty> ColorProperties => colorProperties;
        public List<VectorProperty> VectorProperties => vectorProperties;
        public List<TextureProperty> TextureProperties => textureProperties;
        public int MarkedModifiedPropertiesCount
        {
            get
            {
                EnsureMarkedPropertiesCache();
                return markedModifiedProperties.Count;
            }
        }

#if UNITY_EDITOR
        public IReadOnlyList<string> GetMarkedPropertyNamesSnapshot()
        {
            EnsureMarkedPropertiesCache();
            return serializedMarkedProperties;
        }

        public void OverrideSavedParameters(
            Shader newTargetShader,
            IEnumerable<FloatProperty> newFloatProperties,
            IEnumerable<ColorProperty> newColorProperties,
            IEnumerable<VectorProperty> newVectorProperties,
            IEnumerable<TextureProperty> newTextureProperties,
            IEnumerable<string> markedNames)
        {

            EnsureMarkedPropertiesCache();
            targetShader = newTargetShader;

            floatProperties = CloneFloatProperties(newFloatProperties);
            colorProperties = CloneColorProperties(newColorProperties);
            vectorProperties = CloneVectorProperties(newVectorProperties);
            textureProperties = CloneTextureProperties(newTextureProperties);

            serializedMarkedProperties.Clear();
            markedModifiedProperties.Clear();

            if (markedNames != null)
            {
                foreach (var name in markedNames)
                {
                    if (string.IsNullOrEmpty(name) || markedModifiedProperties.Contains(name))
                    {
                        continue;
                    }

                    markedModifiedProperties.Add(name);
                    serializedMarkedProperties.Add(name);
                }
            }

            UpdateShader();
            UpdateMaterialPropertiesList();
            ApplyMaterialProperties();
        }

        private static List<FloatProperty> CloneFloatProperties(IEnumerable<FloatProperty> source)
        {
            List<FloatProperty> result = new List<FloatProperty>();
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

                result.Add(new FloatProperty
                {
                    propertyName = property.propertyName,
                    value = property.value
                });
            }

            return result;
        }

        private static List<ColorProperty> CloneColorProperties(IEnumerable<ColorProperty> source)
        {
            List<ColorProperty> result = new List<ColorProperty>();
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

                result.Add(new ColorProperty
                {
                    propertyName = property.propertyName,
                    value = property.value
                });
            }

            return result;
        }

        private static List<VectorProperty> CloneVectorProperties(IEnumerable<VectorProperty> source)
        {
            List<VectorProperty> result = new List<VectorProperty>();
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

                result.Add(new VectorProperty
                {
                    propertyName = property.propertyName,
                    value = property.value
                });
            }

            return result;
        }

        private static List<TextureProperty> CloneTextureProperties(IEnumerable<TextureProperty> source)
        {
            List<TextureProperty> result = new List<TextureProperty>();
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

                result.Add(new TextureProperty
                {
                    propertyName = property.propertyName,
                    value = property.value
                });
            }

            return result;
        }
#endif
    }
}

