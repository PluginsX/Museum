using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Graphic))]
[ExecuteInEditMode]
public class UIRoundedCorners : MonoBehaviour
{
    [Header("圆角半径")]
    [SerializeField, Range(0, 500)] 
    private float _topLeftRadius = 40f;
    [SerializeField, Range(0, 500)] 
    private float _topRightRadius = 40f;
    [SerializeField, Range(0, 500)] 
    private float _bottomLeftRadius = 40f;
    [SerializeField, Range(0, 500)] 
    private float _bottomRightRadius = 40f;

    [Header("边框设置")]
    [SerializeField, Range(0, 100)] 
    private float _borderWidth = 5f;
    [SerializeField, ColorUsage(true, true)] 
    private Color _borderColor = Color.white;

    [Header("Shader设置")]
    [SerializeField] 
    Shader shader;

    private Material _material;
    private Graphic _graphic;
    private RectTransform _rectTransform;

    private static readonly int Radius_TL = Shader.PropertyToID("_Radius_TL");
    private static readonly int Radius_TR = Shader.PropertyToID("_Radius_TR");
    private static readonly int Radius_BL = Shader.PropertyToID("_Radius_BL");
    private static readonly int Radius_BR = Shader.PropertyToID("_Radius_BR");
    private static readonly int BorderWidth = Shader.PropertyToID("_BorderWidth");
    private static readonly int BorderColor = Shader.PropertyToID("_BorderColor");

    private void OnEnable()
    {
        InitializeComponents();
        CreateMaterial();
        UpdateMaterialProperties();
    }

    private void OnDisable()
    {
        SafeDestroyMaterial();
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdateMaterialProperties();
    }

    private void InitializeComponents()
    {
        _graphic = GetComponent<Graphic>();
        _rectTransform = GetComponent<RectTransform>();
    }

    private void CreateMaterial()
    {
        if (_material == null)
        {
            if (shader==null){
                Debug.Log("未指定Shader,默认采用 UI/RoundedCorners Shader");
                var shader = Shader.Find("UI/RoundedCorners");
                if (shader == null)
                {
                    Debug.LogError("找不到UI/RoundedCorners Shader，请确保Shader已正确导入");
                    return;
                }
            }
            

            _material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _graphic.material = _material;
        }
    }

    private void SafeDestroyMaterial()
    {
        if (_material != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_material);
            }
            else
            {
                DestroyImmediate(_material);
            }
            _material = null;
        }
    }

    private void UpdateMaterialProperties()
    {
        if (!isActiveAndEnabled || _material == null || _rectTransform == null) 
            return;

        var rect = _rectTransform.rect;
        Vector4 radiuses = new Vector4(
            Mathf.Max(0, _topLeftRadius),
            Mathf.Max(0, _topRightRadius),
            Mathf.Max(0, _bottomRightRadius),
            Mathf.Max(0, _bottomLeftRadius)
        );

        
        _material.SetFloat(Radius_TL, Mathf.Max(0, _topLeftRadius));
        _material.SetFloat(Radius_TR, Mathf.Max(0, _topRightRadius));
        _material.SetFloat(Radius_BL, Mathf.Max(0, _bottomLeftRadius));
        _material.SetFloat(Radius_BR, Mathf.Max(0, _bottomRightRadius));
        _material.SetFloat(BorderWidth, Mathf.Max(0, _borderWidth));
        _material.SetColor(BorderColor, _borderColor);

        _graphic.SetMaterialDirty();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!isActiveAndEnabled) 
            return;

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) 
                return;

            InitializeComponents();
            CreateMaterial();
            UpdateMaterialProperties();
        };
    }
#endif
}