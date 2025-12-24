using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 负责将 RectTransform 的尺寸信息写入 Mesh 的 UV1 通道
/// 配合 Data-Driven 的 Shader 使用
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Graphic))] // 需要 Image 或 RawImage
public class UI_RectSizeToShader : MonoBehaviour, IMeshModifier
{
    private RectTransform _rectTransform;
    private Graphic _graphic;

    private void OnEnable()
    {
        // 检查是否挂载了 TextMeshPro (如果你的项目里有 TMP)
        // 这是一个防御性编程的好习惯
        if (GetComponent("TMPro.TMP_Text") != null)
        {
            Debug.LogError("不要将 UIRectSizeToShader 挂在 TextMeshPro 上！TMP 需要占用 UV 通道。");
            this.enabled = false;
            return;
        }

        _rectTransform = GetComponent<RectTransform>();
        _graphic = GetComponent<Graphic>();
        RefreshMesh();
    }

    private void OnRectTransformDimensionsChange()
    {
        // 当 UI 尺寸变化时，标记网格需要重新构建
        RefreshMesh();
    }
    
    // 在编辑器中改变属性时也能刷新
    private void OnValidate()
    {
        RefreshMesh();
    }

    private void RefreshMesh()
    {
        if (_graphic != null)
        {
            _graphic.SetVerticesDirty();
        }
    }

    // IMeshModifier 接口实现 (旧版，通常不用)
    public void ModifyMesh(Mesh mesh) { }

    // IMeshModifier 接口实现 (核心逻辑)
    public void ModifyMesh(VertexHelper vh)
    {
        if (!isActiveAndEnabled || _rectTransform == null) return;

        var rect = _rectTransform.rect;
        
        // 准备数据：x = 半宽, y = 半高
        Vector2 halfSize = new Vector2(rect.width * 0.5f, rect.height * 0.5f);

        List<UIVertex> verts = new List<UIVertex>();
        vh.GetUIVertexStream(verts);

        for (int i = 0; i < verts.Count; i++)
        {
            UIVertex v = verts[i];
            // 将尺寸数据写入 uv1
            v.uv1 = halfSize; 
            verts[i] = v;
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(verts);
    }
}