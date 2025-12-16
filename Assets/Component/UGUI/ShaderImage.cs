using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace Museum.Component.UGUI
{
    [RequireComponent(typeof(Image))]
    public class ShaderImage : MonoBehaviour
    {
        [SerializeField]
        private Shader targetShader;

        [SerializeField]
        private Image image;

        // 直接显示所有材质参数，不进行过滤
        [System.Serializable]
        private class FloatProperty
        {
            public string propertyName;
            public float value;
        }

        [System.Serializable]
        private class ColorProperty
        {
            public string propertyName;
            public Color value;
        }

        [System.Serializable]
        private class VectorProperty
        {
            public string propertyName;
            public Vector4 value;
        }

        [System.Serializable]
        private class TextureProperty
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

        private Material materialInstance;

        void OnValidate()
        {
            if (image == null)
            {
                image = GetComponent<Image>();
            }

            UpdateShader();
        }

        void Awake()
        {
            if (image == null)
            {
                image = GetComponent<Image>();
            }

            // 在游戏启动时，确保材质实例使用最新保存的参数
            UpdateShader();
        }

        void OnEnable()
        {
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

                // 如果是首次创建材质或Shader改变，更新参数列表
                if (floatProperties.Count == 0 && colorProperties.Count == 0 && 
                    vectorProperties.Count == 0 && textureProperties.Count == 0)
                {
                    UpdateMaterialPropertiesList();
                }
                else
                {
                    // 否则直接应用已保存的参数
                    ApplyMaterialProperties();
                }
            }
            else
            {
                // 材质实例存在且Shader相同，确保参数正确应用
                ApplyMaterialProperties();
            }
        }

        public void UpdateMaterialPropertiesList()
        {
            if (materialInstance == null)
            {
                return;
            }

            floatProperties.Clear();
            colorProperties.Clear();
            vectorProperties.Clear();
            textureProperties.Clear();

            // 显示所有参数，直接分类处理
            int propertyCount = targetShader.GetPropertyCount();

            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = targetShader.GetPropertyName(i);
                ShaderPropertyType propertyType = targetShader.GetPropertyType(i);

                // 根据真实属性类型添加到对应列表
                if (materialInstance.HasProperty(propertyName))
                {
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

        public Shader TargetShader
        {
            get { return targetShader; }
            set
            {
                targetShader = value;
                UpdateShader();
            }
        }

        public Material MaterialInstance
        {
            get { return materialInstance; }
        }
    }
}
