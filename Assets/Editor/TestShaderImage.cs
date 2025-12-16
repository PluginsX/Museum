using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Museum.Component.UGUI;

public class TestShaderImage
{
    [MenuItem("Tools/Test ShaderImage")]
    public static void Test()
    {
        // 创建一个新的GameObject
        GameObject testObject = new GameObject("TestShaderImage");
        
        // 添加Image组件
        Image image = testObject.AddComponent<Image>();
        
        // 添加ShaderImage组件
        ShaderImage shaderImage = testObject.AddComponent<ShaderImage>();
        
        // 查找默认的UI Shader
        Shader uiShader = Shader.Find("UI/Default");
        
        if (uiShader != null)
        {
            // 设置Shader
            shaderImage.TargetShader = uiShader;
            
            // 打印材质属性
            Debug.Log("测试完成：ShaderImage组件已创建并配置");
            Debug.Log($"材质实例：{shaderImage.MaterialInstance}");
            Debug.Log($"材质Shader：{shaderImage.MaterialInstance.shader.name}");
        }
        else
        {
            Debug.LogError("找不到UI/Default Shader");
        }
    }
}